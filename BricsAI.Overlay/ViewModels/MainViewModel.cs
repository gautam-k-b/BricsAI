using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using BricsAI.Overlay.Models;
using BricsAI.Overlay.Services.Agents;

namespace BricsAI.Overlay.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<ChatMessage> Messages { get; set; } = new ObservableCollection<ChatMessage>();

        private string _inputText = string.Empty;
        public string InputText
        {
            get => _inputText;
            set
            {
                _inputText = value;
                OnPropertyChanged();
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
            }
        }

        public bool IsNotBusy => !IsBusy;

        public ICommand SendCommand { get; }
        
        // Quick Actions Dashboard Commands
        public ICommand RunProofingCommand { get; }
        public ICommand CleanGeometryCommand { get; }
        public ICommand GenerateSummaryCommand { get; }

        private readonly Services.ComClient _comClient; // Replaced PipeClient
        private readonly SurveyorAgent _surveyor;
        private readonly ExecutorAgent _executor;
        private readonly ValidatorAgent _validator;

        public MainViewModel()
        {
            _comClient = new Services.ComClient();
            _surveyor = new SurveyorAgent();
            _executor = new ExecutorAgent();
            _validator = new ValidatorAgent();

            SendCommand = new RelayCommand(async _ => await SendMessageAsync());
            RunProofingCommand = new RelayCommand(async _ => await ExecuteQuickAction("Please proof this drawing for an exhibition context. Follow the standard A2Z layering, exploding, and layout rules."));
            CleanGeometryCommand = new RelayCommand(async _ => await ExecuteQuickAction("Clean up the drawing geometry. Delete floating layers, standard garbage layers (like dim/freeze), and run PURGE on everything."));
            GenerateSummaryCommand = new RelayCommand(async _ => await ExecuteQuickAction("I don't need macros run. Please just look at the Surveyor data and generate a Bill of Materials / Audit Summary for this layout."));
            
            // Initial greeting
            Messages.Add(new ChatMessage { Role = "Assistant", Content = "Hello! I am your BricsCAD AI Agent. connecting via COM Automation... (No NETLOAD needed)" });
        }

        private async Task ExecuteQuickAction(string overridePrompt)
        {
            if (IsBusy) return;
            string originalInput = InputText;
            InputText = overridePrompt;
            await SendMessageAsync();
            InputText = originalInput; // Restore whatever they were typing
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(InputText)) return;

            var userMessage = InputText;
            InputText = ""; // Clear input immediately
            
            Messages.Add(new ChatMessage { Role = "User", Content = userMessage });

            IsBusy = true;
            
            // 0. Ensure connected to get the version
            if (!_comClient.IsConnected)
            {
                await _comClient.ConnectAsync();
            }

            string layerMappings = "";
            try
            {
                string mappingPath = Path.Combine(System.AppContext.BaseDirectory, "layer_mappings.json");
                if (File.Exists(mappingPath))
                    layerMappings = File.ReadAllText(mappingPath);
            }
            catch { }

            // Pass 1: Survey Layers (Two-Pass Logic)
            string currentLayers = "";
            if (_comClient.IsConnected)
            {
                try
                {
                    string getLayersCmd = @"{ ""tool_calls"": [{ ""command_name"": ""NET_GET_LAYERS"", ""lisp_code"": ""NET:GET_LAYERS:"" }] }";
                    currentLayers = await Task.Run(() => _comClient.ExecuteActionAsync(getLayersCmd));
                }
                catch { }
            }

            // --- MULTI-AGENT ORCHESTRATION START ---
            int totalTokens = 0;
            var stopwatch = Stopwatch.StartNew();

            // Agent 1: Surveyor
            var surveyorMsg = new ChatMessage { Role = "Assistant", Content = "👷‍♂️ Surveyor Agent: Putting on my hard hat and inspecting the raw drawing layers...", IsThinking = true };
            Messages.Add(surveyorMsg);
            var surveyorResult = await Task.Run(() => _surveyor.AnalyzeDrawingStateAsync(userMessage, currentLayers, layerMappings));
            surveyorMsg.IsThinking = false;
            string surveyorSummary = surveyorResult.Summary;
            totalTokens += surveyorResult.Tokens;
            Messages.Add(new ChatMessage { Role = "Assistant", Content = $"📋 Surveyor Report:\n{surveyorSummary}" });

            int maxRetries = 2;
            int attempt = 0;
            bool success = false;
            string feedback = "";

            while (attempt < maxRetries && !success)
            {
                attempt++;
                string executorContext = attempt == 1 ? surveyorSummary : surveyorSummary + $"\n\nVALIDATOR FEEDBACK FROM PREVIOUS ATTEMPT:\n{feedback}";
                
                // Agent 2: Executor
                var executorMsg = new ChatMessage { Role = "Assistant", Content = $"⚙️ Executor Agent: Drafting the master execution plan to restructure your booths! (Attempt {attempt})...", IsThinking = true };
                Messages.Add(executorMsg);
                var executorResult = await Task.Run(() => _executor.GenerateMacrosAsync(userMessage, executorContext, _comClient.MajorVersion, layerMappings));
                executorMsg.IsThinking = false;
                string actionPlanJson = executorResult.ActionPlan;
                totalTokens += executorResult.Tokens;
                
                // Execute against COM
                var cadMsg = new ChatMessage { Role = "Assistant", Content = $"🚀 BricsCAD: Hijacking your mouse to execute native tools...", IsThinking = true };
                Messages.Add(cadMsg);

                var progress = new System.Progress<string>(update =>
                {
                    cadMsg.Content += $"\n{update}";
                });

                string executionLogs = await Task.Run(() => _comClient.ExecuteActionAsync(actionPlanJson, progress));
                cadMsg.IsThinking = false;

                // DUMP TO DISK FOR DEBUGGING
                File.WriteAllText("AI_Context.txt", executorContext);
                File.WriteAllText("AI_RawActionPlan.json", actionPlanJson);
                File.WriteAllText("AI_ExecutionLogs.txt", executionLogs);

                // Agent 3: Validator
                var validatorMsg = new ChatMessage { Role = "Assistant", Content = "🔍 Validator Agent: Grabbing my magnifying glass to check BricsCAD's work...", IsThinking = true };
                Messages.Add(validatorMsg);
                var validationResult = await Task.Run(() => _validator.ValidateExecutionAsync(userMessage, executionLogs));
                validatorMsg.IsThinking = false;
                
                success = validationResult.success;
                feedback = validationResult.feedback;
                totalTokens += validationResult.tokens;

                if (success)
                {
                    Messages.Add(new ChatMessage { Role = "Assistant", Content = $"✅ Validation Passed: The blueprints look pristine! ({feedback})" });
                }
                else
                {
                    Messages.Add(new ChatMessage { Role = "Assistant", Content = $"❌ Validation Failed: Hmm, something mathematically doesn't add up... ({feedback})" });
                }
            }

            if (!success)
            {
                Messages.Add(new ChatMessage { Role = "Assistant", Content = "⚠️ System: Multi-Agent flow exhausted retries. Please refine your layer mappings or manually intervene." });
            }

            stopwatch.Stop();
            double seconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 1);
            Messages.Add(new ChatMessage { Role = "Assistant", Content = $"📊 Performance: {totalTokens} API tokens consumed. Task completed in {seconds} seconds." });

            IsBusy = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly System.Func<object?, Task> _execute;
        private readonly System.Predicate<object?>? _canExecute;

        public RelayCommand(System.Func<object?, Task> execute, System.Predicate<object?>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);
        public event System.EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}

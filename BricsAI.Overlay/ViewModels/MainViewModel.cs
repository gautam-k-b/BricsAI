using System.Collections.ObjectModel;
using System.ComponentModel;
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
            }
        }

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
                if (File.Exists("layer_mappings.json"))
                    layerMappings = File.ReadAllText("layer_mappings.json");
            }
            catch { }

            // Pass 1: Survey Layers (Two-Pass Logic)
            string currentLayers = "";
            if (_comClient.IsConnected)
            {
                try
                {
                    string getLayersCmd = @"{ ""tool_calls"": [{ ""command_name"": ""NET_GET_LAYERS"", ""lisp_code"": ""NET:GET_LAYERS:"" }] }";
                    currentLayers = await _comClient.ExecuteActionAsync(getLayersCmd);
                }
                catch { }
            }

            // --- MULTI-AGENT ORCHESTRATION START ---

            // Agent 1: Surveyor
            Messages.Add(new ChatMessage { Role = "Assistant", Content = "Surveyor Agent: Gathering drawing context..." });
            string surveyorSummary = await _surveyor.AnalyzeDrawingStateAsync(userMessage, currentLayers, layerMappings);
            Messages.Add(new ChatMessage { Role = "Assistant", Content = $"Surveyor Report:\n{surveyorSummary}" });

            int maxRetries = 2;
            int attempt = 0;
            bool success = false;
            string feedback = "";

            while (attempt < maxRetries && !success)
            {
                attempt++;
                string executorContext = attempt == 1 ? surveyorSummary : surveyorSummary + $"\n\nVALIDATOR FEEDBACK FROM PREVIOUS ATTEMPT:\n{feedback}";
                
                // Agent 2: Executor
                Messages.Add(new ChatMessage { Role = "Assistant", Content = $"Executor Agent: Generating LISP/NET Macros (Attempt {attempt})..." });
                string actionPlanJson = await _executor.GenerateMacrosAsync(userMessage, executorContext, _comClient.MajorVersion, layerMappings);
                
                // Execute against COM
                string executionLogs = await _comClient.ExecuteActionAsync(actionPlanJson);
                Messages.Add(new ChatMessage { Role = "Assistant", Content = $"Execution Logs:\n{executionLogs}" });

                // DUMP TO DISK FOR DEBUGGING
                File.WriteAllText("AI_Context.txt", executorContext);
                File.WriteAllText("AI_RawActionPlan.json", actionPlanJson);
                File.WriteAllText("AI_ExecutionLogs.txt", executionLogs);

                // Agent 3: Validator
                Messages.Add(new ChatMessage { Role = "Assistant", Content = "Validator Agent: Reviewing results..." });
                var validationResult = await _validator.ValidateExecutionAsync(userMessage, executionLogs);
                
                success = validationResult.success;
                feedback = validationResult.feedback;

                if (success)
                {
                    Messages.Add(new ChatMessage { Role = "Assistant", Content = $"✅ Validation Passed: {feedback}" });
                }
                else
                {
                    Messages.Add(new ChatMessage { Role = "Assistant", Content = $"❌ Validation Failed: {feedback}" });
                }
            }

            if (!success)
            {
                Messages.Add(new ChatMessage { Role = "Assistant", Content = "System: Multi-Agent flow exhausted retries. Please refine your prompt or manually intervene." });
            }

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

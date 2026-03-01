using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private readonly MapperAgent _mapper;
        private readonly MappingReviewAgent _mappingReviewAgent;

        private bool _isAwaitingMappingConfirmation = false;
        private string _pendingMappingCommands = "";
        private string _originalProofingCommand = "";

        public MainViewModel()
        {
            _comClient = new Services.ComClient();
            _surveyor = new SurveyorAgent();
            _executor = new ExecutorAgent();
            _validator = new ValidatorAgent();
            _mapper = new MapperAgent();
            _mappingReviewAgent = new MappingReviewAgent();

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

            try
            {
                // --- INTERACTIVE MAPPING REVIEW INTERCEPTION ---
            if (_isAwaitingMappingConfirmation)
            {
                string inputLower = userMessage.ToLower().Trim();
                if (inputLower == "cancel" || inputLower == "stop" || inputLower == "abort" || inputLower == "end")
                {
                    _isAwaitingMappingConfirmation = false;
                    _pendingMappingCommands = "";
                    _originalProofingCommand = "";
                    Messages.Add(new ChatMessage { Role = "Assistant", Content = "🛑 Mapping review cancelled. Dashboard unlocked." });
                    return; // IsBusy is already false here.
                }

                IsBusy = true;
                
                if (inputLower == "yes" || inputLower == "y" || inputLower.StartsWith("yes") || inputLower.StartsWith("looks good") || inputLower.StartsWith("correct") || inputLower.StartsWith("proceed") || inputLower.StartsWith("ok") || inputLower.StartsWith("sure") || inputLower.StartsWith("fine"))
                {
                    var confirmMsg = new ChatMessage { Role = "Assistant", Content = "✅ Saving finalized mappings...", IsThinking = true };
                    Messages.Add(confirmMsg);
                    IProgress<string> confirmProgress = new Progress<string>(update => { confirmMsg.Content += $"\n{update}"; });
                    
                    try
                    {
                        var doc = System.Text.Json.JsonDocument.Parse(_pendingMappingCommands);
                        if (doc.RootElement.GetProperty("tool_calls").GetArrayLength() > 0)
                        {
                            await Task.Run(() => _comClient.ExecuteActionAsync(_pendingMappingCommands, confirmProgress));
                        }
                        else
                        {
                            confirmProgress.Report("No mappings to save.");
                        }
                    }
                    catch
                    {
                        // Ignore malformed JSON instead of crashing
                    }
                    
                    confirmMsg.IsThinking = false;

                    _isAwaitingMappingConfirmation = false;
                    _pendingMappingCommands = "";
                    
                    IsBusy = false; // Unlock so ExecuteQuickAction isn't blocked by its guard
                    
                    // Resume original proofing recursively, but flag it to skip mapping review
                    // to prevent an infinite loop where Surveyor finds the same unmapped layers again.
                    await ExecuteQuickAction(_originalProofingCommand + " _skipMappingReviewSequence_");
                    return;
                }
                
                // Assume it's a correction
                var reviewMsg = new ChatMessage { Role = "Assistant", Content = "🧠 Review Agent: Applying your corrections...", IsThinking = true };
                Messages.Add(reviewMsg);
                
                var reviewResult = await Task.Run(() => _mappingReviewAgent.UpdateMappingsAsync(_pendingMappingCommands, userMessage));
                _pendingMappingCommands = reviewResult.UpdatedMappings;
                
                string formattedUpdatedProps = FormatMappingsForDisplay(_pendingMappingCommands);
                reviewMsg.IsThinking = false;
                reviewMsg.Content = $"♻️ Updated Mappings Proposed:\n\n{formattedUpdatedProps}\n\nDoes this look correct now? (Reply 'yes' to save, 'cancel' to abort, or type more corrections)";
                
                IsBusy = false; // Unlock UI again for further review
                return;
            }

            IsBusy = true;
            
            // 0. Ensure connected to get the version
            if (!_comClient.IsConnected)
            {
                await _comClient.ConnectAsync();
            }

            string layerMappings = "";
            string projRoot = System.IO.Directory.GetParent(System.AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.FullName ?? System.AppContext.BaseDirectory;
            string mappingPath = Path.Combine(projRoot, "layer_mappings.json");
            try
            {
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

            // Agent 1.5: Semantic Layer Auto-Mapper (Intercept Unknowns via C# deterministic parsing)
            var standardA2zLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
            { 
                "0", "Defpoints", "Expo_BoothOutline", "Expo_BoothNumber", "Expo_Building", "Expo_Markings", "Expo_View2", "Expo_Column",
                "Expo_NES", "Expo_MaxBoothOutline", "Expo_MaxBoothNumber"
            };

            // Safely sanitize the COM 'Step' and 'Layers found:' prefix so it doesn't pollute the target logic
            string cleanLayersPayload = currentLayers;
            if (cleanLayersPayload.Contains("Layers found:"))
                cleanLayersPayload = cleanLayersPayload.Substring(cleanLayersPayload.IndexOf("Layers found:") + "Layers found:".Length);

            var unknownLayers = cleanLayersPayload.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l) && !standardA2zLayers.Contains(l))
                .Where(l => 
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(layerMappings)) return true;
                        var existingMappings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(layerMappings);
                        if (existingMappings == null) return true;
                        
                        // 1. Direct exact match check
                        return !existingMappings.ContainsKey(l);
                    }
                    catch
                    {
                        return true;
                    }
                })
                .Distinct()
                .ToList();

            bool skipMappingReview = userMessage.Contains("_skipMappingReviewSequence_");
            string cleanUserMessage = userMessage.Replace("_skipMappingReviewSequence_", "").Trim();
            
            // Heuristic check: only trigger the massive Auto-Mapper loop if the user is 
            // actually asking to proof, map, learn, survey, or evaluate the drawing.
            bool isProofingOrMappingRequest = cleanUserMessage.Contains("proof", StringComparison.OrdinalIgnoreCase) || 
                                              cleanUserMessage.Contains("map", StringComparison.OrdinalIgnoreCase)   ||
                                              cleanUserMessage.Contains("learn", StringComparison.OrdinalIgnoreCase) ||
                                              cleanUserMessage.Contains("survey", StringComparison.OrdinalIgnoreCase) ||
                                              cleanUserMessage.Contains("evaluate", StringComparison.OrdinalIgnoreCase);

            if (unknownLayers.Any() && !skipMappingReview && isProofingOrMappingRequest)
            {
                if (_comClient.IsConnected)
                {
                    // Globally unlock all layers natively before surveying so we securely access all geometric structures
                    await Task.Run(() => _comClient.ForceUnlockAllLayersSynchronously());
                }

                var mapperMsg = new ChatMessage { Role = "Assistant", Content = $"✨ Mapper Agent: Intercepting {unknownLayers.Count} unknown vendor layers. Polling semantics...", IsThinking = true };
                Messages.Add(mapperMsg);

                IProgress<string> mapProgress = new Progress<string>(update => { mapperMsg.Content += $"\n{update}"; });

                var pendingToolCalls = new List<string>();

                foreach (var unknownLayer in unknownLayers)
                {
                    mapProgress.Report($"\n🔎 Polling semantics for '{unknownLayer}'...");
                    string safeLayerName = unknownLayer.Replace("\"", "\\\"").Replace("\\", "\\\\");
                    string footprintPlan = $@"{{ ""tool_calls"": [{{ ""command_name"": ""POLL_SEMANTICS"", ""lisp_code"": ""NET:POLL_LAYER_SEMANTICS:{safeLayerName}"" }}] }}";
                    
                    string footprint = await Task.Run(() => _comClient.ExecuteActionAsync(footprintPlan, mapProgress));
                    
                    if (!footprint.Contains("Error") && footprint.Length > 10)
                    {
                        mapProgress.Report($"🧠 Thinking: Deducing A2Z Mapping...");
                        var mapperResult = await Task.Run(() => _mapper.DeduceLayerMappingAsync(unknownLayer, footprint));
                        totalTokens += mapperResult.Tokens;
                        
                        try 
                        {
                           var doc = System.Text.Json.JsonDocument.Parse(mapperResult.ActionPlan);
                           var calls = doc.RootElement.GetProperty("tool_calls");
                           foreach (var call in calls.EnumerateArray())
                           {
                               pendingToolCalls.Add(call.GetRawText());
                           }
                        } catch { }
                    }
                    else
                    {
                        mapProgress.Report($"⚠️ Layer empty or unreadable. Skipping.");
                    }
                }
                
                mapperMsg.IsThinking = false;

                if (pendingToolCalls.Any())
                {
                    _pendingMappingCommands = "{ \"tool_calls\": [\n" + string.Join(",\n", pendingToolCalls) + "\n] }";
                    _originalProofingCommand = userMessage;
                    _isAwaitingMappingConfirmation = true;
                    
                    string formattedProps = FormatMappingsForDisplay(_pendingMappingCommands);
                    Messages.Add(new ChatMessage { Role = "Assistant", Content = $"🛑 **Human Review Required**\nHere are the proposed layer mappings:\n\n{formattedProps}\n\nDoes this look correct? (Reply 'yes' to proceed, 'cancel' to abort, or provide natural language corrections)" });
                    
                    stopwatch.Stop();
                    double surveySeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 1);
                    Messages.Add(new ChatMessage { Role = "Assistant", Content = $"📊 Performance: {totalTokens} API tokens consumed mapping {unknownLayers.Count} layers. Surveyor completed in {surveySeconds} seconds." });

                    IsBusy = false; // Unlock UI to allow user feedback
                    return; // Halt execution and wait for human response
                }

                // Reload mappings memory bank now that we've dynamically updated it
                try
                {
                    string projRootRefresh = System.IO.Directory.GetParent(System.AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.FullName ?? System.AppContext.BaseDirectory;
                    string mappingPathRefresh = Path.Combine(projRootRefresh, "layer_mappings.json");
                    if (File.Exists(mappingPathRefresh))
                        layerMappings = File.ReadAllText(mappingPathRefresh);
                }
                catch { }
            }

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
                var executorResult = await Task.Run(() => _executor.GenerateMacrosAsync(cleanUserMessage, executorContext, _comClient.MajorVersion, layerMappings));
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
            catch (Exception ex)
            {
                Messages.Add(new ChatMessage { Role = "Assistant", Content = $"❌ A critical system error occurred during orchestration:\n{ex.Message}" });
                IsBusy = false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private string FormatMappingsForDisplay(string jsonMappings)
        {
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(jsonMappings);
                var calls = doc.RootElement.GetProperty("tool_calls");
                var formattedMappings = new List<string>();
                foreach (var call in calls.EnumerateArray())
                {
                    string lispCode = call.GetProperty("lisp_code").GetString() ?? "";
                    if (lispCode.StartsWith("NET:LEARN_LAYER_MAPPING:"))
                    {
                        var parts = lispCode.Substring("NET:LEARN_LAYER_MAPPING:".Length).Split(':');
                        if (parts.Length == 2)
                        {
                            formattedMappings.Add($"• **{parts[0]}**  ➔  **{parts[1]}**");
                        }
                    }
                }
                return string.Join("\n", formattedMappings);
            }
            catch
            {
                return jsonMappings; // Fallback to raw JSON if parse fails
            }
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

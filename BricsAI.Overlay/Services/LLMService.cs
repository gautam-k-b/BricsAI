using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BricsAI.Core;
using System.Linq;
using System.Collections.Generic;
using System.ClientModel;
using Azure.AI.OpenAI;
using OpenAI.Chat;
namespace BricsAI.Overlay.Services
{
    public class LLMService
    {
        private string? _apiKey;
        private string? _model;
        private string? _apiUrl;

        private class AppSettings
        {
            public OpenAISettings? OpenAI { get; set; }
        }

        private class OpenAISettings
        {
            public string? ApiKey { get; set; }
            public string? Model { get; set; }
            public string? ApiUrl { get; set; }
        }

        private ChatClient GetChatClient()
        {
            if (!string.IsNullOrEmpty(_apiUrl) && (_apiUrl.Contains("openai.azure.com") || _apiUrl.Contains("cognitiveservices.azure.com")))
            {
                var uri = new Uri(_apiUrl);
                var baseUri = new Uri($"{uri.Scheme}://{uri.Host}");
                var azureClient = new AzureOpenAIClient(baseUri, new ApiKeyCredential(_apiKey ?? ""));
                return azureClient.GetChatClient(_model ?? "gpt-4o");
            }
            else
            {
                var openAIClient = new OpenAI.OpenAIClient(_apiKey ?? "");
                return openAIClient.GetChatClient(_model ?? "gpt-4o");
            }
        }

        private readonly PluginManager _pluginManager;

        public LLMService()
        {
            _pluginManager = new PluginManager();
            _pluginManager.LoadPlugins();
            LoadConfiguration();
        }

        private void LoadConfiguration()
        {
            try
            {
                var basePath = AppDomain.CurrentDomain.BaseDirectory;
                var settingsPath = Path.Combine(basePath, "appsettings.json");
                
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    _apiKey = settings?.OpenAI?.ApiKey;
                    _model = settings?.OpenAI?.Model ?? "gpt-4o";
                    _apiUrl = settings?.OpenAI?.ApiUrl ?? "https://api.openai.com/v1/chat/completions";
                }
            }
            catch
            {
                // Handle default or error
                _model = "gpt-4o";
                _apiUrl = "https://api.openai.com/v1/chat/completions";
            }
        }

        public async Task<string> GenerateScriptAsync(string userPrompt, int majorVersion, string currentLayers = "")
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "YOUR_API_KEY_HERE")
            {
                return $"(alert \"Please configure your OpenAI API Key in appsettings.json.\")";
            }

            var applicablePlugins = _pluginManager.GetPluginsForVersion(majorVersion).ToList();
            
            var toolsPrompt = string.Join("\n\n", applicablePlugins.Select(p => p.GetPromptExample()));

            var layersContext = string.IsNullOrWhiteSpace(currentLayers) ? "" : $"\nCURRENT DRAWING LAYERS:\n{currentLayers}\nUse these existing layer names when migrating unknown geometry to destination standard layers.\n";

            var systemPrompt = $@"You are an expert BricsCAD automation agent. Your goal is to control BricsCAD V{majorVersion} by outputting structured JSON commands.
                                {layersContext}
                                
                                [USER PREFERENCES & LEARNED RULES]
                                {BricsAI.Core.KnowledgeService.GetLearnings()}

                                YOU MUST OUTPUT ONLY VALID JSON. NO MARKDOWN. NO EXPLANATIONS.

                                CRITICAL RULES:
                                1. NEVER invent custom LISP selection loops (NO sssetfirst, NO vla-getboundingbox). 
                                2. If the user asks to select objects by layer, or specifically inner/outer objects, YOU MUST use the exact `NET:` prefix commands shown in the tools below. The C# host handles the geometry natively.
                                3. ALWAYS prioritize using the provided tool examples. DO NOT hallucinate commands like `_UNSELECT` or nested LISP evaluations for selections.
                                4. MACRO SEQUENCES: You are allowed and encouraged to output massive JSON arrays containing 10+ `tool_calls` to sequentially orchestrate full workflows (e.g., if asked to 'proof' a file).
                                5. PROOFING ORDER OF OPERATIONS: If asked to proof a drawing, you MUST execute exactly this sequence:
                                   A. Explode & Flatten: Run EXPLODE 3-4 times.
                                   B. Layer Standardization: Run the A2ZLAYERS command to create all standard destination layers.
                                   C. Filter Noise: Delete all layers containing 'dim', 'delete', or 'frozen' in their name.
                                   D. Geometric Migration: Use NET: Geometric Classifiers (like NET:SELECT_BOOTH_BOXES) to identify logical elements and move them to standard layers (Expo_BoothOutline, Expo_Building, Expo_Columns).
                                   E. Final Visual Verification: Run A2ZCOLOR command as the VERY LAST step.

                                JSON Schema:
                                {{
                                  ""tool_calls"": [
                                    {{
                                      ""command_name"": ""The primary CAD command or logical name (e.g., 'EXPLODE', 'NET_SELECT_OUTER')"",
                                      ""lisp_code"": ""The actual string to send. (e.g. '(command \""_.CIRCLE\"" ...)' or 'NET:SELECT_OUTER: outlines' or 'NET:MESSAGE: Hello')""
                                    }}
                                  ]
                                }}

                                Basic Example:
                                User: 'Draw a circle at 0,0 with radius 10'
                                Response: {{ ""tool_calls"": [{{ ""command_name"": ""CIRCLE"", ""lisp_code"": ""(command \""_.CIRCLE\"" \""0,0\"" \""10\"")"" }}] }}

                                {toolsPrompt}
                                ";

            var chatClient = GetChatClient();

            try
            {
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userPrompt)
                };

                var options = new ChatCompletionOptions
                {
                    Temperature = 0.1f,
                    ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
                };

                ChatCompletion completion = await chatClient.CompleteChatAsync(messages, options);
                
                var script = completion.Content[0].Text?.Trim();
                
                if (script == null) return string.Empty;

                // Cleanup excessive markdown if model ignores "No Markdown" instruction
                if (script.StartsWith("```json")) script = script.Replace("```json", "").Replace("```", "");
                if (script.StartsWith("```")) script = script.Replace("```", "");

                return script.Trim();
            }
            catch (Exception ex)
            {
                // Fallback valid JSON for error
                return $@"{{ ""tool_calls"": [{{ ""command_name"": ""ALERT"", ""lisp_code"": ""(alert \""LLM Error: {ex.Message}\"")"" }}] }}";
            }
        }
    }
}

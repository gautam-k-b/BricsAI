using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BricsAI.Core;
using System.Linq;

namespace BricsAI.Overlay.Services
{
    public class LLMService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
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

        private class OpenAIRequest
        {
            [JsonPropertyName("model")]
            public string? Model { get; set; }

            [JsonPropertyName("messages")]
            public Message[]? Messages { get; set; }

            [JsonPropertyName("temperature")]
            public double Temperature { get; set; }
            [JsonPropertyName("response_format")]
            public ResponseFormat? ResponseFormat { get; set; }
        }

        private class ResponseFormat
        {
            [JsonPropertyName("type")]
            public string? Type { get; set; }
        }
        private class Message
        {
            [JsonPropertyName("role")]
            public string? Role { get; set; }

            [JsonPropertyName("content")]
            public string? Content { get; set; }
        }

        private class OpenAIResponse
        {
            [JsonPropertyName("choices")]
            public Choice[]? Choices { get; set; }
        }

        private class Choice
        {
            [JsonPropertyName("message")]
            public Message? Message { get; set; }
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

            var requestBody = new OpenAIRequest
            {
                Model = _model,
                Messages = new[]
                {
                    new Message { Role = "system", Content = systemPrompt },
                    new Message { Role = "user", Content = userPrompt }
                },
                Temperature = 0.1,
                ResponseFormat = new ResponseFormat { Type = "json_object" } // Enforce JSON mode if supported by model/proxy
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl ?? "https://api.openai.com/v1/chat/completions")
            {
                Content = content
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);


            try
            {
                // ... (sending request)
                // Return raw JSON for ComClient to parse
                 var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<OpenAIResponse>(responseJson);

                var script = responseData?.Choices?[0]?.Message?.Content?.Trim();
                
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

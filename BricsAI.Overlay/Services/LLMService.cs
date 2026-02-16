using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BricsAI.Overlay.Services
{
    public class LLMService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private string _apiKey;
        private string _model;

        private class AppSettings
        {
            public OpenAISettings OpenAI { get; set; }
        }

        private class OpenAISettings
        {
            public string ApiKey { get; set; }
            public string Model { get; set; }
        }

        private class OpenAIRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }

            [JsonPropertyName("messages")]
            public Message[] Messages { get; set; }

            [JsonPropertyName("temperature")]
            public double Temperature { get; set; }
            [JsonPropertyName("response_format")]
            public ResponseFormat ResponseFormat { get; set; }
        }

        private class ResponseFormat
        {
            [JsonPropertyName("type")]
            public string Type { get; set; }
        }
        private class Message
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }
        }

        private class OpenAIResponse
        {
            [JsonPropertyName("choices")]
            public Choice[] Choices { get; set; }
        }

        private class Choice
        {
            [JsonPropertyName("message")]
            public Message Message { get; set; }
        }

        public LLMService()
        {
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
                }
            }
            catch
            {
                // Handle default or error
                _model = "gpt-4o";
            }
        }

        public async Task<string> GenerateScriptAsync(string userPrompt)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "YOUR_API_KEY_HERE")
            {
                return $"(alert \"Please configure your OpenAI API Key in appsettings.json.\")";
            }

            var systemPrompt = @"You are an expert BricsCAD automation agent. Your goal is to control BricsCAD by outputting structured JSON commands.
                                
                                YOU MUST OUTPUT ONLY VALID JSON. NO MARKDOWN. NO EXPLANATIONS.

                                Support BricsCAD V15 and V19+ differences:
                                - V15: Use classic commands like `-LAYER`, `EXPLORER`.
                                - V19+: Use modern panels like `LAYERSPANELOPEN`.

                                JSON Schema:
                                {
                                  ""tool_calls"": [
                                    {
                                      ""command_name"": ""The primary CAD command (e.g., 'EXPLODE')"",
                                      ""lisp_code"": ""The actual LISP string to execute. (e.g. '(command ""_.CIRCLE"" ...)')"",
                                      ""target_version"": 19, 
                                      ""ui_interaction"": false
                                    }
                                  ]
                                }

                                Examples:
                                User: 'Draw a circle at 0,0 with radius 10'
                                Response: { ""tool_calls"": [{ ""command_name"": ""CIRCLE"", ""lisp_code"": ""(command \""_.CIRCLE\"" \""0,0\"" \""10\"")"", ""target_version"": 19 }] }

                                User: 'Open layer window' (V19 context)
                                Response: { ""tool_calls"": [{ ""command_name"": ""LAYERSPANELOPEN"", ""lisp_code"": ""(initdia) (command \""LAYERSPANELOPEN\"")"", ""target_version"": 19, ""ui_interaction"": true }] }
                                
                                User: 'Open layer window' (V15 context)
                                Response: { ""tool_calls"": [{ ""command_name"": ""-LAYER"", ""lisp_code"": ""(initdia) (command \""_.LAYER\"")"", ""target_version"": 15, ""ui_interaction"": true }] }

                                User: 'Select all objects on layer Walls'
                                Response: { ""tool_calls"": [{ ""command_name"": ""SELECT_LAYER"", ""lisp_code"": ""NET:SELECT_LAYER:Walls"", ""target_version"": 19 }] }

                                User: 'Explode the selected object'
                                Response: { ""tool_calls"": [{ ""command_name"": ""EXPLODE"", ""lisp_code"": ""(command \""_.EXPLODE\"" (ssget \""I\""))"", ""target_version"": 19 }] }

                                User: 'Clean up the drawing'
                                Response: { ""tool_calls"": [{ ""command_name"": ""CLEANUP"", ""lisp_code"": ""(command \""_.PURGE\"" \""A\"" \""\"" \""N\"") (command \""_.AUDIT\"" \""Y\"")"", ""target_version"": 19 }] }
                                
                                User: 'Move selected objects to layer 0'
                                Response: { ""tool_calls"": [{ ""command_name"": ""CHPROP"", ""lisp_code"": ""(command \""_.CHPROP\"" (ssget \""I\"") \""\"" \""LAyer\"" \""0\"" \""\"")"", ""target_version"": 19 }] }

                                User: 'Move the largest box to Layer Frame and smaller boxes to Layer Grids'
                                Response: { ""tool_calls"": [
                                    { 
                                        ""command_name"": ""SORT_BOXES"", 
                                        ""lisp_code"": ""(defun c:SortBoxes (/ ss i ent obj area maxArea maxEnt) (setq ss (ssget \""X\"" '((0 . \""LWPOLYLINE\"")))) (if ss (progn (setq maxArea 0 maxEnt nil) (setq i 0) (repeat (sslength ss) (setq ent (ssname ss i)) (setq obj (vlax-ename->vla-object ent)) (setq area (vla-get-Area obj)) (if (> area maxArea) (setq maxArea area maxEnt ent)) (setq i (1+ i))) (command \""_.LAYER\"" \""M\"" \""Grids\"" \""\"") (command \""_.CHPROP\"" ss \""\"" \""LAyer\"" \""Grids\"" \""\"") (command \""_.LAYER\"" \""M\"" \""Frame\"" \""\"") (if maxEnt (command \""_.CHPROP\"" maxEnt \""\"" \""LAyer\"" \""Frame\"" \""\"")))) (princ)) (c:SortBoxes)"", 
                                        ""target_version"": 19 
                                    }
                                ] }
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

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
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
                
                // Cleanup excessive markdown if model ignores "No Markdown" instruction
                if (script.StartsWith("```json")) script = script.Replace("```json", "").Replace("```", "");
                if (script.StartsWith("```")) script = script.Replace("```", "");

                return script?.Trim();
            }
            catch (Exception ex)
            {
                // Fallback valid JSON for error
                return $@"{{ ""tool_calls"": [{{ ""command_name"": ""ALERT"", ""lisp_code"": ""(alert \""LLM Error: {ex.Message}\"")"" }}] }}";
            }
        }
    }
}

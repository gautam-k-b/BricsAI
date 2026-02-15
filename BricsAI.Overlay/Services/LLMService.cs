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

            var systemPrompt = @"You are an expert BricsCAD automation agent. Your goal is to translate natural language user requests into valid BricsCAD LISP commands.
                                - Return ONLY the LISP code. No markdown, no explanations.
                                - If you need to select objects, use `(ssget)`.
                                - Preferred commands are standard AutoCAD/BricsCAD commands like `_.CIRCLE`, `_.LINE`, `_.CHPROP`.
                                - Examples:
                                User: 'Draw a circle at 0,0 with radius 10' -> Response: (command ""_.CIRCLE"" ""0,0"" ""10"")
                                User: 'Change color to red' -> Response: (command ""_.CHPROP"" (ssget) """" ""Color"" ""Red"" """")
                                
                                -- SPECIAL CAPABILITIES:
                                -- If the user asks to 'Select' or 'Show' all objects on a specific layer, DO NOT use (ssget) or (command ""SELECT"" ...).
                                -- Instead, return exactly this string: NET:SELECT_LAYER:LayerName
                                -- Example: 'Select all objects on layer 0' -> Response: NET:SELECT_LAYER:0
                                -- Example: 'Show me the walls' -> Response: NET:SELECT_LAYER:Walls
                                ";

            var requestBody = new OpenAIRequest
            {
                Model = _model,
                Messages = new[]
                {
                    new Message { Role = "system", Content = systemPrompt },
                    new Message { Role = "user", Content = userPrompt }
                },
                Temperature = 0.2
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
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<OpenAIResponse>(responseJson);

                var script = responseData?.Choices?[0]?.Message?.Content?.Trim();
                
                // Remove markdown code blocks if present
                if (script.StartsWith("```lisp")) script = script.Replace("```lisp", "").Replace("```", "");
                if (script.StartsWith("```")) script = script.Replace("```", "");

                return script?.Trim();
            }
            catch (Exception ex)
            {
                return $"(alert \"LLM Error: {ex.Message}\")";
            }
        }
    }
}

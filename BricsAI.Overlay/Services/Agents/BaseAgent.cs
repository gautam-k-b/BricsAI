using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BricsAI.Overlay.Services.Agents
{
    public abstract class BaseAgent
    {
        protected static readonly HttpClient _httpClient = new HttpClient();
        protected string? _apiKey;
        protected string? _model;
        protected string? _apiUrl;
        
        public string Name { get; protected set; } = "BaseAgent";

        protected class AppSettings
        {
            public OpenAISettings? OpenAI { get; set; }
        }

        protected class OpenAISettings
        {
            public string? ApiKey { get; set; }
            public string? Model { get; set; }
            public string? ApiUrl { get; set; }
        }

        protected class OpenAIRequest
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

        protected class ResponseFormat
        {
            [JsonPropertyName("type")]
            public string? Type { get; set; }
        }

        protected class Message
        {
            [JsonPropertyName("role")]
            public string? Role { get; set; }

            [JsonPropertyName("content")]
            public string? Content { get; set; }
        }

        protected class OpenAIResponse
        {
            [JsonPropertyName("choices")]
            public Choice[]? Choices { get; set; }

            [JsonPropertyName("usage")]
            public Usage? Usage { get; set; }
        }

        protected class Usage
        {
            [JsonPropertyName("total_tokens")]
            public int TotalTokens { get; set; }
        }

        protected class Choice
        {
            [JsonPropertyName("message")]
            public Message? Message { get; set; }
        }

        public BaseAgent()
        {
            LoadConfiguration();
        }

        protected void LoadConfiguration()
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
                _model = "gpt-4o";
                _apiUrl = "https://api.openai.com/v1/chat/completions";
            }
        }

        protected async Task<(string Content, int Tokens)> CallOpenAIAsync(string systemPrompt, string userPrompt, bool expectJson = false)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "YOUR_API_KEY_HERE")
            {
                return (expectJson 
                    ? $@"{{ ""tool_calls"": [{{ ""command_name"": ""NET:MESSAGE: Please configure your OpenAI API Key."", ""lisp_code"": """" }}] }}"
                    : "Error: Please configure your OpenAI API Key.", 0);
            }

            var requestBody = new OpenAIRequest
            {
                Model = _model,
                Messages = new[]
                {
                    new Message { Role = "system", Content = systemPrompt },
                    new Message { Role = "user", Content = userPrompt }
                },
                Temperature = 0.1,
                ResponseFormat = expectJson ? new ResponseFormat { Type = "json_object" } : null
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
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<OpenAIResponse>(responseJson);

                var script = responseData?.Choices?[0]?.Message?.Content?.Trim() ?? string.Empty;

                if (expectJson)
                {
                    if (script.StartsWith("```json")) script = script.Replace("```json", "").Replace("```", "");
                    if (script.StartsWith("```")) script = script.Replace("```", "");
                }

                return (script.Trim(), responseData?.Usage?.TotalTokens ?? 0);
            }
            catch (Exception ex)
            {
                string safeMsg = ex.Message.Replace("\"", "'").Replace("\\", "/");
                return (expectJson 
                    ? $@"{{ ""tool_calls"": [{{ ""command_name"": ""NET:MESSAGE: Agent {Name} Error: {safeMsg}"", ""lisp_code"": """" }}] }}"
                    : $"Agent {Name} Error: {safeMsg}", 0);
            }
        }
    }
}

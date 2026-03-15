using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ClientModel;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace BricsAI.Overlay.Services.Agents
{
    public abstract class BaseAgent
    {
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

        protected ChatClient GetChatClient()
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

        protected async Task<(string Content, int TotalTokens, int InputTokens, int OutputTokens)> CallOpenAIAsync(string systemPrompt, string userPrompt, bool expectJson = false)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "YOUR_API_KEY_HERE")
            {
                return (expectJson 
                    ? $@"{{ ""tool_calls"": [{{ ""command_name"": ""NET:MESSAGE: Please configure your OpenAI API Key."", ""lisp_code"": """" }}] }}"
                    : "Error: Please configure your OpenAI API Key.", 0, 0, 0);
            }

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
                    Temperature = 0.1f
                };

                if (expectJson)
                {
                    options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();
                }

                ChatCompletion completion = await chatClient.CompleteChatAsync(messages, options);
                
                var script = completion.Content[0].Text?.Trim() ?? string.Empty;
                
                if (expectJson)
                {
                    if (script.StartsWith("```json")) script = script.Replace("```json", "").Replace("```", "");
                    if (script.StartsWith("```")) script = script.Replace("```", "");
                }

                int inputTokens = completion.Usage?.InputTokenCount ?? 0;
                int outputTokens = completion.Usage?.OutputTokenCount ?? 0;
                int totalTokens = completion.Usage?.TotalTokenCount ?? 0;

                return (script.Trim(), totalTokens, inputTokens, outputTokens);
            }
            catch (Exception ex)
            {
                string safeMsg = ex.Message.Replace("\"", "'").Replace("\\", "/");
                return (expectJson 
                    ? $@"{{ ""tool_calls"": [{{ ""command_name"": ""NET:MESSAGE: Agent {Name} Error: {safeMsg}"", ""lisp_code"": """" }}] }}"
                    : $"Agent {Name} Error: {safeMsg}", 0, 0, 0);
            }
        }
    }
}

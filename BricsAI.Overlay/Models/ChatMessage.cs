namespace BricsAI.Overlay.Models
{
    public class ChatMessage
    {
        public string Role { get; set; } = string.Empty; // "User" or "Assistant"
        public string Content { get; set; } = string.Empty;
        public bool IsUser => Role == "User";
        public string DisplayName => IsUser ? "User:" : "BricsAI:";
    }
}

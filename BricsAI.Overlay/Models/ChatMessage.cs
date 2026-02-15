namespace BricsAI.Overlay.Models
{
    public class ChatMessage
    {
        public string Role { get; set; } // "User" or "Assistant"
        public string Content { get; set; }
        public bool IsUser => Role == "User";
        public string DisplayName => IsUser ? "User:" : "BricsAI:";
    }
}

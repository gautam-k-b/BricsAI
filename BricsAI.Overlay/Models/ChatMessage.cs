using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BricsAI.Overlay.Models
{
    public class ChatMessage : INotifyPropertyChanged
    {
        private bool _isThinking;

        public string Role { get; set; } = string.Empty; // "User" or "Assistant"
        public string Content { get; set; } = string.Empty;
        public bool IsUser => Role == "User";
        public string DisplayName => IsUser ? "User:" : "BricsAI:";

        public bool IsThinking
        {
            get => _isThinking;
            set
            {
                if (_isThinking != value)
                {
                    _isThinking = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

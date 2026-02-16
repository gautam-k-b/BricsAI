using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using BricsAI.Overlay.Models;

namespace BricsAI.Overlay.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<ChatMessage> Messages { get; set; } = new ObservableCollection<ChatMessage>();

        private string _inputText;
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
            }
        }

        public ICommand SendCommand { get; }

        private readonly Services.ComClient _comClient; // Replaced PipeClient
        private readonly Services.LLMService _llmService;

        public MainViewModel()
        {
            _comClient = new Services.ComClient();
            _llmService = new Services.LLMService();
            SendCommand = new RelayCommand(async _ => await SendMessageAsync());
            
            // Initial greeting
            Messages.Add(new ChatMessage { Role = "Assistant", Content = "Hello! I am your BricsCAD AI Agent. connecting via COM Automation... (No NETLOAD needed)" });
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(InputText)) return;

            var userMessage = InputText;
            InputText = ""; // Clear input immediately
            
            Messages.Add(new ChatMessage { Role = "User", Content = userMessage });

            IsBusy = true;
            
            // 0. Ensure connected
            if (!_comClient.IsConnected)
            {
               // Try connect silently first or just let SendCommand handle it
            }

            // 1. Get script from LLM (now JSON)
            string jsonResponse = await _llmService.GenerateScriptAsync(userMessage);

            // 2. Execute Action (handles JSON parsing and version logic internal to ComClient)
            string response = await _comClient.ExecuteActionAsync(jsonResponse);

            // Add response to chat
            Messages.Add(new ChatMessage { Role = "Assistant", Content = $"Action: {jsonResponse}\nResult: {response}" });

            IsBusy = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly System.Func<object, Task> _execute;
        private readonly System.Predicate<object> _canExecute;

        public RelayCommand(System.Func<object, Task> execute, System.Predicate<object> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);
        public event System.EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}

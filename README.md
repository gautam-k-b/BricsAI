# BricsAI - Intelligent CAD Assistant

BricsAI is an advanced AI-powered assistant for BricsCAD, capable of executing complex CAD operations through natural language commands. It leverages OpenAI's GPT-4o model to interpret user intent and controls BricsCAD via COM Automation.

## 🚀 Key Features

- **Natural Language Control**: Type commands like "Draw a circle" or "Cleanup drawing" instead of memorizing LISP.
- **Structured Tool Use**: Uses JSON-based communication for precise, error-free command execution.
- **Multi-Step Automation**: Can perform sequences of actions (e.g., Select -> Filter -> Move) in a single request.
- **Complex Logic Support**: Handles advanced scenarios like "Find the largest box and move it to Layer Frame" by generating custom LISP algorithms on the fly.
- **Cross-Version Compatibility**: Automatically detects and adapts to:
  - **BricsCAD V15** (Legacy commands like `-LAYER`)
  - **BricsCAD V19+** (Modern panels like `LAYERSPANELOPEN`)

## 🏗️ Architecture

The solution consists of two main components:

1.  **BricsAI.Overlay (WPF Application)**:
    - The main user interface (chat window).
    - Connects to a running BricsCAD instance using **COM Interop** (late binding for compatibility).
    - Handles communication with OpenAI API.
    - Parses structured JSON responses and executes them in BricsCAD.

2.  **BricsAI.Plugin (Legacy)**:
    - *Deprecated approach using .NET Plugin/Named Pipes.*
    - Retained for reference but `BricsAI.Overlay` is the active controller.

## 🛠️ Setup & Usage

### Prerequisites
- BricsCAD (V15 or newer recommended).
- .NET 9.0 SDK.
- OpenAI API Key.

### Configuration
1.  Navigate to `BricsAI.Overlay/appsettings.json`.
2.  Add your OpenAI API Key:
    ```json
    {
      "OpenAI": {
        "ApiKey": "sk-...",
        "Model": "gpt-4o"
      }
    }
    ```

### Running the Assistant
1.  Open `BricsAI.sln` in Visual Studio or VS Code.
2.  Build the `BricsAI.Overlay` project.
3.  **Start BricsCAD** manually.
4.  Run `BricsAI.Overlay.exe`.
5.  The application will automatically connect to the active BricsCAD instance.

## 💡 Example Commands

| Intent | Command to Type | What Happens |
| :--- | :--- | :--- |
| **Draw Utility** | "Draw a circle at 0,0 with radius 10" | Executes `_.CIRCLE`. |
| **Layer Control** | "Open layer window" | Opens `LAYERSPANELOPEN` (V19+) or `_.LAYER` (V15). |
| **Cleanup** | "Clean up the drawing" | Runs `PURGE` and `AUDIT` sequentially. |
| **Complex Logic** | "Move the largest box to Layer Frame..." | Generates a LISP script to calculate areas, find the max, and move objects. |

## 🧩 Technical Details

- **COM via `dynamic`**: We use `dynamic` types in C# to avoid strict dependency on specific BricsCAD COM DLL versions, ensuring broader compatibility.
- **System Prompt**: Converting natural language to LISP is handled by a robust system prompt in `LLMService.cs` with few-shot examples.

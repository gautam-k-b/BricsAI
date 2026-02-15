# BricsAI - AI Integration for BricsCAD

## Overview

BricsAI integrates Large Language Models (LLM) with BricsCAD to automate CAD tasks using natural language commands. 

The solution consists of two main components:
1. **BricsAI.Plugin**: A .NET plugin for BricsCAD that executes commands and manages selections.
2. **BricsAI.Overlay**: A WPF overlay application that accepts user prompts, communicates with OpenAI, and sends the resulting LISP/commands to the plugin via Named Pipes.

## Prerequisites

- **BricsCAD Pro/Platinum** (Supported versions compatible with .NET Framework 4.6.1+)
- **.NET SDK** (for building the solution)
- **OpenAI API Key**

## Project Structure

- `BricsAI.Plugin/`: The BricsCAD .NET plugin project.
- `BricsAI.Overlay/`: The WPF overlay application project.
- `build.bat`: Script to build the entire solution.

## Installation & Setup

1.  **Clone the Repository**
    ```bash
    git clone <repository_url>
    cd AIWithBricsCad/BricsAI
    ```

2.  **Configure API Key**
    - Navigate to `BricsAI.Overlay/`.
    - Create or update `appsettings.json` with your OpenAI API key:
      ```json
      {
        "OpenAI": {
          "ApiKey": "YOUR_OPENAI_API_KEY",
          "Model": "gpt-4o"
        }
      }
      ```

3.  **Build the Solution**
    - Run `build.bat` from the root directory.
    - Alternatively, open `BricsAI.sln` in Visual Studio and build.

## Usage

1.  **Load the Plugin in BricsCAD**
    - Open BricsCAD.
    - Type `NETLOAD` in the command line.
    - Select the built DLL: `BricsAI.Plugin/bin/Debug/net461/BricsAI.Plugin.dll` (path may vary based on build configuration).

2.  **Run the Overlay**
    - Executable: `BricsAI.Overlay/bin/Debug/net8.0-windows/BricsAI.Overlay.exe`
    - The overlay should appear on top of BricsCAD.

3.  **Execute Commands**
    - Type natural language requests into the overlay (e.g., "Draw a circle at 0,0 with radius 50", "Select all walls").
    - The AI will process the request and execute the actions in BricsCAD.

## Features

- **Natural Language Command Translation**: Converts English prompts to BricsCAD LISP commands.
- **Layer Selection**: Intelligent selection of objects on specific layers (e.g., "Select all furniture").
- **Named Pipe Communication**: Robust inter-process communication between the overlay UI and the CAD plugin.

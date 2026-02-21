# Release Notes - BricsAI

## 🌟 v2.0.0 - The Multi-Agent Orchestrator Update

This massive update shifts BricsAI from a simple LLM command generator to a fully autonomous, multi-agent reasoning engine capable of executing complex 6-step proofing workflows with near-perfect reliability.

### 🤖 Multi-Agent Pipeline
- **Surveyor Agent**: Automatically analyzes drawing state, extracting layer counts and bounding box heuristics before touching geometry.
- **Executor Agent**: Synthesizes the Surveyor's data to generate a massive array of LISP and Native COM macros.
- **Validator Agent**: Reads the execution logs directly from BricsCAD to verify that all commands theoretically executed as requested.

### 🏗️ Native COM Selections & Migrations
- **LISP ssget Replacement**: Completely bypassed the `ssget` command's selection bleed-over issues.
- **Native Target Instantiation**: The C# Host now explicitly checks for and creates target layers on the fly (e.g., `Expo_BoothOutline`), directly assigning geometric `.Layer` properties mathematically via DXF 8 filtering.
- **Safe Explosion Constraints**: Blocks are iteratively exploded while explicitly safeguarding primitives (Polylines, Arcs, Text) from destruction.

### ⚙️ Configurable Layer Mappings
- **Dynamic Vendor Mapping**: Introduced `layer_mappings.json`. Users can now map bizarre source CAD layers (e.g., `l1xxxx`) directly to standard A2Z layers (e.g., `Expo_BoothOutline`).
- **Zero-Code Adaptation**: The UI Orchestrator reads this file at runtime and injects it into both the Surveyor and Executor prompts, forcing absolute mapping prioritization without needing to recompile the AI.

### 🧹 Destructive Cleanup Tooling
- **Safe Deletion**: Introduced the `(command "-LAYDEL" ...)` tool logic to forcefully annihilate empty vendor layers that resist standard Purge commands.

---

## v1.1.0 - Foundation Update

## 🚀 New Features

### 1. Robust COM Communication
- **Migrated from Named Pipes**: The application now communicates directly with BricsCAD via **COM Automation (Late Binding)**.
- **Why**: This eliminates the need for `NETLOAD` and enables compatibility with older BricsCAD versions (like V15).

### 2. Structured Tool Use (JSON)
- **Error-Free Execution**: Commands are generated as structured JSON objects rather than plain text.
- **Precision**: Prevents syntax errors in LISP generation.

### 3. Smart Version Detection
- **Auto-Adapt**: Automatically detects whether you are running **BricsCAD V15** or **V19+**.
- **Context-Aware Commands**:
  - **V15**: Uses classic commands (e.g., `-LAYER`, `EXPLORER`).
  - **V19+**: Uses modern panels (e.g., `LAYERSPANELOPEN`).

### 4. Multi-Step & Complex Logic
- **Sequencing**: Can now execute multiple actions in a single response (e.g., Select -> Filter -> Move).
- **Advanced Algorithms**: Capable of generating complex LISP logic, such as:
  - *"Find the largest box and move it to Layer Frame"* (calculates area, sorts, and moves objects).

## 🐛 Bug Fixes
- Fixed `NETLOAD` compatibility issues with BricsCAD V15.
- Resolved "Command not found" errors for modern panels on older versions.
- Improved reliability of object selection and highlighting.

## 🛠️ Developer Notes
- Updated `LLMService.cs` system prompt with 7+ few-shot training examples.
- Refactored `ComClient.cs` to handle JSON parsing and multi-step execution loop.

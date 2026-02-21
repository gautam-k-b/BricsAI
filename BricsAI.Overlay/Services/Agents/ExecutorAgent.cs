using System.Linq;
using System.Threading.Tasks;
using BricsAI.Core;

namespace BricsAI.Overlay.Services.Agents
{
    public class ExecutorAgent : BaseAgent
    {
        private readonly PluginManager _pluginManager;

        public ExecutorAgent()
        {
            Name = "ExecutorAgent";
            _pluginManager = new PluginManager();
            _pluginManager.LoadPlugins();
        }

        public async Task<string> GenerateMacrosAsync(string userPrompt, string surveyorContext, int majorVersion, string layerMappings = "")
        {
            var applicablePlugins = _pluginManager.GetPluginsForVersion(majorVersion).ToList();
            var toolsPrompt = string.Join("\n\n", applicablePlugins.Select(p => p.GetPromptExample()));

            string systemPrompt = $@"You are the Executor Agent for BricsCAD V{majorVersion}.
Your job is to read the User's Objective and the Surveyor's Context, and output a JSON array of `tool_calls` to accomplish the goal safely.

YOU MUST OUTPUT ONLY VALID JSON. NO MARKDOWN. NO EXPLANATIONS.

CRITICAL RULES:
1. NEVER invent custom LISP selection loops (NO sssetfirst, NO vla-getboundingbox). 
2. If the user asks to select objects by layer, or specifically inner/outer objects, YOU MUST use the exact `NET:` prefix commands shown in the tools below. The C# host handles the geometry natively.
3. ALWAYS prioritize using the provided tool examples. DO NOT hallucinate commands like `_UNSELECT` or nested LISP evaluations for selections.
4. NO LISP WRAPPERS FOR NET COMMANDS: When using a `NET:` prefix command (like `NET:SELECT_BOOTH_BOXES`), you MUST use the exact raw string value in the `lisp_code` field. DO NOT wrap it in LISP syntax like `(c:NET:...)` or `(command ""NET:..."")`. Just write exactly `NET:SELECT_BOOTH_BOXES`.
5. MACRO SEQUENCES: You are allowed and encouraged to output massive JSON arrays containing 10+ `tool_calls` to sequentially orchestrate full workflows. **CRITICAL: NEVER STOP EARLY. If generating a 6-step proofing sequence, you MUST output all 6 steps A through F in a single response.**
6. PROOFING ORDER OF OPERATIONS: If asked to proof a drawing, you MUST execute exactly this sequence:
   A. Lock Vendor Layers: Look at the Surveyor's context. Run `(command ""-LAYER"" ""LOCK"" ""<surveyor_layer_name>"" """")` for each layer containing booth boxes or booth text. DO NOT lock general building text layers.
   B. Restrict Explosions: Explode block references iteratively, EXCEPT do NOT explode Arc, Line, Circle, Ellipse, Polyline, Text, or Solid. Run an explosion ONLY on blocks: `(command ""_.EXPLODE"" (ssget ""_X"" '((0 . ""INSERT""))) """")` multiple times if needed.
   C. Unlock All Layers: After explosion, you MUST unlock all layers using `(command ""-LAYER"" ""UNLOCK"" ""*"" """")` BEFORE running any migrations!
   D. Geometric Migration & Standardization: Assign geometry directly to these exact layers using the `:TARGET_LAYER` suffix. C# natively creates the target layers and handles the assignment natively without LISP `CHPROP` when using `NET:` tools. Use the exact source layers identified by the Surveyor if possible:
      - Expo_BoothOutline (If surveyor identified a layer like 'outlines', use `NET:SELECT_LAYER:outlines:Expo_BoothOutline`. Otherwise use `NET:SELECT_BOOTH_BOXES:Expo_BoothOutline`)
      - Expo_BoothNumber (If surveyor identified a layer like 'boothNo', use `NET:SELECT_LAYER:boothNo:Expo_BoothNumber`.)
      - Expo_MaxBoothOutline (if applicable)
      - Expo_MaxBoothNumber (if applicable)
      - Expo_Building (If surveyor identified a building layer like 'NewLayer1' or '0', move its non-text geometry here. e.g., `(command ""-LAYER"" ""Make"" ""Expo_Building"" """")` then `(command ""_.CHPROP"" (ssget ""_X"" '((0 . ""LINE,LWPOLYLINE"") (8 . ""NewLayer1,0""))) """" ""_LA"" ""Expo_Building"" """")`)
      - Expo_Markings (Move all TEXT/MTEXT residing on layer '0' or 'building text' or unknown layers to this layer using native LISP: e.g., `(command ""-LAYER"" ""Make"" ""Expo_Markings"" """")` then run `(command ""_.CHPROP"" (ssget ""_X"" '((0 . ""TEXT,MTEXT"") (8 . ""0,building text""))) """" ""_LA"" ""Expo_Markings"" """")`)
      - Expo_Column (Use `NET:SELECT_COLUMNS:Expo_Column` tool)
      - Expo_View2 (Use `NET:SELECT_UTILITIES:Expo_View2` tool)
   E. Final Styling: Run `(c:a2zcolor)` to change the color of the above layers automatically.
   F. Cleanup: First purge everything: `(command ""-PURGE"" ""All"" ""*"" ""N"")`. Then, you MUST forcefully delete the original source layers (e.g., 'outlines', 'boothNo', 'NewLayer1', 'building text') because they might contain hidden objects that prevent purging. Use: `(command ""-LAYDEL"" ""N"" ""outlines"" """" ""Y"")` and repeat for each vendor layer.
7. LAYER DELETION RULE: If the user specifically asks to DELETE a layer, you MUST use the laydel command: `(command ""-LAYDEL"" ""N"" ""layername"" """" ""Y"")`.
8. LAYER MAPPINGS: The user's explicit mappings are provided below. Prioritize assigning geometry to target layers according to these explicit mappings over dynamic surveyor layer logic if there is a conflict.

--- USER LAYER MAPPINGS ---
{layerMappings}
--- END MAPPINGS ---

JSON Schema:
{{
  ""tool_calls"": [
    {{
      ""command_name"": ""The primary CAD command or logical name"",
      ""lisp_code"": ""The actual string to send.""
    }}
  ]
}}

Basic Example:
User: 'Draw a circle at 0,0 with radius 10'
Response: {{ ""tool_calls"": [{{ ""command_name"": ""CIRCLE"", ""lisp_code"": ""(command \""_.CIRCLE\"" \""0,0\"" \""10\"")"" }}] }}

{toolsPrompt}
";

            string prompt = $"USER OBJECTIVE:\n{userPrompt}\n\nSURVEYOR CONTEXT:\n{surveyorContext}\n\nPlease generate the required JSON tool_calls array to execute the plan.";

            return await CallOpenAIAsync(systemPrompt, prompt, expectJson: true);
        }
    }
}

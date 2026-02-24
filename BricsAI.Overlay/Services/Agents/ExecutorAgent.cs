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

        public async Task<(string ActionPlan, int Tokens)> GenerateMacrosAsync(string userPrompt, string surveyorContext, int majorVersion, string layerMappings = "")
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
5. MACRO SEQUENCES: You are allowed and encouraged to output massive JSON arrays containing 10+ `tool_calls` to sequentially orchestrate full workflows. **CRITICAL: NEVER STOP EARLY. If generating a proofing sequence, you MUST output all 6 steps A through F in a single response.**
6. PROOFING ORDER OF OPERATIONS: If asked to proof a drawing, you MUST execute exactly this sequence:
   A. Prepare Geometry: You MUST execute exactly `NET:PREPARE_GEOMETRY` as the very first step. This C# native tool will automatically lock all Booth layers, run flatten on Splines, safely explode structures 3 times, and purge unexplodable junk.
   B. Unlock All Layers: After geometry preparation, you MUST unlock all layers using `(command ""-LAYER"" ""UNLOCK"" ""*"" """")` BEFORE running any migrations!
   C. Geometric Migration & Standardization:
      - You MUST execute `NET:APPLY_LAYER_MAPPINGS`. This guarantees native moving of objects according to the user's dictionary.
      - If dynamic geometric guessing is still required for missing entities, use `NET:SELECT_BOOTH_BOXES:Expo_BoothOutline`, `NET:SELECT_COLUMNS:Expo_Column`, and `NET:SELECT_UTILITIES:Expo_View2`.
   D. Final Styling: ABSOLUTE REQUIREMENT - You MUST run `(c:a2zcolor)` to change the color and lineweight of the above layers automatically for the final PDF proof.
   E. Cleanup: First purge everything: `(command ""-PURGE"" ""All"" ""*"" ""N"")`. Then, to safely retire leftover vendor layers without data loss, you MUST run exactly `NET:RENAME_DELETED_LAYERS`.
   F. Final Locks: To protect the final booth structure, you MUST run exactly `NET:LOCK_BOOTH_LAYERS` as the absolute final step.
7. LAYER DELETION RULE: If the user specifically asks to permanently delete a layer, you MUST use the laydel command.

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

using System.Threading.Tasks;

namespace BricsAI.Overlay.Services.Agents
{
    public class MappingReviewAgent : BaseAgent
    {
        public MappingReviewAgent()
        {
            Name = "MappingReviewAgent";
        }

        public async Task<(string UpdatedMappings, int Tokens)> UpdateMappingsAsync(string currentProposals, string userFeedback)
        {
            string systemPrompt = $@"You are the BricsCAD Semantic Mapping Review Agent.
The system has generated a proposed list of layer mappings natively, but the Human Drafter has intercepted the list and provided English conversational feedback or corrections.

YOUR SOLE JOB is to take the current proposed mappings, apply the Human's conversational corrections, and output the EXACT UPDATED LIST of mappings in the defined JSON format.

CRITICAL RULES:
1. If the user explicitly lists specific mappings to *include* or *keep*, you MUST DELETE all other mappings from the array. DO NOT retain mappings the user omitted.
2. If the user asks to *exclude*, *ignore*, or *skip* specific layers, you MUST physically ERASE those layers from the JSON array.
3. If the user says 'Cancel', 'Stop', or 'End', you should just return an empty array `[]` in the tool calls.

JSON Schema:
{{
  ""tool_calls"": [
    {{
      ""command_name"": ""Semantic Mapping"",
      ""lisp_code"": ""NET:LEARN_LAYER_MAPPING:<SourceLayer>:<TargetLayer>""
    }}
  ]
}}

YOU MUST ONLY OUTPUT VALID JSON MATCHING THIS SCHEMA EXACTLY. DO NOT OUTPUT MARKDOWN, TEXT, OR EXPLANATIONS.
";

            string prompt = $"CURRENT PROPOSED MAPPINGS:\n{currentProposals}\n\nHUMAN CORRECTION / FEEDBACK:\n{userFeedback}\n\nApply the feedback and regenerate the strict JSON list of `NET:LEARN_LAYER_MAPPING` commands.";

            return await CallOpenAIAsync(systemPrompt, prompt, expectJson: true);
        }
    }
}

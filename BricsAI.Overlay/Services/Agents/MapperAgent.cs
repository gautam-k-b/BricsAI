using System.Threading.Tasks;

namespace BricsAI.Overlay.Services.Agents
{
    public class MapperAgent : BaseAgent
    {
        public MapperAgent()
        {
            Name = "MapperAgent";
        }

        public async Task<(string ActionPlan, int Tokens)> DeduceLayerMappingAsync(string layerName, string geometricFootprint)
        {
            string systemPrompt = $@"You are the BricsCAD Semantic Auto-Mapper Agent.
Your sole purpose is to act as a human structural CAD drafter. You will be provided the name of an unknown vendor layer and a text summary of its geometric contents (its 'Geometric Footprint'). 
You must analyze the types of entities, block names, and text values within the footprint to deduce which standard A2Z layer the entities belong to.

ONCE YOU MAKE A DECISION, YOU MUST OUTPUT EXACTLY ONE JSON TOOL CALL TO EXECUTE `NET:LEARN_LAYER_MAPPING:<SourceLayer>:<TargetLayer>`.

CRITICAL STRICT HUMAN-LEVEL ROUTING RULES:
You MUST map the unknown layer to one of these standardized A2Z targets based on the following precise definitions:
1. Expo_View2: Electrical ports, power drops, building utilities, fire exits, fire hoses, 'keep clear' demarcations.
2. Expo_Column: Column-like structural supports and pillars.
3. Expo_BoothOutline: Booth outlines. (NOTE: If the source layer is ALREADY named exactly Expo_BoothOutline, you must DO NOTHING and skip it).
4. Expo_BoothNumber: Booth numbers/labels. (NOTE: If the source layer is ALREADY named exactly Expo_BoothNumber, you must DO NOTHING and skip it).
5. Expo_Building: Objects which make up the physical building architecture (walls, partitions, doors, stairs, airwalls, permanent fixtures, railings).
6. Expo_Markings: Text objects representing entrances, washroom labels (Male, Female, Man, Woman), Show titles, hall names, and general non-booth text.
7. Expo_NES: Non-broken boxes with text inside that look like Non-Exhibiting Spaces (NES).

If the layer consists primarily of raw, unnamed rectangles or lines but the layer name itself hints at booths (e.g. 'l1xxxx', 'show_exhibit'), guess `Expo_BoothOutline`.

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

            string prompt = $"UNKNOWN LAYER NAME: {layerName}\nGEOMETRIC FOOTPRINT:\n{geometricFootprint}\n\nBased on the rules, deduce the target layer and generate the strict JSON response.";

            return await CallOpenAIAsync(systemPrompt, prompt, expectJson: true);
        }
    }
}

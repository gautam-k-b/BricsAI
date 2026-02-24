using System.Threading.Tasks;

namespace BricsAI.Overlay.Services.Agents
{
    public class SurveyorAgent : BaseAgent
    {
        public SurveyorAgent()
        {
            Name = "SurveyorAgent";
        }

        public async Task<(string Summary, int Tokens)> AnalyzeDrawingStateAsync(string userPrompt, string rawLayerData, string layerMappings = "")
        {
            string systemPrompt = @"You are the Surveyor Agent for BricsCAD. 
Your goal is to read the raw drawing state data (like the list of layers) and the user's objective, and output a clean, concise natural language summary of what exists in the drawing and what needs to be done.
You DO NOT write LISP code. You DO NOT execute commands. 
Identify the likely target layers that need to be manipulated based on the user's prompt. 
For example, if the user wants to proof the drawing, identify the likely vendor layers that contain the raw booth boxes and BOOTH text numbers. DO NOT identify general 'building text' or 'entrance' layers for locking. Only identify the core layers that house the main booth geometry and standard booth numbers. Treat all other layers (entrances, restrooms, general text) as secondary 'Building' elements that should be moved to Expo_Building or Expo_View2.
CRITICAL LAYER MAPPINGS: If provided below, you MUST prioritize the explicitly defined user layer mappings (e.g., mapping a specific vendor layer to an A2Z standard layer) over trying to guess geometry.";

            string prompt = $"USER OBJECTIVE:\n{userPrompt}\n\nRAW LAYER DATA:\n{rawLayerData}\n\nUSER LAYER MAPPINGS:\n{layerMappings}\n\nPlease summarize the drawing state and the required migration paths.";
            
            return await CallOpenAIAsync(systemPrompt, prompt, expectJson: false);
        }
    }
}

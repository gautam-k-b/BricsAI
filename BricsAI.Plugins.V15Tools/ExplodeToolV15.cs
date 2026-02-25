using BricsAI.Core;

namespace BricsAI.Plugins.V15Tools
{
    public class ExplodeToolV15 : IToolPlugin
    {
        public string Name => "Explode All Entities";
        public string Description => "Explodes all block references and complex entities in the BricsCAD V15 drawing.";
        public int TargetVersion => 15;

        public string GetPromptExample()
        {
            return "User: 'Explode all the blocks'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"EXPLODE\", \"lisp_code\": \"(command \\\"_.EXPLODE\\\" (ssget \\\"_X\\\") \\\"\\\")\" }] }\n\n" +
                   "User: 'check for the type MTEXT in quick select and if available, explode it'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"QSELECT_EXPLODE\", \"lisp_code\": \"NET:QSELECT_EXPLODE:MTEXT\" }] }";
        }
    }
}

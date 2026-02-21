namespace BricsAI.Core
{
    public interface IToolPlugin
    {
        string Name { get; }
        string Description { get; }
        
        // Indicates which major version this plugin targets (e.g., 15 or 19)
        int TargetVersion { get; } 
        
        // The example JSON instruction to inject into the Main Agent's System Prompt
        // MUST be specific to the TargetVersion LISP commands.
        string GetPromptExample(); 
    }
}

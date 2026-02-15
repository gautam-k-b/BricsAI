using System;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Bricscad.Runtime;
using Teigha.Runtime;

// Register the extension application
[assembly: ExtensionApplication(typeof(BricsAI.Plugin.PluginEntry))]

namespace BricsAI.Plugin
{
    public class PluginEntry : IExtensionApplication
    {
        private static PipeServer _server;

        public void Initialize()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage("\nLoading BricsAI Plugin...");

            try 
            {
                _server = new PipeServer();
                _server.Start();
                ed.WriteMessage("\nBricsAI Agent Ready. Listening on 'BricsAI_Pipe'.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError starting BricsAI Agent: {ex.Message}");
            }
        }

        public void Terminate()
        {
            _server?.Stop();
        }
    }
}

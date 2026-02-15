using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Bricscad.ApplicationServices;

namespace BricsAI.Plugin
{
    public class PipeServer
    {
        private const string PIPE_NAME = "BricsAI_Pipe";
        private CancellationTokenSource _cts;
        private Task _serverTask;

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _serverTask = Task.Run(() => ListenLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(PIPE_NAME, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous))
                    {
                        await server.WaitForConnectionAsync(token);

                        using (var reader = new StreamReader(server))
                        using (var writer = new StreamWriter(server) { AutoFlush = true })
                        {
                            string command = await reader.ReadLineAsync();
                            if (!string.IsNullOrEmpty(command))
                            {
                                string result = ExecuteCommandOnMainThread(command);
                                await writer.WriteAsync(result);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception) 
                {
                    // Log error or retry
                }
            }
        }

        private string ExecuteCommandOnMainThread(string lispCommand)
        {
            string result = "OK";
            var doc = Application.DocumentManager.MdiActiveDocument;
            
            // Handle Custom .NET Commands from Overlay
            if (lispCommand.StartsWith("NET:SELECT_LAYER:"))
            {
                string layerName = lispCommand.Substring("NET:SELECT_LAYER:".Length).Trim();
                return SelectObjectsOnLayer(doc, layerName);
            }

            // Default: Execute LISP
            using (var lockDoc = doc.LockDocument())
            {
                try
                {
                     doc.SendStringToExecute(lispCommand + "\n", true, false, false);
                }
                catch (System.Exception ex)
                {
                    result = "Error: " + ex.Message;
                }
            }
            return result;
        }

        private string SelectObjectsOnLayer(Document doc, string layerName)
        {
            try
            {
                using (var lockDoc = doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    // Use Teigha.DatabaseServices types
                    var bt = (Teigha.DatabaseServices.BlockTable)tr.GetObject(doc.Database.BlockTableId, Teigha.DatabaseServices.OpenMode.ForRead);
                    var btr = (Teigha.DatabaseServices.BlockTableRecord)tr.GetObject(bt[Teigha.DatabaseServices.BlockTableRecord.ModelSpace], Teigha.DatabaseServices.OpenMode.ForRead);

                    var ids = new System.Collections.Generic.List<Teigha.DatabaseServices.ObjectId>();

                    foreach (var id in btr)
                    {
                        var ent = (Teigha.DatabaseServices.Entity)tr.GetObject(id, Teigha.DatabaseServices.OpenMode.ForRead);
                        // Accessing Layer property on Entity
                        if (ent.Layer.Equals(layerName, StringComparison.OrdinalIgnoreCase))
                        {
                            ids.Add(id);
                        }
                    }

                    if (ids.Count > 0)
                    {
                        doc.Editor.SetImpliedSelection(ids.ToArray());
                        doc.Editor.WriteMessage($"\n[BricsAI] Selected {ids.Count} objects on layer '{layerName}'.\n");
                    }
                    else
                    {
                        doc.Editor.WriteMessage($"\n[BricsAI] No objects found on layer '{layerName}'.\n");
                    }
                    
                    tr.Commit();
                }
                return "Selection Updated";
            }
            catch (Exception ex)
            {
                return "Error selecting layer: " + ex.Message;
            }
        }
    }
}

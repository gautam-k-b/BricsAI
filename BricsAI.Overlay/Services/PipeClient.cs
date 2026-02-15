using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace BricsAI.Overlay.Services
{
    public class PipeClient
    {
        private const string PIPE_NAME = "BricsAI_Pipe";

        public async Task<string> SendCommandAsync(string command)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut))
                {
                    await client.ConnectAsync(2000); // 2s timeout
                    
                    // Use Read/Write without closing the stream implicitly via nested usings if possible,
                    // or just manage the stream directly. 
                    // To avoid "Cannot access closed pipe" on double dispose or race conditions:
                    
                    var writer = new StreamWriter(client) { AutoFlush = true };
                    var reader = new StreamReader(client);

                    try
                    {
                        await writer.WriteLineAsync(command);
                        
                        // Read response
                        string response = await reader.ReadToEndAsync();
                        return response;
                    }
                    catch
                    {
                        throw;
                    }
                    // We rely on client.Dispose() to close the pipe. 
                    // Writer/Reader usually don't need disposal if we don't care about internal buffers being flushed 
                    // (writer.AutoFlush is true) and we are closing the stream anyway.
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}

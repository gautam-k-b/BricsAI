using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;

namespace BricsAI.Overlay.Services
{
    public class ComClient
    {
        private dynamic? _acadApp;
        private readonly PluginManager _pluginManager = new PluginManager();

        public bool IsConnected => _acadApp != null;

        // P/Invoke for GetActiveObject
        [DllImport("oleaut32.dll", PreserveSig = false)]
        private static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.IDispatch)] out object? ppunk);

        private static object? GetActiveObject(string progId)
        {
            try
            {
                Type? t = Type.GetTypeFromProgID(progId);
                if (t == null) return null;
                
                Guid clsid = t.GUID;
                GetActiveObject(ref clsid, IntPtr.Zero, out object? obj);
                return obj;
            }
            catch (Exception)
            {
                return null;
            }
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task<string> SendCommandAsync(string command)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            try
            {
                // Connect if not already connected
                if (_acadApp == null)
                {
                    // Try BricsCAD first
                    _acadApp = GetActiveObject("BricscadApp.AcadApplication");

                    if (_acadApp == null)
                    {
                        // Try AutoCAD fallback
                       _acadApp = GetActiveObject("AutoCAD.Application");
                    }

                    if (_acadApp == null)
                    {
                        return "Error: Could not connect to BricsCAD. Is it running?";
                    }
                }

                if (command.StartsWith("NET:"))
                {
                    var plugin = _pluginManager.GetPluginForCommand(command, MajorVersion);
                    if (plugin != null)
                    {
                        return plugin.Execute(_acadApp.ActiveDocument, command);
                    }
                    return $"WARNING Unrecognized or Unsupported NET command: {command}";
                }
                
                // Standard LISP command
                object? ignore = _acadApp!.ActiveDocument.SendCommand(command + "\n");
                return "Command sent.";
            }
            catch (Exception ex)
            {
                _acadApp = null; // Reset connection on failure
                return $"Error executing command: {ex.Message}";
            }
        }

        public int MajorVersion { get; private set; }

        public async Task<bool> ConnectAsync()
        {
            return await Task.Run(() =>
            {
                if (_acadApp != null) return true;

                try
                {
                    // Try BricsCAD first
                    _acadApp = GetActiveObject("BricscadApp.AcadApplication");
                    if (_acadApp != null)
                    {
                        DetectVersion();
                        _pluginManager.LoadPlugins();
                        return true;
                    }

                    // Try AutoCAD fallback
                    _acadApp = GetActiveObject("AutoCAD.Application");
                    if (_acadApp != null)
                    {
                        DetectVersion();
                        _pluginManager.LoadPlugins();
                        return true;
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            });
        }

        private void DetectVersion()
        {
            try
            {
                string? versionStr = _acadApp?.Version;
                if (versionStr != null)
                {
                    // Extract just the leading digits (e.g., "24.1s (x64)" -> "24")
                    var match = System.Text.RegularExpressions.Regex.Match(versionStr, @"^\d+");
                    if (match.Success && int.TryParse(match.Value, out int v))
                    {
                        MajorVersion = v;
                    }
                    else
                    {
                        MajorVersion = 19;
                    }
                }
                else
                {
                    MajorVersion = 19;
                }
            }
            catch
            {
                MajorVersion = 19; // Default to V19 if detection fails (modern safe)
            }
        }

        public async Task<string> ExecuteActionAsync(string actionJson, System.IProgress<string>? progress = null)
        {
            if (_acadApp == null && !await ConnectAsync())
            {
                string err = "Error: Could not connect to BricsCAD.";
                progress?.Report($"❌ {err}");
                return err;
            }

            try
            {
                // Simple JSON parsing (avoiding full serializer overhead inside ComClient if possible, but we need it here)
                // Assuming format: {"command": "LAYERSPANELOPEN", "lisp_code": "..."}
                // or the user's schema: command_name, lisp_code, target_version
                
                using (var doc = JsonDocument.Parse(actionJson))
                {
                    var root = doc.RootElement;
                    
                    // Check for "tool_calls" array
                    if (root.TryGetProperty("tool_calls", out var tools) && tools.ValueKind == JsonValueKind.Array)
                    {
                        var results = new List<string>();
                        int step = 1;

                        foreach (var tool in tools.EnumerateArray())
                        {
                            string? lispCode = tool.TryGetProperty("lisp_code", out var lisp) ? lisp.GetString() : null;
                            string? commandName = tool.TryGetProperty("command_name", out var cmd) ? cmd.GetString() : null;

                            string netCmd = "";
                            if (!string.IsNullOrEmpty(lispCode) && lispCode!.Contains("NET:")) 
                                netCmd = lispCode.Substring(lispCode.IndexOf("NET:")).TrimEnd(')', ' ', '\n', '\r');
                            else if (!string.IsNullOrEmpty(commandName) && commandName!.Contains("NET:")) 
                                netCmd = commandName.Substring(commandName.IndexOf("NET:")).TrimEnd(')', ' ', '\n', '\r');

                            if (!string.IsNullOrEmpty(netCmd) && _acadApp?.ActiveDocument != null)
                            {
                                if (netCmd.StartsWith("NET:MESSAGE:"))
                                {
                                    string msg = netCmd.Substring("NET:MESSAGE:".Length).Trim();
                                    string ret = $"MESSAGE: {msg}";
                                    results.Add(ret);
                                    progress?.Report($"💬 {msg}");
                                }
                                else
                                {
                                    var plugin = _pluginManager.GetPluginForCommand(netCmd, MajorVersion);
                                    if (plugin != null)
                                    {
                                        progress?.Report($"🛠️ [{step}/{tools.GetArrayLength()}] Executing {plugin.Name}...");
                                        string executeResult = plugin.Execute(_acadApp.ActiveDocument, netCmd);
                                        string logEntry = $"Step {step}: {executeResult}";
                                        results.Add(logEntry);
                                        progress?.Report($"✅ {executeResult}\n");
                                        step++;
                                    }
                                    else
                                    {
                                        string err = $"Step {step++}: WARNING Unrecognized or Unsupported NET command: {netCmd}";
                                        results.Add(err);
                                        progress?.Report($"⚠️ Unsupported Tool: {netCmd}\n");
                                    }
                                }
                            }
                            else if (!string.IsNullOrEmpty(lispCode) && _acadApp?.ActiveDocument != null)
                            {
                                progress?.Report($"🛠️ [{step}/{tools.GetArrayLength()}] Sending Native Command...");
                                object? ignore = _acadApp!.ActiveDocument.SendCommand(lispCode + "\n");
                                string res = $"Step {step++}: Executed LISP [{lispCode}]";
                                results.Add(res);
                                progress?.Report($"✅ Completed Native String\n");
                            }
                            else if (!string.IsNullOrEmpty(commandName) && _acadApp?.ActiveDocument != null)
                            {
                                progress?.Report($"🛠️ [{step}/{tools.GetArrayLength()}] Sending Command {commandName}...");
                                object? ignore = _acadApp!.ActiveDocument.SendCommand(commandName + "\n");
                                string res = $"Step {step++}: Executed {commandName}";
                                results.Add(res);
                                progress?.Report($"✅ Completed {commandName}\n");
                            }
                        }
                        
                        string finalResult = string.Join("\n", results);
                        return finalResult;
                    }
                    
                    // Fallback for direct object (legacy/single tool)
                    JsonElement singleTool = root;
                    string? sLisp = singleTool.TryGetProperty("lisp_code", out var sl) ? sl.GetString() : null;
                    string? sCmd = singleTool.TryGetProperty("command_name", out var sc) ? sc.GetString() : null;

                    string netCmdSingle = "";
                    if (!string.IsNullOrEmpty(sLisp) && sLisp!.Contains("NET:")) 
                        netCmdSingle = sLisp.Substring(sLisp.IndexOf("NET:")).TrimEnd(')', ' ', '\n', '\r');
                    else if (!string.IsNullOrEmpty(sCmd) && sCmd!.Contains("NET:")) 
                        netCmdSingle = sCmd.Substring(sCmd.IndexOf("NET:")).TrimEnd(')', ' ', '\n', '\r');

                    if (!string.IsNullOrEmpty(netCmdSingle) && _acadApp?.ActiveDocument != null)
                    {
                        if (netCmdSingle.StartsWith("NET:MESSAGE:"))
                        {
                            string msg = netCmdSingle.Substring("NET:MESSAGE:".Length).Trim();
                            progress?.Report($"💬 {msg}");
                            return msg;
                        }

                        var plugin = _pluginManager.GetPluginForCommand(netCmdSingle, MajorVersion);
                        if (plugin != null)
                        {
                            progress?.Report($"🛠️ Executing {plugin.Name}...");
                            string executeResult = plugin.Execute(_acadApp.ActiveDocument, netCmdSingle);
                            progress?.Report($"✅ {executeResult}\n");
                            return executeResult;
                        }
                        
                        progress?.Report($"⚠️ Unsupported Tool: {netCmdSingle}\n");
                        return $"WARNING Unrecognized or Unsupported NET command: {netCmdSingle}";
                    }
                    else if (!string.IsNullOrEmpty(sLisp) && _acadApp?.ActiveDocument != null)
                    {
                        progress?.Report($"🛠️ Sending Native Command...");
                        object? ignore = _acadApp!.ActiveDocument.SendCommand(sLisp + "\n");
                        progress?.Report($"✅ Completed Native String\n");
                        return $"Executed: {sLisp}";
                    }
                    else if (!string.IsNullOrEmpty(sCmd) && _acadApp?.ActiveDocument != null)
                    {
                         progress?.Report($"🛠️ Sending Command {sCmd}...");
                         object? ignore = _acadApp!.ActiveDocument.SendCommand(sCmd + "\n");
                         progress?.Report($"✅ Completed {sCmd}\n");
                         return $"Executed Command: {sCmd}";
                    }
                }
                return "Error: No valid command found in action.";
            }
            catch (Exception ex)
            {
                return $"Error executing action: {ex.Message}";
            }
        }

    }
}

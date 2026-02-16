using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BricsAI.Overlay.Services
{
    public class ComClient
    {
        private dynamic _acadApp;

        public bool IsConnected => _acadApp != null;

        // P/Invoke for GetActiveObject
        [DllImport("oleaut32.dll", PreserveSig = false)]
        private static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.IDispatch)] out object ppunk);

        private static object GetActiveObject(string progId)
        {
            try
            {
                Type t = Type.GetTypeFromProgID(progId);
                if (t == null) return null;
                
                Guid clsid = t.GUID;
                GetActiveObject(ref clsid, IntPtr.Zero, out object obj);
                return obj;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<string> SendCommandAsync(string command)
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

                if (command.StartsWith("NET:SELECT_LAYER:"))
                {
                    string layerName = command.Substring("NET:SELECT_LAYER:".Length).Trim();
                    return SelectObjectsOnLayer(_acadApp.ActiveDocument, layerName);
                }

                // Standard LISP command
                _acadApp.ActiveDocument.SendCommand(command + "\n");
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
                        return true;
                    }

                    // Try AutoCAD fallback
                    _acadApp = GetActiveObject("AutoCAD.Application");
                    if (_acadApp != null)
                    {
                        DetectVersion();
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
                string versionStr = _acadApp.Version;
                if (double.TryParse(versionStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v))
                {
                    MajorVersion = (int)v;
                }
            }
            catch
            {
                MajorVersion = 19; // Default to V19 if detection fails (modern safe)
            }
        }

        public async Task<string> ExecuteActionAsync(string actionJson)
        {
            if (_acadApp == null && !await ConnectAsync())
            {
                return "Error: Could not connect to BricsCAD.";
            }

            try
            {
                // Simple JSON parsing (avoiding full serializer overhead inside ComClient if possible, but we need it here)
                // Assuming format: {"command": "LAYERSPANELOPEN", "lisp_code": "..."}
                // or the user's schema: command_name, lisp_code, target_version
                
                using (var doc = System.Text.Json.JsonDocument.Parse(actionJson))
                {
                    var root = doc.RootElement;
                    
                    // Check for "tool_calls" array
                    if (root.TryGetProperty("tool_calls", out var tools) && tools.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var results = new List<string>();
                        int step = 1;

                        foreach (var tool in tools.EnumerateArray())
                        {
                            string lispCode = tool.TryGetProperty("lisp_code", out var lisp) ? lisp.GetString() : null;
                            string commandName = tool.TryGetProperty("command_name", out var cmd) ? cmd.GetString() : null;

                            if (!string.IsNullOrEmpty(lispCode))
                            {
                                _acadApp.ActiveDocument.SendCommand(lispCode + "\n");
                                results.Add($"Step {step++}: Executed LISP");
                            }
                            else if (!string.IsNullOrEmpty(commandName))
                            {
                                _acadApp.ActiveDocument.SendCommand(commandName + "\n");
                                results.Add($"Step {step++}: Executed {commandName}");
                            }
                        }
                        return string.Join("\n", results);
                    }
                    
                    // Fallback for direct object (legacy/single tool)
                    System.Text.Json.JsonElement singleTool = root;
                    string sLisp = singleTool.TryGetProperty("lisp_code", out var sl) ? sl.GetString() : null;
                    string sCmd = singleTool.TryGetProperty("command_name", out var sc) ? sc.GetString() : null;

                    if (!string.IsNullOrEmpty(sLisp))
                    {
                        _acadApp.ActiveDocument.SendCommand(sLisp + "\n");
                        return $"Executed: {sLisp}";
                    }
                    else if (!string.IsNullOrEmpty(sCmd))
                    {
                         _acadApp.ActiveDocument.SendCommand(sCmd + "\n");
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

        private string SelectObjectsOnLayer(dynamic doc, string layerName)
        {
            try
            {
                // We will use a selection set to find objects and select them
                var selectionSets = doc.SelectionSets;
                dynamic sset = null;

                try
                {
                    sset = selectionSets.Item("BricsAI_SelSet");
                    sset.Delete();
                }
                catch { }

                sset = selectionSets.Add("BricsAI_SelSet");

                // Filter for layer
                short[] filterTypes = new short[] { 8 }; // DXF code for Layer
                object[] filterData = new object[] { layerName };

                sset.Select(5, // acSelectionSetAll
                            Type.Missing, 
                            Type.Missing, 
                            filterTypes, 
                            filterData);

                if (sset.Count > 0)
                {
                    // Highlight the objects
                    sset.Highlight(true);
                    
                    // For selecting them as the "current selection", we'd ideally use 
                    // doc.SelectionSets.Add("CURRENT") or similar if supported, or use 
                    // SendCommand("SELECT P \n") after ensuring they are the Previous selection.
                    // But standard COM automation doesn't easily set the "Implied Selection" (Grips).
                    // Highlight is a good visual indicator.
                    
                    return $"Selected {sset.Count} objects on layer '{layerName}'. (Highlighted)";
                }
                
                return $"No objects found on layer '{layerName}'.";
            }
            catch (Exception ex)
            {
                return $"Error selecting layer: {ex.Message}";
            }
        }
    }
}

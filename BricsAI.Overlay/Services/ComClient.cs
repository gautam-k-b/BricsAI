using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BricsAI.Overlay.Services
{
    public class ComClient
    {
        private dynamic? _acadApp;

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

                if (command.StartsWith("NET:SELECT_LAYER:"))
                {
                    string layerName = command.Substring("NET:SELECT_LAYER:".Length).Trim();
                    return SelectObjectsOnLayer(_acadApp!.ActiveDocument, layerName, false);
                }
                
                if (command.StartsWith("NET:SELECT_OUTER:"))
                {
                    string layerName = command.Substring("NET:SELECT_OUTER:".Length).Trim();
                    return SelectObjectsOnLayer(_acadApp!.ActiveDocument, layerName, false, "outer");
                }
                if (command.StartsWith("NET:SELECT_INNER:"))
                {
                    string layerName = command.Substring("NET:SELECT_INNER:".Length).Trim();
                    return SelectObjectsOnLayer(_acadApp!.ActiveDocument, layerName, false, "inner");
                }
                
                if (command.StartsWith("NET:SELECT_BOOTH_BOXES")) return SelectGeometricFeatures(_acadApp!.ActiveDocument, "booths");
                if (command.StartsWith("NET:SELECT_BUILDING_LINES")) return SelectGeometricFeatures(_acadApp!.ActiveDocument, "building");
                if (command.StartsWith("NET:SELECT_COLUMNS")) return SelectGeometricFeatures(_acadApp!.ActiveDocument, "columns");
                if (command.StartsWith("NET:SELECT_UTILITIES")) return SelectGeometricFeatures(_acadApp!.ActiveDocument, "utilities");

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
                string? versionStr = _acadApp?.Version;
                if (versionStr != null && double.TryParse(versionStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v))
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
                            string? lispCode = tool.TryGetProperty("lisp_code", out var lisp) ? lisp.GetString() : null;
                            string? commandName = tool.TryGetProperty("command_name", out var cmd) ? cmd.GetString() : null;

                            string netCmd = "";
                            if (!string.IsNullOrEmpty(lispCode) && lispCode!.Contains("NET:")) 
                                netCmd = lispCode.Substring(lispCode.IndexOf("NET:")).TrimEnd(')', ' ', '\n', '\r');
                            else if (!string.IsNullOrEmpty(commandName) && commandName!.Contains("NET:")) 
                                netCmd = commandName.Substring(commandName.IndexOf("NET:")).TrimEnd(')', ' ', '\n', '\r');

                            if (!string.IsNullOrEmpty(netCmd) && _acadApp?.ActiveDocument != null)
                            {
                                if (netCmd.StartsWith("NET:SELECT_LAYER:"))
                                {
                                    string arg = netCmd.Substring("NET:SELECT_LAYER:".Length).Trim();
                                    var layerParts = arg.Split(':');
                                    string layerName = layerParts[0].Trim();
                                    string? targetLayer = layerParts.Length > 1 ? layerParts[1].Trim() : null;
                                    string res = SelectObjectsOnLayer(_acadApp!.ActiveDocument, layerName, false, "all", targetLayer);
                                    results.Add($"Step {step++}: {res}");
                                }
                                else if (netCmd.StartsWith("NET:SELECT_OUTER:"))
                                {
                                    string layerName = netCmd.Substring("NET:SELECT_OUTER:".Length).Trim();
                                    string res = SelectObjectsOnLayer(_acadApp!.ActiveDocument, layerName, false, "outer");
                                    results.Add($"Step {step++}: {res}");
                                }
                                else if (netCmd.StartsWith("NET:SELECT_INNER:"))
                                {
                                    string layerName = netCmd.Substring("NET:SELECT_INNER:".Length).Trim();
                                    string res = SelectObjectsOnLayer(_acadApp!.ActiveDocument, layerName, false, "inner");
                                    results.Add($"Step {step++}: {res}");
                                }
                                else if (netCmd.StartsWith("NET:MESSAGE:"))
                                {
                                    string msg = netCmd.Substring("NET:MESSAGE:".Length).Trim();
                                    results.Add($"MESSAGE: {msg}");
                                }
                                else if (netCmd.StartsWith("NET:GET_LAYERS:"))
                                {
                                    string res = GetAllLayers(_acadApp!.ActiveDocument);
                                    results.Add($"Step {step++}: {res}");
                                }
                                else if (netCmd.StartsWith("NET:SELECT_BOOTH_BOXES")) results.Add($"Step {step++}: " + SelectGeometricFeatures(_acadApp!.ActiveDocument, "booths", ExtractTarget(netCmd)));
                                else if (netCmd.StartsWith("NET:SELECT_BUILDING_LINES")) results.Add($"Step {step++}: " + SelectGeometricFeatures(_acadApp!.ActiveDocument, "building", ExtractTarget(netCmd)));
                                else if (netCmd.StartsWith("NET:SELECT_COLUMNS")) results.Add($"Step {step++}: " + SelectGeometricFeatures(_acadApp!.ActiveDocument, "columns", ExtractTarget(netCmd)));
                                else if (netCmd.StartsWith("NET:SELECT_UTILITIES")) results.Add($"Step {step++}: " + SelectGeometricFeatures(_acadApp!.ActiveDocument, "utilities", ExtractTarget(netCmd)));
                                else results.Add($"Step {step++}: WARNING Unrecognized NET command: {netCmd}");
                            }
                            else if (!string.IsNullOrEmpty(lispCode) && _acadApp?.ActiveDocument != null)
                            {
                                object? ignore = _acadApp!.ActiveDocument.SendCommand(lispCode + "\n");
                                results.Add($"Step {step++}: Executed LISP [{lispCode}]");
                            }
                            else if (!string.IsNullOrEmpty(commandName) && _acadApp?.ActiveDocument != null)
                            {
                                object? ignore = _acadApp!.ActiveDocument.SendCommand(commandName + "\n");
                                results.Add($"Step {step++}: Executed {commandName}");
                            }
                        }
                        return string.Join("\n", results);
                    }
                    
                    // Fallback for direct object (legacy/single tool)
                    System.Text.Json.JsonElement singleTool = root;
                    string? sLisp = singleTool.TryGetProperty("lisp_code", out var sl) ? sl.GetString() : null;
                    string? sCmd = singleTool.TryGetProperty("command_name", out var sc) ? sc.GetString() : null;

                    string netCmdSingle = "";
                    if (!string.IsNullOrEmpty(sLisp) && sLisp!.Contains("NET:")) 
                        netCmdSingle = sLisp.Substring(sLisp.IndexOf("NET:")).TrimEnd(')', ' ', '\n', '\r');
                    else if (!string.IsNullOrEmpty(sCmd) && sCmd!.Contains("NET:")) 
                        netCmdSingle = sCmd.Substring(sCmd.IndexOf("NET:")).TrimEnd(')', ' ', '\n', '\r');

                    if (!string.IsNullOrEmpty(netCmdSingle) && _acadApp?.ActiveDocument != null)
                    {
                        if (netCmdSingle.StartsWith("NET:SELECT_LAYER:"))
                        {
                            string arg = netCmdSingle.Substring("NET:SELECT_LAYER:".Length).Trim();
                            var layerParts = arg.Split(':');
                            string layerName = layerParts[0].Trim();
                            string? targetLayer = layerParts.Length > 1 ? layerParts[1].Trim() : null;
                            return SelectObjectsOnLayer(_acadApp!.ActiveDocument, layerName, false, "all", targetLayer);
                        }
                        if (netCmdSingle.StartsWith("NET:SELECT_OUTER:"))
                        {
                            string layerName = netCmdSingle.Substring("NET:SELECT_OUTER:".Length).Trim();
                            return SelectObjectsOnLayer(_acadApp!.ActiveDocument, layerName, false, "outer");
                        }
                        if (netCmdSingle.StartsWith("NET:SELECT_INNER:"))
                        {
                            string layerName = netCmdSingle.Substring("NET:SELECT_INNER:".Length).Trim();
                            return SelectObjectsOnLayer(_acadApp!.ActiveDocument, layerName, false, "inner");
                        }
                        if (netCmdSingle.StartsWith("NET:MESSAGE:"))
                        {
                            return netCmdSingle.Substring("NET:MESSAGE:".Length).Trim();
                        }
                        if (netCmdSingle.StartsWith("NET:GET_LAYERS:"))
                        {
                            return GetAllLayers(_acadApp!.ActiveDocument);
                        }
                        if (netCmdSingle.StartsWith("NET:SELECT_BOOTH_BOXES")) return SelectGeometricFeatures(_acadApp!.ActiveDocument, "booths", ExtractTarget(netCmdSingle));
                        if (netCmdSingle.StartsWith("NET:SELECT_BUILDING_LINES")) return SelectGeometricFeatures(_acadApp!.ActiveDocument, "building", ExtractTarget(netCmdSingle));
                        if (netCmdSingle.StartsWith("NET:SELECT_COLUMNS")) return SelectGeometricFeatures(_acadApp!.ActiveDocument, "columns", ExtractTarget(netCmdSingle));
                        if (netCmdSingle.StartsWith("NET:SELECT_UTILITIES")) return SelectGeometricFeatures(_acadApp!.ActiveDocument, "utilities", ExtractTarget(netCmdSingle));
                        
                        return $"WARNING Unrecognized NET command: {netCmdSingle}";
                    }
                    else if (!string.IsNullOrEmpty(sLisp) && _acadApp?.ActiveDocument != null)
                    {
                        object? ignore = _acadApp!.ActiveDocument.SendCommand(sLisp + "\n");
                        return $"Executed: {sLisp}";
                    }
                    else if (!string.IsNullOrEmpty(sCmd) && _acadApp?.ActiveDocument != null)
                    {
                         object? ignore = _acadApp!.ActiveDocument.SendCommand(sCmd + "\n");
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

        private string GetAllLayers(dynamic doc)
        {
            try
            {
                var layers = doc?.Layers;
                if (layers == null) return "Error: Could not access Layers.";
                
                var layerNames = new List<string>();
                for (int i = 0; i < layers.Count; i++)
                {
                    layerNames.Add((string)layers.Item(i).Name);
                }
                
                return $"Layers found: {string.Join(", ", layerNames)}";
            }
            catch (Exception ex)
            {
                return $"Error getting layers: {ex.Message}";
            }
        }

        private string? ExtractTarget(string cmd)
        {
            var parts = cmd.Split(':');
            return parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2].Trim() : null;
        }

        private string SelectGeometricFeatures(dynamic doc, string featureType, string? targetLayer = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(targetLayer)) { try { doc!.Layers.Add(targetLayer); } catch { } }
                var selectionSets = doc?.SelectionSets;
                if (selectionSets == null) return "Error: Could not access SelectionSets.";
                dynamic? sset = null;
                try { sset = selectionSets.Item("BricsAI_GeoSel"); sset.Delete(); } catch { }
                sset = selectionSets.Add("BricsAI_GeoSel");

                if (featureType == "booths")
                {
                    short[] filterTypes = new short[] { 0 }; 
                    object[] filterData = new object[] { "LWPOLYLINE,POLYLINE" };
                    sset.Select(5, Type.Missing, Type.Missing, filterTypes, filterData);
                    
                    var validObjs = new List<dynamic>();
                    for (int i = 0; i < sset.Count; i++)
                    {
                        var obj = sset.Item(i);
                        try
                        {
                            if (obj.Closed)
                            {
                                double area = obj.Area;
                                if (area >= 90 && area <= 150)
                                {
                                    validObjs.Add(obj);
                                    if (!string.IsNullOrEmpty(targetLayer)) { try { obj.Layer = targetLayer; } catch { } }
                                    else { obj.Highlight(true); }
                                }
                            }
                        }
                        catch { }
                    }
                    if (validObjs.Count > 0)
                    {
                        try { doc!.SendCommand("PICKFIRST 1\n"); } catch { }
                        return $"Selected {validObjs.Count} booth boxes.";
                    }
                    return "No booth boxes found.";
                }
                else if (featureType == "building")
                {
                    short[] filterTypes = new short[] { 0 }; 
                    object[] filterData = new object[] { "LWPOLYLINE,POLYLINE,LINE" };
                    sset.Select(5, Type.Missing, Type.Missing, filterTypes, filterData);

                    double maxArea = -1;
                    dynamic? largestObj = null;

                    for (int i = 0; i < sset.Count; i++)
                    {
                        var obj = sset.Item(i);
                        try
                        {
                            obj.GetBoundingBox(out object minPt, out object maxPt);
                            double[] min = (double[])minPt;
                            double[] max = (double[])maxPt;
                            double width = Math.Abs(max[0] - min[0]);
                            double height = Math.Abs(max[1] - min[1]);
                            double area = width * height;

                            if (area > maxArea)
                            {
                                maxArea = area;
                                largestObj = obj;
                            }
                        }
                        catch { }
                    }

                    if (largestObj != null)
                    {
                        if (!string.IsNullOrEmpty(targetLayer)) { try { largestObj.Layer = targetLayer; } catch { } }
                        else { largestObj.Highlight(true); }
                        return $"Selected outer building outline." + (!string.IsNullOrEmpty(targetLayer) ? $" -> Moved to {targetLayer}" : "");
                    }
                    return "No building outline found.";
                }
                else if (featureType == "columns")
                {
                    short[] filterTypes = new short[] { 0 }; 
                    object[] filterData = new object[] { "CIRCLE,INSERT" };
                    sset.Select(5, Type.Missing, Type.Missing, filterTypes, filterData);
                    
                    var validObjs = new List<dynamic>();
                    for (int i = 0; i < sset.Count; i++)
                    {
                        var obj = sset.Item(i);
                        try
                        {
                            obj.GetBoundingBox(out object minPt, out object maxPt);
                            double[] min = (double[])minPt;
                            double[] max = (double[])maxPt;
                            double width = Math.Abs(max[0] - min[0]);
                            double height = Math.Abs(max[1] - min[1]);
                            double area = width * height;
                            
                            if (area > 0 && area < 50) 
                            {
                                validObjs.Add(obj);
                                if (!string.IsNullOrEmpty(targetLayer)) { try { obj.Layer = targetLayer; } catch { } }
                                else { obj.Highlight(true); }
                            }
                        }
                        catch { }
                    }

                    if (validObjs.Count > 0)
                    {
                        return $"Selected {validObjs.Count} columns.";
                    }
                    return "No columns found.";
                }
                else if (featureType == "utilities")
                {
                    short[] filterTypes = new short[] { 0 }; 
                    object[] filterData = new object[] { "HATCH,INSERT" }; 
                    sset.Select(5, Type.Missing, Type.Missing, filterTypes, filterData);
                    
                    int count = 0;
                    for (int i = 0; i < sset.Count; i++)
                    {
                        var obj = sset.Item(i);
                        if (!string.IsNullOrEmpty(targetLayer)) { try { obj.Layer = targetLayer; } catch { } }
                        else { obj.Highlight(true); }
                        count++;
                    }
                    
                    if (count > 0)
                    {
                        return $"Selected {count} utility hatches/symbols.";
                    }
                    return "No utilities found.";
                }

                return "Unknown geometric feature type.";
            }
            catch (Exception ex)
            {
                return $"Error selecting geometric features: {ex.Message}";
            }
        }

        private string SelectObjectsOnLayer(dynamic doc, string layerName, bool exclusive = false, string mode = "all", string? targetLayer = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(targetLayer)) { try { doc!.Layers.Add(targetLayer); } catch { } }

                // We will use a selection set to find objects and select them
                var selectionSets = doc?.SelectionSets;
                if (selectionSets == null) return $"Error: Could not access SelectionSets.";
                dynamic? sset = null;

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
                    if (mode == "all") 
                    {
                        if (!string.IsNullOrEmpty(targetLayer))
                        {
                            for (int i = 0; i < sset.Count; i++)
                            {
                                try { sset.Item(i).Layer = targetLayer; } catch { }
                            }
                            return $"Moved {sset.Count} objects from '{layerName}' to '{targetLayer}'.";
                        }
                        else
                        {
                            sset.Highlight(true);
                            return $"Selected {sset.Count} objects on layer '{layerName}'. (Highlighted)";
                        }
                    }

                    // Geometric Inner/Outer Logic
                    double maxArea = -1;
                    dynamic? largestObj = null;
                    var smallerObjs = new List<dynamic>();

                    for (int i = 0; i < sset.Count; i++)
                    {
                        var obj = sset.Item(i);
                        try
                        {
                            obj.GetBoundingBox(out object minPt, out object maxPt);
                            double[] min = (double[])minPt;
                            double[] max = (double[])maxPt;
                            double width = Math.Abs(max[0] - min[0]);
                            double height = Math.Abs(max[1] - min[1]);
                            double area = width * height;

                            if (area > maxArea)
                            {
                                if (largestObj != null) smallerObjs.Add(largestObj);
                                maxArea = area;
                                largestObj = obj;
                            }
                            else
                            {
                                smallerObjs.Add(obj);
                            }
                        }
                        catch { }
                    }

                    // Deselect everything in the set first, then selectively highlight
                    sset.Highlight(false); 

                    if (mode == "outer" && largestObj != null)
                    {
                        // Add only the largest obj into a new set, or just manually highlight it
                        largestObj!.Highlight(true);
                        return $"Selected outer box (largest bounds) on layer '{layerName}'.";
                    }
                    else if (mode == "inner" && smallerObjs.Count > 0)
                    {
                        foreach (var innerObj in smallerObjs)
                        {
                            innerObj.Highlight(true);
                        }
                        return $"Selected {smallerObjs.Count} inner objects on layer '{layerName}'.";
                    }
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

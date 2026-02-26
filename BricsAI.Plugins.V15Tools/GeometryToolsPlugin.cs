using System;
using System.Collections.Generic;
using BricsAI.Core;

namespace BricsAI.Plugins.V15Tools
{
    public class GeometryToolsPlugin : IToolPlugin
    {
        public string Name => "Geometric Feature Selection & Preparation";
        public string Description => "Handles complex geometry evaluation like bounding boxes, columns, utilities, and whitelist explosions.";
        public int TargetVersion => 15;

        public string GetPromptExample()
        {
            return "User: 'Select the booth outlines'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"SELECT_BOOTH_BOXES\", \"lisp_code\": \"NET:SELECT_BOOTH_BOXES:Expo_BoothOutline\" }] }\n\n" +
                   "User: 'Prepare the geometry'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"PREPARE_GEOMETRY\", \"lisp_code\": \"NET:PREPARE_GEOMETRY\" }] }";
        }

        public bool CanExecute(string netCommandName)
        {
            if (netCommandName == null) return false;
            return netCommandName.StartsWith("NET:SELECT_BOOTH_BOXES") ||
                   netCommandName.StartsWith("NET:SELECT_BUILDING_LINES") ||
                   netCommandName.StartsWith("NET:SELECT_COLUMNS") ||
                   netCommandName.StartsWith("NET:SELECT_UTILITIES") ||
                   netCommandName.StartsWith("NET:PREPARE_GEOMETRY");
        }

        public string Execute(dynamic doc, string netCmd)
        {
            if (netCmd.StartsWith("NET:SELECT_BOOTH_BOXES")) return SelectGeometricFeatures(doc, "booths", ExtractTarget(netCmd));
            if (netCmd.StartsWith("NET:SELECT_BUILDING_LINES")) return SelectGeometricFeatures(doc, "building", ExtractTarget(netCmd));
            if (netCmd.StartsWith("NET:SELECT_COLUMNS")) return SelectGeometricFeatures(doc, "columns", ExtractTarget(netCmd));
            if (netCmd.StartsWith("NET:SELECT_UTILITIES")) return SelectGeometricFeatures(doc, "utilities", ExtractTarget(netCmd));
            if (netCmd.StartsWith("NET:PREPARE_GEOMETRY")) return PrepareGeometry(doc);
            
            return "Error: Command not explicitly handled in GeometryToolsPlugin.";
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
                    object[] filterData = new object[] { "HATCH" }; 
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

        private string PrepareGeometry(dynamic doc)
        {
            try
            {
                // 0. The ultimate safeguard: ALWAYS unlock every single layer in the entire drawing first.
                // If the vendor locked their layers before sending the file, the native EXPLODE / ERASE commands 
                // will completely ignore them even when highlighted! By opening the floodgates here, we guarantee
                // all geometric primitives and nested vendor blocks are exposed to the whitelist iterator.
                doc.SendCommand("(command \"-LAYER\" \"UNLOCK\" \"*\" \"\")\n");

                // 1. Lock booth layers natively (targets)
                try { doc.Layers.Item("Expo_BoothNumber").Lock = true; } catch { }
                try { doc.Layers.Item("Expo_BoothOutline").Lock = true; } catch { }

                // 1b. Lock mapped vendor sources to protect them from explosion and deletion
                try
                {
                    string mappingPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "layer_mappings.json");
                    if (System.IO.File.Exists(mappingPath))
                    {
                        string json = System.IO.File.ReadAllText(mappingPath);
                        var mappings = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(json);
                        if (mappings != null)
                        {
                            foreach (var kvp in mappings)
                            {
                                if (kvp.Value.Equals("Expo_BoothOutline", System.StringComparison.OrdinalIgnoreCase) ||
                                    kvp.Value.Equals("Expo_BoothNumber", System.StringComparison.OrdinalIgnoreCase))
                                {
                                    try { doc.Layers.Item(kvp.Key).Lock = true; } catch { }
                                }
                            }
                        }
                    }
                }
                catch { }

                doc.SendCommand("(setvar \"PICKFIRST\" 1)\n");

                // 2a. Flatten splines precisely like human
                try
                {
                    string sName = "BA_Spline_" + System.Guid.NewGuid().ToString("N").Substring(0, 10);
                    var ssetSplines = doc.SelectionSets.Add(sName);
                    ssetSplines.Select(5, Type.Missing, Type.Missing, new short[] { 0 }, new object[] { "SPLINE" });
                    if (ssetSplines.Count > 0)
                    {
                        doc.SendCommand("(if (setq ss (ssget \"_X\" '((0 . \"SPLINE\")))) (sssetfirst nil ss))\n");
                        doc.SendCommand("FLATTEN\n\n\n"); // Extra enter to clear any hidden lines dialogs
                    }
                    try { ssetSplines.Delete(); } catch { }
                } catch { }

                doc.SendCommand("(setvar \"QAFLAGS\" 1)\n");

                // 2b. Global Exhaustive Explosion: Explode everything NOT in the whitelist recursively until resolved
                // Whitelist: Arc, Line, Circle, Ellipse, modern 2D Polyline, Text, Solid
                // Note: We intentionally omit legacy 'POLYLINE' from this whitelist. BricsCAD structures Polyface Meshes and Polygon Meshes 
                // as legacy 'POLYLINE' entities. By removing the immunity shield from legacy polylines, we force the global extraction 
                // loop to ruthlessly explode 3D Meshes into 3DFaces, which are then caught and ERASED in the cleanup step.
                string whitelistFilter = "'((-4 . \"<NOT\") (-4 . \"<OR\") (0 . \"ARC\") (0 . \"LINE\") (0 . \"CIRCLE\") (0 . \"ELLIPSE\") (0 . \"LWPOLYLINE\") (0 . \"TEXT\") (0 . \"SOLID\") (-4 . \"OR>\") (-4 . \"NOT>\"))";
                short[] wType = new short[] { -4, -4, 0, 0, 0, 0, 0, 0, 0, -4, -4 };
                object[] wData = new object[] { "<NOT", "<OR", "ARC", "LINE", "CIRCLE", "ELLIPSE", "LWPOLYLINE", "TEXT", "SOLID", "OR>", "NOT>" };

                int maxPasses = 30; // High limit to ensure deeply nested blocks are fully pulverized
                int passCount = 0;
                int previousNonStandardCount = -1;
                int identicalCountLoops = 0;

                while (passCount < maxPasses)
                {
                    passCount++;
                    int currentNonStandardCount = 0;
                    try
                    {
                        string eName = "BA_GlobalExp_" + System.Guid.NewGuid().ToString("N").Substring(0, 10);
                        var ssetAll = doc.SelectionSets.Add(eName);
                        ssetAll.Select(5, Type.Missing, Type.Missing, wType, wData);
                        currentNonStandardCount = ssetAll.Count;

                        if (currentNonStandardCount > 0)
                        {
                            if (currentNonStandardCount == previousNonStandardCount)
                            {
                                identicalCountLoops++;
                                if (identicalCountLoops >= 2) 
                                {
                                    // The remaining items are unexplodable. Break to erase them.
                                    try { ssetAll.Delete(); } catch { }
                                    break; 
                                }
                            }
                            else
                            {
                                identicalCountLoops = 0;
                            }

                            previousNonStandardCount = currentNonStandardCount;
                            
                            doc.SendCommand($"(if (setq ss (ssget \"_X\" {whitelistFilter})) (sssetfirst nil ss))\n");
                            doc.SendCommand("_.EXPLODE\n");
                            System.Threading.Thread.Sleep(500); // Allow COM queue to catch up
                        }
                        else
                        {
                            try { ssetAll.Delete(); } catch { }
                            break; // 0 non-standard items remaining! Perfect geometry achieved.
                        }
                        try { ssetAll.Delete(); } catch { }
                    }
                    catch { break; }
                }

                doc.SendCommand("(setvar \"QAFLAGS\" 0)\n");

                int erasedCount = 0;
                // 2c. ERASE unresolvable structures outside the whitelist
                try
                {
                    string delName = "BA_Del_" + System.Guid.NewGuid().ToString("N").Substring(0, 10);
                    var ssetDel = doc.SelectionSets.Add(delName);
                    ssetDel.Select(5, Type.Missing, Type.Missing, wType, wData);
                    erasedCount = ssetDel.Count;
                    if (erasedCount > 0)
                    {
                        doc.SendCommand($"(if (setq ss (ssget \"_X\" {whitelistFilter})) (sssetfirst nil ss))\n");
                        doc.SendCommand("_.ERASE\n");
                        System.Threading.Thread.Sleep(300);
                    }
                    try { ssetDel.Delete(); } catch { }
                } catch { }

                // Removed duplicate ERASE block
                
                doc.SendCommand("(setvar \"QAFLAGS\" 0)\n");

                return $"Geometry Prepared Natively: Executed {passCount} global wipe cycles to explode complex entities recursively, and finalized by erasing {erasedCount} unresolvable objects.";
            }
            catch (Exception ex)
            {
                return $"Error preparing geometry: {ex.Message}";
            }
        }
    }
}

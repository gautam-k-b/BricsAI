using System;
using System.Collections.Generic;
using BricsAI.Core;

namespace BricsAI.Plugins.V15Tools
{
    public class LayerToolsPlugin : IToolPlugin
    {
        public string Name => "Advanced Layer Manipulations";
        public string Description => "Handles complex dynamic layer mapping, filtering, fast renaming, and deep deletion logic.";
        public int TargetVersion => 15;

        public string GetPromptExample()
        {
            return "User: 'Apply the layer mappings from JSON.'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"APPLY_LAYER_MAPPINGS\", \"lisp_code\": \"NET:APPLY_LAYER_MAPPINGS\" }] }\n\n" +
                   "User: 'Delete layers starting with prefix Deleted_'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"DELETE_LAYERS_BY_PREFIX\", \"lisp_code\": \"NET:DELETE_LAYERS_BY_PREFIX:Deleted_\" }] }";
        }

        public bool CanExecute(string netCommandName)
        {
            if (netCommandName == null) return false;
            return netCommandName.StartsWith("NET:SELECT_LAYER") ||
                   netCommandName.StartsWith("NET:SELECT_OUTER") ||
                   netCommandName.StartsWith("NET:SELECT_INNER") ||
                   netCommandName.StartsWith("NET:GET_LAYERS") ||
                   netCommandName.StartsWith("NET:APPLY_LAYER_MAPPINGS") ||
                   netCommandName.StartsWith("NET:RENAME_DELETED_LAYERS") ||
                   netCommandName.StartsWith("NET:DELETE_LAYERS_BY_PREFIX") ||
                   netCommandName.StartsWith("NET:LOCK_BOOTH_LAYERS") ||
                   netCommandName.StartsWith("NET:LEARN_LAYER_MAPPING");
        }

        public string Execute(dynamic doc, string netCmd)
        {
            if (netCmd.StartsWith("NET:SELECT_LAYER:"))
            {
                string arg = netCmd.Substring("NET:SELECT_LAYER:".Length).Trim();
                var layerParts = arg.Split(':');
                string layerName = layerParts[0].Trim();
                string? targetLayer = layerParts.Length > 1 ? layerParts[1].Trim() : null;
                return SelectObjectsOnLayer(doc, layerName, false, "all", targetLayer);
            }
            if (netCmd.StartsWith("NET:SELECT_OUTER:"))
            {
                string layerName = netCmd.Substring("NET:SELECT_OUTER:".Length).Trim();
                return SelectObjectsOnLayer(doc, layerName, false, "outer");
            }
            if (netCmd.StartsWith("NET:SELECT_INNER:"))
            {
                string layerName = netCmd.Substring("NET:SELECT_INNER:".Length).Trim();
                return SelectObjectsOnLayer(doc, layerName, false, "inner");
            }
            if (netCmd.StartsWith("NET:GET_LAYERS:")) return GetAllLayers(doc);
            if (netCmd.StartsWith("NET:APPLY_LAYER_MAPPINGS")) return ApplyLayerMappings(doc);
            if (netCmd.StartsWith("NET:RENAME_DELETED_LAYERS")) return RenameDeletedLayers(doc);
            if (netCmd.StartsWith("NET:DELETE_LAYERS_BY_PREFIX:")) return DeleteLayersByPrefix(doc, ExtractTarget(netCmd));
            if (netCmd.StartsWith("NET:LOCK_BOOTH_LAYERS")) return LockBoothLayers(doc);
            if (netCmd.StartsWith("NET:LEARN_LAYER_MAPPING:")) return LearnLayerMapping(netCmd);
            
            return "Error: Command not explicitly handled in LayerToolsPlugin.";
        }

        private string LearnLayerMapping(string netCmd)
        {
            try
            {
                string mappingData = netCmd.Substring("NET:LEARN_LAYER_MAPPING:".Length).Trim();
                var layerParts = mappingData.Split(':');
                if (layerParts.Length != 2) return "Error: Invalid learn layer mapping format. Expected NET:LEARN_LAYER_MAPPING:Layer1:Layer2";
                
                string sourceLayer = layerParts[0].Trim();
                string targetLayer = layerParts[1].Trim();
                
                string mappingPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "layer_mappings.json");
                Dictionary<string, string> mappings = new Dictionary<string, string>();
                
                if (System.IO.File.Exists(mappingPath))
                {
                    string jsonInfo = System.IO.File.ReadAllText(mappingPath);
                    var existing = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(jsonInfo);
                    if (existing != null) mappings = existing;
                }
                
                mappings[sourceLayer] = targetLayer;
                
                System.IO.File.WriteAllText(mappingPath, System.Text.Json.JsonSerializer.Serialize(mappings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                
                return $"Learned mapping: {sourceLayer} -> {targetLayer}";
            }
            catch (System.Exception ex)
            {
                return $"Error learning layer mapping: {ex.Message}";
            }
        }

        private string? ExtractTarget(string cmd)
        {
            var parts = cmd.Split(':');
            return parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2].Trim() : null;
        }

        private string SelectObjectsOnLayer(dynamic doc, string layerName, bool exclusive = false, string mode = "all", string? targetLayer = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(targetLayer)) { try { doc!.Layers.Add(targetLayer); } catch { } }

                var selectionSets = doc?.SelectionSets;
                if (selectionSets == null) return $"Error: Could not access SelectionSets.";
                dynamic? sset = null;

                try { sset = selectionSets.Item("BricsAI_SelSet"); sset.Delete(); } catch { }
                sset = selectionSets.Add("BricsAI_SelSet");

                short[] filterTypes = new short[] { 8 }; // DXF code for Layer
                object[] filterData = new object[] { layerName };

                sset.Select(5, Type.Missing, Type.Missing, filterTypes, filterData);

                if (sset.Count > 0)
                {
                    if (mode == "all") 
                    {
                        if (!string.IsNullOrEmpty(targetLayer))
                        {
                            try { doc.SendCommand($"(command \"_.CHPROP\" (ssget \"_X\" '((8 . \"{layerName}\"))) \"\" \"_LA\" \"{targetLayer}\" \"\")\n"); } catch { }
                            return $"Moved matching objects from '{layerName}' to '{targetLayer}'.";
                        }
                        else
                        {
                            sset.Highlight(true);
                            return $"Selected {sset.Count} objects on layer '{layerName}'. (Highlighted)";
                        }
                    }

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

                    sset.Highlight(false); 

                    if (mode == "outer" && largestObj != null)
                    {
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

        private string ApplyLayerMappings(dynamic doc)
        {
            try
            {
                string mappingPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "layer_mappings.json");
                if (!System.IO.File.Exists(mappingPath)) return "Error: layer_mappings.json not found.";
                string json = System.IO.File.ReadAllText(mappingPath);
                var mappings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (mappings == null) return "Error: Invalid layer mappings format.";

                System.Text.StringBuilder lispMacro = new System.Text.StringBuilder();

                foreach (var kvp in mappings)
                {
                    try { doc!.Layers.Add(kvp.Value); } catch { } // Ensure target exists
                    lispMacro.AppendLine($"(if (setq ss (ssget \"_X\" '((8 . \"{kvp.Key}\")))) (command \"_.CHPROP\" ss \"\" \"_LA\" \"{kvp.Value}\" \"\"))");
                }

                if (lispMacro.Length > 0)
                {
                    string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "_BricsAI_Mappings.lsp");
                    System.IO.File.WriteAllText(tempPath, lispMacro.ToString());
                    string lispPath = tempPath.Replace("\\", "/");
                    doc.SendCommand($"(load \"{lispPath}\")\n");
                }

                return $"Applied mappings natively with a single batch execution via LISP script load.";
            }
            catch (Exception ex)
            {
                return $"Error applying mappings: {ex.Message}";
            }
        }

        private string RenameDeletedLayers(dynamic doc)
        {
            try
            {
                var layers = doc?.Layers;
                if (layers == null) return "Error: Could not access Layers.";
                
                var allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "0", "Defpoints", 
                    "Expo_BoothNumber", "Expo_BoothOutline", 
                    "Expo_MaxBoothNumber", "Expo_MaxBoothOutline", 
                    "Expo_Building", "Expo_Column", 
                    "Expo_Markings", "Expo_NES", "Expo_View2"
                };

                int renameCount = 0;
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers.Item(i);
                    string name = layer.Name;

                    if (!allowList.Contains(name) && !name.StartsWith("Deleted_", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            layer.Name = "Deleted_" + name;
                            renameCount++;
                        }
                        catch { }
                    }
                }
                return $"Found and renamed {renameCount} non-standard layers with 'Deleted_' prefix.";
            }
            catch (Exception ex)
            {
                return $"Error renaming layers: {ex.Message}";
            }
        }

        private string DeleteLayersByPrefix(dynamic doc, string prefix)
        {
            try
            {
                var layers = doc?.Layers;
                if (layers == null) return "Error: Could not access Layers.";

                // 1. Switch active layer to 0 (cannot purge the current layer)
                doc.SendCommand("(setvar \"CLAYER\" \"0\")\n");
                int targetLayerCount = 0;
                int unlockedCount = 0;
                // 2. Aggressively strip all protections (Lock, Freeze, Off) natively via COM
                for (int i = 0; i < layers.Count; i++)
                {
                    try
                    {
                        var layer = layers.Item(i);
                        string name = layer.Name;

                        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            targetLayerCount++;
                            layer.Lock = false;
                            layer.Freeze = false;
                            layer.LayerOn = true;
                            unlockedCount++;
                        }
                    }
                    catch { } // Ignore if a specific layer throws COM error
                }

                if (targetLayerCount == 0)
                {
                    return $"Found 0 layers starting with '{prefix}'. No deletion necessary.";
                }
                // 3. Command line ERASE with wildcard (now guaranteed to hit everything in active spaces)
                doc.SendCommand($"(if (setq ss (ssget \"_X\" '((8 . \"{prefix}*\")))) (command \"_.ERASE\" ss \"\"))\n");

                // 4. Ultra-Fast In-Process Visual LISP Block Traversal to vaporize nested entities
                string vlisp = "(vl-load-com)(setq blks (vla-get-blocks (vla-get-activedocument (vlax-get-acad-object))))" +
                               "(vlax-for blk blks (if (= (vla-get-isxref blk) :vlax-false) " +
                               "(vlax-for ent blk (if (wcmatch (strcase (vla-get-layer ent)) (strcase \"" + prefix + "*\")) " +
                               "(vl-catch-all-apply 'vla-delete (list ent))))))";
                doc.SendCommand($"{vlisp}\n");

                // 5. Purge Empty Block Definitions / Dependent Dictionaries (Requires 2 passes for nested orphans)
                doc.SendCommand("(command \"-PURGE\" \"All\" \"*\" \"N\")\n");
                doc.SendCommand("(command \"-PURGE\" \"All\" \"*\" \"N\")\n");

                // 6. Purge the strictly targeted empty layers
                doc.SendCommand($"(command \"-PURGE\" \"LA\" \"{prefix}*\" \"N\")\n");

                return $"Unlocked {unlockedCount} '{prefix}' layers natively, erased active spaces, executed ultra-fast deep block vaporization, and double-purged.";
            }
            catch (Exception ex)
            {
                return $"Error deleting layers by prefix: {ex.Message}";
            }
        }

        private string LockBoothLayers(dynamic doc)
        {
            try
            {
                try { doc.Layers.Item("Expo_BoothNumber").Lock = true; } catch { }
                try { doc.Layers.Item("Expo_BoothOutline").Lock = true; } catch { }
                return "Locked Expo_BoothNumber and Expo_BoothOutline via COM.";
            }
            catch (Exception ex)
            {
                return $"Error locking layers: {ex.Message}";
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
    }
}

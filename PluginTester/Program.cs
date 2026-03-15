using System;
using System.IO;
using System.Linq;
using System.Reflection;

class Program
{
    static void Main()
    {
        string dir = @"f:\Projects\BricsAI\BricsAI.Overlay\bin\Debug\net9.0-windows\";
        var files = Directory.GetFiles(dir, "BricsAI.Plugins*.dll");
        Console.WriteLine("Found Plugin DLLs: " + files.Length);
        
        foreach (var file in files)
        {
            Console.WriteLine($"Loading {Path.GetFileName(file)}...");
            var asm = Assembly.LoadFrom(file);
            var types = asm.GetTypes().Where(t => t.GetInterface("IToolPlugin") != null);
            foreach (var t in types)
            {
                var inst = Activator.CreateInstance(t);
                var m = t.GetMethod("CanExecute");
                bool result = (bool)m.Invoke(inst, new object[] { "NET:LEARN_LAYER_MAPPING:0-S-CL:Expo_Column" });
                int tv = (int)t.GetProperty("TargetVersion").GetValue(inst);
                Console.WriteLine($"- {t.Name} (V{tv}) | CanExecute: {result}");
            }
        }
    }
}

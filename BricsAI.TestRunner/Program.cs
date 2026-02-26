using System;
using System.IO;
using System.Threading.Tasks;
using BricsAI.Overlay.Services.Agents;
using BricsAI.Overlay.Services;

class Program
{
    static async Task Main()
    {
        string userMessage = @"I have a new vendor file. Please learn the following layer mappings:
0-S-CL -> Expo_Column
214-A-WA-INT -> Expo_Building
214-A-WA-EXT -> Expo_Building
214-A-WI-WESTBRIDGE -> Expo_Building
214-A-RAIL-BRIDGE -> Expo_Building
0-EL-0 -> Expo_View2
0-EL-00 -> Expo_View2
0-EL-00A -> Expo_View2
0-EL-1 -> Expo_View2
214-A-DR -> Expo_Building
214-A-TX-16TH -> Expo_Markings
214-A-VT-NIC -> Expo_Building
KEY -> Expo_View2
AIRWALL -> Expo_View2
Expo_AisleNumber -> Expo_View2
_VPR-FM -> Expo_ImageMarkings
SHOW-TITLE -> Expo_Markings
Title -> Expo_Markings
Door -> Expo_Building
214-A-BH-EX -> Expo_Building
214-A-VT -> Expo_Building
214-A-WL-EXT -> Expo_Building
214-A-WL-INT -> Expo_Building";

        try
        {
            var surveyor = new SurveyorAgent();
            var executor = new ExecutorAgent();
            
            Console.WriteLine("Running Surveyor...");
            var result1 = await surveyor.AnalyzeDrawingStateAsync(userMessage, "", "");
            Console.WriteLine("Surveyor Summary:\n" + result1.Summary);
            
            Console.WriteLine("\nRunning Executor...");
            var result2 = await executor.GenerateMacrosAsync(userMessage, result1.Summary, 19, "");
            Console.WriteLine("Executor Action Plan:\n" + result2.ActionPlan);
        }
        catch (Exception ex)
        {
            Console.WriteLine("CRASHED: " + ex.Message);
        }
    }
}

using System;
using System.Reflection;
using System.Linq;

class Program {
    static void Main() {
        var asm = Assembly.LoadFrom(@"E:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll");
        var pcr = asm.GetType("Verse.PawnCacheRenderer") ?? asm.GetType("RimWorld.PawnCacheRenderer");
        var methods = pcr.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var m in methods) {
            if (m.Name == "RenderPawn") {
                Console.WriteLine("RenderPawn parameters:");
                foreach (var p in m.GetParameters()) {
                    Console.WriteLine("  " + p.Name + " : " + p.ParameterType.Name);
                }
            }
        }
    }
}

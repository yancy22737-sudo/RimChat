using System;
using System.Reflection;
using System.IO;
using System.Linq;

public class QuestDump
{
    public static void Main()
    {
        try {
            string libPath = @"e:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll";
            Assembly asm = Assembly.LoadFile(libPath);
            Type tClass = asm.GetType("RimWorld.QuestGen.QuestNode_Root_Mission_BanditCamp");
            Type tBase = asm.GetType("RimWorld.QuestGen.QuestNode_Root_Mission");
            using (StreamWriter writer = new StreamWriter("qnode_dump.txt")) {
                if (tClass != null && tBase != null) {
                    var m = tBase.GetMethod("GetRequiredPawnCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    object inst = Activator.CreateInstance(tClass);
                    
                    writer.WriteLine("Pawn count for population 3 points 350: " + m.Invoke(inst, new object[] { 3, 350f }));
                    writer.WriteLine("Pawn count for population 10 points 350: " + m.Invoke(inst, new object[] { 10, 350f }));
                }
            }
        } catch (Exception ex) {
            Console.WriteLine(ex.ToString());
        }
    }
}

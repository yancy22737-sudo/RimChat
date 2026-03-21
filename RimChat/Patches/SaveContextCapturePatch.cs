using System.IO;
using HarmonyLib;
using RimChat.Memory;
using Verse;

namespace RimChat.Patches
{
    /// <summary>
    /// Dependencies: Verse save/load entry points and SaveContextTracker.
    /// Responsibility: capture active save filename at load/save boundaries for stable per-save memory isolation.
    /// </summary>
    [HarmonyPatch(typeof(SavedGameLoaderNow), nameof(SavedGameLoaderNow.LoadGameFromSaveFileNow))]
    public static class SaveContextCapturePatch_LoadFromSaveFileNow
    {
        [HarmonyPrefix]
        private static void Prefix(string fileName)
        {
            SaveContextTracker.CaptureSaveName(fileName);
        }
    }

    [HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.LoadGame), new[] { typeof(string) })]
    public static class SaveContextCapturePatch_LoadGameByName
    {
        [HarmonyPrefix]
        private static void Prefix(string saveFileName)
        {
            SaveContextTracker.CaptureSaveName(saveFileName);
        }
    }

    [HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.LoadGame), new[] { typeof(FileInfo) })]
    public static class SaveContextCapturePatch_LoadGameByFileInfo
    {
        [HarmonyPrefix]
        private static void Prefix(FileInfo saveFile)
        {
            SaveContextTracker.CaptureSaveName(saveFile?.Name);
        }
    }

    [HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame))]
    public static class SaveContextCapturePatch_SaveGame
    {
        [HarmonyPrefix]
        private static void Prefix(string fileName)
        {
            SaveContextTracker.CaptureSaveName(fileName);
        }
    }
}

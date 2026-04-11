using HarmonyLib;
using RimChat.NpcDialogue;
using RimChat.PawnRpgPush;
using Verse;

namespace RimChat.Patches
{
    /// <summary>
    /// Dependencies: Verse.LoadedObjectDirectory, RimChat ChoiceLetter subclasses.
    /// Responsibility: Fix legacy ChoiceLetter objects that have loadID=0 by assigning
    /// unique IDs BEFORE LoadedObjectDirectory.RegisterLoaded registers them,
    /// preventing "An item with the same key has already been added. Key: Letter_0" crashes.
    /// </summary>
    [HarmonyPatch(typeof(LoadedObjectDirectory), "RegisterLoaded")]
    public static class LoadedObjectDirectoryPatch_FixLegacyLetterLoadID
    {
        private static readonly System.Reflection.FieldInfo LetterLoadIDField =
            AccessTools.Field(typeof(Letter), "loadID");

        [HarmonyPrefix]
        static void FixLegacyLetterLoadID(ILoadReferenceable __0)
        {
            if (__0 is ChoiceLetter_NpcInitiatedDialogue npcLetter)
            {
                int current = LetterLoadIDField != null
                    ? (int)LetterLoadIDField.GetValue(npcLetter)
                    : 0;
                if (current <= 0)
                {
                    int assigned = ChoiceLetter_NpcInitiatedDialogue.AssignNextUniqueLoadID();
                    LetterLoadIDField?.SetValue(npcLetter, assigned);
                    Log.Warning(
                        $"[RimChat] Pre-register fix: ChoiceLetter_NpcInitiatedDialogue loadID={current}, assigned={assigned}");
                }
            }
            else if (__0 is ChoiceLetter_PawnRpgInitiatedDialogue rpgLetter)
            {
                int current = LetterLoadIDField != null
                    ? (int)LetterLoadIDField.GetValue(rpgLetter)
                    : 0;
                if (current <= 0)
                {
                    int assigned = ChoiceLetter_PawnRpgInitiatedDialogue.AssignNextUniqueLoadID();
                    LetterLoadIDField?.SetValue(rpgLetter, assigned);
                    Log.Warning(
                        $"[RimChat] Pre-register fix: ChoiceLetter_PawnRpgInitiatedDialogue loadID={current}, assigned={assigned}");
                }
            }
        }
    }
}

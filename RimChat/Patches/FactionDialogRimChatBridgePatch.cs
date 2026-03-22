using System;
using HarmonyLib;
using RimChat.Core;
using RimChat.Dialogue;
using RimChat.UI;
using RimWorld;
using Verse;

namespace RimChat.Patches
{
    /// <summary>
    /// Dependencies: RimWorld.FactionDialogMaker.FactionDialogFor, Verse.DiaNode/DiaOption,
    /// RimChat.Core.RimChatMod, RimChat.UI.Dialog_DiplomacyDialogue.
    /// Responsibility: add a vanilla negotiation root-node option that bridges to RimChat
    /// diplomacy dialogue when comms-console replacement is disabled.
    /// </summary>
    [HarmonyPatch(typeof(FactionDialogMaker), nameof(FactionDialogMaker.FactionDialogFor))]
    public static class FactionDialogRimChatBridgePatch
    {
        private const string OptionLabelKey = "RimChat_UseRimChatContact";

        [HarmonyPostfix]
        private static void Postfix(ref DiaNode __result, Pawn negotiator, Faction faction)
        {
            if (!CanInject(__result, faction))
            {
                return;
            }

            string label = OptionLabelKey.Translate();
            if (HasExistingOption(__result, label))
            {
                return;
            }

            DiaOption rimChatOption = CreateRimChatOption(label, faction, negotiator);
            InsertBeforeCloseOption(__result, rimChatOption);
        }

        private static bool CanInject(DiaNode rootNode, Faction faction)
        {
            if (rootNode?.options == null || faction == null)
            {
                return false;
            }

            if (RimChatMod.Settings == null || RimChatMod.Settings.ReplaceCommsConsole)
            {
                return false;
            }

            return !faction.IsPlayer && !faction.defeated;
        }

        private static bool HasExistingOption(DiaNode rootNode, string translatedLabel)
        {
            foreach (DiaOption option in rootNode.options)
            {
                if (option == null)
                {
                    continue;
                }

                string optionText = Traverse.Create(option).Field("text").GetValue<string>();
                if (string.Equals(optionText, translatedLabel, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static DiaOption CreateRimChatOption(string label, Faction faction, Pawn negotiator)
        {
            DiaOption option = new DiaOption(label)
            {
                resolveTree = true,
                action = () =>
                {
                    if (DialogueWindowCoordinator.TryOpen(
                        DialogueOpenIntent.CreateDiplomacy(faction, negotiator, negotiator?.Map),
                        out string reason))
                    {
                        return;
                    }

                    Log.Warning($"[RimChat] Bridge dialogue open rejected: faction={faction?.Name ?? "null"}, reason={reason ?? "unknown"}");
                    TryOpenDiplomacyDirectly(faction, negotiator, "bridge");
                }
            };
            return option;
        }

        private static void TryOpenDiplomacyDirectly(Faction faction, Pawn negotiator, string source)
        {
            if (Find.WindowStack == null || faction == null || faction.defeated)
            {
                return;
            }

            Log.Warning($"[RimChat] Applying direct diplomacy open fallback: source={source}, faction={faction.Name}");
            Find.WindowStack.Add(new Dialog_DiplomacyDialogue(faction, negotiator));
        }

        private static void InsertBeforeCloseOption(DiaNode rootNode, DiaOption rimChatOption)
        {
            int closeIndex = FindCloseOptionIndex(rootNode);
            if (closeIndex >= 0)
            {
                rootNode.options.Insert(closeIndex, rimChatOption);
                return;
            }

            rootNode.options.Add(rimChatOption);
        }

        private static int FindCloseOptionIndex(DiaNode rootNode)
        {
            for (int i = rootNode.options.Count - 1; i >= 0; i--)
            {
                DiaOption option = rootNode.options[i];
                if (IsCloseOption(option))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsCloseOption(DiaOption option)
        {
            if (option == null || !option.resolveTree)
            {
                return false;
            }

            return option.link == null && option.linkLateBind == null;
        }
    }
}

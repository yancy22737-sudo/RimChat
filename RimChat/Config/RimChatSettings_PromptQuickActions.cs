using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Prompting;
using RimChat.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: prompt workspace state, quick custom-variable helpers, and lightweight quick-rule dialogs.
    /// Responsibility: host in-game prompt quick actions for faction/pawn-specific persona variable rules.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private void DrawPromptWorkspaceQuickActions(Rect rect)
        {
            float labelWidth = Mathf.Min(120f, Mathf.Max(78f, rect.width * 0.32f));
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            // Keep spacing for layout stability while hiding the quick header text.

            float buttonGap = 8f;
            float buttonWidth = Mathf.Max(84f, (rect.width - labelWidth - buttonGap * 2f) * 0.5f);
            Rect factionRect = new Rect(labelRect.xMax + buttonGap, rect.y, buttonWidth, rect.height);
            Rect pawnRect = new Rect(factionRect.xMax + buttonGap, rect.y, Mathf.Max(84f, rect.xMax - (factionRect.xMax + buttonGap)), rect.height);
            bool factionEnabled = CanUsePromptWorkspaceFactionTemplateQuickAction();
            bool pawnEnabled = CanUsePromptWorkspaceQuickPawnAction();

            DrawPromptWorkspaceQuickButton(
                factionRect,
                "RimChat_PromptWorkbench_QuickFaction",
                factionEnabled ? "RimChat_PromptWorkbench_QuickFactionTooltip" : "RimChat_PromptWorkbench_QuickFactionEmpty",
                factionEnabled,
                OpenPromptWorkspaceFactionTemplateMenu);
            DrawPromptWorkspaceQuickButton(
                pawnRect,
                "RimChat_PromptWorkbench_QuickPawn",
                pawnEnabled ? "RimChat_PromptWorkbench_QuickPawnTooltip" : "RimChat_PromptWorkbench_QuickNeedGame",
                pawnEnabled,
                OpenPromptWorkspaceQuickPawnMenu);
        }

        private void DrawPromptWorkspaceQuickButton(Rect rect, string labelKey, string tooltipKey, bool enabled, Action onClick)
        {
            bool oldEnabled = GUI.enabled;
            Color oldColor = GUI.color;
            GUI.enabled = enabled;
            GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            if (Widgets.ButtonText(rect, labelKey.Translate()))
            {
                onClick?.Invoke();
            }

            GUI.color = oldColor;
            GUI.enabled = oldEnabled;
            RegisterTooltip(rect, tooltipKey);
        }

        private static bool CanUsePromptWorkspaceFactionTemplateQuickAction()
        {
            return FactionPromptManager.Instance.AllConfigs.Any(item =>
                item != null && !string.IsNullOrWhiteSpace(item.FactionDefName));
        }

        private static bool CanUsePromptWorkspaceQuickPawnAction()
        {
            return Current.ProgramState == ProgramState.Playing &&
                   Current.Game != null &&
                   Find.FactionManager != null;
        }

        private void OpenPromptWorkspaceFactionTemplateMenu()
        {
            List<FactionPromptConfig> configs = FactionPromptManager.Instance.AllConfigs
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.FactionDefName))
                .OrderBy(item => item.DisplayName ?? item.FactionDefName)
                .ThenBy(item => item.FactionDefName)
                .ToList();
            if (configs.Count == 0)
            {
                Messages.Message("RimChat_PromptWorkbench_QuickFactionEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<FloatMenuOption> options = configs
                .Select(config => new FloatMenuOption(
                    GetPromptWorkspaceQuickFactionTemplateLabel(config),
                    () => Find.WindowStack.Add(new Dialog_FactionPromptEditor(config.Clone()))))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenPromptWorkspaceQuickPawnMenu()
        {
            List<Pawn> pawns = GetPromptWorkspaceQuickPawns();
            if (pawns.Count == 0)
            {
                Messages.Message("RimChat_PromptWorkbench_QuickPawnEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<FloatMenuOption> options = pawns
                .Select(pawn => new FloatMenuOption(
                    GetPromptWorkspaceQuickPawnLabel(pawn),
                    () => HandlePromptWorkspaceQuickPawnSelected(pawn)))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void HandlePromptWorkspaceQuickPawnSelected(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            if (!UserDefinedPromptVariableService.RequiresQuickConflictResolution(this, QuickPromptTargetKind.Pawn))
            {
                Find.WindowStack.Add(new Dialog_QuickPromptVariableRuleEditor(this, pawn, QuickPromptConflictDecision.ReuseExisting));
                return;
            }

            ShowPromptWorkspaceQuickConflictMenu(
                QuickPromptTargetKind.Pawn,
                GetPromptWorkspaceQuickPawnLabel(pawn),
                () => Find.WindowStack.Add(new Dialog_QuickPromptVariableRuleEditor(this, pawn, QuickPromptConflictDecision.ReuseExisting)),
                () => Find.WindowStack.Add(new Dialog_QuickPromptVariableRuleEditor(this, pawn, QuickPromptConflictDecision.TakeOver)));
        }

        private void ShowPromptWorkspaceQuickConflictMenu(
            QuickPromptTargetKind kind,
            string targetLabel,
            Action reuseAction,
            Action takeOverAction)
        {
            string path = UserDefinedPromptVariableService.BuildQuickPath(kind);
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "RimChat_PromptWorkbench_QuickConflictReuse".Translate(path, targetLabel),
                    () => reuseAction?.Invoke()),
                new FloatMenuOption(
                    "RimChat_PromptWorkbench_QuickConflictTakeOver".Translate(path, targetLabel),
                    () => takeOverAction?.Invoke())
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static List<Faction> GetPromptWorkspaceQuickFactions()
        {
            return Find.FactionManager?.AllFactionsListForReading?
                .Where(faction => faction != null && faction.def != null && !string.IsNullOrWhiteSpace(faction.def.defName))
                .GroupBy(faction => faction.def.defName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(faction => faction.Name ?? faction.def?.label ?? faction.def?.defName ?? string.Empty)
                .ToList() ?? new List<Faction>();
        }

        private static List<Pawn> GetPromptWorkspaceQuickPawns()
        {
            return PawnsFinder.AllMapsWorldAndTemporary_Alive
                .Where(IsPromptWorkspaceQuickPawnCandidate)
                .GroupBy(pawn => pawn.ThingID ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(GetPromptWorkspaceQuickPawnSortBucket)
                .ThenBy(GetPromptWorkspaceQuickPawnLabel)
                .ToList();
        }

        private static string GetPromptWorkspaceQuickFactionTemplateLabel(FactionPromptConfig config)
        {
            if (config == null)
            {
                return string.Empty;
            }

            string displayName = string.IsNullOrWhiteSpace(config.DisplayName)
                ? config.FactionDefName
                : config.DisplayName.Trim();
            string tag = FactionPromptManager.Instance.IsDefaultTemplate(config.FactionDefName)
                ? "RimChat_FactionTemplateTagDefault".Translate().ToString()
                : "RimChat_FactionTemplateTagCustom".Translate().ToString();
            return $"{tag} {displayName} ({config.FactionDefName})";
        }

        private static bool IsPromptWorkspaceQuickPawnCandidate(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Faction != Faction.OfPlayer || pawn.RaceProps == null)
            {
                return false;
            }

            if (pawn.IsColonistPlayerControlled)
            {
                return true;
            }

            return pawn.RaceProps.Animal || pawn.RaceProps.IsMechanoid;
        }

        private static int GetPromptWorkspaceQuickPawnSortBucket(Pawn pawn)
        {
            if (pawn?.IsColonistPlayerControlled == true)
            {
                return 0;
            }

            if (pawn?.RaceProps?.Animal == true)
            {
                return 1;
            }

            if (pawn?.RaceProps?.IsMechanoid == true)
            {
                return 2;
            }

            return 3;
        }

        internal void HandlePromptWorkspaceQuickPromptSaved(QuickPromptTargetKind kind, string targetLabel)
        {
            InvalidatePromptVariableBrowserCache();
            _rimTalkSelectedVariableName = UserDefinedPromptVariableService.BuildQuickPath(kind);
            if (kind == QuickPromptTargetKind.Pawn)
            {
                TryEnsurePawnPersonalityTokenInCurrentChannel();
            }

            SelectPromptWorkspaceSection("character_persona");
            EnsurePromptWorkspaceSelection();
            Find.WindowStack.Add(new Dialog_MessageBox(
                "RimChat_PromptWorkbench_QuickSavedBody".Translate(
                    targetLabel,
                    UserDefinedPromptVariableService.BuildQuickToken(kind),
                    PromptSectionSchemaCatalog.GetMainChainSections()
                        .First(section => string.Equals(section.Id, "character_persona", StringComparison.OrdinalIgnoreCase))
                        .GetDisplayLabel()),
                "OK".Translate()));
            Messages.Message("RimChat_PromptWorkbench_QuickSavedToast".Translate(targetLabel), MessageTypeDefOf.TaskCompletion, false);
        }

        private bool TryEnsurePawnPersonalityTokenInCurrentChannel()
        {
            string channel = EnsurePromptWorkspaceSelection();
            if (string.IsNullOrWhiteSpace(channel))
            {
                return false;
            }

            const string sectionId = "character_persona";
            if (!TryAppendPawnPersonalityTokenToSection(channel, sectionId, out string updated))
            {
                return false;
            }

            UpdatePromptWorkspacePersonaSectionBuffer(channel, sectionId, updated);
            return true;
        }

        private bool TryAppendPawnPersonalityTokenToSection(string channel, string sectionId, out string updated)
        {
            const string variableName = "pawn.personality";
            const string token = "{{ pawn.personality }}";
            RimTalkPromptEntryDefaultsConfig catalog = GetPromptSectionCatalogClone();
            string current = catalog.ResolveContent(channel, sectionId) ?? string.Empty;
            if (ContainsVariableToken(current, variableName))
            {
                updated = string.Empty;
                return false;
            }

            updated = string.IsNullOrWhiteSpace(current)
                ? token
                : current.TrimEnd() + "\n" + token;
            catalog.SetContent(channel, sectionId, updated);
            SetPromptSectionCatalog(catalog);
            return true;
        }

        private void UpdatePromptWorkspacePersonaSectionBuffer(string channel, string sectionId, string updated)
        {
            _promptWorkspaceBufferedChannel = channel;
            _promptWorkspaceBufferedNodeMode = false;
            _promptWorkspaceBufferedSectionId = sectionId;
            _promptWorkspaceBufferedNodeId = _promptWorkspaceSelectedNodeId ?? string.Empty;
            _promptWorkspaceEditorBuffer = updated;
        }

        internal static string GetPromptWorkspaceQuickFactionLabel(Faction faction)
        {
            if (faction == null)
            {
                return string.Empty;
            }

            string name = faction.Name ?? faction.def?.label ?? faction.def?.defName ?? string.Empty;
            string defName = faction.def?.defName ?? string.Empty;
            return string.IsNullOrWhiteSpace(defName)
                ? name
                : $"{name} ({defName})";
        }

        internal static string GetPromptWorkspaceQuickPawnLabel(Pawn pawn)
        {
            if (pawn == null)
            {
                return string.Empty;
            }

            string category = pawn.IsColonistPlayerControlled
                ? "RimChat_PromptWorkbench_QuickPawnTypeColonist".Translate().ToString()
                : pawn.RaceProps?.Animal == true
                    ? "RimChat_PromptWorkbench_QuickPawnTypeAnimal".Translate().ToString()
                    : "RimChat_PromptWorkbench_QuickPawnTypeMech".Translate().ToString();
            string name = UserDefinedPromptVariableRuleMatcher.ResolvePawnName(pawn);
            return $"{category} · {name}";
        }
    }
}

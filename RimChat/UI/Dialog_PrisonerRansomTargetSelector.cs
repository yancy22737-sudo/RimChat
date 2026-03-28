using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    internal sealed class Dialog_PrisonerRansomTargetSelector : Window
    {
        private readonly Faction sourceFaction;
        private readonly List<Pawn> candidates;
        private readonly Action<List<Pawn>> onConfirm;
        private readonly Action onCancel;
        private readonly HashSet<int> selectedPawnIds = new HashSet<int>();
        private Vector2 scrollPosition = Vector2.zero;
        private bool committed;

        public override Vector2 InitialSize => new Vector2(760f, 600f);

        public Dialog_PrisonerRansomTargetSelector(
            Faction sourceFaction,
            List<Pawn> candidates,
            Action<List<Pawn>> onConfirm,
            Action onCancel)
        {
            this.sourceFaction = sourceFaction;
            this.candidates = candidates ?? new List<Pawn>();
            this.onConfirm = onConfirm;
            this.onCancel = onCancel;

            doCloseX = true;
            closeOnCancel = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            onlyOneOfTypeAllowed = true;
            draggable = true;
        }

        public override void PreClose()
        {
            base.PreClose();
            if (!committed)
            {
                onCancel?.Invoke();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "RimChat_RansomSelectorTitle".Translate());

            Text.Font = GameFont.Small;
            string factionName = sourceFaction?.Name ?? "Unknown";
            Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, 24f), "RimChat_RansomSelectorSubtitle".Translate(factionName));

            Rect toolbarRect = new Rect(inRect.x, inRect.y + 62f, inRect.width, 34f);
            DrawToolbar(toolbarRect);

            Rect listRect = new Rect(inRect.x, toolbarRect.yMax + 4f, inRect.width, inRect.height - 154f);
            DrawList(listRect);

            bool hasSelection = selectedPawnIds.Count > 0;
            Rect confirmRect = new Rect(inRect.x + inRect.width - 326f, inRect.yMax - 38f, 160f, 32f);
            GUI.color = hasSelection ? Color.white : Color.gray;
            if (Widgets.ButtonText(confirmRect, "RimChat_RansomSelectorConfirm".Translate()))
            {
                if (!CommitSelection())
                {
                    Messages.Message("RimChat_RansomBatchSelectionEmptySystem".Translate(), MessageTypeDefOf.RejectInput, false);
                }
            }

            GUI.color = Color.white;
            Rect cancelRect = new Rect(inRect.x + inRect.width - 160f, inRect.yMax - 38f, 160f, 32f);
            if (Widgets.ButtonText(cancelRect, "RimChat_RansomSelectorCancel".Translate()))
            {
                Close();
            }
        }

        private void DrawToolbar(Rect rect)
        {
            Rect selectAllRect = new Rect(rect.x, rect.y, 120f, rect.height);
            if (Widgets.ButtonText(selectAllRect, "RimChat_RansomSelectorSelectAll".Translate()))
            {
                selectedPawnIds.Clear();
                foreach (Pawn candidate in candidates)
                {
                    if (candidate?.thingIDNumber > 0)
                    {
                        selectedPawnIds.Add(candidate.thingIDNumber);
                    }
                }
            }

            Rect clearRect = new Rect(selectAllRect.xMax + 8f, rect.y, 120f, rect.height);
            if (Widgets.ButtonText(clearRect, "RimChat_RansomSelectorClearAll".Translate()))
            {
                selectedPawnIds.Clear();
            }

            Widgets.Label(
                new Rect(clearRect.xMax + 12f, rect.y + 7f, rect.width - clearRect.xMax - 12f, 24f),
                "RimChat_RansomSelectorSelectedCount".Translate(selectedPawnIds.Count, candidates.Count));
        }

        private void DrawList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(6f);
            if (candidates == null || candidates.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(inner, "RimChat_RansomNoEligiblePrisonerSystem".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            const float rowHeight = 64f;
            float totalHeight = Mathf.Max(inner.height, candidates.Count * rowHeight);
            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, totalHeight);
            Widgets.BeginScrollView(inner, ref scrollPosition, viewRect);
            float y = 0f;
            foreach (Pawn pawn in candidates)
            {
                DrawCandidateRow(new Rect(0f, y, viewRect.width, rowHeight - 2f), pawn);
                y += rowHeight;
            }

            Widgets.EndScrollView();
        }

        private void DrawCandidateRow(Rect rect, Pawn pawn)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            Widgets.DrawBox(rect, 1);
            if (pawn == null || pawn.thingIDNumber <= 0)
            {
                return;
            }

            bool selected = selectedPawnIds.Contains(pawn.thingIDNumber);
            Widgets.Checkbox(new Vector2(rect.x + 8f, rect.y + 20f), ref selected);
            if (selected)
            {
                selectedPawnIds.Add(pawn.thingIDNumber);
            }
            else
            {
                selectedPawnIds.Remove(pawn.thingIDNumber);
            }

            string title = pawn?.LabelShortCap ?? "Unknown";
            string health = BuildHealthLine(pawn);
            Widgets.Label(new Rect(rect.x + 38f, rect.y + 6f, rect.width - 46f, 20f), title);

            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x + 38f, rect.y + 30f, rect.width - 46f, 20f), health);
            GUI.color = Color.white;

            if (!Widgets.ButtonInvisible(rect))
            {
                return;
            }

            if (!selectedPawnIds.Remove(pawn.thingIDNumber))
            {
                selectedPawnIds.Add(pawn.thingIDNumber);
            }
        }

        private bool CommitSelection()
        {
            List<Pawn> selectedPawns = candidates
                .Where(pawn => pawn != null && selectedPawnIds.Contains(pawn.thingIDNumber))
                .ToList();
            if (selectedPawns.Count <= 0)
            {
                return false;
            }

            committed = true;
            onConfirm?.Invoke(selectedPawns);
            Close();
            return true;
        }

        private static string BuildHealthLine(Pawn pawn)
        {
            int healthPct = Mathf.RoundToInt(Mathf.Clamp01(pawn?.health?.summaryHealth?.SummaryHealthPercent ?? 0f) * 100f);
            int consciousnessPct = Mathf.RoundToInt(Mathf.Clamp01(ReadCapacitySafe(pawn, PawnCapacityDefOf.Consciousness)) * 100f);
            return "RimChat_RansomSelectorHealthLine".Translate(healthPct, consciousnessPct).ToString();
        }

        private static float ReadCapacitySafe(Pawn pawn, PawnCapacityDef capacity)
        {
            if (pawn?.health?.capacities == null || capacity == null)
            {
                return 0f;
            }

            try
            {
                return pawn.health.capacities.GetLevel(capacity);
            }
            catch
            {
                return 0f;
            }
        }
    }
}

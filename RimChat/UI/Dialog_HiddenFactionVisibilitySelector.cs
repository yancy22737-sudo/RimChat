using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: RimWorld.Widgets/Window, RimWorld.Faction.
    /// Responsibility: provide a lightweight multi-select popup for hidden faction visibility overrides.
    /// </summary>
    public class Dialog_HiddenFactionVisibilitySelector : Window
    {
        private readonly List<Faction> candidates;
        private readonly HashSet<Faction> selected;
        private readonly Action<List<Faction>> onConfirm;
        private Vector2 scrollPosition = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(480f, 560f);

        public Dialog_HiddenFactionVisibilitySelector(
            IEnumerable<Faction> candidateFactions,
            IEnumerable<Faction> preselectedFactions,
            Action<List<Faction>> onConfirm)
        {
            this.candidates = (candidateFactions ?? Enumerable.Empty<Faction>())
                .Where(f => f != null)
                .Distinct()
                .OrderByDescending(f => f.PlayerGoodwill)
                .ThenBy(f => f.Name)
                .ToList();
            this.selected = new HashSet<Faction>((preselectedFactions ?? Enumerable.Empty<Faction>()).Where(f => f != null));
            this.onConfirm = onConfirm;

            doCloseX = true;
            closeOnCancel = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            onlyOneOfTypeAllowed = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "RimChat_HiddenFactionSelectorTitle".Translate());

            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, 22f), "RimChat_HiddenFactionSelectorDesc".Translate());
            GUI.color = Color.white;

            DrawActionButtons(new Rect(inRect.x, inRect.y + 60f, inRect.width, 28f));
            DrawCandidateList(new Rect(inRect.x, inRect.y + 94f, inRect.width, inRect.height - 144f));
            DrawBottomButtons(new Rect(inRect.x, inRect.yMax - 40f, inRect.width, 36f));
        }

        private void DrawActionButtons(Rect rect)
        {
            float buttonWidth = 96f;
            Rect selectAllRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect clearRect = new Rect(selectAllRect.xMax + 8f, rect.y, buttonWidth, rect.height);

            if (Widgets.ButtonText(selectAllRect, "RimChat_HiddenFactionSelectorSelectAll".Translate()))
            {
                selected.Clear();
                foreach (Faction faction in candidates)
                {
                    selected.Add(faction);
                }
            }

            if (Widgets.ButtonText(clearRect, "RimChat_HiddenFactionSelectorClear".Translate()))
            {
                selected.Clear();
            }
        }

        private void DrawCandidateList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);

            if (candidates.Count == 0)
            {
                DrawEmptyState(rect);
                return;
            }

            Rect innerRect = rect.ContractedBy(6f);
            float rowHeight = 32f;
            float totalHeight = Mathf.Max(innerRect.height, candidates.Count * rowHeight);
            Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, totalHeight);
            Widgets.BeginScrollView(innerRect, ref scrollPosition, viewRect);
            DrawCandidateRows(viewRect, rowHeight);
            Widgets.EndScrollView();
        }

        private void DrawEmptyState(Rect rect)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Widgets.Label(rect.ContractedBy(8f), "RimChat_HiddenFactionSelectorEmpty".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawCandidateRows(Rect viewRect, float rowHeight)
        {
            float y = 0f;
            foreach (Faction faction in candidates)
            {
                DrawCandidateRow(new Rect(0f, y, viewRect.width, rowHeight), faction);
                y += rowHeight;
            }
        }

        private void DrawCandidateRow(Rect rowRect, Faction faction)
        {
            if (Mouse.IsOver(rowRect))
            {
                Widgets.DrawHighlight(rowRect);
            }

            bool isChecked = selected.Contains(faction);
            Rect checkboxRect = new Rect(rowRect.x, rowRect.y + 6f, 24f, 24f);
            Widgets.Checkbox(checkboxRect.position, ref isChecked);
            SetFactionChecked(faction, isChecked);

            DrawFactionIcon(rowRect, faction);
            Rect labelRect = new Rect(rowRect.x + 52f, rowRect.y + 6f, rowRect.width - 60f, 22f);
            Widgets.Label(labelRect, faction.Name ?? faction.def?.label ?? "Unknown");

            if (Widgets.ButtonInvisible(rowRect))
            {
                SetFactionChecked(faction, !selected.Contains(faction));
            }
        }

        private void DrawFactionIcon(Rect rowRect, Faction faction)
        {
            Rect iconRect = new Rect(rowRect.x + 26f, rowRect.y + 5f, 22f, 22f);
            Texture2D icon = faction.def?.FactionIcon;
            if (icon != null && icon != BaseContent.BadTex)
            {
                GUI.DrawTexture(iconRect, icon);
            }
        }

        private void SetFactionChecked(Faction faction, bool checkedState)
        {
            if (checkedState)
            {
                selected.Add(faction);
                return;
            }

            selected.Remove(faction);
        }

        private void DrawBottomButtons(Rect rect)
        {
            float buttonWidth = (rect.width - 10f) / 2f;
            Rect confirmRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect cancelRect = new Rect(confirmRect.xMax + 10f, rect.y, buttonWidth, rect.height);

            if (Widgets.ButtonText(confirmRect, "RimChat_HiddenFactionSelectorConfirm".Translate()))
            {
                onConfirm?.Invoke(selected.ToList());
                Close();
            }

            if (Widgets.ButtonText(cancelRect, "RimChat_HiddenFactionSelectorCancel".Translate()))
            {
                Close();
            }
        }
    }
}

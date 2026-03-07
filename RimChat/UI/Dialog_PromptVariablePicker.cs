using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Persistence;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Responsibility: show supported {{template_variable}} entries and insert selected token.
    /// </summary>
    public class Dialog_PromptVariablePicker : Window
    {
        private readonly IReadOnlyList<PromptTemplateVariableDefinition> definitions;
        private readonly Action<string> onInsert;
        private Vector2 scroll = Vector2.zero;
        private string search = string.Empty;

        public Dialog_PromptVariablePicker(
            IReadOnlyList<PromptTemplateVariableDefinition> definitions,
            Action<string> onInsert)
        {
            this.definitions = definitions ?? Array.Empty<PromptTemplateVariableDefinition>();
            this.onInsert = onInsert;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseButton = true;
            doCloseX = true;
            optionalTitle = "RimChat_PromptVariablePickerTitle".Translate();
        }

        public override Vector2 InitialSize => new Vector2(760f, 560f);

        public override void DoWindowContents(Rect inRect)
        {
            Rect searchRect = new Rect(inRect.x, inRect.y, inRect.width, 28f);
            search = Widgets.TextField(searchRect, search ?? string.Empty);

            IEnumerable<PromptTemplateVariableDefinition> filtered = definitions
                .Where(def => def != null)
                .Where(def =>
                    string.IsNullOrWhiteSpace(search)
                    || def.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || def.DescriptionKey.Translate().ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

            List<PromptTemplateVariableDefinition> list = filtered.ToList();
            Rect listRect = new Rect(inRect.x, searchRect.yMax + 8f, inRect.width, inRect.height - 42f);
            float rowHeight = 54f;
            float contentHeight = Mathf.Max(listRect.height, list.Count * rowHeight);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, contentHeight);

            scroll = GUI.BeginScrollView(listRect, scroll, viewRect);
            for (int i = 0; i < list.Count; i++)
            {
                PromptTemplateVariableDefinition def = list[i];
                Rect rowRect = new Rect(0f, i * rowHeight, viewRect.width, rowHeight - 4f);
                if (i % 2 == 0)
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.12f, 0.12f, 0.14f, 0.5f));
                }

                Rect tokenRect = new Rect(rowRect.x + 8f, rowRect.y + 4f, rowRect.width - 140f, 22f);
                GUI.color = new Color(0.95f, 0.78f, 0.45f);
                Widgets.Label(tokenRect, def.Token);
                GUI.color = Color.white;

                Rect descRect = new Rect(rowRect.x + 8f, rowRect.y + 24f, rowRect.width - 140f, 22f);
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(descRect, def.DescriptionKey.Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                Rect insertRect = new Rect(rowRect.xMax - 116f, rowRect.y + 12f, 108f, 28f);
                if (Widgets.ButtonText(insertRect, "RimChat_InsertVariable".Translate()))
                {
                    onInsert?.Invoke(def.Token);
                }
            }

            GUI.EndScrollView();
        }
    }
}

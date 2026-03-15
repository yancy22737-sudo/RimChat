using System.Collections.Generic;
using System.Linq;
using RimChat.Prompting;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: prompt migration diagnostics model and RimWorld modal UI widgets.
    /// Responsibility: show rewritten/blocked template diagnostics after schema migration.
    /// </summary>
    public sealed class Dialog_PromptMigrationResult : Window
    {
        private readonly List<PromptTemplateRewriteDiagnostic> _diagnostics;
        private readonly int _blockedCount;
        private readonly int _rewrittenCount;
        private Vector2 _scroll = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(980f, 620f);

        internal Dialog_PromptMigrationResult(PromptTemplateAutoRewriteResult result)
        {
            _diagnostics = result?.TemplateDiagnostics?
                .Where(item => item != null)
                .OrderByDescending(item => item.Blocked)
                .ThenBy(item => item.TemplateId)
                .ToList() ?? new List<PromptTemplateRewriteDiagnostic>();
            _blockedCount = _diagnostics.Count(item => item.Blocked);
            _rewrittenCount = _diagnostics.Count(item => item.Rewritten);
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "RimChat_PromptMigrationResultTitle".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + 34f;
            Widgets.Label(
                new Rect(inRect.x, y, inRect.width, 24f),
                "RimChat_PromptMigrationResultSummary".Translate(_diagnostics.Count, _rewrittenCount, _blockedCount));
            y += 28f;

            if (_diagnostics.Count == 0)
            {
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "RimChat_PromptMigrationResultNoData".Translate());
                return;
            }

            Rect listRect = new Rect(inRect.x, y, inRect.width, inRect.height - (y - inRect.y) - 36f);
            DrawDiagnosticsList(listRect);
        }

        private void DrawDiagnosticsList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            float rowHeight = 58f;
            float viewHeight = Mathf.Max(inner.height, _diagnostics.Count * rowHeight + 4f);
            Rect view = new Rect(0f, 0f, inner.width - 16f, viewHeight);
            _scroll = GUI.BeginScrollView(inner, _scroll, view);

            float y = 0f;
            for (int i = 0; i < _diagnostics.Count; i++)
            {
                PromptTemplateRewriteDiagnostic item = _diagnostics[i];
                Rect row = new Rect(0f, y, view.width, rowHeight - 2f);
                Widgets.DrawHighlightIfMouseover(row);
                DrawRowBackground(row, item.Blocked);

                string status = ResolveStatusLabel(item);
                string title = $"{status}  [{item.Channel}]  {item.TemplateId}";
                Widgets.Label(new Rect(row.x + 6f, row.y + 2f, row.width - 12f, 22f), title);
                if (item.Blocked)
                {
                    string reason = string.IsNullOrWhiteSpace(item.Reason)
                        ? "RimChat_PromptMigrationReasonMissing".Translate().ToString()
                        : item.Reason.Trim();
                    GUI.color = new Color(1f, 0.72f, 0.72f);
                    Widgets.Label(new Rect(row.x + 6f, row.y + 24f, row.width - 12f, 30f), reason);
                    GUI.color = Color.white;
                }

                y += rowHeight;
            }

            GUI.EndScrollView();
        }

        private static void DrawRowBackground(Rect row, bool blocked)
        {
            if (!blocked)
            {
                return;
            }

            Widgets.DrawBoxSolid(row, new Color(0.25f, 0.07f, 0.07f, 0.25f));
        }

        private static string ResolveStatusLabel(PromptTemplateRewriteDiagnostic item)
        {
            if (item.Blocked)
            {
                return "RimChat_PromptMigrationStatusBlocked".Translate();
            }

            return item.Rewritten
                ? "RimChat_PromptMigrationStatusRewritten".Translate().ToString()
                : "RimChat_PromptMigrationStatusUnchanged".Translate().ToString();
        }
    }
}

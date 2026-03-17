using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Prompting;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: shared prompt variable catalog and settings UI widgets.
    /// Responsibility: render the shared prompt variable browser for RimTalk and prompt section workspace UIs.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private const float RimTalkVariableCacheRefreshSeconds = 1.2f;
        private const float VariableListRowStep = 24f;

        private string _rimTalkVariableSearch = string.Empty;
        private string _rimTalkSelectedVariableName = string.Empty;
        private readonly List<PromptVariableDisplayEntry> _rimTalkVariableSnapshotCache = new List<PromptVariableDisplayEntry>();
        private readonly List<PromptVariableDisplayEntry> _rimTalkVariableDisplayCache = new List<PromptVariableDisplayEntry>();
        private readonly List<VariableListRow> _rimTalkVariableRowCache = new List<VariableListRow>();
        private readonly Dictionary<string, string> _rimTalkVariableTooltipCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private float _rimTalkVariableCacheRefreshAt = -1f;
        private bool _rimTalkVariableSnapshotReady;
        private int _rimTalkVariableSnapshotVersion;
        private int _rimTalkVariableDisplayVersion = -1;
        private int _rimTalkVariableRowVersion = -1;
        private string _rimTalkVariableDisplaySearch = string.Empty;
        private string _rimTalkVariableRowSearch = string.Empty;

        private void DrawRimTalkTabVariableBrowser(Listing_Standard listing)
        {
            listing.Label("RimChat_RimTalkVariableBrowserTitle".Translate());
            listing.Label("RimChat_RimTalkVariableBrowserHint".Translate());

            Rect searchRow = listing.GetRect(24f);
            float searchLabelWidth = Mathf.Clamp(Text.CalcSize("RimChat_RimTalkVariableSearch".Translate()).x + 6f, 58f, 108f);
            Rect searchLabel = new Rect(searchRow.x, searchRow.y, searchLabelWidth, searchRow.height);
            Rect searchInput = new Rect(searchLabel.xMax + 6f, searchRow.y, searchRow.width - searchLabel.width - 6f, searchRow.height);
            Widgets.Label(searchLabel, "RimChat_RimTalkVariableSearch".Translate());

            string searchBefore = _rimTalkVariableSearch ?? string.Empty;
            _rimTalkVariableSearch = Widgets.TextField(searchInput, searchBefore);
            if (!string.Equals(searchBefore, _rimTalkVariableSearch, StringComparison.Ordinal))
            {
                _rimTalkCompatVariableScroll = Vector2.zero;
            }

            List<PromptVariableDisplayEntry> variables = GetFilteredPromptVariables(_rimTalkVariableSearch);
            if (variables.Count == 0)
            {
                _rimTalkSelectedVariableName = string.Empty;
            }

            const float detailsHeight = 82f;
            Rect browserRect = listing.GetRect(262f);
            Rect listRect = new Rect(browserRect.x, browserRect.y, browserRect.width, browserRect.height - detailsHeight - 6f);
            Rect detailsRect = new Rect(browserRect.x, listRect.yMax + 6f, browserRect.width, detailsHeight);
            DrawPromptVariableList(listRect, variables, selectable: true, currentContent: string.Empty, onInsert: null);
            DrawPromptVariableDetails(detailsRect, ResolveSelectedPromptVariable(variables), onInsert: null);
        }

        private void DrawRimTalkWorkbenchVariableBrowser(Rect rect, string currentEntryContent)
        {
            DrawPromptVariableBrowser(rect, currentEntryContent, entry =>
            {
                AppendVariableToCurrentRimTalkTemplate(entry.Path);
                return true;
            });
        }

        private void DrawPromptVariableBrowser(
            Rect rect,
            string currentContent,
            Func<PromptVariableDisplayEntry, bool> onInsert)
        {
            Rect searchRect = new Rect(rect.x, rect.y, rect.width, 24f);
            string before = _rimTalkVariableSearch ?? string.Empty;
            _rimTalkVariableSearch = Widgets.TextField(searchRect, before);
            if (!string.Equals(before, _rimTalkVariableSearch, StringComparison.Ordinal))
            {
                _rimTalkCompatVariableScroll = Vector2.zero;
            }

            if (string.IsNullOrWhiteSpace(_rimTalkVariableSearch))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.3f);
                Widgets.Label(searchRect.ContractedBy(2f, 0f), "RimChat_RimTalkVariableSearch".Translate());
                GUI.color = Color.white;
            }

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x, rect.y + 26f, rect.width, 20f), "RimChat_RimTalkVariableBrowserHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect listRect = new Rect(rect.x, rect.y + 45f, rect.width, Mathf.Max(1f, rect.height - 45f));
            List<PromptVariableDisplayEntry> variables = GetFilteredPromptVariables(_rimTalkVariableSearch);
            DrawPromptVariableList(listRect, variables, selectable: false, currentContent, onInsert);
        }

        private void DrawPromptVariableList(
            Rect rect,
            List<PromptVariableDisplayEntry> variables,
            bool selectable,
            string currentContent,
            Func<PromptVariableDisplayEntry, bool> onInsert)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.03f, 0.03f, 0.05f));
            Rect inner = rect.ContractedBy(2f);
            EnsurePromptVariableRows(variables);
            int totalRows = _rimTalkVariableRowCache.Count;
            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, totalRows * VariableListRowStep + 6f));
            Widgets.BeginScrollView(inner, ref _rimTalkCompatVariableScroll, viewRect);

            if (totalRows == 0)
            {
                Widgets.Label(new Rect(2f, 0f, viewRect.width - 4f, 20f), "RimChat_RimTalkVariableBrowserHint".Translate());
                Widgets.EndScrollView();
                return;
            }

            ResolveVisibleRowRange(_rimTalkCompatVariableScroll.y, inner.height, totalRows, out int firstRow, out int lastRow);
            for (int rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
            {
                VariableListRow row = _rimTalkVariableRowCache[rowIndex];
                float y = rowIndex * VariableListRowStep;
                if (row.IsHeader)
                {
                    DrawVariableGroupHeaderRow(new Rect(2f, y, viewRect.width - 4f, 20f), row.HeaderText);
                    continue;
                }

                PromptVariableDisplayEntry variable = row.Variable;
                Rect rowRect = new Rect(2f, y, viewRect.width - 4f, 22f);
                DrawVariableEntryRow(rowRect, variable, selectable, currentContent, onInsert);
            }

            Widgets.EndScrollView();
        }

        private void DrawPromptVariableRow(Rect rect, PromptVariableDisplayEntry variable, string currentContent)
        {
            if (variable == null)
            {
                return;
            }

            Text.Font = GameFont.Tiny;
            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;
            string token = BuildVariableToken(variable.Path);
            float tokenWidth = Mathf.Min(Text.CalcSize(token).x + 6f, Mathf.Max(1f, rect.width - 8f));
            Rect tokenRect = new Rect(rect.x + 2f, rect.y + 1f, tokenWidth, rect.height - 2f);
            Rect infoRect = new Rect(tokenRect.xMax + 6f, rect.y + 1f, Mathf.Max(1f, rect.xMax - tokenRect.xMax - 8f), rect.height - 2f);

            GUI.color = new Color(0.8f, 1f, 0.8f);
            Widgets.Label(tokenRect, token.Truncate(tokenRect.width));

            string info = BuildVariableInlineInfo(variable, currentContent);
            if (!string.IsNullOrWhiteSpace(info))
            {
                GUI.color = Color.gray;
                Widgets.Label(infoRect, info.Truncate(infoRect.width));
            }

            Text.WordWrap = oldWordWrap;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawPromptVariableDetails(
            Rect rect,
            PromptVariableDisplayEntry variable,
            Func<PromptVariableDisplayEntry, bool> onInsert)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.09f, 0.11f));
            Rect inner = rect.ContractedBy(6f);
            if (variable == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(inner, "RimChat_RimTalkVariableBrowserHint".Translate());
                GUI.color = Color.white;
                return;
            }

            string insertLabel = "RimChat_InsertVariable".Translate();
            float buttonWidth = onInsert == null ? 0f : Mathf.Clamp(Text.CalcSize(insertLabel).x + 20f, 72f, 118f);
            Rect insertRect = new Rect(inner.xMax - buttonWidth, inner.y, buttonWidth, 24f);
            Rect tokenRect = new Rect(inner.x, inner.y + 2f, inner.width - buttonWidth - (onInsert == null ? 0f : 8f), 20f);
            Rect detailRect = new Rect(inner.x, tokenRect.yMax + 2f, inner.width, inner.height - 24f);

            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;
            string label = BuildVariableToken(variable.Path);
            if (!variable.IsAvailable)
            {
                label += " " + "RimChat_PromptVariableDependencyMissingShort".Translate();
            }

            Widgets.Label(tokenRect, label.Truncate(tokenRect.width));
            Text.WordWrap = oldWordWrap;

            GUI.color = Color.gray;
            string details = BuildVariableGroupKey(variable) + "\n" +
                             BuildAvailabilityLabel(variable) + "\n" +
                             (variable.Description ?? string.Empty);
            Widgets.Label(detailRect, details);
            GUI.color = Color.white;
            if (onInsert != null && Widgets.ButtonText(insertRect, insertLabel))
            {
                onInsert?.Invoke(variable);
            }
        }

        private List<PromptVariableDisplayEntry> GetFilteredPromptVariables(string searchText)
        {
            EnsurePromptVariableSnapshotCacheFresh();
            string normalizedSearch = string.IsNullOrWhiteSpace(searchText) ? string.Empty : searchText.Trim();
            bool unchanged = _rimTalkVariableDisplayVersion == _rimTalkVariableSnapshotVersion &&
                             string.Equals(_rimTalkVariableDisplaySearch, normalizedSearch, StringComparison.Ordinal);
            if (unchanged)
            {
                return _rimTalkVariableDisplayCache;
            }

            _rimTalkVariableDisplaySearch = normalizedSearch;
            _rimTalkVariableDisplayVersion = _rimTalkVariableSnapshotVersion;
            RebuildPromptVariableDisplayCache(normalizedSearch);
            return _rimTalkVariableDisplayCache;
        }

        private void EnsurePromptVariableSnapshotCacheFresh()
        {
            float now = Time.realtimeSinceStartup;
            if (_rimTalkVariableSnapshotReady && now < _rimTalkVariableCacheRefreshAt)
            {
                return;
            }

            _rimTalkVariableCacheRefreshAt = now + RimTalkVariableCacheRefreshSeconds;
            List<PromptVariableDisplayEntry> snapshot = PromptVariableCatalog.GetDisplayEntries().ToList();
            _rimTalkVariableSnapshotCache.Clear();
            _rimTalkVariableSnapshotCache.AddRange(snapshot.Where(item => item != null));
            _rimTalkVariableSnapshotReady = true;
            _rimTalkVariableSnapshotVersion++;
            _rimTalkVariableTooltipCache.Clear();
            InvalidatePromptVariableRowCache();
        }

        private void RebuildPromptVariableDisplayCache(string term)
        {
            _rimTalkVariableDisplayCache.Clear();
            foreach (PromptVariableDisplayEntry entry in _rimTalkVariableSnapshotCache)
            {
                bool matches = string.IsNullOrEmpty(term) ||
                               ContainsTerm(entry?.Path, term) ||
                               ContainsTerm(entry?.Scope, term) ||
                               ContainsTerm(entry?.SourceId, term) ||
                               ContainsTerm(entry?.SourceLabel, term) ||
                               ContainsTerm(entry?.Description, term);
                if (matches)
                {
                    _rimTalkVariableDisplayCache.Add(entry);
                }
            }

            _rimTalkVariableDisplayCache.Sort(ComparePromptVariables);
            InvalidatePromptVariableRowCache();
        }

        private static bool ContainsTerm(string value, string term)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ComparePromptVariables(PromptVariableDisplayEntry left, PromptVariableDisplayEntry right)
        {
            int scope = string.Compare(left?.Scope ?? string.Empty, right?.Scope ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (scope != 0)
            {
                return scope;
            }

            int source = string.Compare(left?.SourceLabel ?? string.Empty, right?.SourceLabel ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (source != 0)
            {
                return source;
            }

            return string.Compare(left?.Path ?? string.Empty, right?.Path ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private PromptVariableDisplayEntry ResolveSelectedPromptVariable(IReadOnlyList<PromptVariableDisplayEntry> variables)
        {
            if (variables == null || variables.Count == 0)
            {
                return null;
            }

            PromptVariableDisplayEntry selected = variables.FirstOrDefault(variable =>
                variable != null && string.Equals(variable.Path, _rimTalkSelectedVariableName, StringComparison.Ordinal));
            if (selected != null)
            {
                return selected;
            }

            _rimTalkSelectedVariableName = variables[0]?.Path ?? string.Empty;
            return variables[0];
        }

        private static string BuildVariableTooltipText(PromptVariableDisplayEntry variable)
        {
            PromptVariableTooltipInfo info = PromptVariableTooltipCatalog.Resolve(variable?.Path);
            string name = "RimChat_PromptVariableTooltip_Name".Translate(info.Name);
            string dataType = "RimChat_PromptVariableTooltip_DataType".Translate(info.DataType);
            string description = "RimChat_PromptVariableTooltip_Description".Translate(info.Description);
            string typicalValues = "RimChat_PromptVariableTooltip_TypicalValues".Translate(BuildTypicalValuesText(info.TypicalValues));
            return $"{name}\n{dataType}\n{description}\n{typicalValues}";
        }

        private static string BuildTypicalValuesText(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "RimChat_PromptVariableTooltip_NoTypicalValues".Translate().ToString();
            }

            var lines = new List<string>(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                lines.Add($"{i + 1}) {values[i]}");
            }

            return string.Join("\n", lines);
        }

        private static string BuildVariableGroupKey(PromptVariableDisplayEntry variable)
        {
            string scope = string.IsNullOrWhiteSpace(variable?.Scope) ? "unknown" : variable.Scope;
            string source = string.IsNullOrWhiteSpace(variable?.SourceLabel) ? "Unknown" : variable.SourceLabel;
            return $"[{scope}] {source}";
        }

        private static string BuildVariableToken(string variableName)
        {
            return "{{ " + (variableName ?? string.Empty) + " }}";
        }

        private static string BuildVariableInlineInfo(PromptVariableDisplayEntry variable, string currentContent)
        {
            string info = variable?.Description ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(info))
            {
                return info;
            }

            if (!string.IsNullOrWhiteSpace(variable?.SourceLabel))
            {
                return variable.SourceLabel;
            }

            return string.Empty;
        }

        private static string BuildAvailabilityLabel(PromptVariableDisplayEntry variable)
        {
            return variable?.IsAvailable == false
                ? "RimChat_PromptVariableDependencyMissing".Translate().ToString()
                : "RimChat_PromptVariableReady".Translate().ToString();
        }

        private void EnsurePromptVariableRows(List<PromptVariableDisplayEntry> variables)
        {
            if (_rimTalkVariableRowVersion == _rimTalkVariableDisplayVersion &&
                string.Equals(_rimTalkVariableRowSearch, _rimTalkVariableDisplaySearch, StringComparison.Ordinal))
            {
                return;
            }

            RebuildPromptVariableRows(variables);
            _rimTalkVariableRowVersion = _rimTalkVariableDisplayVersion;
            _rimTalkVariableRowSearch = _rimTalkVariableDisplaySearch;
        }

        private void RebuildPromptVariableRows(List<PromptVariableDisplayEntry> variables)
        {
            _rimTalkVariableRowCache.Clear();
            string previousGroup = null;
            for (int i = 0; i < variables.Count; i++)
            {
                PromptVariableDisplayEntry variable = variables[i];
                if (variable == null)
                {
                    continue;
                }

                string group = BuildVariableGroupKey(variable);
                if (!string.Equals(previousGroup, group, StringComparison.Ordinal))
                {
                    _rimTalkVariableRowCache.Add(VariableListRow.CreateHeader(group));
                    previousGroup = group;
                }

                _rimTalkVariableRowCache.Add(VariableListRow.CreateVariable(variable));
            }
        }

        private void InvalidatePromptVariableRowCache()
        {
            _rimTalkVariableRowVersion = -1;
            _rimTalkVariableRowSearch = string.Empty;
            _rimTalkVariableRowCache.Clear();
        }

        private static void ResolveVisibleRowRange(
            float scrollY,
            float viewportHeight,
            int rowCount,
            out int firstRow,
            out int lastRow)
        {
            firstRow = Mathf.Max(0, Mathf.FloorToInt(scrollY / VariableListRowStep) - 1);
            lastRow = Mathf.Min(rowCount - 1, Mathf.CeilToInt((scrollY + viewportHeight) / VariableListRowStep) + 1);
        }

        private static void DrawVariableGroupHeaderRow(Rect rect, string header)
        {
            GUI.color = Color.cyan;
            Text.Font = GameFont.Tiny;
            Widgets.Label(rect, "▼ " + (header ?? string.Empty));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawVariableEntryRow(
            Rect rowRect,
            PromptVariableDisplayEntry variable,
            bool selectable,
            string currentContent,
            Func<PromptVariableDisplayEntry, bool> onInsert)
        {
            if (variable == null)
            {
                return;
            }

            if (Mouse.IsOver(rowRect))
            {
                Widgets.DrawHighlight(rowRect);
            }

            if (selectable && Widgets.ButtonInvisible(rowRect))
            {
                _rimTalkSelectedVariableName = variable.Path ?? string.Empty;
            }
            else if (!selectable && Widgets.ButtonInvisible(rowRect))
            {
                _rimTalkSelectedVariableName = variable.Path ?? string.Empty;
                onInsert?.Invoke(variable);
            }

            DrawPromptVariableRow(rowRect, variable, currentContent);
            TooltipHandler.TipRegion(rowRect, GetVariableTooltipTextCached(variable));
        }

        private string GetVariableTooltipTextCached(PromptVariableDisplayEntry variable)
        {
            string path = variable?.Path ?? string.Empty;
            if (!_rimTalkVariableTooltipCache.TryGetValue(path, out string tooltip))
            {
                tooltip = BuildVariableTooltipText(variable);
                _rimTalkVariableTooltipCache[path] = tooltip;
            }

            return tooltip;
        }

        private sealed class VariableListRow
        {
            public bool IsHeader { get; private set; }
            public string HeaderText { get; private set; }
            public PromptVariableDisplayEntry Variable { get; private set; }

            public static VariableListRow CreateHeader(string headerText)
            {
                return new VariableListRow
                {
                    IsHeader = true,
                    HeaderText = headerText ?? string.Empty
                };
            }

            public static VariableListRow CreateVariable(PromptVariableDisplayEntry variable)
            {
                return new VariableListRow
                {
                    IsHeader = false,
                    Variable = variable
                };
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Compat;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: RimTalk compatibility bridge variable snapshot API, UI widgets, and prompt entry insertion handlers.
    /// Responsibility: render and cache the RimTalk variable browser for both RimTalk tab and Prompt Workbench.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private const float RimTalkVariableCacheRefreshSeconds = 1.2f;

        private string _rimTalkVariableSearch = string.Empty;
        private string _rimTalkSelectedVariableName = string.Empty;
        private readonly List<RimTalkRegisteredVariable> _rimTalkVariableSnapshotCache = new List<RimTalkRegisteredVariable>();
        private readonly List<RimTalkRegisteredVariable> _rimTalkVariableDisplayCache = new List<RimTalkRegisteredVariable>();
        private float _rimTalkVariableCacheRefreshAt = -1f;
        private bool _rimTalkVariableSnapshotReady;
        private int _rimTalkVariableSnapshotVersion;
        private int _rimTalkVariableDisplayVersion = -1;
        private string _rimTalkVariableDisplaySearch = string.Empty;

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

            List<RimTalkRegisteredVariable> variables = GetFilteredRimTalkVariables(_rimTalkVariableSearch);
            if (variables.Count == 0)
            {
                _rimTalkSelectedVariableName = string.Empty;
            }

            const float detailsHeight = 66f;
            Rect browserRect = listing.GetRect(262f);
            Rect listRect = new Rect(browserRect.x, browserRect.y, browserRect.width, browserRect.height - detailsHeight - 6f);
            Rect detailsRect = new Rect(browserRect.x, listRect.yMax + 6f, browserRect.width, detailsHeight);

            float rowHeight = 24f;
            float headerHeight = 20f;
            float viewHeight = 4f;
            string lastGroup = string.Empty;
            for (int i = 0; i < variables.Count; i++)
            {
                string currentGroup = BuildVariableGroupKey(variables[i]);
                if (!string.Equals(lastGroup, currentGroup, StringComparison.Ordinal))
                {
                    viewHeight += headerHeight;
                    lastGroup = currentGroup;
                }

                viewHeight += rowHeight;
            }

            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(listRect.height, viewHeight));
            Widgets.BeginScrollView(listRect, ref _rimTalkCompatVariableScroll, viewRect);

            float y = 2f;
            lastGroup = string.Empty;
            for (int i = 0; i < variables.Count; i++)
            {
                RimTalkRegisteredVariable variable = variables[i];
                string group = BuildVariableGroupKey(variable);
                if (!string.Equals(lastGroup, group, StringComparison.Ordinal))
                {
                    GUI.color = Color.gray;
                    Widgets.Label(new Rect(0f, y, viewRect.width, 20f), group);
                    GUI.color = Color.white;
                    y += headerHeight;
                    lastGroup = group;
                }

                bool selected = string.Equals(_rimTalkSelectedVariableName, variable?.Name, StringComparison.Ordinal);
                if (DrawRimTalkTabVariableRow(new Rect(0f, y, viewRect.width, rowHeight), variable, selected))
                {
                    _rimTalkSelectedVariableName = variable?.Name ?? string.Empty;
                }

                y += rowHeight;
            }

            Widgets.EndScrollView();
            RimTalkRegisteredVariable selectedVariable = ResolveSelectedRimTalkVariable(variables);
            DrawRimTalkVariableDetails(detailsRect, selectedVariable);
        }

        private void DrawRimTalkWorkbenchVariableBrowser(Rect rect, string currentEntryContent)
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
            List<RimTalkRegisteredVariable> variables = GetFilteredRimTalkVariables(_rimTalkVariableSearch);
            var grouped = new Dictionary<string, List<RimTalkRegisteredVariable>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < variables.Count; i++)
            {
                RimTalkRegisteredVariable variable = variables[i];
                string group = BuildVariableGroupKey(variable);
                if (!grouped.TryGetValue(group, out List<RimTalkRegisteredVariable> bucket))
                {
                    bucket = new List<RimTalkRegisteredVariable>();
                    grouped[group] = bucket;
                }

                bucket.Add(variable);
            }

            int totalRows = grouped.Sum(pair => pair.Value.Count + 1);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(listRect.height, totalRows * 24f + 6f));
            Widgets.BeginScrollView(listRect, ref _rimTalkCompatVariableScroll, viewRect);

            string stripPrefix = string.Empty;
            int lastDot = (_rimTalkVariableSearch ?? string.Empty).LastIndexOf('.');
            if (lastDot >= 0)
            {
                stripPrefix = _rimTalkVariableSearch.Substring(0, lastDot + 1);
            }

            float y = 0f;
            foreach (KeyValuePair<string, List<RimTalkRegisteredVariable>> pair in grouped)
            {
                GUI.color = Color.cyan;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(0f, y, viewRect.width, 20f), "▼ " + pair.Key);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += 24f;

                List<RimTalkRegisteredVariable> bucket = pair.Value;
                for (int i = 0; i < bucket.Count; i++)
                {
                    RimTalkRegisteredVariable variable = bucket[i];
                    string displayName = variable?.Name ?? string.Empty;
                    if (!string.IsNullOrEmpty(stripPrefix) &&
                        displayName.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        displayName = displayName.Substring(stripPrefix.Length);
                    }

                    DrawRimTalkWorkbenchVariableRow(ref y, viewRect.width, variable, displayName, currentEntryContent);
                }
            }

            if (grouped.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, 0f, viewRect.width, 20f), "RimChat_RimTalkVariableBrowserHint".Translate());
                GUI.color = Color.white;
            }

            Widgets.EndScrollView();
        }

        private List<RimTalkRegisteredVariable> GetFilteredRimTalkVariables(string searchText)
        {
            EnsureRimTalkVariableSnapshotCacheFresh();
            string normalizedSearch = string.IsNullOrWhiteSpace(searchText) ? string.Empty : searchText.Trim();
            bool unchanged = _rimTalkVariableDisplayVersion == _rimTalkVariableSnapshotVersion &&
                             string.Equals(_rimTalkVariableDisplaySearch, normalizedSearch, StringComparison.Ordinal);
            if (unchanged)
            {
                return _rimTalkVariableDisplayCache;
            }

            _rimTalkVariableDisplaySearch = normalizedSearch;
            _rimTalkVariableDisplayVersion = _rimTalkVariableSnapshotVersion;
            RebuildRimTalkVariableDisplayCache(normalizedSearch);
            return _rimTalkVariableDisplayCache;
        }

        private void EnsureRimTalkVariableSnapshotCacheFresh()
        {
            float now = Time.realtimeSinceStartup;
            if (_rimTalkVariableSnapshotReady && now < _rimTalkVariableCacheRefreshAt)
            {
                return;
            }

            _rimTalkVariableCacheRefreshAt = now + RimTalkVariableCacheRefreshSeconds;
            List<RimTalkRegisteredVariable> snapshot = RimTalkCompatBridge.GetRegisteredVariablesSnapshot() ?? new List<RimTalkRegisteredVariable>();
            _rimTalkVariableSnapshotCache.Clear();
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i] != null)
                {
                    _rimTalkVariableSnapshotCache.Add(snapshot[i]);
                }
            }

            _rimTalkVariableSnapshotReady = true;
            _rimTalkVariableSnapshotVersion++;
        }

        private void RebuildRimTalkVariableDisplayCache(string term)
        {
            _rimTalkVariableDisplayCache.Clear();
            for (int i = 0; i < _rimTalkVariableSnapshotCache.Count; i++)
            {
                RimTalkRegisteredVariable item = _rimTalkVariableSnapshotCache[i];
                bool matches = string.IsNullOrEmpty(term) ||
                               ContainsTerm(item?.Name, term) ||
                               ContainsTerm(item?.Type, term) ||
                               ContainsTerm(item?.ModId, term) ||
                               ContainsTerm(item?.Description, term);
                if (matches)
                {
                    _rimTalkVariableDisplayCache.Add(item);
                }
            }

            _rimTalkVariableDisplayCache.Sort(CompareRimTalkVariables);
        }

        private static bool ContainsTerm(string value, string term)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int CompareRimTalkVariables(RimTalkRegisteredVariable left, RimTalkRegisteredVariable right)
        {
            int type = string.Compare(left?.Type ?? string.Empty, right?.Type ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (type != 0)
            {
                return type;
            }

            int mod = string.Compare(left?.ModId ?? string.Empty, right?.ModId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (mod != 0)
            {
                return mod;
            }

            return string.Compare(left?.Name ?? string.Empty, right?.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private RimTalkRegisteredVariable ResolveSelectedRimTalkVariable(IReadOnlyList<RimTalkRegisteredVariable> variables)
        {
            if (variables == null || variables.Count == 0)
            {
                return null;
            }

            RimTalkRegisteredVariable selected = variables.FirstOrDefault(variable =>
                variable != null && string.Equals(variable.Name, _rimTalkSelectedVariableName, StringComparison.Ordinal));
            if (selected != null)
            {
                return selected;
            }

            _rimTalkSelectedVariableName = variables[0]?.Name ?? string.Empty;
            return variables[0];
        }

        private void DrawRimTalkVariableDetails(Rect rect, RimTalkRegisteredVariable variable)
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
            float buttonWidth = Mathf.Clamp(Text.CalcSize(insertLabel).x + 20f, 72f, 118f);
            Rect insertRect = new Rect(inner.xMax - buttonWidth, inner.y, buttonWidth, 24f);
            Rect tokenRect = new Rect(inner.x, inner.y + 2f, inner.width - buttonWidth - 8f, 20f);
            Rect detailRect = new Rect(inner.x, tokenRect.yMax + 2f, inner.width, inner.height - 24f);

            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(tokenRect, BuildVariableToken(variable.Name));
            Text.WordWrap = oldWordWrap;

            GUI.color = Color.gray;
            string details = BuildVariableGroupKey(variable) + "\n" + (variable.Description ?? string.Empty);
            Widgets.Label(detailRect, details);
            GUI.color = Color.white;
            if (Widgets.ButtonText(insertRect, insertLabel))
            {
                AppendVariableToCurrentRimTalkTemplate(variable.Name);
            }
        }

        private bool DrawRimTalkTabVariableRow(Rect rect, RimTalkRegisteredVariable variable, bool selected)
        {
            if (variable == null)
            {
                return false;
            }

            if (selected)
            {
                Widgets.DrawBoxSolid(rect, new Color(0.24f, 0.35f, 0.55f));
            }
            else if (Mouse.IsOver(rect))
            {
                Widgets.DrawBoxSolid(rect, new Color(0.18f, 0.18f, 0.2f));
            }

            string insertLabel = "RimChat_InsertVariable".Translate();
            float insertWidth = Mathf.Clamp(Text.CalcSize(insertLabel).x + 18f, 64f, 106f);
            Rect insertRect = new Rect(rect.xMax - insertWidth, rect.y + 1f, insertWidth, rect.height - 2f);
            Rect tokenRect = new Rect(rect.x + 4f, rect.y + 2f, insertRect.x - rect.x - 8f, rect.height - 4f);

            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(tokenRect, BuildVariableToken(variable.Name));
            Text.WordWrap = oldWordWrap;

            bool clicked = false;
            Rect selectRect = new Rect(rect.x, rect.y, tokenRect.width + 6f, rect.height);
            if (Widgets.ButtonInvisible(selectRect))
            {
                clicked = true;
            }

            if (Widgets.ButtonText(insertRect, insertLabel))
            {
                AppendVariableToCurrentRimTalkTemplate(variable.Name);
                clicked = true;
            }

            string tip = $"[{variable.Type}] {variable.Name}\n{variable.Description}\n{variable.ModId}";
            TooltipHandler.TipRegion(rect, tip);
            return clicked;
        }

        private void DrawRimTalkWorkbenchVariableRow(
            ref float y,
            float width,
            RimTalkRegisteredVariable variable,
            string displayName,
            string currentEntryContent)
        {
            if (variable == null)
            {
                y += 24f;
                return;
            }

            const float rowHeight = 22f;
            string insertLabel = "RimChat_InsertVariable".Translate();
            float insertWidth = Mathf.Clamp(Text.CalcSize(insertLabel).x + 16f, 60f, 100f);
            Rect rowRect = new Rect(0f, y, width, rowHeight);
            Rect insertRect = new Rect(rowRect.xMax - insertWidth - 2f, y + 1f, insertWidth, rowHeight - 2f);
            Rect selectRect = new Rect(0f, y, Mathf.Max(1f, insertRect.x - 4f), rowHeight);
            if (Mouse.IsOver(rowRect))
            {
                Widgets.DrawHighlight(rowRect);
            }

            if (Widgets.ButtonInvisible(selectRect))
            {
                _rimTalkSelectedVariableName = variable.Name ?? string.Empty;
                AppendVariableToCurrentRimTalkTemplate(variable.Name);
            }

            if (Widgets.ButtonText(insertRect, insertLabel))
            {
                _rimTalkSelectedVariableName = variable.Name ?? string.Empty;
                AppendVariableToCurrentRimTalkTemplate(variable.Name);
            }

            Text.Font = GameFont.Tiny;
            string token = BuildVariableToken(string.IsNullOrWhiteSpace(displayName) ? variable.Name : displayName);
            float labelWidth = Text.CalcSize(token).x + 5f;

            GUI.color = new Color(0.8f, 1f, 0.8f);
            Widgets.Label(new Rect(2f, y + 1f, Mathf.Min(labelWidth, selectRect.width - 4f), rowHeight - 2f), token);

            string typeInfo = BuildWorkbenchVariableTypeInfo(variable, currentEntryContent);
            if (!string.IsNullOrWhiteSpace(typeInfo))
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
                float typeX = Mathf.Min(selectRect.xMax - 10f, labelWidth + 8f);
                float typeW = selectRect.width - typeX - 4f;
                if (typeW > 10f)
                {
                    Widgets.Label(new Rect(typeX, y + 1f, typeW, rowHeight - 2f), typeInfo);
                }
            }

            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            string tip = $"[{variable.Type}] {BuildVariableToken(variable.Name)}\n{variable.Description}\n{variable.ModId}";
            TooltipHandler.TipRegion(rowRect, tip);
            y += 24f;
        }

        private static string BuildVariableGroupKey(RimTalkRegisteredVariable variable)
        {
            string type = string.IsNullOrWhiteSpace(variable?.Type) ? "Unknown" : variable.Type;
            string mod = string.IsNullOrWhiteSpace(variable?.ModId) ? "UnknownMod" : variable.ModId;
            return $"[{type}] {mod}";
        }

        private static string BuildVariableToken(string variableName)
        {
            return "{{ " + (variableName ?? string.Empty) + " }}";
        }

        private static string BuildWorkbenchVariableTypeInfo(RimTalkRegisteredVariable variable, string currentEntryContent)
        {
            string baseInfo = string.IsNullOrWhiteSpace(variable?.Description)
                ? variable?.Type ?? string.Empty
                : variable.Description;
            if (string.IsNullOrWhiteSpace(baseInfo))
            {
                baseInfo = variable?.ModId ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(baseInfo))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(currentEntryContent) || string.IsNullOrWhiteSpace(variable?.Name))
            {
                return baseInfo;
            }

            return baseInfo;
        }
    }
}

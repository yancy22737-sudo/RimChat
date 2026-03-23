using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using RimChat.AI;
using RimChat.Core;
using RimChat.Prompting;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: AIChatServiceAsync telemetry snapshot API and RimWorld Window/Widgets.
    /// Responsibility: present in-memory API debug observability with summary, trend, list, detail, and JSON copy.
    /// </summary>
    public sealed class Dialog_ApiDebugObservability : Window
    {
        private const float RefreshIntervalSeconds = 2f;
        private const float HeaderHeight = 30f;
        private const float SummaryHeight = 128f;
        private const float TrendHeight = 180f;
        private const float SectionGap = 8f;
        private const float RowHeight = 24f;
        private static readonly float[] TableColumnWeights = { 0.11f, 0.18f, 0.11f, 0.26f, 0.12f, 0.12f, 0.10f };

        private enum SourceFilterMode
        {
            All = 0,
            PriorityOnly = 1,
            BackgroundOnly = 2
        }

        private enum StatusFilterMode
        {
            All = 0,
            Success = 1,
            Error = 2,
            Cancelled = 3
        }

        private AIRequestDebugSnapshot snapshot;
        private float nextRefreshAtRealtime;
        private Vector2 listScrollPosition = Vector2.zero;
        private Vector2 detailScrollPosition = Vector2.zero;
        private string selectedRequestId = string.Empty;
        private SourceFilterMode sourceFilter = SourceFilterMode.All;
        private StatusFilterMode statusFilter = StatusFilterMode.All;

        public override Vector2 InitialSize => new Vector2(1400f, 860f);

        public Dialog_ApiDebugObservability()
        {
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseX = true;
            doCloseButton = false;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            RefreshSnapshot(force: true);
        }

        public override void DoWindowContents(Rect inRect)
        {
            RefreshSnapshot(force: false);

            float y = inRect.y;
            DrawHeader(new Rect(inRect.x, y, inRect.width, HeaderHeight));
            y += HeaderHeight + SectionGap;

            DrawSummaryCards(new Rect(inRect.x, y, inRect.width, SummaryHeight));
            y += SummaryHeight + SectionGap;

            DrawTrendChart(new Rect(inRect.x, y, inRect.width, TrendHeight));
            y += TrendHeight + SectionGap;

            float bottomHeight = inRect.yMax - y;
            const float panelGap = 8f;
            const float baseDetailRatio = 0.34f;
            const float detailWidthMultiplier = 1.30f; // Widen detail panel by 30%.
            float detailWidth = Mathf.Clamp(inRect.width * baseDetailRatio * detailWidthMultiplier, 300f, inRect.width - 420f);
            float listWidth = inRect.width - detailWidth - panelGap;
            Rect listRect = new Rect(inRect.x, y, listWidth, bottomHeight);
            Rect detailRect = new Rect(listRect.xMax + panelGap, y, detailWidth, bottomHeight);
            List<AIRequestDebugRecord> filtered = DrawRecordsTable(listRect);
            DrawDetailPanel(detailRect, filtered);
        }

        private void RefreshSnapshot(bool force)
        {
            if (!force && Time.realtimeSinceStartup < nextRefreshAtRealtime)
            {
                return;
            }

            if (!AIChatServiceAsync.TryGetRequestDebugSnapshot(out AIRequestDebugSnapshot latest) || latest == null)
            {
                latest = new AIRequestDebugSnapshot
                {
                    GeneratedAtUtc = DateTime.UtcNow,
                    WindowMinutes = 30,
                    Buckets = new List<AIRequestDebugBucket>(),
                    Records = new List<AIRequestDebugRecord>(),
                    Summary = new AIRequestDebugSummary()
                };
            }

            snapshot = latest;
            nextRefreshAtRealtime = Time.realtimeSinceStartup + RefreshIntervalSeconds;
            EnsureSelectionStillValid();
        }

        private void EnsureSelectionStillValid()
        {
            List<AIRequestDebugRecord> filtered = GetFilteredRecords();
            if (filtered.Count == 0)
            {
                selectedRequestId = string.Empty;
                return;
            }

            bool exists = filtered.Any(record => string.Equals(record.RequestId, selectedRequestId, StringComparison.Ordinal));
            if (!exists)
            {
                selectedRequestId = filtered[0].RequestId;
            }
        }

        private void DrawHeader(Rect rect)
        {
            const float settingsButtonWidth = 120f;
            const float updatedLabelWidth = 250f;
            const float rightGap = 8f;

            Text.Font = GameFont.Medium;
            Widgets.Label(
                new Rect(rect.x, rect.y, Mathf.Max(120f, rect.width - settingsButtonWidth - updatedLabelWidth - rightGap * 3f), rect.height),
                "RimChat_ApiDebugWindowTitle".Translate());
            Text.Font = GameFont.Small;

            Rect settingsButtonRect = new Rect(
                rect.xMax - settingsButtonWidth,
                rect.y,
                settingsButtonWidth,
                rect.height);
            if (Widgets.ButtonText(settingsButtonRect, "RimChat_ApiDebugOpenSettingsButton".Translate()))
            {
                TryOpenRimChatSettingsWindow();
            }

            TooltipHandler.TipRegion(settingsButtonRect, "RimChat_ApiDebugOpenSettingsButtonTooltip".Translate());

            string updatedText = "RimChat_ApiDebugLastUpdated".Translate(snapshot?.GeneratedAtUtc.ToLocalTime().ToString("HH:mm:ss") ?? "--");
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = Color.gray;
            Widgets.Label(
                new Rect(
                    settingsButtonRect.xMin - updatedLabelWidth - rightGap,
                    rect.y,
                    updatedLabelWidth,
                    rect.height),
                updatedText);
            GUI.color = Color.white;
            Text.Anchor = oldAnchor;
        }

        private static void TryOpenRimChatSettingsWindow()
        {
            RimChatMod rimChatMod = RimChatMod.Instance ?? LoadedModManager.GetMod<RimChatMod>();
            if (rimChatMod == null)
            {
                Messages.Message("RimChat_ApiDebugOpenSettingsFailed".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack?.Add(new Dialog_ModSettings(rimChatMod));
        }

        private void DrawSummaryCards(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            AIRequestDebugSummary summary = snapshot?.Summary ?? new AIRequestDebugSummary();
            PromptRenderTelemetrySnapshot promptTelemetry = ScribanPromptEngine.GetTelemetrySnapshot();

            const float telemetryHeight = 30f;
            float cardHeight = Mathf.Max(56f, inner.height - telemetryHeight - 6f);
            float cardWidth = (inner.width - 20f) / 5f;
            DrawSummaryCard(new Rect(inner.x + (cardWidth + 5f) * 0f, inner.y, cardWidth, cardHeight), "RimChat_ApiDebugCardTotalTokens".Translate(), summary.TotalTokens.ToString("N0"));
            DrawSummaryCard(new Rect(inner.x + (cardWidth + 5f) * 1f, inner.y, cardWidth, cardHeight), "RimChat_ApiDebugCardRequestCount".Translate(), summary.RequestCount.ToString());
            DrawSummaryCard(new Rect(inner.x + (cardWidth + 5f) * 2f, inner.y, cardWidth, cardHeight), "RimChat_ApiDebugCardSuccessRate".Translate(), $"{summary.SuccessRatePercent:F1}%");
            DrawSummaryCard(new Rect(inner.x + (cardWidth + 5f) * 3f, inner.y, cardWidth, cardHeight), "RimChat_ApiDebugCardAverageLatency".Translate(), $"{summary.AverageDurationMs:F0} ms");
            DrawSummaryCard(new Rect(inner.x + (cardWidth + 5f) * 4f, inner.y, cardWidth, cardHeight), "RimChat_ApiDebugCardPriorityShare".Translate(), $"{summary.HighPriorityTokenSharePercent:F1}%");

            Rect telemetryRect = new Rect(inner.x, inner.y + cardHeight + 6f, inner.width, telemetryHeight);
            DrawPromptTelemetryStrip(telemetryRect, promptTelemetry);
        }

        private static void DrawPromptTelemetryStrip(Rect rect, PromptRenderTelemetrySnapshot telemetry)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.15f));
            Widgets.DrawBox(rect);
            string text = "RimChat_ApiDebugScribanTelemetry".Translate(
                telemetry.CacheHitRatePercent.ToString("F1"),
                telemetry.CacheHits.ToString("N0"),
                telemetry.CacheMisses.ToString("N0"),
                telemetry.CacheEvictions.ToString("N0"),
                telemetry.AverageParseMilliseconds.ToString("F3"),
                telemetry.AverageRenderMilliseconds.ToString("F3"));
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            Widgets.Label(rect.ContractedBy(6f, 4f), text);
            Text.Font = oldFont;
            GUI.color = oldColor;
        }

        private void DrawSummaryCard(Rect rect, string label, string value)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f));
            Widgets.DrawBox(rect);
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, 20f), label);
            GUI.color = Color.white;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 32f, rect.width - 12f, 32f), value);
            Text.Font = GameFont.Small;
        }

        private void DrawTrendChart(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f), "RimChat_ApiDebugTrendTitle".Translate());
            Rect chartRect = new Rect(inner.x, inner.y + 26f, inner.width, inner.height - 26f);
            List<AIRequestDebugBucket> buckets = snapshot?.Buckets ?? new List<AIRequestDebugBucket>();
            if (buckets.Count == 0 || buckets.All(bucket => bucket.TotalTokens <= 0))
            {
                GUI.color = Color.gray;
                Widgets.Label(chartRect, "RimChat_ApiDebugNoData".Translate());
                GUI.color = Color.white;
                return;
            }

            DrawTrendBars(chartRect, buckets);
        }

        private void DrawTrendBars(Rect chartRect, List<AIRequestDebugBucket> buckets)
        {
            int maxTokens = Mathf.Max(1, buckets.Max(bucket => Mathf.Max(0, bucket.TotalTokens)));
            int count = buckets.Count;
            float barWidth = Mathf.Max(8f, chartRect.width / Mathf.Max(1, count));
            for (int i = 0; i < count; i++)
            {
                AIRequestDebugBucket bucket = buckets[i];
                float normalized = Mathf.Clamp01((float)bucket.TotalTokens / maxTokens);
                float highPriorityNormalized = bucket.TotalTokens > 0
                    ? Mathf.Clamp01((float)bucket.HighPriorityTokens / maxTokens)
                    : 0f;
                float barHeight = (chartRect.height - 24f) * normalized;
                float highPriorityHeight = (chartRect.height - 24f) * highPriorityNormalized;
                Rect bar = new Rect(chartRect.x + i * barWidth + 2f, chartRect.yMax - 22f - barHeight, barWidth - 4f, barHeight);
                Rect priorityBar = new Rect(bar.x, chartRect.yMax - 22f - highPriorityHeight, bar.width, highPriorityHeight);
                Widgets.DrawBoxSolid(bar, new Color(0.35f, 0.35f, 0.35f, 0.75f));
                Widgets.DrawBoxSolid(priorityBar, new Color(0.2f, 0.65f, 0.95f, 0.95f));

                DateTime localBucketTime = bucket.BucketStartUtc.ToLocalTime();
                bool shouldDrawLabel = localBucketTime.Minute % 5 == 0 || i == 0 || i == count - 1;
                if (shouldDrawLabel)
                {
                    string label = localBucketTime.ToString("HH:mm");
                    TextAnchor oldAnchor = Text.Anchor;
                    Text.Anchor = TextAnchor.UpperCenter;
                    GUI.color = Color.gray;
                    Widgets.Label(new Rect(chartRect.x + i * barWidth, chartRect.yMax - 20f, barWidth, 20f), label);
                    GUI.color = Color.white;
                    Text.Anchor = oldAnchor;
                }
            }
        }

        private List<AIRequestDebugRecord> DrawRecordsTable(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);

            DrawFilterRow(new Rect(inner.x, inner.y, inner.width, 24f));
            DrawCopyButtons(new Rect(inner.x, inner.y + 28f, inner.width, 24f));

            Rect headerRect = new Rect(inner.x, inner.y + 56f, inner.width, 22f);
            DrawTableHeader(headerRect);

            List<AIRequestDebugRecord> filtered = GetFilteredRecords();
            Rect listRect = new Rect(inner.x, headerRect.yMax + 2f, inner.width, inner.yMax - headerRect.yMax - 2f);
            DrawTableRows(listRect, filtered);
            return filtered;
        }

        private void DrawFilterRow(Rect rect)
        {
            float width = (rect.width - 18f) / 7f;
            DrawFilterButton(new Rect(rect.x + (width + 3f) * 0f, rect.y, width, rect.height), "RimChat_ApiDebugFilterAll".Translate(), sourceFilter == SourceFilterMode.All, () => sourceFilter = SourceFilterMode.All);
            DrawFilterButton(new Rect(rect.x + (width + 3f) * 1f, rect.y, width, rect.height), "RimChat_ApiDebugFilterPriority".Translate(), sourceFilter == SourceFilterMode.PriorityOnly, () => sourceFilter = SourceFilterMode.PriorityOnly);
            DrawFilterButton(new Rect(rect.x + (width + 3f) * 2f, rect.y, width, rect.height), "RimChat_ApiDebugFilterBackground".Translate(), sourceFilter == SourceFilterMode.BackgroundOnly, () => sourceFilter = SourceFilterMode.BackgroundOnly);
            DrawFilterButton(new Rect(rect.x + (width + 3f) * 3f, rect.y, width, rect.height), "RimChat_ApiDebugStatusAll".Translate(), statusFilter == StatusFilterMode.All, () => statusFilter = StatusFilterMode.All);
            DrawFilterButton(new Rect(rect.x + (width + 3f) * 4f, rect.y, width, rect.height), "RimChat_ApiDebugStatusSuccess".Translate(), statusFilter == StatusFilterMode.Success, () => statusFilter = StatusFilterMode.Success);
            DrawFilterButton(new Rect(rect.x + (width + 3f) * 5f, rect.y, width, rect.height), "RimChat_ApiDebugStatusError".Translate(), statusFilter == StatusFilterMode.Error, () => statusFilter = StatusFilterMode.Error);
            DrawFilterButton(new Rect(rect.x + (width + 3f) * 6f, rect.y, width, rect.height), "RimChat_ApiDebugStatusCancelled".Translate(), statusFilter == StatusFilterMode.Cancelled, () => statusFilter = StatusFilterMode.Cancelled);
        }

        private static void DrawFilterButton(Rect rect, string label, bool selected, Action onClick)
        {
            Color old = GUI.color;
            GUI.color = selected ? new Color(0.35f, 0.65f, 0.95f) : Color.white;
            if (Widgets.ButtonText(rect, label))
            {
                onClick?.Invoke();
            }

            GUI.color = old;
        }

        private void DrawCopyButtons(Rect rect)
        {
            float rightButtonWidth = 190f;
            Rect copySelectedRect = new Rect(rect.xMax - rightButtonWidth * 2f - 6f, rect.y, rightButtonWidth, rect.height);
            Rect copyFilteredRect = new Rect(rect.xMax - rightButtonWidth, rect.y, rightButtonWidth, rect.height);

            if (Widgets.ButtonText(copySelectedRect, "RimChat_ApiDebugCopySelectedJson".Translate()))
            {
                TryCopySelectedRecordJson();
            }

            if (Widgets.ButtonText(copyFilteredRect, "RimChat_ApiDebugCopyFilteredJson".Translate()))
            {
                TryCopyFilteredJson();
            }
        }

        private void DrawTableHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.18f, 0.18f, 0.18f));
            Rect[] columns = BuildTableColumns(rect);
            DrawTableCell(columns[0], "RimChat_ApiDebugColumnTime".Translate(), Color.gray);
            DrawTableCell(columns[1], "RimChat_ApiDebugColumnSource".Translate(), Color.gray);
            DrawTableCell(columns[2], "RimChat_ApiDebugColumnStatus".Translate(), Color.gray);
            DrawTableCell(columns[3], "RimChat_ApiDebugColumnModel".Translate(), Color.gray);
            DrawTableCell(columns[4], "RimChat_ApiDebugColumnTokens".Translate(), Color.gray);
            DrawTableCell(columns[5], "RimChat_ApiDebugColumnLatency".Translate(), Color.gray);
            DrawTableCell(columns[6], "RimChat_ApiDebugColumnHttp".Translate(), Color.gray);
        }

        private void DrawTableRows(Rect rect, List<AIRequestDebugRecord> records)
        {
            if (records == null || records.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(rect, "RimChat_ApiDebugNoData".Translate());
                GUI.color = Color.white;
                return;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, records.Count * RowHeight);
            Widgets.BeginScrollView(rect, ref listScrollPosition, viewRect);
            for (int i = 0; i < records.Count; i++)
            {
                DrawTableRow(new Rect(0f, i * RowHeight, viewRect.width, RowHeight), records[i]);
            }

            Widgets.EndScrollView();
        }

        private void DrawTableRow(Rect rect, AIRequestDebugRecord record)
        {
            bool selected = string.Equals(selectedRequestId, record.RequestId, StringComparison.Ordinal);
            Color rowBackground = selected ? new Color(0.16f, 0.28f, 0.44f, 0.95f) : new Color(0f, 0f, 0f, 0f);
            if (selected)
            {
                Widgets.DrawBoxSolid(rect, rowBackground);
            }
            else if (Mouse.IsOver(rect))
            {
                Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 0.2f));
            }

            Color textColor = record.IsHighPrioritySource ? Color.white : new Color(0.62f, 0.62f, 0.62f);
            Rect[] columns = BuildTableColumns(rect);
            DrawTableCell(columns[0], record.RecordedAtUtc.ToLocalTime().ToString("HH:mm:ss"), textColor);
            DrawTableCell(columns[1], GetSourceLabel(record.Source), textColor);
            DrawTableCell(columns[2], GetStatusLabel(record.Status), GetStatusColor(record.Status, textColor));
            DrawTableCell(columns[3], Shorten(record.Model, CalculateMaxChars(columns[3].width, 7f)), textColor);
            DrawTableCell(columns[4], record.TotalTokens.ToString("N0"), textColor);
            DrawTableCell(columns[5], $"{record.DurationMs} ms", textColor);
            DrawTableCell(columns[6], record.HttpStatusCode > 0 ? record.HttpStatusCode.ToString() : "-", textColor);

            if (Widgets.ButtonInvisible(rect))
            {
                selectedRequestId = record.RequestId;
            }
        }

        private static void DrawTableCell(Rect rect, string text, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            Widgets.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 2f), text ?? string.Empty);
            GUI.color = old;
        }

        private static Rect[] BuildTableColumns(Rect rect)
        {
            var columns = new Rect[TableColumnWeights.Length];
            float x = rect.x;
            for (int i = 0; i < TableColumnWeights.Length; i++)
            {
                float width = i == TableColumnWeights.Length - 1
                    ? rect.xMax - x
                    : Mathf.Floor(rect.width * TableColumnWeights[i]);
                width = Mathf.Max(24f, width);
                columns[i] = new Rect(x, rect.y, width, rect.height);
                x += width;
            }

            return columns;
        }

        private static int CalculateMaxChars(float width, float avgCharWidth)
        {
            int chars = Mathf.FloorToInt(Mathf.Max(6f, width - 10f) / Mathf.Max(1f, avgCharWidth));
            return Mathf.Clamp(chars, 6, 64);
        }

        private void DrawDetailPanel(Rect rect, List<AIRequestDebugRecord> filtered)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f), "RimChat_ApiDebugDetailTitle".Translate());
            AIRequestDebugRecord selected = GetSelectedRecord(filtered);
            if (selected == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(inner.x, inner.y + 28f, inner.width, inner.height - 28f), "RimChat_ApiDebugNoSelection".Translate());
                GUI.color = Color.white;
                return;
            }

            string content = BuildDetailText(selected);
            Rect scrollRect = new Rect(inner.x, inner.y + 28f, inner.width, inner.height - 28f);
            float contentHeight = Mathf.Max(scrollRect.height, Text.CalcHeight(content, scrollRect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, scrollRect.width - 16f, contentHeight);
            Widgets.BeginScrollView(scrollRect, ref detailScrollPosition, viewRect);
            Widgets.Label(new Rect(0f, 0f, viewRect.width, viewRect.height), content);
            Widgets.EndScrollView();
        }

        private string BuildDetailText(AIRequestDebugRecord record)
        {
            var sb = new StringBuilder();
            AppendDetailField(sb, "RimChat_ApiDebugColumnTime".Translate().ToString(), record.RecordedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            AppendDetailField(sb, "RimChat_ApiDebugColumnSource".Translate().ToString(), GetSourceLabel(record.Source));
            AppendDetailField(sb, "RimChat_ApiDebugColumnStatus".Translate().ToString(), GetStatusLabel(record.Status));
            AppendDetailField(sb, "RimChat_ApiDebugColumnModel".Translate().ToString(), record.Model ?? string.Empty);
            AppendDetailField(sb, "RimChat_ApiDebugColumnTokens".Translate().ToString(), record.TotalTokens.ToString("N0"));
            AppendDetailField(sb, "Prompt", record.PromptTokens.ToString("N0"));
            AppendDetailField(sb, "Completion", record.CompletionTokens.ToString("N0"));
            AppendDetailField(sb, "Estimated", record.IsEstimatedTokens ? "true" : "false");
            AppendDetailField(sb, "RimChat_ApiDebugColumnLatency".Translate().ToString(), record.DurationMs + " ms");
            AppendDetailField(sb, "RimChat_ApiDebugColumnHttp".Translate().ToString(), record.HttpStatusCode > 0 ? record.HttpStatusCode.ToString() : "-");
            if (!string.IsNullOrWhiteSpace(record.ErrorText))
            {
                AppendDetailField(sb, "RimChat_ApiDebugErrorLabel".Translate().ToString(), record.ErrorText);
            }

            sb.AppendLine();
            sb.AppendLine("=== " + "RimChat_ApiDebugRequestLabel".Translate() + " ===");
            sb.AppendLine(FormatPayloadForDetail(record.RequestText));
            sb.AppendLine();
            sb.AppendLine("=== " + "RimChat_ApiDebugResponseLabel".Translate() + " ===");
            sb.AppendLine(FormatPayloadForDetail(record.ResponseText));
            return sb.ToString();
        }

        private static void AppendDetailField(StringBuilder sb, string key, string value)
        {
            sb.Append('[')
                .Append(key ?? string.Empty)
                .Append("] ")
                .AppendLine(value ?? string.Empty);
        }

        private static string FormatPayloadForDetail(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return "RimChat_ApiDebugEmptyPayload".Translate().ToString();
            }

            string text = payload.Trim();
            text = WebUtility.HtmlDecode(text);
            string pretty = TryPrettyPrintJson(text);
            if (!string.IsNullOrWhiteSpace(pretty))
            {
                text = pretty;
            }

            text = text
                .Replace("\\r\\n", "\n")
                .Replace("\\n", "\n")
                .Replace("\\r", "\n")
                .Replace("\\t", "    ")
                .Replace("\\\"", "\"");

            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return text;
        }

        private static string TryPrettyPrintJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }

            string source = json.Trim();
            if (source.Length < 2)
            {
                return source;
            }

            char first = source[0];
            if (first != '{' && first != '[')
            {
                return source;
            }

            try
            {
                var sb = new StringBuilder(source.Length + 64);
                int indent = 0;
                bool inString = false;
                bool escaped = false;
                for (int i = 0; i < source.Length; i++)
                {
                    char ch = source[i];
                    if (inString)
                    {
                        sb.Append(ch);
                        if (escaped)
                        {
                            escaped = false;
                        }
                        else if (ch == '\\')
                        {
                            escaped = true;
                        }
                        else if (ch == '"')
                        {
                            inString = false;
                        }

                        continue;
                    }

                    switch (ch)
                    {
                        case '"':
                            inString = true;
                            sb.Append(ch);
                            break;
                        case '{':
                        case '[':
                            sb.Append(ch);
                            sb.Append('\n');
                            indent++;
                            AppendIndent(sb, indent);
                            break;
                        case '}':
                        case ']':
                            sb.Append('\n');
                            indent = Math.Max(0, indent - 1);
                            AppendIndent(sb, indent);
                            sb.Append(ch);
                            break;
                        case ',':
                            sb.Append(ch);
                            sb.Append('\n');
                            AppendIndent(sb, indent);
                            break;
                        case ':':
                            sb.Append(": ");
                            break;
                        case '\r':
                        case '\n':
                        case '\t':
                            break;
                        default:
                            sb.Append(ch);
                            break;
                    }
                }

                return sb.ToString();
            }
            catch
            {
                return source;
            }
        }

        private static void AppendIndent(StringBuilder sb, int indent)
        {
            for (int i = 0; i < indent; i++)
            {
                sb.Append("  ");
            }
        }

        private List<AIRequestDebugRecord> GetFilteredRecords()
        {
            IEnumerable<AIRequestDebugRecord> query = snapshot?.Records ?? Enumerable.Empty<AIRequestDebugRecord>();
            switch (sourceFilter)
            {
                case SourceFilterMode.PriorityOnly:
                    query = query.Where(record => record.IsHighPrioritySource);
                    break;
                case SourceFilterMode.BackgroundOnly:
                    query = query.Where(record => !record.IsHighPrioritySource);
                    break;
            }

            switch (statusFilter)
            {
                case StatusFilterMode.Success:
                    query = query.Where(record => record.Status == AIRequestDebugStatus.Success);
                    break;
                case StatusFilterMode.Error:
                    query = query.Where(record => record.Status == AIRequestDebugStatus.Error);
                    break;
                case StatusFilterMode.Cancelled:
                    query = query.Where(record => record.Status == AIRequestDebugStatus.Cancelled);
                    break;
            }

            return query.ToList();
        }

        private AIRequestDebugRecord GetSelectedRecord(List<AIRequestDebugRecord> filtered)
        {
            if (filtered == null || filtered.Count == 0)
            {
                return null;
            }

            return filtered.FirstOrDefault(record => string.Equals(record.RequestId, selectedRequestId, StringComparison.Ordinal))
                ?? filtered[0];
        }

        private static string GetSourceLabel(AIRequestDebugSource source)
        {
            switch (source)
            {
                case AIRequestDebugSource.DiplomacyDialogue:
                    return "RimChat_ApiDebugSourceDiplomacyDialogue".Translate();
                case AIRequestDebugSource.RpgDialogue:
                    return "RimChat_ApiDebugSourceRpgDialogue".Translate();
                case AIRequestDebugSource.NpcPush:
                    return "RimChat_ApiDebugSourceNpcPush".Translate();
                case AIRequestDebugSource.PawnRpgPush:
                    return "RimChat_ApiDebugSourcePawnRpgPush".Translate();
                case AIRequestDebugSource.SocialNews:
                    return "RimChat_ApiDebugSourceSocialNews".Translate();
                case AIRequestDebugSource.StrategySuggestion:
                    return "RimChat_ApiDebugSourceStrategySuggestion".Translate();
                case AIRequestDebugSource.PersonaBootstrap:
                    return "RimChat_ApiDebugSourcePersonaBootstrap".Translate();
                case AIRequestDebugSource.MemorySummary:
                    return "RimChat_ApiDebugSourceMemorySummary".Translate();
                case AIRequestDebugSource.ArchiveCompression:
                    return "RimChat_ApiDebugSourceArchiveCompression".Translate();
                case AIRequestDebugSource.SendImage:
                    return "RimChat_ApiDebugSourceSendImage".Translate();
                case AIRequestDebugSource.ApiUsabilityTest:
                    return "RimChat_ApiDebugSourceApiUsabilityTest".Translate();
                case AIRequestDebugSource.AirdropSelection:
                    return "RimChat_ApiDebugSourceAirdropSelection".Translate();
                default:
                    return "RimChat_ApiDebugSourceOther".Translate();
            }
        }

        private static string GetStatusLabel(AIRequestDebugStatus status)
        {
            switch (status)
            {
                case AIRequestDebugStatus.Success:
                    return "RimChat_ApiDebugStatusSuccess".Translate();
                case AIRequestDebugStatus.Cancelled:
                    return "RimChat_ApiDebugStatusCancelled".Translate();
                default:
                    return "RimChat_ApiDebugStatusError".Translate();
            }
        }

        private static Color GetStatusColor(AIRequestDebugStatus status, Color fallback)
        {
            switch (status)
            {
                case AIRequestDebugStatus.Success:
                    return new Color(0.42f, 0.9f, 0.42f);
                case AIRequestDebugStatus.Error:
                    return new Color(1f, 0.47f, 0.47f);
                case AIRequestDebugStatus.Cancelled:
                    return new Color(0.95f, 0.83f, 0.42f);
                default:
                    return fallback;
            }
        }

        private static string Shorten(string value, int maxLength)
        {
            string text = value ?? string.Empty;
            if (text.Length <= maxLength || maxLength <= 3)
            {
                return text;
            }

            return text.Substring(0, maxLength - 3) + "...";
        }

        private void TryCopySelectedRecordJson()
        {
            AIRequestDebugRecord record = GetSelectedRecord(GetFilteredRecords());
            if (record == null)
            {
                Messages.Message("RimChat_ApiDebugCopyNoSelection".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            GUIUtility.systemCopyBuffer = BuildRecordJson(record);
            Messages.Message("RimChat_ApiDebugCopySelectedSuccess".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        private void TryCopyFilteredJson()
        {
            List<AIRequestDebugRecord> filtered = GetFilteredRecords();
            GUIUtility.systemCopyBuffer = BuildFilteredJson(filtered);
            Messages.Message("RimChat_ApiDebugCopyFilteredSuccess".Translate(filtered.Count.ToString()), MessageTypeDefOf.TaskCompletion, false);
        }

        private string BuildFilteredJson(List<AIRequestDebugRecord> records)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"generatedAtUtc\":\"").Append(EscapeJson((snapshot?.GeneratedAtUtc ?? DateTime.UtcNow).ToString("o"))).Append("\",\n");
            sb.Append("  \"windowMinutes\":").Append(snapshot?.WindowMinutes ?? 60).Append(",\n");
            sb.Append("  \"count\":").Append(records?.Count ?? 0).Append(",\n");
            sb.Append("  \"records\":[\n");
            if (records != null)
            {
                for (int i = 0; i < records.Count; i++)
                {
                    sb.Append(IndentRecordJson(BuildRecordJson(records[i]), "    "));
                    if (i < records.Count - 1)
                    {
                        sb.Append(',');
                    }

                    sb.Append('\n');
                }
            }

            sb.Append("  ]\n");
            sb.Append("}");
            return sb.ToString();
        }

        private static string BuildRecordJson(AIRequestDebugRecord record)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"requestId\":\"").Append(EscapeJson(record?.RequestId ?? string.Empty)).Append("\",\n");
            sb.Append("  \"recordedAtUtc\":\"").Append(EscapeJson((record?.RecordedAtUtc ?? DateTime.UtcNow).ToString("o"))).Append("\",\n");
            sb.Append("  \"source\":\"").Append(EscapeJson(record?.Source.ToString() ?? AIRequestDebugSource.Other.ToString())).Append("\",\n");
            sb.Append("  \"channel\":\"").Append(EscapeJson(record?.Channel.ToString() ?? DialogueUsageChannel.Unknown.ToString())).Append("\",\n");
            sb.Append("  \"model\":\"").Append(EscapeJson(record?.Model ?? string.Empty)).Append("\",\n");
            sb.Append("  \"status\":\"").Append(EscapeJson(record?.Status.ToString() ?? AIRequestDebugStatus.Error.ToString())).Append("\",\n");
            sb.Append("  \"durationMs\":").Append(record?.DurationMs ?? 0).Append(",\n");
            sb.Append("  \"httpStatusCode\":").Append(record?.HttpStatusCode ?? 0).Append(",\n");
            sb.Append("  \"promptTokens\":").Append(record?.PromptTokens ?? 0).Append(",\n");
            sb.Append("  \"completionTokens\":").Append(record?.CompletionTokens ?? 0).Append(",\n");
            sb.Append("  \"totalTokens\":").Append(record?.TotalTokens ?? 0).Append(",\n");
            sb.Append("  \"isEstimatedTokens\":").Append((record?.IsEstimatedTokens ?? false) ? "true" : "false").Append(",\n");
            sb.Append("  \"contractValidationStatus\":\"").Append(EscapeJson(record?.ContractValidationStatus ?? string.Empty)).Append("\",\n");
            sb.Append("  \"contractRetryCount\":").Append(record?.ContractRetryCount ?? 0).Append(",\n");
            sb.Append("  \"contractFailureReason\":\"").Append(EscapeJson(record?.ContractFailureReason ?? string.Empty)).Append("\",\n");
            sb.Append("  \"errorText\":\"").Append(EscapeJson(record?.ErrorText ?? string.Empty)).Append("\",\n");
            sb.Append("  \"requestText\":\"").Append(EscapeJson(record?.RequestText ?? string.Empty)).Append("\",\n");
            sb.Append("  \"responseText\":\"").Append(EscapeJson(record?.ResponseText ?? string.Empty)).Append("\"\n");
            sb.Append("}");
            return sb.ToString();
        }

        private static string IndentRecordJson(string json, string indent)
        {
            string[] lines = (json ?? string.Empty).Split(new[] { '\n' }, StringSplitOptions.None);
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append(indent).Append(lines[i]);
                if (i < lines.Length - 1)
                {
                    sb.Append('\n');
                }
            }

            return sb.ToString();
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                switch (ch)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(ch))
                        {
                            sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(ch);
                        }

                        break;
                }
            }

            return sb.ToString();
        }
    }
}

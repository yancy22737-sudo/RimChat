using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Prompting;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: prompt workspace routing, shared variable browser, and prompt runtime variable catalog.
    /// Responsibility: render the RimTalk bridge page with provider status, shared variables, and summary/persona settings.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private void DrawRimTalkBridgePage(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.12f));
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, 980f);
            Widgets.BeginScrollView(rect, ref _rimTalkTabScroll, viewRect);

            float y = 0f;
            DrawRimTalkBridgeCard(viewRect, ref y);
            DrawRimTalkSummaryPersonaCard(viewRect, ref y);

            Widgets.EndScrollView();
        }

        private void DrawRimTalkBridgeCard(Rect viewRect, ref float y)
        {
            Rect card = new Rect(0f, y, viewRect.width, 220f);
            DrawSectionCard(card, "RimChat_RimTalkBridgeCardTitle".Translate(), "RimChat_RimTalkBridgeCardHint".Translate());
            Rect inner = card.ContractedBy(12f);
            float contentY = inner.y + 46f;

            DrawBridgeStatusLine(new Rect(inner.x, contentY, inner.width, 24f), "rimchat.core", "RimChat Core");
            contentY += 28f;
            DrawBridgeStatusLine(new Rect(inner.x, contentY, inner.width, 24f), "rimtalk.bridge", "RimTalk Bridge");
            contentY += 28f;
            DrawBridgeStatusLine(new Rect(inner.x, contentY, inner.width, 24f), "rimtalk.memorypatch", "MemoryPatch Bridge");
            contentY += 36f;

            Rect diplomacyRect = new Rect(inner.x, contentY, 220f, 30f);
            Rect rpgRect = new Rect(diplomacyRect.xMax + 8f, contentY, 220f, 30f);
            if (Widgets.ButtonText(diplomacyRect, "RimChat_RimTalkOpenDiplomacySections".Translate()))
            {
                OpenPromptWorkbenchWindow();
            }

            if (Widgets.ButtonText(rpgRect, "RimChat_RimTalkOpenRpgSections".Translate()))
            {
                OpenPromptWorkbenchWindowForRpg();
            }

            y = card.yMax + 8f;
        }

        private void DrawBridgeStatusLine(Rect rect, string sourceId, string fallbackLabel)
        {
            List<PromptVariableDisplayEntry> entries = PromptVariableCatalog.GetDisplayEntries()
                .Where(item => string.Equals(item.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            bool available = entries.Any(item => item.IsAvailable);
            string sourceLabel = entries.FirstOrDefault()?.SourceLabel ?? fallbackLabel;
            string availability = available
                ? "RimChat_PromptVariableReady".Translate().ToString()
                : "RimChat_PromptVariableDependencyMissing".Translate().ToString();
            string countText = $"({entries.Count})";

            GUI.color = available ? new Color(0.6f, 0.95f, 0.6f) : new Color(1f, 0.65f, 0.65f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width * 0.6f, rect.height), sourceLabel);
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x + rect.width * 0.6f, rect.y, rect.width * 0.4f, rect.height), availability + " " + countText);
            GUI.color = Color.white;
        }

        private void DrawRimTalkSummaryPersonaCard(Rect viewRect, ref float y)
        {
            Rect card = new Rect(0f, y, viewRect.width, 320f);
            DrawSectionCard(card, "RimChat_RimTalkSummaryPersonaTitle".Translate(), "RimChat_RimTalkSummaryPersonaHint".Translate());
            Rect inner = card.ContractedBy(12f);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(new Rect(inner.x, inner.y + 46f, inner.width, inner.height - 46f));

            bool autoPushSummary = RimTalkAutoPushSessionSummary;
            listing.CheckboxLabeled("RimChat_RimTalkAutoPushSummary".Translate(), ref autoPushSummary);
            RimTalkAutoPushSessionSummary = autoPushSummary;

            bool autoInjectPreset = RimTalkAutoInjectCompatPreset;
            listing.CheckboxLabeled("RimChat_RimTalkAutoInjectPreset".Translate(), ref autoInjectPreset);
            RimTalkAutoInjectCompatPreset = autoInjectPreset;

            listing.Label("RimChat_RimTalkSummaryHistoryLimit".Translate(GetRimTalkSummaryHistoryLimitClamped()));
            string editedHistory = listing.TextEntry(RimTalkSummaryHistoryLimit.ToString());
            if (int.TryParse(editedHistory, out int parsedHistory))
            {
                RimTalkSummaryHistoryLimit = Mathf.Clamp(parsedHistory, RimTalkSummaryHistoryMin, RimTalkSummaryHistoryMax);
            }

            listing.Label("RimChat_RimTalkPersonaCopyTemplate".Translate());
            RimTalkPersonaCopyTemplate = Widgets.TextField(listing.GetRect(26f), RimTalkPersonaCopyTemplate ?? DefaultRimTalkPersonaCopyTemplate);
            GUI.color = Color.gray;
            listing.Label("RimChat_RimTalkPersonaCopyTemplateHint".Translate());
            GUI.color = Color.white;
            listing.End();

            y = card.yMax + 8f;
        }

        private static void DrawSectionCard(Rect rect, string title, string hint)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.09f, 0.11f));
            Rect inner = rect.ContractedBy(12f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), title);
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inner.x, inner.y + 24f, inner.width, 20f), hint);
            GUI.color = Color.white;
        }
    }
}

using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: AI settings section labels and toggle fields.
    /// Responsibility: lightweight UX helpers for AI settings tab header tools.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private string _aiQuickSearchQuery = string.Empty;

        private void DrawAIControlHeaderBar(Listing_Standard listing)
        {
            if (listing == null)
            {
                return;
            }
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            DrawAIQuickSummary(listing);
            DrawAIQuickSearch(listing);
            DrawAIQuickJumpButtons(listing);

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        private void DrawAIQuickSummary(Listing_Standard listing)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            Rect cardRect = listing.GetRect(52f);
            Widgets.DrawBoxSolid(cardRect, new Color(0.10f, 0.14f, 0.20f, 0.55f));
            Widgets.DrawBox(cardRect);

            Rect labelRect = new Rect(cardRect.x + 6f, cardRect.y + 2f, cardRect.width - 12f, 22f);
            GUI.color = new Color(0.75f, 0.9f, 1f);
            Widgets.Label(labelRect, "RimChat_AIQuickTools".Translate());

            int enabledActions = GetEnabledAIBehaviorActionCount();
            GUI.color = Color.white;
            Rect infoRect = new Rect(cardRect.x + 6f, cardRect.y + 24f, cardRect.width - 12f, 22f);
            Widgets.Label(
                infoRect,
                "RimChat_AIQuickSummary".Translate(
                    "RimChat_AIQuickEnabledActions".Translate(enabledActions, 7),
                    "RimChat_AIQuickApiLimit".Translate(MaxAPICallsPerHour)));

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        private void DrawAIQuickSearch(Listing_Standard listing)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            Rect rowRect = listing.GetRect(28f);
            float actionWidth = 70f;
            Rect searchRect = new Rect(rowRect.x, rowRect.y, rowRect.width - actionWidth * 2f - 8f, rowRect.height);
            Rect goRect = new Rect(searchRect.xMax + 4f, rowRect.y, actionWidth, rowRect.height);
            Rect clearRect = new Rect(goRect.xMax + 4f, rowRect.y, actionWidth, rowRect.height);

            _aiQuickSearchQuery = Widgets.TextField(searchRect, _aiQuickSearchQuery ?? string.Empty);
            if (string.IsNullOrWhiteSpace(_aiQuickSearchQuery))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                Widgets.Label(searchRect.ContractedBy(4f, 2f), "RimChat_AIQuickSearchPlaceholder".Translate());
                GUI.color = Color.white;
            }

            if (Widgets.ButtonText(goRect, "RimChat_AIQuickSearchGo".Translate()))
            {
                TryExpandSectionFromSearch(_aiQuickSearchQuery);
            }

            if (Widgets.ButtonText(clearRect, "RimChat_AIQuickSearchClear".Translate()))
            {
                _aiQuickSearchQuery = string.Empty;
            }

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        private void DrawAIQuickJumpButtons(Listing_Standard listing)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            Rect row = listing.GetRect(26f);
            float width = (row.width - 9f) / 4f;
            if (Widgets.ButtonText(new Rect(row.x, row.y, width, row.height), "RimChat_AIQuickJumpBehavior".Translate()))
            {
                ToggleAIControlSection(AIControlSection.AIBehaviorSettings);
            }

            if (Widgets.ButtonText(new Rect(row.x + width + 3f, row.y, width, row.height), "RimChat_AIQuickJumpPresence".Translate()))
            {
                ToggleAIControlSection(AIControlSection.PresenceSettings);
            }

            if (Widgets.ButtonText(new Rect(row.x + (width + 3f) * 2f, row.y, width, row.height), "RimChat_AIQuickJumpNpcPush".Translate()))
            {
                ToggleAIControlSection(AIControlSection.NpcPushSettings);
            }

            if (Widgets.ButtonText(new Rect(row.x + (width + 3f) * 3f, row.y, width, row.height), "RimChat_AIQuickJumpSecurity".Translate()))
            {
                ToggleAIControlSection(AIControlSection.SecuritySettings);
            }

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        private int GetEnabledAIBehaviorActionCount()
        {
            int count = 0;
            if (EnableAIGoodwillAdjustment) count++;
            if (EnableAIGiftSending) count++;
            if (EnableAIWarDeclaration) count++;
            if (EnableAIPeaceMaking) count++;
            if (EnableAITradeCaravan) count++;
            if (EnableAIAidRequest) count++;
            if (EnableAIRaidRequest) count++;
            return count;
        }

        private void TryExpandSectionFromSearch(string query)
        {
            string normalized = query?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (TryMatchSection(normalized, "RimChat_UISettings", AIControlSection.UISettings)) return;
            if (TryMatchSection(normalized, "RimChat_PresenceSettings", AIControlSection.PresenceSettings)) return;
            if (TryMatchSection(normalized, "RimChat_NpcPushSettings", AIControlSection.NpcPushSettings)) return;
            if (TryMatchSection(normalized, "RimChat_AIBehaviorSettings", AIControlSection.AIBehaviorSettings)) return;
            if (TryMatchSection(normalized, "RimChat_RaidSettings", AIControlSection.RaidSettings)) return;
            if (TryMatchSection(normalized, "RimChat_GoodwillSettings", AIControlSection.GoodwillSettings)) return;
            if (TryMatchSection(normalized, "RimChat_GiftSettings", AIControlSection.GiftSettings)) return;
            if (TryMatchSection(normalized, "RimChat_AidSettings", AIControlSection.AidSettings)) return;
            if (TryMatchSection(normalized, "RimChat_WarPeaceSettings", AIControlSection.WarPeaceSettings)) return;
            if (TryMatchSection(normalized, "RimChat_CaravanSettings", AIControlSection.CaravanSettings)) return;
            if (TryMatchSection(normalized, "RimChat_QuestSettings", AIControlSection.QuestSettings)) return;
            if (TryMatchSection(normalized, "RimChat_SocialCircleSettings", AIControlSection.SocialCircleSettings)) return;
            if (TryMatchSection(normalized, "RimChat_SecuritySettings", AIControlSection.SecuritySettings)) return;

            Messages.Message("RimChat_AIQuickNoMatch".Translate(normalized), MessageTypeDefOf.RejectInput, false);
        }

        private bool TryMatchSection(string query, string key, AIControlSection section)
        {
            string label = key.Translate().ToString();
            if (label.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            expandedAIControlSection = section;
            return true;
        }
    }
}

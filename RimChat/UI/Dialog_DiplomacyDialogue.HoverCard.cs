using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: diplomacy dialogue faction/speaker state, RimWorld faction/pawn/world data, and portrait rendering.
 /// Responsibility: render faction and speaker hover cards with goodwill-gated intel reveal in diplomacy dialogue.
 ///</summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private const float HoverCardWidth = 360f;
        private const float HoverCardPortraitSize = 96f;
        private const float HoverCardPadding = 12f;
        private const float HoverCardSectionGap = 8f;
        private const float HoverCardLineHeight = 22f;
        private const float HoverCardRevealDuration = 0.16f;
        private const float HoverCardShowDelay = 0.10f;
        private const float HoverCardHoldDuration = 0.25f;
        private const float HoverCardExpandMargin = 5f;
        private const int GoodwillRevealTierHostile = -60;
        private const int GoodwillRevealTierLow = 0;
        private const int GoodwillRevealTierMedium = 40;
        private const int GoodwillRevealTierHigh = 75;
        private const float HoverCardObscuredAlpha = 0.82f;
        private readonly Dictionary<string, float> hoverCardAlphaByKey = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> hoverCardLastHoverTimeByKey = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> hoverStickyFramesByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        private string lastHoveredAvatarKey;
        private Rect lastHoveredAvatarRect;
        private Faction lastHoveredAvatarFaction;
        private Pawn lastHoveredAvatarPawn;
        private bool isCurrentlyHovered;
        private const int HoverStickyFramesMax = 8;
        private static readonly HashSet<string> HoverCardDrawLoggedKeys = new HashSet<string>(StringComparer.Ordinal);

        private enum FactionIntelRevealTier
        {
            Hostile = 0,
            Low = 1,
            Medium = 2,
            High = 3
        }

        private sealed class HoverCardLine
        {
            public string Label;
            public string Value;
            public bool IsObscured;
        }

        private void DrawFactionHoverCard(Faction targetFaction, Rect anchorRect)
        {
            if (targetFaction == null)
            {
                return;
            }

            Rect expandedRect = anchorRect.ExpandedBy(HoverCardExpandMargin);
            bool isHovered = IsContentRectUnderMouse(expandedRect);
            float alpha = UpdateHoverCardAlpha($"faction:{targetFaction.loadID}", isHovered);
            if (alpha <= 0.01f)
            {
                return;
            }

            DrawHoverCardForFaction(targetFaction, null, anchorRect, alpha);
        }

        private void DrawSpeakerHoverCard(Pawn speakerPawn, Faction targetFaction, Rect anchorRect)
        {
            Faction resolvedFaction = targetFaction ?? speakerPawn?.Faction;
            if (speakerPawn == null && resolvedFaction == null)
            {
                return;
            }

            string key = speakerPawn != null
                ? $"speaker:{speakerPawn.thingIDNumber}"
                : $"speaker-faction:{resolvedFaction?.loadID ?? -1}";
            Rect expandedRect = anchorRect.ExpandedBy(HoverCardExpandMargin);
            bool isHovered = IsContentRectUnderMouse(expandedRect);
            float alpha = UpdateHoverCardAlpha(key, isHovered);

            if (alpha <= 0.01f)
            {
                return;
            }

            DrawHoverCardForFaction(resolvedFaction, speakerPawn, anchorRect, alpha);
        }

        private static bool IsContentRectUnderMouse(Rect contentRect)
        {
            return Mouse.IsOver(contentRect);
        }

        private float UpdateHoverCardAlpha(string key, bool hovered)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return 0f;
            }

            float current = hoverCardAlphaByKey.TryGetValue(key, out float value) ? value : 0f;
            float lastHoverTime = hoverCardLastHoverTimeByKey.TryGetValue(key, out float lht) ? lht : 0f;
            int stickyFrames = hoverStickyFramesByKey.TryGetValue(key, out int sf) ? sf : 0;
            float now = Time.realtimeSinceStartup;

            if (hovered)
            {
                hoverStickyFramesByKey[key] = HoverStickyFramesMax;
                hoverCardLastHoverTimeByKey[key] = now;
                float speed = Time.unscaledDeltaTime / HoverCardRevealDuration;
                float next = Mathf.MoveTowards(current, 1f, speed);
                hoverCardAlphaByKey[key] = next;
                return next;
            }

            if (stickyFrames > 0)
            {
                hoverStickyFramesByKey[key] = stickyFrames - 1;
                return current;
            }

            float elapsedSinceHover = now - lastHoverTime;
            if (elapsedSinceHover < HoverCardHoldDuration)
            {
                return current;
            }

            float speedFade = Time.unscaledDeltaTime / HoverCardRevealDuration;
            float nextFade = Mathf.MoveTowards(current, 0f, speedFade);
            hoverCardAlphaByKey[key] = nextFade;
            return nextFade;
        }

        private void CleanupHoverCardAlpha(IEnumerable<string> activeKeys)
        {
            if (activeKeys == null)
            {
                return;
            }

            HashSet<string> keep = new HashSet<string>(activeKeys, StringComparer.Ordinal);
            List<string> staleKeys = hoverCardAlphaByKey.Keys.Where(key => !keep.Contains(key)).ToList();
            for (int i = 0; i < staleKeys.Count; i++)
            {
                hoverCardAlphaByKey.Remove(staleKeys[i]);
                hoverCardLastHoverTimeByKey.Remove(staleKeys[i]);
            }
        }

        private void DrawHoverCardForFaction(Faction targetFaction, Pawn explicitPawn, Rect anchorRect, float alpha)
        {
            if (targetFaction == null && explicitPawn == null)
            {
                return;
            }

            Pawn subjectPawn = ResolveHoverCardPawn(targetFaction, explicitPawn) ?? explicitPawn;
            Faction fallbackFaction = targetFaction ?? subjectPawn?.Faction ?? faction;
            if (fallbackFaction == null)
            {
                return;
            }

            if (fallbackFaction.IsPlayer)
            {
                DrawHoverCardForPlayerPawn(subjectPawn, anchorRect, alpha);
                return;
            }

            FactionIntelRevealTier tier = ResolveFactionRevealTier(fallbackFaction.PlayerGoodwill);
            List<HoverCardLine> lines = BuildHoverCardLines(fallbackFaction, subjectPawn, tier);
            float contentHeight = CalculateHoverCardContentHeight(lines, fallbackFaction);
            Rect cardRect = BuildHoverCardRect(anchorRect, contentHeight);

            DrawHoverCardChrome(cardRect, alpha, GetGoodwillColor(fallbackFaction.PlayerGoodwill));
            DrawHoverCardContent(cardRect, fallbackFaction, subjectPawn, lines, tier, alpha);
        }

        private void DrawHoverCardForPlayerPawn(Pawn pawn, Rect anchorRect, float alpha)
        {
            if (pawn == null)
            {
                return;
            }

            List<HoverCardLine> lines = new List<HoverCardLine>();
            AddHoverCardLine(lines, "RimChat_HoverCardBio".Translate(), pawn.LabelShort ?? "Colonist".Translate(), true);
            AddHoverCardLine(lines, "RimChat_HoverCardFaction".Translate(), "Your Colonist".Translate(), true);

            float contentHeight = CalculateHoverCardContentHeightPlayer(lines);
            Rect cardRect = BuildHoverCardRect(anchorRect, contentHeight);

            DrawHoverCardChrome(cardRect, alpha, Color.white);
            DrawHoverCardContent(cardRect, null, pawn, lines, FactionIntelRevealTier.High, alpha);
        }

        private static float CalculateHoverCardContentHeightPlayer(List<HoverCardLine> lines)
        {
            int count = lines?.Count ?? 0;
            return HoverCardPadding * 2f + HoverCardPortraitSize + HoverCardSectionGap + count * HoverCardLineHeight + 20f;
        }

        private Pawn ResolveHoverCardPawn(Faction targetFaction, Pawn explicitPawn)
        {
            if (IsEligibleSpeakerPawn(explicitPawn, targetFaction))
            {
                return explicitPawn;
            }

            if (IsEligibleSpeakerPawn(targetFaction?.leader, targetFaction))
            {
                return targetFaction.leader;
            }

            if (IsEligibleSpeakerPawn(sessionFallbackFactionSpeaker, targetFaction))
            {
                return sessionFallbackFactionSpeaker;
            }

            return null;
        }

        private static FactionIntelRevealTier ResolveFactionRevealTier(int goodwill)
        {
            if (goodwill >= GoodwillRevealTierHigh)
            {
                return FactionIntelRevealTier.High;
            }
            if (goodwill >= GoodwillRevealTierMedium)
            {
                return FactionIntelRevealTier.Medium;
            }
            if (goodwill >= GoodwillRevealTierLow)
            {
                return FactionIntelRevealTier.Low;
            }
            return FactionIntelRevealTier.Hostile;
        }

        private List<HoverCardLine> BuildHoverCardLines(Faction targetFaction, Pawn subjectPawn, FactionIntelRevealTier tier)
        {
            List<HoverCardLine> lines = new List<HoverCardLine>();
            AddHoverCardLine(lines, "RimChat_HoverCardBio".Translate(), BuildFactionBioText(targetFaction), tier >= FactionIntelRevealTier.Low);
            AddHoverCardLine(lines, "RimChat_HoverCardIdentity".Translate(), BuildIdentityText(targetFaction, subjectPawn), tier >= FactionIntelRevealTier.Low);
            AddHoverCardLine(lines, "RimChat_HoverCardAge".Translate(), BuildAgeText(subjectPawn), tier >= FactionIntelRevealTier.Medium);
            AddHoverCardLine(lines, "RimChat_HoverCardGender".Translate(), BuildGenderText(subjectPawn), tier >= FactionIntelRevealTier.Low);
            AddHoverCardLine(lines, "RimChat_HoverCardRace".Translate(), BuildRaceText(subjectPawn), tier >= FactionIntelRevealTier.Medium);
            AddHoverCardLine(lines, "RimChat_HoverCardFaction".Translate(), targetFaction.Name ?? "RimChat_HoverCardUnknownValue".Translate().ToString(), true);
            AddHoverCardLine(lines, "RimChat_HoverCardLeader".Translate(), BuildLeaderText(targetFaction), tier >= FactionIntelRevealTier.Medium);
            AddHoverCardLine(lines, "RimChat_HoverCardSettlement".Translate(), BuildSettlementText(targetFaction), tier >= FactionIntelRevealTier.High);
            AddHoverCardLine(lines, "RimChat_HoverCardRelation".Translate(), GetRelationLabelShort(targetFaction.PlayerGoodwill), true);
            AddHoverCardLine(lines, "RimChat_HoverCardGoodwill".Translate(), targetFaction.PlayerGoodwill.ToString("+#;-#;0"), tier >= FactionIntelRevealTier.Medium);
            return lines;
        }

        private static void AddHoverCardLine(List<HoverCardLine> lines, string label, string value, bool revealed)
        {
            if (lines == null || string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            string normalizedValue = string.IsNullOrWhiteSpace(value)
                ? "RimChat_HoverCardUnknownValue".Translate().ToString()
                : value.Trim();
            lines.Add(new HoverCardLine
            {
                Label = label,
                Value = revealed ? normalizedValue : "RimChat_HoverCardUnknownMask".Translate().ToString(),
                IsObscured = !revealed
            });
        }

        private static string BuildFactionBioText(Faction targetFaction)
        {
            string text = targetFaction?.def?.description;
            if (string.IsNullOrWhiteSpace(text))
            {
                return "RimChat_HoverCardNoBio".Translate();
            }

            return text.Trim().Replace("\r", string.Empty);
        }

        private static string BuildIdentityText(Faction targetFaction, Pawn subjectPawn)
        {
            string title = (targetFaction?.def?.leaderTitle ?? string.Empty).Trim();
            if (subjectPawn?.story != null && !string.IsNullOrWhiteSpace(subjectPawn.story.TitleCap))
            {
                return subjectPawn.story.TitleCap;
            }
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
            if (!string.IsNullOrWhiteSpace(subjectPawn?.kindDef?.label))
            {
                return subjectPawn.kindDef.label.CapitalizeFirst();
            }
            return "RimChat_HoverCardUnknownValue".Translate();
        }

        private static string BuildAgeText(Pawn subjectPawn)
        {
            if (subjectPawn?.ageTracker == null)
            {
                return "RimChat_HoverCardUnknownValue".Translate();
            }

            return subjectPawn.ageTracker.AgeBiologicalYears.ToString();
        }

        private static string BuildGenderText(Pawn subjectPawn)
        {
            if (subjectPawn == null)
            {
                return "RimChat_HoverCardUnknownValue".Translate();
            }

            return subjectPawn.gender.ToString();
        }

        private static string BuildRaceText(Pawn subjectPawn)
        {
            if (subjectPawn == null)
            {
                return "RimChat_HoverCardUnknownValue".Translate();
            }

            string xenotype = subjectPawn.genes?.XenotypeLabelCap ?? subjectPawn.genes?.xenotypeName;
            string race = subjectPawn.def?.label ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(xenotype) && !string.Equals(xenotype, race, StringComparison.OrdinalIgnoreCase))
            {
                return $"{race}/{xenotype}";
            }
            return string.IsNullOrWhiteSpace(race) ? "RimChat_HoverCardUnknownValue".Translate().ToString() : race.CapitalizeFirst();
        }

        private static string BuildLeaderText(Faction targetFaction)
        {
            Pawn leader = targetFaction?.leader;
            if (leader?.Name != null)
            {
                return leader.Name.ToStringFull;
            }
            if (leader != null)
            {
                return leader.LabelShortCap;
            }
            return "RimChat_HoverCardUnknownValue".Translate();
        }

        private static string BuildSettlementText(Faction targetFaction)
        {
            if (targetFaction == null || Find.WorldObjects == null)
            {
                return "RimChat_HoverCardUnknownValue".Translate();
            }

            List<Settlement> settlements = Find.WorldObjects.SettlementBases
                .Where(s => s != null && s.Faction == targetFaction)
                .ToList();
            if (settlements.Count == 0)
            {
                return "RimChat_HoverCardNoSettlement".Translate();
            }

            Settlement primary = settlements[0];
            if (string.IsNullOrWhiteSpace(primary.LabelCap))
            {
                return "RimChat_HoverCardSettlementCountOnly".Translate(settlements.Count);
            }
            return "RimChat_HoverCardSettlementSummary".Translate(primary.LabelCap, settlements.Count);
        }

        private static float CalculateHoverCardContentHeight(List<HoverCardLine> lines, Faction targetFaction)
        {
            int count = lines?.Count ?? 0;
            float baseHeight = HoverCardPadding * 2f + HoverCardPortraitSize + HoverCardSectionGap + (count - 1) * HoverCardLineHeight + 20f;
            string bioText = BuildFactionBioText(targetFaction);
            float bioWidth = Mathf.Max(120f, HoverCardWidth - HoverCardPadding * 2f - 74f);
            Text.Font = GameFont.Small;
            float bioHeight = Text.CalcHeight(bioText, bioWidth);
            Text.Font = GameFont.Small;
            return baseHeight + Mathf.Max(HoverCardLineHeight, bioHeight);
        }

        private Rect BuildHoverCardRect(Rect anchorRect, float contentHeight)
        {
            float preferredX = anchorRect.xMax + 12f;
            float preferredY = anchorRect.center.y - contentHeight * 0.3f;
            Rect windowRect = new Rect(0f, 0f, Screen.width, Screen.height);

            if (preferredX + HoverCardWidth > windowRect.xMax - 8f)
            {
                preferredX = anchorRect.x - HoverCardWidth - 12f;
            }
            if (preferredX < windowRect.x + 8f)
            {
                preferredX = windowRect.x + 8f;
            }

            preferredY = Mathf.Clamp(preferredY, windowRect.y + 44f, windowRect.yMax - contentHeight - 8f);
            return new Rect(preferredX, preferredY, HoverCardWidth, contentHeight);
        }

        private void DrawHoverCardChrome(Rect rect, float alpha, Color accent)
        {
            Color fill = new Color(0.07f, 0.08f, 0.11f, 0.985f * alpha);
            Color frame = new Color(0.42f, 0.47f, 0.56f, 0.98f * alpha);
            Widgets.DrawBoxSolid(rect, fill);
            GUI.color = frame;
            Widgets.DrawBox(rect);
            Color accentFill = new Color(accent.r, accent.g, accent.b, 0.95f * alpha);
            GUI.color = accentFill;
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 4f, rect.height), accentFill);
            GUI.color = Color.white;
        }

        private void DrawHoverCardContent(Rect rect, Faction targetFaction, Pawn subjectPawn, List<HoverCardLine> lines, FactionIntelRevealTier tier, float alpha)
        {
            Rect inner = rect.ContractedBy(HoverCardPadding);
            Rect portraitRect = new Rect(inner.x, inner.y, HoverCardPortraitSize, HoverCardPortraitSize);
            DrawHoverPortrait(portraitRect, targetFaction, subjectPawn, alpha);

            float textX = portraitRect.xMax + 10f;
            float textWidth = inner.xMax - textX;
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.97f, 0.98f, 1f, alpha);
            Widgets.Label(new Rect(textX, inner.y, textWidth, 26f), subjectPawn?.LabelShortCap ?? targetFaction?.Name ?? "Unknown");

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.8f, 0.88f, 0.98f, alpha);
            string relationText = targetFaction != null ? GetRelationLabelShort(targetFaction.PlayerGoodwill) : "Your Colonist";
            Widgets.Label(new Rect(textX, inner.y + 24f, textWidth, 18f), relationText);
            GUI.color = new Color(0.82f, 0.86f, 0.92f, alpha);
            Widgets.Label(new Rect(textX, inner.y + 42f, textWidth, 18f), "RimChat_HoverCardRevealTier".Translate(GetRevealTierLabel(tier)));

            float linesY = portraitRect.yMax + HoverCardSectionGap;
            float labelWidth = 74f;
            for (int i = 0; i < lines.Count; i++)
            {
                HoverCardLine line = lines[i];
                float valueWidth = rowValueWidth(inner.width, labelWidth);
                float rowHeight = i == 0
                    ? Mathf.Max(HoverCardLineHeight, Text.CalcHeight(line.Value, valueWidth))
                    : HoverCardLineHeight;
                Rect rowRect = new Rect(inner.x, linesY, inner.width, rowHeight);
                GUI.color = new Color(0.78f, 0.82f, 0.9f, alpha);
                Widgets.Label(new Rect(rowRect.x, rowRect.y, labelWidth, HoverCardLineHeight), line.Label);
                GUI.color = line.IsObscured
                    ? new Color(0.7f, 0.74f, 0.82f, HoverCardObscuredAlpha * alpha)
                    : new Color(0.96f, 0.97f, 1f, alpha);
                bool previousWrap = Text.WordWrap;
                Text.WordWrap = i == 0;
                Widgets.Label(new Rect(rowRect.x + labelWidth, rowRect.y, valueWidth, rowHeight), line.Value);
                Text.WordWrap = previousWrap;
                linesY += rowHeight;
            }

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private static float rowValueWidth(float innerWidth, float labelWidth)
        {
            return Mathf.Max(120f, innerWidth - labelWidth);
        }

        private void DrawHoverPortrait(Rect rect, Faction targetFaction, Pawn subjectPawn, float alpha)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.15f, 0.19f, 0.95f * alpha));
            GUI.color = new Color(0.42f, 0.48f, 0.57f, 0.92f * alpha);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            Texture texture = ResolveSpeakerPortrait(subjectPawn);
            if (texture == null)
            {
                texture = targetFaction?.def?.FactionIcon;
            }

            Rect drawRect = rect.ContractedBy(3f);
            if (texture != null && texture != BaseContent.BadTex)
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(drawRect, texture, ScaleMode.ScaleAndCrop, true);
                GUI.color = Color.white;
                return;
            }

            DrawAvatarFallback(drawRect, subjectPawn?.LabelShortCap ?? targetFaction?.Name ?? "?");
        }

        private static string GetRevealTierLabel(FactionIntelRevealTier tier)
        {
            switch (tier)
            {
                case FactionIntelRevealTier.High:
                    return "RimChat_HoverCardTierHigh".Translate();
                case FactionIntelRevealTier.Medium:
                    return "RimChat_HoverCardTierMedium".Translate();
                case FactionIntelRevealTier.Low:
                    return "RimChat_HoverCardTierLow".Translate();
                default:
                    return "RimChat_HoverCardTierHostile".Translate();
            }
        }
    }
}

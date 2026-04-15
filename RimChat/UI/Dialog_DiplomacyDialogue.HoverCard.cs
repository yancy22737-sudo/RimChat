using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using RimChat.DiplomacySystem;

namespace RimChat.UI
{
    public partial class Dialog_DiplomacyDialogue
    {
        private const float HoverCardWidth = 360f;
        private const float HoverCardPortraitSize = 96f;
        private const float HoverCardPadding = 12f;
        private const float HoverCardSectionGap = 8f;
        private const float HoverCardLineHeight = 22f;
        private const float HoverCardRevealDuration = 0.16f;
        private const float HoverCardExpandMargin = 5f;
        private const int SpeakerHoverBioMaxLength = 500;
        private const string SpeakerHoverBioOverflowSuffix = "...";
        private const int GoodwillRevealTierHostile = -60;
        private const int GoodwillRevealTierLow = 0;
        private const int GoodwillRevealTierMedium = 40;
        private const int GoodwillRevealTierHigh = 75;
        private const float HoverCardObscuredAlpha = 0.82f;
        private readonly Dictionary<string, float> hoverCardAlphaByKey = new Dictionary<string, float>(StringComparer.Ordinal);
        private Pawn activeHoverPawn;
        private Faction activeHoverFaction;
        private Vector2 activeHoverScreenPos;
        private float speakerHoverCardAlpha;
        private bool speakerHoverRequestThisFrame;
        private readonly Dictionary<int, int> factionMaxGoodwillSeen = new Dictionary<int, int>();

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
            bool isHovered = Mouse.IsOver(expandedRect);
            float alpha = UpdateHoverCardAlpha($"faction:{targetFaction.loadID}", isHovered);
            if (alpha <= 0.01f)
            {
                return;
            }

            DrawHoverCardForFaction(targetFaction, null, anchorRect, alpha);
        }

        private float UpdateHoverCardAlpha(string key, bool hovered)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return 0f;
            }

            float current = hoverCardAlphaByKey.TryGetValue(key, out float value) ? value : 0f;
            float speed = Time.unscaledDeltaTime / (hovered ? HoverCardRevealDuration : HoverCardRevealDuration * 2f);
            float target = hovered ? 1f : 0f;
            float next = Mathf.MoveTowards(current, target, speed);
            hoverCardAlphaByKey[key] = next;
            return next;
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

            FactionIntelRevealTier tier = ResolveFactionRevealTier(fallbackFaction);
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
            AddHoverCardLine(lines, "RimChat_HoverCardFaction".Translate(), Faction.OfPlayer.Name ?? "RimChat_HoverCardPlayerFaction".Translate(), true);

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

        private FactionIntelRevealTier ResolveFactionRevealTier(Faction f)
        {
            if (f == null)
            {
                return FactionIntelRevealTier.Hostile;
            }

            int current = f.PlayerGoodwill;
            int loadId = f.loadID;
            if (!factionMaxGoodwillSeen.TryGetValue(loadId, out int maxSeen) || current > maxSeen)
            {
                factionMaxGoodwillSeen[loadId] = current;
                maxSeen = current;
            }

            int effective = Mathf.Max(current, maxSeen);
            if (effective >= GoodwillRevealTierHigh)
            {
                return FactionIntelRevealTier.High;
            }
            if (effective >= GoodwillRevealTierMedium)
            {
                return FactionIntelRevealTier.Medium;
            }
            if (effective >= GoodwillRevealTierLow)
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

            // Always add special items lines, but reveal only at High tier
            bool specialRevealed = tier >= FactionIntelRevealTier.High;
            if (specialRevealed)
            {
                FactionSpecialItemsManager.Instance.MarkRevealed(targetFaction);
            }
            var specialDisplay = FactionSpecialItemsManager.Instance.GetHoverCardDisplay(targetFaction);
            AddHoverCardLine(lines, "RimChat_HoverCardDiscountItem".Translate(), specialDisplay.discountText, specialRevealed);
            AddHoverCardLine(lines, "RimChat_HoverCardScarceItem".Translate(), specialDisplay.scarceText, specialRevealed);

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

        private static string BuildSpeakerHoverBioText(Faction resolvedFaction, bool isPlayer, Pawn activePawn)
        {
            string rawText = isPlayer
                ? (activePawn?.story?.TitleCap ?? "Colonist")
                : BuildFactionBioText(resolvedFaction);

            if (string.IsNullOrEmpty(rawText))
            {
                return string.Empty;
            }

            string normalized = rawText.Replace("\r", string.Empty);
            if (normalized.Length <= SpeakerHoverBioMaxLength)
            {
                return normalized;
            }

            return normalized.Substring(0, SpeakerHoverBioMaxLength) + SpeakerHoverBioOverflowSuffix;
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

            return subjectPawn.gender.GetLabel();
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

        public void RequestSpeakerHoverCard(Pawn pawn, Faction faction, Vector2 screenPos)
        {
            activeHoverPawn = pawn;
            activeHoverFaction = faction;
            activeHoverScreenPos = screenPos;
            speakerHoverRequestThisFrame = true;
        }

        public void DrawSpeakerHoverCard()
        {
            if (activeHoverFaction == null)
            {
                if (speakerHoverCardAlpha > 0.001f)
                {
                    speakerHoverCardAlpha = Mathf.MoveTowards(speakerHoverCardAlpha, 0f, Time.unscaledDeltaTime / 0.1f);
                }
                activeHoverPawn = null;
                return;
            }

            Vector2 mousePos = Event.current?.mousePosition ?? Vector2.zero;
            float cardWidth = 360f;
            float portraitSize = 96f;
            float padding = 12f;
            float lineHeight = 20f;
            float sectionGap = 6f;

            Faction resolvedFaction = activeHoverFaction;
            bool isPlayer = resolvedFaction != null && (resolvedFaction.IsPlayer || resolvedFaction == Faction.OfPlayer);
            FactionIntelRevealTier tier = isPlayer ? FactionIntelRevealTier.High : ResolveFactionRevealTier(resolvedFaction);

            float labelWidth = 74f;
            float valueWidth = cardWidth - padding * 2 - labelWidth;
            string bioText = BuildSpeakerHoverBioText(resolvedFaction, isPlayer, activeHoverPawn);
            bool bioRevealed = isPlayer || tier >= FactionIntelRevealTier.Low;
            string bioDisplay = bioRevealed ? (bioText ?? "???") : "???";

            Text.Font = GameFont.Tiny;
            float bioLineHeight = Mathf.Max(lineHeight, Text.CalcHeight(bioDisplay, valueWidth));
            Text.Font = GameFont.Small;

            int lineCount = 0;
            float totalLineHeight = 0f;
            if (isPlayer)
            {
                lineCount = 4;
                totalLineHeight = bioLineHeight + (lineCount - 1) * lineHeight;
            }
            else
            {
                lineCount = 11; // Always include discount/scarce lines now
                if (string.IsNullOrEmpty(resolvedFaction?.def?.description))
                {
                    lineCount--;
                }
                totalLineHeight = bioLineHeight + (lineCount - 1) * lineHeight;
            }

            float cardHeight = portraitSize + padding * 2 + sectionGap + totalLineHeight + 8f;

            Rect bounds = lastWindowContentRect;
            Rect cardRect = new Rect(
                activeHoverScreenPos.x + 40f,
                activeHoverScreenPos.y - cardHeight / 2f,
                cardWidth,
                cardHeight);

            if (cardRect.xMax > bounds.xMax - 8f)
            {
                cardRect.x = activeHoverScreenPos.x - cardWidth - 10f;
            }
            if (cardRect.x < bounds.x + 8f)
            {
                cardRect.x = bounds.x + 8f;
            }
            if (cardRect.y < bounds.y + 8f)
            {
                cardRect.y = bounds.y + 8f;
            }
            if (cardRect.yMax > bounds.yMax - 8f)
            {
                cardRect.y = bounds.yMax - cardRect.height - 8f;
            }

            float targetAlpha;
            if (speakerHoverRequestThisFrame)
            {
                targetAlpha = 1f;
            }
            else
            {
                targetAlpha = cardRect.Contains(mousePos) ? 1f : 0f;
            }

            speakerHoverCardAlpha = Mathf.MoveTowards(speakerHoverCardAlpha, targetAlpha, Time.unscaledDeltaTime / 0.15f);

            if (speakerHoverCardAlpha < 0.01f)
            {
                activeHoverPawn = null;
                activeHoverFaction = null;
                speakerHoverRequestThisFrame = false;
                return;
            }

            Color accentColor = isPlayer ? Color.white : GetGoodwillColor(resolvedFaction.PlayerGoodwill);
            Color bgColor = new Color(0.07f, 0.08f, 0.11f, 0.985f * speakerHoverCardAlpha);
            Color frameColor = new Color(0.42f, 0.47f, 0.56f, 0.98f * speakerHoverCardAlpha);

            Widgets.DrawBoxSolid(cardRect, bgColor);
            GUI.color = frameColor;
            Widgets.DrawBox(cardRect);
            Color accentFill = new Color(accentColor.r, accentColor.g, accentColor.b, 0.95f * speakerHoverCardAlpha);
            GUI.color = accentFill;
            Widgets.DrawBoxSolid(new Rect(cardRect.x, cardRect.y, 4f, cardRect.height), accentFill);
            GUI.color = Color.white;

            Rect portraitRect = new Rect(cardRect.x + padding, cardRect.y + padding, portraitSize, portraitSize);
            DrawHoverCardPortrait(portraitRect, activeHoverPawn, resolvedFaction, speakerHoverCardAlpha);

            float textX = portraitRect.xMax + padding;
            float textWidth = cardRect.width - portraitSize - padding * 3;
            float curY = cardRect.y + padding;

            Text.Font = GameFont.Small;
            GUI.color = new Color(0.97f, 0.98f, 1f, speakerHoverCardAlpha);
            string name = activeHoverPawn?.LabelShortCap ?? resolvedFaction?.Name ?? "Unknown";
            Widgets.Label(new Rect(textX, curY, textWidth, 24f), name);
            curY += 24f;

            Text.Font = GameFont.Tiny;
            if (isPlayer)
            {
                GUI.color = new Color(0.8f, 0.88f, 0.98f, speakerHoverCardAlpha);
                Widgets.Label(new Rect(textX, curY, textWidth, lineHeight), "RimChat_HoverCardPlayerFaction".Translate());
                curY += lineHeight;
            }
            else
            {
                GUI.color = new Color(accentColor.r, accentColor.g, accentColor.b, speakerHoverCardAlpha);
                Widgets.Label(new Rect(textX, curY, textWidth, lineHeight), GetRelationLabelShort(resolvedFaction.PlayerGoodwill));
                curY += lineHeight;

                GUI.color = new Color(0.82f, 0.86f, 0.92f, speakerHoverCardAlpha);
                Widgets.Label(new Rect(textX, curY, textWidth, lineHeight), "RimChat_HoverCardRevealTier".Translate(GetRevealTierLabel(tier)));
            }

            float linesY = portraitRect.yMax + sectionGap;

            if (isPlayer)
            {
                DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardBio".Translate(), bioText, true, speakerHoverCardAlpha, bioLineHeight);
                DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardFaction".Translate(), Faction.OfPlayer.Name ?? "RimChat_HoverCardPlayerFaction".Translate(), true, speakerHoverCardAlpha);
                if (activeHoverPawn != null)
                {
                    DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardAge".Translate(), activeHoverPawn.ageTracker?.AgeBiologicalYears.ToString() ?? "?", true, speakerHoverCardAlpha);
                    DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardGender".Translate(), activeHoverPawn.gender.GetLabel(), true, speakerHoverCardAlpha);
                }
            }
            else
            {
                DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardBio".Translate(), bioText, tier >= FactionIntelRevealTier.Low, speakerHoverCardAlpha, bioLineHeight);
                DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardIdentity".Translate(), BuildIdentityText(resolvedFaction, activeHoverPawn), tier >= FactionIntelRevealTier.Low, speakerHoverCardAlpha);
                DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardAge".Translate(), BuildAgeText(activeHoverPawn), tier >= FactionIntelRevealTier.Medium, speakerHoverCardAlpha);
                DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardGender".Translate(), BuildGenderText(activeHoverPawn), tier >= FactionIntelRevealTier.Low, speakerHoverCardAlpha);
                DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardRace".Translate(), BuildRaceText(activeHoverPawn), tier >= FactionIntelRevealTier.Medium, speakerHoverCardAlpha);
                DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardFaction".Translate(), resolvedFaction.Name ?? "???", true, speakerHoverCardAlpha);
                DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardSettlement".Translate(), BuildSettlementText(resolvedFaction), tier >= FactionIntelRevealTier.High, speakerHoverCardAlpha);
                DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardRelation".Translate(), GetRelationLabelShort(resolvedFaction.PlayerGoodwill), true, speakerHoverCardAlpha);
                DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardGoodwill".Translate(), resolvedFaction.PlayerGoodwill.ToString("+#;-#;0"), tier >= FactionIntelRevealTier.Medium, speakerHoverCardAlpha);

                // Always add special items lines, but reveal only at High tier
                bool specialRevealed = tier >= FactionIntelRevealTier.High;
                if (specialRevealed)
                {
                    FactionSpecialItemsManager.Instance.MarkRevealed(resolvedFaction);
                }
                var specialDisplay = FactionSpecialItemsManager.Instance.GetHoverCardDisplay(resolvedFaction);
                DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardDiscountItem".Translate(), specialDisplay.discountText, specialRevealed, speakerHoverCardAlpha);
                DrawIntelLine(cardRect, ref linesY, padding, labelWidth, "RimChat_HoverCardScarceItem".Translate(), specialDisplay.scarceText, specialRevealed, speakerHoverCardAlpha);
            }

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            speakerHoverRequestThisFrame = false;
        }

        private void DrawHoverCardPortrait(Rect rect, Pawn pawn, Faction targetFaction, float alpha)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.15f, 0.19f, 0.95f * alpha));
            GUI.color = new Color(0.42f, 0.48f, 0.57f, 0.92f * alpha);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            Texture texture = ResolveSpeakerPortrait(pawn);
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

            DrawAvatarFallback(drawRect, pawn?.LabelShortCap ?? targetFaction?.Name ?? "?");
        }

        private static void DrawIntelLine(Rect cardRect, ref float y, float padding, float labelWidth, string label, string value, bool revealed, float alpha, float rowHeight = 20f)
        {
            string displayValue = revealed ? (value ?? "???") : "???";
            float valueWidth = cardRect.width - padding * 2 - labelWidth;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.78f, 0.82f, 0.9f, alpha);
            Widgets.Label(new Rect(cardRect.x + padding, y, labelWidth, rowHeight), label);
            GUI.color = revealed
                ? new Color(0.96f, 0.97f, 1f, alpha)
                : new Color(0.7f, 0.74f, 0.82f, HoverCardObscuredAlpha * alpha);
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = rowHeight > 22f;
            Widgets.Label(new Rect(cardRect.x + padding + labelWidth, y, valueWidth, rowHeight), displayValue);
            Text.WordWrap = prevWrap;
            y += rowHeight;
        }
    }
}

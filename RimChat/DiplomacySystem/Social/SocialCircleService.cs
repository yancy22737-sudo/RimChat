using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.Core;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: RimWorld factions/goodwill/incident/world-settlement APIs, RimChat settings.
 /// Responsibility: social post generation, impact execution, and intent scoring.
 ///</summary>
    public static class SocialCircleService
    {
        private const int MaxFactionSettlementsFromSocial = 12;
        private const int MinSettlementDistanceFromPlayer = 12;

        private static readonly string[] NegativeKeywords =
        {
            "insult", "humiliate", "threat", "betray", "offend", "raid", "attack",
            "humiliation", "aggression"
        };

        private static readonly string[] PositiveKeywords =
        {
            "help", "aid", "gift", "cooperate", "peace", "alliance",
            "support", "truce"
        };

        private static readonly string[] MilitaryKeywords =
        {
            "raid", "attack", "war", "siege", "strike",
            "offensive", "battle"
        };

        private static readonly SocialPostImpactType[] CoreMediumLowIncidentPool =
        {
            SocialPostImpactType.IncidentColdSnap,
            SocialPostImpactType.IncidentCropBlight,
            SocialPostImpactType.IncidentHeatWave,
            SocialPostImpactType.IncidentSolarFlare,
            SocialPostImpactType.IncidentFlashstorm
        };

        public static int CalculateNextIntervalTicks(RimChatSettings settings)
        {
            int minDays = Math.Max(1, settings?.SocialPostIntervalMinDays ?? 5);
            int maxDays = Math.Max(minDays, settings?.SocialPostIntervalMaxDays ?? 7);
            int days = Rand.RangeInclusive(minDays, maxDays);
            return days * 60000;
        }

        public static PublicSocialPost CreateScheduledPost(Faction sourceFaction, Faction targetFaction)
        {
            SocialPostCategory category = (SocialPostCategory)Rand.RangeInclusive(0, 3);
            int sentiment = CalculateScheduledSentiment(sourceFaction);
            return CreatePost(
                sourceFaction,
                targetFaction,
                category,
                sentiment,
                string.Empty,
                false,
                DebugGenerateReason.Scheduled,
                string.Empty);
        }

        public static PublicSocialPost CreatePost(
            Faction sourceFaction,
            Faction targetFaction,
            SocialPostCategory category,
            int sentiment,
            string summary,
            bool fromPlayerDialogue,
            DebugGenerateReason reason,
            string intentHint)
        {
            sentiment = Mathf.Clamp(sentiment, -2, 2);
            float credibility = fromPlayerDialogue ? Rand.Range(0.55f, 0.9f) : Rand.Range(0.45f, 1.0f);
            SocialPostImpactType impactType = DetermineImpactType(category, sentiment);

            var post = new PublicSocialPost
            {
                PostId = Guid.NewGuid().ToString("N"),
                CreatedTick = Find.TickManager?.TicksGame ?? 0,
                SourceFaction = sourceFaction,
                TargetFaction = targetFaction,
                Category = category,
                Sentiment = sentiment,
                Credibility = credibility,
                IsFromPlayerDialogue = fromPlayerDialogue,
                IntentHint = intentHint ?? string.Empty,
                SourceLeaderName = GetLeaderName(sourceFaction),
                TargetLeaderName = GetLeaderName(targetFaction),
                ImpactType = impactType,
                ImpactMagnitude = 1,
                BaseLikeCount = CalculateInitialLikeCount(sourceFaction)
            };

            post.CurrentLikeCount = post.BaseLikeCount;
            post.Content = BuildPostContent(post, summary);
            post.EffectSummary = BuildPlannedEffectSummary(post);
            Log.Message($"[RimChat] Social post created ({reason}) impact={post.ImpactType}: {post.Content}");
            return post;
        }

        public static int CalculateSoftImpactDelta(PublicSocialPost post)
        {
            if (post == null) return 0;
            float value = post.Sentiment * post.Credibility * 2f;
            return Mathf.Clamp(Mathf.RoundToInt(value), -4, 4);
        }

        public static string ApplyPostImpact(PublicSocialPost post)
        {
            int goodwillDelta = ApplySoftImpact(post);
            bool extraApplied = TryApplyExtendedImpact(post);
            return BuildAppliedEffectSummary(post, goodwillDelta, extraApplied);
        }

        public static int ApplySoftImpact(PublicSocialPost post)
        {
            if (post == null) return 0;
            int delta = CalculateSoftImpactDelta(post);
            if (delta == 0) return 0;

            TryAffectPlayerGoodwill(post.SourceFaction, delta);
            if (post.TargetFaction != null && post.TargetFaction != post.SourceFaction)
            {
                TryAffectPlayerGoodwill(post.TargetFaction, delta);
            }
            return delta;
        }

        public static int GenerateLikeIncrement(PublicSocialPost post)
        {
            int baseLikes = Math.Max(1, post?.BaseLikeCount ?? 1);
            int step = Math.Max(1, Mathf.RoundToInt(Mathf.Sqrt(baseLikes) * 0.35f));
            return Rand.RangeInclusive(step, step + 2);
        }

        public static int RollLikeGoodwillBonus(PublicSocialPost post)
        {
            if (post?.SourceFaction == null || post.SourceFaction.defeated) return 0;
            float chance = 0.08f + Mathf.Clamp01(post.Credibility - 0.5f) * 0.2f;
            if (!Rand.Chance(chance)) return 0;
            return Rand.Chance(0.75f) ? 1 : 2;
        }

        public static void UpdateActionIntents(SocialCircleState state, PublicSocialPost post, int currentTick)
        {
            if (state == null || post == null) return;

            if (post.Sentiment <= -1)
            {
                Faction raidFaction = post.TargetFaction ?? post.SourceFaction;
                float gain = Mathf.Clamp(Math.Abs(post.Sentiment) * post.Credibility * 0.35f, 0.1f, 0.8f);
                AddIntentScore(state, raidFaction, SocialIntentType.Raid, gain, currentTick);
                return;
            }

            if (post.Sentiment < 1 || post.SourceFaction == null) return;

            if (post.SourceFaction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally)
            {
                AddIntentScore(state, post.SourceFaction, SocialIntentType.Aid, 0.25f * post.Credibility, currentTick);
                return;
            }

            if (post.SourceFaction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
            {
                AddIntentScore(state, post.SourceFaction, SocialIntentType.Caravan, 0.2f * post.Credibility, currentTick);
            }
        }

        public static void DecayIntents(SocialCircleState state)
        {
            if (state == null || state.ActionIntents == null) return;
            for (int i = state.ActionIntents.Count - 1; i >= 0; i--)
            {
                SocialActionIntent intent = state.ActionIntents[i];
                if (intent == null || intent.Faction == null || intent.Faction.defeated)
                {
                    state.ActionIntents.RemoveAt(i);
                    continue;
                }

                intent.Score = Mathf.Max(0f, intent.Score - 0.1f);
                if (intent.Score <= 0.001f)
                {
                    state.ActionIntents.RemoveAt(i);
                }
            }
        }

        public static bool TryAnalyzeDialogueKeywords(
            string playerText,
            string aiText,
            out SocialPostCategory category,
            out int sentiment,
            out string intentHint)
        {
            string merged = $"{playerText} {aiText}".ToLowerInvariant();
            category = ContainsAny(merged, MilitaryKeywords) ? SocialPostCategory.Military : SocialPostCategory.Diplomatic;
            sentiment = 0;
            intentHint = string.Empty;

            int negative = CountHits(merged, NegativeKeywords);
            int positive = CountHits(merged, PositiveKeywords);
            if (negative == 0 && positive == 0) return false;

            sentiment = Mathf.Clamp(positive - negative, -2, 2);
            if (sentiment == 0)
            {
                sentiment = negative >= positive ? -1 : 1;
            }

            if (sentiment <= -1 && ContainsAny(merged, MilitaryKeywords))
            {
                intentHint = SocialIntentType.Raid.ToString();
            }
            return true;
        }

        public static string GetCategoryLabel(SocialPostCategory category)
        {
            return GetCategoryLabelKey(category).Translate();
        }

        public static string GetCategoryLabelKey(SocialPostCategory category)
        {
            switch (category)
            {
                case SocialPostCategory.Military:
                    return "RimChat_NewsCategoryMilitary";
                case SocialPostCategory.Economic:
                    return "RimChat_NewsCategoryEconomic";
                case SocialPostCategory.Anomaly:
                    return "RimChat_NewsCategoryAnomaly";
                default:
                    return "RimChat_NewsCategoryDiplomatic";
            }
        }

        private static string BuildPostContent(PublicSocialPost post, string summary)
        {
            string sourceName = post.SourceFaction?.Name ?? "RimChat_Unknown".Translate().ToString();
            string targetName = post.TargetFaction?.Name ?? "RimChat_SocialNoTarget".Translate().ToString();
            string sourceLeader = string.IsNullOrEmpty(post.SourceLeaderName)
                ? "RimChat_SocialNoLeader".Translate().ToString()
                : post.SourceLeaderName;
            string targetLeader = string.IsNullOrEmpty(post.TargetLeaderName)
                ? "RimChat_SocialNoLeader".Translate().ToString()
                : post.TargetLeaderName;
            string category = GetCategoryLabel(post.Category);
            string impactNarrative = GetImpactNarrative(post.ImpactType);

            if (!string.IsNullOrWhiteSpace(summary))
            {
                return "RimChat_SocialPostTemplateSummaryLeader".Translate(
                    summary.Trim(),
                    sourceName,
                    sourceLeader,
                    targetName,
                    targetLeader,
                    impactNarrative);
            }

            if (post.Sentiment > 0)
            {
                return "RimChat_SocialPostTemplatePositiveLeader".Translate(
                    sourceName,
                    sourceLeader,
                    targetName,
                    targetLeader,
                    impactNarrative,
                    category);
            }

            if (post.Sentiment < 0)
            {
                return "RimChat_SocialPostTemplateNegativeLeader".Translate(
                    sourceName,
                    sourceLeader,
                    targetName,
                    targetLeader,
                    impactNarrative,
                    category);
            }

            return "RimChat_SocialPostTemplateNeutralLeader".Translate(
                sourceName,
                sourceLeader,
                targetName,
                targetLeader,
                impactNarrative,
                category);
        }

        private static string BuildPlannedEffectSummary(PublicSocialPost post)
        {
            int goodwillDelta = CalculateSoftImpactDelta(post);
            string goodwillText = goodwillDelta >= 0 ? $"+{goodwillDelta}" : goodwillDelta.ToString();
            string impactText = GetImpactResultText(post?.ImpactType ?? SocialPostImpactType.Goodwill, true);
            return "RimChat_SocialEffectSummaryComposite".Translate(goodwillText, impactText);
        }

        private static string BuildAppliedEffectSummary(PublicSocialPost post, int goodwillDelta, bool extraApplied)
        {
            string goodwillText = goodwillDelta >= 0 ? $"+{goodwillDelta}" : goodwillDelta.ToString();
            string impactText = GetImpactResultText(post?.ImpactType ?? SocialPostImpactType.Goodwill, extraApplied);
            return "RimChat_SocialEffectSummaryComposite".Translate(goodwillText, impactText);
        }

        private static string GetImpactNarrative(SocialPostImpactType impactType)
        {
            switch (impactType)
            {
                case SocialPostImpactType.SettlementGain:
                    return "RimChat_SocialImpactNarrativeSettlementGain".Translate();
                case SocialPostImpactType.SettlementLoss:
                    return "RimChat_SocialImpactNarrativeSettlementLoss".Translate();
                case SocialPostImpactType.IncidentColdSnap:
                    return "RimChat_SocialImpactNarrativeColdSnap".Translate();
                case SocialPostImpactType.IncidentBlight:
                case SocialPostImpactType.IncidentCropBlight:
                    return "RimChat_SocialImpactNarrativeCropBlight".Translate();
                case SocialPostImpactType.IncidentHeatWave:
                    return "RimChat_SocialImpactNarrativeHeatWave".Translate();
                case SocialPostImpactType.IncidentSolarFlare:
                    return "RimChat_SocialImpactNarrativeSolarFlare".Translate();
                case SocialPostImpactType.IncidentFlashstorm:
                    return "RimChat_SocialImpactNarrativeFlashstorm".Translate();
                default:
                    return "RimChat_SocialImpactNarrativeGoodwill".Translate();
            }
        }

        private static string GetImpactResultText(SocialPostImpactType impactType, bool success)
        {
            if (!success && impactType != SocialPostImpactType.Goodwill)
            {
                return "RimChat_SocialImpactResultSkipped".Translate();
            }

            switch (impactType)
            {
                case SocialPostImpactType.SettlementGain:
                    return "RimChat_SocialImpactResultSettlementGain".Translate();
                case SocialPostImpactType.SettlementLoss:
                    return "RimChat_SocialImpactResultSettlementLoss".Translate();
                case SocialPostImpactType.IncidentColdSnap:
                    return "RimChat_SocialImpactResultColdSnap".Translate();
                case SocialPostImpactType.IncidentBlight:
                case SocialPostImpactType.IncidentCropBlight:
                    return "RimChat_SocialImpactResultCropBlight".Translate();
                case SocialPostImpactType.IncidentHeatWave:
                    return "RimChat_SocialImpactResultHeatWave".Translate();
                case SocialPostImpactType.IncidentSolarFlare:
                    return "RimChat_SocialImpactResultSolarFlare".Translate();
                case SocialPostImpactType.IncidentFlashstorm:
                    return "RimChat_SocialImpactResultFlashstorm".Translate();
                default:
                    return "RimChat_SocialImpactResultGoodwill".Translate();
            }
        }

        private static SocialPostImpactType DetermineImpactType(SocialPostCategory category, int sentiment)
        {
            if (category == SocialPostCategory.Anomaly)
            {
                return RollCoreMediumLowIncidentImpact();
            }

            if (category == SocialPostCategory.Economic && sentiment >= 1 && Rand.Chance(0.45f))
            {
                return SocialPostImpactType.SettlementGain;
            }

            if (category == SocialPostCategory.Economic && sentiment <= -1 && Rand.Chance(0.35f))
            {
                return SocialPostImpactType.IncidentCropBlight;
            }

            if (category == SocialPostCategory.Military && sentiment <= -1 && Rand.Chance(0.42f))
            {
                return SocialPostImpactType.SettlementLoss;
            }

            if (category == SocialPostCategory.Diplomatic)
            {
                if (sentiment >= 1 && Rand.Chance(0.2f)) return SocialPostImpactType.SettlementGain;
                if (sentiment <= -1 && Rand.Chance(0.2f)) return RollCoreMediumLowIncidentImpact();
            }

            if (Rand.Chance(0.14f))
            {
                return RollCoreMediumLowIncidentImpact();
            }

            return SocialPostImpactType.Goodwill;
        }

        private static SocialPostImpactType RollCoreMediumLowIncidentImpact()
        {
            return CoreMediumLowIncidentPool.RandomElement();
        }

        private static int CalculateScheduledSentiment(Faction sourceFaction)
        {
            if (sourceFaction == null) return Rand.RangeInclusive(-1, 1);
            int baseValue = Mathf.RoundToInt(sourceFaction.PlayerGoodwill / 40f);
            int jitter = Rand.RangeInclusive(-1, 1);
            return Mathf.Clamp(baseValue + jitter, -2, 2);
        }

        private static void TryAffectPlayerGoodwill(Faction faction, int delta)
        {
            if (faction == null || faction.defeated || delta == 0) return;
            faction.TryAffectGoodwillWith(Faction.OfPlayer, delta, false, true, null);
        }

        private static bool TryApplyExtendedImpact(PublicSocialPost post)
        {
            if (post == null) return false;

            switch (post.ImpactType)
            {
                case SocialPostImpactType.SettlementGain:
                    return TryAddFactionSettlement(post.SourceFaction);
                case SocialPostImpactType.SettlementLoss:
                    return TryRemoveFactionSettlement(post.TargetFaction ?? post.SourceFaction);
                case SocialPostImpactType.IncidentColdSnap:
                    return TryTriggerIncident("ColdSnap", post.SourceFaction);
                case SocialPostImpactType.IncidentBlight:
                case SocialPostImpactType.IncidentCropBlight:
                    return TryTriggerIncident("CropBlight", post.SourceFaction);
                case SocialPostImpactType.IncidentHeatWave:
                    return TryTriggerIncident("HeatWave", post.SourceFaction);
                case SocialPostImpactType.IncidentSolarFlare:
                    return TryTriggerIncident("SolarFlare", post.SourceFaction);
                case SocialPostImpactType.IncidentFlashstorm:
                    return TryTriggerIncident("Flashstorm", post.SourceFaction);
                default:
                    return true;
            }
        }

        private static bool TryAddFactionSettlement(Faction faction)
        {
            if (faction == null || faction.defeated || Find.WorldObjects == null) return false;
            if (CountFactionSettlements(faction) >= MaxFactionSettlementsFromSocial) return false;

            PlanetTile tile = TileFinder.RandomSettlementTileFor(faction, true, IsTileAllowedForSocialSettlement);
            if (!tile.Valid) return false;

            Settlement settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            settlement.SetFaction(faction);
            settlement.Tile = tile;
            settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement, faction.def?.settlementNameMaker);
            Find.WorldObjects.Add(settlement);
            return true;
        }

        private static bool TryRemoveFactionSettlement(Faction faction)
        {
            if (faction == null || faction.defeated || Find.WorldObjects == null) return false;

            List<Settlement> settlements = Find.WorldObjects.Settlements
                .Where(s => s != null && s.Faction == faction)
                .ToList();
            if (settlements.Count <= 1) return false;

            Settlement target = SelectSettlementForRemoval(settlements);
            if (target == null) return false;

            Find.WorldObjects.Remove(target);
            return true;
        }

        private static Settlement SelectSettlementForRemoval(List<Settlement> settlements)
        {
            if (settlements == null || settlements.Count == 0) return null;
            Map playerMap = Find.AnyPlayerHomeMap;
            if (playerMap == null) return settlements.RandomElement();

            int playerTile = playerMap.Tile;
            return settlements
                .OrderByDescending(s => DistanceToPlayerTile(s.Tile, playerTile))
                .FirstOrDefault();
        }

        private static int DistanceToPlayerTile(PlanetTile tile, int playerTile)
        {
            if (playerTile < 0 || Find.WorldGrid == null) return 0;
            int distance = Find.WorldGrid.TraversalDistanceBetween(tile, playerTile);
            return distance < 0 ? 0 : distance;
        }

        private static bool IsTileAllowedForSocialSettlement(PlanetTile tile)
        {
            if (!tile.Valid || Find.WorldObjects == null) return false;
            if (Find.WorldObjects.Settlements.Any(s => s != null && s.Tile == tile)) return false;

            if (!TileFinder.IsValidTileForNewSettlement(tile, new StringBuilder(), false))
            {
                return false;
            }

            Map playerMap = Find.AnyPlayerHomeMap;
            if (playerMap == null || Find.WorldGrid == null) return true;

            int distance = Find.WorldGrid.TraversalDistanceBetween(tile, playerMap.Tile);
            if (distance < 0) return false;
            return distance >= MinSettlementDistanceFromPlayer;
        }

        private static bool TryTriggerIncident(string defName, Faction sourceFaction)
        {
            Map map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            if (map == null) return false;

            IncidentDef incidentDef = DefDatabase<IncidentDef>.GetNamedSilentFail(defName);
            if (incidentDef == null) return false;

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(incidentDef.category, map);
            parms.forced = true;
            parms.faction = sourceFaction;
            return incidentDef.Worker.TryExecute(parms);
        }

        private static int CalculateInitialLikeCount(Faction sourceFaction)
        {
            int settlements = CountFactionSettlements(sourceFaction);
            int min = Math.Max(3, settlements * 3 + 1);
            int max = Math.Max(min + 3, settlements * 8 + 9);
            return Rand.RangeInclusive(min, max);
        }

        private static int CountFactionSettlements(Faction faction)
        {
            if (faction == null || Find.WorldObjects == null) return 0;
            return Find.WorldObjects.Settlements.Count(s => s != null && s.Faction == faction);
        }

        private static string GetLeaderName(Faction faction)
        {
            return faction?.leader?.Name?.ToStringFull ?? string.Empty;
        }

        private static void AddIntentScore(
            SocialCircleState state,
            Faction faction,
            SocialIntentType intentType,
            float amount,
            int currentTick)
        {
            if (state == null || faction == null || amount <= 0f) return;

            SocialActionIntent intent = state.ActionIntents.Find(i => i.Faction == faction && i.IntentType == intentType);
            if (intent == null)
            {
                intent = new SocialActionIntent { Faction = faction, IntentType = intentType };
                state.ActionIntents.Add(intent);
            }

            intent.Score = Mathf.Min(2f, intent.Score + amount);
            intent.LastUpdatedTick = currentTick;
        }

        private static bool ContainsAny(string source, IEnumerable<string> keywords)
        {
            foreach (string keyword in keywords)
            {
                if (!string.IsNullOrEmpty(keyword) && source.Contains(keyword))
                {
                    return true;
                }
            }
            return false;
        }

        private static int CountHits(string source, IEnumerable<string> keywords)
        {
            int count = 0;
            foreach (string keyword in keywords)
            {
                if (!string.IsNullOrEmpty(keyword) && source.Contains(keyword))
                {
                    count++;
                }
            }
            return count;
        }
    }
}



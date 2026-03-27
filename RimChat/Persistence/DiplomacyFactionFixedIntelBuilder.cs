using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.Config;
using RimChat.DiplomacySystem;
using RimChat.WorldState;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimChat.Persistence
{
    /// <summary>/// Dependencies: diplomacy manager, AI interface, world intel ledgers, and faction runtime state.
 /// Responsibility: build fixed faction-intel prompt block appended after faction prompt text.
 ///</summary>
    internal static class DiplomacyFactionFixedIntelBuilder
    {
        public static string Build(Faction faction, string promptChannel)
        {
            if (!ShouldInject(promptChannel) || faction == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== FIXED_FACTION_INTEL ===");
            AppendFactionBasics(sb, faction);
            AppendCurrentStateFlags(sb, faction);
            AppendSettlementDestructionHistory(sb, faction);
            AppendRaidDamageIntel(sb, faction);
            AppendPlayerTechLevel(sb);
            return sb.ToString().TrimEnd();
        }

        private static bool ShouldInject(string promptChannel)
        {
            return string.Equals(promptChannel, RimTalkPromptEntryChannelCatalog.DiplomacyDialogue, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(promptChannel, RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(promptChannel, RimTalkPromptEntryChannelCatalog.DiplomacyStrategy, StringComparison.OrdinalIgnoreCase);
        }

        private static void AppendFactionBasics(StringBuilder sb, Faction faction)
        {
            sb.AppendLine("FactionDescription: " + NormalizeInlineText(faction.def?.description, "Unknown"));
            sb.AppendLine("FactionTechLevel: " + (faction.def?.techLevel.ToString() ?? "Undefined"));
        }

        private static void AppendCurrentStateFlags(StringBuilder sb, Faction faction)
        {
            GameComponent_DiplomacyManager diplomacyManager = GameComponent_DiplomacyManager.Instance;
            bool hasCaravan = diplomacyManager?.HasCaravanDispatchedNow(faction) == true;
            bool hasRaid = diplomacyManager?.HasRaidScheduledNow(faction) == true;
            bool hasQuest = GameAIInterface.Instance?.HasActiveRimChatQuestForFaction(faction) == true;
            bool hasPlayerExpedition = HasPlayerExpeditionNow();

            sb.AppendLine("HasFactionCaravanDispatchedNow: " + ToBoolText(hasCaravan));
            sb.AppendLine("HasFactionQuestPublishedNow: " + ToBoolText(hasQuest));
            sb.AppendLine("HasFactionRaidScheduledNow: " + ToBoolText(hasRaid));
            sb.AppendLine("HasPlayerExpeditionNow: " + ToBoolText(hasPlayerExpedition));
        }

        private static bool HasPlayerExpeditionNow()
        {
            IEnumerable<Caravan> caravans = Find.WorldObjects?.Caravans;
            return caravans != null && caravans.Any(caravan => caravan != null && caravan.Faction == Faction.OfPlayer);
        }

        private static void AppendSettlementDestructionHistory(StringBuilder sb, Faction faction)
        {
            FactionIntelLedgerComponent ledger = FactionIntelLedgerComponent.Instance;
            List<FactionSettlementDestructionRecord> records = ledger?.GetSettlementDestructionRecords(faction)
                ?? new List<FactionSettlementDestructionRecord>();
            if (records.Count == 0)
            {
                sb.AppendLine("FactionSettlementDestroyedHistory: none");
                return;
            }

            sb.AppendLine("FactionSettlementDestroyedHistory:");
            for (int i = 0; i < records.Count; i++)
            {
                FactionSettlementDestructionRecord record = records[i];
                sb.AppendLine(
                    $"- when={FormatTick(record.OccurredTick)}; settlement={NormalizeInlineText(record.SettlementLabel, "UnknownSettlement")}; " +
                    $"destroyed_by={NormalizeInlineText(record.DestroyedByFactionName, "Unknown")}");
            }
        }

        private static void AppendRaidDamageIntel(StringBuilder sb, Faction faction)
        {
            FactionIntelLedgerComponent ledger = FactionIntelLedgerComponent.Instance;
            List<FactionRaidDamageRecord> records = ledger?.GetRaidDamageRecordsForAttacker(faction)
                ?? new List<FactionRaidDamageRecord>();

            if (records.Count == 0)
            {
                sb.AppendLine("FactionRaidImpactOnPlayerLatest: none");
                sb.AppendLine("FactionRaidImpactOnPlayerTotal: buildings_destroyed=0, player_deaths=0, player_downed_peak_sum=0, player_loss_sum=0");
                sb.AppendLine("FactionRaidCasualtiesLatest: attacker_deaths=0");
                sb.AppendLine("FactionRaidCasualtiesTotal: attacker_deaths=0");
                return;
            }

            FactionRaidDamageRecord latest = records[0];
            int totalBuildingsDestroyed = records.Sum(record => Math.Max(0, record.PlayerBuildingsDestroyed));
            int totalPlayerDeaths = records.Sum(record => Math.Max(0, record.PlayerDeaths));
            int totalPlayerDownedPeak = records.Sum(record => Math.Max(0, record.PlayerDownedPeak));
            int totalAttackerDeaths = records.Sum(record => Math.Max(0, record.AttackerDeaths));

            sb.AppendLine(
                $"FactionRaidImpactOnPlayerLatest: when={FormatTick(latest.BattleEndTick)}, buildings_destroyed={Math.Max(0, latest.PlayerBuildingsDestroyed)}, " +
                $"player_deaths={Math.Max(0, latest.PlayerDeaths)}, player_downed_peak={Math.Max(0, latest.PlayerDownedPeak)}, " +
                $"player_loss_sum={Math.Max(0, latest.PlayerDeaths) + Math.Max(0, latest.PlayerDownedPeak)}");
            sb.AppendLine(
                $"FactionRaidImpactOnPlayerTotal: buildings_destroyed={totalBuildingsDestroyed}, player_deaths={totalPlayerDeaths}, " +
                $"player_downed_peak_sum={totalPlayerDownedPeak}, player_loss_sum={totalPlayerDeaths + totalPlayerDownedPeak}");
            sb.AppendLine(
                $"FactionRaidCasualtiesLatest: attacker_deaths={Math.Max(0, latest.AttackerDeaths)} (when={FormatTick(latest.BattleEndTick)})");
            sb.AppendLine($"FactionRaidCasualtiesTotal: attacker_deaths={totalAttackerDeaths}");
        }

        private static void AppendPlayerTechLevel(StringBuilder sb)
        {
            TechLevel techLevel = Faction.OfPlayer?.def?.techLevel ?? TechLevel.Undefined;
            sb.AppendLine("PlayerFactionTechLevel: " + techLevel);
        }

        private static string ToBoolText(bool value)
        {
            return value ? "true" : "false";
        }

        private static string NormalizeInlineText(string text, string fallback)
        {
            string normalized = (text ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            if (normalized.Length == 0)
            {
                return fallback;
            }

            return normalized;
        }

        private static string FormatTick(int tick)
        {
            if (tick <= 0)
            {
                return "unknown";
            }

            float days = tick / (float)GenDate.TicksPerDay;
            return $"tick:{tick}; day:{days:F2}";
        }
    }
}

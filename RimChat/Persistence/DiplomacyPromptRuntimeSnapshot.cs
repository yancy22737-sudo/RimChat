using RimWorld;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: PromptPersistenceService dynamic block renderers and world-state observers.
    /// Responsibility: immutable read-only snapshot for diplomacy runtime prompt heavy blocks and validation metadata.
    /// </summary>
    public sealed class DiplomacyPromptRuntimeSnapshot
    {
        public DiplomacyPromptRuntimeSnapshot(
            string factionLoadId,
            string environmentPromptBlock,
            string memoryDataBlock,
            string factionInfoBlock,
            string playerPawnProfileBlock,
            string playerRoyaltySummaryBlock,
            string factionSettlementSummaryBlock,
            int builtTick,
            int memoryRevision,
            int worldEventRevision,
            int playerGoodwill,
            FactionRelationKind playerRelationKind,
            long promptFilesStampUtcTicks,
            int settingsSignature)
        {
            FactionLoadId = factionLoadId ?? string.Empty;
            EnvironmentPromptBlock = environmentPromptBlock ?? string.Empty;
            MemoryDataBlock = memoryDataBlock ?? string.Empty;
            FactionInfoBlock = factionInfoBlock ?? string.Empty;
            PlayerPawnProfileBlock = playerPawnProfileBlock ?? string.Empty;
            PlayerRoyaltySummaryBlock = playerRoyaltySummaryBlock ?? string.Empty;
            FactionSettlementSummaryBlock = factionSettlementSummaryBlock ?? string.Empty;
            BuiltTick = builtTick;
            MemoryRevision = memoryRevision;
            WorldEventRevision = worldEventRevision;
            PlayerGoodwill = playerGoodwill;
            PlayerRelationKind = playerRelationKind;
            PromptFilesStampUtcTicks = promptFilesStampUtcTicks;
            SettingsSignature = settingsSignature;
        }

        public string FactionLoadId { get; }

        public string EnvironmentPromptBlock { get; }

        public string MemoryDataBlock { get; }

        public string FactionInfoBlock { get; }

        public string PlayerPawnProfileBlock { get; }

        public string PlayerRoyaltySummaryBlock { get; }

        public string FactionSettlementSummaryBlock { get; }

        public int BuiltTick { get; }

        public int MemoryRevision { get; }

        public int WorldEventRevision { get; }

        public int PlayerGoodwill { get; }

        public FactionRelationKind PlayerRelationKind { get; }

        public long PromptFilesStampUtcTicks { get; }

        public int SettingsSignature { get; }
    }
}

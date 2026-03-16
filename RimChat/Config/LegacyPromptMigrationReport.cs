using System;
using System.Collections.Generic;
using System.Linq;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: none.
    /// Responsibility: describe the latest legacy prompt import result for UI, logs, and disk reports.
    /// </summary>
    [Serializable]
    internal sealed class LegacyPromptMigrationReport
    {
        public string GeneratedAtUtc = string.Empty;
        public string SourceId = string.Empty;
        public int ImportedCount;
        public int RewrittenCount;
        public int RejectedCount;
        public int DefaultedCount;
        public List<LegacyPromptMigrationEntry> Entries = new List<LegacyPromptMigrationEntry>();

        public LegacyPromptMigrationReport Clone()
        {
            return new LegacyPromptMigrationReport
            {
                GeneratedAtUtc = GeneratedAtUtc ?? string.Empty,
                SourceId = SourceId ?? string.Empty,
                ImportedCount = ImportedCount,
                RewrittenCount = RewrittenCount,
                RejectedCount = RejectedCount,
                DefaultedCount = DefaultedCount,
                Entries = Entries?
                    .Where(item => item != null)
                    .Select(item => item.Clone())
                    .ToList() ?? new List<LegacyPromptMigrationEntry>()
            };
        }
    }

    /// <summary>
    /// Dependencies: none.
    /// Responsibility: describe one legacy prompt section import decision.
    /// </summary>
    [Serializable]
    internal sealed class LegacyPromptMigrationEntry
    {
        public string SourceId = string.Empty;
        public string PromptChannel = string.Empty;
        public string SectionId = string.Empty;
        public string Status = string.Empty;
        public string Detail = string.Empty;
        public bool FallbackApplied;

        public LegacyPromptMigrationEntry Clone()
        {
            return new LegacyPromptMigrationEntry
            {
                SourceId = SourceId ?? string.Empty,
                PromptChannel = PromptChannel ?? string.Empty,
                SectionId = SectionId ?? string.Empty,
                Status = Status ?? string.Empty,
                Detail = Detail ?? string.Empty,
                FallbackApplied = FallbackApplied
            };
        }
    }
}

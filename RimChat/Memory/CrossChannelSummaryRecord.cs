using System;
using System.Collections.Generic;
using Verse;

namespace RimChat.Memory
{
    /// <summary>/// Dependencies: Verse.IExposable.
 /// Responsibility: represent one cross-channel memory summary for faction-level long-term recall.
 ///</summary>
    public enum CrossChannelSummarySource
    {
        Unknown = 0,
        RpgDepart = 1,
        DiplomacySession = 2
    }

    public sealed class CrossChannelSummaryRecord : IExposable
    {
        public CrossChannelSummarySource Source = CrossChannelSummarySource.Unknown;
        public string FactionId = string.Empty;
        public int PawnLoadId = -1;
        public string PawnName = string.Empty;
        public string SummaryText = string.Empty;
        public List<string> KeyFacts = new List<string>();
        public int GameTick = 0;
        public float Confidence = 0f;
        public string ContentHash = string.Empty;
        public bool IsLlmFallback = false;
        public long CreatedTimestamp = 0L;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Source, "source", CrossChannelSummarySource.Unknown);
            Scribe_Values.Look(ref FactionId, "factionId", string.Empty);
            Scribe_Values.Look(ref PawnLoadId, "pawnLoadId", -1);
            Scribe_Values.Look(ref PawnName, "pawnName", string.Empty);
            Scribe_Values.Look(ref SummaryText, "summaryText", string.Empty);
            Scribe_Collections.Look(ref KeyFacts, "keyFacts", LookMode.Value);
            Scribe_Values.Look(ref GameTick, "gameTick", 0);
            Scribe_Values.Look(ref Confidence, "confidence", 0f);
            Scribe_Values.Look(ref ContentHash, "contentHash", string.Empty);
            Scribe_Values.Look(ref IsLlmFallback, "isLlmFallback", false);
            Scribe_Values.Look(ref CreatedTimestamp, "createdTimestamp", 0L);

            if (KeyFacts == null)
            {
                KeyFacts = new List<string>();
            }
        }

        public CrossChannelSummaryRecord Clone()
        {
            return new CrossChannelSummaryRecord
            {
                Source = Source,
                FactionId = FactionId ?? string.Empty,
                PawnLoadId = PawnLoadId,
                PawnName = PawnName ?? string.Empty,
                SummaryText = SummaryText ?? string.Empty,
                KeyFacts = KeyFacts != null ? new List<string>(KeyFacts) : new List<string>(),
                GameTick = GameTick,
                Confidence = Confidence,
                ContentHash = ContentHash ?? string.Empty,
                IsLlmFallback = IsLlmFallback,
                CreatedTimestamp = CreatedTimestamp
            };
        }
    }
}

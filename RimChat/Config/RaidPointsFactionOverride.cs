using System;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: Verse Scribe.
    /// Responsibility: persist per-faction raid point tuning overrides.
    /// </summary>
    [Serializable]
    public sealed class RaidPointsFactionOverride : IExposable
    {
        public string FactionDefName = string.Empty;
        public float RaidPointsMultiplier = 1f;
        public float MinRaidPoints = 35f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref FactionDefName, "factionDefName", string.Empty);
            Scribe_Values.Look(ref RaidPointsMultiplier, "raidPointsMultiplier", 1f);
            Scribe_Values.Look(ref MinRaidPoints, "minRaidPoints", 35f);
        }

        public bool MatchesFactionDef(string factionDefName)
        {
            return !string.IsNullOrWhiteSpace(FactionDefName)
                && string.Equals(FactionDefName, factionDefName, StringComparison.OrdinalIgnoreCase);
        }

        public void Normalize()
        {
            FactionDefName = FactionDefName?.Trim() ?? string.Empty;
            RaidPointsMultiplier = ClampMultiplier(RaidPointsMultiplier);
            MinRaidPoints = ClampMinPoints(MinRaidPoints);
        }

        public static float ClampMultiplier(float value)
        {
            return value < 0.1f ? 0.1f : (value > 5f ? 5f : value);
        }

        public static float ClampMinPoints(float value)
        {
            return value < 0f ? 0f : (value > 1000f ? 1000f : value);
        }
    }
}

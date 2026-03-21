using System;
using System.Collections.Generic;
using System.Linq;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: preset payload equivalence helpers and canonical default payload builder.
    /// Responsibility: resolve stable default preset identity and timestamped auto-fork naming.
    /// </summary>
    internal sealed partial class PromptPresetService
    {
        private static string BuildTimestampPresetName(string prefix, DateTime nowLocal)
        {
            string stem = string.IsNullOrWhiteSpace(prefix) ? "Custom" : prefix.Trim();
            return $"{stem} {nowLocal:yyyyMMdd-HHmmss}";
        }

        private static string ResolveDefaultPresetId(List<PromptPresetConfig> presets)
        {
            List<PromptPresetConfig> all = presets?.Where(p => p != null).ToList() ?? new List<PromptPresetConfig>();
            if (all.Count == 0)
            {
                return string.Empty;
            }

            PromptPresetChannelPayloads canonicalPayload = CreateCanonicalDefaultPayload();
            List<PromptPresetConfig> candidates = all
                .Where(p => IsCanonicalDefaultCandidate(p, canonicalPayload))
                .ToList();
            PromptPresetConfig selected = candidates.Count > 0
                ? SelectEarliestPreset(candidates, all)
                : SelectEarliestPreset(all, all);
            return selected?.Id ?? all[0].Id;
        }

        private static bool IsCanonicalDefaultCandidate(PromptPresetConfig preset, PromptPresetChannelPayloads canonicalPayload)
        {
            if (preset?.ChannelPayloads == null)
            {
                return false;
            }

            PromptPresetChannelPayloads left = preset.ChannelPayloads.Clone();
            PromptPresetChannelPayloads right = canonicalPayload?.Clone() ?? CreateCanonicalDefaultPayload();
            NormalizePayload(left);
            NormalizePayload(right);
            return ArePayloadsEquivalent(left, right);
        }

        private static PromptPresetConfig SelectEarliestPreset(
            List<PromptPresetConfig> candidates,
            List<PromptPresetConfig> allPresets)
        {
            List<PromptPresetConfig> all = candidates?.Where(p => p != null).ToList() ?? new List<PromptPresetConfig>();
            if (all.Count == 0)
            {
                return null;
            }

            return all
                .OrderBy(p => ParseCreatedAtOrMax(p.CreatedAtUtc))
                .ThenBy(p => ResolvePresetIndex(allPresets, p.Id))
                .FirstOrDefault();
        }

        private static DateTime ParseCreatedAtOrMax(string value)
        {
            if (DateTime.TryParse(value, out DateTime parsed))
            {
                return parsed;
            }

            return DateTime.MaxValue;
        }

        private static int ResolvePresetIndex(List<PromptPresetConfig> presets, string presetId)
        {
            if (presets == null || string.IsNullOrWhiteSpace(presetId))
            {
                return int.MaxValue;
            }

            int index = presets.FindIndex(p => string.Equals(p?.Id, presetId, StringComparison.Ordinal));
            return index < 0 ? int.MaxValue : index;
        }
    }
}

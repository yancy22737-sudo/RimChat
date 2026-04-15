using System;
using System.Collections.Generic;
using Verse;

namespace RimChat.Core
{
    /// <summary>
    /// Dependencies: LoadedModManager mod list.
    /// Responsibility: provide cached mod-token availability lookup for runtime hot paths.
    /// </summary>
    public static class ModDependencyProbe
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, bool> AvailabilityByToken =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public static bool IsLoaded(string token)
        {
            string normalized = token?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            lock (CacheLock)
            {
                if (AvailabilityByToken.TryGetValue(normalized, out bool cached))
                {
                    return cached;
                }
            }

            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
            if (mods == null || mods.Count == 0)
            {
                return false;
            }

            bool detected = false;
            for (int i = 0; i < mods.Count; i++)
            {
                ModContentPack mod = mods[i];
                if (mod == null)
                {
                    continue;
                }

                string packageId = mod.PackageIdPlayerFacing ?? string.Empty;
                string name = mod.Name ?? string.Empty;
                if (ContainsToken(packageId, normalized) || ContainsToken(name, normalized))
                {
                    detected = true;
                    break;
                }
            }

            lock (CacheLock)
            {
                AvailabilityByToken[normalized] = detected;
            }

            return detected;
        }

        private static bool ContainsToken(string source, string token)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   !string.IsNullOrWhiteSpace(token) &&
                   source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

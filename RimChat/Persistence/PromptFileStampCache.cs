using System;
using System.Collections.Generic;
using System.IO;
using RimChat.Config;

namespace RimChat.Persistence
{
    /// <summary>
    /// Responsibility: cache prompt file last-write timestamps to avoid per-tick disk IO.
    /// </summary>
    public sealed class PromptFileStampCache
    {
        private const int CacheValidityTicks = 250; // ~5 seconds at 60fps

        private long cachedStamp = -1;
        private int cachedAtTick = -1;
        private readonly object syncRoot = new object();

        public long GetStamp(int currentTick)
        {
            lock (syncRoot)
            {
                if (cachedAtTick > 0 && currentTick - cachedAtTick < CacheValidityTicks)
                {
                    return cachedStamp;
                }

                cachedStamp = ComputePromptFilesStampUtcTicks();
                cachedAtTick = currentTick;
                return cachedStamp;
            }
        }

        public void Prime(int currentTick)
        {
            lock (syncRoot)
            {
                if (cachedAtTick > 0)
                {
                    return;
                }

                cachedStamp = ComputePromptFilesStampUtcTicks();
                cachedAtTick = currentTick;
            }
        }

        public void Invalidate()
        {
            lock (syncRoot)
            {
                cachedAtTick = -1;
            }
        }

        private static long ComputePromptFilesStampUtcTicks()
        {
            long maxTicks = 0L;
            foreach (string path in EnumeratePromptFilePaths())
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                long ticks = File.GetLastWriteTimeUtc(path).Ticks;
                if (ticks > maxTicks)
                {
                    maxTicks = ticks;
                }
            }

            return maxTicks;
        }

        private static IEnumerable<string> EnumeratePromptFilePaths()
        {
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SystemPromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.DiplomacyPromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SocialCirclePromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.FactionPromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.PromptUnifiedDefaultFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.FactionPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.PromptUnifiedCustomFileName);
        }
    }
}

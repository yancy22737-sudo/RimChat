using System;
using System.Collections.Generic;
using System.Globalization;

namespace RimChat.Memory
{
    /// <summary>
    /// Dependencies: RPG archive prompt memory builder and runtime archive mutation hooks.
    /// Responsibility: cache prompt memory blocks with version-based invalidation to reduce repeated main-thread rebuilds.
    /// </summary>
    public sealed partial class RpgNpcDialogueArchiveManager
    {
        private struct PromptMemoryCacheEntry
        {
            public long Version;
            public string MemoryBlock;
        }

        private readonly Dictionary<string, PromptMemoryCacheEntry> _promptMemoryCache =
            new Dictionary<string, PromptMemoryCacheEntry>(StringComparer.Ordinal);
        private long _promptMemoryCacheVersion;

        private void ResetPromptMemoryCacheLockless()
        {
            _promptMemoryCacheVersion = 0L;
            _promptMemoryCache.Clear();
        }

        private void InvalidatePromptMemoryCacheLockless()
        {
            _promptMemoryCacheVersion++;
            _promptMemoryCache.Clear();
        }

        private bool TryGetPromptMemoryCacheLockless(string cacheKey, out string memoryBlock)
        {
            memoryBlock = string.Empty;
            if (string.IsNullOrWhiteSpace(cacheKey) ||
                !_promptMemoryCache.TryGetValue(cacheKey, out PromptMemoryCacheEntry cacheEntry))
            {
                return false;
            }

            if (cacheEntry.Version != _promptMemoryCacheVersion)
            {
                _promptMemoryCache.Remove(cacheKey);
                return false;
            }

            memoryBlock = cacheEntry.MemoryBlock ?? string.Empty;
            return true;
        }

        private void SetPromptMemoryCacheLockless(string cacheKey, string memoryBlock)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return;
            }

            _promptMemoryCache[cacheKey] = new PromptMemoryCacheEntry
            {
                Version = _promptMemoryCacheVersion,
                MemoryBlock = memoryBlock ?? string.Empty
            };
        }

        private static string BuildPromptMemoryCacheKey(
            int targetPawnLoadId,
            int interlocutorPawnLoadId,
            int summaryTurnLimit,
            int summaryCharBudget)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3}",
                targetPawnLoadId,
                interlocutorPawnLoadId,
                summaryTurnLimit,
                summaryCharBudget);
        }
    }
}

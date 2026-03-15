using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Scriban;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: Scriban Template runtime.
    /// Responsibility: provide bounded LRU cache for compiled prompt templates.
    /// </summary>
    internal sealed class PromptTemplateCache
    {
        private readonly int _capacity;
        private readonly object _syncRoot = new object();
        private readonly Dictionary<string, LinkedListNode<PromptTemplateCacheEntry>> _index;
        private readonly LinkedList<PromptTemplateCacheEntry> _lru = new LinkedList<PromptTemplateCacheEntry>();

        public PromptTemplateCache(int capacity)
        {
            _capacity = Math.Max(1, capacity);
            _index = new Dictionary<string, LinkedListNode<PromptTemplateCacheEntry>>(StringComparer.Ordinal);
        }

        public bool TryGet(string key, out Template template)
        {
            lock (_syncRoot)
            {
                if (!_index.TryGetValue(key ?? string.Empty, out LinkedListNode<PromptTemplateCacheEntry> node))
                {
                    template = null;
                    return false;
                }

                Touch(node);
                template = node.Value.Template;
                return template != null;
            }
        }

        public bool Set(string key, Template template)
        {
            if (template == null)
            {
                return false;
            }

            string normalizedKey = key ?? string.Empty;
            lock (_syncRoot)
            {
                if (_index.TryGetValue(normalizedKey, out LinkedListNode<PromptTemplateCacheEntry> existing))
                {
                    existing.Value.Template = template;
                    Touch(existing);
                    return false;
                }

                var node = new LinkedListNode<PromptTemplateCacheEntry>(new PromptTemplateCacheEntry(normalizedKey, template));
                _lru.AddFirst(node);
                _index[normalizedKey] = node;
                return TrimIfNeeded();
            }
        }

        private void Touch(LinkedListNode<PromptTemplateCacheEntry> node)
        {
            if (node == null || node.List != _lru || node == _lru.First)
            {
                return;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
        }

        private bool TrimIfNeeded()
        {
            if (_index.Count <= _capacity)
            {
                return false;
            }

            LinkedListNode<PromptTemplateCacheEntry> tail = _lru.Last;
            if (tail == null)
            {
                return false;
            }

            _lru.RemoveLast();
            _index.Remove(tail.Value.Key);
            return true;
        }
    }

    /// <summary>
    /// Dependencies: PromptRenderTelemetry counters.
    /// Responsibility: expose immutable render/cache snapshot for diagnostics.
    /// </summary>
    internal sealed class PromptRenderTelemetrySnapshot
    {
        public long CacheHits { get; set; }
        public long CacheMisses { get; set; }
        public long CacheEvictions { get; set; }
        public long ParseCount { get; set; }
        public long RenderCount { get; set; }
        public double AverageParseMilliseconds { get; set; }
        public double AverageRenderMilliseconds { get; set; }
        public double CacheHitRatePercent { get; set; }
    }

    /// <summary>
    /// Dependencies: none.
    /// Responsibility: track in-memory template cache and render timing telemetry.
    /// </summary>
    internal static class PromptRenderTelemetry
    {
        private static long _cacheHits;
        private static long _cacheMisses;
        private static long _cacheEvictions;
        private static long _parseCount;
        private static long _parseTicks;
        private static long _renderCount;
        private static long _renderTicks;

        public static void RecordCacheHit()
        {
            Interlocked.Increment(ref _cacheHits);
        }

        public static void RecordCacheMiss()
        {
            Interlocked.Increment(ref _cacheMisses);
        }

        public static void RecordCacheEviction()
        {
            Interlocked.Increment(ref _cacheEvictions);
        }

        public static void RecordParseTicks(long ticks)
        {
            Interlocked.Increment(ref _parseCount);
            Interlocked.Add(ref _parseTicks, Math.Max(0L, ticks));
        }

        public static long RecordRenderTicks(long ticks)
        {
            Interlocked.Add(ref _renderTicks, Math.Max(0L, ticks));
            return Interlocked.Increment(ref _renderCount);
        }

        public static PromptRenderTelemetrySnapshot CaptureSnapshot()
        {
            long hits = Interlocked.Read(ref _cacheHits);
            long misses = Interlocked.Read(ref _cacheMisses);
            long evictions = Interlocked.Read(ref _cacheEvictions);
            long parseCount = Interlocked.Read(ref _parseCount);
            long parseTicks = Interlocked.Read(ref _parseTicks);
            long renderCount = Interlocked.Read(ref _renderCount);
            long renderTicks = Interlocked.Read(ref _renderTicks);
            long cacheRequests = hits + misses;

            return new PromptRenderTelemetrySnapshot
            {
                CacheHits = hits,
                CacheMisses = misses,
                CacheEvictions = evictions,
                ParseCount = parseCount,
                RenderCount = renderCount,
                AverageParseMilliseconds = ConvertTicksToMilliseconds(parseTicks, parseCount),
                AverageRenderMilliseconds = ConvertTicksToMilliseconds(renderTicks, renderCount),
                CacheHitRatePercent = cacheRequests <= 0
                    ? 0d
                    : (double)hits / cacheRequests * 100d
            };
        }

        private static double ConvertTicksToMilliseconds(long ticks, long count)
        {
            if (ticks <= 0 || count <= 0)
            {
                return 0d;
            }

            return ((double)ticks * 1000d / Stopwatch.Frequency) / count;
        }
    }

    internal sealed class PromptTemplateCacheEntry
    {
        public string Key { get; }
        public Template Template { get; set; }

        public PromptTemplateCacheEntry(string key, Template template)
        {
            Key = key ?? string.Empty;
            Template = template;
        }
    }
}

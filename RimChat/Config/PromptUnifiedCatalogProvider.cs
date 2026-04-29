using System;
using System.Collections.Generic;
using System.IO;
using RimChat.Persistence;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: prompt domain file catalog and Unity JsonUtility.
    /// Responsibility: load/save unified prompt catalog from Prompt/Default and Prompt/Custom.
    /// </summary>
    internal static class PromptUnifiedCatalogProvider
    {
        private static readonly object SyncRoot = new object();
        private static PromptUnifiedCatalog cached;
        private static DateTime cachedWriteTimeUtc;
        private static string cachedPath = string.Empty;

        internal static PromptUnifiedCatalog LoadMerged()
        {
            lock (SyncRoot)
            {
                string customPath = PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.PromptUnifiedCustomFileName);
                if (IsCacheValid(customPath))
                {
                    return cached?.Clone() ?? PromptUnifiedCatalog.CreateFallback();
                }

                PromptUnifiedCatalog result = LoadDefault();
                if (File.Exists(customPath))
                {
                    PromptUnifiedCatalog custom = TryRead(customPath);
                    if (custom != null)
                    {
                        PromptUnifiedCatalog merged = custom.Clone();
                        RestoreCustomNodeRegistrations(merged);
                        merged.NormalizeWith(result);
                        result = merged;
                    }
                }

                result ??= PromptUnifiedCatalog.CreateFallback();
                RestoreCustomNodeRegistrations(result);
                result.NormalizeWith(PromptUnifiedCatalog.CreateFallback());
                cached = result.Clone();
                cachedPath = customPath;
                cachedWriteTimeUtc = File.Exists(customPath) ? File.GetLastWriteTimeUtc(customPath) : DateTime.MinValue;
                return result;
            }
        }

        internal static void SaveCustom(PromptUnifiedCatalog catalog)
        {
            if (catalog == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                string path = PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.PromptUnifiedCustomFileName);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                PromptUnifiedCatalog normalized = catalog.Clone();
                normalized.NormalizeWith(LoadDefault());
                string json = ReflectionJsonFieldSerializer.Serialize(normalized, prettyPrint: true);
                File.WriteAllText(path, json);
                cached = normalized.Clone();
                cachedPath = path;
                cachedWriteTimeUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            }
        }

        private static PromptUnifiedCatalog LoadDefault()
        {
            string path = PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.PromptUnifiedDefaultFileName);
            PromptUnifiedCatalog loaded = TryRead(path);
            if (loaded == null)
            {
                loaded = PromptUnifiedCatalog.CreateFallback();
            }

            loaded.NormalizeWith(PromptUnifiedCatalog.CreateFallback());
            return loaded;
        }

        private static PromptUnifiedCatalog TryRead(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<PromptUnifiedCatalog>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to parse unified prompt catalog from {path}: {ex.Message}");
                return null;
            }
        }

        private static bool IsCacheValid(string customPath)
        {
            if (cached == null || !string.Equals(cachedPath, customPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!File.Exists(customPath))
            {
                return cachedWriteTimeUtc == DateTime.MinValue;
            }

            return File.GetLastWriteTimeUtc(customPath) == cachedWriteTimeUtc;
        }

        private static void RestoreCustomNodeRegistrations(PromptUnifiedCatalog catalog)
        {
            if (catalog?.Channels == null)
            {
                return;
            }

            var allRegistrations = new List<PromptUnifiedNodeRegistration>();
            foreach (PromptUnifiedChannelConfig channel in catalog.Channels)
            {
                if (channel?.CustomNodes == null)
                {
                    continue;
                }

                foreach (PromptUnifiedNodeRegistration reg in channel.CustomNodes)
                {
                    if (reg != null && !string.IsNullOrWhiteSpace(reg.NodeId) &&
                        !allRegistrations.Any(r => string.Equals(r.NodeId, reg.NodeId, StringComparison.OrdinalIgnoreCase)))
                    {
                        allRegistrations.Add(reg);
                    }
                }
            }

            PromptUnifiedNodeSchemaCatalog.RestoreCustomNodes(allRegistrations);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using RimChat.Core;
using Verse;

namespace RimChat.Patches
{
    /// <summary>
    /// Dependencies: Verse.Translator.TryTranslate, RimChat mod content root path.
    /// Responsibility: provide a strict English fallback for RimChat keyed texts when
    /// the active language pack does not contain RimChat translation files.
    /// </summary>
    [HarmonyPatch]
    public static class TranslatorPatch_RimChatEnglishFallback
    {
        private const string KeyPrefix = "RimChat_";
        private static bool warnedLanguageFallback;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Translator),
                nameof(Translator.TryTranslate),
                new[] { typeof(string), typeof(TaggedString).MakeByRefType() });
        }

        [HarmonyPostfix]
        private static void Postfix(string __0, ref TaggedString __1, ref bool __result)
        {
            if (__result || string.IsNullOrWhiteSpace(__0))
            {
                return;
            }

            if (!__0.StartsWith(KeyPrefix, StringComparison.Ordinal))
            {
                return;
            }

            if (!RimChatEnglishKeyFallbackCatalog.TryGetValue(__0, out string fallback))
            {
                return;
            }

            __1 = fallback;
            __result = true;
            WarnOnceForLanguageFallback();
        }

        private static void WarnOnceForLanguageFallback()
        {
            if (warnedLanguageFallback)
            {
                return;
            }

            warnedLanguageFallback = true;
            string activeFolder = LanguageDatabase.activeLanguage?.folderName ?? "(null)";
            Log.Warning(
                $"[RimChat] Active language '{activeFolder}' is missing RimChat keyed entries. " +
                "Fail-fast fallback is now serving RimChat_* keys from English.");
        }
    }

    internal static class RimChatEnglishKeyFallbackCatalog
    {
        private static readonly object Sync = new object();
        private static Dictionary<string, string> cache;
        private static bool loadAttempted;

        internal static bool TryGetValue(string key, out string value)
        {
            EnsureLoaded();
            if (cache != null && cache.TryGetValue(key, out string text))
            {
                value = text;
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static void EnsureLoaded()
        {
            if (loadAttempted)
            {
                return;
            }

            lock (Sync)
            {
                if (loadAttempted)
                {
                    return;
                }

                loadAttempted = true;
                cache = LoadEnglishKeyedDictionary();
            }
        }

        private static Dictionary<string, string> LoadEnglishKeyedDictionary()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            string rootDir = RimChatMod.Instance?.Content?.RootDir
                ?? LoadedModManager.GetMod<RimChatMod>()?.Content?.RootDir
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rootDir))
            {
                Log.Warning("[RimChat] English keyed fallback initialization skipped: mod root directory is empty.");
                return map;
            }

            string xmlPath = Path.Combine(rootDir, "1.6", "Languages", "English", "Keyed", "RimChat_Keys.xml");
            if (!File.Exists(xmlPath))
            {
                Log.Warning($"[RimChat] English keyed fallback initialization skipped: file not found at '{xmlPath}'.");
                return map;
            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(xmlPath);
                XmlNode languageData = document.DocumentElement;
                if (languageData == null)
                {
                    return map;
                }

                foreach (XmlNode child in languageData.ChildNodes)
                {
                    if (child.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    string key = child.Name?.Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    string text = child.InnerText ?? string.Empty;
                    map[key] = text;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to load English keyed fallback dictionary: {ex.Message}");
            }

            return map;
        }
    }
}

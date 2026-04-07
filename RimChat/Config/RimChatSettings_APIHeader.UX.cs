using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimChat.Core;
using RimChat.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: RimWorld/Verse widgets, mod content path APIs, version-log viewer window.
    /// Responsibility: render API tab header tools and load localized version-log content.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private const string EnglishLanguageFolder = "English";
        private const string LanguagesRelativePath = "1.6\\Languages";
        private const string VersionLogFileLocalizedDefault = "VersionLog.txt";
        private const string VersionLogFileEnglish = "VersionLog_en.txt";
        private const string VersionLogFileByLanguagePattern = "VersionLog_{0}.txt";
        private const string RimChatGitHubUrl = "https://github.com/yancy22737-sudo/RimChat";
        private const string DefaultVersionValue = "0.0.0";
        private static readonly Dictionary<string, string> LanguageFolderAliasMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = "english",
                ["enus"] = "english",
                ["eng"] = "english",
                ["zh"] = "chinesesimplified",
                ["zhcn"] = "chinesesimplified",
                ["zhhans"] = "chinesesimplified",
                ["chs"] = "chinesesimplified",
                ["simplifiedchinese"] = "chinesesimplified",
                ["zhtw"] = "chinesetraditional",
                ["zhhant"] = "chinesetraditional",
                ["cht"] = "chinesetraditional",
                ["traditionalchinese"] = "chinesetraditional"
            };

        private string cachedVersionLanguage = string.Empty;
        private string cachedVersionLogPath = string.Empty;
        private string cachedVersionLogContent = string.Empty;
        private string cachedVersionValue = DefaultVersionValue;
        private bool cachedVersionReadFailed;
        private string cachedVersionReadError = string.Empty;

        private void DrawApiSettingsHeaderBar(Listing_Standard listing)
        {
            EnsureVersionLogCache();
            Rect rowRect = listing.GetRect(24f);

            string versionLabel = "RimChat_APIVersionButtonLabel".Translate(cachedVersionValue);
            const float githubWidth = 74f;
            float versionWidth = Mathf.Clamp(Text.CalcSize(versionLabel).x + 16f, 130f, 250f);
            const float spacing = 6f;

            Rect githubRect = new Rect(rowRect.xMax - githubWidth, rowRect.y, githubWidth, rowRect.height);
            Rect versionRect = new Rect(githubRect.x - spacing - versionWidth, rowRect.y, versionWidth, rowRect.height);
            Rect titleRect = new Rect(rowRect.x, rowRect.y, versionRect.x - spacing - rowRect.x, rowRect.height);

            Widgets.Label(titleRect, "RimChat_APISettings".Translate());
            DrawVersionButton(versionRect, versionLabel);
            DrawGitHubButton(githubRect);
        }

        private void DrawVersionButton(Rect buttonRect, string label)
        {
            bool clicked = Widgets.ButtonText(buttonRect, label);
            RegisterTooltip(buttonRect, "RimChat_APIVersionButtonTooltip");
            if (!clicked)
            {
                return;
            }

            SoundDefOf.Click.PlayOneShotOnCamera(null);
            Find.WindowStack.Add(new Dialog_VersionLogViewer(
                "RimChat_VersionLogWindowTitle".Translate(),
                GetVersionLogDisplayContent()));
        }

        private void DrawGitHubButton(Rect buttonRect)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(0.24f, 0.72f, 0.24f);
            bool clicked = Widgets.ButtonText(buttonRect, "RimChat_APIGitHubButton".Translate());
            GUI.color = previousColor;
            RegisterTooltip(buttonRect, "RimChat_APIGitHubButtonTooltip");

            if (!clicked)
            {
                return;
            }

            SoundDefOf.Click.PlayOneShotOnCamera(null);
            Application.OpenURL(RimChatGitHubUrl);
        }

        private void EnsureVersionLogCache()
        {
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            if (string.Equals(cachedVersionLanguage, language, StringComparison.Ordinal))
            {
                return;
            }

            cachedVersionLanguage = language;
            cachedVersionLogPath = ResolveVersionLogPath(language);
            cachedVersionReadFailed = false;
            cachedVersionReadError = string.Empty;
            cachedVersionLogContent = ReadVersionLogContent(cachedVersionLogPath);
            cachedVersionValue = ParseVersionFirstLine(cachedVersionLogContent);
        }

        private string ResolveVersionLogPath(string languageFolder)
        {
            string rootDir = ResolveModRootDir();
            List<string> availableLanguages = GetAvailableLanguages(rootDir);
            string matchedFolder = ResolveActiveLanguageFolder(languageFolder, availableLanguages);
            bool fallbackToEnglishFolder = !IsFolderMatched(matchedFolder, languageFolder);
            if (fallbackToEnglishFolder)
            {
                string fallbackPath = CombineRootPath(rootDir, VersionLogFileEnglish);
                string availableLabel = availableLanguages.Count == 0
                    ? "(none)"
                    : string.Join(", ", availableLanguages.ToArray());
                Log.Warning(
                    $"[RimChat] Active language folder '{languageFolder}' was not found in '{LanguagesRelativePath}'. " +
                    $"Available folders: {availableLabel}. Fail-fast fallback to '{EnglishLanguageFolder}' and '{fallbackPath}'.");
            }

            List<string> candidates = BuildVersionLogCandidates(rootDir, matchedFolder);
            for (int i = 0; i < candidates.Count; i++)
            {
                string path = candidates[i];
                if (File.Exists(path))
                {
                    if (i > 0)
                    {
                        Log.Warning(
                            $"[RimChat] Version log file missing for language folder '{matchedFolder}'. " +
                            $"Tried '{candidates[0]}'. Fail-fast fallback to '{path}'.");
                    }

                    return path;
                }
            }

            string englishPath = CombineRootPath(rootDir, VersionLogFileEnglish);
            Log.Warning(
                $"[RimChat] No version log file exists for language folder '{matchedFolder}'. " +
                $"Tried: {string.Join(" | ", candidates.ToArray())}. Final fallback path: '{englishPath}'.");
            return englishPath;
        }

        private static string ResolveModRootDir()
        {
            return RimChatMod.Instance?.Content?.RootDir
                ?? LoadedModManager.GetMod<RimChatMod>()?.Content?.RootDir
                ?? string.Empty;
        }

        private List<string> GetAvailableLanguages()
        {
            return GetAvailableLanguages(ResolveModRootDir());
        }

        private static List<string> GetAvailableLanguages(string rootDir)
        {
            var languages = new List<string>();
            string languagesRoot = string.IsNullOrWhiteSpace(rootDir)
                ? LanguagesRelativePath
                : Path.Combine(rootDir, LanguagesRelativePath);
            if (!Directory.Exists(languagesRoot))
            {
                return languages;
            }

            string[] dirs = Directory.GetDirectories(languagesRoot);
            for (int i = 0; i < dirs.Length; i++)
            {
                string folder = Path.GetFileName(dirs[i])?.Trim();
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }

                if (languages.Exists(item => string.Equals(item, folder, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                languages.Add(folder);
            }

            languages.Sort(StringComparer.OrdinalIgnoreCase);
            return languages;
        }

        private static string ResolveActiveLanguageFolder(string languageFolder, List<string> availableLanguages)
        {
            string direct = FindFolderByExactName(languageFolder, availableLanguages);
            if (!string.IsNullOrEmpty(direct))
            {
                return direct;
            }

            string normalized = NormalizeLanguageToken(languageFolder);
            string normalizedMatch = FindFolderByNormalizedName(normalized, availableLanguages);
            if (!string.IsNullOrEmpty(normalizedMatch))
            {
                return normalizedMatch;
            }

            if (LanguageFolderAliasMap.TryGetValue(normalized, out string aliasTarget))
            {
                string aliasMatch = FindFolderByNormalizedName(aliasTarget, availableLanguages);
                if (!string.IsNullOrEmpty(aliasMatch))
                {
                    return aliasMatch;
                }
            }

            return FindFolderByExactName(EnglishLanguageFolder, availableLanguages) ?? EnglishLanguageFolder;
        }

        private static string FindFolderByExactName(string input, List<string> availableLanguages)
        {
            if (string.IsNullOrWhiteSpace(input) || availableLanguages == null)
            {
                return null;
            }

            for (int i = 0; i < availableLanguages.Count; i++)
            {
                if (string.Equals(availableLanguages[i], input.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return availableLanguages[i];
                }
            }

            return null;
        }

        private static string FindFolderByNormalizedName(string normalizedTarget, List<string> availableLanguages)
        {
            if (string.IsNullOrWhiteSpace(normalizedTarget) || availableLanguages == null)
            {
                return null;
            }

            for (int i = 0; i < availableLanguages.Count; i++)
            {
                string normalizedCurrent = NormalizeLanguageToken(availableLanguages[i]);
                if (string.Equals(normalizedCurrent, normalizedTarget, StringComparison.Ordinal))
                {
                    return availableLanguages[i];
                }
            }

            return null;
        }

        private static string NormalizeLanguageToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(value.Length);
            string trimmed = value.Trim();
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
        }

        private static bool IsFolderMatched(string matchedFolder, string activeFolder)
        {
            if (string.IsNullOrWhiteSpace(activeFolder))
            {
                return false;
            }

            if (string.Equals(matchedFolder, activeFolder.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string normalizedMatched = NormalizeLanguageToken(matchedFolder);
            string normalizedActive = NormalizeLanguageToken(activeFolder);
            return !string.IsNullOrWhiteSpace(normalizedMatched)
                && string.Equals(normalizedMatched, normalizedActive, StringComparison.Ordinal);
        }

        private static List<string> BuildVersionLogCandidates(string rootDir, string matchedFolder)
        {
            var candidates = new List<string>();
            bool isEnglish = string.Equals(
                NormalizeLanguageToken(matchedFolder),
                NormalizeLanguageToken(EnglishLanguageFolder),
                StringComparison.Ordinal);

            if (!isEnglish && !string.IsNullOrWhiteSpace(matchedFolder))
            {
                string languageSpecific = string.Format(
                    VersionLogFileByLanguagePattern,
                    matchedFolder.Trim());
                candidates.Add(CombineRootPath(rootDir, languageSpecific));
                candidates.Add(CombineRootPath(rootDir, VersionLogFileLocalizedDefault));
            }

            candidates.Add(CombineRootPath(rootDir, VersionLogFileEnglish));
            return candidates;
        }

        private static string CombineRootPath(string rootDir, string fileName)
        {
            return string.IsNullOrWhiteSpace(rootDir)
                ? fileName
                : Path.Combine(rootDir, fileName);
        }

        private string ReadVersionLogContent(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return string.Empty;
            }

            try
            {
                return File.ReadAllText(filePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                cachedVersionReadFailed = true;
                cachedVersionReadError = ex.Message;
                Log.Warning($"[RimChat] Failed to read version log file: {filePath}. {ex.Message}");
                return string.Empty;
            }
        }

        private string GetVersionLogDisplayContent()
        {
            if (!string.IsNullOrWhiteSpace(cachedVersionLogContent))
            {
                return cachedVersionLogContent;
            }

            if (cachedVersionReadFailed)
            {
                return "RimChat_VersionLogReadFailed".Translate(cachedVersionLogPath, cachedVersionReadError);
            }

            if (!File.Exists(cachedVersionLogPath))
            {
                return "RimChat_VersionLogMissing".Translate(cachedVersionLogPath);
            }

            return "RimChat_VersionLogEmpty".Translate(cachedVersionLogPath);
        }

        internal string GetVersionDisplayVersion()
        {
            EnsureVersionLogCache();
            return cachedVersionValue;
        }

        internal string GetVersionLogDisplayContentForLanguage(string languageFolder)
        {
            string path = ResolveVersionLogPath(languageFolder);
            string content = ReadVersionLogContentFromPath(path, out string readError);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            if (!string.IsNullOrWhiteSpace(readError))
            {
                return "RimChat_VersionLogReadFailed".Translate(path, readError);
            }

            if (!File.Exists(path))
            {
                return "RimChat_VersionLogMissing".Translate(path);
            }

            return "RimChat_VersionLogEmpty".Translate(path);
        }

        private static string ReadVersionLogContentFromPath(string filePath, out string readError)
        {
            readError = string.Empty;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return string.Empty;
            }

            try
            {
                return File.ReadAllText(filePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                readError = ex.Message;
                Log.Warning($"[RimChat] Failed to read version log file: {filePath}. {ex.Message}");
                return string.Empty;
            }
        }

        private static string ParseVersionFirstLine(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return DefaultVersionValue;
            }

            string[] lines = content.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i]?.Trim();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    return line;
                }
            }

            return DefaultVersionValue;
        }
    }
}

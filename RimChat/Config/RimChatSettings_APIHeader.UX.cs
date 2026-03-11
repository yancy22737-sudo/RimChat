using System;
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
        private const string VersionLogFileChinese = "VersionLog.txt";
        private const string VersionLogFileEnglish = "VersionLog_en.txt";
        private const string RimChatGitHubUrl = "https://github.com/yancy22737-sudo/RimChat";
        private const string DefaultVersionValue = "0.0.0";

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
            string fileName = SelectVersionLogFileName(languageFolder);
            string rootDir = RimChatMod.Instance?.Content?.RootDir
                ?? LoadedModManager.GetMod<RimChatMod>()?.Content?.RootDir
                ?? string.Empty;
            return string.IsNullOrWhiteSpace(rootDir) ? fileName : Path.Combine(rootDir, fileName);
        }

        private static string SelectVersionLogFileName(string languageFolder)
        {
            return languageFolder switch
            {
                "ChineseSimplified" => VersionLogFileChinese,
                "ChineseTraditional" => VersionLogFileChinese,
                _ => VersionLogFileEnglish
            };
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

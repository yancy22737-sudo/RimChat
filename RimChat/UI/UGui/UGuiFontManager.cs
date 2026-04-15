using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Verse;

namespace RimChat.UI.UGui
{
    /// <summary>
    /// Dependencies: TextMeshPro settings/resources and Verse language state.
    /// Responsibility: provide the shared game-default TMP font for UGUI panels
    /// and report whether current language has usable CJK coverage.
    /// </summary>
    internal static class UGuiFontManager
    {
        private const string BuiltinFontResourceName = "LIBERATIONSANS SDF";

        internal static TMP_FontAsset DefaultFont { get; private set; }
        internal static bool SupportsCjk { get; private set; }

        private static bool _initialized;
        private static bool _ownsFontAsset;

        /// <summary>
        /// Ensure font state is initialized once per session.
        /// </summary>
        internal static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _ownsFontAsset = false;
            DefaultFont = ResolveGameDefaultFont();

            if (DefaultFont == null)
            {
                SupportsCjk = false;
                Log.Warning("[RimChat] Game default TMP font is unavailable. UGUI CJK rendering is disabled.");
                return;
            }

            SupportsCjk = HasCjkCoverage(DefaultFont);
            if (SupportsCjk)
            {
                Log.Message($"[RimChat] Using game default TMP font for UGUI: {DefaultFont.name}");
                return;
            }

            Log.Warning($"[RimChat] Game default TMP font '{DefaultFont.name}' lacks CJK coverage. UGUI will fallback to IMGUI.");
        }

        /// <summary>
        /// Apply current shared font to target TMP text.
        /// </summary>
        internal static void ApplyFont(TMP_Text textComponent)
        {
            if (textComponent == null || DefaultFont == null)
            {
                return;
            }

            textComponent.font = DefaultFont;
        }

        /// <summary>
        /// Dispose owned runtime assets only.
        /// </summary>
        internal static void Dispose()
        {
            if (_ownsFontAsset && DefaultFont != null)
            {
                Object.Destroy(DefaultFont);
            }

            DefaultFont = null;
            SupportsCjk = false;
            _ownsFontAsset = false;
            _initialized = false;
        }

        private static TMP_FontAsset ResolveGameDefaultFont()
        {
            TMP_FontAsset gameDefault = TMP_Settings.defaultFontAsset;
            if (gameDefault != null && HasCjkCoverage(gameDefault))
            {
                return gameDefault;
            }

            foreach (TMP_FontAsset fallback in EnumerateGameDefaultFallbacks(gameDefault))
            {
                if (fallback != null && HasCjkCoverage(fallback))
                {
                    return fallback;
                }
            }

            return gameDefault ?? Resources.GetBuiltinResource<TMP_FontAsset>(BuiltinFontResourceName);
        }

        private static bool HasCjkCoverage(TMP_FontAsset font)
        {
            string probe = ResolveCjkProbeText();
            if (string.IsNullOrEmpty(probe))
            {
                probe = "中文";
            }

            if (font.HasCharacters(probe))
            {
                return true;
            }

            uint[] missing;
            return font.HasCharacters(probe, out missing, searchFallbacks: true, tryAddCharacter: false);
        }

        private static string ResolveCjkProbeText()
        {
            string folder = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            if (folder.IndexOf("Japanese", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "日本語";
            }

            if (folder.IndexOf("Korean", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "한국어";
            }

            return "中文";
        }

        private static IEnumerable<TMP_FontAsset> EnumerateGameDefaultFallbacks(TMP_FontAsset primary)
        {
            var seen = new HashSet<TMP_FontAsset>();

            if (primary != null && primary.fallbackFontAssetTable != null)
            {
                for (int i = 0; i < primary.fallbackFontAssetTable.Count; i++)
                {
                    TMP_FontAsset candidate = primary.fallbackFontAssetTable[i];
                    if (candidate == null || !seen.Add(candidate))
                    {
                        continue;
                    }

                    yield return candidate;
                }
            }

            if (TMP_Settings.fallbackFontAssets == null)
            {
                yield break;
            }

            for (int i = 0; i < TMP_Settings.fallbackFontAssets.Count; i++)
            {
                TMP_FontAsset candidate = TMP_Settings.fallbackFontAssets[i];
                if (candidate == null || !seen.Add(candidate))
                {
                    continue;
                }

                yield return candidate;
            }
        }
    }
}

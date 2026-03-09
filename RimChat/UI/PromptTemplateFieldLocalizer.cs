using System;
using System.Collections.Generic;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: Verse translation APIs.
    /// Responsibility: map legacy prompt field names to stable localization keys with safe fallback text.
    /// </summary>
    internal static class PromptTemplateFieldLocalizer
    {
        private static readonly Dictionary<string, string> FieldKeySuffixByName =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CoreStyle"] = "CoreStyle",
                ["核心风格"] = "CoreStyle",
                ["Vocabulary"] = "Vocabulary",
                ["VocabularyFeatures"] = "Vocabulary",
                ["用词特征"] = "Vocabulary",
                ["Tone"] = "Tone",
                ["ToneFeatures"] = "Tone",
                ["语气特征"] = "Tone",
                ["Sentence"] = "Sentence",
                ["SentenceFeatures"] = "Sentence",
                ["句式特征"] = "Sentence",
                ["Taboos"] = "Taboos",
                ["表达禁忌"] = "Taboos"
            };

        public static string GetLabel(string fieldName)
        {
            return GetTranslatedValue(fieldName, string.Empty, fieldName);
        }

        public static string GetDescription(string fieldName, string fallbackDescription)
        {
            return GetTranslatedValue(fieldName, "Desc", fallbackDescription);
        }

        private static string GetTranslatedValue(string fieldName, string keySuffix, string fallback)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                return fallback ?? string.Empty;
            }

            if (!FieldKeySuffixByName.TryGetValue(fieldName.Trim(), out string resolvedSuffix))
            {
                return fallback ?? fieldName;
            }

            string key = $"RimChat_Field{resolvedSuffix}{keySuffix}";
            return TranslateOrFallback(key, fallback ?? fieldName);
        }

        private static string TranslateOrFallback(string key, string fallback)
        {
            string translated = key.Translate();
            return string.Equals(translated, key, StringComparison.Ordinal) ? fallback : translated;
        }
    }
}

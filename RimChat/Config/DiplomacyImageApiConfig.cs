using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace RimChat.Config
{
    /// <summary>/// Dependencies: Verse Scribe serialization APIs.
 /// Responsibility: hold standalone diplomacy image API runtime/persistent settings.
 ///</summary>
    [Serializable]
    public class DiplomacyImageApiConfig : IExposable
    {
        private const int MinImagePixels = 3686400;
        public const string DefaultImageSize = "2560x1440";
        public const string DefaultVolcEngineImageEndpoint = "https://ark.cn-beijing.volces.com/api/v3/images/generations";
        public const string DefaultVolcEngineImageModel = "doubao-seedream-3-0-t2i-250415";
        public bool IsEnabled = false;
        public string Endpoint = string.Empty;
        public string ApiKey = string.Empty;
        public string Model = string.Empty;
        public string DefaultSize = DefaultImageSize;
        public bool DefaultWatermark = false;
        public int TimeoutSeconds = 120;

        public void ExposeData()
        {
            Scribe_Values.Look(ref IsEnabled, "isEnabled", false);
            Scribe_Values.Look(ref Endpoint, "endpoint", string.Empty);
            Scribe_Values.Look(ref ApiKey, "apiKey", string.Empty);
            Scribe_Values.Look(ref Model, "model", string.Empty);
            Scribe_Values.Look(ref DefaultSize, "defaultSize", DefaultImageSize);
            Scribe_Values.Look(ref DefaultWatermark, "defaultWatermark", false);
            Scribe_Values.Look(ref TimeoutSeconds, "timeoutSeconds", 120);
            Normalize();
        }

        public void Normalize()
        {
            Endpoint = NormalizeText(Endpoint);
            ApiKey = NormalizeText(ApiKey);
            Model = NormalizeText(Model);
            DefaultSize = NormalizeImageSize(DefaultSize, DefaultImageSize);

            TimeoutSeconds = Math.Max(10, Math.Min(300, TimeoutSeconds));
        }

        public void ApplyFallbackDefaults(string preferredEndpoint, string preferredModel)
        {
            string endpointFallback = NormalizeText(preferredEndpoint);
            string modelFallback = NormalizeText(preferredModel);

            if (string.IsNullOrWhiteSpace(Endpoint))
            {
                Endpoint = string.IsNullOrWhiteSpace(endpointFallback)
                    ? DefaultVolcEngineImageEndpoint
                    : endpointFallback;
            }

            if (string.IsNullOrWhiteSpace(Model))
            {
                Model = string.IsNullOrWhiteSpace(modelFallback)
                    ? DefaultVolcEngineImageModel
                    : modelFallback;
            }

            if (string.IsNullOrWhiteSpace(DefaultSize))
            {
                DefaultSize = DefaultImageSize;
            }
        }

        public bool IsConfigured()
        {
            return IsEnabled &&
                !string.IsNullOrWhiteSpace(Endpoint) &&
                !string.IsNullOrWhiteSpace(ApiKey) &&
                !string.IsNullOrWhiteSpace(Model);
        }

        public static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!char.IsControl(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Trim();
        }

        public static string NormalizeImageSize(string rawSize, string fallback)
        {
            string normalizedFallback = NormalizeText(fallback);
            normalizedFallback = NormalizeSizeToken(normalizedFallback);
            normalizedFallback = ResolveSizeAlias(normalizedFallback);
            if (!IsValidImageSize(normalizedFallback))
            {
                normalizedFallback = DefaultImageSize;
            }

            string normalized = NormalizeText(rawSize);
            normalized = NormalizeSizeToken(normalized);
            normalized = ResolveSizeAlias(normalized);
            if (IsNullLikeToken(normalized) || !IsValidImageSize(normalized))
            {
                return normalizedFallback;
            }

            return normalized;
        }

        private static bool IsNullLikeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "undefined", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "nil", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidImageSize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            int sep = value.IndexOf('x');
            if (sep <= 0 || sep >= value.Length - 1)
            {
                return false;
            }

            string w = value.Substring(0, sep);
            string h = value.Substring(sep + 1);
            return int.TryParse(w, out int width)
                && int.TryParse(h, out int height)
                && width > 0
                && height > 0
                && (long)width * height >= MinImagePixels;
        }

        private static string ResolveSizeAlias(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "small":
                    return DefaultImageSize;  // 3686400 >= MinImagePixels
                case "medium":
                    return "3072x1728";
                case "large":
                    return "3840x2160";
                case "portrait":
                    return "1440x2560";
                case "landscape":
                    return DefaultImageSize;
                default:
                    return value;
            }
        }

        private static string NormalizeSizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Trim();
            if ((normalized.StartsWith("\"", StringComparison.Ordinal) && normalized.EndsWith("\"", StringComparison.Ordinal)) ||
                (normalized.StartsWith("'", StringComparison.Ordinal) && normalized.EndsWith("'", StringComparison.Ordinal)))
            {
                normalized = normalized.Substring(1, normalized.Length - 2).Trim();
            }

            return normalized.Replace('X', 'x');
        }
    }

    /// <summary>/// Dependencies: Verse Scribe serialization APIs.
 /// Responsibility: define one editable diplomacy image prompt template entry.
 ///</summary>
    [Serializable]
    public class DiplomacyImagePromptTemplate : IExposable
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Text = string.Empty;
        public string Description = string.Empty;
        public bool Enabled = true;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id", string.Empty);
            Scribe_Values.Look(ref Name, "name", string.Empty);
            Scribe_Values.Look(ref Text, "text", string.Empty);
            Scribe_Values.Look(ref Description, "description", string.Empty);
            Scribe_Values.Look(ref Enabled, "enabled", true);
            if (string.IsNullOrWhiteSpace(Id))
            {
                Id = Guid.NewGuid().ToString("N");
            }
        }

        public DiplomacyImagePromptTemplate Clone()
        {
            return new DiplomacyImagePromptTemplate
            {
                Id = Id,
                Name = Name,
                Text = Text,
                Description = Description,
                Enabled = Enabled
            };
        }
    }

    /// <summary>/// Dependencies: Prompt text constants and template data model.
 /// Responsibility: provide default image template seeds and migration helpers.
 ///</summary>
    public static class DiplomacyImageTemplateDefaults
    {
        public const string DefaultTemplateId = "faction_leader_portrait";
        private static readonly Dictionary<string, string> LegacyTemplateIdAliasMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "diplomacy_scene", DefaultTemplateId },
                { "diplomacyscene", DefaultTemplateId },
                { "diplomacy_image", DefaultTemplateId },
                { "diplomacyimage", DefaultTemplateId },
                { "leader_portrait", DefaultTemplateId }
            };

        public static DiplomacyImagePromptTemplate CreateDefaultTemplate()
        {
            return new DiplomacyImagePromptTemplate
            {
                Id = DefaultTemplateId,
                Name = PromptTextConstants.SendImageDefaultTemplateName,
                Description = PromptTextConstants.SendImageDefaultTemplateDescription,
                Text = PromptTextConstants.SendImageDefaultTemplateText,
                Enabled = true
            };
        }

        public static void EnsureDefaults(List<DiplomacyImagePromptTemplate> templates)
        {
            if (templates == null)
            {
                return;
            }

            templates.RemoveAll(item => item == null);
            if (templates.Count == 0)
            {
                templates.Add(CreateDefaultTemplate());
                return;
            }

            DiplomacyImagePromptTemplate defaultTemplate = templates.Find(item =>
                string.Equals(item.Id, DefaultTemplateId, StringComparison.OrdinalIgnoreCase));
            if (defaultTemplate == null)
            {
                templates.Insert(0, CreateDefaultTemplate());
            }
            else
            {
                if (string.IsNullOrWhiteSpace(defaultTemplate.Name))
                {
                    defaultTemplate.Name = PromptTextConstants.SendImageDefaultTemplateName;
                }
                if (string.IsNullOrWhiteSpace(defaultTemplate.Description))
                {
                    defaultTemplate.Description = PromptTextConstants.SendImageDefaultTemplateDescription;
                }
                if (string.IsNullOrWhiteSpace(defaultTemplate.Text))
                {
                    defaultTemplate.Text = PromptTextConstants.SendImageDefaultTemplateText;
                }
            }

            bool hasEnabled = templates.Any(item => item != null && item.Enabled);
            if (!hasEnabled)
            {
                DiplomacyImagePromptTemplate preferred = templates.FirstOrDefault(item =>
                    item != null &&
                    string.Equals(item.Id, DefaultTemplateId, StringComparison.OrdinalIgnoreCase));
                if (preferred != null)
                {
                    preferred.Enabled = true;
                }
                else
                {
                    DiplomacyImagePromptTemplate first = templates.FirstOrDefault(item => item != null);
                    if (first != null)
                    {
                        first.Enabled = true;
                    }
                }
            }
        }

        public static string ResolveTemplateId(List<DiplomacyImagePromptTemplate> templates, string requestedTemplateId)
        {
            if (templates == null || templates.Count == 0)
            {
                return string.Empty;
            }

            string normalized = NormalizeTemplateId(requestedTemplateId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            DiplomacyImagePromptTemplate exact = templates.FirstOrDefault(item =>
                item != null &&
                !string.IsNullOrWhiteSpace(item.Id) &&
                string.Equals(item.Id, normalized, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact.Id.Trim();
            }

            if (!LegacyTemplateIdAliasMap.TryGetValue(normalized, out string mappedId))
            {
                return string.Empty;
            }

            DiplomacyImagePromptTemplate mapped = templates.FirstOrDefault(item =>
                item != null &&
                !string.IsNullOrWhiteSpace(item.Id) &&
                string.Equals(item.Id, mappedId, StringComparison.OrdinalIgnoreCase));
            return mapped?.Id?.Trim() ?? string.Empty;
        }

        public static string ResolvePreferredEnabledTemplateId(List<DiplomacyImagePromptTemplate> templates)
        {
            if (templates == null || templates.Count == 0)
            {
                return string.Empty;
            }

            DiplomacyImagePromptTemplate preferred = templates.FirstOrDefault(item =>
                item != null &&
                item.Enabled &&
                !string.IsNullOrWhiteSpace(item.Id) &&
                string.Equals(item.Id, DefaultTemplateId, StringComparison.OrdinalIgnoreCase));
            if (preferred != null)
            {
                return preferred.Id.Trim();
            }

            DiplomacyImagePromptTemplate fallback = templates.FirstOrDefault(item =>
                item != null &&
                item.Enabled &&
                !string.IsNullOrWhiteSpace(item.Id));
            return fallback?.Id?.Trim() ?? string.Empty;
        }

        private static string NormalizeTemplateId(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return string.Empty;
            }

            return templateId.Trim();
        }
    }
}

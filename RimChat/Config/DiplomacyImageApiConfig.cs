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
        public const string DefaultImageSize = "2560x1440";
        public const string DefaultVolcEngineImageEndpoint = "https://ark.cn-beijing.volces.com/api/v3/images/generations";
        public const string DefaultVolcEngineImageModel = "doubao-seedream-3-0-t2i-250415";
        public const string ModeSyncUrl = "sync_url";
        public const string ModeSyncPayload = "sync_payload";
        public const string ModeAsyncJob = "async_job";
        public const string SchemaPresetArk = "ark";
        public const string SchemaPresetOpenAI = "openai";
        public const string SchemaPresetComfyUi = "comfyui";
        public const string SchemaPresetCustom = "custom";
        public const string AuthModeBearer = "bearer";
        public const string AuthModeApiKeyHeader = "api_key_header";
        public const string AuthModeQueryKey = "query_key";
        public const string AuthModeNone = "none";
        public const string ProviderPresetArk = "ark_volcengine";
        public const string ProviderPresetOpenAI = "openai_compatible";
        public const string ProviderPresetSiliconFlow = "siliconflow";
        public const string ProviderPresetComfyUiLocal = "comfyui_local";
        public const string ProviderPresetCustom = "custom";

        public bool IsEnabled = false;
        public string Endpoint = string.Empty;
        public string ApiKey = string.Empty;
        public string Model = string.Empty;
        public string DefaultSize = DefaultImageSize;
        public bool DefaultWatermark = false;
        public int TimeoutSeconds = 120;
        public string Mode = ModeSyncUrl;
        public string SchemaPreset = SchemaPresetArk;
        public string AuthMode = AuthModeBearer;
        public string ApiKeyHeaderName = "X-API-Key";
        public string ApiKeyQueryName = "api_key";
        public string ResponseUrlPath = "url,data[0].url,images[0].url,output[0].url";
        public string ResponseB64Path = "b64_json,data[0].b64_json,images[0].b64_json";
        public string AsyncSubmitPath = "/prompt";
        public string AsyncStatusPathTemplate = "/history/{job_id}";
        public string AsyncImageFetchPath = "/view";
        public int PollIntervalMs = 1000;
        public int PollMaxAttempts = 180;
        public string ProviderPreset = ProviderPresetArk;
        public bool ShowAdvanced = false;

        public void ExposeData()
        {
            Scribe_Values.Look(ref IsEnabled, "isEnabled", false);
            Scribe_Values.Look(ref Endpoint, "endpoint", string.Empty);
            Scribe_Values.Look(ref ApiKey, "apiKey", string.Empty);
            Scribe_Values.Look(ref Model, "model", string.Empty);
            Scribe_Values.Look(ref DefaultSize, "defaultSize", DefaultImageSize);
            Scribe_Values.Look(ref DefaultWatermark, "defaultWatermark", false);
            Scribe_Values.Look(ref TimeoutSeconds, "timeoutSeconds", 120);
            Scribe_Values.Look(ref Mode, "mode", ModeSyncUrl);
            Scribe_Values.Look(ref SchemaPreset, "schemaPreset", SchemaPresetArk);
            Scribe_Values.Look(ref AuthMode, "authMode", AuthModeBearer);
            Scribe_Values.Look(ref ApiKeyHeaderName, "apiKeyHeaderName", "X-API-Key");
            Scribe_Values.Look(ref ApiKeyQueryName, "apiKeyQueryName", "api_key");
            Scribe_Values.Look(ref ResponseUrlPath, "responseUrlPath", "url,data[0].url,images[0].url,output[0].url");
            Scribe_Values.Look(ref ResponseB64Path, "responseB64Path", "b64_json,data[0].b64_json,images[0].b64_json");
            Scribe_Values.Look(ref AsyncSubmitPath, "asyncSubmitPath", "/prompt");
            Scribe_Values.Look(ref AsyncStatusPathTemplate, "asyncStatusPathTemplate", "/history/{job_id}");
            Scribe_Values.Look(ref AsyncImageFetchPath, "asyncImageFetchPath", "/view");
            Scribe_Values.Look(ref PollIntervalMs, "pollIntervalMs", 1000);
            Scribe_Values.Look(ref PollMaxAttempts, "pollMaxAttempts", 180);
            Scribe_Values.Look(ref ProviderPreset, "providerPreset", ProviderPresetArk);
            Scribe_Values.Look(ref ShowAdvanced, "showAdvanced", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars &&
                PollMaxAttempts == 60 &&
                (string.Equals(SchemaPreset, SchemaPresetComfyUi, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(ProviderPreset, ProviderPresetComfyUiLocal, StringComparison.OrdinalIgnoreCase)))
            {
                // Migrate old default async polling window for ComfyUI profiles.
                PollMaxAttempts = 180;
            }
            Normalize();
        }

        public void Normalize()
        {
            Endpoint = NormalizeText(Endpoint);
            ApiKey = NormalizeText(ApiKey);
            Model = NormalizeText(Model);
            DefaultSize = NormalizeImageSize(DefaultSize, DefaultImageSize);
            Mode = NormalizeMode(Mode);
            SchemaPreset = NormalizeSchemaPreset(SchemaPreset, Endpoint);
            AuthMode = NormalizeAuthMode(AuthMode, SchemaPreset);
            ApiKeyHeaderName = NormalizeText(ApiKeyHeaderName);
            ApiKeyQueryName = NormalizeText(ApiKeyQueryName);
            ResponseUrlPath = NormalizePathSpec(ResponseUrlPath, "url,data[0].url,images[0].url,output[0].url");
            ResponseB64Path = NormalizePathSpec(ResponseB64Path, "b64_json,data[0].b64_json,images[0].b64_json");
            AsyncSubmitPath = NormalizeText(AsyncSubmitPath);
            AsyncStatusPathTemplate = NormalizeText(AsyncStatusPathTemplate);
            AsyncImageFetchPath = NormalizeText(AsyncImageFetchPath);
            ProviderPreset = NormalizeProviderPreset(ProviderPreset);

            TimeoutSeconds = Math.Max(10, Math.Min(300, TimeoutSeconds));
            PollIntervalMs = Math.Max(250, Math.Min(10000, PollIntervalMs));
            PollMaxAttempts = Math.Max(1, Math.Min(600, PollMaxAttempts));
            ApplyProviderPresetDefaults();
            ApplyPresetDefaults();
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
                (string.Equals(AuthMode, AuthModeNone, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(ApiKey)) &&
                !string.IsNullOrWhiteSpace(Model);
        }

        public static string NormalizeMode(string mode)
        {
            string normalized = NormalizeText(mode).ToLowerInvariant();
            if (normalized == ModeSyncPayload || normalized == ModeAsyncJob)
            {
                return normalized;
            }
            return ModeSyncUrl;
        }

        public static string NormalizeSchemaPreset(string preset, string endpoint)
        {
            string normalized = NormalizeText(preset).ToLowerInvariant();
            if (normalized == SchemaPresetArk || normalized == SchemaPresetOpenAI || normalized == SchemaPresetComfyUi || normalized == SchemaPresetCustom)
            {
                return normalized;
            }

            string endpointLower = NormalizeText(endpoint).ToLowerInvariant();
            if (endpointLower.Contains("/prompt") || endpointLower.Contains(":8188"))
            {
                return SchemaPresetComfyUi;
            }

            return SchemaPresetArk;
        }

        public static string NormalizeAuthMode(string mode, string schemaPreset)
        {
            string normalized = NormalizeText(mode).ToLowerInvariant();
            if (normalized == AuthModeBearer || normalized == AuthModeApiKeyHeader || normalized == AuthModeQueryKey || normalized == AuthModeNone)
            {
                return normalized;
            }

            return string.Equals(schemaPreset, SchemaPresetComfyUi, StringComparison.OrdinalIgnoreCase)
                ? AuthModeNone
                : AuthModeBearer;
        }

        public static string NormalizeProviderPreset(string preset)
        {
            string normalized = NormalizeText(preset).ToLowerInvariant();
            if (normalized == ProviderPresetArk
                || normalized == ProviderPresetOpenAI
                || normalized == ProviderPresetSiliconFlow
                || normalized == ProviderPresetComfyUiLocal
                || normalized == ProviderPresetCustom)
            {
                return normalized;
            }

            return ProviderPresetArk;
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

        private static string NormalizePathSpec(string value, string fallback)
        {
            string normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return fallback;
            }

            string[] parts = normalized.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cleaned = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string candidate = NormalizeText(parts[i]);
                if (string.IsNullOrWhiteSpace(candidate) || unique.Contains(candidate))
                {
                    continue;
                }

                unique.Add(candidate);
                cleaned.Add(candidate);
            }

            return cleaned.Count == 0 ? fallback : string.Join(",", cleaned);
        }

        private void ApplyPresetDefaults()
        {
            if (string.Equals(SchemaPreset, SchemaPresetComfyUi, StringComparison.OrdinalIgnoreCase))
            {
                Mode = ModeAsyncJob;
                if (string.IsNullOrWhiteSpace(AsyncSubmitPath))
                {
                    AsyncSubmitPath = "/prompt";
                }
                if (string.IsNullOrWhiteSpace(AsyncStatusPathTemplate))
                {
                    AsyncStatusPathTemplate = "/history/{job_id}";
                }
                if (string.IsNullOrWhiteSpace(AsyncImageFetchPath))
                {
                    AsyncImageFetchPath = "/view";
                }
                if (string.IsNullOrWhiteSpace(ResponseUrlPath))
                {
                    ResponseUrlPath = "url,data[0].url,images[0].url,output[0].url";
                }
            }
            else if (string.Equals(SchemaPreset, SchemaPresetOpenAI, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(Mode, ModeAsyncJob, StringComparison.OrdinalIgnoreCase))
                {
                    Mode = ModeSyncPayload;
                }
                if (string.IsNullOrWhiteSpace(ResponseUrlPath))
                {
                    ResponseUrlPath = "url,data[0].url,images[0].url,output[0].url";
                }
                if (string.IsNullOrWhiteSpace(ResponseB64Path))
                {
                    ResponseB64Path = "b64_json,data[0].b64_json,images[0].b64_json";
                }
            }
        }

        public void ApplyProviderPresetDefaults()
        {
            string preset = NormalizeProviderPreset(ProviderPreset);
            if (string.Equals(preset, ProviderPresetCustom, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(preset, ProviderPresetComfyUiLocal, StringComparison.OrdinalIgnoreCase))
            {
                SchemaPreset = SchemaPresetComfyUi;
                Mode = ModeAsyncJob;
                AuthMode = AuthModeNone;
                PollMaxAttempts = Math.Max(PollMaxAttempts, 180);
                if (string.IsNullOrWhiteSpace(Endpoint))
                {
                    Endpoint = "http://127.0.0.1:8188/prompt";
                }
                return;
            }

            bool useOpenAiSchema = string.Equals(preset, ProviderPresetOpenAI, StringComparison.OrdinalIgnoreCase)
                || string.Equals(preset, ProviderPresetSiliconFlow, StringComparison.OrdinalIgnoreCase);
            SchemaPreset = useOpenAiSchema ? SchemaPresetOpenAI : SchemaPresetArk;
            Mode = string.Equals(preset, ProviderPresetSiliconFlow, StringComparison.OrdinalIgnoreCase)
                ? ModeSyncUrl
                : (string.Equals(preset, ProviderPresetOpenAI, StringComparison.OrdinalIgnoreCase)
                    ? ModeSyncPayload
                    : ModeSyncUrl);
            AuthMode = AuthModeBearer;

            if (string.Equals(preset, ProviderPresetSiliconFlow, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(Endpoint))
                {
                    Endpoint = "https://api.siliconflow.cn/v1/images/generations";
                }
                if (string.IsNullOrWhiteSpace(Model))
                {
                    Model = "black-forest-labs/FLUX.1-schnell";
                }
                return;
            }

            if (string.Equals(preset, ProviderPresetOpenAI, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(Endpoint))
                {
                    Endpoint = "https://api.openai.com/v1/images/generations";
                }
                if (string.IsNullOrWhiteSpace(Model))
                {
                    Model = "gpt-image-1";
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(Endpoint))
            {
                Endpoint = DefaultVolcEngineImageEndpoint;
            }
            if (string.IsNullOrWhiteSpace(Model))
            {
                Model = DefaultVolcEngineImageModel;
            }
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
                && height > 0;
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
                    return DefaultImageSize;
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

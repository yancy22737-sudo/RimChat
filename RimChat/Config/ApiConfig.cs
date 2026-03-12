using System;
using System.Text;
using Verse;
using RimChat.AI;

namespace RimChat.Config
{
    public enum CustomUrlMode
    {
        BaseUrl = 0,
        FullEndpoint = 1
    }

    public struct CustomUrlRuntimeResolution
    {
        public string ChatEndpoint;
        public string ModelsEndpoint;
        public bool HasSuspiciousBasePath;
        public bool WasSiliconFlowHostMapped;
    }

    public class ApiConfig : IExposable
    {
        public const string DeepSeekOfficialBaseUrl = "https://api.deepseek.com/v1";

        public bool IsEnabled = true;
        public AIProvider Provider = AIProvider.OpenAI;
        public string ApiKey = "";
        public string SelectedModel = "";
        public string CustomModelName = "";
        public string BaseUrl = "";
        public CustomUrlMode CustomUrlMode = CustomUrlMode.BaseUrl;
        private bool customUrlModeInitialized = false;

        public void ExposeData()
        {
            Scribe_Values.Look(ref IsEnabled, "isEnabled", true);
            Scribe_Values.Look(ref Provider, "provider", AIProvider.OpenAI);
            Scribe_Values.Look(ref ApiKey, "apiKey", "");
            Scribe_Values.Look(ref SelectedModel, "selectedModel", "");
            Scribe_Values.Look(ref CustomModelName, "customModelName", "");
            Scribe_Values.Look(ref BaseUrl, "baseUrl", "");
            Scribe_Values.Look(ref CustomUrlMode, "customUrlMode", CustomUrlMode.BaseUrl);
            Scribe_Values.Look(ref customUrlModeInitialized, "customUrlModeInitialized", false);
            BaseUrl = NormalizeUrl(BaseUrl);
            if (Scribe.mode == LoadSaveMode.LoadingVars && !customUrlModeInitialized)
            {
                CustomUrlMode = InferCustomUrlMode(BaseUrl);
                customUrlModeInitialized = true;
            }
        }

        public bool IsValid()
        {
            if (!IsEnabled) return false;
            return !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SelectedModel);
        }

        public string GetEffectiveModelName()
        {
            if (SelectedModel == "Custom")
                return CustomModelName;
            return SelectedModel;
        }

        public string GetEffectiveEndpoint()
        {
            if (Provider == AIProvider.Custom && TryResolveCustomRuntimeEndpoints(out CustomUrlRuntimeResolution resolved))
            {
                return resolved.ChatEndpoint;
            }

            return NormalizeUrl(Provider.GetEndpointUrl());
        }

        public bool TryResolveCustomRuntimeEndpoints(out CustomUrlRuntimeResolution resolved)
        {
            resolved = default(CustomUrlRuntimeResolution);
            if (Provider != AIProvider.Custom)
            {
                return false;
            }

            string normalized = NormalizeUrl(BaseUrl);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            resolved = ResolveCustomRuntimeEndpoints(normalized, CustomUrlMode);
            return !string.IsNullOrEmpty(resolved.ChatEndpoint);
        }

        public void MarkCustomUrlModeInitialized()
        {
            customUrlModeInitialized = true;
        }

        public static string NormalizeUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (!char.IsWhiteSpace(current))
                {
                    builder.Append(current);
                }
            }

            return builder.ToString().Trim();
        }

        public static string ToModelsEndpoint(string value)
        {
            string normalized = NormalizeUrl(value);
            if (string.IsNullOrEmpty(normalized))
            {
                return string.Empty;
            }

            if (TryBuildModelsEndpointFromUri(normalized, out string endpoint))
            {
                return endpoint;
            }

            string trimmed = normalized.TrimEnd('/');
            if (string.IsNullOrEmpty(trimmed))
            {
                return string.Empty;
            }

            if (trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring(0, trimmed.Length - "/chat/completions".Length) + "/models";
            }

            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed + "/models";
            }

            return trimmed + "/v1/models";
        }

        public static string EnsureChatCompletionsEndpoint(string baseUrl)
        {
            string normalized = NormalizeUrl(baseUrl);
            if (string.IsNullOrEmpty(normalized))
            {
                return string.Empty;
            }

            if (TryBuildChatCompletionsEndpointFromUri(normalized, out string endpoint))
            {
                return endpoint;
            }

            string trimmed = normalized.TrimEnd('/');
            if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return trimmed + "/v1/chat/completions";
        }

        private static CustomUrlRuntimeResolution ResolveCustomRuntimeEndpoints(string sourceUrl, CustomUrlMode mode)
        {
            string mappedUrl = MapSiliconFlowCloudHost(sourceUrl, out bool mapped);
            bool suspicious = false;
            string chatEndpoint;
            if (mode == CustomUrlMode.BaseUrl)
            {
                chatEndpoint = ResolveBaseModeChatEndpoint(mappedUrl, out suspicious);
            }
            else
            {
                chatEndpoint = NormalizeUrl(mappedUrl);
            }

            return new CustomUrlRuntimeResolution
            {
                ChatEndpoint = chatEndpoint,
                ModelsEndpoint = ToModelsEndpoint(chatEndpoint),
                HasSuspiciousBasePath = suspicious,
                WasSiliconFlowHostMapped = mapped
            };
        }

        private static string MapSiliconFlowCloudHost(string url, out bool mapped)
        {
            mapped = false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || !IsSiliconFlowCloudHost(uri.Host))
            {
                return url;
            }

            var builder = new UriBuilder(uri)
            {
                Host = "api.siliconflow.cn"
            };
            mapped = !string.Equals(uri.Host, builder.Host, StringComparison.OrdinalIgnoreCase);
            return NormalizeUrl(builder.Uri.AbsoluteUri);
        }

        private static bool IsSiliconFlowCloudHost(string host)
        {
            return !string.IsNullOrWhiteSpace(host) &&
                   host.StartsWith("cloud.siliconflow.", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveBaseModeChatEndpoint(string url, out bool suspicious)
        {
            suspicious = false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return ResolveBaseModeChatEndpointFallback(url, out suspicious);
            }

            string path = uri.AbsolutePath ?? string.Empty;
            if (ContainsChatCompletionsPath(path))
            {
                return NormalizeUrl(url);
            }

            if (string.IsNullOrEmpty(path) || path == "/" || IsV1RootPath(path))
            {
                var builder = new UriBuilder(uri) { Path = "/v1/chat/completions" };
                return NormalizeUrl(builder.Uri.AbsoluteUri);
            }

            suspicious = true;
            return NormalizeUrl(url);
        }

        private static string ResolveBaseModeChatEndpointFallback(string url, out bool suspicious)
        {
            suspicious = false;
            string normalized = NormalizeUrl(url);
            if (string.IsNullOrEmpty(normalized) || ContainsChatCompletionsPath(normalized))
            {
                return normalized;
            }

            if (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return normalized + "/chat/completions";
            }

            int schemeIndex = normalized.IndexOf("://", StringComparison.Ordinal);
            int pathIndex = schemeIndex >= 0 ? normalized.IndexOf('/', schemeIndex + 3) : -1;
            if (schemeIndex >= 0 && pathIndex < 0)
            {
                return normalized + "/v1/chat/completions";
            }

            suspicious = true;
            return normalized;
        }

        private static bool ContainsChatCompletionsPath(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf("/chat/completions", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsV1RootPath(string path)
        {
            return string.Equals(path, "/v1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(path, "/v1/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryBuildModelsEndpointFromUri(string normalized, out string endpoint)
        {
            endpoint = string.Empty;
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string path = uri.AbsolutePath ?? string.Empty;
            if (string.IsNullOrEmpty(path) || path == "/" || IsV1RootPath(path))
            {
                var rootBuilder = new UriBuilder(uri) { Path = "/v1/models" };
                endpoint = NormalizeUrl(rootBuilder.Uri.AbsoluteUri);
                return true;
            }

            if (path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = NormalizeUrl(uri.AbsoluteUri);
                return true;
            }

            if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                string basePath = path.Substring(0, path.Length - "/chat/completions".Length);
                var builder = new UriBuilder(uri) { Path = $"{basePath}/models" };
                endpoint = NormalizeUrl(builder.Uri.AbsoluteUri);
                return true;
            }

            var fallbackBuilder = new UriBuilder(uri) { Path = path.TrimEnd('/') + "/v1/models" };
            endpoint = NormalizeUrl(fallbackBuilder.Uri.AbsoluteUri);
            return true;
        }

        private static bool TryBuildChatCompletionsEndpointFromUri(string normalized, out string endpoint)
        {
            endpoint = string.Empty;
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string path = uri.AbsolutePath ?? string.Empty;
            if (ContainsChatCompletionsPath(path))
            {
                endpoint = NormalizeUrl(uri.AbsoluteUri);
                return true;
            }

            if (string.IsNullOrEmpty(path) || path == "/")
            {
                var rootBuilder = new UriBuilder(uri) { Path = "/v1/chat/completions" };
                endpoint = NormalizeUrl(rootBuilder.Uri.AbsoluteUri);
                return true;
            }

            if (IsV1RootPath(path))
            {
                var v1Builder = new UriBuilder(uri) { Path = "/v1/chat/completions" };
                endpoint = NormalizeUrl(v1Builder.Uri.AbsoluteUri);
                return true;
            }

            var fallbackBuilder = new UriBuilder(uri) { Path = path.TrimEnd('/') + "/v1/chat/completions" };
            endpoint = NormalizeUrl(fallbackBuilder.Uri.AbsoluteUri);
            return true;
        }

        private static CustomUrlMode InferCustomUrlMode(string baseUrl)
        {
            return ContainsChatCompletionsPath(NormalizeUrl(baseUrl))
                ? CustomUrlMode.FullEndpoint
                : CustomUrlMode.BaseUrl;
        }
    }
}

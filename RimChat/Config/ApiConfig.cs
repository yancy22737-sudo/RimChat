using System;
using System.Text;
using Verse;
using RimChat.AI;

namespace RimChat.Config
{
    public class ApiConfig : IExposable
    {
        public bool IsEnabled = true;
        public AIProvider Provider = AIProvider.OpenAI;
        public string ApiKey = "";
        public string SelectedModel = "";
        public string CustomModelName = "";
        public string BaseUrl = "";

        public void ExposeData()
        {
            Scribe_Values.Look(ref IsEnabled, "isEnabled", true);
            Scribe_Values.Look(ref Provider, "provider", AIProvider.OpenAI);
            Scribe_Values.Look(ref ApiKey, "apiKey", "");
            Scribe_Values.Look(ref SelectedModel, "selectedModel", "");
            Scribe_Values.Look(ref CustomModelName, "customModelName", "");
            Scribe_Values.Look(ref BaseUrl, "baseUrl", "");
            BaseUrl = NormalizeUrl(BaseUrl);
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
            if (Provider == AIProvider.Custom && !string.IsNullOrEmpty(BaseUrl))
                return NormalizeUrl(BaseUrl);
            return NormalizeUrl(Provider.GetEndpointUrl());
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
            return NormalizeUrl(value).Replace("/chat/completions", "/models");
        }

        public static string EnsureChatCompletionsEndpoint(string baseUrl)
        {
            string normalized = NormalizeUrl(baseUrl).TrimEnd('/');
            if (string.IsNullOrEmpty(normalized))
            {
                return string.Empty;
            }

            if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return normalized + "/v1/chat/completions";
        }
    }
}

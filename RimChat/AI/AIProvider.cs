using System.Collections.Generic;

namespace RimChat.AI
{
    public enum AIProvider
    {
        OpenAI,
        Google,
        DeepSeek,
        OpenRouter,
        GLM,
        Kimi,
        Mistral,
        Grok,
        Custom,
        None
    }

    public struct ProviderDef
    {
        public string Label;
        public string EndpointUrl;
        public string ListModelsUrl;
    }

    public static class AIProviderRegistry
    {
        public static readonly Dictionary<AIProvider, ProviderDef> Defs = new()
        {
            {
                AIProvider.OpenAI, new ProviderDef
                {
                    Label = "OpenAI",
                    EndpointUrl = "https:// Api.openai.com/v1/chat/completions",
                    ListModelsUrl = "https:// Api.openai.com/v1/models"
                }
            },
            {
                AIProvider.Google, new ProviderDef
                {
                    Label = "Google",
                    EndpointUrl = "https:// Generativelanguage.googleapis.com/v1beta/openai/chat/completions",
                    ListModelsUrl = "https:// Generativelanguage.googleapis.com/v1beta/models"
                }
            },
            {
                AIProvider.DeepSeek, new ProviderDef
                {
                    Label = "DeepSeek",
                    EndpointUrl = "https:// Api.deepseek.com/v1/chat/completions",
                    ListModelsUrl = "https:// Api.deepseek.com/v1/models"
                }
            },
            {
                AIProvider.OpenRouter, new ProviderDef
                {
                    Label = "OpenRouter",
                    EndpointUrl = "https:// Openrouter.ai/api/v1/chat/completions",
                    ListModelsUrl = "https:// Openrouter.ai/api/v1/models"
                }
            },
            {
                AIProvider.GLM, new ProviderDef
                {
                    Label = "GLM",
                    EndpointUrl = "https:// Open.bigmodel.cn/api/paas/v4/chat/completions",
                    ListModelsUrl = "https:// Open.bigmodel.cn/api/paas/v4/models"
                }
            },
            {
                AIProvider.Kimi, new ProviderDef
                {
                    Label = "Kimi",
                    EndpointUrl = "https:// Api.moonshot.cn/v1/chat/completions",
                    ListModelsUrl = "https:// Api.moonshot.cn/v1/models"
                }
            },
            {
                AIProvider.Mistral, new ProviderDef
                {
                    Label = "Mistral",
                    EndpointUrl = "https:// Api.mistral.ai/v1/chat/completions",
                    ListModelsUrl = "https:// Api.mistral.ai/v1/models"
                }
            },
            {
                AIProvider.Grok, new ProviderDef
                {
                    Label = "Grok",
                    EndpointUrl = "https:// Api.x.ai/v1/chat/completions",
                    ListModelsUrl = "https:// Api.x.ai/v1/models"
                }
            },
            {
                AIProvider.Custom, new ProviderDef
                {
                    Label = "Custom",
                    EndpointUrl = "",
                    ListModelsUrl = ""
                }
            }
        };

        public static string GetLabel(this AIProvider p)
        {
            if (Defs.TryGetValue(p, out var def) && !string.IsNullOrEmpty(def.Label))
            {
                return def.Label;
            }
            return p.ToString();
        }

        public static string GetEndpointUrl(this AIProvider p)
        {
            return Defs.TryGetValue(p, out var def) ? def.EndpointUrl : "";
        }

        public static string GetListModelsUrl(this AIProvider p)
        {
            return Defs.TryGetValue(p, out var def) ? def.ListModelsUrl : "";
        }
    }
}

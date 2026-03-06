using System.Collections.Generic;

namespace RimDiplomacy.AI
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
                    EndpointUrl = "https://api.openai.com/v1/chat/completions",
                    ListModelsUrl = "https://api.openai.com/v1/models"
                }
            },
            {
                AIProvider.Google, new ProviderDef
                {
                    Label = "Google",
                    EndpointUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
                    ListModelsUrl = "https://generativelanguage.googleapis.com/v1beta/models"
                }
            },
            {
                AIProvider.DeepSeek, new ProviderDef
                {
                    Label = "DeepSeek",
                    EndpointUrl = "https://api.deepseek.com/v1/chat/completions",
                    ListModelsUrl = "https://api.deepseek.com/v1/models"
                }
            },
            {
                AIProvider.OpenRouter, new ProviderDef
                {
                    Label = "OpenRouter",
                    EndpointUrl = "https://openrouter.ai/api/v1/chat/completions",
                    ListModelsUrl = "https://openrouter.ai/api/v1/models"
                }
            },
            {
                AIProvider.GLM, new ProviderDef
                {
                    Label = "GLM",
                    EndpointUrl = "https://open.bigmodel.cn/api/paas/v4/chat/completions",
                    ListModelsUrl = "https://open.bigmodel.cn/api/paas/v4/models"
                }
            },
            {
                AIProvider.Kimi, new ProviderDef
                {
                    Label = "Kimi",
                    EndpointUrl = "https://api.moonshot.cn/v1/chat/completions",
                    ListModelsUrl = "https://api.moonshot.cn/v1/models"
                }
            },
            {
                AIProvider.Mistral, new ProviderDef
                {
                    Label = "Mistral",
                    EndpointUrl = "https://api.mistral.ai/v1/chat/completions",
                    ListModelsUrl = "https://api.mistral.ai/v1/models"
                }
            },
            {
                AIProvider.Grok, new ProviderDef
                {
                    Label = "Grok",
                    EndpointUrl = "https://api.x.ai/v1/chat/completions",
                    ListModelsUrl = "https://api.x.ai/v1/models"
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

using System.Collections.Generic;
using System.Text;

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
        Player2,
        Custom,
        None
    }

    public struct ProviderDef
    {
        public string Label;
        public string EndpointUrl;
        public string ListModelsUrl;
        public Dictionary<string, string> ExtraHeaders;
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
                    ListModelsUrl = "https://api.deepseek.com/models"
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
                AIProvider.Player2, new ProviderDef
                {
                    Label = "Player2",
                    EndpointUrl = "https://api.player2.game/v1/chat/completions",
                    ListModelsUrl = "",
                    ExtraHeaders = new Dictionary<string, string>
                    {
                        { "player2-game-key", "019cdde4-f361-7aaf-b521-c39981d9c8ad" }
                    }
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
            string url = Defs.TryGetValue(p, out var def) ? def.EndpointUrl : "";
            return NormalizeProviderUrl(url);
        }

        public static string GetListModelsUrl(this AIProvider p)
        {
            string url = Defs.TryGetValue(p, out var def) ? def.ListModelsUrl : "";
            return NormalizeProviderUrl(url);
        }

        public static bool SupportsModelListing(this AIProvider p)
        {
            if (!Defs.TryGetValue(p, out var def)) return false;
            return !string.IsNullOrWhiteSpace(def.ListModelsUrl);
        }

        public static Dictionary<string, string> GetExtraHeaders(this AIProvider p)
        {
            if (Defs.TryGetValue(p, out var def) && def.ExtraHeaders != null)
            {
                return def.ExtraHeaders;
            }
            return null;
        }

        public static bool RequiresApiKey(this AIProvider p)
        {
            return p != AIProvider.None;
        }

        private static string NormalizeProviderUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(url.Length);
            for (int i = 0; i < url.Length; i++)
            {
                char current = url[i];
                if (!char.IsWhiteSpace(current))
                {
                    builder.Append(current);
                }
            }

            return builder.ToString().Trim();
        }
    }
}

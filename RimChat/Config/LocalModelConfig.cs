using RimChat.AI;
using Verse;

namespace RimChat.Config
{
    public class LocalModelConfig : IExposable
    {
        private const string Player2LocalHost = "localhost:4315";
        private const string Player2LocalHostAlt = "127.0.0.1:4315";

        public string BaseUrl = "http://localhost:11434";
        public string ModelName = "";

        public void ExposeData()
        {
            Scribe_Values.Look(ref BaseUrl, "baseUrl", "http://localhost:11434");
            Scribe_Values.Look(ref ModelName, "modelName", "");
            BaseUrl = ApiConfig.NormalizeUrl(BaseUrl);
        }

        public bool IsValid()
        {
            if (!string.IsNullOrWhiteSpace(GetNormalizedBaseUrl()))
            {
                // Player2 local does not require a model name
                return IsPlayer2Local() || !string.IsNullOrWhiteSpace(ModelName);
            }
            return false;
        }

        public string GetNormalizedBaseUrl()
        {
            return ApiConfig.NormalizeUrl(BaseUrl);
        }

        /// <summary>
        /// Detect whether BaseUrl points to a local Player2 app (localhost:4315).
        /// </summary>
        public bool IsPlayer2Local()
        {
            string url = GetNormalizedBaseUrl()?.ToLowerInvariant() ?? string.Empty;
            return url.Contains(Player2LocalHost) || url.Contains(Player2LocalHostAlt);
        }
    }
}

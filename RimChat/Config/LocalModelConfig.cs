using Verse;

namespace RimChat.Config
{
    public class LocalModelConfig : IExposable
    {
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
            return !string.IsNullOrWhiteSpace(GetNormalizedBaseUrl()) && !string.IsNullOrWhiteSpace(ModelName);
        }

        public string GetNormalizedBaseUrl()
        {
            return ApiConfig.NormalizeUrl(BaseUrl);
        }
    }
}

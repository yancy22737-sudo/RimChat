using Verse;
using RimDiplomacy.AI;

namespace RimDiplomacy.Config
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
                return BaseUrl;
            return Provider.GetEndpointUrl();
        }
    }
}

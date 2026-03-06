using Verse;

namespace RimDiplomacy.Config
{
    public class LocalModelConfig : IExposable
    {
        public string BaseUrl = "http://localhost:11434";
        public string ModelName = "";

        public void ExposeData()
        {
            Scribe_Values.Look(ref BaseUrl, "baseUrl", "http://localhost:11434");
            Scribe_Values.Look(ref ModelName, "modelName", "");
        }

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ModelName);
        }
    }
}

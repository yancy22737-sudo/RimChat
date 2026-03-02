using System;
using System.Collections.Generic;
using Verse;

namespace RimDiplomacy.Config
{
    public class APIPreset : IExposable
    {
        public string PresetName = "";
        public string ApiEndpoint = "";
        public string ApiKey = "";
        public string ModelName = "";

        public void ExposeData()
        {
            Scribe_Values.Look(ref PresetName, "PresetName", "");
            Scribe_Values.Look(ref ApiEndpoint, "ApiEndpoint", "");
            Scribe_Values.Look(ref ApiKey, "ApiKey", "");
            Scribe_Values.Look(ref ModelName, "ModelName", "");
        }

        public APIPreset Clone()
        {
            return new APIPreset
            {
                PresetName = this.PresetName,
                ApiEndpoint = this.ApiEndpoint,
                ApiKey = this.ApiKey,
                ModelName = this.ModelName
            };
        }
    }
}

using System;
using RimWorld;
using Verse;

namespace RimChat.Util
{
    public static class DLCCompatibility
    {
        public static bool IsIdeologyActive
        {
            get
            {
                try
                {
                    Type factionType = typeof(Faction);
                    var ideosField = factionType.GetField("ideos");
                    return ideosField != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool IsRoyaltyActive
        {
            get
            {
                try
                {
                    Type factionDefType = typeof(FactionDef);
                    var royalTitleTagsField = factionDefType.GetField("royalTitleTags");
                    return royalTitleTagsField != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool IsBiotechActive
        {
            get
            {
                try
                {
                    Type pawnType = typeof(Pawn);
                    var genesField = pawnType.GetField("genes");
                    return genesField != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool IsAnomalyActive
        {
            get
            {
                try
                {
                    Type thingDefType = typeof(ThingDef);
                    var anomalyEntityField = thingDefType.GetField("anomalyEntity");
                    return anomalyEntityField != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void LogDLCStatus()
        {
            string dlcList = "None";
            bool hasRoyalty = IsRoyaltyActive;
            bool hasIdeology = IsIdeologyActive;
            bool hasBiotech = IsBiotechActive;
            bool hasAnomaly = IsAnomalyActive;

            if (hasRoyalty || hasIdeology || hasBiotech || hasAnomaly)
            {
                dlcList = "";
                if (hasRoyalty) dlcList = dlcList + "Royalty, ";
                if (hasIdeology) dlcList = dlcList + "Ideology, ";
                if (hasBiotech) dlcList = dlcList + "Biotech, ";
                if (hasAnomaly) dlcList = dlcList + "Anomaly, ";
                dlcList = dlcList.Substring(0, dlcList.Length - 2);
            }

            Log.Message("[RimChat] Active DLCs: " + dlcList);
        }
    }
}

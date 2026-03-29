using RimWorld;
using Verse;

namespace RimChat.Core
{
    public static class MapUtility
    {
        public static bool IsOrbitalBaseMap(Map map)
        {
            if (map?.Parent == null)
            {
                return false;
            }

            string defName = map.Parent.def?.defName ?? string.Empty;
            return defName.Contains("OrbitalBase") ||
                   defName.Contains("SpaceSite") ||
                   defName.Contains("OrbitalTrade") ||
                   defName.Contains("Orbital");
        }
    }
}

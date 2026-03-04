using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace RimDiplomacy.Relation
{
    public class RPGRelationValues : IExposable
    {
        public float Favorability;
        public float Trust;
        public float Fear;
        public float Respect;
        public float Dependency;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Favorability, "favorability", 0f);
            Scribe_Values.Look(ref Trust, "trust", 0f);
            Scribe_Values.Look(ref Fear, "fear", 0f);
            Scribe_Values.Look(ref Respect, "respect", 0f);
            Scribe_Values.Look(ref Dependency, "dependency", 0f);
        }
        
        public string GetSummary()
        {
            return $"好感:{Favorability:F1} 信任:{Trust:F1} 恐惧:{Fear:F1} 尊重:{Respect:F1} 依赖:{Dependency:F1}";
        }
        
        public void UpdateFromLLM(float favDelta, float trustDelta, float fearDelta, float respectDelta, float depDelta)
        {
            Favorability = ClampValue(Favorability + favDelta);
            Trust = ClampValue(Trust + trustDelta);
            Fear = ClampValue(Fear + fearDelta);
            Respect = ClampValue(Respect + respectDelta);
            Dependency = ClampValue(Dependency + depDelta);
        }

        private float ClampValue(float value)
        {
            return Math.Max(-100f, Math.Min(100f, value));
        }
    }
}

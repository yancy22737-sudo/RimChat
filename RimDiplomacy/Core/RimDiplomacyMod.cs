using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimDiplomacy
{
    public class RimDiplomacyMod : Mod
    {
        public static RimDiplomacySettings Settings;
        public static RimDiplomacyMod Instance;
        public RimDiplomacySettings InstanceSettings => Settings;

        public RimDiplomacyMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<RimDiplomacySettings>();
            
            // Apply Harmony patches
            var harmony = new Harmony("RimDiplomacy.AIDriven");
            harmony.PatchAll();
            
            DLCCompatibility.LogDLCStatus();
            Log.Message("[RimDiplomacy] Mod initialized successfully.");
        }

        public override string SettingsCategory()
        {
            return "RimDiplomacy";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoWindowContents(inRect);
        }
    }
}

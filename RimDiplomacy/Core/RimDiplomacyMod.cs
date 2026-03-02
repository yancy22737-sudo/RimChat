using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using RimDiplomacy.Config;
using RimDiplomacy.Util;

namespace RimDiplomacy.Core
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

            // Initialize FactionPromptManager
            FactionPromptManager.Instance.Initialize();

            // Apply Harmony patches
            var harmony = new Harmony("RimDiplomacy.AIDriven");
            harmony.PatchAll();

            DLCCompatibility.LogDLCStatus();
            Log.Message("[RimDiplomacy] Mod initialized successfully.");
        }

        public override string SettingsCategory()
        {
            return "RimDiplomacy_SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoWindowContents(inRect);
        }

        /// <summary>
        /// 获取模组设置文件夹路径
        /// </summary>
        public string GetSettingsFolderPath()
        {
            string path = Path.Combine(GenFilePaths.ConfigFolderPath, "RimDiplomacy");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }
    }
}

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
using RimChat.Comp;
using RimChat.Config;
using RimChat.Prompting;
using RimChat.Util;

namespace RimChat.Core
{
    public class RimChatMod : Mod
    {
        public static RimChatSettings Settings;
        public static RimChatMod Instance;
        public RimChatSettings InstanceSettings => Settings;

        public RimChatMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<RimChatSettings>();
            Settings?.EnsureRpgPromptTextsLoaded();
            PromptRuntimeVariableBridge.TryCleanupLegacyRimChatVariables(force: true);

            // Initialize FactionPromptManager
            FactionPromptManager.Instance.Initialize();

            // Apply Harmony patches
            var harmony = new Harmony("RimChat.AIDriven");
            harmony.PatchAll();
            
            // Initialize custom patches that require dynamic method lookup
            RimChat.Patches.CommsConsolePatch.Initialize(harmony);
            RimChat.Patches.QuestGenPatch.Initialize(harmony);
            LongEventHandler.ExecuteWhenFinished(PawnDialogueCompDefInjector.EnsureInjected);

            DLCCompatibility.LogDLCStatus();
            Log.Message("[RimChat] Mod initialized successfully.");
        }

        public override string SettingsCategory()
        {
            return "RimChat_SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoWindowContents(inRect);
        }

        /// <summary>
        /// Get mod settings folder path
        /// </summary>
        public string GetSettingsFolderPath()
        {
            string path = Path.Combine(GenFilePaths.ConfigFolderPath, "RimChat");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }
    }
}

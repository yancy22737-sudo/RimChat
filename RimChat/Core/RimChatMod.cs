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
            PromptRuntimeVariableBridge.InitializeBridgeChain();
            Settings?.EnsureRpgPromptTextsLoaded();
            Settings?.EnsurePawnPersonalityTokenForRpgChannelsSafe();
            RefreshDefaultPresetSnapshotOnStartup();

            // Initialize FactionPromptManager
            FactionPromptManager.Instance.Initialize();

            // Apply Harmony patches
            var harmony = new Harmony("RimChat.AIDriven");
            RimChat.Patches.HarmonyPatchStartupSelfCheck.Run();
            harmony.PatchAll();

            // Initialize custom patches that require dynamic method lookup
            RimChat.Patches.CommsConsolePatch.Initialize(harmony);
            RimChat.Patches.QuestGenPatch.Initialize(harmony);

            // Inject CompPawnDialogue to all eligible pawn ThingDefs after all defs are loaded
            LongEventHandler.ExecuteWhenFinished(PawnDialogueCompDefInjector.EnsureInjected);

            DLCCompatibility.LogDLCStatus();
            Log.Message("[RimChat] Mod initialized successfully.");
        }

        private static void RefreshDefaultPresetSnapshotOnStartup()
        {
            if (Settings == null)
            {
                return;
            }

            try
            {
                // Force-refresh immutable default preset payload from Prompt/Default files on every startup.
                IPromptPresetService presetService = new PromptPresetService();
                PromptPresetStoreConfig store = presetService.LoadAll(Settings);
                presetService.SaveAll(store);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Default preset refresh on startup failed: {ex.Message}");
            }
        }

        public override string SettingsCategory()
        {
            return "RimChat_SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            bool workbenchActive = Settings.selectedTab == 2;
            ResizeSettingsWindowForWorkbench(workbenchActive);

            if (workbenchActive)
            {
                // Escape hatch: a small "back to settings" button so the user is never trapped.
                // selectedTab is a sticky instance field — without this, closing & reopening
                // the dialog while on this tab leaves the user with no way to navigate away.
                Rect backRect = new Rect(inRect.x, inRect.y, 140f, 24f);
                if (Widgets.ButtonText(backRect, "RimChat_ReturnToSettings".Translate()))
                {
                    Settings.selectedTab = 0;
                    return;
                }

                Rect contentRect = new Rect(inRect.x, inRect.y + 28f, inRect.width, inRect.height - 28f);
                // Block GUI.changed from propagating to parent Dialog_ModSettings,
                // which would otherwise trigger WriteSettings() → ExposeData() (80+ Scribe fields) every Repaint.
                bool guiChanged = GUI.changed;
                Settings.DrawTab_PromptSettingsDirect(contentRect);
                GUI.changed = guiChanged;
            }
            else
            {
                Settings.DoWindowContents(inRect);
            }
        }

        private static void ResizeSettingsWindowForWorkbench(bool workbenchActive)
        {
            Dialog_ModSettings settingsWindow = Find.WindowStack.WindowOfType<Dialog_ModSettings>();
            if (settingsWindow == null) return;

            settingsWindow.doCloseX = true;
            settingsWindow.draggable = true;
            settingsWindow.closeOnAccept = false;
            settingsWindow.absorbInputAroundWindow = false;
            settingsWindow.preventCameraMotion = false;
            settingsWindow.closeOnClickedOutside = false;

            float targetWidth = workbenchActive
                ? Mathf.Min(Verse.UI.screenWidth * 0.9f, 1580f)
                : 900f;
            float targetHeight = workbenchActive
                ? Mathf.Min(Verse.UI.screenHeight * 0.9f, 960f)
                : 700f;

            if (Mathf.Abs(settingsWindow.windowRect.width - targetWidth) > 1f ||
                Mathf.Abs(settingsWindow.windowRect.height - targetHeight) > 1f)
            {
                settingsWindow.windowRect.width = targetWidth;
                settingsWindow.windowRect.height = targetHeight;
                settingsWindow.windowRect.x = (Verse.UI.screenWidth - targetWidth) / 2f;
                settingsWindow.windowRect.y = (Verse.UI.screenHeight - targetHeight) / 2f;
            }
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

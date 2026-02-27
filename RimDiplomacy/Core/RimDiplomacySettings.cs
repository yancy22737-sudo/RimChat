using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using Verse.Sound;

namespace RimDiplomacy
{
    public partial class RimDiplomacySettings : ModSettings
    {
        // Provider Selection
        public bool UseCloudProviders = true;

        // Cloud API Configs
        public List<ApiConfig> CloudConfigs = new List<ApiConfig>();

        // Local Model Config
        public LocalModelConfig LocalConfig = new LocalModelConfig();

        // AI Control Settings
        public int MaxAIFactions = 3;
        public bool EnableAISupplementRaid = true;
        public bool EnableAISupplementCaravan = true;
        public bool EnableAISupplementReinforce = true;
        public bool EnableAIDialogueQuest = true;

        // AI Behavior Limits
        public int MaxGoodwillAdjustmentPerCall = 15;
        public int MaxDailyGoodwillAdjustment = 30;
        public int GoodwillCooldownTicks = 0;
        public int MaxGiftSilverAmount = 1000;
        public int MaxGiftGoodwillGain = 10;
        public int GiftCooldownTicks = 60000;
        public int MinGoodwillForAid = 40;
        public int AidCooldownTicks = 120000;
        public int MaxGoodwillForWarDeclaration = -50;
        public int WarCooldownTicks = 60000;
        public int MaxPeaceCost = 5000;
        public int PeaceGoodwillReset = -20;
        public int PeaceCooldownTicks = 60000;
        public int CaravanCooldownTicks = 90000;
        public bool EnableAIGoodwillAdjustment = true;
        public bool EnableAIGiftSending = true;
        public bool EnableAIWarDeclaration = true;
        public bool EnableAIPeaceMaking = true;
        public bool EnableAITradeCaravan = true;
        public bool EnableAIAidRequest = true;
        public bool EnableAPICallLogging = true;
        public int MaxAPICallsPerHour = 20;

        // Threshold Settings
        public int GoodwillThresholdHostile = -80;
        public int GoodwillThresholdFriendly = 80;
        public int PlayerProvokeCooldownHours = 24;
        public int ThreatCooldownDays = 3;

        // News System Settings
        public bool EnableNewsSystem = true;
        public int NewsIntervalMinDays = 2;
        public int NewsIntervalMaxDays = 3;
        public int MaxNewsQueueSize = 10;
        public bool EnablePlayerInfluenceNews = true;
        public bool EnableAISimulationNews = true;

        // Debug Settings
        public bool EnableDebugLogging = false;
        public bool LogAIRequests = true;
        public bool LogAIResponses = true;
        public bool LogInternals = false;

        // Connection Test State
        private string connectionTestStatus = "";
        private bool isTestingConnection = false;

        // Model Cache
        private static readonly Dictionary<string, List<string>> ModelCache = new();

        // Prompt Settings
        public PromptConfig GlobalPrompt = new PromptConfig { Name = "Global", SystemPrompt = GetDefaultGlobalPrompt() };
        public List<PromptConfig> FactionPrompts = new List<PromptConfig>();
        private Vector2 factionListScrollPosition = Vector2.zero;
        private Vector2 promptEditorScrollPosition = Vector2.zero;
        private bool showHiddenFactions = false;
        private Faction selectedFactionForPrompt = null;

        // Tab Settings
        private int selectedTab = 0;
        private readonly string[] tabNames = { "RimDiplomacy_Tab_API", "RimDiplomacy_Tab_ModOptions", "RimDiplomacy_Tab_AIControl", "RimDiplomacy_Tab_Prompts" };

        public override void ExposeData()
        {
            Scribe_Values.Look(ref UseCloudProviders, "UseCloudProviders", true);
            Scribe_Collections.Look(ref CloudConfigs, "CloudConfigs", LookMode.Deep);
            Scribe_Deep.Look(ref LocalConfig, "LocalConfig");

            Scribe_Values.Look(ref MaxAIFactions, "MaxAIFactions", 3);
            Scribe_Values.Look(ref EnableAISupplementRaid, "EnableAISupplementRaid", true);
            Scribe_Values.Look(ref EnableAISupplementCaravan, "EnableAISupplementCaravan", true);
            Scribe_Values.Look(ref EnableAISupplementReinforce, "EnableAISupplementReinforce", true);
            Scribe_Values.Look(ref EnableAIDialogueQuest, "EnableAIDialogueQuest", true);
            Scribe_Values.Look(ref GoodwillThresholdHostile, "GoodwillThresholdHostile", -80);
            Scribe_Values.Look(ref GoodwillThresholdFriendly, "GoodwillThresholdFriendly", 80);
            Scribe_Values.Look(ref PlayerProvokeCooldownHours, "PlayerProvokeCooldownHours", 24);
            Scribe_Values.Look(ref ThreatCooldownDays, "ThreatCooldownDays", 3);
            Scribe_Values.Look(ref EnableNewsSystem, "EnableNewsSystem", true);
            Scribe_Values.Look(ref NewsIntervalMinDays, "NewsIntervalMinDays", 2);
            Scribe_Values.Look(ref NewsIntervalMaxDays, "NewsIntervalMaxDays", 3);
            Scribe_Values.Look(ref MaxNewsQueueSize, "MaxNewsQueueSize", 10);
            Scribe_Values.Look(ref EnablePlayerInfluenceNews, "EnablePlayerInfluenceNews", true);
            Scribe_Values.Look(ref EnableAISimulationNews, "EnableAISimulationNews", true);

            // Debug Settings
            Scribe_Values.Look(ref EnableDebugLogging, "EnableDebugLogging", false);
            Scribe_Values.Look(ref LogAIRequests, "LogAIRequests", true);
            Scribe_Values.Look(ref LogAIResponses, "LogAIResponses", true);
            Scribe_Values.Look(ref LogInternals, "LogInternals", false);

            // Prompt Settings
            Scribe_Deep.Look(ref GlobalPrompt, "GlobalPrompt");
            Scribe_Collections.Look(ref FactionPrompts, "FactionPrompts", LookMode.Deep);

            // AI Control Settings
            ExposeData_AI();

            // Initialize defaults
            if (CloudConfigs == null) CloudConfigs = new List<ApiConfig>();
            if (LocalConfig == null) LocalConfig = new LocalModelConfig();
            if (GlobalPrompt == null) GlobalPrompt = new PromptConfig { Name = "Global", SystemPrompt = GetDefaultGlobalPrompt() };
            if (FactionPrompts == null) FactionPrompts = new List<PromptConfig>();

            base.ExposeData();
        }

        private static string GetDefaultGlobalPrompt()
        {
            return "You are an AI controlling a faction in RimWorld. Respond in character based on the faction's characteristics, leader traits, and current relationship with the player.";
        }

        public void DoWindowContents(Rect inRect)
        {
            // Draw tabs at the top
            float tabHeight = 32f;
            Rect tabRect = new Rect(inRect.x, inRect.y, inRect.width, tabHeight);
            DrawTabs(tabRect);

            // Content area below tabs
            Rect contentRect = new Rect(inRect.x, inRect.y + tabHeight + 5f, inRect.width, inRect.height - tabHeight - 5f);
            
            if (selectedTab == 3)
            {
                DrawTab_PromptSettingsDirect(contentRect);
            }
            else if (selectedTab == 2)
            {
                DrawTab_AIControl(contentRect);
            }
            else
            {
                Listing_Standard listingStandard = new Listing_Standard();
                listingStandard.Begin(contentRect);

                switch (selectedTab)
                {
                    case 0:
                        DrawTab_APISettings(listingStandard);
                        break;
                    case 1:
                        DrawTab_ModOptions(listingStandard);
                        break;
                }

                listingStandard.End();
            }
        }

        private void DrawTabs(Rect tabRect)
        {
            float tabWidth = tabRect.width / tabNames.Length;
            
            for (int i = 0; i < tabNames.Length; i++)
            {
                Rect singleTabRect = new Rect(tabRect.x + i * tabWidth, tabRect.y, tabWidth, tabRect.height);
                
                bool isSelected = i == selectedTab;
                
                // Tab background
                if (isSelected)
                {
                    Widgets.DrawBoxSolid(singleTabRect, new Color(0.2f, 0.4f, 0.6f));
                }
                else
                {
                    Widgets.DrawBoxSolid(singleTabRect, new Color(0.15f, 0.15f, 0.15f));
                }
                
                // Tab border
                Widgets.DrawBox(singleTabRect);
                
                // Tab label
                TextAnchor oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = isSelected ? Color.white : Color.gray;
                Widgets.Label(singleTabRect, tabNames[i].Translate());
                GUI.color = Color.white;
                Text.Anchor = oldAnchor;
                
                // Click handling
                if (Widgets.ButtonInvisible(singleTabRect))
                {
                    selectedTab = i;
                }
            }
        }

        private void DrawTab_APISettings(Listing_Standard listing)
        {
            listing.Label("RimDiplomacy_APISettings".Translate());
            listing.GapLine();

            // Provider Selection
            DrawProviderSelection(listing);
            listing.Gap();

            // Draw appropriate section
            if (UseCloudProviders)
            {
                DrawCloudProvidersSection(listing);
            }
            else
            {
                DrawLocalProviderSection(listing);
            }

            listing.Gap();
            DrawConnectionTestButton(listing);

            listing.Gap();
            DrawDebugSettingsSection(listing);
        }

        private void DrawDebugSettingsSection(Listing_Standard listing)
        {
            listing.Label("RimDiplomacy_DebugSettings".Translate());
            listing.GapLine();

            listing.CheckboxLabeled("RimDiplomacy_EnableDebugLogging".Translate(), ref EnableDebugLogging);
            if (EnableDebugLogging)
            {
                listing.CheckboxLabeled("RimDiplomacy_LogAIRequests".Translate(), ref LogAIRequests);
                listing.CheckboxLabeled("RimDiplomacy_LogAIResponses".Translate(), ref LogAIResponses);
                listing.CheckboxLabeled("RimDiplomacy_LogInternals".Translate(), ref LogInternals);
            }
        }

        private void DrawTab_ModOptions(Listing_Standard listing)
        {
            listing.Label("RimDiplomacy_AIControlSettings".Translate());
            listing.GapLine();

            listing.Label("RimDiplomacy_SettingsMaxAIFactionsLabel".Translate(MaxAIFactions));
            MaxAIFactions = (int)listing.Slider(MaxAIFactions, 1, 10);

            listing.CheckboxLabeled("RimDiplomacy_EnableAISupplementRaid".Translate(), ref EnableAISupplementRaid);
            listing.CheckboxLabeled("RimDiplomacy_EnableAISupplementCaravan".Translate(), ref EnableAISupplementCaravan);
            listing.CheckboxLabeled("RimDiplomacy_EnableAISupplementReinforce".Translate(), ref EnableAISupplementReinforce);
            listing.CheckboxLabeled("RimDiplomacy_EnableAIDialogueQuest".Translate(), ref EnableAIDialogueQuest);

            listing.Gap();
            listing.Label("RimDiplomacy_ThresholdSettings".Translate());
            listing.GapLine();

            listing.Label("RimDiplomacy_SettingsHostileThresholdLabel".Translate(GoodwillThresholdHostile));
            GoodwillThresholdHostile = (int)listing.Slider(GoodwillThresholdHostile, -100, 0);

            listing.Label("RimDiplomacy_SettingsFriendlyThresholdLabel".Translate(GoodwillThresholdFriendly));
            GoodwillThresholdFriendly = (int)listing.Slider(GoodwillThresholdFriendly, 0, 100);

            listing.Gap();
            listing.Label("RimDiplomacy_NewsSystemSettings".Translate());
            listing.GapLine();

            listing.CheckboxLabeled("RimDiplomacy_EnableNewsSystem".Translate(), ref EnableNewsSystem);
            if (EnableNewsSystem)
            {
                listing.Label("RimDiplomacy_SettingsNewsIntervalMinLabel".Translate(NewsIntervalMinDays));
                NewsIntervalMinDays = (int)listing.Slider(NewsIntervalMinDays, 1, 5);

                listing.Label("RimDiplomacy_SettingsNewsIntervalMaxLabel".Translate(NewsIntervalMaxDays));
                NewsIntervalMaxDays = (int)listing.Slider(NewsIntervalMaxDays, NewsIntervalMinDays, 7);

                listing.CheckboxLabeled("RimDiplomacy_EnablePlayerInfluenceNews".Translate(), ref EnablePlayerInfluenceNews);
                listing.CheckboxLabeled("RimDiplomacy_EnableAISimulationNews".Translate(), ref EnableAISimulationNews);
            }
        }

        private void DrawTab_PromptSettings(Listing_Standard listing)
        {
            DrawPromptSettingsSection(listing);
        }

        private Vector2 promptTabScrollPosition = Vector2.zero;

        private void DrawTab_PromptSettingsDirect(Rect rect)
        {
            float viewHeight = 1000f;
            Rect viewRect = new Rect(0, 0, rect.width - 16f, viewHeight);
            
            Widgets.BeginScrollView(rect, ref promptTabScrollPosition, viewRect);
            
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(new Rect(0, 0, viewRect.width, viewRect.height));
            
            DrawPromptSettingsSection(listing);
            
            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawProviderSelection(Listing_Standard listing)
        {
            Rect radioRect1 = listing.GetRect(24f);
            if (Widgets.RadioButtonLabeled(radioRect1, "RimDiplomacy_CloudProviders".Translate(), UseCloudProviders))
            {
                UseCloudProviders = true;
            }

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect cloudDescRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(cloudDescRect, "RimDiplomacy_CloudProvidersDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.Gap(3f);

            Rect radioRect2 = listing.GetRect(24f);
            if (Widgets.RadioButtonLabeled(radioRect2, "RimDiplomacy_LocalProvider".Translate(), !UseCloudProviders))
            {
                UseCloudProviders = false;
            }

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect localDescRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(localDescRect, "RimDiplomacy_LocalProviderDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawCloudProvidersSection(Listing_Standard listing)
        {
            Rect headerRect = listing.GetRect(24f);

            float addBtnSize = 24f;
            Rect addButtonRect = new Rect(headerRect.x + headerRect.width - addBtnSize, headerRect.y, addBtnSize, addBtnSize);
            headerRect.width -= (addBtnSize + 5f);

            Widgets.Label(headerRect, "RimDiplomacy_CloudApiConfigurations".Translate());

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect descRect = listing.GetRect(Text.LineHeight * 2);
            descRect.width -= 35f;
            Widgets.Label(descRect, "RimDiplomacy_CloudApiConfigurationsDesc".Translate());
            GUI.color = Color.white;

            Color prevColor = GUI.color;
            GUI.color = new Color(0.3f, 0.9f, 0.3f);
            if (Widgets.ButtonText(addButtonRect, "+"))
            {
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                CloudConfigs.Add(new ApiConfig());
            }
            GUI.color = prevColor;

            Text.Font = GameFont.Small;
            listing.Gap(6f);

            // Table Headers
            Rect tableHeaderRect = listing.GetRect(20f);
            float x = tableHeaderRect.x;
            float y = tableHeaderRect.y;
            float height = tableHeaderRect.height;
            float totalWidth = tableHeaderRect.width;

            float providerWidth = 90f;
            float modelWidth = 180f;
            float controlsWidth = 100f;

            Rect providerHeaderRect = new Rect(x, y, providerWidth, height);
            Widgets.Label(providerHeaderRect, "RimDiplomacy_ProviderHeader".Translate());

            float middleStartX = x + providerWidth + 5f;
            Rect apiKeyHeaderRect = new Rect(middleStartX, y, 180f, height);
            Widgets.Label(apiKeyHeaderRect, "RimDiplomacy_ApiKeyHeader".Translate());

            Rect modelHeaderRect = new Rect(totalWidth - controlsWidth - modelWidth - 5f, y, modelWidth, height);
            Widgets.Label(modelHeaderRect, "RimDiplomacy_ModelHeader".Translate());

            Rect enabledHeaderRect = new Rect(totalWidth - controlsWidth + 5f, y, controlsWidth, height);
            Widgets.Label(enabledHeaderRect, "RimDiplomacy_EnabledHeader".Translate());

            listing.Gap(3f);

            for (int i = 0; i < CloudConfigs.Count; i++)
            {
                if (DrawCloudConfigRow(listing, CloudConfigs[i], i))
                {
                    CloudConfigs.RemoveAt(i);
                    i--;
                }
                listing.Gap(2f);
            }

            Text.Font = GameFont.Small;
        }

        private bool DrawCloudConfigRow(Listing_Standard listing, ApiConfig config, int index)
        {
            Text.Font = GameFont.Tiny;

            Rect rowRect = listing.GetRect(24f);
            float x = rowRect.x;
            float y = rowRect.y;
            float height = rowRect.height;
            float totalWidth = rowRect.width;

            float providerWidth = 90f;
            float modelWidth = 180f;
            float controlsWidth = 100f;
            float gap = 5f;

            float middleZoneWidth = totalWidth - providerWidth - modelWidth - controlsWidth - (gap * 3);
            float middleStartX = x + providerWidth + gap;

            Color originalColor = GUI.color;
            if (!config.IsEnabled)
            {
                GUI.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);
            }

            // 1. Provider Dropdown
            DrawProviderDropdown(x, y, height, providerWidth, config);

            // 2. Middle Zone (API Key or Custom URL)
            if (config.Provider == AIProvider.Custom)
            {
                float keyWidth = (middleZoneWidth * 0.4f) - (gap / 2);
                float urlWidth = (middleZoneWidth * 0.6f) - (gap / 2);

                DrawApiKeyInput(middleStartX, y, height, keyWidth, config);
                DrawBaseUrlInput(middleStartX + keyWidth + gap, y, height, urlWidth, config);
            }
            else
            {
                DrawApiKeyInput(middleStartX, y, height, middleZoneWidth, config);
            }

            // 3. Model
            float modelStartX = middleStartX + middleZoneWidth + gap;
            DrawModelSelector(modelStartX, y, height, modelWidth, config);

            GUI.color = originalColor;

            // 4. Controls (Enable + Reorder + Delete)
            float btnSize = 22f;
            float btnGap = 2f;

            float deleteX = totalWidth - btnSize;
            float downX = deleteX - btnGap - btnSize;
            float upX = downX - btnGap - btnSize;

            float controlsStartX = totalWidth - controlsWidth;
            float checkboxSpaceWidth = upX - controlsStartX;
            float checkboxX = controlsStartX + (checkboxSpaceWidth - 24f) / 2f;

            Rect toggleRect = new Rect(checkboxX, y, 24f, height);
            Widgets.Checkbox(new Vector2(toggleRect.x, toggleRect.y), ref config.IsEnabled, 20f);
            if (Mouse.IsOver(toggleRect)) TooltipHandler.TipRegion(toggleRect, "RimDiplomacy_EnableDisableTooltip".Translate());

            // Reorder buttons
            Rect upButtonRect = new Rect(upX, y, btnSize, height);
            if (Widgets.ButtonText(upButtonRect, "▲") && index > 0)
            {
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                (CloudConfigs[index], CloudConfigs[index - 1]) = (CloudConfigs[index - 1], CloudConfigs[index]);
            }

            Rect downButtonRect = new Rect(downX, y, btnSize, height);
            if (Widgets.ButtonText(downButtonRect, "▼") && index < CloudConfigs.Count - 1)
            {
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                (CloudConfigs[index], CloudConfigs[index + 1]) = (CloudConfigs[index + 1], CloudConfigs[index]);
            }

            // Delete button
            Rect deleteRect = new Rect(deleteX, y, btnSize, height);
            bool deleteClicked = false;
            bool canDelete = CloudConfigs.Count > 1;

            Color prevDeleteColor = GUI.color;
            if (canDelete)
            {
                GUI.color = new Color(1f, 0.4f, 0.4f);
            }
            else
            {
                GUI.color = Color.gray;
            }

            if (Widgets.ButtonText(deleteRect, "×", active: canDelete))
            {
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                deleteClicked = true;
            }
            GUI.color = prevDeleteColor;

            Text.Font = GameFont.Tiny;
            return deleteClicked;
        }

        private void DrawProviderDropdown(float x, float y, float height, float width, ApiConfig config)
        {
            Rect providerRect = new Rect(x, y, width, height);
            if (Widgets.ButtonText(providerRect, config.Provider.GetLabel()))
            {
                List<FloatMenuOption> providerOptions = new List<FloatMenuOption>();
                foreach (AIProvider provider in Enum.GetValues(typeof(AIProvider)))
                {
                    if (provider == AIProvider.None) continue;

                    providerOptions.Add(new FloatMenuOption(provider.GetLabel(), () =>
                    {
                        config.Provider = provider;
                        if (provider == AIProvider.Custom)
                        {
                            config.SelectedModel = "Custom";
                        }
                        else
                        {
                            config.SelectedModel = "";
                        }
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(providerOptions));
            }
        }

        private void DrawApiKeyInput(float x, float y, float height, float width, ApiConfig config)
        {
            Rect apiKeyRect = new Rect(x, y, width, height);
            config.ApiKey = DrawTextFieldWithPlaceholder(apiKeyRect, config.ApiKey, "RimDiplomacy_Placeholder_ApiKey".Translate());
        }

        private void DrawBaseUrlInput(float x, float y, float height, float width, ApiConfig config)
        {
            Rect baseUrlRect = new Rect(x, y, width, height);
            config.BaseUrl = DrawTextFieldWithPlaceholder(baseUrlRect, config.BaseUrl, "https://...");
            if (Mouse.IsOver(baseUrlRect)) TooltipHandler.TipRegion(baseUrlRect, "RimDiplomacy_BaseUrlTooltip".Translate());
        }

        private void DrawModelSelector(float x, float y, float height, float width, ApiConfig config)
        {
            Rect modelRect = new Rect(x, y, width, height);

            if (config.SelectedModel == "Custom")
            {
                float xButtonWidth = 22f;
                float textFieldWidth = width - xButtonWidth - 2f;

                Rect textFieldRect = new Rect(x, y, textFieldWidth, height);
                Rect backButtonRect = new Rect(x + textFieldWidth + 2f, y, xButtonWidth, height);

                config.CustomModelName = DrawTextFieldWithPlaceholder(textFieldRect, config.CustomModelName, "Model ID");

                if (Widgets.ButtonText(backButtonRect, "×"))
                {
                    SoundDefOf.Click.PlayOneShotOnCamera(null);
                    config.SelectedModel = "";
                }
            }
            else
            {
                string buttonLabel = string.IsNullOrEmpty(config.SelectedModel) ? "RimDiplomacy_ChooseModel".Translate() : config.SelectedModel;
                if (Widgets.ButtonText(modelRect, buttonLabel))
                {
                    ShowModelSelectionMenu(config);
                }
            }
        }

        private string DrawTextFieldWithPlaceholder(Rect rect, string text, string placeholder)
        {
            string result = Widgets.TextField(rect, text);

            if (string.IsNullOrEmpty(result))
            {
                TextAnchor originalAnchor = Text.Anchor;
                Color originalColor = GUI.color;

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);

                Rect labelRect = new Rect(rect.x + 5f, rect.y, rect.width - 5f, rect.height);
                Widgets.Label(labelRect, placeholder);

                GUI.color = originalColor;
                Text.Anchor = originalAnchor;
            }

            return result;
        }

        private void ShowModelSelectionMenu(ApiConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("RimDiplomacy_EnterApiKey".Translate(), null)
                }));
                return;
            }

            string url = config.Provider.GetListModelsUrl();
            if (string.IsNullOrEmpty(url)) return;

            if (config.Provider == AIProvider.Custom && !string.IsNullOrEmpty(config.BaseUrl))
            {
                url = config.BaseUrl.Replace("/chat/completions", "/models");
            }

            void OpenMenu(List<string> models)
            {
                var options = new List<FloatMenuOption>();

                if (models != null && models.Any())
                {
                    options.AddRange(models.Select(model => new FloatMenuOption(model, () => config.SelectedModel = model)));
                }
                else
                {
                    options.Add(new FloatMenuOption("(no models found)", null));
                }

                options.Add(new FloatMenuOption("Custom", () => config.SelectedModel = "Custom"));
                Find.WindowStack.Add(new FloatMenu(options));
            }

            if (ModelCache.ContainsKey(url))
            {
                OpenMenu(ModelCache[url]);
            }
            else
            {
                FetchModelsAsync(url, config.ApiKey, OpenMenu);
            }
        }

        private void FetchModelsAsync(string url, string apiKey, Action<List<string>> callback)
        {
            Task.Run(() =>
            {
                try
                {
                    using (var request = new UnityWebRequest(url, "GET"))
                    {
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                        request.timeout = 10;

                        var operation = request.SendWebRequest();
                        while (!operation.isDone) { }

                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            var models = ParseModelsFromResponse(request.downloadHandler.text);
                            ModelCache[url] = models;
                            callback(models);
                        }
                        else
                        {
                            callback(null);
                        }
                    }
                }
                catch
                {
                    callback(null);
                }
            });
        }

        private List<string> ParseModelsFromResponse(string json)
        {
            var models = new List<string>();
            try
            {
                // Simple JSON parsing for models list
                if (json.Contains("\"id\":"))
                {
                    var parts = json.Split('"');
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        if (parts[i] == "id" && i + 2 < parts.Length)
                        {
                            string modelId = parts[i + 2];
                            if (!string.IsNullOrEmpty(modelId) && !models.Contains(modelId))
                            {
                                models.Add(modelId);
                            }
                        }
                    }
                }
            }
            catch { }
            return models;
        }

        private void DrawLocalProviderSection(Listing_Standard listing)
        {
            listing.Label("RimDiplomacy_LocalProviderConfiguration".Translate());
            listing.Gap(6f);

            Rect rowRect = listing.GetRect(24f);
            float x = rowRect.x;
            float y = rowRect.y;
            float height = rowRect.height;

            Rect baseUrlLabelRect = new Rect(x, y, 80f, height);
            Widgets.Label(baseUrlLabelRect, "RimDiplomacy_BaseUrlLabel".Translate());
            x += 85f;

            Rect urlRect = new Rect(x, y, 250f, height);
            LocalConfig.BaseUrl = Widgets.TextField(urlRect, LocalConfig.BaseUrl);
            x += 285f;

            Rect modelLabelRect = new Rect(x, y, 70f, height);
            Widgets.Label(modelLabelRect, "RimDiplomacy_ModelLabel".Translate());
            x += 75f;

            Rect modelRect = new Rect(x, y, 200f, height);
            LocalConfig.ModelName = Widgets.TextField(modelRect, LocalConfig.ModelName);
        }

        private void DrawConnectionTestButton(Listing_Standard listing)
        {
            Rect buttonRect = listing.GetRect(30f);
            string buttonLabel = isTestingConnection
                ? "RimDiplomacy_TestingConnection".Translate()
                : "RimDiplomacy_TestConnectionButton".Translate();

            GUI.color = isTestingConnection ? Color.gray : Color.white;
            bool clicked = Widgets.ButtonText(buttonRect, buttonLabel, active: !isTestingConnection);
            GUI.color = Color.white;

            if (clicked && !isTestingConnection)
            {
                TestConnection();
            }

            listing.Gap(2f);

            if (!string.IsNullOrEmpty(connectionTestStatus))
            {
                GUI.color = GetStatusColor();
                listing.Label(connectionTestStatus);
                GUI.color = Color.white;
            }
        }

        private Color GetStatusColor()
        {
            if (connectionTestStatus.Contains("RimDiplomacy_ConnectionSuccess".Translate().ToString()))
                return Color.green;
            if (connectionTestStatus.Contains("RimDiplomacy_ConnectionFailed".Translate().ToString()))
                return Color.red;
            return Color.yellow;
        }

        private void TestConnection()
        {
            isTestingConnection = true;
            connectionTestStatus = "RimDiplomacy_ConnectionTesting".Translate();

            LongEventHandler.QueueLongEvent(() =>
            {
                TestConnectionSync();
            }, "RimDiplomacy_TestingConnection".Translate(), false, null);
        }

        private void TestConnectionSync()
        {
            try
            {
                if (UseCloudProviders)
                {
                    var validConfig = CloudConfigs.FirstOrDefault(c => c.IsValid());
                    if (validConfig == null)
                    {
                        connectionTestStatus = "RimDiplomacy_ConnectionFailed".Translate("RimDiplomacy_NoValidConfig".Translate());
                        return;
                    }
                    TestCloudConnection(validConfig);
                }
                else
                {
                    TestLocalConnection();
                }
            }
            catch (Exception ex)
            {
                connectionTestStatus = "RimDiplomacy_ConnectionFailed".Translate(ex.Message);
            }
            finally
            {
                isTestingConnection = false;
            }
        }

        private void TestCloudConnection(ApiConfig config)
        {
            string url = config.Provider.GetListModelsUrl();
            if (config.Provider == AIProvider.Custom && !string.IsNullOrEmpty(config.BaseUrl))
            {
                url = config.BaseUrl.Replace("/chat/completions", "/models");
            }

            using (var request = new UnityWebRequest(url, "GET"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 10;
                request.SetRequestHeader("Authorization", $"Bearer {config.ApiKey}");

                var operation = request.SendWebRequest();
                while (!operation.isDone) { System.Threading.Thread.Sleep(100); }

                if (request.result == UnityWebRequest.Result.Success || request.responseCode == 200)
                {
                    connectionTestStatus = "RimDiplomacy_ConnectionSuccess".Translate();
                }
                else if (request.responseCode == 401)
                {
                    connectionTestStatus = "RimDiplomacy_ConnectionFailed".Translate("RimDiplomacy_InvalidAPIKey".Translate());
                }
                else
                {
                    connectionTestStatus = "RimDiplomacy_ConnectionFailed".Translate($"HTTP {request.responseCode}");
                }
            }
        }

        private void TestLocalConnection()
        {
            string baseUrl = LocalConfig.BaseUrl.TrimEnd('/');
            
            // Try Ollama endpoint first
            string testUrl = baseUrl + "/api/tags";
            bool success = TryTestUrl(testUrl, "GET", null);
            
            // If Ollama fails, try Player2 endpoint
            if (!success)
            {
                testUrl = baseUrl + "/v1/models";
                success = TryTestUrl(testUrl, "GET", null);
            }
            
            // If both fail, try a simple POST to chat completions endpoint
            if (!success)
            {
                testUrl = baseUrl + "/v1/chat/completions";
                success = TryTestUrl(testUrl, "POST", "{\"model\":\"test\",\"messages\":[]}");
            }
            
            if (success)
            {
                connectionTestStatus = "RimDiplomacy_ConnectionSuccess".Translate();
            }
            else
            {
                connectionTestStatus = "RimDiplomacy_ConnectionFailed".Translate("RimDiplomacy_LocalServiceNotFound".Translate());
            }
        }
        
        private bool TryTestUrl(string url, string method, string body)
        {
            try
            {
                using (var request = new UnityWebRequest(url, method))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.timeout = 5;
                    
                    if (body != null)
                    {
                        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                        request.SetRequestHeader("Content-Type", "application/json");
                    }

                    var operation = request.SendWebRequest();
                    while (!operation.isDone) { System.Threading.Thread.Sleep(50); }

                    return request.responseCode > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        #region Prompt Settings

        private void DrawPromptSettingsSection(Listing_Standard listing)
        {
            listing.Label("RimDiplomacy_PromptSettings".Translate());
            listing.GapLine();

            // Global Prompt
            DrawGlobalPromptEditor(listing);
            listing.Gap();

            // Faction Prompts
            DrawFactionPromptsList(listing);
        }

        private void DrawGlobalPromptEditor(Listing_Standard listing)
        {
            listing.Label("RimDiplomacy_GlobalPrompt".Translate());
            
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect descRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(descRect, "RimDiplomacy_GlobalPromptDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(3f);

            // Token count display
            int tokenCount = EstimateTokenCount(GlobalPrompt.SystemPrompt);
            Text.Font = GameFont.Tiny;
            GUI.color = tokenCount > 2000 ? Color.red : Color.green;
            Rect tokenRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(tokenRect, "RimDiplomacy_TokenCount".Translate(tokenCount));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(3f);

            // Text area
            float textHeight = 150f;
            Rect textAreaRect = listing.GetRect(textHeight);
            
            GlobalPrompt.SystemPrompt = Widgets.TextArea(textAreaRect, GlobalPrompt.SystemPrompt);

            listing.Gap(3f);

            // Reset button
            Rect resetRect = listing.GetRect(24f);
            if (Widgets.ButtonText(resetRect, "RimDiplomacy_ResetToDefault".Translate()))
            {
                GlobalPrompt.SystemPrompt = GetDefaultGlobalPrompt();
            }
        }

        private void DrawFactionPromptsList(Listing_Standard listing)
        {
            // Check if in game
            if (Current.Game == null || Find.FactionManager == null)
            {
                GUI.color = Color.gray;
                listing.Label("RimDiplomacy_FactionPromptsNeedGame".Translate());
                GUI.color = Color.white;
                return;
            }

            listing.Label("RimDiplomacy_FactionPrompts".Translate());
            
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect descRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(descRect, "RimDiplomacy_FactionPromptsDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(5f);

            // Show hidden factions toggle
            Rect toggleRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(toggleRect, "RimDiplomacy_ShowHiddenFactions".Translate(), ref showHiddenFactions);
            listing.Gap(5f);

            // Two-column layout: Faction list on left, prompt editor on right
            float totalHeight = 400f;
            Rect mainRect = listing.GetRect(totalHeight);
            
            float leftWidth = mainRect.width * 0.4f;
            float rightWidth = mainRect.width * 0.6f - 10f;
            
            Rect leftRect = new Rect(mainRect.x, mainRect.y, leftWidth, totalHeight);
            Rect rightRect = new Rect(mainRect.x + leftWidth + 10f, mainRect.y, rightWidth, totalHeight);

            // Draw faction list (left side)
            DrawFactionList(leftRect);
            
            // Draw prompt editor (right side)
            DrawPromptEditor(rightRect);
        }

        private void DrawFactionList(Rect rect)
        {
            // Background
            Widgets.DrawBox(rect);
            Rect innerRect = rect.ContractedBy(4f);

            // Title
            Text.Font = GameFont.Small;
            Rect titleRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 24f);
            Widgets.Label(titleRect, "RimDiplomacy_FactionList".Translate());
            
            // Scrollable list
            float listY = innerRect.y + 28f;
            Rect listRect = new Rect(innerRect.x, listY, innerRect.width, innerRect.height - 28f);
            
            var factions = GetVisibleFactions();
            
            // Show empty message if no factions
            if (factions.Count == 0)
            {
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(listRect, "RimDiplomacy_NoFactionsAvailable".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }
            
            float rowHeight = 28f;
            float totalListHeight = Mathf.Max(factions.Count * rowHeight, listRect.height);
            Rect viewRect = new Rect(0, 0, listRect.width - 16f, totalListHeight);

            Widgets.BeginScrollView(listRect, ref factionListScrollPosition, viewRect);
            
            float y = 0f;
            for (int i = 0; i < factions.Count; i++)
            {
                var faction = factions[i];
                var prompt = GetOrCreateFactionPrompt(faction);
                
                Rect rowRect = new Rect(0, y, viewRect.width, rowHeight);
                
                // Selection highlight
                if (selectedFactionForPrompt == faction)
                {
                    Widgets.DrawHighlightSelected(rowRect);
                }
                else if (i % 2 == 0)
                {
                    Widgets.DrawLightHighlight(rowRect);
                }

                // Click to select
                if (Widgets.ButtonInvisible(rowRect))
                {
                    selectedFactionForPrompt = faction;
                }

                // Faction icon (if available)
                float xOffset = 4f;
                if (faction.def != null)
                {
                    Texture2D factionIcon = faction.def.FactionIcon;
                    if (factionIcon != null && factionIcon != BaseContent.BadTex)
                    {
                        Rect iconRect = new Rect(xOffset, y + 2f, 24f, 24f);
                        GUI.DrawTexture(iconRect, factionIcon);
                        xOffset += 28f;
                    }
                }

                // Faction name and relation
                Rect nameRect = new Rect(xOffset, y, viewRect.width - xOffset - 30f, rowHeight);
                Text.Anchor = TextAnchor.MiddleLeft;
                
                string label = faction.Name;
                if (prompt.Enabled)
                {
                    label = "✓ " + label;
                }
                Widgets.Label(nameRect, label);
                Text.Anchor = TextAnchor.UpperLeft;

                // Relation color indicator
                Rect relationRect = new Rect(viewRect.width - 26f, y + 6f, 16f, 16f);
                GUI.color = GetRelationColor(faction);
                Widgets.DrawBoxSolid(relationRect, GUI.color);
                GUI.color = Color.white;

                y += rowHeight;
            }
            
            Widgets.EndScrollView();
        }

        private void DrawPromptEditor(Rect rect)
        {
            // Background
            Widgets.DrawBox(rect);
            Rect innerRect = rect.ContractedBy(6f);

            if (selectedFactionForPrompt == null)
            {
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(innerRect, "RimDiplomacy_SelectFaction".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            var prompt = GetOrCreateFactionPrompt(selectedFactionForPrompt);

            // Header with faction info
            float y = innerRect.y;
            
            // Faction name and enable checkbox
            Rect headerRect = new Rect(innerRect.x, y, innerRect.width, 28f);
            Widgets.CheckboxLabeled(headerRect, selectedFactionForPrompt.Name + " - " + "RimDiplomacy_CustomPrompt".Translate(), ref prompt.Enabled);
            y += 32f;

            // Auto-fill button
            Rect autoFillRect = new Rect(innerRect.x, y, 120f, 24f);
            if (Widgets.ButtonText(autoFillRect, "RimDiplomacy_AutoFill".Translate()))
            {
                prompt.SystemPrompt = GenerateFactionPromptFromInfo(selectedFactionForPrompt);
            }
            y += 28f;

            // Token count
            int tokenCount = EstimateTokenCount(prompt.SystemPrompt);
            Text.Font = GameFont.Tiny;
            GUI.color = tokenCount > 2000 ? Color.red : Color.green;
            Rect tokenRect = new Rect(innerRect.x, y, innerRect.width, Text.LineHeight);
            Widgets.Label(tokenRect, "RimDiplomacy_TokenCount".Translate(tokenCount));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 20f;

            // Text area
            float textHeight = innerRect.yMax - y - 30f;
            Rect textRect = new Rect(innerRect.x, y, innerRect.width, textHeight);
            prompt.SystemPrompt = Widgets.TextArea(textRect, prompt.SystemPrompt);
            y += textHeight + 5f;

            // Reset button
            Rect resetRect = new Rect(innerRect.x + innerRect.width - 100f, y, 100f, 24f);
            if (Widgets.ButtonText(resetRect, "RimDiplomacy_Reset".Translate()))
            {
                prompt.SystemPrompt = GenerateFactionPromptFromInfo(selectedFactionForPrompt);
            }
        }

        private List<Faction> GetVisibleFactions()
        {
            if (Find.FactionManager == null)
            {
                Log.Warning("[RimDiplomacy] FactionManager is null");
                return new List<Faction>();
            }

            var allFactions = Find.FactionManager.AllFactions;
            if (allFactions == null)
            {
                Log.Warning("[RimDiplomacy] AllFactions is null");
                return new List<Faction>();
            }

            var result = new List<Faction>();
            
            foreach (var faction in allFactions)
            {
                if (faction == null) continue;
                if (faction.IsPlayer) continue;
                if (!showHiddenFactions && faction.Hidden) continue;
                result.Add(faction);
            }

            return result;
        }

        private PromptConfig GetOrCreateFactionPrompt(Faction faction)
        {
            string factionId = faction.def.defName + "_" + faction.loadID;
            var prompt = FactionPrompts.Find(p => p.FactionId == factionId);
            
            if (prompt == null)
            {
                prompt = new PromptConfig
                {
                    Name = faction.Name,
                    FactionId = factionId,
                    SystemPrompt = GenerateFactionPromptFromInfo(faction),
                    Enabled = false
                };
                FactionPrompts.Add(prompt);
            }
            
            return prompt;
        }

        private Color GetRelationColor(Faction faction)
        {
            int goodwill = faction.PlayerGoodwill;
            if (goodwill >= 80) return Color.green;
            if (goodwill >= 0) return Color.yellow;
            if (goodwill >= -80) return new Color(1f, 0.5f, 0f); // Orange
            return Color.red;
        }

        private string GenerateFactionPromptFromInfo(Faction faction)
        {
            var sb = new System.Text.StringBuilder();
            
            sb.AppendLine($"You are the leader of {faction.Name}.");
            sb.AppendLine();
            
            // Basic info
            sb.AppendLine($"Faction Type: {faction.def.label}");
            sb.AppendLine($"Current Goodwill: {faction.PlayerGoodwill}");
            
            // Leader info
            if (faction.leader != null)
            {
                sb.AppendLine($"Leader: {faction.leader.Name.ToStringFull}");
                if (faction.leader.story != null)
                {
                    sb.AppendLine($"Leader Traits: {string.Join(", ", faction.leader.story.traits.allTraits.Select(t => t.Label))}");
                }
            }
            
            // Ideology (only if Ideology DLC is active)
            if (DLCCompatibility.IsIdeologyActive)
            {
                try
                {
                    var ideosField = faction.GetType().GetField("ideos");
                    if (ideosField != null)
                    {
                        var ideos = ideosField.GetValue(faction);
                        if (ideos != null)
                        {
                            var primaryIdeoProp = ideos.GetType().GetProperty("PrimaryIdeo");
                            if (primaryIdeoProp != null)
                            {
                                var ideo = primaryIdeoProp.GetValue(ideos);
                                if (ideo != null)
                                {
                                    var nameProp = ideo.GetType().GetProperty("name");
                                    var descProp = ideo.GetType().GetProperty("description");
                                    
                                    if (nameProp != null)
                                    {
                                        string ideoName = nameProp.GetValue(ideo) as string;
                                        if (!string.IsNullOrEmpty(ideoName))
                                        {
                                            sb.AppendLine($"Ideology: {ideoName}");
                                        }
                                    }
                                    
                                    if (descProp != null)
                                    {
                                        string ideoDesc = descProp.GetValue(ideo) as string;
                                        if (!string.IsNullOrEmpty(ideoDesc))
                                        {
                                            sb.AppendLine($"Description: {ideoDesc}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            
            // Instructions
            sb.AppendLine();
            sb.AppendLine("Respond to diplomatic interactions in character, considering your faction's characteristics, current relationship with the player, and your leader's personality.");
            
            return sb.ToString();
        }

        private int EstimateTokenCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            // Rough estimation: ~4 characters per token for English/Chinese mixed
            return text.Length / 4;
        }

        #endregion


    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using Verse.Sound;
using RimDiplomacy.UI;
using RimDiplomacy.AI;
using RimDiplomacy.Persistence;

namespace RimDiplomacy.Config
{
    /// <summary>
    /// 逐字输出速度模式
    /// </summary>
    public enum TypewriterSpeedMode
    {
        Fast = 0,      // 极速（快节奏/跳过感）：0.02s / 字
        Standard = 1,  // 标准（最推荐）：0.05s / 字
        Immersive = 2  // 沉浸/慢速：0.11s / 字
    }

    public partial class RimDiplomacySettings : ModSettings
    {
        // Provider Selection
        public bool UseCloudProviders = true;

        // Cloud API Configs
        public List<ApiConfig> CloudConfigs = new List<ApiConfig>();

        // Local Model Config
        public LocalModelConfig LocalConfig = new LocalModelConfig();



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
        public int AidDelayBaseTicks = 90000;
        public int CaravanDelayBaseTicks = 135000;
        public int RaidCooldownTicks = 180000;
        public bool EnableAIGoodwillAdjustment = true;
        public bool EnableAIGiftSending = true;
        public bool EnableAIWarDeclaration = true;
        public bool EnableAIPeaceMaking = true;
        public bool EnableAITradeCaravan = true;
        public bool EnableAIRaidRequest = true;
        public bool EnableAIAidRequest = true;

        // Raid Granular Settings
        public bool EnableRaidStrategy_ImmediateAttack = true;
        public bool EnableRaidStrategy_ImmediateAttackSmart = true;
        public bool EnableRaidStrategy_StageThenAttack = true;
        public bool EnableRaidStrategy_ImmediateAttackSappers = true;
        public bool EnableRaidStrategy_Siege = true;

        public bool EnableRaidArrival_EdgeWalkIn = true;
        public bool EnableRaidArrival_EdgeDrop = true;
        public bool EnableRaidArrival_EdgeWalkInGroups = true;
        public bool EnableRaidArrival_RandomDrop = false;
        public bool EnableRaidArrival_CenterDrop = false;

        public bool EnableAPICallLogging = true;
        public int MaxAPICallsPerHour = 20;



        // Debug Settings
        public bool EnableDebugLogging = false;
        public bool LogAIRequests = true;
        public bool LogAIResponses = true;
        public bool LogInternals = false;
        public bool LogFullMessages = false;

        // UI Settings - 逐字输出速度
        public TypewriterSpeedMode TypewriterSpeedMode = TypewriterSpeedMode.Immersive;

        // Comms Console Settings
        public bool ReplaceCommsConsole = true;

        // RPG Dialogue Settings
        public bool EnableRPGDialogue = true;

        // Connection Test State
        private string connectionTestStatus = "";
        private bool isTestingConnection = false;

        // Model Cache
        private static readonly Dictionary<string, List<string>> ModelCache = new();

        // Prompt Settings - 使用新的 FactionPromptManager
        private Vector2 factionListScrollPosition = Vector2.zero;
        private Vector2 promptEditorScrollPosition = Vector2.zero;
        private bool showHiddenFactions = false;
        private string selectedFactionDefName = null;
        private string editingCustomPrompt = "";
        private bool editingUseCustomPrompt = false;

        // Global Prompt Settings
        public string GlobalSystemPrompt = "";
        public string GlobalDialoguePrompt = "";
        public string RPGSystemPrompt = "";
        public string RPGDialoguePrompt = "";
        public int MaxSystemPromptLength = 2000;
        public int MaxDialoguePromptLength = 2000;
        public int MaxFactionPromptLength = 4000;

        // Prompt editing state
        private string editingSystemPrompt = "";
        private string editingDialoguePrompt = "";
        private Vector2 globalPromptScrollPosition = Vector2.zero;

        // Enhanced TextArea components
        private EnhancedTextArea systemPromptTextArea;
        private EnhancedTextArea dialoguePromptTextArea;
        private EnhancedTextArea factionPromptTextArea;

        // Tab Settings
        private int selectedTab = 0;
        private readonly string[] tabNames = { "RimDiplomacy_Tab_API", "RimDiplomacy_Tab_AIControl", "RimDiplomacy_Tab_Prompts", "RimDiplomacy_Tab_RPG" };

        public override void ExposeData()
        {
            Scribe_Values.Look(ref UseCloudProviders, "UseCloudProviders", true);
            Scribe_Collections.Look(ref CloudConfigs, "CloudConfigs", LookMode.Deep);
            Scribe_Deep.Look(ref LocalConfig, "LocalConfig");

            // Debug Settings
            Scribe_Values.Look(ref EnableDebugLogging, "EnableDebugLogging", false);
            Scribe_Values.Look(ref LogAIRequests, "LogAIRequests", true);
            Scribe_Values.Look(ref LogAIResponses, "LogAIResponses", true);
            Scribe_Values.Look(ref LogInternals, "LogInternals", false);
            Scribe_Values.Look(ref LogFullMessages, "LogFullMessages", false);

            // UI Settings
            Scribe_Values.Look(ref TypewriterSpeedMode, "TypewriterSpeedMode", TypewriterSpeedMode.Standard);

            // Comms Console Settings
            Scribe_Values.Look(ref ReplaceCommsConsole, "ReplaceCommsConsole", true);

            // RPG Dialogue Settings
            Scribe_Values.Look(ref EnableRPGDialogue, "EnableRPGDialogue", true);
            Scribe_Values.Look(ref RPGSystemPrompt, "RPGSystemPrompt", "");
            Scribe_Values.Look(ref RPGDialoguePrompt, "RPGDialoguePrompt", "");

            // Global Prompt Settings
            Scribe_Values.Look(ref GlobalSystemPrompt, "GlobalSystemPrompt", "");
            Scribe_Values.Look(ref GlobalDialoguePrompt, "GlobalDialoguePrompt", "");
            Scribe_Values.Look(ref MaxSystemPromptLength, "MaxSystemPromptLength", 2000);
            Scribe_Values.Look(ref MaxDialoguePromptLength, "MaxDialoguePromptLength", 2000);
            Scribe_Values.Look(ref MaxFactionPromptLength, "MaxFactionPromptLength", 4000);

            // AI Control Settings
            ExposeData_AI();

            // 初始化默认值
            if (CloudConfigs == null) CloudConfigs = new List<ApiConfig>();
            if (LocalConfig == null) LocalConfig = new LocalModelConfig();

            base.ExposeData();
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
                DrawTab_RPGDialogue(contentRect);
            }
            else if (selectedTab == 2)
            {
                DrawTab_PromptSettingsDirect(contentRect);
            }
            else if (selectedTab == 1)
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
                listing.CheckboxLabeled("RimDiplomacy_LogFullMessages".Translate(), ref LogFullMessages);
            }
        }



        private Vector2 promptTabScrollPosition = Vector2.zero;

        private void DrawTab_PromptSettingsDirect(Rect rect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);

            DrawAdvancedPromptSettingsSection(listing);

            listing.End();
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
            if (string.IsNullOrEmpty(url))
            {
                // 如果URL为空，直接显示自定义选项
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Custom", () => config.SelectedModel = "Custom")
                }));
                return;
            }

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
                // 先显示加载中菜单
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Loading models...", null)
                }));
                
                // 使用协程异步获取模型列表
                FetchModelsCoroutine(url, config.ApiKey, OpenMenu);
            }
        }

        private void FetchModelsCoroutine(string url, string apiKey, Action<List<string>> callback)
        {
            // 确保AIChatServiceAsync实例存在
            var service = AIChatServiceAsync.Instance;
            
            Task.Run(() =>
            {
                List<string> models = null;
                try
                {
                    using (var request = new UnityWebRequest(url, "GET"))
                    {
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                        request.timeout = 10;

                        var operation = request.SendWebRequest();
                        
                        // 使用非阻塞方式等待请求完成
                        while (!operation.isDone)
                        {
                            System.Threading.Thread.Sleep(50);
                        }

                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            models = ParseModelsFromResponse(request.downloadHandler.text);
                            ModelCache[url] = models;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimDiplomacy] Failed to fetch models: {ex.Message}");
                }
                
                // 在主线程执行回调（更新UI）
                service.ExecuteOnMainThread(() => callback(models));
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

        #region Global Prompt Settings

        /// <summary>
        /// 从Prompt文件加载默认提示词（如果设置中为空）
        /// </summary>
        private void LoadDefaultPromptsIfNeeded()
        {
            // 只在设置为空时从文件加载
            if (string.IsNullOrEmpty(GlobalSystemPrompt))
            {
                var promptConfig = PromptFileManager.LoadGlobalPrompt();
                if (promptConfig != null)
                {
                    if (!string.IsNullOrEmpty(promptConfig.SystemPrompt))
                    {
                        GlobalSystemPrompt = promptConfig.SystemPrompt;
                        Log.Message("[RimDiplomacy] 已从文件加载全局系统提示词");
                    }
                    if (!string.IsNullOrEmpty(promptConfig.DialoguePrompt))
                    {
                        GlobalDialoguePrompt = promptConfig.DialoguePrompt;
                        Log.Message("[RimDiplomacy] 已从文件加载全局对话提示词");
                    }
                }
            }
        }

        /// <summary>
        /// 保存全局提示词到文件
        /// </summary>
        private void SaveGlobalPromptsToFile()
        {
            var config = new PromptConfig
            {
                Name = "Global",
                SystemPrompt = GlobalSystemPrompt,
                DialoguePrompt = GlobalDialoguePrompt,
                Enabled = true,
                FactionId = ""
            };
            PromptFileManager.SaveGlobalPrompt(config);
        }

        /// <summary>
        /// 绘制全局提示词设置区域
        /// </summary>
        private void DrawGlobalPromptSettingsSection(Listing_Standard listing)
        {
            listing.Label("RimDiplomacy_GlobalPromptSettings".Translate());
            listing.GapLine();

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect descRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(descRect, "RimDiplomacy_GlobalPromptSettingsDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(5f);

            // 从Prompt文件加载默认提示词（如果设置中为空）
            LoadDefaultPromptsIfNeeded();

            // 初始化编辑状态
            if (string.IsNullOrEmpty(editingSystemPrompt) && !string.IsNullOrEmpty(GlobalSystemPrompt))
            {
                editingSystemPrompt = GlobalSystemPrompt;
            }
            if (string.IsNullOrEmpty(editingDialoguePrompt) && !string.IsNullOrEmpty(GlobalDialoguePrompt))
            {
                editingDialoguePrompt = GlobalDialoguePrompt;
            }

            // 初始化增强型文本框
            if (systemPromptTextArea == null)
            {
                systemPromptTextArea = new EnhancedTextArea("SystemPromptTextArea", MaxSystemPromptLength);
                systemPromptTextArea.Text = editingSystemPrompt;
                systemPromptTextArea.OnTextChanged += (newText) => editingSystemPrompt = newText;
            }
            if (dialoguePromptTextArea == null)
            {
                dialoguePromptTextArea = new EnhancedTextArea("DialoguePromptTextArea", MaxDialoguePromptLength);
                dialoguePromptTextArea.Text = editingDialoguePrompt;
                dialoguePromptTextArea.OnTextChanged += (newText) => editingDialoguePrompt = newText;
            }

            // 更新最大长度限制
            systemPromptTextArea.MaxLength = MaxSystemPromptLength;
            dialoguePromptTextArea.MaxLength = MaxDialoguePromptLength;

            // 系统提示词
            Rect sysLabelRect = listing.GetRect(24f);
            Widgets.Label(sysLabelRect, "RimDiplomacy_SystemPromptLabel".Translate());
            if (Mouse.IsOver(sysLabelRect))
            {
                TooltipHandler.TipRegion(sysLabelRect, "RimDiplomacy_SystemPromptDesc".Translate());
            }

            float sysTextHeight = 120f;
            Rect sysTextRect = listing.GetRect(sysTextHeight);
            systemPromptTextArea.Draw(sysTextRect);
            editingSystemPrompt = systemPromptTextArea.Text;

            listing.Gap(5f);

            // 对话提示词
            Rect dlgLabelRect = listing.GetRect(24f);
            Widgets.Label(dlgLabelRect, "RimDiplomacy_DialoguePromptLabel".Translate());
            if (Mouse.IsOver(dlgLabelRect))
            {
                TooltipHandler.TipRegion(dlgLabelRect, "RimDiplomacy_DialoguePromptDesc".Translate());
            }

            float dlgTextHeight = 120f;
            Rect dlgTextRect = listing.GetRect(dlgTextHeight);
            dialoguePromptTextArea.Draw(dlgTextRect);
            editingDialoguePrompt = dialoguePromptTextArea.Text;

            listing.Gap(10f);

            // 保存按钮
            Rect saveRect = listing.GetRect(28f);
            bool canSave = !systemPromptTextArea.HasExceededLimit && !dialoguePromptTextArea.HasExceededLimit;
            GUI.color = canSave ? new Color(0.3f, 0.8f, 0.3f) : Color.gray;
            if (Widgets.ButtonText(saveRect, "RimDiplomacy_SavePrompt".Translate()) && canSave)
            {
                GlobalSystemPrompt = editingSystemPrompt;
                GlobalDialoguePrompt = editingDialoguePrompt;
                // 同时保存到文件
                SaveGlobalPromptsToFile();
                Messages.Message("RimDiplomacy_PromptSaved".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制提示词长度限制设置区域
        /// </summary>
        private void DrawPromptLengthLimitSection(Listing_Standard listing)
        {
            listing.Label("RimDiplomacy_PromptLengthLimit".Translate());
            listing.GapLine();

            // 系统提示词长度限制
            listing.Label("RimDiplomacy_MaxSystemPromptLength".Translate(MaxSystemPromptLength));
            MaxSystemPromptLength = (int)listing.Slider(MaxSystemPromptLength, 500, 4000);

            // 对话提示词长度限制
            listing.Label("RimDiplomacy_MaxDialoguePromptLength".Translate(MaxDialoguePromptLength));
            MaxDialoguePromptLength = (int)listing.Slider(MaxDialoguePromptLength, 500, 4000);

            // 派系提示词长度限制
            listing.Label("RimDiplomacy_MaxPromptLength".Translate(MaxFactionPromptLength));
            MaxFactionPromptLength = (int)listing.Slider(MaxFactionPromptLength, 1000, 8000);

            // 警告提示
            Text.Font = GameFont.Tiny;
            GUI.color = Color.yellow;
            Rect warningRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(warningRect, "RimDiplomacy_PromptLengthWarning".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        #endregion

        #region Faction Prompt Settings (New)

        /// <summary>
        /// 绘制派系Prompt设置区域
        /// </summary>
        private void DrawFactionPromptSettingsSection(Listing_Standard listing)
        {
            listing.Label("RimDiplomacy_FactionPromptSettings".Translate());
            listing.GapLine();

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect descRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(descRect, "RimDiplomacy_FactionPromptSettingsDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(5f);

            // 配置文件路径显示
            string configPath = FactionPromptManager.Instance.ConfigFilePath;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Rect pathRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(pathRect, $"Config: {configPath}");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(5f);

            // 显示隐藏派系选项
            Rect toggleRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(toggleRect, "RimDiplomacy_ShowHiddenFactions".Translate(), ref showHiddenFactions);
            listing.Gap(10f);

            // 两栏布局：左侧派系列表，右侧编辑器
            float totalHeight = 420f;
            Rect mainRect = listing.GetRect(totalHeight);

            float leftWidth = mainRect.width * 0.38f;
            float rightWidth = mainRect.width * 0.6f - 10f;

            Rect leftRect = new Rect(mainRect.x, mainRect.y, leftWidth, totalHeight);
            Rect rightRect = new Rect(mainRect.x + leftWidth + 10f, mainRect.y, rightWidth, totalHeight);

            // 绘制派系列表
            DrawFactionPromptList(leftRect);

            // 绘制Prompt编辑器
            DrawFactionPromptEditor(rightRect);

            listing.Gap(10f);

            // 底部操作按钮
            DrawFactionPromptActionButtons(listing);
        }

        /// <summary>
        /// 绘制派系Prompt列表
        /// </summary>
        private void DrawFactionPromptList(Rect rect)
        {
            Widgets.DrawBox(rect);
            Rect innerRect = rect.ContractedBy(4f);

            // 标题
            Text.Font = GameFont.Small;
            Rect titleRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 24f);
            Widgets.Label(titleRect, "RimDiplomacy_FactionList".Translate());

            // 可滚动列表
            float listY = innerRect.y + 28f;
            Rect listRect = new Rect(innerRect.x, listY, innerRect.width, innerRect.height - 28f);

            var configs = FactionPromptManager.Instance.AllConfigs;

            if (configs.Count == 0)
            {
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(listRect, "RimDiplomacy_NoFactionConfigs".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            float rowHeight = 30f;
            float totalListHeight = Mathf.Max(configs.Count * rowHeight, listRect.height);
            Rect viewRect = new Rect(0, 0, listRect.width - 16f, totalListHeight);

            Widgets.BeginScrollView(listRect, ref factionListScrollPosition, viewRect);

            float y = 0f;
            for (int i = 0; i < configs.Count; i++)
            {
                var config = configs[i];
                if (!showHiddenFactions && IsHiddenFaction(config.FactionDefName))
                {
                    continue;
                }

                Rect rowRect = new Rect(0, y, viewRect.width, rowHeight);

                // 选中高亮
                if (selectedFactionDefName == config.FactionDefName)
                {
                    Widgets.DrawHighlightSelected(rowRect);
                }
                else if (i % 2 == 0)
                {
                    Widgets.DrawLightHighlight(rowRect);
                }

                // 点击选择
                if (Widgets.ButtonInvisible(rowRect))
                {
                    selectedFactionDefName = config.FactionDefName;
                    editingCustomPrompt = config.CustomPrompt ?? "";
                    editingUseCustomPrompt = config.UseCustomPrompt;
                }

                float xOffset = 4f;

                // 自定义指示器
                if (config.UseCustomPrompt)
                {
                    Rect customRect = new Rect(xOffset, y + 8f, 14f, 14f);
                    GUI.color = new Color(0.3f, 0.8f, 0.3f);
                    Widgets.DrawBoxSolid(customRect, GUI.color);
                    GUI.color = Color.white;
                    xOffset += 20f;
                }

                // 派系名称
                Rect nameRect = new Rect(xOffset, y, viewRect.width - xOffset - 10f, rowHeight);
                Text.Anchor = TextAnchor.MiddleLeft;
                string displayName = string.IsNullOrEmpty(config.DisplayName) ? config.FactionDefName : config.DisplayName;
                Widgets.Label(nameRect, displayName.Truncate(nameRect.width));
                Text.Anchor = TextAnchor.UpperLeft;

                y += rowHeight;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// 判断是否为隐藏派系
        /// </summary>
        private bool IsHiddenFaction(string factionDefName)
        {
            var def = DefDatabase<FactionDef>.GetNamedSilentFail(factionDefName);
            if (def == null) return false;
            // 通过反射获取Hidden属性
            try
            {
                var hiddenField = typeof(FactionDef).GetField("hidden");
                if (hiddenField != null)
                {
                    return (bool)hiddenField.GetValue(def);
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 绘制派系Prompt编辑器
        /// </summary>
        private void DrawFactionPromptEditor(Rect rect)
        {
            Widgets.DrawBox(rect);
            Rect innerRect = rect.ContractedBy(6f);

            if (string.IsNullOrEmpty(selectedFactionDefName))
            {
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(innerRect, "RimDiplomacy_SelectFactionForPrompt".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            var config = FactionPromptManager.Instance.GetConfig(selectedFactionDefName);
            if (config == null)
            {
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(innerRect, "RimDiplomacy_FactionConfigNotFound".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            float y = innerRect.y;

            // 派系名称标题
            Text.Font = GameFont.Medium;
            Rect headerRect = new Rect(innerRect.x, y, innerRect.width, 28f);
            string displayName = string.IsNullOrEmpty(config.DisplayName) ? config.FactionDefName : config.DisplayName;
            Widgets.Label(headerRect, displayName);
            Text.Font = GameFont.Small;
            y += 32f;

            // 使用自定义Prompt选项
            Rect checkboxRect = new Rect(innerRect.x, y, innerRect.width, 24f);
            bool prevUseCustom = editingUseCustomPrompt;
            Widgets.CheckboxLabeled(checkboxRect, "RimDiplomacy_UseCustomPrompt".Translate(), ref editingUseCustomPrompt);
            if (prevUseCustom != editingUseCustomPrompt)
            {
                config.UseCustomPrompt = editingUseCustomPrompt;
                FactionPromptManager.Instance.UpdateConfig(config);
            }
            y += 28f;

            // 分隔线
            Rect lineRect = new Rect(innerRect.x, y, innerRect.width, 2f);
            Widgets.DrawBoxSolid(lineRect, new Color(0.3f, 0.3f, 0.3f, 0.5f));
            y += 8f;

            if (editingUseCustomPrompt)
            {
                // 编辑自定义Prompt
                DrawCustomPromptEditor(innerRect, ref y, config);
            }
            else
            {
                // 显示默认Prompt详情
                DrawDefaultPromptViewer(innerRect, ref y, config);
            }
        }

        /// <summary>
        /// 绘制自定义Prompt编辑器
        /// </summary>
        private void DrawCustomPromptEditor(Rect innerRect, ref float y, FactionPromptConfig config)
        {
            // 初始化派系提示词文本框
            if (factionPromptTextArea == null || factionPromptTextArea.Text != editingCustomPrompt)
            {
                factionPromptTextArea = new EnhancedTextArea($"FactionPrompt_{config.FactionDefName}", MaxFactionPromptLength);
                factionPromptTextArea.Text = editingCustomPrompt;
                factionPromptTextArea.OnTextChanged += (newText) => editingCustomPrompt = newText;
            }
            factionPromptTextArea.MaxLength = MaxFactionPromptLength;

            // 文本编辑区域
            float textHeight = innerRect.yMax - y - 70f;
            Rect textRect = new Rect(innerRect.x, y, innerRect.width, textHeight);
            factionPromptTextArea.Draw(textRect);
            editingCustomPrompt = factionPromptTextArea.Text;
            y += textHeight + 8f;

            // 按钮行
            float btnWidth = (innerRect.width - 20f) / 3;

            // 保存按钮
            Rect saveRect = new Rect(innerRect.x, y, btnWidth, 28f);
            bool canSave = !factionPromptTextArea.HasExceededLimit;
            GUI.color = canSave ? new Color(0.3f, 0.8f, 0.3f) : Color.gray;
            if (Widgets.ButtonText(saveRect, "RimDiplomacy_SavePrompt".Translate()) && canSave)
            {
                config.ApplyCustomPrompt(editingCustomPrompt);
                FactionPromptManager.Instance.UpdateConfig(config);
                Messages.Message("RimDiplomacy_PromptSaved".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }
            GUI.color = Color.white;

            // 重置为默认按钮
            Rect resetRect = new Rect(innerRect.x + btnWidth + 10f, y, btnWidth, 28f);
            if (Widgets.ButtonText(resetRect, "RimDiplomacy_ResetToDefault".Translate()))
            {
                ShowResetPromptConfirmation(config);
            }

            // 查看默认按钮
            Rect viewRect = new Rect(innerRect.x + btnWidth * 2 + 20f, y, btnWidth, 28f);
            if (Widgets.ButtonText(viewRect, "RimDiplomacy_ViewDefault".Translate()))
            {
                string defaultPrompt = config.BuildPromptFromTemplate();
                Find.WindowStack.Add(new Dialog_MessageBox(
                    defaultPrompt,
                    "OK".Translate(),
                    null,
                    null,
                    null,
                    null,
                    false,
                    null,
                    null,
                    WindowLayer.Dialog
                ));
            }
        }

        /// <summary>
        /// 绘制默认 Prompt 查看器
        /// </summary>
        private void DrawDefaultPromptViewer(Rect innerRect, ref float y, FactionPromptConfig config)
        {
            // 各特征显示
            float sectionHeight = 60f;

            // 核心风格
            DrawPromptFeature(innerRect, ref y, "RimDiplomacy_CoreStyle".Translate(), config.GetFieldValue("核心风格"), sectionHeight);

            // 用词特征
            DrawPromptFeature(innerRect, ref y, "RimDiplomacy_VocabularyFeatures".Translate(), config.GetFieldValue("用词特征"), sectionHeight);

            // 语气特征
            DrawPromptFeature(innerRect, ref y, "RimDiplomacy_ToneFeatures".Translate(), config.GetFieldValue("语气特征"), sectionHeight);

            // 句式特征
            DrawPromptFeature(innerRect, ref y, "RimDiplomacy_SentenceFeatures".Translate(), config.GetFieldValue("句式特征"), sectionHeight);

            // 表达禁忌
            DrawPromptFeature(innerRect, ref y, "RimDiplomacy_Taboos".Translate(), config.GetFieldValue("表达禁忌"), sectionHeight);

            // 按钮行
            float btnWidth = (innerRect.width - 20f) / 2;
            float btnY = innerRect.yMax - 34f;

            // 编辑模板按钮
            Rect editTemplateRect = new Rect(innerRect.x, btnY, btnWidth, 28f);
            if (Widgets.ButtonText(editTemplateRect, "编辑模板"))
            {
                Find.WindowStack.Add(new Dialog_FactionPromptEditor(config));
            }

            // 预览按钮
            Rect previewRect = new Rect(innerRect.x + btnWidth + 10f, btnY, btnWidth, 28f);
            if (Widgets.ButtonText(previewRect, "RimDiplomacy_PreviewPrompt".Translate()))
            {
                string fullPrompt = config.GetEffectivePrompt();
                Find.WindowStack.Add(new Dialog_MessageBox(
                    fullPrompt,
                    "确定",
                    null,
                    null,
                    null,
                    null,
                    false,
                    null,
                    null,
                    WindowLayer.Dialog
                ));
            }
        }

        /// <summary>
        /// 绘制Prompt特征项
        /// </summary>
        private void DrawPromptFeature(Rect innerRect, ref float y, string label, string content, float height)
        {
            // 标签
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Rect labelRect = new Rect(innerRect.x, y, innerRect.width, Text.LineHeight);
            Widgets.Label(labelRect, label);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += Text.LineHeight + 2f;

            // 内容框
            Rect contentRect = new Rect(innerRect.x, y, innerRect.width, height);
            Widgets.DrawBoxSolid(contentRect, new Color(0.1f, 0.1f, 0.1f, 0.3f));

            GUI.color = new Color(0.9f, 0.9f, 0.9f);
            Text.Font = GameFont.Tiny;
            Rect textRect = contentRect.ContractedBy(4f);
            Widgets.Label(textRect, content ?? "");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            y += height + 6f;
        }

        /// <summary>
        /// 显示重置Prompt确认对话框
        /// </summary>
        private void ShowResetPromptConfirmation(FactionPromptConfig config)
        {
            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(
                "RimDiplomacy_ResetPromptConfirm".Translate(config.DisplayName),
                () =>
                {
                    config.ResetToDefault();
                    editingCustomPrompt = "";
                    editingUseCustomPrompt = false;
                    FactionPromptManager.Instance.UpdateConfig(config);
                    Messages.Message("RimDiplomacy_PromptReset".Translate(), MessageTypeDefOf.NeutralEvent, false);
                },
                true,
                "RimDiplomacy_ResetConfirmTitle".Translate()
            );
            Find.WindowStack.Add(dialog);
        }

        /// <summary>
        /// 绘制派系Prompt操作按钮
        /// </summary>
        private void DrawFactionPromptActionButtons(Listing_Standard listing)
        {
            Rect buttonRowRect = listing.GetRect(28f);
            float btnWidth = (buttonRowRect.width - 20f) / 3;

            // 导出配置按钮
            Rect exportRect = new Rect(buttonRowRect.x, buttonRowRect.y, btnWidth, buttonRowRect.height);
            if (Widgets.ButtonText(exportRect, "RimDiplomacy_ExportPrompts".Translate()))
            {
                ShowExportPromptsDialog();
            }

            // 导入配置按钮
            Rect importRect = new Rect(buttonRowRect.x + btnWidth + 10f, buttonRowRect.y, btnWidth, buttonRowRect.height);
            if (Widgets.ButtonText(importRect, "RimDiplomacy_ImportPrompts".Translate()))
            {
                ShowImportPromptsDialog();
            }

            // 重置所有按钮
            Rect resetAllRect = new Rect(buttonRowRect.x + btnWidth * 2 + 20f, buttonRowRect.y, btnWidth, buttonRowRect.height);
            GUI.color = new Color(1f, 0.6f, 0.6f);
            if (Widgets.ButtonText(resetAllRect, "RimDiplomacy_ResetAllPrompts".Translate()))
            {
                ShowResetAllPromptsConfirmation();
            }
            GUI.color = Color.white;
        }

        /// <summary>
        /// 显示导出Prompts对话框
        /// </summary>
        private void ShowExportPromptsDialog()
        {
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RimDiplomacy_Prompts.json");
            Find.WindowStack.Add(new Dialog_SaveFile(defaultPath, (path) =>
            {
                if (FactionPromptManager.Instance.ExportConfigs(path))
                {
                    Messages.Message("RimDiplomacy_ExportSuccess".Translate(path), MessageTypeDefOf.NeutralEvent, false);
                }
                else
                {
                    Messages.Message("RimDiplomacy_ExportFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                }
            }));
        }

        /// <summary>
        /// 显示导入Prompts对话框
        /// </summary>
        private void ShowImportPromptsDialog()
        {
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RimDiplomacy_Prompts.json");
            Find.WindowStack.Add(new Dialog_LoadFile(defaultPath, (path) =>
            {
                if (FactionPromptManager.Instance.ImportConfigs(path))
                {
                    // 刷新编辑状态
                    if (!string.IsNullOrEmpty(selectedFactionDefName))
                    {
                        var config = FactionPromptManager.Instance.GetConfig(selectedFactionDefName);
                        if (config != null)
                        {
                            editingCustomPrompt = config.CustomPrompt ?? "";
                            editingUseCustomPrompt = config.UseCustomPrompt;
                        }
                    }
                    Messages.Message("RimDiplomacy_ImportSuccess".Translate(), MessageTypeDefOf.NeutralEvent, false);
                }
                else
                {
                    Messages.Message("RimDiplomacy_ImportFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                }
            }));
        }

        /// <summary>
        /// 显示重置所有Prompts确认对话框
        /// </summary>
        private void ShowResetAllPromptsConfirmation()
        {
            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(
                "RimDiplomacy_ResetAllPromptsConfirm".Translate(),
                () =>
                {
                    FactionPromptManager.Instance.ResetAllConfigs();
                    editingCustomPrompt = "";
                    editingUseCustomPrompt = false;
                    selectedFactionDefName = null;
                    Messages.Message("RimDiplomacy_AllPromptsReset".Translate(), MessageTypeDefOf.NeutralEvent, false);
                },
                true,
                "RimDiplomacy_ResetConfirmTitle".Translate()
            );
            Find.WindowStack.Add(dialog);
        }

        /// <summary>
        /// 估算Token数量
        /// </summary>
        private int EstimateTokenCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            // 粗略估算：中英文混合约4字符/Token
            return text.Length / 4;
        }

        #endregion


    }
}

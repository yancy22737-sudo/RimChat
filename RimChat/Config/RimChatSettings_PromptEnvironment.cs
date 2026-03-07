using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimChat.Persistence;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    public partial class RimChatSettings
    {
        private Vector2 _envPageScroll = Vector2.zero;
        private Vector2 _envSceneListScroll = Vector2.zero;
        private Vector2 _envSceneContentScroll = Vector2.zero;
        private Vector2 _envPreviewScroll = Vector2.zero;
        private int _selectedEnvironmentSceneIndex = -1;
        private string _selectedEnvironmentSceneId = string.Empty;
        private string _sceneTagsBuffer = string.Empty;
        private string _scenePriorityBuffer = "0";
        private string _environmentPreviewCache = string.Empty;
        private int _environmentPreviewCooldown = 0;

        private const float EnvCardGap = 10f;
        private static readonly Color EnvCardBg = new Color(0.08f, 0.08f, 0.10f);
        private static readonly Color EnvSectionBg = new Color(0.10f, 0.11f, 0.14f);

        private void DrawEnvironmentPromptsEditorScrollable(Rect rect)
        {
            EnsureEnvironmentPromptConfig();
            EnvironmentPromptConfig envConfig = SystemPromptConfigData.EnvironmentPrompt;

            Widgets.DrawBoxSolid(rect, EnvSectionBg);
            Widgets.DrawBox(rect);

            Rect innerRect = rect.ContractedBy(8f);
            float contentHeight = CalculateEnvironmentPageHeight(envConfig);
            Rect viewRect = new Rect(0f, 0f, innerRect.width - 18f, contentHeight);
            _envPageScroll = GUI.BeginScrollView(innerRect, _envPageScroll, viewRect);

            float y = 0f;
            bool changed = false;

            Rect worldviewRect = new Rect(0f, y, viewRect.width, GetWorldviewCardHeight(envConfig));
            changed |= DrawEnvironmentWorldviewCard(worldviewRect, envConfig);
            y += worldviewRect.height + EnvCardGap;

            Rect sceneSystemRect = new Rect(0f, y, viewRect.width, GetSceneSystemCardHeight(envConfig));
            changed |= DrawEnvironmentSceneSystemCard(sceneSystemRect, envConfig);
            y += sceneSystemRect.height + EnvCardGap;

            Rect environmentParamsRect = new Rect(0f, y, viewRect.width, GetEnvironmentContextCardHeight(envConfig));
            changed |= DrawEnvironmentContextCard(environmentParamsRect, envConfig);
            y += environmentParamsRect.height + EnvCardGap;

            Rect eventIntelRect = new Rect(0f, y, viewRect.width, GetEventIntelCardHeight(envConfig));
            changed |= DrawEnvironmentEventIntelCard(eventIntelRect, envConfig);
            y += eventIntelRect.height + EnvCardGap;

            Rect sceneEntriesRect = new Rect(0f, y, viewRect.width, 390f);
            changed |= DrawEnvironmentSceneEntriesCard(sceneEntriesRect, envConfig);
            y += sceneEntriesRect.height + EnvCardGap;

            Rect rpgSwitchesRect = new Rect(0f, y, viewRect.width, GetRpgSwitchesCardHeight());
            changed |= DrawEnvironmentRpgSwitchesCard(rpgSwitchesRect, envConfig);
            y += rpgSwitchesRect.height + EnvCardGap;

            Rect previewRect = new Rect(0f, y, viewRect.width, 260f);
            DrawEnvironmentPreviewCard(previewRect);

            GUI.EndScrollView();

            if (changed)
            {
                _environmentPreviewCooldown = 0;
            }
        }

        private float CalculateEnvironmentPageHeight(EnvironmentPromptConfig config)
        {
            return GetWorldviewCardHeight(config)
                + GetSceneSystemCardHeight(config)
                + GetEnvironmentContextCardHeight(config)
                + GetEventIntelCardHeight(config)
                + 390f
                + GetRpgSwitchesCardHeight()
                + 260f
                + EnvCardGap * 7f;
        }

        private float GetWorldviewCardHeight(EnvironmentPromptConfig config)
        {
            return config.Worldview.Enabled ? 198f : 98f;
        }

        private float GetSceneSystemCardHeight(EnvironmentPromptConfig config)
        {
            return config.SceneSystem.Enabled ? 172f : 98f;
        }

        private float GetEnvironmentContextCardHeight(EnvironmentPromptConfig config)
        {
            return config.EnvironmentContextSwitches.Enabled ? 228f : 98f;
        }

        private float GetEventIntelCardHeight(EnvironmentPromptConfig config)
        {
            return config.EventIntelPrompt?.Enabled == true ? 320f : 100f;
        }

        private float GetRpgSwitchesCardHeight()
        {
            return 236f;
        }

        private Rect DrawEnvironmentCard(Rect rect, string titleKey)
        {
            Widgets.DrawBoxSolid(rect, EnvCardBg);
            Widgets.DrawBox(rect);
            GUI.color = new Color(0.95f, 0.78f, 0.45f);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 22f), titleKey.Translate());
            GUI.color = Color.white;
            Widgets.DrawLineHorizontal(rect.x + 8f, rect.y + 28f, rect.width - 16f);
            return new Rect(rect.x + 8f, rect.y + 34f, rect.width - 16f, rect.height - 40f);
        }

        private void EnsureEnvironmentPromptConfig()
        {
            if (SystemPromptConfigData.EnvironmentPrompt == null)
            {
                SystemPromptConfigData.EnvironmentPrompt = new EnvironmentPromptConfig();
            }

            if (SystemPromptConfigData.EnvironmentPrompt.Worldview == null)
            {
                SystemPromptConfigData.EnvironmentPrompt.Worldview = new WorldviewPromptConfig();
            }

            if (SystemPromptConfigData.EnvironmentPrompt.SceneSystem == null)
            {
                SystemPromptConfigData.EnvironmentPrompt.SceneSystem = new SceneSystemPromptConfig();
            }

            if (SystemPromptConfigData.EnvironmentPrompt.SceneEntries == null)
            {
                SystemPromptConfigData.EnvironmentPrompt.SceneEntries = new List<ScenePromptEntryConfig>();
            }

            if (SystemPromptConfigData.EnvironmentPrompt.EnvironmentContextSwitches == null)
            {
                SystemPromptConfigData.EnvironmentPrompt.EnvironmentContextSwitches = new EnvironmentContextSwitchesConfig();
            }

            if (SystemPromptConfigData.EnvironmentPrompt.RpgSceneParamSwitches == null)
            {
                SystemPromptConfigData.EnvironmentPrompt.RpgSceneParamSwitches = new RpgSceneParamSwitchesConfig();
            }

            if (SystemPromptConfigData.EnvironmentPrompt.EventIntelPrompt == null)
            {
                SystemPromptConfigData.EnvironmentPrompt.EventIntelPrompt = new EventIntelPromptConfig();
            }
        }

        private bool DrawEnvironmentWorldviewCard(Rect rect, EnvironmentPromptConfig envConfig)
        {
            Rect contentRect = DrawEnvironmentCard(rect, "RimChat_EnvironmentWorldviewLabel");
            bool changed = false;
            bool oldEnabled = envConfig.Worldview.Enabled;

            Widgets.CheckboxLabeled(
                new Rect(contentRect.x, contentRect.y, contentRect.width, 24f),
                "RimChat_EnvironmentWorldviewEnabled".Translate(),
                ref envConfig.Worldview.Enabled);
            changed |= oldEnabled != envConfig.Worldview.Enabled;

            int limit = Mathf.Max(MaxSystemPromptLength, 500);
            int length = envConfig.Worldview.Content?.Length ?? 0;
            GUI.color = length > limit * 0.9f ? Color.yellow : Color.gray;
            Widgets.Label(
                new Rect(contentRect.x, contentRect.y + 28f, contentRect.width, 18f),
                "RimChat_EnvironmentCharCount".Translate(length, limit));
            GUI.color = Color.white;

            if (!envConfig.Worldview.Enabled)
            {
                return changed;
            }

            Rect textRect = new Rect(contentRect.x, contentRect.y + 48f, contentRect.width, contentRect.height - 52f);
            Widgets.DrawBoxSolid(textRect, new Color(0.05f, 0.05f, 0.07f));
            string oldText = envConfig.Worldview.Content ?? string.Empty;
            string newText = Widgets.TextArea(textRect.ContractedBy(4f), oldText);
            if (newText.Length > limit)
            {
                newText = newText.Substring(0, limit);
            }

            if (!string.Equals(oldText, newText, StringComparison.Ordinal))
            {
                envConfig.Worldview.Content = newText;
                changed = true;
            }

            return changed;
        }

        private bool DrawEnvironmentSceneSystemCard(Rect rect, EnvironmentPromptConfig envConfig)
        {
            Rect contentRect = DrawEnvironmentCard(rect, "RimChat_EnvironmentSceneSystemLabel");
            bool changed = false;
            float y = contentRect.y;

            bool oldEnabled = envConfig.SceneSystem.Enabled;
            Widgets.CheckboxLabeled(
                new Rect(contentRect.x, y, contentRect.width, 24f),
                "RimChat_EnvironmentSceneSystemEnabled".Translate(),
                ref envConfig.SceneSystem.Enabled);
            changed |= oldEnabled != envConfig.SceneSystem.Enabled;
            y += 26f;

            if (!envConfig.SceneSystem.Enabled)
            {
                return changed;
            }

            bool oldPreset = envConfig.SceneSystem.PresetTagsEnabled;
            Widgets.CheckboxLabeled(
                new Rect(contentRect.x, y, contentRect.width, 24f),
                "RimChat_EnvironmentPresetTagsEnabled".Translate(),
                ref envConfig.SceneSystem.PresetTagsEnabled);
            changed |= oldPreset != envConfig.SceneSystem.PresetTagsEnabled;
            y += 28f;

            changed |= DrawEnvironmentIntSlider(
                contentRect,
                ref y,
                ref envConfig.SceneSystem.MaxSceneChars,
                200,
                4000,
                "RimChat_EnvironmentMaxSceneChars");

            changed |= DrawEnvironmentIntSlider(
                contentRect,
                ref y,
                ref envConfig.SceneSystem.MaxTotalChars,
                500,
                12000,
                "RimChat_EnvironmentMaxTotalChars");

            return changed;
        }

        private bool DrawEnvironmentIntSlider(
            Rect contentRect,
            ref float y,
            ref int value,
            int min,
            int max,
            string labelKey)
        {
            Widgets.Label(
                new Rect(contentRect.x, y, contentRect.width, 20f),
                labelKey.Translate(value));
            y += 20f;

            int oldValue = value;
            value = Mathf.RoundToInt(Widgets.HorizontalSlider(
                new Rect(contentRect.x, y, contentRect.width, 22f),
                value,
                min,
                max));
            y += 28f;
            return oldValue != value;
        }

        private bool DrawEnvironmentContextCard(Rect rect, EnvironmentPromptConfig envConfig)
        {
            Rect contentRect = DrawEnvironmentCard(rect, "RimChat_EnvironmentContextLabel");
            bool changed = false;
            EnvironmentContextSwitchesConfig switches = envConfig.EnvironmentContextSwitches;

            bool oldEnabled = switches.Enabled;
            Widgets.CheckboxLabeled(
                new Rect(contentRect.x, contentRect.y, contentRect.width, 24f),
                "RimChat_EnvironmentContextEnabled".Translate(),
                ref switches.Enabled);
            changed |= oldEnabled != switches.Enabled;

            if (!switches.Enabled)
            {
                return changed;
            }

            float colGap = 16f;
            float colWidth = (contentRect.width - colGap) / 2f;
            Rect leftCol = new Rect(contentRect.x, contentRect.y + 30f, colWidth, contentRect.height - 32f);
            Rect rightCol = new Rect(leftCol.xMax + colGap, leftCol.y, colWidth, leftCol.height);

            changed |= DrawEnvironmentContextLeftColumn(leftCol, switches);
            changed |= DrawEnvironmentContextRightColumn(rightCol, switches);
            return changed;
        }

        private bool DrawEnvironmentContextLeftColumn(Rect rect, EnvironmentContextSwitchesConfig switches)
        {
            bool changed = false;
            float y = rect.y;
            changed |= DrawEnvironmentCheckbox(new Rect(rect.x, y, rect.width, 24f), "RimChat_EnvironmentContextTime", ref switches.IncludeTime); y += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(rect.x, y, rect.width, 24f), "RimChat_EnvironmentContextDate", ref switches.IncludeDate); y += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(rect.x, y, rect.width, 24f), "RimChat_EnvironmentContextSeason", ref switches.IncludeSeason); y += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(rect.x, y, rect.width, 24f), "RimChat_EnvironmentContextWeather", ref switches.IncludeWeather); y += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(rect.x, y, rect.width, 24f), "RimChat_EnvironmentContextLocationTemperature", ref switches.IncludeLocationAndTemperature);
            return changed;
        }

        private bool DrawEnvironmentContextRightColumn(Rect rect, EnvironmentContextSwitchesConfig switches)
        {
            bool changed = false;
            float y = rect.y;
            changed |= DrawEnvironmentCheckbox(new Rect(rect.x, y, rect.width, 24f), "RimChat_EnvironmentContextTerrain", ref switches.IncludeTerrain); y += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(rect.x, y, rect.width, 24f), "RimChat_EnvironmentContextBeauty", ref switches.IncludeBeauty); y += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(rect.x, y, rect.width, 24f), "RimChat_EnvironmentContextCleanliness", ref switches.IncludeCleanliness); y += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(rect.x, y, rect.width, 24f), "RimChat_EnvironmentContextSurroundings", ref switches.IncludeSurroundings); y += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(rect.x, y, rect.width, 24f), "RimChat_EnvironmentContextWealth", ref switches.IncludeWealth);
            return changed;
        }

        private bool DrawEnvironmentCheckbox(Rect rect, string key, ref bool value)
        {
            bool oldValue = value;
            Widgets.CheckboxLabeled(rect, key.Translate(), ref value);
            return oldValue != value;
        }

        private bool DrawEnvironmentSceneEntriesCard(Rect rect, EnvironmentPromptConfig envConfig)
        {
            Rect contentRect = DrawEnvironmentCard(rect, "RimChat_EnvironmentSceneEntriesLabel");
            float listWidth = Mathf.Min(250f, contentRect.width * 0.38f);
            Rect listRect = new Rect(contentRect.x, contentRect.y, listWidth, contentRect.height);
            Rect editorRect = new Rect(listRect.xMax + 10f, contentRect.y, contentRect.width - listWidth - 10f, contentRect.height);

            bool changed = false;
            changed |= DrawEnvironmentSceneList(listRect, envConfig);
            changed |= DrawEnvironmentSceneEditor(editorRect, envConfig);
            return changed;
        }

        private bool DrawEnvironmentEventIntelCard(Rect rect, EnvironmentPromptConfig envConfig)
        {
            Rect contentRect = DrawEnvironmentCard(rect, "RimChat_EnvironmentEventIntelLabel");
            EventIntelPromptConfig intel = envConfig.EventIntelPrompt;
            bool changed = false;
            float y = contentRect.y;

            bool oldEnabled = intel.Enabled;
            Widgets.CheckboxLabeled(
                new Rect(contentRect.x, y, contentRect.width, 24f),
                "RimChat_EnvironmentEventIntelEnabled".Translate(),
                ref intel.Enabled);
            changed |= oldEnabled != intel.Enabled;
            y += 26f;

            if (!intel.Enabled)
            {
                return changed;
            }

            changed |= DrawEnvironmentCheckbox(
                new Rect(contentRect.x, y, contentRect.width * 0.5f, 24f),
                "RimChat_EnvironmentEventIntelApplyDiplomacy",
                ref intel.ApplyToDiplomacy);
            changed |= DrawEnvironmentCheckbox(
                new Rect(contentRect.x + contentRect.width * 0.5f, y, contentRect.width * 0.5f, 24f),
                "RimChat_EnvironmentEventIntelApplyRpg",
                ref intel.ApplyToRpg);
            y += 24f;

            changed |= DrawEnvironmentCheckbox(
                new Rect(contentRect.x, y, contentRect.width * 0.5f, 24f),
                "RimChat_EnvironmentEventIntelIncludeMapEvents",
                ref intel.IncludeMapEvents);
            changed |= DrawEnvironmentCheckbox(
                new Rect(contentRect.x + contentRect.width * 0.5f, y, contentRect.width * 0.5f, 24f),
                "RimChat_EnvironmentEventIntelIncludeRaidReports",
                ref intel.IncludeRaidBattleReports);
            y += 28f;

            changed |= DrawEnvironmentIntSlider(
                contentRect,
                ref y,
                ref intel.DaysWindow,
                1,
                30,
                "RimChat_EnvironmentEventIntelDaysWindow");

            changed |= DrawEnvironmentIntSlider(
                contentRect,
                ref y,
                ref intel.MaxStoredRecords,
                20,
                200,
                "RimChat_EnvironmentEventIntelMaxStored");

            changed |= DrawEnvironmentIntSlider(
                contentRect,
                ref y,
                ref intel.MaxInjectedItems,
                1,
                20,
                "RimChat_EnvironmentEventIntelMaxItems");

            changed |= DrawEnvironmentIntSlider(
                contentRect,
                ref y,
                ref intel.MaxInjectedChars,
                200,
                4000,
                "RimChat_EnvironmentEventIntelMaxChars");

            return changed;
        }

        private bool DrawEnvironmentSceneList(Rect rect, EnvironmentPromptConfig envConfig)
        {
            bool changed = false;
            float buttonWidth = (rect.width - 6f) / 2f;
            Rect addRect = new Rect(rect.x, rect.y, buttonWidth, 24f);
            if (Widgets.ButtonText(addRect, "RimChat_EnvironmentAddScene".Translate()))
            {
                envConfig.SceneEntries.Add(new ScenePromptEntryConfig
                {
                    Name = "RimChat_EnvironmentNewSceneName".Translate().ToString(),
                    Priority = 10,
                    MatchTags = new List<string> { "channel:diplomacy", "scene:social" }
                });
                _selectedEnvironmentSceneIndex = envConfig.SceneEntries.Count - 1;
                SyncEnvironmentSelection(envConfig);
                changed = true;
            }

            bool hasSelection = _selectedEnvironmentSceneIndex >= 0 && _selectedEnvironmentSceneIndex < envConfig.SceneEntries.Count;
            Rect removeRect = new Rect(addRect.xMax + 6f, rect.y, buttonWidth, 24f);
            if (Widgets.ButtonText(removeRect, "RimChat_EnvironmentRemoveScene".Translate(), active: hasSelection))
            {
                envConfig.SceneEntries.RemoveAt(_selectedEnvironmentSceneIndex);
                _selectedEnvironmentSceneIndex = Mathf.Clamp(_selectedEnvironmentSceneIndex - 1, -1, envConfig.SceneEntries.Count - 1);
                SyncEnvironmentSelection(envConfig);
                changed = true;
            }

            Rect listRect = new Rect(rect.x, rect.y + 28f, rect.width, rect.height - 28f);
            float contentHeight = Mathf.Max(listRect.height, envConfig.SceneEntries.Count * 30f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, contentHeight);
            _envSceneListScroll = GUI.BeginScrollView(listRect, _envSceneListScroll, viewRect);

            for (int i = 0; i < envConfig.SceneEntries.Count; i++)
            {
                ScenePromptEntryConfig entry = envConfig.SceneEntries[i];
                Rect rowRect = new Rect(0f, i * 30f, viewRect.width, 26f);
                bool selected = i == _selectedEnvironmentSceneIndex;
                if (selected)
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.23f, 0.34f, 0.55f, 0.9f));
                }
                else if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.16f, 0.18f, 0.22f));
                }

                string name = string.IsNullOrWhiteSpace(entry?.Name) ? "UnnamedScene" : entry.Name;
                string channel = entry.ApplyToDiplomacy && entry.ApplyToRPG
                    ? "D+R"
                    : entry.ApplyToDiplomacy ? "D" : entry.ApplyToRPG ? "R" : "-";
                string label = $"{name} [{channel}] P:{entry.Priority}";
                Widgets.Label(new Rect(rowRect.x + 4f, rowRect.y + 3f, rowRect.width - 8f, rowRect.height), label);

                if (Widgets.ButtonInvisible(rowRect))
                {
                    _selectedEnvironmentSceneIndex = i;
                    SyncEnvironmentSelection(envConfig);
                }
            }

            GUI.EndScrollView();
            return changed;
        }

        private bool DrawEnvironmentSceneEditor(Rect rect, EnvironmentPromptConfig envConfig)
        {
            if (_selectedEnvironmentSceneIndex < 0 || _selectedEnvironmentSceneIndex >= envConfig.SceneEntries.Count)
            {
                GUI.color = Color.gray;
                Widgets.Label(rect, "RimChat_EnvironmentSelectSceneHint".Translate());
                GUI.color = Color.white;
                return false;
            }

            ScenePromptEntryConfig entry = envConfig.SceneEntries[_selectedEnvironmentSceneIndex];
            if (entry == null)
            {
                return false;
            }

            if (!string.Equals(_selectedEnvironmentSceneId, entry.Id, StringComparison.Ordinal))
            {
                SyncEnvironmentSelection(envConfig);
            }

            bool changed = false;
            float y = rect.y;

            Widgets.Label(new Rect(rect.x, y, rect.width, 18f), "RimChat_EnvironmentSceneNameLabel".Translate());
            y += 18f;
            string oldName = entry.Name ?? string.Empty;
            string newName = Widgets.TextField(new Rect(rect.x, y, rect.width, 24f), oldName);
            if (!string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                entry.Name = newName;
                changed = true;
            }
            y += 28f;

            Widgets.Label(new Rect(rect.x, y, 90f, 18f), "RimChat_EnvironmentPriorityLabel".Translate());
            y += 18f;
            string oldPriorityBuffer = _scenePriorityBuffer ?? "0";
            _scenePriorityBuffer = Widgets.TextField(new Rect(rect.x, y, 90f, 24f), oldPriorityBuffer);
            if (int.TryParse(_scenePriorityBuffer, out int parsedPriority))
            {
                parsedPriority = Mathf.Clamp(parsedPriority, -999, 999);
                if (entry.Priority != parsedPriority)
                {
                    entry.Priority = parsedPriority;
                    changed = true;
                }
            }

            float rightX = rect.x + 110f;
            changed |= DrawEnvironmentCheckbox(
                new Rect(rightX, y, rect.width - 110f, 24f),
                "RimChat_EnvironmentEntryEnabled",
                ref entry.Enabled);
            y += 24f;
            changed |= DrawEnvironmentCheckbox(
                new Rect(rightX, y, rect.width - 110f, 24f),
                "RimChat_EnvironmentApplyDiplomacy",
                ref entry.ApplyToDiplomacy);
            y += 24f;
            changed |= DrawEnvironmentCheckbox(
                new Rect(rightX, y, rect.width - 110f, 24f),
                "RimChat_EnvironmentApplyRPG",
                ref entry.ApplyToRPG);
            y += 28f;

            Widgets.Label(new Rect(rect.x, y, rect.width, 18f), "RimChat_EnvironmentSceneTagsLabel".Translate());
            y += 18f;
            string oldTagsBuffer = _sceneTagsBuffer ?? string.Empty;
            _sceneTagsBuffer = Widgets.TextField(new Rect(rect.x, y, rect.width, 24f), oldTagsBuffer);
            if (!string.Equals(oldTagsBuffer, _sceneTagsBuffer, StringComparison.Ordinal))
            {
                entry.MatchTags = ParseTagCsv(_sceneTagsBuffer);
                changed = true;
            }
            y += 28f;

            Widgets.Label(new Rect(rect.x, y, rect.width, 18f), "RimChat_EnvironmentSceneContentLabel".Translate());
            y += 20f;

            Rect textAreaRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
            float contentHeight = Mathf.Max(textAreaRect.height, Text.CalcHeight(entry.Content ?? string.Empty, textAreaRect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, textAreaRect.width - 16f, contentHeight);
            _envSceneContentScroll = GUI.BeginScrollView(textAreaRect, _envSceneContentScroll, viewRect);
            string oldContent = entry.Content ?? string.Empty;
            string newContent = GUI.TextArea(viewRect, oldContent);
            GUI.EndScrollView();
            if (!string.Equals(oldContent, newContent, StringComparison.Ordinal))
            {
                entry.Content = newContent;
                changed = true;
            }

            return changed;
        }

        private bool DrawEnvironmentRpgSwitchesCard(Rect rect, EnvironmentPromptConfig envConfig)
        {
            Rect contentRect = DrawEnvironmentCard(rect, "RimChat_EnvironmentRpgParamsLabel");
            RpgSceneParamSwitchesConfig switches = envConfig.RpgSceneParamSwitches;
            bool changed = false;

            float colGap = 14f;
            float colWidth = (contentRect.width - colGap) / 2f;
            Rect leftCol = new Rect(contentRect.x, contentRect.y, colWidth, contentRect.height);
            Rect rightCol = new Rect(leftCol.xMax + colGap, contentRect.y, colWidth, contentRect.height);
            float leftY = leftCol.y;
            float rightY = rightCol.y;

            changed |= DrawEnvironmentCheckbox(new Rect(leftCol.x, leftY, leftCol.width, 24f), "RimChat_EnvironmentRpgParamSkills", ref switches.IncludeSkills); leftY += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(leftCol.x, leftY, leftCol.width, 24f), "RimChat_EnvironmentRpgParamEquipment", ref switches.IncludeEquipment); leftY += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(leftCol.x, leftY, leftCol.width, 24f), "RimChat_EnvironmentRpgParamGenes", ref switches.IncludeGenes); leftY += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(leftCol.x, leftY, leftCol.width, 24f), "RimChat_EnvironmentRpgParamColonyInventory", ref switches.IncludeColonyInventorySummary); leftY += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(leftCol.x, leftY, leftCol.width, 24f), "RimChat_EnvironmentRpgParamRecentJobState", ref switches.IncludeRecentJobState);

            changed |= DrawEnvironmentCheckbox(new Rect(rightCol.x, rightY, rightCol.width, 24f), "RimChat_EnvironmentRpgParamNeeds", ref switches.IncludeNeeds); rightY += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(rightCol.x, rightY, rightCol.width, 24f), "RimChat_EnvironmentRpgParamHediffs", ref switches.IncludeHediffs); rightY += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(rightCol.x, rightY, rightCol.width, 24f), "RimChat_EnvironmentRpgParamRecentEvents", ref switches.IncludeRecentEvents); rightY += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(rightCol.x, rightY, rightCol.width, 24f), "RimChat_EnvironmentRpgParamHomeAlerts", ref switches.IncludeHomeAlerts); rightY += 24f;
            changed |= DrawEnvironmentCheckbox(new Rect(rightCol.x, rightY, rightCol.width, 24f), "RimChat_EnvironmentRpgParamAttributeLevels", ref switches.IncludeAttributeLevels);

            return changed;
        }

        private void DrawEnvironmentPreviewCard(Rect rect)
        {
            Rect contentRect = DrawEnvironmentCard(rect, "RimChat_EnvironmentPreviewTitle");
            Rect refreshRect = new Rect(contentRect.xMax - 136f, contentRect.y, 136f, 24f);
            if (Widgets.ButtonText(refreshRect, "RimChat_EnvironmentPreviewRefresh".Translate()))
            {
                _environmentPreviewCooldown = 0;
            }

            if (--_environmentPreviewCooldown <= 0)
            {
                _environmentPreviewCache = BuildEnvironmentPreviewText();
                _environmentPreviewCooldown = 60;
            }

            Rect textRect = new Rect(contentRect.x, contentRect.y + 28f, contentRect.width, contentRect.height - 30f);
            float contentHeight = Mathf.Max(textRect.height, Text.CalcHeight(_environmentPreviewCache ?? string.Empty, textRect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, textRect.width - 16f, contentHeight);
            _envPreviewScroll = GUI.BeginScrollView(textRect, _envPreviewScroll, viewRect);
            GUI.color = new Color(0.78f, 0.82f, 0.88f);
            Widgets.Label(viewRect, _environmentPreviewCache ?? string.Empty);
            GUI.color = Color.white;
            GUI.EndScrollView();
        }

        private string BuildEnvironmentPreviewText()
        {
            try
            {
                var sb = new StringBuilder();
                var service = PromptPersistenceService.Instance;
                var config = SystemPromptConfigData;

                Faction sampleFaction = Find.FactionManager?.AllFactionsVisible?.FirstOrDefault(f => f != null && !f.IsPlayer);
                if (sampleFaction != null)
                {
                    var diplomacyContext = DialogueScenarioContext.CreateDiplomacy(sampleFaction, false, new[] { "scene:social" });
                    sb.AppendLine("=== Diplomacy Preview ===");
                    sb.AppendLine(service.BuildEnvironmentPromptBlocks(config, diplomacyContext));
                    sb.AppendLine();
                }

                Pawn first = PawnsFinder.AllMapsWorldAndTemporary_Alive.FirstOrDefault(p => p?.RaceProps?.Humanlike == true);
                Pawn second = PawnsFinder.AllMapsWorldAndTemporary_Alive.FirstOrDefault(p => p?.RaceProps?.Humanlike == true && p != first);
                if (first != null && second != null)
                {
                    var rpgContext = DialogueScenarioContext.CreateRpg(first, second, false, new[] { "scene:daily" });
                    sb.AppendLine("=== RPG Preview ===");
                    sb.AppendLine(service.BuildEnvironmentPromptBlocks(config, rpgContext));
                }

                if (sb.Length == 0)
                {
                    sb.AppendLine("RimChat_EnvironmentPreviewNoContext".Translate());
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Preview Error: {ex.Message}";
            }
        }

        private List<string> ParseTagCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                return new List<string>();
            }

            return csv
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim().ToLowerInvariant())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct()
                .ToList();
        }

        private void SyncEnvironmentSelection(EnvironmentPromptConfig envConfig)
        {
            if (_selectedEnvironmentSceneIndex < 0 || _selectedEnvironmentSceneIndex >= envConfig.SceneEntries.Count)
            {
                _selectedEnvironmentSceneId = string.Empty;
                _sceneTagsBuffer = string.Empty;
                _scenePriorityBuffer = "0";
                return;
            }

            ScenePromptEntryConfig entry = envConfig.SceneEntries[_selectedEnvironmentSceneIndex];
            _selectedEnvironmentSceneId = entry?.Id ?? string.Empty;
            _sceneTagsBuffer = entry?.MatchTags != null ? string.Join(", ", entry.MatchTags) : string.Empty;
            _scenePriorityBuffer = (entry?.Priority ?? 0).ToString();
        }
    }
}

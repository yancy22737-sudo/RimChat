using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimChat.AI;

namespace RimChat.Config
{
    /// <summary>/// RimChat AI闂備胶顢婇惌鍥礃閵娧冨箑闂備胶鍎甸弲婵嬧€﹂崼銉ョ煑鐟滃繘骞忕€ｎ喖绀堢憸蹇涘几閸岀偞鐓欑痪鐗埳戝▍鍛存煟? /// 闂備礁鎲￠悧鏇㈠箠鎼淬劌绠氶柛顐ｇ箥閻撱儲鎱ㄥ鍡楀鐞氱喖姊绘笟鍥т簻妞わ妇鏁绘俊鏉戭吋婢跺﹦顢呴梺鐐藉劥濞呮洜鈧氨娼狪闂傚倸鍊哥€氼參宕濋弴銏犳槬婵°倕鎳忛埛鎾绘煕椤愶絿濡囬柛瀣尰缁虹晫绮欓崹顔跨ゴ缂? ///</summary>
    public partial class RimChatSettings : ModSettings
    {
        #region 闂佺懓鍚嬪娆戞崲閹版澘鍨傛い蹇撶墕缁€?- AI 闂備胶顢婇惌鍥礃閵娧冨箑闂佽崵濮崇粈浣规櫠娴犲鍋?
        void ExposeData_AI()
        {
            Scribe_Values.Look(ref MaxGoodwillAdjustmentPerCall, "MaxGoodwillAdjustmentPerCall", 15);
            Scribe_Values.Look(ref MaxDailyGoodwillAdjustment, "MaxDailyGoodwillAdjustment", 30);
            Scribe_Values.Look(ref GoodwillCooldownTicks, "GoodwillCooldownTicks", 2500);

            Scribe_Values.Look(ref MaxGiftSilverAmount, "MaxGiftSilverAmount", 1000);
            Scribe_Values.Look(ref MaxGiftGoodwillGain, "MaxGiftGoodwillGain", 10);
            Scribe_Values.Look(ref GiftCooldownTicks, "GiftCooldownTicks", 60000);

            Scribe_Values.Look(ref MinGoodwillForAid, "MinGoodwillForAid", 40);
            Scribe_Values.Look(ref AidCooldownTicks, "AidCooldownTicks", 120000);

            Scribe_Values.Look(ref MaxGoodwillForWarDeclaration, "MaxGoodwillForWarDeclaration", -50);
            Scribe_Values.Look(ref WarCooldownTicks, "WarCooldownTicks", 60000);

            Scribe_Values.Look(ref MaxPeaceCost, "MaxPeaceCost", 5000);
            Scribe_Values.Look(ref PeaceGoodwillReset, "PeaceGoodwillReset", -20);
            Scribe_Values.Look(ref PeaceCooldownTicks, "PeaceCooldownTicks", 60000);

            Scribe_Values.Look(ref CaravanCooldownTicks, "CaravanCooldownTicks", 90000);
            Scribe_Values.Look(ref AidDelayBaseTicks, "AidDelayBaseTicks", 90000);
            Scribe_Values.Look(ref CaravanDelayBaseTicks, "CaravanDelayBaseTicks", 135000);

            Scribe_Values.Look(ref MinQuestCooldownDays, "MinQuestCooldownDays", 7);
            Scribe_Values.Look(ref MaxQuestCooldownDays, "MaxQuestCooldownDays", 12);

            Scribe_Values.Look(ref EnableAIGoodwillAdjustment, "EnableAIGoodwillAdjustment", true);
            Scribe_Values.Look(ref EnableAIGiftSending, "EnableAIGiftSending", true);
            Scribe_Values.Look(ref EnableAIWarDeclaration, "EnableAIWarDeclaration", true);
            Scribe_Values.Look(ref EnableAIPeaceMaking, "EnableAIPeaceMaking", true);
            Scribe_Values.Look(ref EnableAITradeCaravan, "EnableAITradeCaravan", true);
            Scribe_Values.Look(ref EnableAIAidRequest, "EnableAIAidRequest", true);
            Scribe_Values.Look(ref EnableAIRaidRequest, "EnableAIRaidRequest", true);

            // Raid Granular Settings
            Scribe_Values.Look(ref EnableRaidStrategy_ImmediateAttack, "EnableRaidStrategy_ImmediateAttack", true);
            Scribe_Values.Look(ref EnableRaidStrategy_ImmediateAttackSmart, "EnableRaidStrategy_ImmediateAttackSmart", true);
            Scribe_Values.Look(ref EnableRaidStrategy_StageThenAttack, "EnableRaidStrategy_StageThenAttack", true);
            Scribe_Values.Look(ref EnableRaidStrategy_ImmediateAttackSappers, "EnableRaidStrategy_ImmediateAttackSappers", true);
            Scribe_Values.Look(ref EnableRaidStrategy_Siege, "EnableRaidStrategy_Siege", true);

            Scribe_Values.Look(ref EnableRaidArrival_EdgeWalkIn, "EnableRaidArrival_EdgeWalkIn", true);
            Scribe_Values.Look(ref EnableRaidArrival_EdgeDrop, "EnableRaidArrival_EdgeDrop", true);
            Scribe_Values.Look(ref EnableRaidArrival_EdgeWalkInGroups, "EnableRaidArrival_EdgeWalkInGroups", true);
            Scribe_Values.Look(ref EnableRaidArrival_RandomDrop, "EnableRaidArrival_RandomDrop", false);
            Scribe_Values.Look(ref EnableRaidArrival_CenterDrop, "EnableRaidArrival_CenterDrop", false);

            Scribe_Values.Look(ref EnableAPICallLogging, "EnableAPICallLogging", true);
            Scribe_Values.Look(ref MaxAPICallsPerHour, "MaxAPICallsPerHour", 0);

            Scribe_Values.Look(ref EnableFactionPresenceStatus, "EnableFactionPresenceStatus", true);
            Scribe_Values.Look(ref PresenceCacheHours, "PresenceCacheHours", 2f);
            Scribe_Values.Look(ref PresenceForcedOfflineHours, "PresenceForcedOfflineHours", 24f);
            Scribe_Values.Look(ref PresenceNightBiasEnabled, "PresenceNightBiasEnabled", true);
            Scribe_Values.Look(ref PresenceNightStartHour, "PresenceNightStartHour", 22);
            Scribe_Values.Look(ref PresenceNightEndHour, "PresenceNightEndHour", 6);
            Scribe_Values.Look(ref PresenceNightOfflineBias, "PresenceNightOfflineBias", 0.65f);
            Scribe_Values.Look(ref PresenceUseAdvancedProfiles, "PresenceUseAdvancedProfiles", true);
            Scribe_Values.Look(ref PresenceOnlineStart_Default, "PresenceOnlineStart_Default", 7);
            Scribe_Values.Look(ref PresenceOnlineDuration_Default, "PresenceOnlineDuration_Default", 12);
            Scribe_Values.Look(ref PresenceOnlineStart_Neolithic, "PresenceOnlineStart_Neolithic", 8);
            Scribe_Values.Look(ref PresenceOnlineDuration_Neolithic, "PresenceOnlineDuration_Neolithic", 8);
            Scribe_Values.Look(ref PresenceOnlineStart_Medieval, "PresenceOnlineStart_Medieval", 8);
            Scribe_Values.Look(ref PresenceOnlineDuration_Medieval, "PresenceOnlineDuration_Medieval", 10);
            Scribe_Values.Look(ref PresenceOnlineStart_Industrial, "PresenceOnlineStart_Industrial", 7);
            Scribe_Values.Look(ref PresenceOnlineDuration_Industrial, "PresenceOnlineDuration_Industrial", 14);
            Scribe_Values.Look(ref PresenceOnlineStart_Spacer, "PresenceOnlineStart_Spacer", 6);
            Scribe_Values.Look(ref PresenceOnlineDuration_Spacer, "PresenceOnlineDuration_Spacer", 18);
            Scribe_Values.Look(ref PresenceOnlineStart_Ultra, "PresenceOnlineStart_Ultra", 4);
            Scribe_Values.Look(ref PresenceOnlineDuration_Ultra, "PresenceOnlineDuration_Ultra", 20);
            Scribe_Values.Look(ref PresenceOnlineStart_Archotech, "PresenceOnlineStart_Archotech", 4);
            Scribe_Values.Look(ref PresenceOnlineDuration_Archotech, "PresenceOnlineDuration_Archotech", 20);

            Scribe_Values.Look(ref EnableSocialCircle, "EnableSocialCircle", true);
            Scribe_Values.Look(ref SocialPostIntervalMinDays, "SocialPostIntervalMinDays", 5);
            Scribe_Values.Look(ref SocialPostIntervalMaxDays, "SocialPostIntervalMaxDays", 7);
            Scribe_Values.Look(ref EnablePlayerInfluenceNews, "EnablePlayerInfluenceNews", true);
            Scribe_Values.Look(ref EnableAISimulationNews, "EnableAISimulationNews", true);
            Scribe_Values.Look(ref EnableSocialCircleAutoActions, "EnableSocialCircleAutoActions", false);

            Scribe_Values.Look(ref EnableNpcInitiatedDialogue, "EnableNpcInitiatedDialogue", true);
            Scribe_Values.Look(
                ref NpcPushFrequencyMode,
                "NpcPushFrequencyMode",
                global::RimChat.Config.NpcPushFrequencyMode.Low);
            Scribe_Values.Look(ref NpcQueueMaxPerFaction, "NpcQueueMaxPerFaction", 3);
            Scribe_Values.Look(ref NpcQueueExpireHours, "NpcQueueExpireHours", 12f);
            Scribe_Values.Look(ref EnableBusyByDrafted, "EnableBusyByDrafted", true);
            Scribe_Values.Look(ref EnableBusyByHostiles, "EnableBusyByHostiles", true);
            Scribe_Values.Look(ref EnableBusyByClickRate, "EnableBusyByClickRate", true);

            MaxAPICallsPerHour = Mathf.Max(0, MaxAPICallsPerHour);
        }

        #endregion

        #region UI缂傚倸鍊烽悞锕傛晪闂?- AI闂備胶顢婇惌鍥礃閵娧冨箑闂傚倷绶￠崑鍕囬悽绋课ョ€广儱顦涵鈧?
        private Vector2 aiSettingsScrollPosition = Vector2.zero;
        private AIControlSection expandedAIControlSection = AIControlSection.UISettings;

        private enum AIControlSection
        {
            None,
            UISettings,
            PresenceSettings,
            NpcPushSettings,
            AIBehaviorSettings,
            RaidSettings,
            GoodwillSettings,
            GiftSettings,
            AidSettings,
            WarPeaceSettings,
            CaravanSettings,
            QuestSettings,
            SocialCircleSettings,
            SecuritySettings
        }

        private void DrawTab_AIControl(Rect rect)
        {
            float viewHeight = CalculateAIControlContentHeight(rect.width - 16f);
            Rect viewRect = new Rect(0, 0, rect.width - 16f, viewHeight);
            
            Widgets.BeginScrollView(rect, ref aiSettingsScrollPosition, viewRect);
            
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(new Rect(0, 0, viewRect.width, viewRect.height));
            DrawAIControlHeaderBar(listing);

            DrawAccordionSection(listing, AIControlSection.UISettings, "RimChat_UISettings".Translate(), ResetUISettingsToDefault, DrawUISettings, new Color(0.9f, 0.9f, 1f));
            DrawAccordionSection(listing, AIControlSection.PresenceSettings, "RimChat_PresenceSettings".Translate(), ResetPresenceSettingsToDefault, DrawPresenceSettings, new Color(0.85f, 1f, 0.85f));
            DrawAccordionSection(listing, AIControlSection.NpcPushSettings, "RimChat_NpcPushSettings".Translate(), ResetNpcInitiatedDialogueSettings, DrawNpcInitiatedDialogueSettings, new Color(0.85f, 0.9f, 1f));
            DrawAccordionSection(listing, AIControlSection.AIBehaviorSettings, "RimChat_AIBehaviorSettings".Translate(), ResetAIBehaviorToDefault, DrawAIBehaviorToggles);
            DrawAccordionSection(listing, AIControlSection.RaidSettings, "RimChat_RaidSettings".Translate(), ResetRaidSettingsToDefault, DrawRaidSettings, new Color(1f, 0.6f, 0.6f));
            DrawAccordionSection(listing, AIControlSection.GoodwillSettings, "RimChat_GoodwillSettings".Translate(), ResetGoodwillSettingsToDefault, DrawGoodwillSettings, new Color(0.8f, 0.9f, 1f));
            DrawAccordionSection(listing, AIControlSection.AidSettings, "RimChat_AidSettings".Translate(), ResetAidSettingsToDefault, DrawAidSettings, new Color(0.7f, 1f, 0.8f));
            DrawAccordionSection(listing, AIControlSection.WarPeaceSettings, "RimChat_WarPeaceSettings".Translate(), ResetWarPeaceSettingsToDefault, DrawWarPeaceSettings, new Color(1f, 0.7f, 0.7f));
            DrawAccordionSection(listing, AIControlSection.CaravanSettings, "RimChat_CaravanSettings".Translate(), ResetCaravanSettingsToDefault, DrawCaravanSettings, new Color(0.9f, 0.8f, 1f));
            DrawAccordionSection(listing, AIControlSection.QuestSettings, "RimChat_QuestSettings".Translate(), ResetQuestSettingsToDefault, DrawQuestSettings, new Color(0.8f, 0.8f, 1f));
            DrawAccordionSection(listing, AIControlSection.SocialCircleSettings, "RimChat_SocialCircleSettings".Translate(), ResetSocialCircleSettingsToDefault, DrawSocialCircleSettings, new Color(0.8f, 1f, 0.95f));
            DrawAccordionSection(listing, AIControlSection.SecuritySettings, "RimChat_SecuritySettings".Translate(), ResetSecuritySettingsToDefault, DrawSecuritySettings, new Color(1f, 0.9f, 0.5f));

            listing.End();
            Widgets.EndScrollView();
        }

        /// <summary>/// 闂佽崵濮崇欢銈囨閺囥垺鍋╃紒顐㈠殬闂備胶顢婇惌鍥礃閵娧冨箑闂傚倷绶￠崑鍕囬悽绋课ョ€广儱顦涵鈧梺鐐藉劚閸熷潡寮崼鏇熷€电痪顓炴媼濞兼劙鏌嶈閸撴瑩鈥﹂悜鑺ュ仧妞ゆ棁濮ら崕?
 ///</summary>
        private float CalculateAIControlContentHeight(float width)
        {
            float headerHeight = 34f * 12f + 120f;
            float expandedContentHeight = GetExpandedSectionBodyHeight();
            float viewHeight = headerHeight + expandedContentHeight + 40f;
            float minHeight = Mathf.Max(260f, width * 0.6f);
            return Mathf.Max(viewHeight, minHeight);
        }

        private float GetExpandedSectionBodyHeight()
        {
            return expandedAIControlSection switch
            {
                AIControlSection.None => 0f,
                AIControlSection.UISettings => 180f,
                AIControlSection.PresenceSettings => 760f,
                AIControlSection.NpcPushSettings => 320f,
                AIControlSection.AIBehaviorSettings => 220f,
                AIControlSection.RaidSettings => 300f,
                AIControlSection.GoodwillSettings => 220f,
                AIControlSection.GiftSettings => 0f,
                AIControlSection.AidSettings => 220f,
                AIControlSection.WarPeaceSettings => 300f,
                AIControlSection.CaravanSettings => 180f,
                AIControlSection.QuestSettings => 160f,
                AIControlSection.SocialCircleSettings => 320f,
                AIControlSection.SecuritySettings => 170f,
                _ => 260f
            };
        }

        private void ToggleAIControlSection(AIControlSection section)
        {
            expandedAIControlSection = expandedAIControlSection == section
                ? AIControlSection.None
                : section;
        }

        private void DrawAccordionSection(
            Listing_Standard listing,
            AIControlSection section,
            string title,
            System.Action resetAction,
            System.Action<Listing_Standard> drawContent,
            Color? titleColor = null)
        {
            Rect headerRect = listing.GetRect(30f);
            bool expanded = expandedAIControlSection == section;
            float buttonWidth = expanded ? 80f : 0f;
            float rightPadding = expanded ? 10f : 0f;
            Rect clickableRect = new Rect(headerRect.x, headerRect.y, headerRect.width - buttonWidth - rightPadding, headerRect.height);
            Rect titleRect = new Rect(clickableRect.x + 6f, clickableRect.y, clickableRect.width - 6f, clickableRect.height);
            Rect buttonRect = new Rect(headerRect.x + headerRect.width - 80f, headerRect.y + 2f, 80f, 24f);
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;

            Color headerBackground = expanded
                ? new Color(0.20f, 0.28f, 0.42f, 0.35f)
                : (Mouse.IsOver(clickableRect) ? new Color(0.16f, 0.18f, 0.22f, 0.45f) : new Color(0.12f, 0.12f, 0.14f, 0.30f));
            Widgets.DrawBoxSolid(headerRect, headerBackground);
            if (expanded)
            {
                Color accent = titleColor ?? new Color(0.45f, 0.75f, 1f, 0.9f);
                Widgets.DrawBoxSolid(new Rect(headerRect.x, headerRect.y, 3f, headerRect.height), accent);
            }

            Color original = GUI.color;
            Text.Font = GameFont.Small;
            if (titleColor.HasValue) GUI.color = titleColor.Value;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(titleRect, title);
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            GUI.color = original;
            RegisterTooltip(clickableRect, GetAISectionTooltipKey(section));

            if (Widgets.ButtonInvisible(clickableRect))
            {
                ToggleAIControlSection(section);
                SoundDefOf.Click.PlayOneShotOnCamera(null);
            }

            if (expanded)
            {
                Color prevButtonColor = GUI.color;
                GUI.color = new Color(0.85f, 0.85f, 0.85f);
                if (Widgets.ButtonText(buttonRect, "RimChat_ResetToDefault".Translate()))
                {
                    ShowResetConfirmationDialog(title, resetAction);
                }
                GUI.color = prevButtonColor;
            }

            if (expanded)
            {
                listing.Gap(2f);
                drawContent?.Invoke(listing);
                listing.Gap(8f);
            }
            else
            {
                listing.Gap(4f);
            }

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        private void DrawUISettings(Listing_Standard listing)
        {
            listing.CheckboxLabeled("RimChat_ReplaceCommsConsole".Translate(), ref ReplaceCommsConsole);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect commsDescRect = listing.GetRect(Text.LineHeight * 2f);
            Widgets.Label(commsDescRect, "RimChat_ReplaceCommsConsoleDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(6f);

            listing.Label("RimChat_TypewriterSpeed".Translate());
            listing.Gap(6f);

            Rect speedRowRect = listing.GetRect(32f);
            float columnWidth = (speedRowRect.width - 20f) / 3f;
            float spacing = 10f;

            Rect fastRect = new Rect(speedRowRect.x, speedRowRect.y, columnWidth, 32f);
            Rect standardRect = new Rect(speedRowRect.x + columnWidth + spacing, speedRowRect.y, columnWidth, 32f);
            Rect immersiveRect = new Rect(speedRowRect.x + (columnWidth + spacing) * 2, speedRowRect.y, columnWidth, 32f);

            DrawSpeedOption(fastRect, "RimChat_SpeedFast".Translate(), TypewriterSpeedMode == TypewriterSpeedMode.Fast, () => TypewriterSpeedMode = TypewriterSpeedMode.Fast);
            DrawSpeedOption(standardRect, "RimChat_SpeedStandard".Translate(), TypewriterSpeedMode == TypewriterSpeedMode.Standard, () => TypewriterSpeedMode = TypewriterSpeedMode.Standard);
            DrawSpeedOption(immersiveRect, "RimChat_SpeedImmersive".Translate(), TypewriterSpeedMode == TypewriterSpeedMode.Immersive, () => TypewriterSpeedMode = TypewriterSpeedMode.Immersive);

            listing.Gap(6f);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.65f, 0.65f, 0.65f);
            string speedDesc = TypewriterSpeedMode switch
            {
                TypewriterSpeedMode.Fast => "RimChat_SpeedFastDesc".Translate(),
                TypewriterSpeedMode.Standard => "RimChat_SpeedStandardDesc".Translate(),
                TypewriterSpeedMode.Immersive => "RimChat_SpeedImmersiveDesc".Translate(),
                _ => ""
            };
            Rect descRect = listing.GetRect(Text.LineHeight * 2f);
            Widgets.Label(descRect, speedDesc);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(8f);
        }

        private void DrawPresenceSettings(Listing_Standard listing)
        {
            listing.CheckboxLabeled("RimChat_EnablePresenceSystem".Translate(), ref EnableFactionPresenceStatus);

            listing.Label("RimChat_PresenceCacheHours".Translate(PresenceCacheHours.ToString("F1")));
            PresenceCacheHours = listing.Slider(PresenceCacheHours, 1f, 48f);

            listing.Label("RimChat_PresenceForcedOfflineHours".Translate(PresenceForcedOfflineHours.ToString("F1")));
            PresenceForcedOfflineHours = listing.Slider(PresenceForcedOfflineHours, 1f, 72f);

            listing.CheckboxLabeled("RimChat_PresenceNightBiasEnabled".Translate(), ref PresenceNightBiasEnabled);
            if (PresenceNightBiasEnabled)
            {
                listing.Label("RimChat_PresenceNightStartHour".Translate(PresenceNightStartHour));
                PresenceNightStartHour = Mathf.RoundToInt(listing.Slider(PresenceNightStartHour, 0f, 23f));

                listing.Label("RimChat_PresenceNightEndHour".Translate(PresenceNightEndHour));
                PresenceNightEndHour = Mathf.RoundToInt(listing.Slider(PresenceNightEndHour, 0f, 23f));

                listing.Label("RimChat_PresenceNightOfflineBias".Translate((PresenceNightOfflineBias * 100f).ToString("F0")));
                PresenceNightOfflineBias = listing.Slider(PresenceNightOfflineBias, 0f, 1f);
            }

            listing.CheckboxLabeled("RimChat_PresenceAdvancedProfiles".Translate(), ref PresenceUseAdvancedProfiles);
            if (PresenceUseAdvancedProfiles)
            {
                DrawPresenceProfileSliders(listing, "RimChat_PresenceProfileDefault".Translate(), ref PresenceOnlineStart_Default, ref PresenceOnlineDuration_Default);
                DrawPresenceProfileSliders(listing, "RimChat_PresenceProfileNeolithic".Translate(), ref PresenceOnlineStart_Neolithic, ref PresenceOnlineDuration_Neolithic);
                DrawPresenceProfileSliders(listing, "RimChat_PresenceProfileMedieval".Translate(), ref PresenceOnlineStart_Medieval, ref PresenceOnlineDuration_Medieval);
                DrawPresenceProfileSliders(listing, "RimChat_PresenceProfileIndustrial".Translate(), ref PresenceOnlineStart_Industrial, ref PresenceOnlineDuration_Industrial);
                DrawPresenceProfileSliders(listing, "RimChat_PresenceProfileSpacer".Translate(), ref PresenceOnlineStart_Spacer, ref PresenceOnlineDuration_Spacer);
                DrawPresenceProfileSliders(listing, "RimChat_PresenceProfileUltra".Translate(), ref PresenceOnlineStart_Ultra, ref PresenceOnlineDuration_Ultra);
                DrawPresenceProfileSliders(listing, "RimChat_PresenceProfileArchotech".Translate(), ref PresenceOnlineStart_Archotech, ref PresenceOnlineDuration_Archotech);
            }
        }

        private void DrawPresenceProfileSliders(Listing_Standard listing, string profileLabel, ref int startHour, ref int durationHours)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.95f, 0.75f);
            listing.Label(profileLabel);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.Label("RimChat_PresenceProfileStartHour".Translate(startHour));
            startHour = Mathf.RoundToInt(listing.Slider(startHour, 0f, 23f));

            listing.Label("RimChat_PresenceProfileDurationHours".Translate(durationHours));
            durationHours = Mathf.RoundToInt(listing.Slider(durationHours, 1f, 24f));
        }

        /// <summary>/// 缂傚倸鍊烽悞锕傛晪闂佺硶鏅滈〃濠囧蓟閸涘瓨鍋勭€瑰嫰鍋婇崬娲⒒娓氬洤浜滄い锔炬暬婵℃潙顓兼径瀣珫闂佸壊鍋呯换鍌滅矆鐎ｎ喗鈷戞い鎰╁焺濡插綊鎮楅崹顐ょ煉闁?+ 闂備礁鎼崐绋棵洪敃鈧敃銏″鐎涙ɑ娅? ///</summary>
        private void DrawSpeedOption(Rect rect, string label, bool isActive, System.Action onClick)
        {
            // 缂傚倸鍊烽悞锕傛晪闂佺硶鏅滈〃濠囧箠濡ゅ啩娌柣鎰靛墰瑜版煡姊洪幐搴ｂ槈闁绘妫濋妴鍛邦樄鐎殿喚顭堥…銊╁醇濮橆兛澹曟繝銏ｆ硾椤︽娊宕㈤鍕厵閻庢稒顭囨晶顒勬煕鐎ｎ偅宕岀€规洘鍨甸…銊╁箛椤旂虎妲?
            if (isActive)
            {
                Widgets.DrawBoxSolid(rect, new Color(0.25f, 0.45f, 0.7f, 0.3f));
            }
            else if (Mouse.IsOver(rect))
            {
                Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.2f, 0.5f));
            }

            float radioSize = 20f;
            float radioX = rect.x + 10f;
            float radioY = rect.y + (rect.height - radioSize) / 2f;
            Rect radioRect = new Rect(radioX, radioY, radioSize, radioSize);
            
            Color outerColor = isActive ? new Color(0.3f, 0.7f, 1f) : new Color(0.5f, 0.5f, 0.55f);
            GUI.color = outerColor;
            GUI.DrawTexture(radioRect, BaseContent.WhiteTex);
            
            if (isActive)
            {
                float innerSize = radioSize * 0.5f;
                float innerX = radioX + (radioSize - innerSize) / 2f;
                float innerY = radioY + (radioSize - innerSize) / 2f;
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(innerX, innerY, innerSize, innerSize), BaseContent.WhiteTex);
            }
            
            GUI.color = Color.white;

            Text.Font = GameFont.Small;
            GUI.color = isActive ? Color.white : new Color(0.85f, 0.85f, 0.9f);
            Rect textRect = new Rect(radioX + radioSize + 8f, rect.y + (rect.height - Text.LineHeight) / 2f, rect.width - radioSize - 16f, Text.LineHeight);
            Widgets.Label(textRect, label);
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(rect))
            {
                onClick();
                SoundDefOf.Click.PlayOneShotOnCamera(null);
            }
        }

        /// <summary>/// AI 闂佽崵鍋炵粙鎴炵附閺冨倹瀚婚柣鏃傚帶缁犳垿鎮归崶顏勭毢缁炬儳顭烽弻? ///</summary>
        private void DrawAIBehaviorToggles(Listing_Standard listing)
        {
            listing.CheckboxLabeled("RimChat_EnableAIGoodwillAdjustment".Translate(), ref EnableAIGoodwillAdjustment);
            listing.CheckboxLabeled("RimChat_EnableAIGiftSending".Translate(), ref EnableAIGiftSending);
            listing.CheckboxLabeled("RimChat_EnableAIWarDeclaration".Translate(), ref EnableAIWarDeclaration);
            listing.CheckboxLabeled("RimChat_EnableAIPeaceMaking".Translate(), ref EnableAIPeaceMaking);
            listing.CheckboxLabeled("RimChat_EnableAITradeCaravan".Translate(), ref EnableAITradeCaravan);
            listing.CheckboxLabeled("RimChat_EnableAIAidRequest".Translate(), ref EnableAIAidRequest);
            listing.CheckboxLabeled("RimChat_EnableAIRaidRequest".Translate(), ref EnableAIRaidRequest);
        }

        /// <summary>/// 闂佽崵鍋為崙褰掑储婵傜鍚规い鏃傚亾婵ジ鏌涢幘妤€鎳忛悗? ///</summary>
        private void DrawRaidSettings(Listing_Standard listing)
        {
            listing.CheckboxLabeled("RimChat_EnableRaidStrategy_ImmediateAttack".Translate(), ref EnableRaidStrategy_ImmediateAttack);
            listing.CheckboxLabeled("RimChat_EnableRaidStrategy_ImmediateAttackSmart".Translate(), ref EnableRaidStrategy_ImmediateAttackSmart);
            listing.CheckboxLabeled("RimChat_EnableRaidStrategy_StageThenAttack".Translate(), ref EnableRaidStrategy_StageThenAttack);
            listing.CheckboxLabeled("RimChat_EnableRaidStrategy_ImmediateAttackSappers".Translate(), ref EnableRaidStrategy_ImmediateAttackSappers);
            listing.CheckboxLabeled("RimChat_EnableRaidStrategy_Siege".Translate(), ref EnableRaidStrategy_Siege);

            // 闂備礁鎲＄敮妤佸垔娴犲绠垫い蹇撶墕濡﹢鏌ｉ悢绋款棆缁绢厸鍋?
            listing.CheckboxLabeled("RimChat_EnableRaidArrival_EdgeWalkIn".Translate(), ref EnableRaidArrival_EdgeWalkIn);
            listing.CheckboxLabeled("RimChat_EnableRaidArrival_EdgeDrop".Translate(), ref EnableRaidArrival_EdgeDrop);
            listing.CheckboxLabeled("RimChat_EnableRaidArrival_EdgeWalkInGroups".Translate(), ref EnableRaidArrival_EdgeWalkInGroups);
            listing.CheckboxLabeled("RimChat_EnableRaidArrival_RandomDrop".Translate(), ref EnableRaidArrival_RandomDrop);
            listing.CheckboxLabeled("RimChat_EnableRaidArrival_CenterDrop".Translate(), ref EnableRaidArrival_CenterDrop);
            if (EnableRaidArrival_CenterDrop || EnableRaidArrival_RandomDrop)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.yellow;
                listing.Label("RimChat_CenterDropWarning".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
        }

        /// <summary>/// 濠电娀娼ч崐鑺ユ叏閵堝绀夐柛娑卞枟閸庣喖鏌ㄩ弴姘冲厡婵炲牆鐖奸弻鈩冩媴娓氼垱顥撳銈嗘⒐濞叉粎妲? ///</summary>
        private void DrawGoodwillSettings(Listing_Standard listing)
        {
            listing.Label($"RimChat_MaxGoodwillAdjustmentPerCall".Translate(MaxGoodwillAdjustmentPerCall));
            MaxGoodwillAdjustmentPerCall = (int)listing.Slider(MaxGoodwillAdjustmentPerCall, 0, 50);

            listing.Label($"RimChat_MaxDailyGoodwillAdjustment".Translate(MaxDailyGoodwillAdjustment));
            MaxDailyGoodwillAdjustment = (int)listing.Slider(MaxDailyGoodwillAdjustment, 0, 100);

            float cooldownHours = GoodwillCooldownTicks / 2500f;
            listing.Label($"RimChat_GoodwillCooldown".Translate(cooldownHours.ToString("F1")));
            cooldownHours = listing.Slider(cooldownHours, 0f, 24f);
            GoodwillCooldownTicks = (int)(cooldownHours * 2500);

            if (MaxGoodwillAdjustmentPerCall > MaxDailyGoodwillAdjustment / 2)
            {
                GUI.color = Color.yellow;
                Text.Font = GameFont.Tiny;
                listing.Label("RimChat_GoodwillWarning".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        /// <summary>/// 缂傚倷璁查崑鎾绘煠濞村娅呴柍閿嬬墵閹鎷呯粙搴撴寖闂? ///</summary>
        private void DrawGiftSettings(Listing_Standard listing)
        {
            listing.Label($"RimChat_MaxGiftSilverAmount".Translate(MaxGiftSilverAmount));
            MaxGiftSilverAmount = (int)listing.Slider(MaxGiftSilverAmount, 100, 5000);

            // 闂備礁鎼悧鍐磻閹炬剚鐔嗛柛顐㈡閸熴劑宕戦妸鈺傜厵闁规鍠栭弸搴ㄦ倵鐟欏嫬鈻曠€殿喓鍔戝畷婊勬媴鐟欏嫬巍
            listing.Label($"RimChat_MaxGiftGoodwillGain".Translate(MaxGiftGoodwillGain));
            MaxGiftGoodwillGain = (int)listing.Slider(MaxGiftGoodwillGain, 1, 25);

            float cooldownDays = GiftCooldownTicks / 60000f;
            listing.Label($"RimChat_GiftCooldown".Translate(cooldownDays.ToString("F1")));
            cooldownDays = listing.Slider(cooldownDays, 0.5f, 5f);
            GiftCooldownTicks = (int)(cooldownDays * 60000);
        }

        /// <summary>/// 闂備礁婀辩划顖氼焽濞嗘劖鍙忔い蹇撴婵ジ鏌涢幘妤€鎳忛悗? ///</summary>
        private void DrawAidSettings(Listing_Standard listing)
        {
            listing.Label($"RimChat_MinGoodwillForAid".Translate(MinGoodwillForAid));
            MinGoodwillForAid = (int)listing.Slider(MinGoodwillForAid, 0, 100);

            float cooldownDays = AidCooldownTicks / 60000f;
            listing.Label($"RimChat_AidCooldown".Translate(cooldownDays.ToString("F1")));
            cooldownDays = listing.Slider(cooldownDays, 1f, 7f);
            AidCooldownTicks = (int)(cooldownDays * 60000);

            float delayDays = AidDelayBaseTicks / 60000f;
            listing.Label($"RimChat_AidDelay".Translate(delayDays.ToString("F1")));
            delayDays = listing.Slider(delayDays, 0.0f, 5f);
            AidDelayBaseTicks = (int)(delayDays * 60000);
        }

        /// <summary>/// 闂備胶鎳撻悺銊ф箒缂備降鍔婇崐鏍矙婢跺鍎熼柍鈺佸暙椤忣垰螖閻橀潧浠滈柣銈呮喘椤㈡瑩寮撮悩鐢碉紴? ///</summary>
        private void DrawWarPeaceSettings(Listing_Standard listing)
        {
            listing.Label($"RimChat_MaxGoodwillForWar".Translate(MaxGoodwillForWarDeclaration));
            MaxGoodwillForWarDeclaration = (int)listing.Slider(MaxGoodwillForWarDeclaration, -100, 0);

            float warCooldownDays = WarCooldownTicks / 60000f;
            listing.Label($"RimChat_WarCooldown".Translate(warCooldownDays.ToString("F1")));
            warCooldownDays = listing.Slider(warCooldownDays, 1f, 7f);
            WarCooldownTicks = (int)(warCooldownDays * 60000);

            listing.Gap();

            listing.Label($"RimChat_MaxPeaceCost".Translate(MaxPeaceCost));
            MaxPeaceCost = (int)listing.Slider(MaxPeaceCost, 0, 10000);

            listing.Label($"RimChat_PeaceGoodwillReset".Translate(PeaceGoodwillReset));
            PeaceGoodwillReset = (int)listing.Slider(PeaceGoodwillReset, -100, 0);

            float peaceCooldownDays = PeaceCooldownTicks / 60000f;
            listing.Label($"RimChat_PeaceCooldown".Translate(peaceCooldownDays.ToString("F1")));
            peaceCooldownDays = listing.Slider(peaceCooldownDays, 1f, 7f);
            PeaceCooldownTicks = (int)(peaceCooldownDays * 60000);
        }

        /// <summary>/// 闂備礁鎽滈崰搴∥涘┑鍠綁鏁傞悙顒€顎涢梺鍛婃寙閸涱喚鈧? ///</summary>
        private void DrawCaravanSettings(Listing_Standard listing)
        {
            float cooldownDays = CaravanCooldownTicks / 60000f;
            listing.Label($"RimChat_CaravanCooldown".Translate(cooldownDays.ToString("F1")));
            cooldownDays = listing.Slider(cooldownDays, 0.5f, 5f);
            CaravanCooldownTicks = (int)(cooldownDays * 60000);

            float delayDays = CaravanDelayBaseTicks / 60000f;
            listing.Label($"RimChat_CaravanDelay".Translate(delayDays.ToString("F1")));
            delayDays = listing.Slider(delayDays, 0.0f, 7f);
            CaravanDelayBaseTicks = (int)(delayDays * 60000);
        }

        /// <summary>/// 濠电偛顕慨楣冾敋瑜庨幈銊╂偄閻戞ê顎涢梺鍛婃寙閸涱喚鈧? ///</summary>
        private void DrawQuestSettings(Listing_Standard listing)
        {
            listing.Label($"RimChat_MinQuestCooldown".Translate(MinQuestCooldownDays));
            MinQuestCooldownDays = (int)listing.Slider(MinQuestCooldownDays, 1, 30);

            listing.Label($"RimChat_MaxQuestCooldown".Translate(MaxQuestCooldownDays));
            MaxQuestCooldownDays = (int)listing.Slider(MaxQuestCooldownDays, Math.Max(MinQuestCooldownDays, 1), 60);
        }

        /// <summary>/// 闂佽娴烽幊鎾凰囬鐐茬煑闊洦娲樻刊濂告煕閹炬鎳忛悗? ///</summary>
        private void DrawSecuritySettings(Listing_Standard listing)
        {
            listing.CheckboxLabeled("RimChat_EnableAPICallLogging".Translate(), ref EnableAPICallLogging);

            listing.Label("RimChat_MaxAPICallsPerHour".Translate(GetApiCallLimitLabelValue()));
            int clampedLimit = Mathf.Clamp(MaxAPICallsPerHour, 0, 100);
            MaxAPICallsPerHour = Mathf.RoundToInt(listing.Slider(clampedLimit, 0f, 100f));
        }

        private string GetApiCallLimitLabelValue()
        {
            int limit = Mathf.Max(0, MaxAPICallsPerHour);
            if (limit <= 0)
            {
                return "RimChat_Unlimited".Translate().ToString();
            }

            return limit.ToString();
        }

        /// <summary>/// 缂傚倸鍊烽悞锕傛晪闂佺硶鏅滈〃濠傜暦濮樿泛骞㈡俊銈傚亾闂傚懏锕㈤弻鈥愁吋閸涱喖鏋犲銈忕导缁瑥顕ｉ崐鐔虹杸闁靛／鍜佹Х闂備礁鎲￠悧鏇㈠箠鎼淬劌绠氶柛顐犲劚閸愨偓闂佹悶鍎洪崜锕傚汲椤栫偞鐓曟繝濠傚暞濠€鏉棵归悪鈧崰妤€顕ラ崟顐悑濠㈣泛鑻粭锟犳煟閻橀亶妾烽柛濠冪墱閳ь剙鐏氱划鎾诲蓟? ///</summary>
        private void DrawSectionHeader(Listing_Standard listing, string title, System.Action resetAction, Color? titleColor = null)
        {
            Rect headerRect = listing.GetRect(28f);
            float buttonWidth = 80f;
            float buttonHeight = 24f;

            Rect titleRect = new Rect(headerRect.x, headerRect.y, headerRect.width - buttonWidth - 10f, headerRect.height);

            Color originalColor = GUI.color;
            if (titleColor.HasValue)
            {
                GUI.color = titleColor.Value;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(titleRect, title);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = originalColor;

            Rect lineRect = new Rect(headerRect.x, headerRect.y + headerRect.height - 2f, headerRect.width - buttonWidth - 10f, 2f);
            Widgets.DrawBoxSolid(lineRect, new Color(0.3f, 0.3f, 0.3f, 0.5f));

            Rect buttonRect = new Rect(headerRect.x + headerRect.width - buttonWidth, headerRect.y + 2f, buttonWidth, buttonHeight);
            Color prevColor = GUI.color;
            GUI.color = new Color(0.85f, 0.85f, 0.85f);

            if (Widgets.ButtonText(buttonRect, "RimChat_ResetToDefault".Translate()))
            {
                ShowResetConfirmationDialog(title, resetAction);
            }

            GUI.color = prevColor;
        }

        /// <summary>/// 闂備礁鎼€氼剚鏅舵禒瀣︽慨妯挎硾缁犳帡鏌曡箛鏇烆€屾俊鑼额嚙椤鈽夊▎妯煎姼濡炪倖鎹佸畷闈涒槈閻㈠壊鏁婃繛鍡樺劤閹鏌ｆ惔锝嗘毄妞ゃ垹锕幆渚€鎸婃径妯荤? ///</summary>
        private void ShowResetConfirmationDialog(string sectionName, System.Action resetAction)
        {
            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(
                "RimChat_ResetSectionConfirm".Translate(sectionName),
                () =>
                {
                    resetAction?.Invoke();
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                },
                true,
                "RimChat_ResetConfirmTitle".Translate()
            );
            Find.WindowStack.Add(dialog);
        }

        #region 闂備礁鎲￠懝鍓р偓姘煎墴瀹曡鎯旈妸銉ь槺闂佺粯鍨剁湁闁告帗甯掗…璺ㄦ崉閾忓墣褏绱掗鍛仯闁瑰嘲顑夋俊鍫曞幢濡厧骞嶆繝?
        /// <summary>/// 闂備浇顕栭崢褰掑垂瑜版崵鍥儓濠甸亶鏌ｉ悙瀵糕槈濠靛倹姊婚幏褰掓偄閻戞ê顎涢梺鍛婃寙閸涱喚鈧厽绻涢幋鐐村鞍婵＄偟鏅崚鎺楊敍濠婂嫬顎涢梺闈涚墕閹冲宕? ///</summary>
        private void ResetAIBehaviorToDefault()
        {
            EnableAIGoodwillAdjustment = true;
            EnableAIGiftSending = true;
            EnableAIWarDeclaration = true;
            EnableAIPeaceMaking = true;
            EnableAITradeCaravan = true;
            EnableAIAidRequest = true;
            EnableAIRaidRequest = true;
        }

        /// <summary>/// 闂備浇顕栭崢褰掑垂瑜版崵鍥嚑椤掍礁浠忓銈嗘尵閸嬫稑袙婵犲洦鍋ｅù锝囶焾閳锋棃鏌ｉ妶鍛棡缂佸顦叅妞ゅ繐妫楃粭锟犳煟閻橀亶妾烽柛濠冩礋閸┾偓? ///</summary>
        private void ResetRaidSettingsToDefault()
        {
            EnableRaidStrategy_ImmediateAttack = true;
            EnableRaidStrategy_ImmediateAttackSmart = true;
            EnableRaidStrategy_StageThenAttack = true;
            EnableRaidStrategy_ImmediateAttackSappers = true;
            EnableRaidStrategy_Siege = true;

            EnableRaidArrival_EdgeWalkIn = true;
            EnableRaidArrival_EdgeDrop = true;
            EnableRaidArrival_EdgeWalkInGroups = true;
            EnableRaidArrival_RandomDrop = false;
            EnableRaidArrival_CenterDrop = false;
        }

        /// <summary>/// 闂備浇顕栭崢褰掑垂瑜版崵鍥矗婢跺矂妾梺鍏间航閸庤鲸淇婇幎钘夌閺夊牆澧介悾铏亜閺冣偓濞叉粎妲愰弮鍫晩闁哄嫬绻掗ˇ鐗堟叏閹烘挾鈯曟い顓炵墦椤㈡ɑ绻濆顒傦紮? ///</summary>
        private void ResetGoodwillSettingsToDefault()
        {
            MaxGoodwillAdjustmentPerCall = 15;
            MaxDailyGoodwillAdjustment = 30;
            GoodwillCooldownTicks = 2500;
        }

        /// <summary>/// 闂備浇顕栭崢褰掑垂瑜版崵鍥嚑椤掑倻鐒奸梺鍏肩ゴ閺呮盯鍩涢弽顓熷仯濞达絿顭堥埛鏃堟煟閵堝懏顥炵紒瀣槸鐓ゆい蹇撴缁楋繝鏌ｉ悩閬嶆闁稿﹥娲熼崺鈧? ///</summary>
        private void ResetGiftSettingsToDefault()
        {
            MaxGiftSilverAmount = 1000;
            MaxGiftGoodwillGain = 10;
            GiftCooldownTicks = 60000;
        }

        /// <summary>/// 闂備浇顕栭崢褰掑垂瑜版崵鍥蓟閵夈儳顔岄梺鍝勵槹閸ㄤ絻顤呴梺鑽ゅС缁€浣规櫠娴犲鍋柛鈩冾焽閳绘梹绻涘顔荤敖閻㈩垱鐩幃瑙勬媴闂堟稈鍋撻弴銏犵劦? ///</summary>
        private void ResetAidSettingsToDefault()
        {
            MinGoodwillForAid = 40;
            AidCooldownTicks = 120000;
            AidDelayBaseTicks = 90000;
        }

        /// <summary>/// 闂備浇顕栭崢褰掑垂瑜版崵鍥蓟閵夈儳顓哄┑鈽嗗灠濠€閬嶅箰閵娿儮妲堥柟鐐▕椤庢鏌熼摎鍌氬祮闁绘侗鍠氶埀顒€婀辨刊顓㈠疮鎼达絿纾介柛鎰劤閺嬫瑩鎮归幇顔兼瀾妞ゎ亖鍋撳┑鈽嗗灡椤戞瑩宕ラ崶顒佺厱? ///</summary>
        private void ResetWarPeaceSettingsToDefault()
        {
            MaxGoodwillForWarDeclaration = -50;
            WarCooldownTicks = 60000;
            MaxPeaceCost = 5000;
            PeaceGoodwillReset = -20;
            PeaceCooldownTicks = 60000;
        }

        /// <summary>/// 闂備浇顕栭崢褰掑垂瑜版崵鍥蓟閵夈儳鏌堥梺绯曞墲缁嬫帟顤傞梺鑽ゅС缁€浣规櫠娴犲鍋柛鈩冾焽閳绘梹绻涘顔荤敖閻㈩垱鐩幃瑙勬媴闂堟稈鍋撻弴銏犵劦? ///</summary>
        private void ResetCaravanSettingsToDefault()
        {
            CaravanCooldownTicks = 90000;
            CaravanDelayBaseTicks = 135000;
        }

        /// <summary>/// 闂備浇顕栭崢褰掑垂瑜版崵鍥矗婢跺矈娴勯柣鐘叉处瑜板啴锝為妶澶嬪仯濞达絿顭堥埛鏃堟煟閵堝懏顥炵紒瀣槸鐓ゆい蹇撴缁楋繝鏌ｉ悩閬嶆闁稿﹥娲熼崺鈧? ///</summary>
        private void ResetQuestSettingsToDefault()
        {
            MinQuestCooldownDays = 7;
            MaxQuestCooldownDays = 12;
        }

        /// <summary>/// 闂備浇顕栭崢褰掑垂瑜版崵鍥嚑椤戣棄浜鹃柣鐔煎亰濡叉悂鏌涘▎蹇曠闁瑰嘲顑夊畷婊嗩槾闁哄鍊搁埥澶愬箻鐎涙ǜ浠㈢紓渚囧櫘閸ㄦ娊骞忕€ｎ喖围闁告侗浜滄禍? ///</summary>
        private void ResetSecuritySettingsToDefault()
        {
            EnableAPICallLogging = true;
            MaxAPICallsPerHour = 0;
        }

        /// <summary>/// 闂備浇顕栭崢褰掑垂瑜版崵?UI 闂佽崵濮崇粈浣规櫠娴犲鍋柛鈩冾焽閳绘梹绻涘顔荤敖閻㈩垱鐩幃瑙勬媴闂堟稈鍋撻弴銏犵劦? ///</summary>
        private void ResetUISettingsToDefault()
        {
            TypewriterSpeedMode = TypewriterSpeedMode.Standard;
            ReplaceCommsConsole = false;
        }

        /// <summary>/// 闂備浇顕栭崢褰掑垂瑜版崵鍥蓟閵夈儲宓嶉梺闈浤涢崘鈺冩瀮闂備胶绮…鍫ュ春閺嶎厼鐒垫い鎴炲缁佺増銇勯弮鈧ú婊呮閺冨牜鏁婇柡鍕箳椤︾増鎱ㄩ幒鎾垛姇妞ゎ厼鐗撻, 妯荤節濮橆剛锛? ///</summary>
        private void ResetPresenceSettingsToDefault()
        {
            EnableFactionPresenceStatus = true;
            PresenceCacheHours = 2f;
            PresenceForcedOfflineHours = 24f;
            PresenceNightBiasEnabled = true;
            PresenceNightStartHour = 22;
            PresenceNightEndHour = 6;
            PresenceNightOfflineBias = 0.65f;
            PresenceUseAdvancedProfiles = true;
            PresenceOnlineStart_Default = 7;
            PresenceOnlineDuration_Default = 12;
            PresenceOnlineStart_Neolithic = 8;
            PresenceOnlineDuration_Neolithic = 8;
            PresenceOnlineStart_Medieval = 8;
            PresenceOnlineDuration_Medieval = 10;
            PresenceOnlineStart_Industrial = 7;
            PresenceOnlineDuration_Industrial = 14;
            PresenceOnlineStart_Spacer = 6;
            PresenceOnlineDuration_Spacer = 18;
            PresenceOnlineStart_Ultra = 4;
            PresenceOnlineDuration_Ultra = 20;
            PresenceOnlineStart_Archotech = 4;
            PresenceOnlineDuration_Archotech = 20;
        }

        /// <summary>/// 闂傚倷鐒﹁ぐ鍐矓閸洘鍋柛鈩冪☉缁犮儵鏌嶈閸撶喎顕ｉ悽绋块唶缂佸搫瀚板濠氬礋椤掆偓婵洭鏌涢埡鍌ゆ畷缂佸顦叅妞ゅ繐妫楃粭锟犳煟閻橀亶妾烽柛濠冩礋閸┾偓妞ゆ帒鍊堕埀顒€顑囧Σ鎰枎閹邦喒鏀冲┑鐘绘涧閻楀﹤鈻撳畝鍕厽妞ゎ偒鍓欐俊铏圭磼椤垵澧寸€规洘顨婇幃鈩冩償椤斿吋娅嶉梻? ///</summary>
        private void ResetAILimitsToDefault()
        {
            ResetGoodwillSettingsToDefault();
            ResetGiftSettingsToDefault();
            ResetAidSettingsToDefault();
            ResetWarPeaceSettingsToDefault();
            ResetCaravanSettingsToDefault();
            ResetQuestSettingsToDefault();
            ResetSocialCircleSettingsToDefault();
            ResetSecuritySettingsToDefault();
            ResetPresenceSettingsToDefault();
            ResetNpcInitiatedDialogueSettings();
        }

        #endregion

        #endregion
    }
}



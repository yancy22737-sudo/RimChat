using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimDiplomacy.AI;

namespace RimDiplomacy.Config
{
    /// <summary>
    /// RimDiplomacy AI控制相关设置扩展
    /// 包含滚动选项卡和AI限制阈值配置
    /// </summary>
    public partial class RimDiplomacySettings : ModSettings
    {
        #region 序列化 - AI 控制设置

        void ExposeData_AI()
        {
            // 好感度限制
            Scribe_Values.Look(ref MaxGoodwillAdjustmentPerCall, "MaxGoodwillAdjustmentPerCall", 15);
            Scribe_Values.Look(ref MaxDailyGoodwillAdjustment, "MaxDailyGoodwillAdjustment", 30);
            Scribe_Values.Look(ref GoodwillCooldownTicks, "GoodwillCooldownTicks", 2500);

            // 礼物设置
            Scribe_Values.Look(ref MaxGiftSilverAmount, "MaxGiftSilverAmount", 1000);
            Scribe_Values.Look(ref MaxGiftGoodwillGain, "MaxGiftGoodwillGain", 10);
            Scribe_Values.Look(ref GiftCooldownTicks, "GiftCooldownTicks", 60000);

            // 援助设置
            Scribe_Values.Look(ref MinGoodwillForAid, "MinGoodwillForAid", 40);
            Scribe_Values.Look(ref AidCooldownTicks, "AidCooldownTicks", 120000);

            // 战争设置
            Scribe_Values.Look(ref MaxGoodwillForWarDeclaration, "MaxGoodwillForWarDeclaration", -50);
            Scribe_Values.Look(ref WarCooldownTicks, "WarCooldownTicks", 60000);

            // 和平设置
            Scribe_Values.Look(ref MaxPeaceCost, "MaxPeaceCost", 5000);
            Scribe_Values.Look(ref PeaceGoodwillReset, "PeaceGoodwillReset", -20);
            Scribe_Values.Look(ref PeaceCooldownTicks, "PeaceCooldownTicks", 60000);

            // 商队设置
            Scribe_Values.Look(ref CaravanCooldownTicks, "CaravanCooldownTicks", 90000);

            // AI行为开关
            Scribe_Values.Look(ref EnableAIGoodwillAdjustment, "EnableAIGoodwillAdjustment", true);
            Scribe_Values.Look(ref EnableAIGiftSending, "EnableAIGiftSending", true);
            Scribe_Values.Look(ref EnableAIWarDeclaration, "EnableAIWarDeclaration", true);
            Scribe_Values.Look(ref EnableAIPeaceMaking, "EnableAIPeaceMaking", true);
            Scribe_Values.Look(ref EnableAITradeCaravan, "EnableAITradeCaravan", true);
            Scribe_Values.Look(ref EnableAIAidRequest, "EnableAIAidRequest", true);

            // 安全设置
            Scribe_Values.Look(ref EnableAPICallLogging, "EnableAPICallLogging", true);
            Scribe_Values.Look(ref MaxAPICallsPerHour, "MaxAPICallsPerHour", 20);
        }

        #endregion

        #region UI绘制 - AI控制选项卡

        private Vector2 aiSettingsScrollPosition = Vector2.zero;

        private void DrawTab_AIControl(Rect rect)
        {
            float viewHeight = CalculateAIControlContentHeight(rect.width - 16f);
            Rect viewRect = new Rect(0, 0, rect.width - 16f, viewHeight);
            
            Widgets.BeginScrollView(rect, ref aiSettingsScrollPosition, viewRect);
            
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(new Rect(0, 0, viewRect.width, viewRect.height));

            DrawAIBehaviorToggles(listing);
            listing.Gap();

            DrawGoodwillSettings(listing);
            listing.Gap();

            DrawGiftSettings(listing);
            listing.Gap();

            DrawAidSettings(listing);
            listing.Gap();

            DrawWarPeaceSettings(listing);
            listing.Gap();

            DrawCaravanSettings(listing);
            listing.Gap();

            DrawSecuritySettings(listing);

            listing.End();
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 计算AI控制选项卡内容总高度
        /// </summary>
        private float CalculateAIControlContentHeight(float width)
        {
            float height = 0f;
            float lineHeight = 24f;
            float sectionGap = 12f;
            float sliderHeight = 28f;

            // AI行为设置分区 (标题 + 6个复选框)
            height += lineHeight + 4f; // 标题 + GapLine
            height += 6 * lineHeight;  // 6个复选框
            height += sectionGap;

            // 好感度设置分区 (标题 + 3个滑块 + 可能的警告)
            height += lineHeight + 4f;
            height += 3 * (lineHeight + sliderHeight); // 标签 + 滑块
            height += sectionGap;

            // 礼物设置分区 (标题 + 3个滑块)
            height += lineHeight + 4f;
            height += 3 * (lineHeight + sliderHeight);
            height += sectionGap;

            // 援助设置分区 (标题 + 2个滑块)
            height += lineHeight + 4f;
            height += 2 * (lineHeight + sliderHeight);
            height += sectionGap;

            // 战争与和平设置分区 (标题 + 5个滑块 + Gap)
            height += lineHeight + 4f;
            height += 5 * (lineHeight + sliderHeight) + lineHeight; // + Gap
            height += sectionGap;

            // 商队设置分区 (标题 + 1个滑块)
            height += lineHeight + 4f;
            height += 1 * (lineHeight + sliderHeight);
            height += sectionGap;

            // 安全设置分区 (标题 + 1个复选框 + 1个滑块 + 按钮)
            height += lineHeight + 4f;
            height += lineHeight; // 复选框
            height += lineHeight + sliderHeight; // 滑块
            height += lineHeight + 8f; // 按钮 + 间距

            return height + 20f; // 额外边距
        }

        /// <summary>
        /// AI行为总开关
        /// </summary>
        private void DrawAIBehaviorToggles(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "RimDiplomacy_AIBehaviorSettings".Translate(), ResetAIBehaviorToDefault);

            listing.CheckboxLabeled("RimDiplomacy_EnableAIGoodwillAdjustment".Translate(), ref EnableAIGoodwillAdjustment);
            listing.CheckboxLabeled("RimDiplomacy_EnableAIGiftSending".Translate(), ref EnableAIGiftSending);
            listing.CheckboxLabeled("RimDiplomacy_EnableAIWarDeclaration".Translate(), ref EnableAIWarDeclaration);
            listing.CheckboxLabeled("RimDiplomacy_EnableAIPeaceMaking".Translate(), ref EnableAIPeaceMaking);
            listing.CheckboxLabeled("RimDiplomacy_EnableAITradeCaravan".Translate(), ref EnableAITradeCaravan);
            listing.CheckboxLabeled("RimDiplomacy_EnableAIAidRequest".Translate(), ref EnableAIAidRequest);
        }

        /// <summary>
        /// 好感度调整设置
        /// </summary>
        private void DrawGoodwillSettings(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "RimDiplomacy_GoodwillSettings".Translate(), ResetGoodwillSettingsToDefault, new Color(0.8f, 0.9f, 1f));

            // 单次调整上限
            listing.Label($"RimDiplomacy_MaxGoodwillAdjustmentPerCall".Translate(MaxGoodwillAdjustmentPerCall));
            MaxGoodwillAdjustmentPerCall = (int)listing.Slider(MaxGoodwillAdjustmentPerCall, 0, 50);

            // 每日累计上限
            listing.Label($"RimDiplomacy_MaxDailyGoodwillAdjustment".Translate(MaxDailyGoodwillAdjustment));
            MaxDailyGoodwillAdjustment = (int)listing.Slider(MaxDailyGoodwillAdjustment, 0, 100);

            // 冷却时间（小时）
            float cooldownHours = GoodwillCooldownTicks / 2500f;
            listing.Label($"RimDiplomacy_GoodwillCooldown".Translate(cooldownHours.ToString("F1")));
            cooldownHours = listing.Slider(cooldownHours, 0f, 24f);
            GoodwillCooldownTicks = (int)(cooldownHours * 2500);

            // 警告提示
            if (MaxGoodwillAdjustmentPerCall > MaxDailyGoodwillAdjustment / 2)
            {
                GUI.color = Color.yellow;
                Text.Font = GameFont.Tiny;
                listing.Label("RimDiplomacy_GoodwillWarning".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        /// <summary>
        /// 礼物设置
        /// </summary>
        private void DrawGiftSettings(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "RimDiplomacy_GiftSettings".Translate(), ResetGiftSettingsToDefault, new Color(1f, 0.9f, 0.7f));

            // 最大白银数量
            listing.Label($"RimDiplomacy_MaxGiftSilverAmount".Translate(MaxGiftSilverAmount));
            MaxGiftSilverAmount = (int)listing.Slider(MaxGiftSilverAmount, 100, 5000);

            // 最大好感度收益
            listing.Label($"RimDiplomacy_MaxGiftGoodwillGain".Translate(MaxGiftGoodwillGain));
            MaxGiftGoodwillGain = (int)listing.Slider(MaxGiftGoodwillGain, 1, 25);

            // 冷却时间（天）
            float cooldownDays = GiftCooldownTicks / 60000f;
            listing.Label($"RimDiplomacy_GiftCooldown".Translate(cooldownDays.ToString("F1")));
            cooldownDays = listing.Slider(cooldownDays, 0.5f, 5f);
            GiftCooldownTicks = (int)(cooldownDays * 60000);
        }

        /// <summary>
        /// 援助设置
        /// </summary>
        private void DrawAidSettings(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "RimDiplomacy_AidSettings".Translate(), ResetAidSettingsToDefault, new Color(0.7f, 1f, 0.8f));

            // 最低好感度要求
            listing.Label($"RimDiplomacy_MinGoodwillForAid".Translate(MinGoodwillForAid));
            MinGoodwillForAid = (int)listing.Slider(MinGoodwillForAid, 0, 100);

            // 冷却时间（天）
            float cooldownDays = AidCooldownTicks / 60000f;
            listing.Label($"RimDiplomacy_AidCooldown".Translate(cooldownDays.ToString("F1")));
            cooldownDays = listing.Slider(cooldownDays, 1f, 7f);
            AidCooldownTicks = (int)(cooldownDays * 60000);
        }

        /// <summary>
        /// 战争与和平设置
        /// </summary>
        private void DrawWarPeaceSettings(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "RimDiplomacy_WarPeaceSettings".Translate(), ResetWarPeaceSettingsToDefault, new Color(1f, 0.7f, 0.7f));

            // 战争设置
            listing.Label($"RimDiplomacy_MaxGoodwillForWar".Translate(MaxGoodwillForWarDeclaration));
            MaxGoodwillForWarDeclaration = (int)listing.Slider(MaxGoodwillForWarDeclaration, -100, 0);

            float warCooldownDays = WarCooldownTicks / 60000f;
            listing.Label($"RimDiplomacy_WarCooldown".Translate(warCooldownDays.ToString("F1")));
            warCooldownDays = listing.Slider(warCooldownDays, 1f, 7f);
            WarCooldownTicks = (int)(warCooldownDays * 60000);

            listing.Gap();

            // 和平设置
            listing.Label($"RimDiplomacy_MaxPeaceCost".Translate(MaxPeaceCost));
            MaxPeaceCost = (int)listing.Slider(MaxPeaceCost, 0, 10000);

            listing.Label($"RimDiplomacy_PeaceGoodwillReset".Translate(PeaceGoodwillReset));
            PeaceGoodwillReset = (int)listing.Slider(PeaceGoodwillReset, -100, 0);

            float peaceCooldownDays = PeaceCooldownTicks / 60000f;
            listing.Label($"RimDiplomacy_PeaceCooldown".Translate(peaceCooldownDays.ToString("F1")));
            peaceCooldownDays = listing.Slider(peaceCooldownDays, 1f, 7f);
            PeaceCooldownTicks = (int)(peaceCooldownDays * 60000);
        }

        /// <summary>
        /// 商队设置
        /// </summary>
        private void DrawCaravanSettings(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "RimDiplomacy_CaravanSettings".Translate(), ResetCaravanSettingsToDefault, new Color(0.9f, 0.8f, 1f));

            float cooldownDays = CaravanCooldownTicks / 60000f;
            listing.Label($"RimDiplomacy_CaravanCooldown".Translate(cooldownDays.ToString("F1")));
            cooldownDays = listing.Slider(cooldownDays, 0.5f, 5f);
            CaravanCooldownTicks = (int)(cooldownDays * 60000);
        }

        /// <summary>
        /// 安全设置
        /// </summary>
        private void DrawSecuritySettings(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "RimDiplomacy_SecuritySettings".Translate(), ResetSecuritySettingsToDefault, new Color(1f, 0.9f, 0.5f));

            listing.CheckboxLabeled("RimDiplomacy_EnableAPICallLogging".Translate(), ref EnableAPICallLogging);

            listing.Label($"RimDiplomacy_MaxAPICallsPerHour".Translate(MaxAPICallsPerHour));
            MaxAPICallsPerHour = (int)listing.Slider(MaxAPICallsPerHour, 5, 100);
        }

        /// <summary>
        /// 绘制分区标题栏，包含标题和恢复默认按钮
        /// </summary>
        private void DrawSectionHeader(Listing_Standard listing, string title, System.Action resetAction, Color? titleColor = null)
        {
            Rect headerRect = listing.GetRect(28f);
            float buttonWidth = 80f;
            float buttonHeight = 24f;

            // 标题区域
            Rect titleRect = new Rect(headerRect.x, headerRect.y, headerRect.width - buttonWidth - 10f, headerRect.height);

            // 绘制标题颜色
            Color originalColor = GUI.color;
            if (titleColor.HasValue)
            {
                GUI.color = titleColor.Value;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(titleRect, title);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = originalColor;

            // 分隔线
            Rect lineRect = new Rect(headerRect.x, headerRect.y + headerRect.height - 2f, headerRect.width - buttonWidth - 10f, 2f);
            Widgets.DrawBoxSolid(lineRect, new Color(0.3f, 0.3f, 0.3f, 0.5f));

            // 恢复默认按钮
            Rect buttonRect = new Rect(headerRect.x + headerRect.width - buttonWidth, headerRect.y + 2f, buttonWidth, buttonHeight);
            Color prevColor = GUI.color;
            GUI.color = new Color(0.85f, 0.85f, 0.85f);

            if (Widgets.ButtonText(buttonRect, "RimDiplomacy_ResetToDefault".Translate()))
            {
                ShowResetConfirmationDialog(title, resetAction);
            }

            GUI.color = prevColor;
        }

        /// <summary>
        /// 显示恢复默认确认对话框
        /// </summary>
        private void ShowResetConfirmationDialog(string sectionName, System.Action resetAction)
        {
            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(
                "RimDiplomacy_ResetSectionConfirm".Translate(sectionName),
                () =>
                {
                    resetAction?.Invoke();
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                },
                true,
                "RimDiplomacy_ResetConfirmTitle".Translate()
            );
            Find.WindowStack.Add(dialog);
        }

        #region 各分区恢复默认方法

        /// <summary>
        /// 恢复AI行为设置为默认值
        /// </summary>
        private void ResetAIBehaviorToDefault()
        {
            EnableAIGoodwillAdjustment = true;
            EnableAIGiftSending = true;
            EnableAIWarDeclaration = true;
            EnableAIPeaceMaking = true;
            EnableAITradeCaravan = true;
            EnableAIAidRequest = true;
        }

        /// <summary>
        /// 恢复好感度设置为默认值
        /// </summary>
        private void ResetGoodwillSettingsToDefault()
        {
            MaxGoodwillAdjustmentPerCall = 15;
            MaxDailyGoodwillAdjustment = 30;
            GoodwillCooldownTicks = 2500;
        }

        /// <summary>
        /// 恢复礼物设置为默认值
        /// </summary>
        private void ResetGiftSettingsToDefault()
        {
            MaxGiftSilverAmount = 1000;
            MaxGiftGoodwillGain = 10;
            GiftCooldownTicks = 60000;
        }

        /// <summary>
        /// 恢复援助设置为默认值
        /// </summary>
        private void ResetAidSettingsToDefault()
        {
            MinGoodwillForAid = 40;
            AidCooldownTicks = 120000;
        }

        /// <summary>
        /// 恢复战争与和平设置为默认值
        /// </summary>
        private void ResetWarPeaceSettingsToDefault()
        {
            MaxGoodwillForWarDeclaration = -50;
            WarCooldownTicks = 60000;
            MaxPeaceCost = 5000;
            PeaceGoodwillReset = -20;
            PeaceCooldownTicks = 60000;
        }

        /// <summary>
        /// 恢复商队设置为默认值
        /// </summary>
        private void ResetCaravanSettingsToDefault()
        {
            CaravanCooldownTicks = 90000;
        }

        /// <summary>
        /// 恢复安全设置为默认值
        /// </summary>
        private void ResetSecuritySettingsToDefault()
        {
            EnableAPICallLogging = true;
            MaxAPICallsPerHour = 20;
        }

        /// <summary>
        /// 重置所有AI限制为默认值（保留用于兼容）
        /// </summary>
        private void ResetAILimitsToDefault()
        {
            ResetGoodwillSettingsToDefault();
            ResetGiftSettingsToDefault();
            ResetAidSettingsToDefault();
            ResetWarPeaceSettingsToDefault();
            ResetCaravanSettingsToDefault();
            ResetSecuritySettingsToDefault();
        }

        #endregion

        #endregion
    }
}

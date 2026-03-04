using System;
using RimWorld;
using UnityEngine;
using Verse;
using RimDiplomacy.UI;

namespace RimDiplomacy.Config
{
    public partial class RimDiplomacySettings : ModSettings
    {
        private Vector2 rpgSettingsScrollPosition = Vector2.zero;
        private EnhancedTextArea rpgSystemPromptTextArea;
        private EnhancedTextArea rpgDialoguePromptTextArea;

        private void DrawTab_RPGDialogue(Rect rect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);

            DrawRPGDialogueSettingsSection(listing);

            listing.End();
        }

        private void DrawRPGDialogueSettingsSection(Listing_Standard listing)
        {
            listing.Label("RimDiplomacy_RPGDialogueSettings".Translate());
            listing.GapLine();

            listing.CheckboxLabeled("RimDiplomacy_EnableRPGDialogue".Translate(), ref EnableRPGDialogue);
            listing.Gap(10f);

            if (EnableRPGDialogue)
            {
                // 初始化 RPG 提示词增强型文本框
                if (rpgSystemPromptTextArea == null)
                {
                    rpgSystemPromptTextArea = new EnhancedTextArea("RPGSystemPromptTextArea", MaxSystemPromptLength);
                    rpgSystemPromptTextArea.Text = RPGSystemPrompt;
                    rpgSystemPromptTextArea.OnTextChanged += (newText) => RPGSystemPrompt = newText;
                }
                if (rpgDialoguePromptTextArea == null)
                {
                    rpgDialoguePromptTextArea = new EnhancedTextArea("RPGDialoguePromptTextArea", MaxDialoguePromptLength);
                    rpgDialoguePromptTextArea.Text = RPGDialoguePrompt;
                    rpgDialoguePromptTextArea.OnTextChanged += (newText) => RPGDialoguePrompt = newText;
                }

                // 更新最大长度限制
                rpgSystemPromptTextArea.MaxLength = MaxSystemPromptLength;
                rpgDialoguePromptTextArea.MaxLength = MaxDialoguePromptLength;

                // RPG 系统提示词
                Rect sysLabelRect = listing.GetRect(24f);
                Widgets.Label(sysLabelRect, "RimDiplomacy_RPGSystemPromptLabel".Translate());
                if (Mouse.IsOver(sysLabelRect))
                {
                    TooltipHandler.TipRegion(sysLabelRect, "RimDiplomacy_RPGSystemPromptDesc".Translate());
                }

                float sysTextHeight = 120f;
                Rect sysTextRect = listing.GetRect(sysTextHeight);
                rpgSystemPromptTextArea.Draw(sysTextRect);
                RPGSystemPrompt = rpgSystemPromptTextArea.Text;

                listing.Gap(5f);

                // RPG 对话提示词模板
                Rect dlgLabelRect = listing.GetRect(24f);
                Widgets.Label(dlgLabelRect, "RimDiplomacy_RPGDialoguePromptLabel".Translate());
                if (Mouse.IsOver(dlgLabelRect))
                {
                    TooltipHandler.TipRegion(dlgLabelRect, "RimDiplomacy_RPGDialoguePromptDesc".Translate());
                }

                float dlgTextHeight = 120f;
                Rect dlgTextRect = listing.GetRect(dlgTextHeight);
                rpgDialoguePromptTextArea.Draw(dlgTextRect);
                RPGDialoguePrompt = rpgDialoguePromptTextArea.Text;

                listing.Gap(10f);

                // 保存按钮
                Rect saveRect = listing.GetRect(28f);
                bool canSave = !rpgSystemPromptTextArea.HasExceededLimit && !rpgDialoguePromptTextArea.HasExceededLimit;
                GUI.color = canSave ? new Color(0.3f, 0.8f, 0.3f) : Color.gray;
                if (Widgets.ButtonText(saveRect, "RimDiplomacy_SaveRPGPrompt".Translate()) && canSave)
                {
                    // 设置已经通过回调更新，这里主要是显示消息
                    Messages.Message("RimDiplomacy_RPGPromptSaved".Translate(), MessageTypeDefOf.NeutralEvent, false);
                }
                GUI.color = Color.white;
            }
        }
    }
}

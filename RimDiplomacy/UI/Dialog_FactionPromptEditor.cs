using UnityEngine;
using Verse;

namespace RimDiplomacy
{
    /// <summary>
    /// 派系提示词编辑窗口
    /// </summary>
    public class Dialog_FactionPromptEditor : Window
    {
        private readonly PromptConfig promptConfig;
        private string editingBuffer;

        public Dialog_FactionPromptEditor(PromptConfig config)
        {
            this.promptConfig = config;
            this.editingBuffer = config.SystemPrompt;
            this.doCloseButton = false;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(600f, 500f);

        public override void DoWindowContents(Rect inRect)
        {
            // Title
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 32f);
            Widgets.Label(titleRect, $"{promptConfig.Name} - {"RimDiplomacy_PromptEditor".Translate()}");
            Text.Font = GameFont.Small;

            float y = 40f;

            // Description
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect descRect = new Rect(inRect.x, y, inRect.width, Text.LineHeight);
            Widgets.Label(descRect, "RimDiplomacy_PromptEditorDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 25f;

            // Token count
            int tokenCount = EstimateTokenCount(editingBuffer);
            Text.Font = GameFont.Tiny;
            GUI.color = tokenCount > 2000 ? Color.red : Color.green;
            Rect tokenRect = new Rect(inRect.x, y, inRect.width, Text.LineHeight);
            Widgets.Label(tokenRect, "RimDiplomacy_TokenCount".Translate(tokenCount));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 20f;

            // Text area
            float textHeight = inRect.height - y - 50f;
            Rect textRect = new Rect(inRect.x, y, inRect.width, textHeight);
            
            GUI.BeginGroup(textRect);
            Rect innerRect = new Rect(0, 0, textRect.width - 16f, Mathf.Max(textHeight, Text.CalcHeight(editingBuffer, textRect.width - 20f) + 20f));
            
            editingBuffer = Widgets.TextArea(innerRect, editingBuffer);
            
            GUI.EndGroup();

            y += textHeight + 10f;

            // Buttons
            float btnWidth = 100f;
            float btnHeight = 35f;
            float btnY = inRect.yMax - btnHeight;

            // Save button
            Rect saveRect = new Rect(inRect.xMax - btnWidth * 2 - 10f, btnY, btnWidth, btnHeight);
            if (Widgets.ButtonText(saveRect, "RimDiplomacy_Save".Translate()))
            {
                promptConfig.SystemPrompt = editingBuffer;
                Close();
            }

            // Cancel button
            Rect cancelRect = new Rect(inRect.xMax - btnWidth, btnY, btnWidth, btnHeight);
            if (Widgets.ButtonText(cancelRect, "RimDiplomacy_Cancel".Translate()))
            {
                Close();
            }

            // Reset button
            Rect resetRect = new Rect(inRect.x, btnY, btnWidth, btnHeight);
            if (Widgets.ButtonText(resetRect, "RimDiplomacy_Reset".Translate()))
            {
                editingBuffer = GetDefaultFactionPrompt(promptConfig);
            }
        }

        private string GetDefaultFactionPrompt(PromptConfig config)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"You are the leader of {config.Name}.");
            sb.AppendLine("Respond to diplomatic interactions based on your faction's characteristics, current relationship with the player, and your leader's personality traits.");
            return sb.ToString();
        }

        private int EstimateTokenCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return text.Length / 4;
        }
    }
}

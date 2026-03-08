using System.Text;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// 依赖: RPG Action label映射 (GetRpgActionLabel) .
 /// 职责: 发送button旁问号提示与 RPG Actions Tooltip 渲染.
 ///</summary>
    public partial class Dialog_RPGPawnDialogue
    {
        private static readonly string[] RpgActionHintOrder =
        {
            "ExitDialogue",
            "ExitDialogueCooldown",
            "RomanceAttempt",
            "MarriageProposal",
            "Breakup",
            "Divorce",
            "Date",
            "TryGainMemory",
            "TryAffectSocialGoodwill",
            "ReduceResistance",
            "ReduceWill",
            "Recruit",
            "TryTakeOrderedJob",
            "TriggerIncident",
            "GrantInspiration"
        };

        private string rpgActionHintTooltipCache = string.Empty;

        private void DrawRpgPotentialActionsHint(Rect sendRect, float uiAlpha)
        {
            float visibleAlpha = Mathf.Clamp01(uiAlpha);
            if (visibleAlpha <= 0.01f)
            {
                return;
            }

            Rect hintRect = new Rect(sendRect.x - 16f, sendRect.yMax - 16f, 14f, 14f);
            bool hovered = Mouse.IsOver(hintRect);
            float targetAlpha = hovered ? Mathf.Max(visibleAlpha, 0.72f) : Mathf.Max(visibleAlpha * 0.55f, 0.2f);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.9f, 0.92f, 1f, targetAlpha);
            Widgets.Label(hintRect, "?");

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;

            TooltipHandler.TipRegion(hintRect, GetRpgPotentialActionsTooltipText());
        }

        private string GetRpgPotentialActionsTooltipText()
        {
            if (!string.IsNullOrEmpty(rpgActionHintTooltipCache))
            {
                return rpgActionHintTooltipCache;
            }

            var sb = new StringBuilder();
            sb.AppendLine("RimChat_ActionsHint_RpgTitle".Translate());
            foreach (string actionName in RpgActionHintOrder)
            {
                sb.AppendLine("- " + GetRpgActionLabel(actionName));
            }

            rpgActionHintTooltipCache = sb.ToString().TrimEnd();
            return rpgActionHintTooltipCache;
        }
    }
}

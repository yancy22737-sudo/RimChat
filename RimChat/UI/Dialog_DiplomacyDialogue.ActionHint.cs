using System.Collections.Generic;
using System.Text;
using RimChat.DiplomacySystem;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// 依赖: ApiActionEligibilityService, 派系上下文。
    /// 职责: 发送按钮旁问号提示与外交 Actions Tooltip 渲染。
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private static readonly string[] ActionHintOrder =
        {
            "adjust_goodwill",
            "send_gift",
            "request_aid",
            "declare_war",
            "make_peace",
            "request_caravan",
            "request_raid",
            "trigger_incident",
            "create_quest",
            "reject_request",
            "publish_public_post",
            "exit_dialogue",
            "go_offline",
            "set_dnd"
        };

        private int actionHintTooltipCacheTick = -999999;
        private string actionHintTooltipCache = string.Empty;

        private void DrawPotentialActionsHint(Rect sendRect)
        {
            Rect hintRect = new Rect(sendRect.x - 16f, sendRect.yMax - 16f, 14f, 14f);
            bool hovered = Mouse.IsOver(hintRect);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = hovered
                ? new Color(0.82f, 0.9f, 1f, 0.78f)
                : new Color(0.82f, 0.9f, 1f, 0.35f);
            Widgets.Label(hintRect, "?");

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;

            TooltipHandler.TipRegion(hintRect, GetPotentialActionsTooltipText());
        }

        private string GetPotentialActionsTooltipText()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (!string.IsNullOrEmpty(actionHintTooltipCache) && currentTick - actionHintTooltipCacheTick <= 120)
            {
                return actionHintTooltipCache;
            }

            var sb = new StringBuilder();
            sb.AppendLine("RimChat_ActionsHint_DiplomacyTitle".Translate());

            Dictionary<string, ActionValidationResult> allowedActions = ApiActionEligibilityService.Instance?.GetAllowedActions(faction);
            if (allowedActions == null || allowedActions.Count == 0)
            {
                sb.Append("RimChat_ActionsHint_None".Translate());
            }
            else
            {
                string statusAvailable = "RimChat_ActionsHint_StatusAvailable".Translate();
                string statusBlocked = "RimChat_ActionsHint_StatusBlocked".Translate();
                foreach (string actionType in ActionHintOrder)
                {
                    if (!allowedActions.TryGetValue(actionType, out ActionValidationResult validation))
                    {
                        continue;
                    }

                    string label = GetDiplomacyActionHintLabel(actionType);
                    string status = validation != null && validation.Allowed ? statusAvailable : statusBlocked;
                    sb.AppendLine($"- {label} [{status}]");
                }
            }

            actionHintTooltipCache = sb.ToString().TrimEnd();
            actionHintTooltipCacheTick = currentTick;
            return actionHintTooltipCache;
        }

        private string GetDiplomacyActionHintLabel(string actionType)
        {
            string key = $"RimChat_DiplomacyActionLabel_{actionType}";
            TaggedString translated = key.Translate();
            return translated.RawText == key ? actionType : translated.RawText;
        }
    }
}

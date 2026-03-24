using System.Collections.Generic;
using System.Text;
using RimChat.AI;
using RimChat.DiplomacySystem;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// 依赖: ApiActionEligibilityService, factioncontext.
 /// 职责: 发送button旁问号提示与diplomacy Actions Tooltip 渲染.
 ///</summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private static readonly HashSet<string> HiddenActionHintTypes = new HashSet<string>
        {
            AIActionNames.SendGift,
            AIActionNames.TriggerIncident,
            AIActionNames.ExitDialogue,
            AIActionNames.GoOffline,
            AIActionNames.SetDnd
        };

        private static readonly string[] ActionHintOrder =
        {
            AIActionNames.AdjustGoodwill,
            AIActionNames.SendGift,
            AIActionNames.RequestAid,
            AIActionNames.DeclareWar,
            AIActionNames.MakePeace,
            AIActionNames.RequestCaravan,
            AIActionNames.RequestRaid,
            AIActionNames.RequestItemAirdrop,
            AIActionNames.PayPrisonerRansom,
            AIActionNames.TriggerIncident,
            AIActionNames.CreateQuest,
            AIActionNames.RejectRequest,
            AIActionNames.PublishPublicPost,
            AIActionNames.ExitDialogue,
            AIActionNames.GoOffline,
            AIActionNames.SetDnd
        };

        private int actionHintTooltipCacheTick = -999999;
        private string actionHintTooltipCache = string.Empty;

        private void DrawPotentialActionsHint(Rect sendRect)
        {
            Rect hintRect = new Rect(sendRect.xMax - 16f, sendRect.yMax + 2f, 24f, 18f);
            bool hovered = Mouse.IsOver(hintRect);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = hovered
                ? new Color(0.84f, 0.92f, 1f, 0.9f)
                : new Color(0.84f, 0.92f, 1f, 0.56f);
            Widgets.Label(hintRect, "[?]");

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
                    if (HiddenActionHintTypes.Contains(actionType))
                    {
                        continue;
                    }

                    if (!allowedActions.TryGetValue(actionType, out ActionValidationResult validation))
                    {
                        continue;
                    }

                    string label = GetDiplomacyActionHintLabel(actionType);
                    bool isAllowed = validation != null && validation.Allowed;
                    string status = isAllowed ? statusAvailable : statusBlocked;
                    sb.AppendLine(BuildActionHintLine(label, status, isAllowed));
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

        private static string BuildActionHintLine(string label, string status, bool isAllowed)
        {
            string line = $"- {label} [{status}]";
            return isAllowed ? line : $"<color=#8F99A8>{line}</color>";
        }
    }
}

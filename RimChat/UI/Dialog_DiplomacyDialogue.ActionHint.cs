using System;
using System.Collections.Generic;
using System.Text;
using RimChat.AI;
using RimChat.DiplomacySystem;
using RimWorld;
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
            AIActionNames.RequestInfo,
            AIActionNames.PayPrisonerRansom,
            AIActionNames.TriggerIncident,
            AIActionNames.CreateQuest,
            AIActionNames.RejectRequest,
            AIActionNames.PublishPublicPost,
            AIActionNames.ExitDialogue,
            AIActionNames.GoOffline,
            AIActionNames.SetDnd
        };

        private static readonly Dictionary<string, string> ActionReasonKeyMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["aid_not_ally"] = "RimChat_ActionsHint_Reason_AidNotAlly",
            ["aid_goodwill_too_low"] = "RimChat_ActionsHint_Reason_GoodwillTooLow",
            ["feature_disabled"] = "RimChat_ActionsHint_Reason_FeatureDisabled",
            ["invalid_faction"] = "RimChat_ActionsHint_Reason_InvalidFaction",
            ["airdrop_need_required"] = "RimChat_ActionsHint_Reason_AirdropNeedRequired",
            ["airdrop_payment_items_required"] = "RimChat_ActionsHint_Reason_AirdropPaymentRequired",
            ["airdrop_scenario_invalid"] = "RimChat_ActionsHint_Reason_AirdropScenarioInvalid",
            ["caravan_hostile"] = "RimChat_ActionsHint_Reason_CaravanHostile",
            ["raid_not_hostile"] = "RimChat_ActionsHint_Reason_RaidNotHostile",
            ["already_hostile"] = "RimChat_ActionsHint_Reason_AlreadyHostile",
            ["war_goodwill_too_high"] = "RimChat_ActionsHint_Reason_WarGoodwillTooHigh",
            ["not_at_war"] = "RimChat_ActionsHint_Reason_NotAtWar",
            ["projected_goodwill_below_zero"] = "RimChat_ActionsHint_Reason_ProjectedGoodwillBelowZero",
            ["no_eligible_quests"] = "RimChat_ActionsHint_Reason_NoEligibleQuests",
            ["peace_talk_only_range"] = "RimChat_ActionsHint_Reason_PeaceTalkOnlyRange",
            ["request_info_type_invalid"] = "RimChat_ActionsHint_Reason_RequestInfoTypeInvalid",
            ["ransom_target_required"] = "RimChat_ActionsHint_Reason_RansomTargetRequired",
            ["ransom_offer_required"] = "RimChat_ActionsHint_Reason_RansomOfferRequired",
            ["ransom_invalid_mode"] = "RimChat_ActionsHint_Reason_RansomInvalidMode",
            ["ransom_target_not_found"] = "RimChat_ActionsHint_Reason_RansomTargetNotFound",
            ["ransom_target_not_eligible"] = "RimChat_ActionsHint_Reason_RansomTargetNotEligible",
            ["feature_in_development"] = "RimChat_ActionsHint_Reason_FeatureInDevelopment",
            ["unknown_action"] = "RimChat_ActionsHint_Reason_UnknownAction"
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
                    sb.AppendLine(BuildActionHintLine(label, status, isAllowed, validation));
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

        private static string BuildActionHintLine(string label, string status, bool isAllowed, ActionValidationResult validation = null)
        {
            string reasonSuffix = "";
            if (!isAllowed && validation != null)
            {
                reasonSuffix = " " + GetLocalizedValidationReason(validation);
            }

            string line = $"- {label} [{status}]{reasonSuffix}";
            return isAllowed ? line : $"<color=#8F99A8>{line}</color>";
        }

        private static string GetLocalizedValidationReason(ActionValidationResult validation)
        {
            if (validation == null)
            {
                return "";
            }

            string code = (validation.Code ?? string.Empty).Trim();
            int remainingSeconds = validation.RemainingSeconds;
            if (IsCooldownCode(code) && remainingSeconds > 0)
            {
                return FormatCooldownReason(code, remainingSeconds);
            }

            if (ActionReasonKeyMap.TryGetValue(code, out string key))
            {
                TaggedString translated = key.Translate();
                if (!string.Equals(translated.RawText, key, StringComparison.Ordinal))
                {
                    return translated;
                }
            }

            if (!string.IsNullOrWhiteSpace(validation.Message))
            {
                return validation.Message.Trim();
            }

            if (remainingSeconds > 0)
            {
                string cooldownReason = FormatCooldownReason(code, remainingSeconds);
                if (!string.IsNullOrWhiteSpace(cooldownReason))
                {
                    return cooldownReason;
                }
            }

            return "RimChat_ActionsHint_Reason_Generic".Translate();
        }

        private static bool IsCooldownCode(string code)
        {
            return !string.IsNullOrWhiteSpace(code) &&
                   code.EndsWith("_cooldown", StringComparison.Ordinal);
        }

        private static bool IsGameTimeCooldownCode(string code)
        {
            return string.Equals(code, "airdrop_cooldown", StringComparison.Ordinal);
        }

        private static string FormatCooldownReason(string code, int remainingSeconds)
        {
            if (remainingSeconds <= 0)
            {
                return "";
            }

            if (IsGameTimeCooldownCode(code))
            {
                return FormatGameTimeCooldownReason(remainingSeconds);
            }

            if (remainingSeconds >= 86400)
            {
                int days = remainingSeconds / 86400;
                return "RimChat_ActionsHint_CooldownDays".Translate(days);
            }

            if (remainingSeconds >= 3600)
            {
                int hours = remainingSeconds / 3600;
                return "RimChat_ActionsHint_CooldownHours".Translate(hours);
            }

            int minutes = remainingSeconds / 60;
            return "RimChat_ActionsHint_CooldownMinutes".Translate(minutes);
        }

        private static string FormatGameTimeCooldownReason(int remainingSeconds)
        {
            int remainingTicks = Math.Max(0, remainingSeconds) * 60;
            if (remainingTicks <= 0)
            {
                return "";
            }

            int days = remainingTicks / GenDate.TicksPerDay;
            int remainderTicks = remainingTicks % GenDate.TicksPerDay;
            int hours = Mathf.CeilToInt(remainderTicks / (float)GenDate.TicksPerHour);
            if (days > 0)
            {
                if (hours > 0)
                {
                    return "RimChat_ActionsHint_CooldownGameDaysHours".Translate(days, hours);
                }

                return "RimChat_ActionsHint_CooldownGameDays".Translate(days);
            }

            int remainingHours = Mathf.Max(1, Mathf.CeilToInt(remainingTicks / (float)GenDate.TicksPerHour));
            return "RimChat_ActionsHint_CooldownGameHours".Translate(remainingHours);
        }
    }
}

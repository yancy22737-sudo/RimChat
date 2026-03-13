using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Core;
using RimChat.Relation;
using RimChat.Util;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Centralized API action eligibility and quest template validation.
 ///</summary>
    public sealed class ApiActionEligibilityService
    {
        private static ApiActionEligibilityService _instance;
        public static ApiActionEligibilityService Instance => _instance ?? (_instance = new ApiActionEligibilityService());

        private static readonly string[] SupportedActions =
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
            "send_image",
            "reject_request",
            "publish_public_post",
            "exit_dialogue",
            "go_offline",
            "set_dnd"
        };

        private static readonly string[] SupportedQuestDefs =
        {
            "OpportunitySite_ItemStash",
            "TradeRequest",
            "OpportunitySite_PeaceTalks",
            "PawnLend",
            "ThreatReward_Raid_MiscReward",
            "Hospitality_Refugee",
            "BestowingCeremony"
        };

        private static readonly HashSet<string> BanditCampAllowedFactionDefs = new HashSet<string>
        {
            "Empire",
            "OutlanderCivil",
            "OutlanderRough"
        };

        // Safety-first policy: disable templates with recurring technical failures in runtime.
        private static readonly HashSet<string> HighRiskQuestTemplates = new HashSet<string>(StringComparer.Ordinal)
        {
            "OpportunitySite_ItemStash",
            "AncientComplex_Mission",
            "Mission_BanditCamp"
        };

        private const int PeaceTalkOnlyMinGoodwill = -50;
        private const int MakePeaceReenabledMinGoodwill = -20;
        private const string PeaceTalkQuestDefName = "OpportunitySite_PeaceTalks";

        private ApiActionEligibilityService()
        {
        }

        public Dictionary<string, ActionValidationResult> GetAllowedActions(Faction faction)
        {
            var result = new Dictionary<string, ActionValidationResult>(StringComparer.OrdinalIgnoreCase);
            foreach (string action in SupportedActions)
            {
                result[action] = ValidateActionExecution(faction, action, null);
            }
            return result;
        }

        public ActionValidationResult ValidateActionExecution(Faction faction, string actionType, Dictionary<string, object> parameters)
        {
            if (faction == null)
            {
                return ActionValidationResult.Denied("invalid_faction", "Faction cannot be null");
            }

            if (!IsFeatureEnabled(actionType))
            {
                return ActionValidationResult.Denied("feature_disabled", $"Feature {actionType} is disabled in settings");
            }

            switch (actionType)
            {
                case "request_aid":
                    if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Ally)
                    {
                        return ActionValidationResult.Denied("aid_not_ally", "Can only request aid from allied factions");
                    }
                    {
                        int minGoodwill = RimChatMod.Instance?.InstanceSettings?.MinGoodwillForAid ?? 0;
                        if (faction.PlayerGoodwill < minGoodwill)
                        {
                            return ActionValidationResult.Denied("aid_goodwill_too_low", $"Need at least {minGoodwill} goodwill to request aid");
                        }
                    }
                    {
                        ActionValidationResult projectedGoodwill = ValidateProjectedGoodwillFloor(
                            faction,
                            ResolveDialogueActionTypeForProjectedGoodwill(actionType, parameters));
                        if (!projectedGoodwill.Allowed)
                        {
                            return projectedGoodwill;
                        }
                    }
                    return ValidateCooldown(faction, "RequestAid", "aid_cooldown");

                case "request_caravan":
                    if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
                    {
                        return ActionValidationResult.Denied("caravan_hostile", "Cannot request caravan from hostile faction");
                    }
                    {
                        ActionValidationResult projectedGoodwill = ValidateProjectedGoodwillFloor(
                            faction,
                            ResolveDialogueActionTypeForProjectedGoodwill(actionType, parameters));
                        if (!projectedGoodwill.Allowed)
                        {
                            return projectedGoodwill;
                        }
                    }
                    return ValidateCooldown(faction, "RequestTradeCaravan", "caravan_cooldown");

                case "request_raid":
                    if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
                    {
                        return ActionValidationResult.Denied("raid_not_hostile", "AI can only launch raids if the faction is hostile to the player");
                    }
                    return ValidateCooldown(faction, "RequestRaid", "raid_cooldown");

                case "declare_war":
                    if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
                    {
                        return ActionValidationResult.Denied("already_hostile", "Already at war with this faction");
                    }
                    {
                        int maxGoodwill = RimChatMod.Instance?.InstanceSettings?.MaxGoodwillForWarDeclaration ?? 0;
                        if (faction.PlayerGoodwill > maxGoodwill)
                        {
                            return ActionValidationResult.Denied("war_goodwill_too_high", $"Cannot declare war with goodwill above {maxGoodwill}");
                        }
                    }
                    return ValidateCooldown(faction, "DeclareWar", "war_cooldown");

                case "make_peace":
                    if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
                    {
                        return ActionValidationResult.Denied("not_at_war", "Not at war with this faction");
                    }
                    {
                        ActionValidationResult peacePolicy = ValidateMakePeaceGoodwillPolicy(faction);
                        if (!peacePolicy.Allowed)
                        {
                            return peacePolicy;
                        }
                    }
                    return ValidateCooldown(faction, "MakePeace", "peace_cooldown");

                case "adjust_goodwill":
                    return ValidateCooldown(faction, "AdjustGoodwill", "goodwill_cooldown");

                case "send_gift":
                    return ValidateCooldown(faction, "SendGift", "gift_cooldown");

                case "create_quest":
                    {
                        ActionValidationResult cooldown = ValidateCooldown(faction, "CreateQuest", "quest_cooldown");
                        if (!cooldown.Allowed) return cooldown;

                        ActionValidationResult projectedGoodwill = ValidateProjectedGoodwillFloor(
                            faction,
                            ResolveDialogueActionTypeForProjectedGoodwill(actionType, parameters));
                        if (!projectedGoodwill.Allowed)
                        {
                            return projectedGoodwill;
                        }

                        string questDefName = TryReadStringParameter(parameters, "questDefName");
                        ActionValidationResult peaceTalkOnly = ValidatePeaceTalkOnlyQuestPolicy(faction, questDefName);
                        if (!peaceTalkOnly.Allowed)
                        {
                            return peaceTalkOnly;
                        }

                        var available = GetAvailableQuestDefsForFaction(faction);
                        if (available.Count == 0)
                        {
                            return ActionValidationResult.Denied("no_eligible_quests", $"No eligible quest templates are available for faction '{faction.Name}'.");
                        }

                        return ActionValidationResult.AllowedResult();
                    }

                case "trigger_incident":
                case "reject_request":
                case "publish_public_post":
                case "exit_dialogue":
                case "go_offline":
                case "set_dnd":
                    return ActionValidationResult.AllowedResult();

                case "send_image":
                    {
                        bool isPrecheck = parameters == null || parameters.Count == 0;
                        string templateId = TryReadStringParameter(parameters, "template_id");
                        if (string.IsNullOrWhiteSpace(templateId))
                        {
                            templateId = TryReadStringParameter(parameters, "templateId");
                        }

                        templateId = ResolveExistingImageTemplateId(templateId);
                        if (string.IsNullOrWhiteSpace(templateId))
                        {
                            templateId = GetDefaultEnabledImageTemplateId();
                            if (string.IsNullOrWhiteSpace(templateId))
                            {
                                if (isPrecheck)
                                {
                                    return ActionValidationResult.Denied("template_unavailable", "send_image has no enabled image template.");
                                }

                                return ActionValidationResult.Denied("template_required", "send_image requires parameter 'template_id'.");
                            }
                        }

                        if (!IsEnabledImageTemplate(templateId))
                        {
                            return ActionValidationResult.Denied("template_missing", $"send_image template '{templateId}' does not exist or is disabled.");
                        }

                        return ActionValidationResult.AllowedResult();
                    }
            }

            return ActionValidationResult.Denied("unknown_action", $"Unknown action type: {actionType}");
        }

        private static ActionValidationResult ValidateProjectedGoodwillFloor(
            Faction faction,
            DialogueGoodwillCost.DialogueActionType? actionType)
        {
            if (faction == null || !actionType.HasValue)
            {
                return ActionValidationResult.AllowedResult();
            }

            int fixedCost = DialogueGoodwillCost.GetBaseValue(actionType.Value);
            int projectedGoodwill = faction.PlayerGoodwill + fixedCost;
            if (projectedGoodwill >= 0)
            {
                return ActionValidationResult.AllowedResult();
            }

            return ActionValidationResult.Denied(
                "projected_goodwill_below_zero",
                $"Blocked because fixed goodwill cost {fixedCost} would reduce goodwill from {faction.PlayerGoodwill} to {projectedGoodwill}, below 0.");
        }

        private static DialogueGoodwillCost.DialogueActionType? ResolveDialogueActionTypeForProjectedGoodwill(
            string actionType,
            Dictionary<string, object> parameters)
        {
            switch (actionType)
            {
                case "request_caravan":
                    return DialogueGoodwillCost.DialogueActionType.RequestCaravan;
                case "create_quest":
                    return DialogueGoodwillCost.DialogueActionType.CreateQuest;
                case "request_aid":
                    return ResolveAidDialogueActionType(parameters);
                default:
                    return null;
            }
        }

        private static DialogueGoodwillCost.DialogueActionType ResolveAidDialogueActionType(Dictionary<string, object> parameters)
        {
            string aidType = TryReadStringParameter(parameters, "type");
            switch ((aidType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "medical":
                    return DialogueGoodwillCost.DialogueActionType.RequestMedicalAid;
                case "resources":
                case "resource":
                    return DialogueGoodwillCost.DialogueActionType.RequestResourceAid;
                default:
                    return DialogueGoodwillCost.DialogueActionType.RequestMilitaryAid;
            }
        }

        private static string TryReadStringParameter(Dictionary<string, object> parameters, string key)
        {
            if (parameters == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            if (!parameters.TryGetValue(key, out object value) || value == null)
            {
                return string.Empty;
            }

            return value.ToString();
        }

        public QuestValidationResult ValidateCreateQuest(Faction faction, string questDefName, Dictionary<string, object> parameters)
        {
            if (faction == null)
            {
                return QuestValidationResult.Denied("invalid_faction", "Faction cannot be null");
            }

            ActionValidationResult actionValidation = ValidateActionExecution(faction, "create_quest", parameters);
            if (!actionValidation.Allowed)
            {
                return QuestValidationResult.Denied(actionValidation.Code, actionValidation.Message, actionValidation.RemainingSeconds);
            }

            if (string.IsNullOrWhiteSpace(questDefName))
            {
                return QuestValidationResult.Denied("quest_def_required", "create_quest requires a valid questDefName from the injected allowed list.");
            }

            if (IsAncientQuestTemplateName(questDefName))
            {
                return QuestValidationResult.Denied("ancient_quest_disabled", $"Quest '{questDefName}' is disabled by safety policy and cannot be created.");
            }

            if (DefDatabase<QuestScriptDef>.GetNamedSilentFail(questDefName) == null)
            {
                return QuestValidationResult.Denied("quest_template_missing", $"Quest template '{questDefName}' is missing or not a QuestScriptDef.");
            }

            if (!TryValidateQuestTemplateForFaction(faction, questDefName, out string code, out string message))
            {
                return QuestValidationResult.Denied(code, message);
            }

            return QuestValidationResult.AllowedResult(questDefName);
        }

        public List<QuestTemplateEligibility> GetQuestEligibilityReport(Faction faction)
        {
            var report = new List<QuestTemplateEligibility>();
            foreach (string questDefName in SupportedQuestDefs)
            {
                if (TryValidateQuestTemplateForFaction(faction, questDefName, out string code, out string message))
                {
                    report.Add(new QuestTemplateEligibility
                    {
                        QuestDefName = questDefName,
                        Allowed = true,
                        Code = "allowed",
                        Message = "Allowed"
                    });
                }
                else
                {
                    report.Add(new QuestTemplateEligibility
                    {
                        QuestDefName = questDefName,
                        Allowed = false,
                        Code = code,
                        Message = message
                    });
                }
            }
            return report;
        }

        public List<string> GetAvailableQuestDefsForFaction(Faction faction)
        {
            return GetQuestEligibilityReport(faction)
                .Where(x => x.Allowed)
                .Select(x => x.QuestDefName)
                .ToList();
        }

        private bool TryValidateQuestTemplateForFaction(Faction faction, string questDefName, out string code, out string message)
        {
            code = "allowed";
            message = "Allowed";

            if (faction == null)
            {
                code = "invalid_faction";
                message = "Faction cannot be null";
                return false;
            }

            if (DefDatabase<QuestScriptDef>.GetNamedSilentFail(questDefName) == null)
            {
                code = "quest_template_missing";
                message = $"Quest template '{questDefName}' is missing.";
                return false;
            }

            if (faction.def != null && faction.def.permanentEnemy)
            {
                code = "permanent_enemy_blocked";
                message = $"Faction '{faction.Name}' is permanently hostile and cannot issue diplomacy quests.";
                return false;
            }

            if (IsAncientQuestTemplateName(questDefName) || HighRiskQuestTemplates.Contains(questDefName))
            {
                code = "quest_template_high_risk_disabled";
                message = $"Quest '{questDefName}' is disabled by safety policy due to technical risk.";
                return false;
            }

            switch (questDefName)
            {
                case "TradeRequest":
                    if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
                    {
                        code = "trade_hostile";
                        message = $"Quest '{questDefName}' requires a non-hostile faction.";
                        return false;
                    }
                    if (!HasSettlement(faction))
                    {
                        code = "trade_no_settlement";
                        message = $"Quest '{questDefName}' requires at least one settlement for faction '{faction.Name}'.";
                        return false;
                    }
                    break;

                case "OpportunitySite_PeaceTalks":
                    if (!HasFactionLeader(faction))
                    {
                        code = "peace_no_leader";
                        message = $"Quest '{questDefName}' requires a valid faction leader.";
                        return false;
                    }
                    break;

                case "AncientComplex_Mission":
                    if (!DLCCompatibility.IsIdeologyActive)
                    {
                        code = "ideology_required";
                        message = $"Quest '{questDefName}' requires Ideology DLC.";
                        return false;
                    }
                    if (!HasFactionLeader(faction))
                    {
                        code = "ancient_no_leader";
                        message = $"Quest '{questDefName}' requires a valid faction leader.";
                        return false;
                    }
                    break;

                case "Mission_BanditCamp":
                    if (!DLCCompatibility.IsRoyaltyActive)
                    {
                        code = "royalty_required";
                        message = $"Quest '{questDefName}' requires Royalty DLC.";
                        return false;
                    }
                    if (!BanditCampAllowedFactionDefs.Contains(faction.def?.defName ?? string.Empty))
                    {
                        code = "banditcamp_faction_not_supported";
                        message = $"Quest '{questDefName}' only supports faction defs: Empire, OutlanderCivil, OutlanderRough.";
                        return false;
                    }
                    break;

                case "PawnLend":
                    if (!DLCCompatibility.IsRoyaltyActive)
                    {
                        code = "royalty_required";
                        message = $"Quest '{questDefName}' requires Royalty DLC.";
                        return false;
                    }
                    if (faction.def == null || faction.def.techLevel < TechLevel.Industrial)
                    {
                        code = "pawnlend_tech_too_low";
                        message = $"Quest '{questDefName}' requires Industrial+ faction tech level.";
                        return false;
                    }
                    break;

                case "ThreatReward_Raid_MiscReward":
                case "Hospitality_Refugee":
                case "BestowingCeremony":
                    if (!DLCCompatibility.IsRoyaltyActive)
                    {
                        code = "royalty_required";
                        message = $"Quest '{questDefName}' requires Royalty DLC.";
                        return false;
                    }
                    if (!string.Equals(faction.def?.defName, "Empire", StringComparison.Ordinal))
                    {
                        code = "empire_only";
                        message = $"Quest '{questDefName}' is restricted to Empire faction in this integration.";
                        return false;
                    }
                    break;
            }

            if (IsInPeaceTalkOnlyRange(faction) &&
                !string.Equals(questDefName, PeaceTalkQuestDefName, StringComparison.Ordinal))
            {
                code = "peace_talk_only_range";
                message = $"Current goodwill {faction.PlayerGoodwill} is in [{PeaceTalkOnlyMinGoodwill},{MakePeaceReenabledMinGoodwill - 1}]. Only quest '{PeaceTalkQuestDefName}' is allowed.";
                return false;
            }

            return true;
        }

        private static ActionValidationResult ValidateMakePeaceGoodwillPolicy(Faction faction)
        {
            if (faction == null)
            {
                return ActionValidationResult.Denied("invalid_faction", "Faction cannot be null");
            }

            int goodwill = faction.PlayerGoodwill;
            if (goodwill < PeaceTalkOnlyMinGoodwill)
            {
                return ActionValidationResult.Denied(
                    "peace_goodwill_too_low",
                    $"Direct peace is blocked because goodwill is {goodwill} (< {PeaceTalkOnlyMinGoodwill}). Hostility is too deep for an immediate treaty.");
            }

            if (goodwill < MakePeaceReenabledMinGoodwill)
            {
                return ActionValidationResult.Denied(
                    "peace_talk_required",
                    $"Direct peace is blocked because goodwill is {goodwill} in [{PeaceTalkOnlyMinGoodwill},{MakePeaceReenabledMinGoodwill - 1}]. Use create_quest with questDefName '{PeaceTalkQuestDefName}' for peace talks.");
            }

            return ActionValidationResult.AllowedResult();
        }

        private static ActionValidationResult ValidatePeaceTalkOnlyQuestPolicy(Faction faction, string questDefName)
        {
            if (faction == null || !IsInPeaceTalkOnlyRange(faction))
            {
                return ActionValidationResult.AllowedResult();
            }

            if (string.IsNullOrWhiteSpace(questDefName) ||
                string.Equals(questDefName, PeaceTalkQuestDefName, StringComparison.Ordinal))
            {
                return ActionValidationResult.AllowedResult();
            }

            return ActionValidationResult.Denied(
                "peace_talk_only_range",
                $"Current goodwill {faction.PlayerGoodwill} is in [{PeaceTalkOnlyMinGoodwill},{MakePeaceReenabledMinGoodwill - 1}]. Only quest '{PeaceTalkQuestDefName}' is allowed.");
        }

        private static bool IsInPeaceTalkOnlyRange(Faction faction)
        {
            if (faction == null)
            {
                return false;
            }

            int goodwill = faction.PlayerGoodwill;
            return goodwill >= PeaceTalkOnlyMinGoodwill && goodwill < MakePeaceReenabledMinGoodwill;
        }

        private static bool HasSettlement(Faction faction)
        {
            return Find.WorldObjects?.Settlements != null && Find.WorldObjects.Settlements.Any(s => s.Faction == faction);
        }

        private static bool HasFactionLeader(Faction faction)
        {
            return faction?.leader != null || HasSettlement(faction);
        }

        private static bool IsEnabledImageTemplate(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return false;
            }

            string resolved = ResolveExistingImageTemplateId(templateId);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return false;
            }

            var settings = RimChatMod.Instance?.InstanceSettings;
            List<RimChat.Config.DiplomacyImagePromptTemplate> templates = settings?.DiplomacyImagePromptTemplates;
            if (templates == null || templates.Count == 0)
            {
                return false;
            }

            return templates.Any(item =>
                item != null &&
                item.Enabled &&
                string.Equals(item.Id, resolved, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetDefaultEnabledImageTemplateId()
        {
            var settings = RimChatMod.Instance?.InstanceSettings;
            if (settings == null)
            {
                return string.Empty;
            }

            settings.DiplomacyImagePromptTemplates ??= new List<RimChat.Config.DiplomacyImagePromptTemplate>();
            RimChat.Config.DiplomacyImageTemplateDefaults.EnsureDefaults(settings.DiplomacyImagePromptTemplates);
            List<RimChat.Config.DiplomacyImagePromptTemplate> templates = settings.DiplomacyImagePromptTemplates;
            if (templates.Count == 0)
            {
                return string.Empty;
            }

            return RimChat.Config.DiplomacyImageTemplateDefaults.ResolvePreferredEnabledTemplateId(templates);
        }

        private static string ResolveExistingImageTemplateId(string requestedTemplateId)
        {
            if (string.IsNullOrWhiteSpace(requestedTemplateId))
            {
                return string.Empty;
            }

            var settings = RimChatMod.Instance?.InstanceSettings;
            List<RimChat.Config.DiplomacyImagePromptTemplate> templates = settings?.DiplomacyImagePromptTemplates;
            if (templates == null || templates.Count == 0)
            {
                return string.Empty;
            }

            return RimChat.Config.DiplomacyImageTemplateDefaults.ResolveTemplateId(templates, requestedTemplateId);
        }

        private static ActionValidationResult ValidateCooldown(Faction faction, string methodName, string code)
        {
            int remaining = GameAIInterface.Instance.GetRemainingCooldownSeconds(faction, methodName);
            if (remaining > 0)
            {
                return ActionValidationResult.Denied(code, $"{methodName} is on cooldown for {faction.Name}. Remaining: {remaining} seconds", remaining);
            }
            return ActionValidationResult.AllowedResult();
        }

        private static bool IsAncientQuestTemplateName(string questDefName)
        {
            return !string.IsNullOrEmpty(questDefName) &&
                   questDefName.IndexOf("Ancient", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsFeatureEnabled(string actionType)
        {
            var settings = RimChatMod.Instance?.InstanceSettings;
            if (settings == null) return false;

            switch (actionType)
            {
                case "adjust_goodwill":
                    return settings.EnableAIGoodwillAdjustment;
                case "send_gift":
                    return settings.EnableAIGiftSending;
                case "request_aid":
                    return settings.EnableAIAidRequest;
                case "declare_war":
                    return settings.EnableAIWarDeclaration;
                case "make_peace":
                    return settings.EnableAIPeaceMaking;
                case "request_caravan":
                    return settings.EnableAITradeCaravan;
                case "request_raid":
                    return settings.EnableAIRaidRequest;
                case "create_quest":
                case "trigger_incident":
                case "reject_request":
                    return true;
                case "send_image":
                    return settings.DiplomacyImageApi != null && settings.DiplomacyImageApi.IsConfigured();
                case "publish_public_post":
                    return settings.EnableSocialCircle && settings.EnablePlayerInfluenceNews;
                case "exit_dialogue":
                case "go_offline":
                case "set_dnd":
                    return settings.EnableFactionPresenceStatus;
                default:
                    return false;
            }
        }
    }

    public class ActionValidationResult
    {
        public bool Allowed { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public int RemainingSeconds { get; set; }

        public static ActionValidationResult AllowedResult()
        {
            return new ActionValidationResult { Allowed = true, Code = "allowed", Message = "Allowed", RemainingSeconds = 0 };
        }

        public static ActionValidationResult Denied(string code, string message, int remainingSeconds = 0)
        {
            return new ActionValidationResult
            {
                Allowed = false,
                Code = code ?? "denied",
                Message = message ?? "Action denied",
                RemainingSeconds = Math.Max(0, remainingSeconds)
            };
        }
    }

    public class QuestValidationResult : ActionValidationResult
    {
        public string NormalizedQuestDefName { get; set; }

        public static QuestValidationResult AllowedResult(string questDefName)
        {
            return new QuestValidationResult
            {
                Allowed = true,
                Code = "allowed",
                Message = "Allowed",
                NormalizedQuestDefName = questDefName
            };
        }

        public new static QuestValidationResult Denied(string code, string message, int remainingSeconds = 0)
        {
            return new QuestValidationResult
            {
                Allowed = false,
                Code = code ?? "denied",
                Message = message ?? "Quest denied",
                RemainingSeconds = Math.Max(0, remainingSeconds),
                NormalizedQuestDefName = null
            };
        }
    }

    public class QuestTemplateEligibility
    {
        public string QuestDefName { get; set; }
        public bool Allowed { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
    }
}



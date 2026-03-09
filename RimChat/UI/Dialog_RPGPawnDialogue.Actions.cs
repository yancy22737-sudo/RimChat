using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimChat.AI;
using RimChat.Config;
using RimChat.Core;
using RimChat.DiplomacySystem;
using RimChat.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Responsibilities: execute RPG actions, normalize action names, and render compact action feedback.
 /// Dependencies: LLMRpgApiResponse, GameComponent_RPGManager, RimWorld defs/workers.
 ///</summary>
    public partial class Dialog_RPGPawnDialogue
    {
        private struct ActionFeedbackEntry
        {
            public string Text;
            public Color Color;
            public float CreatedAt;
            public float Duration;
        }

        private readonly List<ActionFeedbackEntry> actionFeedbackEntries = new List<ActionFeedbackEntry>();
        private static readonly Color ActionSuccessColor = new Color(0.45f, 0.9f, 0.55f, 1f);
        private static readonly Color ActionFailureColor = new Color(0.95f, 0.6f, 0.45f, 1f);
        private static readonly Color ActionErrorColor = new Color(0.95f, 0.4f, 0.4f, 1f);
        private static readonly Color ActionInfoColor = new Color(0.55f, 0.78f, 0.98f, 1f);
        private const float ActionFeedbackDefaultDuration = 3.8f;
        private const int ActionFeedbackMaxCount = 8;
        private const int MemoryRound5Threshold = 5;
        private const float MemoryRoundChance = 0.8f;
        private bool memoryRound5Evaluated;
        private int consecutiveNoActionAssistantTurns;
        private int lastIntentMappedAssistantRound = -999;
        private static readonly Dictionary<string, string> TryGainMemoryAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "chatted", "RimChat_PleasantChatMemory" },
            { "chat", "RimChat_PleasantChatMemory" },
            { "smalltalk", "RimChat_PleasantChatMemory" },
            { "small_talk", "RimChat_PleasantChatMemory" },
            { "chit_chat", "RimChat_PleasantChatMemory" },
            { "deep_talk", "RimChat_DeepConversationMemory" },
            { "deepchat", "RimChat_DeepConversationMemory" },
            { "kind_words", "KindWordsMood" },
            { "kindword", "KindWordsMood" },
            { "insult", "InsultedMood" },
            { "slight", "RimChat_SlightedMemory" }
        };
        private static readonly string[] TryGainMemoryExampleDefs =
        {
            "RimChat_PleasantChatMemory", "RimChat_DeepConversationMemory", "KindWordsMood", "RimChat_SlightedMemory", "InsultedMood", "AteWithoutTable",
            "SleepDisturbed", "SleptOutside", "SleptInCold", "SleptInHeat", "GotSomeLovin", "Catharsis"
        };
        private static readonly string[] AutoMemoryPreferredDefs =
        {
            "RimChat_DeepConversationMemory", "KindWordsMood", "RimChat_PleasantChatMemory", "Catharsis"
        };
        private static readonly string[] CooldownExitFallbackHints =
        {
            "leave me alone", "do not contact me", "don't contact me", "stop talking",
            "get lost", "go away", "don't bother me",
            "\u522b\u518d\u6253\u6270", "\u522b\u8054\u7cfb\u6211", "\u4e0d\u8981\u518d\u627e\u6211",
            "\u79bb\u6211\u8fdc\u70b9", "\u6eda\u5f00", "\u8bf7\u79bb\u5f00"
        };
        private static readonly string[] NormalExitFallbackHints =
        {
            "goodbye", "see you", "talk later", "let's pause", "need to go", "that's all for now",
            "\u518d\u89c1", "\u4e0d\u804a\u4e86", "\u4eca\u5929\u5c31\u5230\u8fd9",
            "\u5148\u804a\u5230\u8fd9", "\u5c31\u804a\u5230\u8fd9", "\u6539\u5929\u518d\u804a",
            "\u6211\u8981\u53bb\u5fd9\u4e86", "\u665a\u70b9\u518d\u8bf4", "\u56de\u5934\u518d\u804a"
        };
        private static readonly string[] StrongRejectHints =
        {
            "never", "won't", "refuse", "stop", "don't ask again", "leave me alone",
            "\u4f11\u60f3", "\u4e0d\u53ef\u80fd", "\u5c11\u6765", "\u522b\u518d\u95ee", "\u7981\u6b62"
        };
        private static readonly string[] CollaborationHints =
        {
            "i can", "i will", "let me", "deal", "agreed", "okay", "understood",
            "\u53ef\u4ee5", "\u6211\u4f1a", "\u6ca1\u95ee\u9898", "\u884c", "\u597d"
        };

        private void ApplyRPGAPIAndShowPopup(LLMRpgApiResponse apiRes)
        {
            if (apiRes == null)
            {
                return;
            }

            ExecuteRpgActions(apiRes.Actions);
        }

        private void ExecuteRpgActions(List<LLMRpgApiResponse.ApiAction> actions)
        {
            if (actions == null || actions.Count == 0)
            {
                return;
            }

            foreach (var action in actions)
            {
                ExecuteRpgAction(action);
            }
        }

        private void ExecuteRpgAction(LLMRpgApiResponse.ApiAction action)
        {
            string normalizedName = NormalizeRpgActionName(action?.action);
            if (string.IsNullOrEmpty(normalizedName))
            {
                return;
            }

            LogRpgActionDebug($"RPG action received: raw={action?.action ?? "null"}, normalized={normalizedName}");

            try
            {
                bool success = normalizedName switch
                {
                    "ExitDialogue" => ExecuteExitDialogue(action?.reason, false),
                    "ExitDialogueCooldown" => ExecuteExitDialogue(action?.reason, true),
                    "RomanceAttempt" => ExecuteRomanceAttempt(action),
                    "MarriageProposal" => ExecuteMarriageProposal(action),
                    "Breakup" => ExecuteBreakup(action),
                    "Divorce" => ExecuteDivorce(action),
                    "Date" => ExecuteDate(action),
                    "TryGainMemory" => ExecuteTryGainMemory(action),
                    "TryAffectSocialGoodwill" => ExecuteTryAffectSocialGoodwill(action),
                    "ReduceResistance" => ExecuteReduceResistance(action),
                    "ReduceWill" => ExecuteReduceWill(action),
                    "Recruit" => ExecuteRecruit(),
                    "TryTakeOrderedJob" => ExecuteTryTakeOrderedJob(action),
                    "TriggerIncident" => ExecuteTriggerIncident(action),
                    "GrantInspiration" => ExecuteGrantInspiration(action),
                    _ => ExecuteUnknownAction(normalizedName, action)
                };

                if (success)
                {
                    NotifyActionSuccess(normalizedName);
                }
            }
            catch (Exception ex)
            {
                NotifyActionError(normalizedName);
                Log.Warning($"[RimChat] RPG action execution failed: {action?.action}, error={ex}");
            }
        }

        private bool ExecuteExitDialogue(string reason, bool withCooldown)
        {
            if (withCooldown)
            {
                var manager = Current.Game?.GetComponent<GameComponent_RPGManager>();
                int cooldownTicks = manager?.GetRpgDialogueExitCooldownTicks() ?? 60000;
                manager?.StartRpgDialogueCooldown(target, cooldownTicks);
                float days = cooldownTicks / 60000f;
                AddActionFeedback("RimChat_RPGActionToast_CooldownApplied".Translate(days.ToString("F1")), ActionInfoColor, 4.6f);
            }

            HandleNpcExitDialogue(reason);
            return true;
        }

        private bool ExecuteRomanceAttempt(LLMRpgApiResponse.ApiAction action)
        {
            if (!CanApplyRelationshipAction("RomanceAttempt"))
            {
                return false;
            }

            PawnRelationDef loverDef = ResolveRelationDef("RomanceAttempt", PawnRelationDefOf.Lover, "Lover");
            if (loverDef == null)
            {
                return false;
            }

            PawnRelationDef exLoverDef = ResolveRelationDef("RomanceAttempt", PawnRelationDefOf.ExLover, "ExLover");
            PawnRelationDef exSpouseDef = ResolveRelationDef("RomanceAttempt", PawnRelationDefOf.ExSpouse, "ExSpouse");

            if (HasPairRelation(PawnRelationDefOf.Spouse) || HasPairRelation(PawnRelationDefOf.Fiance) || HasPairRelation(loverDef))
            {
                return true;
            }

            RemovePairRelation(exLoverDef);
            RemovePairRelation(exSpouseDef);
            AddPairRelation(loverDef);
            return true;
        }

        private bool ExecuteMarriageProposal(LLMRpgApiResponse.ApiAction action)
        {
            if (!CanApplyRelationshipAction("MarriageProposal"))
            {
                return false;
            }

            PawnRelationDef spouseDef = ResolveRelationDef("MarriageProposal", PawnRelationDefOf.Spouse, "Spouse");
            if (spouseDef == null)
            {
                return false;
            }

            PawnRelationDef exSpouseDef = ResolveRelationDef("MarriageProposal", PawnRelationDefOf.ExSpouse, "ExSpouse");
            PawnRelationDef loverDef = ResolveRelationDef("MarriageProposal", PawnRelationDefOf.Lover, "Lover");
            PawnRelationDef fianceDef = ResolveRelationDef("MarriageProposal", PawnRelationDefOf.Fiance, "Fiance");
            PawnRelationDef exLoverDef = ResolveRelationDef("MarriageProposal", PawnRelationDefOf.ExLover, "ExLover");

            ClearOtherSpousesForMarriage(target, initiator, spouseDef, exSpouseDef);
            ClearOtherSpousesForMarriage(initiator, target, spouseDef, exSpouseDef);

            RemovePairRelation(exSpouseDef);
            RemovePairRelation(exLoverDef);
            RemovePairRelation(fianceDef);
            RemovePairRelation(loverDef);
            AddPairRelation(spouseDef);
            return true;
        }

        private bool ExecuteBreakup(LLMRpgApiResponse.ApiAction action)
        {
            if (!CanApplyRelationshipAction("Breakup"))
            {
                return false;
            }

            PawnRelationDef spouseDef = ResolveRelationDef("Breakup", PawnRelationDefOf.Spouse, "Spouse");
            PawnRelationDef exSpouseDef = ResolveRelationDef("Breakup", PawnRelationDefOf.ExSpouse, "ExSpouse");
            PawnRelationDef loverDef = ResolveRelationDef("Breakup", PawnRelationDefOf.Lover, "Lover");
            PawnRelationDef fianceDef = ResolveRelationDef("Breakup", PawnRelationDefOf.Fiance, "Fiance");
            PawnRelationDef exLoverDef = ResolveRelationDef("Breakup", PawnRelationDefOf.ExLover, "ExLover");

            bool hadMarriage = HasPairRelation(spouseDef);
            bool hadRomance = HasPairRelation(loverDef) || HasPairRelation(fianceDef);

            if (hadMarriage)
            {
                RemovePairRelation(spouseDef);
                AddPairRelation(exSpouseDef);
            }

            RemovePairRelation(loverDef);
            RemovePairRelation(fianceDef);

            if (!hadMarriage)
            {
                AddPairRelation(exLoverDef);
            }

            if (!hadMarriage && !hadRomance)
            {
                AddPairRelation(exLoverDef);
            }

            return true;
        }

        private bool ExecuteDate(LLMRpgApiResponse.ApiAction action)
        {
            // "Date" has no dedicated hard state in vanilla; map it to guaranteed romantic state.
            return ExecuteRomanceAttempt(action);
        }

        private bool ExecuteDivorce(LLMRpgApiResponse.ApiAction action)
        {
            if (!CanApplyRelationshipAction("Divorce"))
            {
                NotifyActionFailure("Divorce", "RimChat_RPGActionFail_InvalidTarget".Translate());
                return false;
            }

            PawnRelationDef spouseDef = ResolveRelationDef("Divorce", PawnRelationDefOf.Spouse, "Spouse");
            PawnRelationDef exSpouseDef = ResolveRelationDef("Divorce", PawnRelationDefOf.ExSpouse, "ExSpouse");
            if (spouseDef == null || exSpouseDef == null)
            {
                return false;
            }

            RemovePairRelation(PawnRelationDefOf.Fiance);
            RemovePairRelation(PawnRelationDefOf.Lover);
            RemovePairRelation(spouseDef);
            AddPairRelation(exSpouseDef);
            return true;
        }

        private bool ExecuteTryGainMemory(LLMRpgApiResponse.ApiAction action)
        {
            if (target?.needs?.mood?.thoughts?.memories == null)
            {
                NotifyActionFailure("TryGainMemory", "RimChat_RPGActionFail_InvalidTarget".Translate());
                return false;
            }

            string requested = action?.defName ?? string.Empty;
            ThoughtDef def = ResolveTryGainMemoryThoughtDef(requested, out string resolvedFrom);
            if (def == null)
            {
                string examples = BuildTryGainMemoryExamplesText();
                string reason = "RimChat_RPGActionFail_InvalidDefName".Translate(string.IsNullOrEmpty(requested) ? "null" : requested);
                if (!string.IsNullOrWhiteSpace(examples))
                {
                    reason += " " + "RimChat_RPGActionFail_DefNameExamples".Translate(examples);
                }

                NotifyActionFailure("TryGainMemory", reason);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(resolvedFrom))
            {
                LogRpgActionDebug($"TryGainMemory resolved alias '{requested}' -> '{def.defName}' via {resolvedFrom}");
            }

            target.needs.mood.thoughts.memories.TryGainMemory(def, initiator);
            AddSystemFeedback("RimChat_RPGSystem_MemoryApplied".Translate(def.defName), 3.8f);
            return true;
        }

        private ThoughtDef ResolveTryGainMemoryThoughtDef(string requestedDefName, out string resolvedFrom)
        {
            resolvedFrom = string.Empty;
            if (string.IsNullOrWhiteSpace(requestedDefName))
            {
                return null;
            }

            ThoughtDef exact = ResolveVisibleMemoryThoughtDef(DefDatabase<ThoughtDef>.GetNamedSilentFail(requestedDefName), out string exactResolvedFrom);
            if (IsUsableMemoryThoughtDef(exact))
            {
                resolvedFrom = exactResolvedFrom;
                return exact;
            }

            string normalized = NormalizeDefToken(requestedDefName);
            if (TryResolveMemoryAlias(normalized, out ThoughtDef aliasDef))
            {
                resolvedFrom = "alias";
                return aliasDef;
            }

            ThoughtDef normalizedDef = FindThoughtDefByNormalizedName(normalized);
            if (IsUsableMemoryThoughtDef(normalizedDef))
            {
                resolvedFrom = "normalized";
                return normalizedDef;
            }

            if (TryResolveMemoryHeuristic(normalized, out ThoughtDef heuristicDef))
            {
                resolvedFrom = "chat-heuristic";
                return heuristicDef;
            }

            return null;
        }

        private bool TryResolveMemoryAlias(string normalized, out ThoughtDef aliasDef)
        {
            aliasDef = null;
            if (!TryGainMemoryAliases.TryGetValue(normalized, out string aliasTarget))
            {
                return false;
            }

            aliasDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(aliasTarget);
            return IsUsableMemoryThoughtDef(aliasDef);
        }

        private bool TryResolveMemoryHeuristic(string normalized, out ThoughtDef heuristicDef)
        {
            heuristicDef = null;
            if (!normalized.Contains("chat"))
            {
                return false;
            }

            heuristicDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("RimChat_PleasantChatMemory");
            return IsUsableMemoryThoughtDef(heuristicDef);
        }

        private ThoughtDef FindThoughtDefByNormalizedName(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            List<ThoughtDef> defs = DefDatabase<ThoughtDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThoughtDef def = defs[i];
                if (!IsUsableMemoryThoughtDef(def))
                {
                    continue;
                }

                if (NormalizeDefToken(def.defName) == normalized)
                {
                    return def;
                }
            }

            return null;
        }

        private bool IsUsableMemoryThoughtDef(ThoughtDef def)
        {
            return def != null &&
                   (def.thoughtClass == null || typeof(Thought_Memory).IsAssignableFrom(def.thoughtClass));
        }

        private ThoughtDef ResolveVisibleMemoryThoughtDef(ThoughtDef def, out string resolvedFrom)
        {
            resolvedFrom = string.Empty;
            if (def == null)
            {
                return null;
            }

            switch (def.defName)
            {
                case "Chitchat":
                    resolvedFrom = "visible-chat";
                    return DefDatabase<ThoughtDef>.GetNamedSilentFail("RimChat_PleasantChatMemory") ?? def;
                case "DeepTalk":
                    resolvedFrom = "visible-deep-talk";
                    return DefDatabase<ThoughtDef>.GetNamedSilentFail("RimChat_DeepConversationMemory") ?? def;
                case "Slighted":
                    resolvedFrom = "visible-slighted";
                    return DefDatabase<ThoughtDef>.GetNamedSilentFail("RimChat_SlightedMemory") ?? def;
                case "KindWords":
                    resolvedFrom = "mood-companion";
                    return DefDatabase<ThoughtDef>.GetNamedSilentFail("KindWordsMood") ?? def;
                case "Insulted":
                    resolvedFrom = "mood-companion";
                    return DefDatabase<ThoughtDef>.GetNamedSilentFail("InsultedMood") ?? def;
                default:
                    return def;
            }
        }

        private string NormalizeDefToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            char[] chars = token.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }

        private string BuildTryGainMemoryExamplesText()
        {
            var valid = new List<string>();
            for (int i = 0; i < TryGainMemoryExampleDefs.Length; i++)
            {
                string name = TryGainMemoryExampleDefs[i];
                if (IsUsableMemoryThoughtDef(DefDatabase<ThoughtDef>.GetNamedSilentFail(name)))
                {
                    valid.Add(name);
                }
            }

            return valid.Count == 0 ? string.Empty : string.Join(", ", valid);
        }

        private bool ExecuteTryAffectSocialGoodwill(LLMRpgApiResponse.ApiAction action)
        {
            if (target?.Faction == null || initiator?.Faction == null)
            {
                NotifyActionFailure("TryAffectSocialGoodwill", "RimChat_RPGActionFail_MissingFaction".Translate());
                return false;
            }

            target.Faction.TryAffectGoodwillWith(initiator.Faction, action.amount, true, true, null);
            return true;
        }

        private bool ExecuteReduceResistance(LLMRpgApiResponse.ApiAction action)
        {
            if (target?.guest == null || !target.IsPrisoner || action.amount <= 0)
            {
                NotifyActionFailure("ReduceResistance", "RimChat_RPGActionFail_InvalidTarget".Translate());
                return false;
            }

            target.guest.resistance = Math.Max(0f, target.guest.resistance - action.amount);
            return true;
        }

        private bool ExecuteReduceWill(LLMRpgApiResponse.ApiAction action)
        {
            if (target?.guest == null || !target.IsPrisoner || action.amount <= 0)
            {
                NotifyActionFailure("ReduceWill", "RimChat_RPGActionFail_InvalidTarget".Translate());
                return false;
            }

            target.guest.will = Math.Max(0f, target.guest.will - action.amount);
            return true;
        }

        private bool ExecuteRecruit()
        {
            if (target == null || initiator?.Faction == null || target.Faction == initiator.Faction)
            {
                NotifyActionFailure("Recruit", "RimChat_RPGActionFail_InvalidTarget".Translate());
                return false;
            }

            RecruitUtility.Recruit(target, initiator.Faction, initiator);
            return true;
        }

        private bool ExecuteTryTakeOrderedJob(LLMRpgApiResponse.ApiAction action)
        {
            if (!string.Equals(action?.defName, "AttackMelee", StringComparison.OrdinalIgnoreCase))
            {
                NotifyActionFailure("TryTakeOrderedJob", "RimChat_RPGActionFail_UnsupportedJob".Translate(action?.defName ?? "null"));
                return false;
            }

            var attackJob = new Verse.AI.Job(JobDefOf.AttackMelee, initiator);
            target?.jobs?.TryTakeOrderedJob(attackJob, Verse.AI.JobTag.Misc);
            return true;
        }

        private bool ExecuteTriggerIncident(LLMRpgApiResponse.ApiAction action)
        {
            IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail(action?.defName);
            Map map = target?.MapHeld ?? Find.CurrentMap;
            if (def == null || map == null)
            {
                NotifyActionFailure("TriggerIncident", "RimChat_RPGActionFail_InvalidDefName".Translate(action?.defName ?? "null"));
                return false;
            }

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(def.category, map);
            parms.faction = target?.Faction;
            if (action.amount > 0)
            {
                parms.points = action.amount;
            }

            bool executed = def.Worker.TryExecute(parms);
            if (!executed)
            {
                NotifyActionFailure("TriggerIncident", "RimChat_RPGActionFail_GameRejected".Translate());
            }
            return executed;
        }

        private bool ExecuteGrantInspiration(LLMRpgApiResponse.ApiAction action)
        {
            object handler = target?.mindState?.inspirationHandler;
            if (handler == null)
            {
                NotifyActionFailure("GrantInspiration", "RimChat_RPGActionFail_InvalidTarget".Translate());
                return false;
            }

            List<InspirationDef> candidates = BuildInspirationCandidates(action?.defName);
            if (candidates.Count == 0)
            {
                NotifyActionFailure("GrantInspiration", "RimChat_RPGActionFail_InvalidDefName".Translate(action?.defName ?? "null"));
                return false;
            }

            foreach (InspirationDef def in candidates)
            {
                if (!TryStartInspiration(handler, def, action?.reason))
                {
                    continue;
                }

                LogRpgActionDebug($"GrantInspiration success: def={def.defName}, pawn={target?.LabelShort ?? "null"}");
                return true;
            }

            LogRpgActionDebug($"GrantInspiration rejected for all candidates. requestedDef={action?.defName ?? "null"}");
            NotifyActionFailure("GrantInspiration", "RimChat_RPGActionFail_GameRejected".Translate());
            return false;
        }

        private bool ExecuteUnknownAction(string normalizedName, LLMRpgApiResponse.ApiAction action)
        {
            string rawAction = action?.action ?? normalizedName;
            AddActionFeedback("RimChat_RPGActionToast_Unknown".Translate(rawAction), ActionFailureColor);
            Log.Message($"[RimChat] Unknown RPG action ignored: {rawAction}");
            return false;
        }

        private bool CanApplyRelationshipAction(string actionName)
        {
            if (target == null || initiator == null || target.relations == null || initiator.relations == null || target == initiator)
            {
                NotifyActionFailure(actionName, "RimChat_RPGActionFail_InvalidTarget".Translate());
                return false;
            }

            return true;
        }

        private PawnRelationDef ResolveRelationDef(string actionName, PawnRelationDef relationDef, string defName)
        {
            PawnRelationDef resolved = relationDef ?? DefDatabase<PawnRelationDef>.GetNamedSilentFail(defName);
            if (resolved == null)
            {
                NotifyActionFailure(actionName, "RimChat_RPGActionFail_InvalidDefName".Translate(defName));
            }
            return resolved;
        }

        private bool HasPairRelation(PawnRelationDef relationDef)
        {
            if (relationDef == null || target?.relations == null || initiator == null)
            {
                return false;
            }

            return target.relations.DirectRelationExists(relationDef, initiator);
        }

        private void AddPairRelation(PawnRelationDef relationDef)
        {
            if (relationDef == null || target?.relations == null || initiator == null)
            {
                return;
            }

            if (!target.relations.DirectRelationExists(relationDef, initiator))
            {
                target.relations.AddDirectRelation(relationDef, initiator);
            }
        }

        private void RemovePairRelation(PawnRelationDef relationDef)
        {
            if (relationDef == null || target?.relations == null || initiator == null)
            {
                return;
            }

            if (target.relations.DirectRelationExists(relationDef, initiator))
            {
                target.relations.RemoveDirectRelation(relationDef, initiator);
            }
        }

        private void ClearOtherSpousesForMarriage(Pawn pawn, Pawn keepPartner, PawnRelationDef spouseDef, PawnRelationDef exSpouseDef)
        {
            if (pawn?.relations?.DirectRelations == null || spouseDef == null)
            {
                return;
            }

            List<Pawn> otherSpouses = pawn.relations.DirectRelations
                .Where(r => r.def == spouseDef && r.otherPawn != null && r.otherPawn != keepPartner)
                .Select(r => r.otherPawn)
                .Distinct()
                .ToList();

            foreach (Pawn otherSpouse in otherSpouses)
            {
                if (pawn.relations.DirectRelationExists(spouseDef, otherSpouse))
                {
                    pawn.relations.RemoveDirectRelation(spouseDef, otherSpouse);
                }

                if (exSpouseDef != null && !pawn.relations.DirectRelationExists(exSpouseDef, otherSpouse))
                {
                    pawn.relations.AddDirectRelation(exSpouseDef, otherSpouse);
                }
            }
        }

        private List<InspirationDef> BuildInspirationCandidates(string defName)
        {
            var result = new List<InspirationDef>();
            if (!string.IsNullOrWhiteSpace(defName))
            {
                InspirationDef preferred = DefDatabase<InspirationDef>.GetNamedSilentFail(defName);
                if (preferred != null)
                {
                    result.Add(preferred);
                }
                else
                {
                    LogRpgActionDebug($"GrantInspiration invalid defName from LLM: {defName}");
                }
            }

            var defs = DefDatabase<InspirationDef>.AllDefsListForReading;
            if (defs == null || defs.Count == 0)
            {
                return result;
            }

            foreach (InspirationDef def in defs.InRandomOrder())
            {
                if (def == null || result.Contains(def))
                {
                    continue;
                }
                result.Add(def);
            }

            return result;
        }

        private bool TryStartInspiration(object handler, InspirationDef inspirationDef, string reason)
        {
            if (handler == null || inspirationDef == null)
            {
                return false;
            }

            MethodInfo[] methods = handler
                .GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => string.Equals(m.Name, "TryStartInspiration", StringComparison.Ordinal))
                .OrderBy(m => m.GetParameters().Length)
                .ToArray();

            foreach (MethodInfo method in methods)
            {
                object[] args = BuildInspirationInvokeArgs(method, inspirationDef, reason);
                if (args == null)
                {
                    continue;
                }

                try
                {
                    object invokeResult = method.Invoke(handler, args);
                    if (invokeResult is bool started)
                    {
                        return started;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    LogRpgActionDebug($"GrantInspiration invoke failed on {method}: {ex.Message}");
                }
            }

            return false;
        }

        private object[] BuildInspirationInvokeArgs(MethodInfo method, InspirationDef inspirationDef, string reason)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                return null;
            }

            if (!parameters[0].ParameterType.IsAssignableFrom(typeof(InspirationDef)) &&
                !typeof(InspirationDef).IsAssignableFrom(parameters[0].ParameterType))
            {
                return null;
            }

            object[] args = new object[parameters.Length];
            args[0] = inspirationDef;
            for (int i = 1; i < parameters.Length; i++)
            {
                Type paramType = parameters[i].ParameterType;
                if (paramType == typeof(string))
                {
                    args[i] = reason ?? string.Empty;
                }
                else if (parameters[i].HasDefaultValue)
                {
                    args[i] = parameters[i].DefaultValue;
                }
                else if (paramType == typeof(bool))
                {
                    args[i] = false;
                }
                else if (!paramType.IsValueType || Nullable.GetUnderlyingType(paramType) != null)
                {
                    args[i] = null;
                }
                else
                {
                    args[i] = Activator.CreateInstance(paramType);
                }
            }

            return args;
        }

        private void LogRpgActionDebug(string message)
        {
            if (RimChatMod.Settings?.EnableDebugLogging != true)
            {
                return;
            }

            Log.Message($"[RimChat] {message}");
        }

        private void HandleNpcExitDialogue(string reason)
        {
            isDialogueEndedByNpc = true;
            dialogueEndReason = reason ?? string.Empty;
            GUI.FocusControl(null);
        }

        private void NotifyActionSuccess(string actionName)
        {
            string actionLabel = GetRpgActionLabel(actionName);
            AddActionFeedback("RimChat_RPGActionToast_Success".Translate(actionLabel), ActionSuccessColor);
            LogRpgActionDebug($"RPG action success: {actionName}");
        }

        private void NotifyActionFailure(string actionName, string reason)
        {
            string actionLabel = GetRpgActionLabel(actionName);
            AddActionFeedback("RimChat_RPGActionToast_Failure".Translate(actionLabel, reason), ActionFailureColor);
            LogRpgActionDebug($"RPG action failed: {actionName}, reason={reason}");
        }

        private void NotifyActionError(string actionName)
        {
            string actionLabel = GetRpgActionLabel(actionName);
            AddActionFeedback("RimChat_RPGActionToast_Error".Translate(actionLabel), ActionErrorColor);
            Log.Warning($"[RimChat] RPG action error: {actionName}");
        }

        private string GetRpgActionLabel(string actionName)
        {
            string key = $"RimChat_RPGActionLabel_{actionName}";
            TaggedString translated = key.Translate();
            return translated.RawText == key ? actionName : translated.RawText;
        }

        private void AddActionFeedback(string text, Color color, float duration = ActionFeedbackDefaultDuration)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            actionFeedbackEntries.Add(new ActionFeedbackEntry
            {
                Text = text,
                Color = color,
                Duration = duration,
                CreatedAt = Time.realtimeSinceStartup
            });

            if (actionFeedbackEntries.Count > ActionFeedbackMaxCount)
            {
                actionFeedbackEntries.RemoveAt(0);
            }
        }

        private void AddSystemFeedback(string text, float duration = 4.2f)
        {
            AddActionFeedback(text, ActionInfoColor, duration);
        }

        private void DrawActionFeedback(Rect contentRect)
        {
            RemoveExpiredActionFeedback();
            if (actionFeedbackEntries.Count == 0)
            {
                return;
            }

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Text.Anchor = TextAnchor.UpperRight;
            Text.Font = GameFont.Tiny;

            float lineHeight = 18f;
            float width = Mathf.Min(460f, contentRect.width * 0.45f);
            float panelHeight = actionFeedbackEntries.Count * lineHeight + 8f;
            Rect panelRect = new Rect(contentRect.xMax - width - 8f, contentRect.y + 2f, width + 8f, panelHeight);
            Widgets.DrawBoxSolid(panelRect, new Color(0.05f, 0.07f, 0.09f, 0.6f));

            float y = panelRect.y + 4f;

            for (int i = actionFeedbackEntries.Count - 1; i >= 0; i--)
            {
                ActionFeedbackEntry entry = actionFeedbackEntries[i];
                float age = Time.realtimeSinceStartup - entry.CreatedAt;
                float alpha = Mathf.Clamp01(1f - age / entry.Duration);
                GUI.color = new Color(entry.Color.r, entry.Color.g, entry.Color.b, alpha);
                Rect rect = new Rect(contentRect.xMax - width - 4f, y, width, lineHeight);
                Widgets.Label(rect, entry.Text);
                y += lineHeight;
            }

            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            GUI.color = Color.white;
        }

        private void RemoveExpiredActionFeedback()
        {
            float now = Time.realtimeSinceStartup;
            actionFeedbackEntries.RemoveAll(entry => now - entry.CreatedAt > entry.Duration);
        }

        private string NormalizeRpgActionName(string actionName)
        {
            if (string.IsNullOrWhiteSpace(actionName))
            {
                return null;
            }

            string normalized = actionName.Trim().Replace("-", "_").ToLowerInvariant();
            return normalized switch
            {
                "romanceattempt" or "romance_attempt" or "romance" or "fall_in_love" or "start_romance" or "\u604b\u7231" => "RomanceAttempt",
                "marriageproposal" or "marriage_proposal" or "propose_marriage" or "marry" or "\u7ed3\u5a5a" => "MarriageProposal",
                "breakup" or "break_up" or "split_up" or "\u5206\u624b" => "Breakup",
                "divorce" or "\u79bb\u5a5a" => "Divorce",
                "date" or "dating" or "\u7ea6\u4f1a" => "Date",
                "trygainmemory" or "try_gain_memory" => "TryGainMemory",
                "tryaffectsocialgoodwill" or "try_affect_social_goodwill" => "TryAffectSocialGoodwill",
                "reduceresistance" or "reduce_resistance" => "ReduceResistance",
                "reducewill" or "reduce_will" => "ReduceWill",
                "recruit" or "action4" or "action_4" or "action 4" or "\u7b2c4\u4e2a\u52a8\u4f5c" or "\u7b2c\u56db\u4e2a\u52a8\u4f5c" => "Recruit",
                "trytakeorderedjob" or "try_take_ordered_job" => "TryTakeOrderedJob",
                "triggerincident" or "trigger_incident" => "TriggerIncident",
                "grantinspiration" or "grant_inspiration" => "GrantInspiration",
                "exitdialoguecooldown" or "exit_dialogue_cooldown" or "exit_dialogue_with_cooldown" => "ExitDialogueCooldown",
                "exitdialogue" or "exit_dialogue" or "end_dialogue" => "ExitDialogue",
                _ => actionName.Trim()
            };
        }

        private void EnsureRpgActionFallbacks(LLMRpgApiResponse apiResponse)
        {
            if (apiResponse?.Actions == null)
            {
                return;
            }

            EnsureRpgExitActionFallback(apiResponse);
            EnsureRpgIntentDrivenActionMapping(apiResponse);
            EnsureRpgMemoryActionFallback(apiResponse);
            EnsureRpgMinimumActionCoverage(apiResponse);
        }

        private void EnsureRpgExitActionFallback(LLMRpgApiResponse apiResponse)
        {
            if (apiResponse?.Actions == null)
            {
                return;
            }

            bool hasExitAction = HasRpgAction(apiResponse, "ExitDialogue") || HasRpgAction(apiResponse, "ExitDialogueCooldown");
            if (hasExitAction)
            {
                return;
            }

            string text = apiResponse.DialogueContent ?? string.Empty;
            if (ShouldUseCooldownExitFallback(text))
            {
                apiResponse.Actions.Add(new LLMRpgApiResponse.ApiAction { action = "ExitDialogueCooldown" });
                return;
            }

            if (ShouldUseNormalExitFallback(text))
            {
                apiResponse.Actions.Add(new LLMRpgApiResponse.ApiAction { action = "ExitDialogue" });
            }
        }

        private enum IntentActionCategory
        {
            NeutralInfo = 0,
            CollaborationCommitment = 1,
            SoftEnding = 2,
            StrongReject = 3
        }

        private void EnsureRpgIntentDrivenActionMapping(LLMRpgApiResponse apiResponse)
        {
            PromptPolicyConfig policy = GetPromptPolicyForActionMapping();
            if (policy?.EnableIntentDrivenActionMapping != true || apiResponse?.Actions == null)
            {
                return;
            }

            int rounds = GetNpcDialogueRoundCount();
            int cooldown = Math.Max(0, policy.IntentActionCooldownTurns);
            if (cooldown > 0 && rounds - lastIntentMappedAssistantRound < cooldown)
            {
                return;
            }

            IntentActionCategory category = ClassifyIntentActionCategory(apiResponse.DialogueContent);
            bool changed = false;
            switch (category)
            {
                case IntentActionCategory.StrongReject:
                    changed = TryMapStrongRejectToAction(apiResponse);
                    break;
                case IntentActionCategory.SoftEnding:
                    changed = TryMapSoftEndingToAction(apiResponse);
                    break;
                case IntentActionCategory.CollaborationCommitment:
                    changed = TryMapCollaborationToAction(apiResponse, rounds, policy);
                    break;
            }

            if (changed)
            {
                lastIntentMappedAssistantRound = rounds;
            }
        }

        private static PromptPolicyConfig GetPromptPolicyForActionMapping()
        {
            SystemPromptConfig config = RimChat.Persistence.PromptPersistenceService.Instance?.LoadConfig();
            PromptPolicyConfig policy = config?.PromptPolicy;
            return policy?.Clone() ?? PromptPolicyConfig.CreateDefault();
        }

        private IntentActionCategory ClassifyIntentActionCategory(string dialogueText)
        {
            string text = dialogueText ?? string.Empty;
            if (ShouldUseCooldownExitFallback(text) || ContainsAnyPhrase(text, StrongRejectHints))
            {
                return IntentActionCategory.StrongReject;
            }

            if (ShouldUseNormalExitFallback(text))
            {
                return IntentActionCategory.SoftEnding;
            }

            if (ContainsAnyPhrase(text, CollaborationHints))
            {
                return IntentActionCategory.CollaborationCommitment;
            }

            return IntentActionCategory.NeutralInfo;
        }

        private bool TryMapStrongRejectToAction(LLMRpgApiResponse apiResponse)
        {
            if (HasRpgAction(apiResponse, "ExitDialogue") || HasRpgAction(apiResponse, "ExitDialogueCooldown"))
            {
                return false;
            }

            apiResponse.Actions.Add(new LLMRpgApiResponse.ApiAction
            {
                action = "ExitDialogueCooldown",
                reason = "intent_map_strong_reject"
            });
            return true;
        }

        private bool TryMapSoftEndingToAction(LLMRpgApiResponse apiResponse)
        {
            if (HasRpgAction(apiResponse, "ExitDialogue") || HasRpgAction(apiResponse, "ExitDialogueCooldown"))
            {
                return false;
            }

            apiResponse.Actions.Add(new LLMRpgApiResponse.ApiAction
            {
                action = "ExitDialogue",
                reason = "intent_map_soft_end"
            });
            return true;
        }

        private bool TryMapCollaborationToAction(LLMRpgApiResponse apiResponse, int rounds, PromptPolicyConfig policy)
        {
            if (HasAnyRpgEffects(apiResponse) || HasRpgAction(apiResponse, "TryGainMemory"))
            {
                return false;
            }

            int minRounds = Math.Max(0, policy?.IntentMinAssistantRoundsForMemory ?? 0);
            if (rounds < minRounds)
            {
                return false;
            }

            string memoryDefName = ResolveAutoMemoryDefName(rounds);
            if (string.IsNullOrWhiteSpace(memoryDefName))
            {
                return false;
            }

            apiResponse.Actions.Add(new LLMRpgApiResponse.ApiAction
            {
                action = "TryGainMemory",
                defName = memoryDefName,
                reason = "intent_map_collaboration"
            });
            return true;
        }

        private void EnsureRpgMemoryActionFallback(LLMRpgApiResponse apiResponse)
        {
            int rounds = GetNpcDialogueRoundCount();
            if (rounds < MemoryRound5Threshold)
            {
                return;
            }

            if (HasRpgAction(apiResponse, "TryGainMemory"))
            {
                memoryRound5Evaluated = true;
                return;
            }

            if (!memoryRound5Evaluated)
            {
                memoryRound5Evaluated = true;
                TryAddRoundMemoryFallback(apiResponse, rounds, MemoryRoundChance);
            }
        }

        private void TryAddRoundMemoryFallback(LLMRpgApiResponse apiResponse, int rounds, float chance)
        {
            float roll = Rand.Value;
            if (roll > chance)
            {
                AddSystemFeedback("RimChat_RPGSystem_MemoryRollFailed".Translate(rounds, (chance * 100f).ToString("F0"), (roll * 100f).ToString("F0")));
                return;
            }

            string memoryDefName = ResolveAutoMemoryDefName(rounds);
            if (string.IsNullOrWhiteSpace(memoryDefName))
            {
                AddSystemFeedback("RimChat_RPGSystem_MemoryNoDef".Translate());
                return;
            }

            apiResponse.Actions.Add(new LLMRpgApiResponse.ApiAction
            {
                action = "TryGainMemory",
                defName = memoryDefName,
                reason = "auto_round_memory"
            });
            AddSystemFeedback("RimChat_RPGSystem_MemoryRollSuccess".Translate(rounds, (chance * 100f).ToString("F0"), (roll * 100f).ToString("F0"), memoryDefName), 4.8f);
        }

        private string ResolveAutoMemoryDefName(int rounds)
        {
            if (rounds >= 10 && IsUsableMemoryThoughtDef(DefDatabase<ThoughtDef>.GetNamedSilentFail("DeepTalk")))
            {
                return "DeepTalk";
            }

            if (rounds < 10 && IsUsableMemoryThoughtDef(DefDatabase<ThoughtDef>.GetNamedSilentFail("Chitchat")))
            {
                return "Chitchat";
            }

            for (int i = 0; i < AutoMemoryPreferredDefs.Length; i++)
            {
                string defName = AutoMemoryPreferredDefs[i];
                if (IsUsableMemoryThoughtDef(DefDatabase<ThoughtDef>.GetNamedSilentFail(defName)))
                {
                    return defName;
                }
            }

            return string.Empty;
        }

        private int GetNpcDialogueRoundCount()
        {
            return chatHistory?.Count(m => string.Equals(m.role, "assistant", StringComparison.Ordinal)) ?? 0;
        }

        private bool HasRpgAction(LLMRpgApiResponse apiResponse, string actionName)
        {
            if (apiResponse?.Actions == null)
            {
                return false;
            }

            return apiResponse.Actions.Any(a => NormalizeRpgActionName(a?.action) == actionName);
        }

        private bool ShouldUseCooldownExitFallback(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return ContainsAnyPhrase(text, CooldownExitFallbackHints);
        }

        private bool ShouldUseNormalExitFallback(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return ContainsAnyPhrase(text, NormalExitFallbackHints);
        }

        private void EnsureRpgMinimumActionCoverage(LLMRpgApiResponse apiResponse)
        {
            if (apiResponse == null)
            {
                return;
            }

            if (HasAnyRpgEffects(apiResponse))
            {
                consecutiveNoActionAssistantTurns = 0;
                return;
            }

            consecutiveNoActionAssistantTurns++;
            int noActionThreshold = Math.Max(1, GetPromptPolicyForActionMapping()?.IntentNoActionStreakThreshold ?? 2);
            if (consecutiveNoActionAssistantTurns < noActionThreshold)
            {
                return;
            }

            if (TryAddNoActionStreakMemoryFallback(apiResponse))
            {
                consecutiveNoActionAssistantTurns = 0;
            }
        }

        private bool HasAnyRpgEffects(LLMRpgApiResponse apiResponse)
        {
            if (apiResponse == null)
            {
                return false;
            }

            return apiResponse.Actions?.Count > 0;
        }

        private bool TryAddNoActionStreakMemoryFallback(LLMRpgApiResponse apiResponse)
        {
            if (apiResponse?.Actions == null || HasRpgAction(apiResponse, "TryGainMemory"))
            {
                return false;
            }

            int rounds = GetNpcDialogueRoundCount();
            string memoryDefName = ResolveAutoMemoryDefName(rounds);
            if (string.IsNullOrWhiteSpace(memoryDefName))
            {
                return false;
            }

            apiResponse.Actions.Add(new LLMRpgApiResponse.ApiAction
            {
                action = "TryGainMemory",
                defName = memoryDefName,
                reason = "auto_no_action_streak"
            });

            AddSystemFeedback("RimChat_RPGSystem_MemoryRollSuccess".Translate(rounds, "100", "100", memoryDefName), 4.8f);
            return true;
        }

        private bool ContainsAnyPhrase(string text, IReadOnlyList<string> hints)
        {
            if (string.IsNullOrWhiteSpace(text) || hints == null || hints.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < hints.Count; i++)
            {
                string hint = hints[i];
                if (string.IsNullOrWhiteSpace(hint))
                {
                    continue;
                }

                if (text.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}


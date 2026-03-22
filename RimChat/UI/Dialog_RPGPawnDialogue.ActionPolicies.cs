using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.AI;
using RimChat.Config;
using RimChat.Memory;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: prompt policy config, RPG API response parsing, and RPG memory catalog.
    /// Responsibility: normalize RPG action names and apply exit/intent/memory fallback policies.
    ///</summary>
    public partial class Dialog_RPGPawnDialogue
    {
        private const int MemoryRound5Threshold = 5;
        private const float MemoryRoundChance = 0.8f;
        private bool memoryRound5Evaluated;
        private int consecutiveNoActionAssistantTurns;
        private int lastIntentMappedAssistantRound = -999;
        private bool autoMemoryFallbackConsumed;
        private bool suppressAutoMemoryFallbackForTurn;

        private static readonly string[] CooldownExitFallbackHints =
        {
            "leave me alone", "do not contact me", "don't contact me", "stop talking",
            "get lost", "go away", "don't bother me",
            "别再打扰", "别联系我", "不要再找我",
            "离我远点", "滚开", "请离开"
        };

        private static readonly string[] NormalExitFallbackHints =
        {
            "goodbye", "see you", "talk later", "let's pause", "need to go", "that's all for now",
            "再见", "不聊了", "今天就到这",
            "先聊到这", "就聊到这", "改天再聊",
            "我要去忙了", "晚点再说", "回头再聊"
        };

        private static readonly string[] StrongRejectHints =
        {
            "never", "won't", "refuse", "stop", "don't ask again", "leave me alone",
            "休想", "不可能", "少来", "别再问", "禁止"
        };

        private static readonly string[] CollaborationHints =
        {
            "i will do it", "i can help", "let me handle it", "leave it to me", "i'll take care of it",
            "我会去做", "我可以帮你", "我来处理", "交给我", "我会负责"
        };

        private enum IntentActionCategory
        {
            NeutralInfo = 0,
            CollaborationCommitment = 1,
            SoftEnding = 2,
            StrongReject = 3
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
                return NotifyInvalidTryGainMemory(requested);
            }

            LogTryGainMemoryResolution(requested, resolvedFrom, def);
            ApplyTryGainMemory(def);
            return true;
        }

        private ThoughtDef ResolveTryGainMemoryThoughtDef(string requestedDefName, out string resolvedFrom)
        {
            return RpgMemoryCatalog.ResolveRequestedThoughtDef(requestedDefName, out resolvedFrom);
        }

        private string BuildTryGainMemoryExamplesText()
        {
            return RpgMemoryCatalog.BuildPromptExamplesTextWithFallback("KindWordsMood, InsultedMood");
        }

        private bool NotifyInvalidTryGainMemory(string requestedDefName)
        {
            string examples = BuildTryGainMemoryExamplesText();
            string reason = "RimChat_RPGActionFail_InvalidDefName".Translate(string.IsNullOrEmpty(requestedDefName) ? "null" : requestedDefName);
            if (!string.IsNullOrWhiteSpace(examples))
            {
                reason += " " + "RimChat_RPGActionFail_DefNameExamples".Translate(examples);
            }

            NotifyActionFailure("TryGainMemory", reason);
            return false;
        }

        private void LogTryGainMemoryResolution(string requestedDefName, string resolvedFrom, ThoughtDef def)
        {
            if (string.IsNullOrWhiteSpace(resolvedFrom))
            {
                return;
            }

            LogRpgActionDebug($"TryGainMemory resolved alias '{requestedDefName}' -> '{def.defName}' via {resolvedFrom}");
        }

        private void ApplyTryGainMemory(ThoughtDef def)
        {
            target.needs.mood.thoughts.memories.TryGainMemory(def, initiator);
            string displayName = RpgMemoryCatalog.BuildDisplayName(def);
            float moodEffect = GetMoodEffect(def);
            Color moodColor = moodEffect >= 0 ? MoodPositiveColor : MoodNegativeColor;
            AddActionFeedback("RimChat_RPGSystem_MemoryApplied".Translate(), displayName, ActionInfoColor, moodColor, 3.8f);
        }

        private float GetMoodEffect(ThoughtDef def)
        {
            if (def?.stages == null || def.stages.Count == 0)
            {
                return 0f;
            }

            var stage = def.stages[0];
            return stage?.baseMoodEffect ?? 0f;
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
                "romanceattempt" or "romance_attempt" or "romance" or "fall_in_love" or "start_romance" or "恋爱" => "RomanceAttempt",
                "marriageproposal" or "marriage_proposal" or "propose_marriage" or "marry" or "结婚" => "MarriageProposal",
                "breakup" or "break_up" or "split_up" or "分手" => "Breakup",
                "divorce" or "离婚" => "Divorce",
                "date" or "dating" or "约会" => "Date",
                "trygainmemory" or "try_gain_memory" => "TryGainMemory",
                "tryaffectsocialgoodwill" or "try_affect_social_goodwill" => "TryAffectSocialGoodwill",
                "reduceresistance" or "reduce_resistance" => "ReduceResistance",
                "reducewill" or "reduce_will" => "ReduceWill",
                "recruit" or "action4" or "action_4" or "action 4" or "第4个动作" or "第四个动作" => "Recruit",
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

            bool allowAutoMemoryFallback = !ShouldSuppressAutoMemoryFallback();
            EnsureRpgExitActionFallback(apiResponse);
            EnsureRpgIntentDrivenActionMapping(apiResponse, allowAutoMemoryFallback);
            if (!allowAutoMemoryFallback)
            {
                return;
            }

            EnsureRpgMemoryActionFallback(apiResponse);
            EnsureRpgMinimumActionCoverage(apiResponse);
        }

        private void EnsureRpgExitActionFallback(LLMRpgApiResponse apiResponse)
        {
            if (apiResponse?.Actions == null || HasExitAction(apiResponse))
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

        private void EnsureRpgIntentDrivenActionMapping(LLMRpgApiResponse apiResponse, bool allowAutoMemoryFallback)
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

            if (!TryMapIntentDrivenAction(apiResponse, rounds, policy, allowAutoMemoryFallback))
            {
                return;
            }

            lastIntentMappedAssistantRound = rounds;
        }

        private static PromptPolicyConfig GetPromptPolicyForActionMapping()
        {
            SystemPromptConfig config = RimChat.Persistence.PromptPersistenceService.Instance?.LoadConfig();
            PromptPolicyConfig policy = config?.PromptPolicy;
            return policy?.Clone() ?? PromptPolicyConfig.CreateDefault();
        }

        private bool TryMapIntentDrivenAction(
            LLMRpgApiResponse apiResponse,
            int rounds,
            PromptPolicyConfig policy,
            bool allowAutoMemoryFallback)
        {
            IntentActionCategory category = ClassifyIntentActionCategory(apiResponse.DialogueContent);
            switch (category)
            {
                case IntentActionCategory.StrongReject:
                    return TryMapStrongRejectToAction(apiResponse);
                case IntentActionCategory.SoftEnding:
                    return TryMapSoftEndingToAction(apiResponse);
                case IntentActionCategory.CollaborationCommitment:
                    if (!allowAutoMemoryFallback)
                    {
                        return false;
                    }

                    return TryMapCollaborationToAction(apiResponse, rounds, policy);
                default:
                    return false;
            }
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

            return ContainsAnyPhrase(text, CollaborationHints)
                ? IntentActionCategory.CollaborationCommitment
                : IntentActionCategory.NeutralInfo;
        }

        private bool TryMapStrongRejectToAction(LLMRpgApiResponse apiResponse)
        {
            if (HasExitAction(apiResponse))
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
            if (HasExitAction(apiResponse))
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
            if (autoMemoryFallbackConsumed || HasAnyRpgEffects(apiResponse) || HasRpgAction(apiResponse, "TryGainMemory"))
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
            autoMemoryFallbackConsumed = true;
            return true;
        }

        private void EnsureRpgMemoryActionFallback(LLMRpgApiResponse apiResponse)
        {
            int rounds = GetNpcDialogueRoundCount();
            if (rounds < MemoryRound5Threshold || HasRpgAction(apiResponse, "TryGainMemory"))
            {
                memoryRound5Evaluated = rounds >= MemoryRound5Threshold;
                return;
            }

            if (memoryRound5Evaluated)
            {
                return;
            }

            memoryRound5Evaluated = true;
            TryAddRoundMemoryFallback(apiResponse, rounds, MemoryRoundChance);
        }

        private void TryAddRoundMemoryFallback(LLMRpgApiResponse apiResponse, int rounds, float chance)
        {
            if (autoMemoryFallbackConsumed)
            {
                return;
            }

            float roll = Rand.Value;
            if (roll > chance)
            {
                AddSystemFeedback("RimChat_RPGSystem_MemoryRollFailed".Translate(rounds, (chance * 100f).ToString("F0"), (roll * 100f).ToString("F0")));
                return;
            }

            ThoughtDef def = ResolveAutoMemoryThoughtDef(rounds);
            if (def == null)
            {
                AddSystemFeedback("RimChat_RPGSystem_MemoryNoDef".Translate());
                return;
            }

            apiResponse.Actions.Add(new LLMRpgApiResponse.ApiAction
            {
                action = "TryGainMemory",
                defName = def.defName,
                reason = "auto_round_memory"
            });
            autoMemoryFallbackConsumed = true;
            AddSystemFeedback("RimChat_RPGSystem_MemoryRollSuccess".Translate(rounds, (chance * 100f).ToString("F0"), (roll * 100f).ToString("F0"), RpgMemoryCatalog.BuildDisplayName(def)), 4.8f);
        }

        private string ResolveAutoMemoryDefName(int rounds)
        {
            ThoughtDef def = ResolveAutoMemoryThoughtDef(rounds);
            return def?.defName ?? string.Empty;
        }

        private ThoughtDef ResolveAutoMemoryThoughtDef(int rounds)
        {
            string defName = RpgMemoryCatalog.ResolveAutoDefName(rounds);
            return DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
        }

        private int GetNpcDialogueRoundCount()
        {
            return chatHistory?.Count(message => string.Equals(message.role, "assistant", StringComparison.Ordinal)) ?? 0;
        }

        private bool HasRpgAction(LLMRpgApiResponse apiResponse, string actionName)
        {
            if (apiResponse?.Actions == null)
            {
                return false;
            }

            return apiResponse.Actions.Any(action => NormalizeRpgActionName(action?.action) == actionName);
        }

        private bool HasExitAction(LLMRpgApiResponse apiResponse)
        {
            return HasRpgAction(apiResponse, "ExitDialogue") ||
                   HasRpgAction(apiResponse, "ExitDialogueCooldown");
        }

        private bool ShouldUseCooldownExitFallback(string text)
        {
            return ContainsAnyPhrase(text, CooldownExitFallbackHints);
        }

        private bool ShouldUseNormalExitFallback(string text)
        {
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

            if (!TryAddNoActionStreakMemoryFallback(apiResponse))
            {
                return;
            }

            consecutiveNoActionAssistantTurns = 0;
        }

        private bool HasAnyRpgEffects(LLMRpgApiResponse apiResponse)
        {
            return apiResponse?.Actions?.Count > 0;
        }

        private bool TryAddNoActionStreakMemoryFallback(LLMRpgApiResponse apiResponse)
        {
            if (autoMemoryFallbackConsumed || apiResponse?.Actions == null || HasRpgAction(apiResponse, "TryGainMemory"))
            {
                return false;
            }

            int rounds = GetNpcDialogueRoundCount();
            ThoughtDef def = ResolveAutoMemoryThoughtDef(rounds);
            if (def == null)
            {
                return false;
            }

            apiResponse.Actions.Add(new LLMRpgApiResponse.ApiAction
            {
                action = "TryGainMemory",
                defName = def.defName,
                reason = "auto_no_action_streak"
            });
            autoMemoryFallbackConsumed = true;
            AddSystemFeedback("RimChat_RPGSystem_MemoryRollSuccess".Translate(rounds, "100", "100", RpgMemoryCatalog.BuildDisplayName(def)), 4.8f);
            return true;
        }

        private bool ShouldSuppressAutoMemoryFallback()
        {
            return suppressAutoMemoryFallbackForTurn;
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

using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.AI;
using RimChat.Memory;
using RimChat.NpcDialogue;
using RimChat.Persistence;
using RimChat.Core;
using RimChat.UI;
using RimWorld;
using Verse;

namespace RimChat.PawnRpgPush
{
    /// <summary>/// Dependencies: AIChatServiceAsync, PromptPersistenceService, LeaderMemoryManager, Verse letter/window stack.
 /// Responsibility: Build PawnRPG proactive LLM requests, handle retry/drop policy, and deliver finalized letters.
 ///</summary>
    public partial class GameComponent_PawnRpgDialoguePushManager
    {
        private void StartGeneration(PawnRpgTriggerContext context, Pawn npcPawn, Pawn playerPawn)
        {
            if (context == null || npcPawn == null || playerPawn == null || !IsValidTargetFaction(context.Faction))
            {
                return;
            }

            if (!AIChatServiceAsync.Instance.IsConfigured())
            {
                Log.Warning($"[RimChat] PawnRPG proactive dropped (AI not configured): {context.Faction?.Name ?? "Unknown"}");
                return;
            }

            List<ChatMessageData> messages = BuildGenerationMessages(context, npcPawn, playerPawn);
            string requestId = string.Empty;
            requestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                messages,
                onSuccess: response => OnGenerationSuccess(requestId, response),
                onError: error => OnGenerationError(requestId, error),
                usageChannel: DialogueUsageChannel.Rpg,
                debugSource: AIRequestDebugSource.PawnRpgPush);

            if (string.IsNullOrEmpty(requestId))
            {
                return;
            }

            pendingRequests[requestId] = new PendingGenerationContext
            {
                Context = context,
                NpcPawn = npcPawn,
                PlayerPawn = playerPawn,
                Messages = messages,
                Attempt = 1
            };
        }

        private void OnGenerationSuccess(string requestId, string response)
        {
            if (string.IsNullOrEmpty(requestId) || !pendingRequests.TryGetValue(requestId, out PendingGenerationContext pending))
            {
                return;
            }

            pendingRequests.Remove(requestId);
            string message = SanitizeModelOutput(response);
            if (string.IsNullOrWhiteSpace(message))
            {
                Log.Warning("[RimChat] PawnRPG proactive generation empty after sanitize.");
                return;
            }

            DeliverMessage(pending.Context, pending.NpcPawn, pending.PlayerPawn, message);
        }

        private void OnGenerationError(string requestId, string error)
        {
            if (string.IsNullOrEmpty(requestId) || !pendingRequests.TryGetValue(requestId, out PendingGenerationContext pending))
            {
                return;
            }

            pendingRequests.Remove(requestId);
            if (pending.Attempt < 2 && AIChatServiceAsync.Instance.IsConfigured())
            {
                RetryGeneration(pending);
                return;
            }

            Log.Warning($"[RimChat] PawnRPG proactive dropped after retry: {error}");
        }

        private void RetryGeneration(PendingGenerationContext pending)
        {
            string retryId = string.Empty;
            retryId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                pending.Messages,
                onSuccess: response => OnGenerationSuccess(retryId, response),
                onError: error => OnGenerationError(retryId, error),
                usageChannel: DialogueUsageChannel.Rpg,
                debugSource: AIRequestDebugSource.PawnRpgPush);

            if (string.IsNullOrEmpty(retryId))
            {
                return;
            }

            pendingRequests[retryId] = new PendingGenerationContext
            {
                Context = pending.Context,
                NpcPawn = pending.NpcPawn,
                PlayerPawn = pending.PlayerPawn,
                Messages = pending.Messages,
                Attempt = pending.Attempt + 1
            };
        }

        private void DeliverMessage(PawnRpgTriggerContext context, Pawn npcPawn, Pawn playerPawn, string text)
        {
            if (context == null || npcPawn == null || playerPawn == null || !IsValidTargetFaction(context.Faction))
            {
                return;
            }

            if (ChoiceLetter_PawnRpgInitiatedDialogue.IsDialogueAlreadyOpen(playerPawn, npcPawn))
            {
                return;
            }

            TaggedString title = GetLetterTitle(context, npcPawn, playerPawn);
            LetterDef letterDef = GetLetterDef(context);
            var letter = new ChoiceLetter_PawnRpgInitiatedDialogue();
            letter.Setup(npcPawn, playerPawn, title, text, letterDef);
            Find.LetterStack.ReceiveLetter(letter, string.Empty, 0, true);

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            PawnRpgNpcPushState npcState = GetOrCreateNpcState(npcPawn);
            npcState.lastNpcEvaluateTick = currentTick;
            if (!CanBypassGlobalCooldown(context))
            {
                lastColonyDeliveredTick = currentTick;
            }
        }

        private TaggedString GetLetterTitle(PawnRpgTriggerContext context, Pawn npcPawn, Pawn playerPawn)
        {
            string key = context?.Category switch
            {
                NpcDialogueCategory.DiplomacyTask => "RimChat_PawnRpgPush_TitleTask",
                NpcDialogueCategory.WarningThreat => "RimChat_PawnRpgPush_TitleWarning",
                _ => "RimChat_PawnRpgPush_TitleSocial"
            };
            string senderName = npcPawn?.Name?.ToStringShort ?? npcPawn?.LabelShort ?? "Unknown";
            string receiverName = playerPawn?.Name?.ToStringShort ?? playerPawn?.LabelShort ?? "Unknown";
            return key.Translate(senderName, receiverName);
        }

        private LetterDef GetLetterDef(PawnRpgTriggerContext context)
        {
            if (context?.Category == NpcDialogueCategory.WarningThreat)
            {
                return context.Severity >= 3 ? LetterDefOf.ThreatBig : LetterDefOf.ThreatSmall;
            }

            return context?.Category == NpcDialogueCategory.DiplomacyTask
                ? LetterDefOf.PositiveEvent
                : LetterDefOf.NeutralEvent;
        }

        private List<ChatMessageData> BuildGenerationMessages(PawnRpgTriggerContext context, Pawn npcPawn, Pawn playerPawn)
        {
            var messages = new List<ChatMessageData>();
            PromptPersistenceService.Instance.Initialize();
            List<string> sceneTags = BuildProactiveSceneTags(context?.Category ?? NpcDialogueCategory.Social);
            string systemPrompt = PromptPersistenceService.Instance.BuildRPGFullSystemPrompt(playerPawn, npcPawn, true, sceneTags);
            messages.Add(new ChatMessageData { role = "system", content = systemPrompt });
            AppendRecentRpgContext(messages, npcPawn, playerPawn);

            string category = context.Category switch
            {
                NpcDialogueCategory.DiplomacyTask => "diplomacy_task",
                NpcDialogueCategory.WarningThreat => "warning_threat",
                _ => "social"
            };
            string reason = BuildReasonText(context);
            string userPrompt =
                $"Generate one proactive pawn dialogue line now.\n" +
                $"Category: {category}\n" +
                $"TriggerType: {context.TriggerType}\n" +
                $"Reason: {reason}\n" +
                $"Severity: {context.Severity}\n";
            messages.Add(new ChatMessageData { role = "user", content = userPrompt });
            return messages;
        }

        private List<string> BuildProactiveSceneTags(NpcDialogueCategory category)
        {
            var tags = new List<string>();
            switch (category)
            {
                case NpcDialogueCategory.DiplomacyTask:
                    tags.Add("scene:task");
                    break;
                case NpcDialogueCategory.WarningThreat:
                    tags.Add("scene:threat");
                    tags.Add("scene:conflict");
                    break;
                default:
                    tags.Add("scene:daily");
                    break;
            }

            return tags;
        }

        private void AppendRecentRpgContext(List<ChatMessageData> messages, Pawn npcPawn, Pawn playerPawn)
        {
            if (messages == null || npcPawn == null || playerPawn == null)
            {
                return;
            }

            string npcMood = TryGetMoodPercent(npcPawn, out float mood) ? $"{(mood * 100f):F0}%" : "unknown";
            int opinion = GetOpinion(npcPawn, playerPawn);
            bool intimate = HasIntimateRelation(npcPawn, playerPawn);
            string relationLine = intimate ? "intimate" : $"opinion_{opinion}";
            string contextLine = $"Current relation={relationLine}, npc_mood={npcMood}.";
            messages.Add(new ChatMessageData { role = "user", content = contextLine });

            var memory = LeaderMemoryManager.Instance?.GetMemory(npcPawn.Faction);
            if (memory?.SignificantEvents != null && memory.SignificantEvents.Count > 0)
            {
                string memoryText = string.Join("; ", memory.SignificantEvents
                    .OrderByDescending(e => e.OccurredTick)
                    .Take(2)
                    .Select(e => e.Description)
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
                messages.Add(new ChatMessageData { role = "user", content = $"Recent high-impact memories: {memoryText}" });
            }
        }

        private string BuildReasonText(PawnRpgTriggerContext context)
        {
            if (context == null)
            {
                return "unknown";
            }

            if (context.SourceTag == "quest_deadline")
            {
                string[] parts = (context.Metadata ?? string.Empty).Split('|');
                if (parts.Length >= 3 && int.TryParse(parts[2], out int ticksLeft))
                {
                    float hours = ticksLeft / (float)TickPerHour;
                    return $"quest_deadline:{parts[1]}:{hours:F1}h_left";
                }
            }

            if (context.SourceTag == "trade_completed")
            {
                string[] parts = (context.Metadata ?? string.Empty).Split('|');
                if (parts.Length >= 2)
                {
                    return $"trade_completed:sold={parts[0]},bought={parts[1]}";
                }
            }

            return $"{context.SourceTag}:{context.Reason}";
        }

        private string SanitizeModelOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return string.Empty;
            }

            string cleaned = output.Replace("```json", string.Empty).Replace("```", string.Empty).Trim();
            int jsonIndex = cleaned.IndexOf('{');
            if (jsonIndex >= 0)
            {
                cleaned = cleaned.Substring(0, jsonIndex).Trim();
            }

            string[] lines = cleaned
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
            if (lines.Length == 0)
            {
                return string.Empty;
            }

            string merged = string.Join(" ", lines);
            int hardLimit = RimChatMod.Settings?.ProactiveMessageHardLimit ?? 0;
            if (hardLimit > 0 && merged.Length > hardLimit)
            {
                merged = merged.Substring(0, hardLimit).TrimEnd();
            }

            ImmersionGuardResult guardResult = ImmersionOutputGuard.ValidateVisibleDialogue(merged);
            if (!guardResult.IsValid)
            {
                Log.Warning($"[RimChat] Immersion guard blocked PawnRPG push text: reason={ImmersionOutputGuard.BuildViolationTag(guardResult.ViolationReason)}, snippet={guardResult.ViolationSnippet}");
                return ImmersionOutputGuard.BuildLocalFallbackDialogue(DialogueUsageChannel.Rpg);
            }

            return guardResult.VisibleDialogue;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimDiplomacy.AI;
using RimDiplomacy.Memory;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimDiplomacy.UI
{
    /// <summary>
    /// Dependencies: AIResponseParser.StrategySuggestion, FactionDialogueSession, negotiator/map context.
    /// Responsibility: 策略建议按钮展示、发送、会话缓存与上下文注入（外交窗口专用）。
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private const int StrategySuggestionRequiredCount = 3;
        private const float StrategyButtonSpacing = 6f;
        private const float StrategyIconSlotWidth = 34f;
        private const float StrategyAnimSpeed = 10f;
        private const float StrategyIntroOffset = 5f;
        private const int StrategyLabelDisplayMaxChars = 10;
        private const int StrategyHistoryWindow = 8;
        private float strategyBarAnimProgress = 0f;
        private bool strategySuggestionRequestPending = false;
        private int strategyFxSignature = 0;
        private float strategyFxStartRealtime = -99f;

        private void DrawControlsRow(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.13f));

            Rect iconRect = new Rect(rect.x + 4f, rect.y, StrategyIconSlotWidth, rect.height);
            fiveDimensionBar.DrawCompactIcon(iconRect);

            Rect strategyRect = new Rect(iconRect.xMax + 4f, rect.y, rect.width - (iconRect.width + 8f), rect.height);
            DrawStrategyStatusHint(strategyRect);
            DrawStrategySuggestionBar(strategyRect);
        }

        private void DrawStrategySuggestionBar(Rect rect)
        {
            bool shouldShow = ShouldShowStrategySuggestionBar();
            float target = shouldShow ? 1f : 0f;
            strategyBarAnimProgress = Mathf.MoveTowards(strategyBarAnimProgress, target, Time.deltaTime * StrategyAnimSpeed);

            if (strategyBarAnimProgress <= 0.01f)
            {
                return;
            }

            float barAlpha = Mathf.SmoothStep(0f, 1f, strategyBarAnimProgress);
            GUI.color = new Color(1f, 1f, 1f, barAlpha);
            var list = session.pendingStrategySuggestions?.ToList();
            if (list == null || list.Count == 0)
            {
                GUI.color = Color.white;
                return;
            }
            int count = Mathf.Min(StrategySuggestionRequiredCount, list.Count);
            if (count <= 0)
            {
                GUI.color = Color.white;
                return;
            }
            float buttonWidth = (rect.width - (count - 1) * StrategyButtonSpacing) / count;
            float buttonHeight = rect.height - 4f;
            Rect barBgRect = new Rect(rect.x, rect.y + 2f, rect.width, buttonHeight);
            Widgets.DrawBoxSolid(barBgRect, new Color(0.13f, 0.16f, 0.2f, 0.52f * barAlpha));
            DrawStrategyAppearFx(rect, count, buttonWidth, buttonHeight, barAlpha, list);

            for (int i = 0; i < count; i++)
            {
                float itemProgress = Mathf.Clamp01((barAlpha - i * 0.06f) / 0.72f);
                if (itemProgress <= 0.01f)
                {
                    continue;
                }

                float easedProgress = Mathf.SmoothStep(0f, 1f, itemProgress);
                float yOffset = (1f - easedProgress) * StrategyIntroOffset;
                Rect btnRect = new Rect(rect.x + i * (buttonWidth + StrategyButtonSpacing), rect.y + 2f + yOffset, buttonWidth, buttonHeight);
                var suggestion = list[i];

                Color old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, easedProgress);
                if (Widgets.ButtonText(btnRect, BuildStrategyButtonLabel(suggestion)))
                {
                    TrySendStrategySuggestion(suggestion);
                    GUI.color = Color.white;
                    return;
                }
                GUI.color = old;

                AddStrategyTooltip(btnRect, suggestion);
            }
            GUI.color = Color.white;
        }

        private void DrawStrategyAppearFx(Rect rect, int count, float buttonWidth, float buttonHeight, float alpha, List<PendingStrategySuggestion> list)
        {
            int signature = 17;
            for (int i = 0; i < count; i++)
            {
                signature = signature * 31 + ((list[i]?.ShortLabel ?? string.Empty).GetHashCode());
            }
            if (signature != strategyFxSignature)
            {
                strategyFxSignature = signature;
                strategyFxStartRealtime = Time.realtimeSinceStartup;
            }

            float elapsed = Time.realtimeSinceStartup - strategyFxStartRealtime;
            if (elapsed < 0f || elapsed > 0.75f)
            {
                return;
            }

            float progress = Mathf.Clamp01(elapsed / 0.75f);
            float glowAlpha = (1f - progress) * 0.28f * alpha;
            for (int i = 0; i < count; i++)
            {
                Rect baseRect = new Rect(rect.x + i * (buttonWidth + StrategyButtonSpacing), rect.y + 2f, buttonWidth, buttonHeight);
                Rect fxRect = baseRect.ExpandedBy(8f * (1f - progress));
                Widgets.DrawBoxSolid(fxRect, new Color(0.35f, 0.72f, 1f, glowAlpha));
            }
        }

        private bool ShouldShowStrategySuggestionBar()
        {
            if (session == null || session.isWaitingForResponse)
            {
                return false;
            }

            if (session.pendingStrategySuggestions == null || session.pendingStrategySuggestions.Count != StrategySuggestionRequiredCount)
            {
                return false;
            }

            bool blocked = IsInputBlockedByPresence(out _, out _);
            if (blocked || !CanSendMessageNow())
            {
                return false;
            }

            return HasStrategyUsesRemaining(session);
        }

        private string BuildStrategyButtonLabel(PendingStrategySuggestion suggestion)
        {
            string label = suggestion?.ShortLabel ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label))
            {
                label = "RimDiplomacy_StrategyFallbackLabel".Translate();
            }
            label = label.Replace("\r", string.Empty).Replace("\n", " ").Trim();
            if (label.Length > StrategyLabelDisplayMaxChars)
            {
                return label.Substring(0, StrategyLabelDisplayMaxChars);
            }
            return label;
        }

        private void DrawStrategyStatusHint(Rect rect)
        {
            string hint = BuildStrategyStatusHint();
            if (string.IsNullOrWhiteSpace(hint))
            {
                return;
            }

            GUI.color = new Color(0.72f, 0.78f, 0.86f, 0.8f);
            Text.Font = GameFont.Tiny;
            Rect hintRect = new Rect(rect.x + 6f, rect.y + 8f, rect.width - 10f, 16f);
            Widgets.Label(hintRect, hint);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private string BuildStrategyStatusHint()
        {
            if (session == null)
            {
                return string.Empty;
            }

            int social = GetNegotiatorSocialLevel();
            int useLimit = GetStrategyUseLimitBySocial(social);
            int remaining = Math.Max(0, useLimit - session.strategyUsesConsumed);
            if (social < 5)
            {
                return "RimDiplomacy_StrategyNeedSocialHint".Translate(social);
            }

            if (strategySuggestionRequestPending && remaining > 0)
            {
                return "RimDiplomacy_StrategyGeneratingHint".Translate(remaining, useLimit);
            }

            if (remaining <= 0)
            {
                return "RimDiplomacy_StrategyUsesExhaustedHint".Translate(useLimit);
            }

            if (session.pendingStrategySuggestions != null && session.pendingStrategySuggestions.Count == StrategySuggestionRequiredCount)
            {
                return string.Empty;
            }

            return "RimDiplomacy_StrategyRemainingHint".Translate(remaining, useLimit);
        }

        private void AddStrategyTooltip(Rect rect, PendingStrategySuggestion suggestion)
        {
            if (suggestion == null)
            {
                return;
            }

            string keywords = suggestion.StrategyKeywords == null || suggestion.StrategyKeywords.Count == 0
                ? string.Empty
                : string.Join(" / ", suggestion.StrategyKeywords.Take(4));

            string basis = string.IsNullOrWhiteSpace(suggestion.TriggerBasis)
                ? "RimDiplomacy_StrategyFallbackBasis".Translate()
                : suggestion.TriggerBasis;

            string tip = string.IsNullOrWhiteSpace(keywords)
                ? basis
                : $"{basis}\n{keywords}";

            if (!string.IsNullOrWhiteSpace(tip))
            {
                TooltipHandler.TipRegion(rect, tip);
            }
        }

        private void TrySendStrategySuggestion(PendingStrategySuggestion suggestion)
        {
            if (suggestion == null || string.IsNullOrWhiteSpace(suggestion.HiddenReply))
            {
                return;
            }

            if (!HasStrategyUsesRemaining(session))
            {
                return;
            }

            if (!CanSendMessageNow())
            {
                return;
            }

            session.strategyUsesConsumed++;
            inputText = string.Empty;
            SendPreparedMessage(suggestion.HiddenReply.Trim(), true);
        }

        private void ApplyStrategySuggestions(FactionDialogueSession currentSession, List<StrategySuggestion> suggestions)
        {
            if (currentSession == null)
            {
                return;
            }

            if (currentSession.isConversationEndedByNpc)
            {
                ClearPendingStrategySuggestions(currentSession);
                strategySuggestionRequestPending = false;
                return;
            }

            if (!HasStrategyUsesRemaining(currentSession))
            {
                ClearPendingStrategySuggestions(currentSession);
                strategySuggestionRequestPending = false;
                return;
            }

            if (suggestions == null || suggestions.Count != StrategySuggestionRequiredCount)
            {
                ClearPendingStrategySuggestions(currentSession);
                Log.Message("[RimDiplomacy] Strategy payload missing/invalid, requesting follow-up strategy payload.");
                TryRequestStrategySuggestionsFromLLM(currentSession, faction);
                return;
            }

            var mapped = suggestions
                .Select(MapStrategySuggestion)
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.HiddenReply))
                .Take(StrategySuggestionRequiredCount)
                .ToList();

            if (mapped.Count != StrategySuggestionRequiredCount)
            {
                ClearPendingStrategySuggestions(currentSession);
                Log.Message("[RimDiplomacy] Strategy payload invalid after parse, requesting follow-up strategy payload.");
                TryRequestStrategySuggestionsFromLLM(currentSession, faction);
                return;
            }

            ApplyAttributeBasisFallback(mapped);
            strategySuggestionRequestPending = false;
            currentSession.pendingStrategySuggestions = mapped;
        }

        private bool HasStrategyUsesRemaining(FactionDialogueSession currentSession)
        {
            if (currentSession == null)
            {
                return false;
            }

            int social = GetNegotiatorSocialLevel();
            int useLimit = GetStrategyUseLimitBySocial(social);
            return social >= 5 && currentSession.strategyUsesConsumed < useLimit;
        }

        private int GetNegotiatorSocialLevel()
        {
            return negotiator?.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
        }

        private int GetStrategyUseLimitBySocial(int socialLevel)
        {
            if (socialLevel < 5)
            {
                return 0;
            }

            if (socialLevel >= 15)
            {
                return 3;
            }

            if (socialLevel >= 10)
            {
                return 2;
            }

            return 1;
        }

        private void TryRequestStrategySuggestionsFromLLM(FactionDialogueSession currentSession, Faction currentFaction)
        {
            if (currentSession == null || currentFaction == null || strategySuggestionRequestPending)
            {
                return;
            }

            if (!HasStrategyUsesRemaining(currentSession))
            {
                return;
            }

            if (currentSession.pendingStrategySuggestions != null &&
                currentSession.pendingStrategySuggestions.Count == StrategySuggestionRequiredCount)
            {
                return;
            }

            if (!AIChatServiceAsync.Instance.IsConfigured())
            {
                return;
            }

            var requestMessages = BuildStrategySuggestionRequestMessages(currentSession, currentFaction);
            if (requestMessages == null || requestMessages.Count == 0)
            {
                return;
            }

            int snapshotMessageCount = currentSession.messages?.Count ?? 0;
            strategySuggestionRequestPending = true;
            Log.Message("[RimDiplomacy] Sending strategy follow-up request.");

            AIChatServiceAsync.Instance.SendChatRequestAsync(
                requestMessages,
                onSuccess: response =>
                {
                    strategySuggestionRequestPending = false;
                    if (currentSession == null)
                    {
                        return;
                    }

                    if ((currentSession.messages?.Count ?? 0) != snapshotMessageCount || currentSession.isWaitingForResponse)
                    {
                        return;
                    }

                    var parsed = AIResponseParser.ParseResponse(response, currentFaction);
                    var mapped = parsed?.StrategySuggestions?
                        .Select(MapStrategySuggestion)
                        .Where(s => s != null && !string.IsNullOrWhiteSpace(s.HiddenReply))
                        .Take(StrategySuggestionRequiredCount)
                        .ToList() ?? new List<PendingStrategySuggestion>();

                    if (mapped.Count == StrategySuggestionRequiredCount)
                    {
                        currentSession.pendingStrategySuggestions = mapped;
                        Log.Message("[RimDiplomacy] Strategy follow-up request succeeded, strategy buttons primed.");
                    }
                },
                onError: error =>
                {
                    strategySuggestionRequestPending = false;
                    Log.Warning($"[RimDiplomacy] Strategy follow-up request failed: {error}");
                },
                onProgress: null
            );
        }

        private List<ChatMessageData> BuildStrategySuggestionRequestMessages(FactionDialogueSession currentSession, Faction currentFaction)
        {
            var messages = new List<ChatMessageData>();
            var sb = new StringBuilder();
            sb.AppendLine("You generate strategy_suggestions for a diplomacy UI.");
            sb.AppendLine("Output JSON only. Do not output markdown.");
            sb.AppendLine("Required format:");
            sb.AppendLine("{\"strategy_suggestions\":[{\"short_label\":\"\",\"trigger_basis\":\"\",\"strategy_keywords\":[\"\"],\"hidden_reply\":\"\"},{...},{...}]}");
            sb.AppendLine("Rules:");
            sb.AppendLine("- Exactly 3 items.");
            sb.AppendLine("- short_label <= 8 Chinese characters and must be actionable strategy intent (not full sentence).");
            sb.AppendLine("- trigger_basis concise (<= 10 Chinese characters).");
            sb.AppendLine("- hidden_reply must be a complete sendable line.");
            sb.AppendLine("- Keep style aligned with current faction voice and player's language.");
            sb.AppendLine("- At least 2 items must explicitly be based on player attributes/context: social skill, traits, colony wealth tier, recent player tone.");
            sb.AppendLine("- Prefer strategy direction, not generic consolation wording.");
            messages.Add(new ChatMessageData { role = "system", content = sb.ToString() });

            string strategyContext = BuildStrategyPlayerContextPrompt();
            if (!string.IsNullOrWhiteSpace(strategyContext))
            {
                messages.Add(new ChatMessageData { role = "system", content = strategyContext });
            }

            messages.Add(new ChatMessageData
            {
                role = "system",
                content = $"Faction: {currentFaction.Name}\nCurrentGoodwill: {currentFaction.PlayerGoodwill}\nStrategyRemainingUses: {Math.Max(0, GetStrategyUseLimitBySocial(GetNegotiatorSocialLevel()) - currentSession.strategyUsesConsumed)}"
            });

            AppendRecentDialogueForStrategy(messages, currentSession);
            messages.Add(new ChatMessageData { role = "user", content = "Generate strategy_suggestions now." });
            return messages;
        }

        private void AppendRecentDialogueForStrategy(List<ChatMessageData> messages, FactionDialogueSession currentSession)
        {
            if (messages == null || currentSession?.messages == null || currentSession.messages.Count == 0)
            {
                return;
            }

            int start = Mathf.Max(0, currentSession.messages.Count - StrategyHistoryWindow);
            for (int i = start; i < currentSession.messages.Count; i++)
            {
                var msg = currentSession.messages[i];
                if (msg == null || msg.IsSystemMessage() || string.IsNullOrWhiteSpace(msg.message))
                {
                    continue;
                }

                string role = msg.isPlayer ? "user" : "assistant";
                messages.Add(new ChatMessageData { role = role, content = msg.message.Trim() });
            }
        }

        private PendingStrategySuggestion MapStrategySuggestion(StrategySuggestion source)
        {
            if (source == null)
            {
                return null;
            }

            string shortLabel = (source.ShortLabel ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (shortLabel.Length > 8)
            {
                shortLabel = shortLabel.Substring(0, 8);
            }

            string basis = (source.TriggerBasis ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (basis.Length > 10)
            {
                basis = basis.Substring(0, 10);
            }

            return new PendingStrategySuggestion
            {
                ShortLabel = shortLabel,
                TriggerBasis = basis,
                StrategyKeywords = source.StrategyKeywords?.Take(5).ToList() ?? new List<string>(),
                HiddenReply = source.HiddenReply ?? string.Empty
            };
        }

        private void ApplyAttributeBasisFallback(List<PendingStrategySuggestion> suggestions)
        {
            if (suggestions == null || suggestions.Count == 0)
            {
                return;
            }

            var basisPool = BuildAttributeBasisPool();
            if (basisPool.Count == 0)
            {
                basisPool.Add("RimDiplomacy_StrategyFallbackBasis".Translate());
            }

            for (int i = 0; i < suggestions.Count; i++)
            {
                var item = suggestions[i];
                if (item == null)
                {
                    continue;
                }

                if (IsGenericBasis(item.TriggerBasis))
                {
                    item.TriggerBasis = basisPool[i % basisPool.Count];
                }
            }
        }

        private List<string> BuildAttributeBasisPool()
        {
            var list = new List<string>();
            int social = GetNegotiatorSocialLevel();
            if (social >= 5)
            {
                list.Add("RimDiplomacy_StrategyBasisSocial".Translate());
            }

            if (negotiator?.story?.traits?.allTraits != null && negotiator.story.traits.allTraits.Count > 0)
            {
                list.Add("RimDiplomacy_StrategyBasisTrait".Translate(negotiator.story.traits.allTraits[0].Label));
            }

            float wealth = 0f;
            if (Find.Maps != null)
            {
                wealth = Find.Maps.Where(m => m.IsPlayerHome).Sum(m => m.wealthWatcher?.WealthTotal ?? 0f);
            }
            list.Add(wealth >= 120000f
                ? "RimDiplomacy_StrategyBasisWealthHigh".Translate()
                : "RimDiplomacy_StrategyBasisWealth".Translate());
            list.Add("RimDiplomacy_StrategyBasisRecentTone".Translate());
            return list;
        }

        private bool IsGenericBasis(string basis)
        {
            if (string.IsNullOrWhiteSpace(basis))
            {
                return true;
            }

            string normalized = basis.Trim().ToLowerInvariant();
            return normalized == "综合判断" ||
                   normalized == "综合" ||
                   normalized == "general" ||
                   normalized == "generic" ||
                   normalized.Contains("unknown");
        }

        private void ClearPendingStrategySuggestions(FactionDialogueSession currentSession)
        {
            if (currentSession == null)
            {
                return;
            }

            currentSession.pendingStrategySuggestions?.Clear();
        }

        private string BuildStrategyPlayerContextPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== PLAYER STRATEGY CONTEXT (SOFT HINTS) ===");
            AppendNegotiatorContext(sb);
            AppendColonyWealthContext(sb);
            AppendRecentInteractionContext(sb);
            AppendStrategyAvailabilityContext(sb);
            sb.AppendLine("Use the context above as soft hints only; do not treat them as hard thresholds.");
            return sb.ToString();
        }

        private void AppendStrategyAvailabilityContext(StringBuilder sb)
        {
            if (session == null)
            {
                return;
            }

            int social = GetNegotiatorSocialLevel();
            int useLimit = GetStrategyUseLimitBySocial(social);
            int remaining = Math.Max(0, useLimit - session.strategyUsesConsumed);
            sb.AppendLine($"Strategy Ability: social={social}, max_uses={useLimit}, remaining_uses={remaining}");
            sb.AppendLine("If remaining_uses <= 0, do not include strategy_suggestions.");
            sb.AppendLine("If remaining_uses > 0, prefer compact, attribute-grounded strategy suggestions.");
        }

        private void AppendNegotiatorContext(StringBuilder sb)
        {
            if (negotiator == null)
            {
                sb.AppendLine("Negotiator: unavailable");
                return;
            }

            int social = negotiator.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            string traits = negotiator.story?.traits?.allTraits == null
                ? "none"
                : string.Join(", ", negotiator.story.traits.allTraits.Select(t => t.Label).Take(6));

            sb.AppendLine($"Negotiator: {negotiator.LabelShort} | Social: {social}");
            sb.AppendLine($"Negotiator Traits: {traits}");
        }

        private void AppendColonyWealthContext(StringBuilder sb)
        {
            float wealth = 0f;
            if (Find.Maps != null)
            {
                wealth = Find.Maps.Where(m => m.IsPlayerHome).Sum(m => m.wealthWatcher?.WealthTotal ?? 0f);
            }

            string tier = wealth switch
            {
                >= 250000f => "顶级",
                >= 120000f => "高",
                >= 50000f => "中",
                >= 15000f => "低",
                _ => "极低"
            };
            sb.AppendLine($"Colony Wealth: {wealth:F0} (Tier: {tier})");
        }

        private void AppendRecentInteractionContext(StringBuilder sb)
        {
            if (session?.messages == null || session.messages.Count == 0)
            {
                sb.AppendLine("Recent Player Interaction: none");
                return;
            }

            var recentPlayers = session.messages
                .Where(m => m != null && m.isPlayer && !string.IsNullOrWhiteSpace(m.message))
                .Reverse()
                .Take(4)
                .Select(m => m.message.Replace("\n", " ").Trim())
                .ToList();

            if (recentPlayers.Count == 0)
            {
                sb.AppendLine("Recent Player Interaction: none");
                return;
            }

            int aggressiveCount = recentPlayers.Count(m => ContainsAnyStrategyToken(m.ToLowerInvariant(),
                "war", "attack", "threat", "kill", "侮辱", "威胁", "进攻", "开战", "袭击"));

            sb.AppendLine($"Recent Player Interaction: {recentPlayers.Count} turns, aggressive={aggressiveCount}");
            sb.AppendLine($"Recent Snippets: {string.Join(" || ", recentPlayers.Select(m => m.Length > 60 ? m.Substring(0, 60) : m))}");
        }

        private bool ContainsAnyStrategyToken(string source, params string[] tokens)
        {
            if (string.IsNullOrEmpty(source) || tokens == null)
            {
                return false;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(tokens[i]) && source.Contains(tokens[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

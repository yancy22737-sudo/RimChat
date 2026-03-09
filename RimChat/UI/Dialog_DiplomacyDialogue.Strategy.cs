using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.AI;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimChat.WorldState;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: AIResponseParser.StrategySuggestion, FactionDialogueSession, negotiator/map context.
 /// Responsibility: 策略建议button展示, 发送, session缓存与context注入 (diplomacywindow专用) .
 ///</summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private const int StrategySuggestionRequiredCount = 3;
        private const float StrategyButtonSpacing = 6f;
        private const float StrategyIconSlotWidth = 34f;
        private const float StrategyAnimSpeed = 10f;
        private const float StrategyIntroOffset = 5f;
        private const int StrategyLabelDisplayMaxChars = 6;
        private const int StrategyBasisDisplayMaxChars = 8;
        private const int StrategyTooltipReplyMaxChars = 72;
        private float strategyBarAnimProgress = 0f;
        private bool strategySuggestionRequestPending = false;
        private string strategySuggestionRequestId = null;
        private int strategyFxSignature = 0;
        private float strategyFxStartRealtime = -99f;

        private void DrawControlsRow(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.13f));
            DrawStrategyStatusHint(rect);
            DrawStrategySuggestionBar(rect);
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
                signature = signature * 31 + ((list[i]?.StrategyName ?? string.Empty).GetHashCode());
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
            string label = suggestion?.StrategyName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label))
            {
                label = "RimChat_StrategyFallbackLabel".Translate();
            }
            label = label.Replace("\r", string.Empty).Replace("\n", " ").Trim();
            if (IsGenericStrategyLabel(label))
            {
                label = BuildStrategyLabelFromReply(suggestion?.Content ?? string.Empty);
            }
            if (label.Length > StrategyLabelDisplayMaxChars)
            {
                label = label.Substring(0, StrategyLabelDisplayMaxChars);
            }

            string basis = CompactStrategyReasonForDisplay(suggestion?.FactReason ?? string.Empty);
            if (basis.Length > StrategyBasisDisplayMaxChars)
            {
                basis = basis.Substring(0, StrategyBasisDisplayMaxChars);
            }

            if (string.IsNullOrWhiteSpace(basis))
            {
                return label;
            }

            return $"{label}（{basis}）";
        }

        private string CompactStrategyReasonForDisplay(string reason)
        {
            string compact = (reason ?? string.Empty).Replace("\r", string.Empty).Replace("\n", " ").Trim();
            compact = System.Text.RegularExpressions.Regex.Replace(
                compact,
                "\\[\\s*F\\d+\\s*\\]",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            compact = compact.Replace("事实", string.Empty)
                             .Replace("原因", string.Empty)
                             .Replace("because", string.Empty)
                             .Replace("Because", string.Empty)
                             .Trim(' ', ':', '：', '-', '|', ';', '；');

            var parts = new List<string>();
            string wealth = ExtractWealthTier(compact);
            if (!string.IsNullOrWhiteSpace(wealth))
            {
                parts.Add($"财富{wealth}");
            }

            int? social = ExtractIntNearKeyword(compact, "社交", "social");
            if (social.HasValue)
            {
                parts.Add($"社交{social.Value}");
            }

            int? population = ExtractIntNearKeyword(compact, "殖民者", "人口", "colonists");
            if (population.HasValue)
            {
                parts.Add($"人口{population.Value}");
            }

            if (parts.Count > 0)
            {
                return string.Join("·", parts.Take(2));
            }

            string head = compact;
            int separator = head.IndexOfAny(new[] { '，', ',', '。', ';', '；', '|' });
            if (separator > 0)
            {
                head = head.Substring(0, separator).Trim();
            }
            if (head.Length <= StrategyBasisDisplayMaxChars)
            {
                return head;
            }
            return head.Substring(0, StrategyBasisDisplayMaxChars);
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
                return "RimChat_StrategyNeedSocialHint".Translate(social);
            }

            if (strategySuggestionRequestPending && remaining > 0)
            {
                return "RimChat_StrategyGeneratingHint".Translate(remaining, useLimit);
            }

            if (remaining <= 0)
            {
                return "RimChat_StrategyUsesExhaustedHint".Translate(useLimit);
            }

            if (session.pendingStrategySuggestions != null && session.pendingStrategySuggestions.Count == StrategySuggestionRequiredCount)
            {
                return "RimChat_StrategyReadyHint".Translate(remaining, useLimit);
            }

            return "RimChat_StrategyRemainingHint".Translate(remaining, useLimit);
        }

        private void AddStrategyTooltip(Rect rect, PendingStrategySuggestion suggestion)
        {
            if (suggestion == null)
            {
                return;
            }

            string reason = string.IsNullOrWhiteSpace(suggestion.FactReason)
                ? "RimChat_StrategyFallbackBasis".Translate()
                : suggestion.FactReason;

            string contentPreview = (suggestion.Content ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (contentPreview.Length > StrategyTooltipReplyMaxChars)
            {
                contentPreview = contentPreview.Substring(0, StrategyTooltipReplyMaxChars) + "...";
            }

            string tip = string.IsNullOrWhiteSpace(contentPreview)
                ? reason
                : $"{reason}\n{contentPreview}";

            if (!string.IsNullOrWhiteSpace(tip))
            {
                TooltipHandler.TipRegion(rect, tip);
            }
        }

        private void TrySendStrategySuggestion(PendingStrategySuggestion suggestion)
        {
            if (suggestion == null || string.IsNullOrWhiteSpace(suggestion.Content))
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
            SendPreparedMessage(suggestion.Content.Trim(), true);
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
                Log.Message("[RimChat] Strategy payload missing/invalid; requesting strict follow-up strategy payload.");
                TryRequestStrategySuggestionsFromLLM(currentSession, faction);
                return;
            }

            var mapped = suggestions
                .Select(MapStrategySuggestion)
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Content))
                .Take(StrategySuggestionRequiredCount)
                .ToList();
            mapped = EnsureStrategySuggestionCount(mapped);

            if (mapped.Count != StrategySuggestionRequiredCount)
            {
                ClearPendingStrategySuggestions(currentSession);
                Log.Message("[RimChat] Strategy payload invalid after parse, requesting follow-up strategy payload.");
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
            Log.Message("[RimChat] Sending strategy follow-up request.");

            string requestId = string.Empty;
            requestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                requestMessages,
                onSuccess: response =>
                {
                    if (!string.IsNullOrEmpty(strategySuggestionRequestId) &&
                        !string.Equals(strategySuggestionRequestId, requestId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    strategySuggestionRequestId = null;
                    strategySuggestionRequestPending = false;
                    if (!IsStrategyRequestContextValid(currentSession, currentFaction, snapshotMessageCount))
                    {
                        return;
                    }

                    var parsed = AIResponseParser.ParseResponse(response, currentFaction);
                    var mapped = parsed?.StrategySuggestions?
                        .Select(MapStrategySuggestion)
                        .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Content))
                        .Take(StrategySuggestionRequiredCount)
                        .ToList() ?? new List<PendingStrategySuggestion>();
                    bool usedLocalFallback = mapped.Count != StrategySuggestionRequiredCount;
                    mapped = EnsureStrategySuggestionCount(mapped);

                    if (mapped.Count == StrategySuggestionRequiredCount)
                    {
                        currentSession.pendingStrategySuggestions = mapped;
                        if (usedLocalFallback)
                        {
                            Log.Warning("[RimChat] Strategy follow-up payload invalid; primed local fallback strategy set.");
                        }
                        else
                        {
                            Log.Message("[RimChat] Strategy follow-up request succeeded, strategy buttons primed.");
                        }
                        return;
                    }

                    Log.Warning("[RimChat] Strategy follow-up produced no valid strategy payload.");
                },
                onError: error =>
                {
                    if (!string.IsNullOrEmpty(strategySuggestionRequestId) &&
                        !string.Equals(strategySuggestionRequestId, requestId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    strategySuggestionRequestId = null;
                    strategySuggestionRequestPending = false;
                    if (IsStrategyRequestContextValid(currentSession, currentFaction, snapshotMessageCount) &&
                        !currentSession.isConversationEndedByNpc &&
                        HasStrategyUsesRemaining(currentSession))
                    {
                        currentSession.pendingStrategySuggestions = EnsureStrategySuggestionCount(new List<PendingStrategySuggestion>());
                        Log.Warning($"[RimChat] Strategy follow-up request failed: {error}; local fallback strategies primed.");
                        return;
                    }

                    Log.Warning($"[RimChat] Strategy follow-up request failed: {error}");
                },
                onProgress: null
            );

            if (string.IsNullOrEmpty(requestId))
            {
                strategySuggestionRequestPending = false;
                return;
            }

            strategySuggestionRequestId = requestId;
        }

        private List<ChatMessageData> BuildStrategySuggestionRequestMessages(FactionDialogueSession currentSession, Faction currentFaction)
        {
            var messages = new List<ChatMessageData>();
            var sb = new StringBuilder();
            sb.AppendLine("You generate strategy_suggestions for a diplomacy UI.");
            sb.AppendLine("Return exactly one JSON object only.");
            sb.AppendLine("The first character must be '{' and the last character must be '}'.");
            sb.AppendLine("Do not output markdown fences, prose, notes, or any extra text.");
            sb.AppendLine("Required format:");
            sb.AppendLine("{\"strategy_suggestions\":[{\"strategy_name\":\"\",\"reason\":\"\",\"content\":\"\"},{...},{...}]}");
            sb.AppendLine("Rules:");
            sb.AppendLine("- Exactly 3 items.");
            sb.AppendLine("- Output keys must be exactly: strategy_suggestions, strategy_name, reason, content.");
            sb.AppendLine("- strategy_name <= 6 Chinese characters; must be actionable intent (not a full sentence).");
            sb.AppendLine("- reason must be fact-grounded: include at least one fact reference tag like [F1] or [F3].");
            sb.AppendLine("- reason must explain why this strategy fits those facts; do not use generic wording like '综合判断'.");
            sb.AppendLine("- reason should be compact for button display (<= 14 Chinese characters preferred).");
            sb.AppendLine("- reason example format: \"表现弱势(财富低)\", \"利用口才(社交12)\".");
            sb.AppendLine("- content must be a complete sendable line the player can auto-send directly.");
            sb.AppendLine("- Keep style aligned with current faction voice and player's language.");
            sb.AppendLine("- At least 2 items must explicitly be based on player attributes/context: social skill, traits, colony wealth tier, recent player tone.");
            sb.AppendLine("- Prefer strategy direction, not generic consolation wording.");
            sb.AppendLine("- Never output item fields like action, priority, risk_assessment, task, plan, macro_advice.");
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

            string factPack = BuildStrategyFactPackForPrompt(currentSession, currentFaction);
            if (!string.IsNullOrWhiteSpace(factPack))
            {
                messages.Add(new ChatMessageData { role = "system", content = factPack });
            }

            string scenarioDossier = BuildStrategyScenarioDossierPrompt(currentSession, currentFaction);
            if (!string.IsNullOrWhiteSpace(scenarioDossier))
            {
                messages.Add(new ChatMessageData { role = "system", content = scenarioDossier });
            }

            AppendRecentDialogueForStrategy(messages, currentSession);
            messages.Add(new ChatMessageData { role = "user", content = "Generate strategy_suggestions now and return JSON object only." });
            return messages;
        }

        private void AppendRecentDialogueForStrategy(List<ChatMessageData> messages, FactionDialogueSession currentSession)
        {
            if (messages == null || currentSession?.messages == null || currentSession.messages.Count == 0)
            {
                return;
            }

            List<ChatMessageData> compressedHistory =
                DialogueContextCompressionService.BuildFromDialogueMessages(currentSession.messages);
            for (int i = 0; i < compressedHistory.Count; i++)
            {
                ChatMessageData msg = compressedHistory[i];
                if (msg == null || string.IsNullOrWhiteSpace(msg.content))
                {
                    continue;
                }

                messages.Add(new ChatMessageData
                {
                    role = msg.role,
                    content = msg.content.Trim()
                });
            }
        }

        private PendingStrategySuggestion MapStrategySuggestion(StrategySuggestion source)
        {
            if (source == null)
            {
                return null;
            }

            string strategyName = (source.StrategyName ?? source.ShortLabel ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (string.IsNullOrWhiteSpace(strategyName) || IsCodeLikeStrategyName(strategyName))
            {
                string labelSeed = $"{source.Content} {source.Reason}".Trim();
                strategyName = BuildStrategyLabelFromReply(labelSeed);
            }
            if (IsGenericStrategyLabel(strategyName))
            {
                strategyName = BuildStrategyLabelFromReply(source.Content ?? source.Reason ?? string.Empty);
            }
            if (strategyName.Length > 6)
            {
                strategyName = strategyName.Substring(0, 6);
            }

            string reason = (source.Reason ?? source.TriggerBasis ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = source.Content ?? string.Empty;
            }
            if (reason.Length > 80)
            {
                reason = reason.Substring(0, 80);
            }

            return new PendingStrategySuggestion
            {
                StrategyName = strategyName,
                FactReason = reason,
                StrategyKeywords = source.StrategyKeywords?.Take(5).ToList() ?? new List<string>(),
                Content = source.Content ?? source.HiddenReply ?? string.Empty
            };
        }

        private List<PendingStrategySuggestion> EnsureStrategySuggestionCount(List<PendingStrategySuggestion> suggestions)
        {
            var result = (suggestions ?? new List<PendingStrategySuggestion>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Content))
                .Take(StrategySuggestionRequiredCount)
                .ToList();

            var basisPool = BuildAttributeBasisPool();
            if (basisPool.Count == 0)
            {
                basisPool.Add("RimChat_StrategyFallbackBasis".Translate());
            }

            while (result.Count < StrategySuggestionRequiredCount)
            {
                int index = result.Count;
                string reply = BuildDefaultStrategyReplyByIndex(index);
                string label = BuildDefaultStrategyNameByIndex(index);
                string basis = basisPool[index % basisPool.Count];
                result.Add(new PendingStrategySuggestion
                {
                    StrategyName = label,
                    FactReason = basis,
                    StrategyKeywords = new List<string> { label },
                    Content = reply
                });
            }

            ApplyAttributeBasisFallback(result);
            return result;
        }

        private string BuildDefaultStrategyReplyByIndex(int index)
        {
            return index switch
            {
                0 => "RimChat_StrategyFallbackReply1".Translate(),
                1 => "RimChat_StrategyFallbackReply2".Translate(),
                _ => "RimChat_StrategyFallbackReply3".Translate()
            };
        }

        private string BuildDefaultStrategyNameByIndex(int index)
        {
            return index switch
            {
                0 => "RimChat_StrategyLabelSocialLeverage".Translate(),
                1 => "RimChat_StrategyLabelResourceTransfer".Translate(),
                _ => "RimChat_StrategyLabelRiskBuffer".Translate()
            };
        }

        private string BuildStrategyLabelFromReply(string reply)
        {
            string cleaned = (reply ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (cleaned.Length == 0)
            {
                return "RimChat_StrategyFallbackLabel".Translate();
            }

            string lower = cleaned.ToLowerInvariant();
            if (ContainsAnyStrategyToken(lower, "social", "口才", "谈判", "交涉", "说服"))
            {
                return "RimChat_StrategyLabelSocialLeverage".Translate();
            }
            if (ContainsAnyStrategyToken(lower, "trade", "贸易", "资源", "组件", "物资", "代工"))
            {
                return "RimChat_StrategyLabelResourceTransfer".Translate();
            }
            if (ContainsAnyStrategyToken(lower, "weak", "示弱", "弱势"))
            {
                return "RimChat_StrategyLabelWeakPosture".Translate();
            }
            if (ContainsAnyStrategyToken(lower, "risk", "风险", "防御", "人口", "缓冲"))
            {
                return "RimChat_StrategyLabelRiskBuffer".Translate();
            }
            if (ContainsAnyStrategyToken(lower, "respect", "trust", "goodwill", "关系", "信任", "亲密", "尊重"))
            {
                return "RimChat_StrategyLabelRelationRepair".Translate();
            }
            if (ContainsAnyStrategyToken(lower, "emotion", "情绪", "共鸣", "安抚"))
            {
                return "RimChat_StrategyLabelEmotionalResonance".Translate();
            }

            string label = cleaned.TrimStart('-', '*', '#').Trim();
            if (label.Length > 6)
            {
                label = label.Substring(0, 6);
            }
            return label;
        }

        private bool IsCodeLikeStrategyName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.Trim();
            if (value.Contains("_"))
            {
                return true;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                bool asciiWord = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-';
                if (!asciiWord)
                {
                    return false;
                }
            }
            return true;
        }

        private bool IsGenericStrategyLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return true;
            }

            string normalized = label.Trim().ToLowerInvariant();
            return normalized == "策略建议" ||
                   normalized == "建议" ||
                   normalized == "strategy" ||
                   normalized == "proposal";
        }

        private int? ExtractIntNearKeyword(string text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null || keywords.Length == 0)
            {
                return null;
            }

            foreach (string keyword in keywords)
            {
                string pattern = $"{keyword}[^0-9]{{0,8}}(\\d{{1,3}})";
                var match = System.Text.RegularExpressions.Regex.Match(
                    text,
                    pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private string ExtractWealthTier(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string value = text.ToLowerInvariant();
            if (ContainsAnyStrategyToken(value, "very_low", "极低", "贫困"))
            {
                return "极低";
            }
            if (ContainsAnyStrategyToken(value, "low", "较低", "低"))
            {
                return "低";
            }
            if (ContainsAnyStrategyToken(value, "very_high", "极高"))
            {
                return "极高";
            }
            if (ContainsAnyStrategyToken(value, "high", "较高", "高"))
            {
                return "高";
            }
            if (ContainsAnyStrategyToken(value, "mid", "medium", "中"))
            {
                return "中";
            }

            var match = System.Text.RegularExpressions.Regex.Match(value, "wealth[^0-9]{0,8}(\\d{4,7})");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int wealth))
            {
                if (wealth >= 250000) return "极高";
                if (wealth >= 120000) return "高";
                if (wealth >= 50000) return "中";
                if (wealth >= 15000) return "低";
                return "极低";
            }

            return string.Empty;
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
                basisPool.Add("RimChat_StrategyFallbackBasis".Translate());
            }

            for (int i = 0; i < suggestions.Count; i++)
            {
                var item = suggestions[i];
                if (item == null)
                {
                    continue;
                }

                if (IsGenericBasis(item.FactReason))
                {
                    item.FactReason = basisPool[i % basisPool.Count];
                    continue;
                }

                if (!HasFactReference(item.FactReason))
                {
                    item.FactReason = $"{basisPool[i % basisPool.Count]} | {item.FactReason}";
                }
            }
        }

        private string BuildStrategyFactPackForPrompt(FactionDialogueSession currentSession, Faction currentFaction)
        {
            int social = GetNegotiatorSocialLevel();
            int useLimit = GetStrategyUseLimitBySocial(social);
            int remaining = Math.Max(0, useLimit - (currentSession?.strategyUsesConsumed ?? 0));
            string trait = negotiator?.story?.traits?.allTraits?.FirstOrDefault()?.Label ?? "none";
            float wealth = Find.Maps == null
                ? 0f
                : Find.Maps.Where(m => m.IsPlayerHome).Sum(m => m.wealthWatcher?.WealthTotal ?? 0f);
            string wealthTier = wealth >= 250000f ? "very_high"
                : wealth >= 120000f ? "high"
                : wealth >= 50000f ? "mid"
                : wealth >= 15000f ? "low"
                : "very_low";
            Map map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            string mapLabel = map?.Parent?.LabelCap ?? map?.Biome?.LabelCap ?? "Unknown";
            string season = map == null ? "Unknown" : GenLocalDate.Season(map).ToString();
            string weather = map?.weatherManager?.curWeather?.LabelCap ?? "Unknown";
            float outdoorTemp = map?.mapTemperature?.OutdoorTemp ?? 0f;
            int colonists = map?.mapPawns?.FreeColonistsSpawnedCount ?? 0;
            int drafted = map?.mapPawns?.FreeColonistsSpawned?.Count(p => p != null && p.Drafted) ?? 0;
            int hostilesOnMap = map?.mapPawns?.AllPawnsSpawned?.Count(p => p != null && p.HostileTo(Faction.OfPlayer)) ?? 0;
            string leaderName = currentFaction?.leader?.Name?.ToStringFull ?? "Unknown";
            string relationKind = currentFaction?.RelationKindWith(Faction.OfPlayer).ToString() ?? "Unknown";
            int settlementCount = Find.WorldObjects?.Settlements?.Count(s => s != null && s.Faction == currentFaction) ?? 0;
            string techLevel = currentFaction?.def?.techLevel.ToString() ?? "Unknown";
            string memoryDigest = BuildStrategyMemoryDigest(currentFaction);
            string worldEventDigest = BuildStrategyWorldEventDigest(currentFaction);

            int aggressiveCount = 0;
            if (currentSession?.messages != null)
            {
                aggressiveCount = currentSession.messages
                    .Where(m => m != null && m.isPlayer && !string.IsNullOrWhiteSpace(m.message))
                    .Reverse()
                    .Take(4)
                    .Count(m => ContainsAnyStrategyToken((m.message ?? string.Empty).ToLowerInvariant(),
                        "war", "attack", "threat", "kill", "侮辱", "威胁", "进攻", "开战", "袭击"));
            }

            var sb = new StringBuilder();
            sb.AppendLine("FACT PACK (use these IDs in reason):");
            sb.AppendLine($"[F1] Faction={currentFaction?.Name ?? "Unknown"}, Goodwill={currentFaction?.PlayerGoodwill ?? 0}");
            sb.AppendLine($"[F2] NegotiatorSocial={social}, Trait={trait}");
            sb.AppendLine($"[F3] StrategyUses remaining={remaining}/{useLimit}");
            sb.AppendLine($"[F4] ColonyWealth={wealth:F0}, Tier={wealthTier}");
            sb.AppendLine($"[F5] RecentPlayerAggressiveTurns(last4)={aggressiveCount}");
            sb.AppendLine($"[F6] Map={mapLabel}, Season={season}, Weather={weather}, TempC={outdoorTemp:F0}");
            sb.AppendLine($"[F7] ColonyStatus colonists={colonists}, drafted={drafted}, hostiles_on_map={hostilesOnMap}");
            sb.AppendLine($"[F8] FactionProfile leader={leaderName}, relation={relationKind}, settlements={settlementCount}, tech={techLevel}");
            sb.AppendLine($"[F9] MemoryHighlights={memoryDigest}");
            sb.AppendLine($"[F10] WorldIntel={worldEventDigest}");
            sb.AppendLine("Reason quality bar: reference concrete facts and explain causality.");
            return sb.ToString();
        }

        private string BuildStrategyScenarioDossierPrompt(FactionDialogueSession currentSession, Faction currentFaction)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== STRATEGY SCENARIO DOSSIER ===");
            AppendFactionIdentityContext(sb, currentFaction);
            AppendEnvironmentBackgroundContext(sb);
            AppendRecentSessionBackgroundContext(sb, currentSession);
            AppendMemoryBackgroundContext(sb, currentFaction);
            sb.AppendLine("Use this dossier to write concrete strategy reasons, not generic descriptions.");
            return sb.ToString();
        }

        private void AppendFactionIdentityContext(StringBuilder sb, Faction currentFaction)
        {
            if (sb == null || currentFaction == null)
            {
                return;
            }

            string leader = currentFaction.leader?.Name?.ToStringFull ?? "Unknown";
            string defName = currentFaction.def?.defName ?? "UnknownDef";
            string tech = currentFaction.def?.techLevel.ToString() ?? "Unknown";
            string relation = currentFaction.RelationKindWith(Faction.OfPlayer).ToString();
            int goodwill = currentFaction.PlayerGoodwill;
            int settlements = Find.WorldObjects?.Settlements?.Count(s => s != null && s.Faction == currentFaction) ?? 0;
            sb.AppendLine($"FactionIdentity: name={currentFaction.Name}, leader={leader}, def={defName}, tech={tech}");
            sb.AppendLine($"FactionRelation: goodwill={goodwill}, kind={relation}, settlements={settlements}");
        }

        private void AppendEnvironmentBackgroundContext(StringBuilder sb)
        {
            if (sb == null)
            {
                return;
            }

            Map map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            if (map == null)
            {
                sb.AppendLine("Environment: map unavailable");
                return;
            }

            string label = map.Parent?.LabelCap ?? map.Biome?.LabelCap ?? $"Map#{map.uniqueID}";
            string season = GenLocalDate.Season(map).ToString();
            int hour = GenLocalDate.HourOfDay(map);
            string weather = map.weatherManager?.curWeather?.LabelCap ?? "Unknown";
            float temp = map.mapTemperature?.OutdoorTemp ?? 0f;
            int colonists = map.mapPawns?.FreeColonistsSpawnedCount ?? 0;
            int hostiles = map.mapPawns?.AllPawnsSpawned?.Count(p => p != null && p.HostileTo(Faction.OfPlayer)) ?? 0;
            sb.AppendLine($"Environment: map={label}, season={season}, hour={hour}, weather={weather}, tempC={temp:F0}");
            sb.AppendLine($"MapTacticalState: colonists={colonists}, hostiles_on_map={hostiles}");
        }

        private void AppendRecentSessionBackgroundContext(StringBuilder sb, FactionDialogueSession currentSession)
        {
            if (sb == null)
            {
                return;
            }

            if (currentSession?.messages == null || currentSession.messages.Count == 0)
            {
                sb.AppendLine("SessionBackground: no previous messages");
                return;
            }

            int totalTurns = currentSession.messages.Count;
            int playerTurns = currentSession.messages.Count(m => m != null && m.isPlayer);
            int aiTurns = currentSession.messages.Count(m => m != null && !m.isPlayer);
            string lastPlayer = currentSession.messages.LastOrDefault(m => m != null && m.isPlayer)?.message ?? string.Empty;
            sb.AppendLine($"SessionBackground: total_turns={totalTurns}, player_turns={playerTurns}, ai_turns={aiTurns}");
            sb.AppendLine($"LastPlayerMessage: {TrimPrompt(lastPlayer, 120)}");
        }

        private void AppendMemoryBackgroundContext(StringBuilder sb, Faction currentFaction)
        {
            if (sb == null)
            {
                return;
            }

            sb.AppendLine($"MemoryBackground: {BuildStrategyMemoryDigest(currentFaction)}");
            sb.AppendLine($"WorldEventBackground: {BuildStrategyWorldEventDigest(currentFaction)}");
        }

        private string BuildStrategyMemoryDigest(Faction currentFaction)
        {
            if (currentFaction == null)
            {
                return "none";
            }

            FactionLeaderMemory memory = LeaderMemoryManager.Instance?.GetMemory(currentFaction);
            if (memory == null)
            {
                return "none";
            }

            List<string> parts = new List<string>();
            List<SignificantEventMemory> events = (memory.SignificantEvents ?? new List<SignificantEventMemory>())
                .Where(evt => evt != null)
                .OrderByDescending(evt => evt.OccurredTick)
                .Take(2)
                .ToList();
            for (int i = 0; i < events.Count; i++)
            {
                SignificantEventMemory evt = events[i];
                parts.Add($"{evt.EventType}:{TrimPrompt(evt.Description, 40)}");
            }

            CrossChannelSummaryRecord latestSummary = (memory.DiplomacySessionSummaries ?? new List<CrossChannelSummaryRecord>())
                .Where(item => item != null)
                .OrderByDescending(item => item.GameTick)
                .FirstOrDefault();
            if (latestSummary != null && !string.IsNullOrWhiteSpace(latestSummary.SummaryText))
            {
                parts.Add($"summary:{TrimPrompt(latestSummary.SummaryText, 60)}");
            }

            return parts.Count == 0 ? "none" : string.Join(" | ", parts);
        }

        private string BuildStrategyWorldEventDigest(Faction currentFaction)
        {
            WorldEventLedgerComponent ledger = WorldEventLedgerComponent.Instance;
            if (ledger == null || currentFaction == null)
            {
                return "none";
            }

            List<string> parts = new List<string>();
            List<WorldEventRecord> events = ledger.GetRecentWorldEvents(currentFaction, 2, true, true)
                .Where(record => record != null)
                .Take(2)
                .ToList();
            for (int i = 0; i < events.Count; i++)
            {
                parts.Add(TrimPrompt(events[i].Summary, 48));
            }

            RaidBattleReportRecord raid = ledger.GetRecentRaidBattleReports(currentFaction, 3, true)
                .FirstOrDefault(record => record != null);
            if (raid != null && !string.IsNullOrWhiteSpace(raid.Summary))
            {
                parts.Add($"raid:{TrimPrompt(raid.Summary, 48)}");
            }

            return parts.Count == 0 ? "none" : string.Join(" | ", parts);
        }

        private string TrimPrompt(string text, int maxChars)
        {
            string value = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (value.Length <= maxChars)
            {
                return value;
            }

            if (maxChars <= 3)
            {
                return value.Substring(0, Math.Max(0, maxChars));
            }

            return value.Substring(0, maxChars - 3) + "...";
        }

        private List<string> BuildAttributeBasisPool()
        {
            var list = new List<string>();
            int social = GetNegotiatorSocialLevel();
            if (social >= 5)
            {
                list.Add("RimChat_StrategyBasisSocial".Translate());
            }

            if (negotiator?.story?.traits?.allTraits != null && negotiator.story.traits.allTraits.Count > 0)
            {
                list.Add("RimChat_StrategyBasisTrait".Translate(negotiator.story.traits.allTraits[0].Label));
            }

            float wealth = 0f;
            if (Find.Maps != null)
            {
                wealth = Find.Maps.Where(m => m.IsPlayerHome).Sum(m => m.wealthWatcher?.WealthTotal ?? 0f);
            }
            list.Add(wealth >= 120000f
                ? "RimChat_StrategyBasisWealthHigh".Translate()
                : "RimChat_StrategyBasisWealth".Translate());
            list.Add("RimChat_StrategyBasisRecentTone".Translate());
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

        private bool HasFactReference(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return false;
            }

            string normalized = reason.ToLowerInvariant();
            for (int i = 1; i <= 11; i++)
            {
                if (normalized.Contains($"[f{i}]"))
                {
                    return true;
                }
            }

            return false;
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
            sb.AppendLine("=== PLAYER NEGOTIATOR CONTEXT (NOT YOUR IDENTITY) ===");
            sb.AppendLine("Identity guard: You are the target faction representative, not the player negotiator.");
            sb.AppendLine("Never claim you are the negotiator, colony assistant, or any player-colony pawn.");
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
                sb.AppendLine("PlayerNegotiator (not you): unavailable");
                return;
            }

            int social = negotiator.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            string traits = negotiator.story?.traits?.allTraits == null
                ? "none"
                : string.Join(", ", negotiator.story.traits.allTraits.Select(t => t.Label).Take(6));

            sb.AppendLine($"PlayerNegotiator (not you): {negotiator.LabelShort} | Social: {social}");
            sb.AppendLine($"PlayerNegotiator Traits: {traits}");
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

        private bool IsStrategyRequestContextValid(FactionDialogueSession currentSession, Faction currentFaction, int snapshotMessageCount)
        {
            if (currentSession == null || currentFaction == null || currentFaction.defeated)
            {
                return false;
            }

            if (currentSession.isWaitingForResponse)
            {
                return false;
            }

            if ((currentSession.messages?.Count ?? 0) != snapshotMessageCount)
            {
                return false;
            }

            FactionDialogueSession liveSession = GameComponent_DiplomacyManager.Instance?.GetSession(currentFaction);
            return ReferenceEquals(liveSession, currentSession);
        }

        private void CancelStrategySuggestionRequest()
        {
            if (string.IsNullOrEmpty(strategySuggestionRequestId))
            {
                return;
            }

            AIChatServiceAsync.Instance.CancelRequest(strategySuggestionRequestId);
            strategySuggestionRequestId = null;
            strategySuggestionRequestPending = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimChat.Dialogue;
using RimWorld;
using Verse;

namespace RimChat.AI
{
    /// <summary>/// LLMresponseparser
 /// 解析AI的JSON格式response, 提取API调用和dialoguecontents
 ///</summary>
    public class AIResponseParser
    {
        /// <summary>/// 解析AIresponse
 ///</summary>
        /// <param name="response">AI返回的原始response</param>
        /// <param name="faction">当前dialogue的faction</param>
        /// <returns>解析result</returns>
        public static ParsedResponse ParseResponse(string response, Faction faction)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return new ParsedResponse
                {
                    Success = false,
                    ErrorMessage = "Empty response",
                    DialogueText = "I have nothing to say at the moment.",
                    Actions = new List<AIAction>(),
                    StrategySuggestions = new List<StrategySuggestion>()
                };
            }

            try
            {
                string narrativeFallback = ExtractNarrativeText(response);

                // 尝试解析JSON格式
                var jsonResponse = ParseJsonResponse(response);
                if (jsonResponse != null)
                {
                    ParsedResponse parsed = ProcessJsonResponse(jsonResponse, faction, narrativeFallback);
                    if (string.IsNullOrWhiteSpace(parsed.DialogueText) &&
                        parsed.Actions.Count == 0 &&
                        parsed.StrategySuggestions.Count == 0)
                    {
                        parsed.DialogueText = ImmersionOutputGuard.BuildLocalFallbackDialogue(DialogueUsageChannel.Diplomacy);
                    }

                    return parsed;
                }

                // 如果不是JSON, 作为纯textprocessing
                return new ParsedResponse
                {
                    Success = true,
                    DialogueText = NormalizeDialogueText(response),
                    Actions = new List<AIAction>(),
                    StrategySuggestions = new List<StrategySuggestion>()
                };
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to parse AI response: {ex.Message}");
                return new ParsedResponse
                {
                    Success = true,
                    DialogueText = NormalizeDialogueText(response),
                    Actions = new List<AIAction>(),
                    StrategySuggestions = new List<StrategySuggestion>()
                };
            }
        }

        public static ParsedResponse ParseResponse(DialogueResponseEnvelope envelope, Faction faction)
        {
            if (envelope == null)
            {
                return ParseResponse(string.Empty, faction);
            }

            try
            {
                string narrativeFallback = NormalizeDialogueText(envelope.VisibleDialogue);
                var result = new ParsedResponse
                {
                    Success = true,
                    DialogueText = narrativeFallback,
                    Actions = new List<AIAction>(),
                    StrategySuggestions = new List<StrategySuggestion>()
                };

                List<AIAction> parsedActions = CollectActionsFromJson(envelope.ActionsJson);
                if (parsedActions.Count > 0)
                {
                    result.Actions.AddRange(parsedActions);
                }

                if (string.IsNullOrWhiteSpace(result.DialogueText) &&
                    result.Actions.Count == 0)
                {
                    result.DialogueText = ImmersionOutputGuard.BuildLocalFallbackDialogue(DialogueUsageChannel.Diplomacy);
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to parse dialogue envelope: {ex.Message}");
                return ParseResponse(envelope.ToLegacyText(), faction);
            }
        }

        /// <summary>/// 尝试从response中提取JSON
 ///</summary>
        private static JsonResponse ParseJsonResponse(string response)
        {
            List<JsonPayloadSegment> payloadSegments = ExtractJsonPayloadSegments(response, includeGenericJson: false);
            if (payloadSegments.Count == 0)
            {
                return null;
            }

            JsonPayloadSegment actionSegment = payloadSegments.Find(segment => segment.HasActions);
            JsonPayloadSegment strategySegment = payloadSegments.Find(segment => segment.HasStrategySuggestions);

            var result = new JsonResponse();
            result.RawJson = actionSegment?.Json ?? strategySegment?.Json ?? payloadSegments[0].Json;

            string strategySuggestionsSource = strategySegment?.Json ?? result.RawJson;
            string strategySuggestionsJson = ExtractJsonArray(strategySuggestionsSource, "strategy_suggestions");
            if (!string.IsNullOrEmpty(strategySuggestionsJson))
            {
                result.StrategySuggestions = ParseStrategySuggestions(strategySuggestionsJson);
            }
            else
            {
                result.StrategySuggestions = new List<StrategySuggestion>();
            }

            return result;
        }

        /// <summary>/// 从JSON中提取浮点数values
 ///</summary>
        private static float ExtractFloatValue(string json, string key)
        {
            string pattern = $"\"{key}\":";
            int index = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                // 尝试不带引号的key
                pattern = $"{key}:";
                index = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return 0f;
            }

            index += pattern.Length;
            // 跳过空白字符
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;

            if (index >= json.Length) return 0f;

            // 提取数values
            var valueSb = new StringBuilder();
            while (index < json.Length && (char.IsDigit(json[index]) || json[index] == '-' || json[index] == '.'))
            {
                valueSb.Append(json[index]);
                index++;
            }

            if (float.TryParse(valueSb.ToString(), out float value))
            {
                return value;
            }

            return 0f;
        }

        /// <summary>/// processingJSON格式的response
 ///</summary>
        private static ParsedResponse ProcessJsonResponse(JsonResponse json, Faction faction, string narrativeFallback)
        {
            var result = new ParsedResponse
            {
                Success = true,
                DialogueText = NormalizeDialogueText(narrativeFallback),
                Actions = new List<AIAction>(),
                StrategySuggestions = json.StrategySuggestions ?? new List<StrategySuggestion>()
            };

            var parsedActions = CollectActions(json);
            if (parsedActions.Count == 0)
            {
                return result;
            }

            result.Actions.AddRange(parsedActions);

            return result;
        }

        private static string ExtractNarrativeText(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return string.Empty;
            }

            List<JsonPayloadSegment> segments = ExtractJsonPayloadSegments(response, includeGenericJson: true);
            string candidate = segments.Count > 0
                ? RemoveJsonSegmentsFromText(response, segments)
                : response;

            int jsonFenceIndex = candidate.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (jsonFenceIndex > 0)
            {
                candidate = candidate.Substring(0, jsonFenceIndex);
            }

            int firstBrace = candidate.IndexOf('{');
            if (firstBrace > 0)
            {
                candidate = candidate.Substring(0, firstBrace);
            }

            return candidate
                .Replace("```json", string.Empty)
                .Replace("```", string.Empty)
                .Trim();
        }

        private static List<JsonPayloadSegment> ExtractJsonPayloadSegments(string response, bool includeGenericJson)
        {
            var segments = new List<JsonPayloadSegment>();
            if (string.IsNullOrWhiteSpace(response))
            {
                return segments;
            }

            foreach (JsonPayloadSegment segment in ExtractTopLevelJsonObjectSegments(response))
            {
                bool hasActions = segment.Json.IndexOf("\"actions\"", StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasStrategySuggestions = segment.Json.IndexOf("\"strategy_suggestions\"", StringComparison.OrdinalIgnoreCase) >= 0;
                bool shouldInclude = hasActions || hasStrategySuggestions;
                if (!shouldInclude && includeGenericJson)
                {
                    shouldInclude = LooksLikeStructuredJsonObject(segment.Json);
                }

                if (!shouldInclude)
                {
                    continue;
                }

                segments.Add(new JsonPayloadSegment
                {
                    Start = segment.Start,
                    End = segment.End,
                    Json = segment.Json,
                    HasActions = hasActions,
                    HasStrategySuggestions = hasStrategySuggestions
                });
            }

            return segments;
        }

        private static List<JsonPayloadSegment> ExtractTopLevelJsonObjectSegments(string response)
        {
            var segments = new List<JsonPayloadSegment>();
            if (string.IsNullOrWhiteSpace(response))
            {
                return segments;
            }

            int start = -1;
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < response.Length; i++)
            {
                char current = response[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == '{')
                {
                    if (depth == 0)
                    {
                        start = i;
                    }
                    depth++;
                    continue;
                }

                if (current != '}')
                {
                    continue;
                }

                if (depth <= 0)
                {
                    continue;
                }

                depth--;
                if (depth == 0 && start >= 0)
                {
                    segments.Add(new JsonPayloadSegment
                    {
                        Start = start,
                        End = i,
                        Json = response.Substring(start, i - start + 1)
                    });
                    start = -1;
                }
            }

            return segments;
        }

        private static string RemoveJsonSegmentsFromText(string source, List<JsonPayloadSegment> segments)
        {
            if (string.IsNullOrWhiteSpace(source) || segments == null || segments.Count == 0)
            {
                return source ?? string.Empty;
            }

            var sb = new StringBuilder();
            int cursor = 0;
            foreach (JsonPayloadSegment segment in segments.OrderBy(item => item.Start))
            {
                if (segment.Start < cursor)
                {
                    continue;
                }

                if (segment.Start > cursor)
                {
                    sb.Append(source.Substring(cursor, segment.Start - cursor));
                }
                cursor = Math.Min(source.Length, segment.End + 1);
            }

            if (cursor < source.Length)
            {
                sb.Append(source.Substring(cursor));
            }

            return sb.ToString();
        }

        private static bool LooksLikeStructuredJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            string trimmed = json.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) ||
                !trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return false;
            }

            return trimmed.IndexOf("\":", StringComparison.Ordinal) >= 0;
        }

        private static string NormalizeDialogueText(string text)
        {
            string normalized = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            normalized = ModelOutputSanitizer.StripReasoningTags(normalized).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            normalized = StripVisibleStrategySection(normalized);
            normalized = normalized.Replace("```json", string.Empty)
                                   .Replace("```", string.Empty)
                                   .Trim();

            string lower = normalized.ToLowerInvariant();
            if (lower == "i understand." ||
                lower == "i understand" ||
                lower == "your in-character response here" ||
                lower == "i have nothing to say at the moment.")
            {
                return string.Empty;
            }

            ImmersionGuardResult guardResult = ImmersionOutputGuard.ValidateVisibleDialogue(normalized);
            if (!guardResult.IsValid)
            {
                Log.Warning($"[RimChat] Immersion guard blocked diplomacy text: reason={ImmersionOutputGuard.BuildViolationTag(guardResult.ViolationReason)}, snippet={guardResult.ViolationSnippet}");
                return string.Empty;
            }

            return guardResult.VisibleDialogue;
        }

        private static string StripVisibleStrategySection(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Replace("\r\n", "\n");
            string lower = normalized.ToLowerInvariant();
            string[] markers =
            {
                "\n**策略建议",
                "\n策略建议：",
                "\n策略建议:",
                "\n***\n\n**策略建议",
                "\n**strategy suggestions",
                "\nstrategy suggestions:",
                "\nstrategy suggestion:"
            };

            int cutIndex = -1;
            foreach (string marker in markers)
            {
                int idx = lower.IndexOf(marker, StringComparison.Ordinal);
                if (idx >= 0 && (cutIndex < 0 || idx < cutIndex))
                {
                    cutIndex = idx;
                }
            }

            if (cutIndex < 0)
            {
                string start = lower.TrimStart();
                if (start.StartsWith("**策略建议", StringComparison.Ordinal) ||
                    start.StartsWith("策略建议：", StringComparison.Ordinal) ||
                    start.StartsWith("策略建议:", StringComparison.Ordinal) ||
                    start.StartsWith("**strategy suggestions", StringComparison.Ordinal) ||
                    start.StartsWith("strategy suggestions:", StringComparison.Ordinal))
                {
                    return string.Empty;
                }

                return normalized.Trim();
            }

            return normalized.Substring(0, cutIndex).Trim();
        }

        /// <summary>/// 检查action类型whether有效
 ///</summary>
        private static bool IsValidAction(string action)
        {
            action = NormalizeActionName(action);
            string[] validActions = new string[]
            {
                "adjust_goodwill",
                "send_gift",
                "request_aid",
                "declare_war",
                "make_peace",
                "request_caravan",
                "request_visitor",
                "request_raid",
                "request_raid_call_everyone",
                "request_raid_waves",
                "request_item_airdrop",
                "request_info",
                "pay_prisoner_ransom",
                "trigger_incident",
                "create_quest",
                "reject_request",
                "publish_public_post",
                "exit_dialogue",
                "go_offline",
                "set_dnd"
            };

            return Array.Exists(validActions, a => a == action);
        }

        private static string NormalizeActionName(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return string.Empty;
            }

            string normalized = action.Trim().Trim('"').ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            switch (normalized)
            {
                case "none":
                    return "none";
                case "exit":
                case "exitdialogue":
                case "enddialogue":
                case "end_dialogue":
                    return "exit_dialogue";
                case "gooffline":
                case "offline":
                    return "go_offline";
                case "setdnd":
                case "dnd":
                case "do_not_disturb":
                case "donotdisturb":
                    return "set_dnd";
                case "publishpublicpost":
                case "publicpost":
                case "publish_post":
                case "social_post":
                    return "publish_public_post";
                case "requestinfo":
                case "ask_info":
                case "requestinformation":
                    return "request_info";
                case "requestvisitor":
                case "visitor_request":
                case "request_visit":
                case "visitorgroup":
                    return "request_visitor";
                case "requestraidcalleveryone":
                case "raid_call_everyone":
                case "call_everyone":
                case "call_all_factions":
                case "everyone_attack":
                case "joint_raid":
                case "all_in":
                case "联合袭击":
                case "一起上":
                case "都叫来":
                case "全都叫来":
                    return "request_raid_call_everyone";
                case "requestraidwaves":
                case "raid_waves":
                case "multi_raid":
                    return "request_raid_waves";
                default:
                    return normalized;
            }
        }

        private static List<AIAction> CollectActions(JsonResponse json)
        {
            return CollectActionsFromJson(json?.RawJson);
        }

        private static List<AIAction> CollectActionsFromJson(string json)
        {
            var actions = new List<AIAction>();
            var keptRansomTargetIds = new List<int>();
            var droppedDuplicateRansomTargetIds = new List<int>();
            int keptRansomWithoutTargetCount = 0;

            string trimmedJson = (json ?? string.Empty).Trim();
            string actionsArray = trimmedJson.StartsWith("[", StringComparison.Ordinal)
                ? trimmedJson
                : ExtractJsonArray(trimmedJson, "actions");
            if (string.IsNullOrEmpty(actionsArray))
            {
                return actions;
            }

            foreach (string actionObj in SplitJsonObjects(actionsArray))
            {
                string actionType = ExtractJsonString(actionObj, "action");
                if (string.IsNullOrEmpty(actionType))
                {
                    continue;
                }
                string reason = ExtractJsonString(actionObj, "reason");
                string parametersJson = ExtractJsonObject(actionObj, "parameters");
                var parameters = string.IsNullOrEmpty(parametersJson)
                    ? new Dictionary<string, object>()
                    : ParseParameters(parametersJson);

                string normalizedAction = NormalizeActionName(actionType);
                if (string.Equals(normalizedAction, AIActionNames.CreateQuest, StringComparison.Ordinal))
                {
                    string questDefName = ExtractJsonString(actionObj, "questDefName");
                    if (string.IsNullOrEmpty(questDefName))
                    {
                        questDefName = ExtractJsonString(actionObj, "defName");
                    }
                    if (!string.IsNullOrEmpty(questDefName) && !parameters.ContainsKey("questDefName"))
                    {
                        parameters["questDefName"] = questDefName;
                    }
                    if (!parameters.ContainsKey("questDefName"))
                    {
                        Log.Warning($"[RimChat] create_quest action missing questDefName. Raw actionObj: {actionObj}");
                    }
                }

                AddActionIfValid(
                    actions,
                    actionType,
                    parameters,
                    reason,
                    keptRansomTargetIds,
                    droppedDuplicateRansomTargetIds,
                    ref keptRansomWithoutTargetCount);
            }

            LogRansomParseSummary(keptRansomTargetIds, droppedDuplicateRansomTargetIds, keptRansomWithoutTargetCount);
            return actions;
        }

        private static List<StrategySuggestion> ParseStrategySuggestions(string arrayJson)
        {
            var suggestions = new List<StrategySuggestion>();
            foreach (string suggestionObj in SplitJsonObjects(arrayJson))
            {
                var parsed = ParseStrategySuggestionItem(suggestionObj);
                if (parsed != null)
                {
                    suggestions.Add(parsed);
                }
            }

            if (suggestions.Count != 3)
            {
                return new List<StrategySuggestion>();
            }

            return suggestions;
        }

        private static StrategySuggestion ParseStrategySuggestionItem(string suggestionObj)
        {
            if (string.IsNullOrWhiteSpace(suggestionObj))
            {
                return null;
            }

            string content = ExtractJsonString(suggestionObj, "content");
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ExtractJsonString(suggestionObj, "hidden_reply");
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ExtractJsonString(suggestionObj, "reply");
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ExtractJsonString(suggestionObj, "full_reply");
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ExtractJsonString(suggestionObj, "expected_outcome");
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ExtractJsonString(suggestionObj, "description");
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ExtractJsonString(suggestionObj, "suggestion");
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ExtractJsonString(suggestionObj, "recommendation");
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ExtractJsonString(suggestionObj, "proposal");
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ExtractJsonString(suggestionObj, "reasoning");
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ExtractJsonString(suggestionObj, "macro_advice");
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ExtractJsonString(suggestionObj, "reason");
            }
            content = (content ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            string strategyName = ExtractJsonString(suggestionObj, "strategy_name");
            if (string.IsNullOrWhiteSpace(strategyName))
            {
                strategyName = ExtractJsonString(suggestionObj, "name");
            }
            if (string.IsNullOrWhiteSpace(strategyName))
            {
                strategyName = ExtractJsonString(suggestionObj, "title");
            }
            if (string.IsNullOrWhiteSpace(strategyName))
            {
                strategyName = ExtractJsonString(suggestionObj, "short_label");
            }
            if (string.IsNullOrWhiteSpace(strategyName))
            {
                strategyName = ExtractJsonString(suggestionObj, "label");
            }
            if (string.IsNullOrWhiteSpace(strategyName))
            {
                strategyName = ExtractJsonString(suggestionObj, "task");
            }
            if (string.IsNullOrWhiteSpace(strategyName))
            {
                strategyName = ExtractJsonString(suggestionObj, "plan");
            }
            if (string.IsNullOrWhiteSpace(strategyName))
            {
                strategyName = ExtractJsonString(suggestionObj, "macro_advice");
            }
            if (string.IsNullOrWhiteSpace(strategyName))
            {
                strategyName = ExtractJsonString(suggestionObj, "action");
            }

            string factReason = ExtractJsonString(suggestionObj, "reason");
            if (string.IsNullOrWhiteSpace(factReason))
            {
                factReason = ExtractJsonString(suggestionObj, "fact_reason");
            }
            if (string.IsNullOrWhiteSpace(factReason))
            {
                factReason = ExtractJsonString(suggestionObj, "trigger_basis");
            }
            if (string.IsNullOrWhiteSpace(factReason))
            {
                factReason = ExtractJsonString(suggestionObj, "basis");
            }
            if (string.IsNullOrWhiteSpace(factReason))
            {
                factReason = ExtractJsonString(suggestionObj, "trigger");
            }
            if (string.IsNullOrWhiteSpace(factReason))
            {
                factReason = ExtractJsonString(suggestionObj, "risk_level");
            }
            if (string.IsNullOrWhiteSpace(factReason))
            {
                factReason = ExtractJsonString(suggestionObj, "reasoning");
            }
            if (string.IsNullOrWhiteSpace(factReason))
            {
                factReason = ExtractJsonString(suggestionObj, "rationale");
            }
            if (string.IsNullOrWhiteSpace(factReason))
            {
                factReason = ExtractJsonString(suggestionObj, "risk_assessment");
            }
            if (string.IsNullOrWhiteSpace(factReason))
            {
                factReason = ExtractJsonString(suggestionObj, "analysis");
            }

            string keywordsJson = ExtractJsonArray(suggestionObj, "strategy_keywords");
            if (string.IsNullOrWhiteSpace(keywordsJson))
            {
                keywordsJson = ExtractJsonArray(suggestionObj, "keywords");
            }

            var keywords = ParseStringArray(keywordsJson);
            strategyName = NormalizeStrategyName(strategyName, keywords, content);
            factReason = NormalizeStrategyReason(factReason);

            return new StrategySuggestion
            {
                StrategyName = strategyName,
                Reason = factReason,
                StrategyKeywords = keywords,
                Content = content
            };
        }

        private static string NormalizeStrategyName(string label, List<string> keywords, string content)
        {
            string result = label ?? string.Empty;
            if (string.IsNullOrWhiteSpace(result) && keywords != null && keywords.Count > 0)
            {
                result = keywords[0];
            }
            if (string.IsNullOrWhiteSpace(result))
            {
                result = content;
            }
            result = (result ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (result.Length == 0)
            {
                result = "策略建议";
            }
            if (result.Length > 14)
            {
                result = result.Substring(0, 14);
            }
            return result;
        }

        private static string NormalizeStrategyReason(string reason)
        {
            string result = (reason ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (string.IsNullOrWhiteSpace(result))
            {
                return "综合判断";
            }
            if (result.Length > 80)
            {
                return result.Substring(0, 80);
            }
            return result;
        }

        private static List<string> ParseStringArray(string arrayJson)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(arrayJson))
            {
                return result;
            }

            string content = arrayJson.Trim();
            if (content.StartsWith("[")) content = content.Substring(1);
            if (content.EndsWith("]")) content = content.Substring(0, content.Length - 1);

            bool inString = false;
            var sb = new StringBuilder();
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (c == '"' && (i == 0 || content[i - 1] != '\\'))
                {
                    if (inString)
                    {
                        string item = UnescapeJsonString(sb.ToString()).Trim();
                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            result.Add(item);
                        }
                        sb.Clear();
                    }
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    sb.Append(c);
                }
            }

            return result;
        }

        private static string ExtractTopLevelJsonString(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            int valueStart = FindTopLevelKeyValueStart(json, key);
            if (valueStart < 0 || valueStart >= json.Length)
            {
                return string.Empty;
            }

            if (json[valueStart] == '"')
            {
                int cursor = valueStart + 1;
                var sb = new StringBuilder();
                while (cursor < json.Length)
                {
                    char current = json[cursor];
                    if (current == '"' && json[cursor - 1] != '\\')
                    {
                        break;
                    }

                    sb.Append(current);
                    cursor++;
                }
                return UnescapeJsonString(sb.ToString());
            }

            return string.Empty;
        }

        private static int FindTopLevelKeyValueStart(string json, string key)
        {
            string needle = $"\"{key}\"";
            bool inString = false;
            int depth = 0;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                {
                    if (!inString &&
                        depth == 1 &&
                        i + needle.Length <= json.Length &&
                        string.Compare(json, i, needle, 0, needle.Length, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        int cursor = i + needle.Length;
                        while (cursor < json.Length && char.IsWhiteSpace(json[cursor]))
                        {
                            cursor++;
                        }
                        if (cursor >= json.Length || json[cursor] != ':')
                        {
                            continue;
                        }
                        cursor++;
                        while (cursor < json.Length && char.IsWhiteSpace(json[cursor]))
                        {
                            cursor++;
                        }
                        return cursor < json.Length ? cursor : -1;
                    }

                    inString = !inString;
                }
                if (inString)
                {
                    continue;
                }
                if (c == '{')
                {
                    depth++;
                    continue;
                }
                if (c == '}')
                {
                    depth = Math.Max(0, depth - 1);
                    continue;
                }
            }

            return -1;
        }

        private static void AddActionIfValid(
            List<AIAction> actions,
            string actionType,
            Dictionary<string, object> parameters,
            string reason,
            List<int> keptRansomTargetIds,
            List<int> droppedDuplicateRansomTargetIds,
            ref int keptRansomWithoutTargetCount)
        {
            string normalizedAction = NormalizeActionName(actionType);
            if (string.IsNullOrEmpty(normalizedAction) || normalizedAction == "none")
            {
                return;
            }

            if (!IsValidAction(normalizedAction))
            {
                Log.Warning($"[RimChat] Unknown AI action: {normalizedAction}");
                return;
            }

            if (parameters == null)
            {
                parameters = new Dictionary<string, object>();
            }

            if (string.Equals(normalizedAction, AIActionNames.RequestItemAirdrop, StringComparison.Ordinal) &&
                !HasValidAirdropBarterParameters(parameters))
            {
                bool needPresent = HasNonEmptyText(parameters, "need", requireString: true);
                string paymentItemsType = DescribeAirdropParameterType(parameters, "payment_items");
                string paymentItemsCount = DescribeAirdropPaymentItemsCount(parameters);
                string paymentItem0Type = DescribeAirdropPaymentItem0Type(parameters);
                string paymentItem0Keys = DescribeAirdropPaymentItem0Keys(parameters);
                Log.Warning(
                    $"[RimChat] Dropped request_item_airdrop action because required parameters are missing or invalid (need, payment_items). " +
                    $"need_present={needPresent}, " +
                    $"payment_items_type={paymentItemsType}, " +
                    $"payment_items_count={paymentItemsCount}, " +
                    $"payment_item0_type={paymentItem0Type}, " +
                    $"payment_item0_keys={paymentItem0Keys}");
                return;
            }

            if (string.Equals(normalizedAction, AIActionNames.PayPrisonerRansom, StringComparison.Ordinal))
            {
                if (!HasValidPrisonerRansomParameters(
                        parameters,
                        out string invalidParameter,
                        out string paymentModeRaw,
                        out string paymentModeNormalized,
                        out bool paymentModePassthrough))
                {
                    Log.Warning(
                        $"[RimChat] pay_prisoner_ransom parameters unresolved: missing_or_invalid={invalidParameter ?? "unknown"}, " +
                        $"payment_mode_raw={FormatRansomLogValue(paymentModeRaw)}, " +
                        $"payment_mode_normalized={FormatRansomLogValue(paymentModeNormalized)}, " +
                        $"passthrough_to_execution={paymentModePassthrough}. " +
                        "Dropping action in parser for fail-fast validation.");
                    return;
                }

                parameters.Remove("__ransom_missing_parameter");
                if (TryGetRansomTargetPawnLoadId(parameters, out int targetPawnLoadId))
                {
                    if (IsDuplicateRansomActionForTarget(actions, targetPawnLoadId))
                    {
                        droppedDuplicateRansomTargetIds?.Add(targetPawnLoadId);
                        Log.Message($"[RimChat] pay_prisoner_ransom parser dropped duplicate target action. target_pawn_load_id={targetPawnLoadId}");
                        return;
                    }

                    keptRansomTargetIds?.Add(targetPawnLoadId);
                }
                else
                {
                    keptRansomWithoutTargetCount++;
                }

                Log.Message(
                    $"[RimChat] pay_prisoner_ransom parser accepted: " +
                    $"payment_mode_raw={FormatRansomLogValue(paymentModeRaw)}, " +
                    $"payment_mode_normalized={FormatRansomLogValue(paymentModeNormalized)}, " +
                    $"passthrough_to_execution={paymentModePassthrough}.");
                if (string.IsNullOrWhiteSpace(reason) &&
                    parameters.TryGetValue("reason", out object reasonObj) &&
                    reasonObj != null)
                {
                    reason = reasonObj.ToString();
                }

                actions.Add(new AIAction
                {
                    ActionType = normalizedAction,
                    Parameters = parameters,
                    Reason = reason
                });
                return;
            }

            if (string.IsNullOrWhiteSpace(reason) &&
                parameters.TryGetValue("reason", out object reasonObjNonRansom) &&
                reasonObjNonRansom != null)
            {
                reason = reasonObjNonRansom.ToString();
            }

            if (actions.Exists(a =>
                string.Equals(a.ActionType, normalizedAction, StringComparison.Ordinal) &&
                string.Equals(a.Reason ?? string.Empty, reason ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            actions.Add(new AIAction
            {
                ActionType = normalizedAction,
                Parameters = parameters,
                Reason = reason
            });
        }

        private static bool IsDuplicateRansomActionForTarget(List<AIAction> actions, int targetPawnLoadId)
        {
            if (actions == null || targetPawnLoadId <= 0)
            {
                return false;
            }

            return actions.Any(action =>
                action != null &&
                string.Equals(action.ActionType, AIActionNames.PayPrisonerRansom, StringComparison.Ordinal) &&
                TryGetRansomTargetPawnLoadId(action.Parameters, out int existingTargetPawnLoadId) &&
                existingTargetPawnLoadId == targetPawnLoadId);
        }

        private static bool TryGetRansomTargetPawnLoadId(Dictionary<string, object> parameters, out int targetPawnLoadId)
        {
            targetPawnLoadId = 0;
            return TryReadLoosePositiveIntegerParameter(parameters, "target_pawn_load_id", out targetPawnLoadId);
        }

        private static void LogRansomParseSummary(
            List<int> keptRansomTargetIds,
            List<int> droppedDuplicateRansomTargetIds,
            int keptRansomWithoutTargetCount)
        {
            int keptTargetCount = keptRansomTargetIds?.Count ?? 0;
            int droppedTargetCount = droppedDuplicateRansomTargetIds?.Count ?? 0;
            if (keptTargetCount <= 0 && droppedTargetCount <= 0 && keptRansomWithoutTargetCount <= 0)
            {
                return;
            }

            string keptTargets = keptTargetCount > 0
                ? string.Join(",", keptRansomTargetIds.Distinct().OrderBy(id => id))
                : "none";
            string droppedTargets = droppedTargetCount > 0
                ? string.Join(",", droppedDuplicateRansomTargetIds.Distinct().OrderBy(id => id))
                : "none";
            Log.Message(
                $"[RimChat] pay_prisoner_ransom parser summary: kept_targets={keptTargets}, " +
                $"dropped_duplicate_targets={droppedTargets}, kept_without_target={Math.Max(0, keptRansomWithoutTargetCount)}.");
        }

        private static bool HasValidAirdropBarterParameters(Dictionary<string, object> parameters)
        {
            NormalizeAirdropBarterParameters(parameters);

            if (!HasNonEmptyText(parameters, "need", requireString: true))
            {
                return false;
            }

            if (parameters == null ||
                !parameters.TryGetValue("payment_items", out object rawItems) ||
                rawItems == null ||
                !(rawItems is IEnumerable<object> paymentItems))
            {
                return false;
            }

            bool hasAny = false;
            foreach (object row in paymentItems)
            {
                if (!(row is Dictionary<string, object> item) ||
                    !HasNonEmptyText(item, "item") ||
                    !HasPositiveInteger(item, "count"))
                {
                    return false;
                }

                hasAny = true;
            }

            return hasAny;
        }

        private static void NormalizeAirdropBarterParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return;
            }

            if (TryReadStringByAliases(
                    parameters,
                    out string need,
                    "need",
                    "need_def",
                    "needDef",
                    "__airdrop_bound_need_def"))
            {
                string normalizedNeed = need.Trim();
                SetCanonicalParameter(parameters, "need", normalizedNeed);
                int explicitNeedCount = ExtractSingleExplicitAirdropNeedCount(normalizedNeed);
                if (explicitNeedCount > 0)
                {
                    SetCanonicalParameter(parameters, "count", explicitNeedCount);
                    parameters["__airdrop_explicit_need_count"] = explicitNeedCount;
                }
            }

            if (!TryReadParameterByAliases(parameters, out object rawPaymentItems, "payment_items") ||
                !(rawPaymentItems is IEnumerable<object> paymentItems))
            {
                return;
            }

            var normalizedItems = new List<object>();
            foreach (object row in paymentItems)
            {
                if (row is Dictionary<string, object> item)
                {
                    NormalizeAirdropPaymentItem(item);
                    normalizedItems.Add(item);
                    continue;
                }

                normalizedItems.Add(row);
            }

            SetCanonicalParameter(parameters, "payment_items", normalizedItems);
        }

        private static int ExtractSingleExplicitAirdropNeedCount(string need)
        {
            if (string.IsNullOrWhiteSpace(need))
            {
                return 0;
            }

            MatchCollection matches = System.Text.RegularExpressions.Regex.Matches(need, "\\d+");
            if (matches.Count != 1)
            {
                return 0;
            }

            return int.TryParse(matches[0].Value, out int parsed)
                ? Math.Max(0, parsed)
                : 0;
        }

        private static void NormalizeAirdropPaymentItem(Dictionary<string, object> item)
        {
            if (item == null || item.Count == 0)
            {
                return;
            }

            if (TryReadStringByAliases(item, out string value, "item", "defName", "def_name", "thingDef", "thing_def"))
            {
                SetCanonicalParameter(item, "item", value.Trim());
            }
        }

        private static string DescribeAirdropParameterType(Dictionary<string, object> parameters, string key)
        {
            if (parameters == null)
            {
                return "<parameters-null>";
            }

            if (string.IsNullOrWhiteSpace(key) || !parameters.TryGetValue(key, out object raw) || raw == null)
            {
                return "<missing>";
            }

            return raw.GetType().FullName ?? raw.GetType().Name;
        }

        private static string DescribeAirdropPaymentItemsCount(Dictionary<string, object> parameters)
        {
            if (parameters == null ||
                !parameters.TryGetValue("payment_items", out object rawItems) ||
                rawItems == null)
            {
                return "<missing>";
            }

            if (!(rawItems is IEnumerable<object> paymentItems))
            {
                return "<not-enumerable>";
            }

            return paymentItems.Count().ToString();
        }

        private static string DescribeAirdropPaymentItem0Type(Dictionary<string, object> parameters)
        {
            if (parameters == null ||
                !parameters.TryGetValue("payment_items", out object rawItems) ||
                rawItems == null)
            {
                return "<missing>";
            }

            if (!(rawItems is IEnumerable<object> paymentItems))
            {
                return "<not-enumerable>";
            }

            object first = paymentItems.FirstOrDefault();
            if (first == null)
            {
                return "<empty>";
            }

            return first.GetType().FullName ?? first.GetType().Name;
        }

        private static string DescribeAirdropPaymentItem0Keys(Dictionary<string, object> parameters)
        {
            if (parameters == null ||
                !parameters.TryGetValue("payment_items", out object rawItems) ||
                rawItems == null)
            {
                return "<missing>";
            }

            if (!(rawItems is IEnumerable<object> paymentItems))
            {
                return "<not-enumerable>";
            }

            object first = paymentItems.FirstOrDefault();
            if (!(first is Dictionary<string, object> item))
            {
                return "<not-dictionary>";
            }

            return item.Count <= 0
                ? "<no-keys>"
                : string.Join(",", item.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        }

        private static bool HasValidPrisonerRansomParameters(
            Dictionary<string, object> parameters,
            out string invalidParameter,
            out string paymentModeRaw,
            out string paymentModeNormalized,
            out bool paymentModePassthrough)
        {
            invalidParameter = string.Empty;
            paymentModeRaw = string.Empty;
            paymentModeNormalized = string.Empty;
            paymentModePassthrough = false;
            TryReadStringByAliases(
                parameters,
                out paymentModeRaw,
                "payment_mode",
                "paymentMode",
                "pay_mode",
                "payMode",
                "mode");

            NormalizePrisonerRansomParameters(parameters);

            // target_pawn_load_id is optional at parse stage; execution layer can bind from session state.
            if (TryReadLoosePositiveIntegerParameter(parameters, "target_pawn_load_id", out int targetPawnLoadId))
            {
                parameters["target_pawn_load_id"] = targetPawnLoadId;
            }

            if (!TryReadLoosePositiveIntegerParameter(parameters, "offer_silver", out int offerSilver))
            {
                invalidParameter = "offer_silver";
                return false;
            }

            parameters["offer_silver"] = offerSilver;
            if (parameters == null || !parameters.TryGetValue("payment_mode", out object modeObj) || modeObj == null)
            {
                paymentModeNormalized = "(omitted)";
                return true;
            }

            string mode = modeObj.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;
            if (TryNormalizePrisonerRansomPaymentMode(mode, out string normalizedMode, out bool passthroughToExecution))
            {
                paymentModeNormalized = normalizedMode;
                paymentModePassthrough = passthroughToExecution;
                if (!string.IsNullOrWhiteSpace(normalizedMode))
                {
                    parameters["payment_mode"] = normalizedMode;
                }

                return true;
            }

            paymentModeNormalized = "(omitted)";
            return true;
        }

        private static bool TryNormalizePrisonerRansomPaymentMode(
            string rawMode,
            out string normalizedMode,
            out bool passthroughToExecution)
        {
            normalizedMode = string.Empty;
            passthroughToExecution = false;
            if (string.IsNullOrWhiteSpace(rawMode))
            {
                return false;
            }

            string mode = rawMode.Trim().ToLowerInvariant();
            switch (mode)
            {
                case "silver":
                case "银币":
                case "银":
                case "coin":
                case "coins":
                case "cash":
                    normalizedMode = "silver";
                    return true;
                default:
                    normalizedMode = mode;
                    passthroughToExecution = true;
                    return true;
            }
        }

        private static string FormatRansomLogValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "(omitted)"
                : value.Trim();
        }

        private static void NormalizePrisonerRansomParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return;
            }

            if (TryReadLoosePositiveIntegerByAliases(
                    parameters,
                    out int targetPawnLoadId,
                    "target_pawn_load_id",
                    "targetPawnLoadId",
                    "target_pawn_id",
                    "targetPawnId",
                    "prisoner_load_id",
                    "prisonerLoadId",
                    "pawn_load_id",
                    "pawnLoadId",
                    "pawn_id",
                    "target_id"))
            {
                SetCanonicalParameter(parameters, "target_pawn_load_id", targetPawnLoadId);
            }

            if (TryReadLoosePositiveIntegerByAliases(
                    parameters,
                    out int offerSilver,
                    "offer_silver",
                    "offerSilver",
                    "offered_silver",
                    "offeredSilver",
                    "silver",
                    "amount",
                    "ransom_silver",
                    "ransomSilver"))
            {
                SetCanonicalParameter(parameters, "offer_silver", offerSilver);
            }

            if (TryReadStringByAliases(
                    parameters,
                    out string paymentMode,
                    "payment_mode",
                    "paymentMode",
                    "pay_mode",
                    "payMode",
                    "mode"))
            {
                SetCanonicalParameter(parameters, "payment_mode", paymentMode.Trim().ToLowerInvariant());
            }
        }

        private static bool TryReadLoosePositiveIntegerByAliases(
            Dictionary<string, object> values,
            out int parsed,
            params string[] aliases)
        {
            parsed = 0;
            if (!TryReadParameterByAliases(values, out object raw, aliases))
            {
                return false;
            }

            return TryReadLoosePositiveInteger(raw, out parsed);
        }

        private static bool TryReadStringByAliases(
            Dictionary<string, object> values,
            out string text,
            params string[] aliases)
        {
            text = string.Empty;
            if (!TryReadParameterByAliases(values, out object raw, aliases) || raw == null)
            {
                return false;
            }

            text = raw.ToString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(text);
        }

        private static bool TryReadParameterByAliases(
            Dictionary<string, object> values,
            out object raw,
            params string[] aliases)
        {
            raw = null;
            if (values == null || values.Count == 0 || aliases == null || aliases.Length == 0)
            {
                return false;
            }

            foreach (string alias in aliases)
            {
                string key = FindDictionaryKey(values, alias);
                if (string.IsNullOrWhiteSpace(key) || !values.TryGetValue(key, out object value) || value == null)
                {
                    continue;
                }

                raw = value;
                return true;
            }

            return false;
        }

        private static string FindDictionaryKey(Dictionary<string, object> values, string expected)
        {
            if (values == null || string.IsNullOrWhiteSpace(expected))
            {
                return string.Empty;
            }

            foreach (string key in values.Keys)
            {
                if (string.Equals(key, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }

            return string.Empty;
        }

        private static void SetCanonicalParameter(Dictionary<string, object> values, string canonicalKey, object value)
        {
            if (values == null || string.IsNullOrWhiteSpace(canonicalKey))
            {
                return;
            }

            string existing = FindDictionaryKey(values, canonicalKey);
            if (!string.IsNullOrWhiteSpace(existing) && !string.Equals(existing, canonicalKey, StringComparison.Ordinal))
            {
                values.Remove(existing);
            }

            values[canonicalKey] = value;
        }

        private static bool TryReadLoosePositiveIntegerParameter(Dictionary<string, object> values, string key, out int parsed)
        {
            parsed = 0;
            if (values == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string actualKey = FindDictionaryKey(values, key);
            if (string.IsNullOrWhiteSpace(actualKey) || !values.TryGetValue(actualKey, out object raw))
            {
                return false;
            }

            return TryReadLoosePositiveInteger(raw, out parsed);
        }

        private static bool TryReadLoosePositiveInteger(object raw, out int parsed)
        {
            parsed = 0;
            if (raw == null)
            {
                return false;
            }

            if (raw is int intValue)
            {
                parsed = intValue;
                return parsed > 0;
            }

            if (raw is long longValue)
            {
                if (longValue <= 0 || longValue > int.MaxValue)
                {
                    return false;
                }

                parsed = (int)longValue;
                return true;
            }

            if (raw is double doubleValue)
            {
                int rounded = (int)Math.Round(doubleValue);
                if (rounded <= 0 || Math.Abs(doubleValue - rounded) > 0.001d)
                {
                    return false;
                }

                parsed = rounded;
                return true;
            }

            string source = NormalizeNumberishText(raw.ToString());
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            if (int.TryParse(source, NumberStyles.Integer, CultureInfo.InvariantCulture, out int directParsed) && directParsed > 0)
            {
                parsed = directParsed;
                return true;
            }

            string digitsOnly = ExtractDigits(source);
            if (string.IsNullOrWhiteSpace(digitsOnly))
            {
                return false;
            }

            if (int.TryParse(digitsOnly, NumberStyles.Integer, CultureInfo.InvariantCulture, out int recovered) && recovered > 0)
            {
                parsed = recovered;
                return true;
            }

            return false;
        }

        private static string NormalizeNumberishText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw.Trim())
            {
                if (c >= '０' && c <= '９')
                {
                    sb.Append((char)('0' + (c - '０')));
                    continue;
                }

                if (c == '，' || c == ',')
                {
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString().Trim();
        }

        private static string ExtractDigits(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(source.Length);
            foreach (char c in source)
            {
                if (char.IsDigit(c))
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static bool HasNonEmptyText(Dictionary<string, object> values, string key, bool requireString = false)
        {
            if (values == null || !values.TryGetValue(key, out object raw) || raw == null)
            {
                return false;
            }

            if (requireString && !(raw is string))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(raw.ToString());
        }

        private static bool HasPositiveInteger(Dictionary<string, object> values, string key)
        {
            if (values == null || !values.TryGetValue(key, out object raw) || raw == null)
            {
                return false;
            }

            if (raw is int intValue)
            {
                return intValue > 0;
            }

            if (raw is long longValue)
            {
                return longValue > 0 && longValue <= int.MaxValue;
            }

            string text = raw.ToString();
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0;
        }

        /// <summary>/// 从JSON中提取字符串values
 ///</summary>
        private static string ExtractJsonString(string json, string key)
        {
            string pattern = $"\"{key}\":";
            int index = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;

            index += pattern.Length;
            // 跳过空白字符
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;

            if (index >= json.Length) return null;

            // 检查whether是字符串
            if (json[index] == '"')
            {
                index++;
                var sb = new StringBuilder();
                while (index < json.Length)
                {
                    char c = json[index];
                    if (c == '"' && (index == 0 || json[index - 1] != '\\'))
                    {
                        break;
                    }
                    sb.Append(c);
                    index++;
                }
                return UnescapeJsonString(sb.ToString());
            }

            // 不是字符串, 提取到下一个逗号或括号
            var valueSb = new StringBuilder();
            while (index < json.Length && json[index] != ',' && json[index] != '}' && json[index] != ']')
            {
                valueSb.Append(json[index]);
                index++;
            }
            return valueSb.ToString().Trim();
        }

        /// <summary>/// 从JSON中提取对象
 ///</summary>
        private static string ExtractJsonObject(string json, string key)
        {
            string pattern = $"\"{key}\":";
            int index = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;

            index += pattern.Length;
            // 跳过空白字符
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;

            if (index >= json.Length || json[index] != '{') return null;

            // 找到匹配的结束括号
            int braceCount = 1;
            int startIndex = index;
            index++;

            while (index < json.Length && braceCount > 0)
            {
                if (json[index] == '{') braceCount++;
                else if (json[index] == '}') braceCount--;
                index++;
            }

            return json.Substring(startIndex, index - startIndex);
        }

        private static string ExtractJsonArray(string json, string key)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            string pattern = $"\"{key}\":";
            int index = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;

            index += pattern.Length;
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
            if (index >= json.Length || json[index] != '[') return null;

            int depth = 1;
            int start = index;
            index++;
            while (index < json.Length && depth > 0)
            {
                if (json[index] == '[') depth++;
                else if (json[index] == ']') depth--;
                index++;
            }

            return depth == 0 ? json.Substring(start, index - start) : null;
        }

        private static List<string> SplitJsonObjects(string arrayJson)
        {
            var objects = new List<string>();
            if (string.IsNullOrWhiteSpace(arrayJson))
            {
                return objects;
            }

            string content = arrayJson.Trim();
            if (content.StartsWith("[")) content = content.Substring(1);
            if (content.EndsWith("]")) content = content.Substring(0, content.Length - 1);

            int depth = 0;
            int start = -1;
            bool inString = false;
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (c == '"' && (i == 0 || content[i - 1] != '\\'))
                {
                    inString = !inString;
                    continue;
                }
                if (inString) continue;

                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        objects.Add(content.Substring(start, i - start + 1));
                        start = -1;
                    }
                }
            }

            return objects;
        }

        /// <summary>/// 解析参数对象
 ///</summary>
        private static Dictionary<string, object> ParseParameters(string parametersJson)
        {
            var result = new Dictionary<string, object>();

            // 移除外层花括号
            string content = parametersJson.Trim();
            if (content.StartsWith("{")) content = content.Substring(1);
            if (content.EndsWith("}")) content = content.Substring(0, content.Length - 1);

            // 简单解析键values对
            var pairs = SplitJsonPairs(content);
            foreach (var pair in pairs)
            {
                var kv = pair.Split(new[] { ':' }, 2);
                if (kv.Length == 2)
                {
                    string key = kv[0].Trim().Trim('"');
                    result[key] = ParseJsonValue(kv[1]);
                }
            }

            return result;
        }

        private static object ParseJsonValue(string rawValue)
        {
            string value = (rawValue ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return string.Empty;
            }

            if (value.StartsWith("{") && value.EndsWith("}"))
            {
                return ParseJsonObject(value);
            }

            if (value.StartsWith("[") && value.EndsWith("]"))
            {
                return ParseJsonArray(value);
            }

            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return UnescapeJsonString(value.Substring(1, value.Length - 2));
            }

            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
            {
                return intValue;
            }

            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue))
            {
                return longValue;
            }

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue))
            {
                return doubleValue;
            }

            return UnescapeJsonString(value.Trim('"'));
        }

        private static Dictionary<string, object> ParseJsonObject(string objectJson)
        {
            var result = new Dictionary<string, object>();
            if (string.IsNullOrWhiteSpace(objectJson))
            {
                return result;
            }

            string content = objectJson.Trim();
            if (content.StartsWith("{"))
            {
                content = content.Substring(1);
            }
            if (content.EndsWith("}"))
            {
                content = content.Substring(0, content.Length - 1);
            }

            foreach (string pair in SplitJsonPairs(content))
            {
                string[] kv = pair.Split(new[] { ':' }, 2);
                if (kv.Length != 2)
                {
                    continue;
                }

                string key = kv[0].Trim().Trim('"');
                result[key] = ParseJsonValue(kv[1]);
            }

            return result;
        }

        private static List<object> ParseJsonArray(string arrayJson)
        {
            var result = new List<object>();
            if (string.IsNullOrWhiteSpace(arrayJson))
            {
                return result;
            }

            string content = arrayJson.Trim();
            if (content.StartsWith("["))
            {
                content = content.Substring(1);
            }
            if (content.EndsWith("]"))
            {
                content = content.Substring(0, content.Length - 1);
            }

            foreach (string item in SplitJsonArrayItems(content))
            {
                result.Add(ParseJsonValue(item));
            }

            return result;
        }

        private static List<string> SplitJsonArrayItems(string content)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(content))
            {
                return result;
            }

            var builder = new StringBuilder();
            int depth = 0;
            bool inString = false;
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (c == '"' && (i == 0 || content[i - 1] != '\\'))
                {
                    inString = !inString;
                }
                else if (!inString)
                {
                    if (c == '{' || c == '[')
                    {
                        depth++;
                    }
                    else if (c == '}' || c == ']')
                    {
                        depth--;
                    }
                    else if (c == ',' && depth == 0)
                    {
                        string item = builder.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            result.Add(item);
                        }

                        builder.Clear();
                        continue;
                    }
                }

                builder.Append(c);
            }

            string tail = builder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(tail))
            {
                result.Add(tail);
            }

            return result;
        }

        /// <summary>/// 分割JSON键values对
 ///</summary>
        private static List<string> SplitJsonPairs(string content)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            int braceDepth = 0;
            bool inString = false;

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];

                if (c == '"' && (i == 0 || content[i - 1] != '\\'))
                {
                    inString = !inString;
                }
                else if (!inString)
                {
                    if (c == '{' || c == '[') braceDepth++;
                    else if (c == '}' || c == ']') braceDepth--;
                    else if (c == ',' && braceDepth == 0)
                    {
                        result.Add(sb.ToString());
                        sb.Clear();
                        continue;
                    }
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
            {
                result.Add(sb.ToString());
            }

            return result;
        }

        /// <summary>/// 反转义JSON字符串
 ///</summary>
        private static string UnescapeJsonString(string str)
        {
            return str.Replace("\\\"", "\"")
                      .Replace("\\\\", "\\")
                      .Replace("\\n", "\n")
                      .Replace("\\r", "\r")
                      .Replace("\\t", "\t");
        }
    }

    /// <summary>/// JSONresponse结构
 ///</summary>
    public class JsonResponse
    {
        public string RawJson { get; set; }
        public List<StrategySuggestion> StrategySuggestions { get; set; }
    }

    internal sealed class JsonPayloadSegment
    {
        public int Start { get; set; }
        public int End { get; set; }
        public string Json { get; set; }
        public bool HasActions { get; set; }
        public bool HasStrategySuggestions { get; set; }
    }

    /// <summary>/// 解析后的response
 ///</summary>
    public class ParsedResponse
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string DialogueText { get; set; }
        public List<AIAction> Actions { get; set; }
        public List<StrategySuggestion> StrategySuggestions { get; set; }
    }

    /// <summary>/// 供玩家select的策略建议
 ///</summary>
    public class StrategySuggestion
    {
        public string StrategyName { get; set; }
        public string Reason { get; set; }
        public List<string> StrategyKeywords { get; set; }
        public string Content { get; set; }
    }

    /// <summary>/// AI动作
 ///</summary>
    public class AIAction
    {
        public string ActionType { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
        public string Reason { get; set; }
    }
}


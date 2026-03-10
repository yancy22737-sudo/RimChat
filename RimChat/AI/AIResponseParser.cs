using System;
using System.Collections.Generic;
using System.Text;
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
                    return ProcessJsonResponse(jsonResponse, faction, narrativeFallback);
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

        /// <summary>/// 尝试从response中提取JSON
 ///</summary>
        private static JsonResponse ParseJsonResponse(string response)
        {
            // LookupJSON开始和结束位置
            int jsonStart = response.IndexOf('{');
            int jsonEnd = response.LastIndexOf('}');

            if (jsonStart < 0 || jsonEnd <= jsonStart)
                return null;

            string json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);

            var result = new JsonResponse();
            result.RawJson = json;

            string strategySuggestionsJson = ExtractJsonArray(json, "strategy_suggestions");
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

            int jsonFenceIndex = response.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (jsonFenceIndex > 0)
            {
                return response.Substring(0, jsonFenceIndex).Trim();
            }

            int firstBrace = response.IndexOf('{');
            if (firstBrace > 0)
            {
                return response.Substring(0, firstBrace).Trim();
            }

            return response.Trim();
        }

        private static string NormalizeDialogueText(string text)
        {
            string normalized = (text ?? string.Empty).Trim();
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

            return normalized;
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
                "request_raid",
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

            string normalized = action.Trim().Trim('"').ToLowerInvariant().Replace("-", "_");
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
                default:
                    return normalized;
            }
        }

        private static List<AIAction> CollectActions(JsonResponse json)
        {
            var actions = new List<AIAction>();

            string actionsArray = ExtractJsonArray(json.RawJson, "actions");
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

                AddActionIfValid(actions, actionType, parameters, reason);
            }

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

        private static void AddActionIfValid(List<AIAction> actions, string actionType, Dictionary<string, object> parameters, string reason)
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
            if (string.IsNullOrWhiteSpace(reason) &&
                parameters.TryGetValue("reason", out object reasonObj) &&
                reasonObj != null)
            {
                reason = reasonObj.ToString();
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
                    string value = kv[1].Trim();

                    // 尝试解析为整数
                    if (int.TryParse(value, out int intValue))
                    {
                        result[key] = intValue;
                    }
                    // 尝试解析为浮点数
                    else if (float.TryParse(value, out float floatValue))
                    {
                        result[key] = floatValue;
                    }
                    // 字符串
                    else
                    {
                        result[key] = UnescapeJsonString(value.Trim('"'));
                    }
                }
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



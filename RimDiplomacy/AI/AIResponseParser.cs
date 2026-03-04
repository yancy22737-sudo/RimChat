using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace RimDiplomacy.AI
{
    /// <summary>
    /// LLM响应解析器
    /// 解析AI的JSON格式响应，提取API调用和对话内容
    /// </summary>
    public class AIResponseParser
    {
        /// <summary>
        /// 解析AI响应
        /// </summary>
        /// <param name="response">AI返回的原始响应</param>
        /// <param name="faction">当前对话的派系</param>
        /// <returns>解析结果</returns>
        public static ParsedResponse ParseResponse(string response, Faction faction)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return new ParsedResponse
                {
                    Success = false,
                    ErrorMessage = "Empty response",
                    DialogueText = "I have nothing to say at the moment."
                };
            }

            try
            {
                // 尝试解析JSON格式
                var jsonResponse = ParseJsonResponse(response);
                if (jsonResponse != null)
                {
                    return ProcessJsonResponse(jsonResponse, faction);
                }

                // 如果不是JSON，作为纯文本处理
                return new ParsedResponse
                {
                    Success = true,
                    DialogueText = response.Trim(),
                    Actions = new List<AIAction>()
                };
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] Failed to parse AI response: {ex.Message}");
                return new ParsedResponse
                {
                    Success = true,
                    DialogueText = response.Trim(),
                    Actions = new List<AIAction>()
                };
            }
        }

        /// <summary>
        /// 尝试从响应中提取JSON
        /// </summary>
        private static JsonResponse ParseJsonResponse(string response)
        {
            // 查找JSON开始和结束位置
            int jsonStart = response.IndexOf('{');
            int jsonEnd = response.LastIndexOf('}');

            if (jsonStart < 0 || jsonEnd <= jsonStart)
                return null;

            string json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);

            var result = new JsonResponse();

            // 提取action字段
            result.Action = ExtractJsonString(json, "action");
            result.Response = ExtractJsonString(json, "response");
            result.Reason = ExtractJsonString(json, "reason");

            // 提取parameters对象
            string parametersJson = ExtractJsonObject(json, "parameters");
            if (!string.IsNullOrEmpty(parametersJson))
            {
                result.Parameters = ParseParameters(parametersJson);
            }
            else
            {
                result.Parameters = new Dictionary<string, object>();
            }

            // 提取relation_changes对象
            string relationChangesJson = ExtractJsonObject(json, "relation_changes");
            if (!string.IsNullOrEmpty(relationChangesJson))
            {
                result.RelationChanges = ParseRelationChanges(relationChangesJson);
            }

            return result;
        }

        /// <summary>
        /// 解析五维关系值变化
        /// </summary>
        private static RelationChanges ParseRelationChanges(string json)
        {
            var result = new RelationChanges();

            // 提取各维度变化值
            result.Trust = ExtractFloatValue(json, "trust");
            result.Intimacy = ExtractFloatValue(json, "intimacy");
            result.Reciprocity = ExtractFloatValue(json, "reciprocity");
            result.Respect = ExtractFloatValue(json, "respect");
            result.Influence = ExtractFloatValue(json, "influence");
            result.Reason = ExtractJsonString(json, "reason");

            // 限制变化范围
            result.Trust = ClampRelationDelta(result.Trust);
            result.Intimacy = ClampRelationDelta(result.Intimacy);
            result.Reciprocity = ClampRelationDelta(result.Reciprocity);
            result.Respect = ClampRelationDelta(result.Respect);
            result.Influence = ClampRelationDelta(result.Influence);

            return result;
        }

        /// <summary>
        /// 从JSON中提取浮点数值
        /// </summary>
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

            // 提取数值
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

        /// <summary>
        /// 限制关系值变化范围
        /// </summary>
        private static float ClampRelationDelta(float delta)
        {
            // 单次变化限制在 -10 到 +10 之间
            return Math.Max(-10f, Math.Min(10f, delta));
        }

        /// <summary>
        /// 处理JSON格式的响应
        /// </summary>
        private static ParsedResponse ProcessJsonResponse(JsonResponse json, Faction faction)
        {
            var result = new ParsedResponse
            {
                Success = true,
                DialogueText = json.Response ?? "I understand.",
                Actions = new List<AIAction>(),
                RelationChanges = json.RelationChanges
            };

            // 如果没有action，只是纯对话
            if (string.IsNullOrEmpty(json.Action) || json.Action == "none")
            {
                return result;
            }

            // 创建AIAction
            var action = new AIAction
            {
                ActionType = json.Action.ToLower(),
                Parameters = json.Parameters,
                Reason = json.Reason
            };

            // 验证action是否有效
            if (IsValidAction(action.ActionType))
            {
                result.Actions.Add(action);
            }
            else
            {
                Log.Warning($"[RimDiplomacy] Unknown AI action: {action.ActionType}");
            }

            return result;
        }

        /// <summary>
        /// 检查action类型是否有效
        /// </summary>
        private static bool IsValidAction(string action)
        {
            string[] validActions = new string[]
            {
                "adjust_goodwill",
                "send_gift",
                "request_aid",
                "declare_war",
                "make_peace",
                "request_caravan",
                "request_raid",
                "reject_request"
            };

            return Array.Exists(validActions, a => a == action);
        }

        /// <summary>
        /// 从JSON中提取字符串值
        /// </summary>
        private static string ExtractJsonString(string json, string key)
        {
            string pattern = $"\"{key}\":";
            int index = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;

            index += pattern.Length;
            // 跳过空白字符
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;

            if (index >= json.Length) return null;

            // 检查是否是字符串
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

            // 不是字符串，提取到下一个逗号或括号
            var valueSb = new StringBuilder();
            while (index < json.Length && json[index] != ',' && json[index] != '}' && json[index] != ']')
            {
                valueSb.Append(json[index]);
                index++;
            }
            return valueSb.ToString().Trim();
        }

        /// <summary>
        /// 从JSON中提取对象
        /// </summary>
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

        /// <summary>
        /// 解析参数对象
        /// </summary>
        private static Dictionary<string, object> ParseParameters(string parametersJson)
        {
            var result = new Dictionary<string, object>();

            // 移除外层花括号
            string content = parametersJson.Trim();
            if (content.StartsWith("{")) content = content.Substring(1);
            if (content.EndsWith("}")) content = content.Substring(0, content.Length - 1);

            // 简单解析键值对
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

        /// <summary>
        /// 分割JSON键值对
        /// </summary>
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

        /// <summary>
        /// 反转义JSON字符串
        /// </summary>
        private static string UnescapeJsonString(string str)
        {
            return str.Replace("\\\"", "\"")
                      .Replace("\\\\", "\\")
                      .Replace("\\n", "\n")
                      .Replace("\\r", "\r")
                      .Replace("\\t", "\t");
        }
    }

    /// <summary>
    /// JSON响应结构
    /// </summary>
    public class JsonResponse
    {
        public string Action { get; set; }
        public string Response { get; set; }
        public string Reason { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
        public RelationChanges RelationChanges { get; set; }
    }

    /// <summary>
    /// 五维关系值变化
    /// </summary>
    public class RelationChanges
    {
        public float Trust { get; set; }
        public float Intimacy { get; set; }
        public float Reciprocity { get; set; }
        public float Respect { get; set; }
        public float Influence { get; set; }
        public string Reason { get; set; }

        /// <summary>
        /// 检查是否有任何变化
        /// </summary>
        public bool HasChanges()
        {
            return Math.Abs(Trust) > 0.01f ||
                   Math.Abs(Intimacy) > 0.01f ||
                   Math.Abs(Reciprocity) > 0.01f ||
                   Math.Abs(Respect) > 0.01f ||
                   Math.Abs(Influence) > 0.01f;
        }

        /// <summary>
        /// 获取变化摘要
        /// </summary>
        public string GetChangeSummary()
        {
            var changes = new System.Collections.Generic.List<string>();
            if (Math.Abs(Trust) > 0.01f) changes.Add($"信任{(Trust > 0 ? "+" : "")}{Trust:F1}");
            if (Math.Abs(Intimacy) > 0.01f) changes.Add($"亲密{(Intimacy > 0 ? "+" : "")}{Intimacy:F1}");
            if (Math.Abs(Reciprocity) > 0.01f) changes.Add($"互惠{(Reciprocity > 0 ? "+" : "")}{Reciprocity:F1}");
            if (Math.Abs(Respect) > 0.01f) changes.Add($"尊重{(Respect > 0 ? "+" : "")}{Respect:F1}");
            if (Math.Abs(Influence) > 0.01f) changes.Add($"影响{(Influence > 0 ? "+" : "")}{Influence:F1}");
            return string.Join(", ", changes);
        }
    }

    /// <summary>
    /// 解析后的响应
    /// </summary>
    public class ParsedResponse
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string DialogueText { get; set; }
        public List<AIAction> Actions { get; set; }
        public RelationChanges RelationChanges { get; set; }
    }

    /// <summary>
    /// AI动作
    /// </summary>
    public class AIAction
    {
        public string ActionType { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
        public string Reason { get; set; }
    }
}

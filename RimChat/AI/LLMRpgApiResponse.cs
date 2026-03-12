using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Verse;

namespace RimChat.AI
{
    public class LLMRpgApiResponse
    {
        public string DialogueContent { get; set; }
        public List<ApiAction> Actions { get; set; } = new List<ApiAction>();
        
        // New fields for Incidents and Quests
        public string IncidentDefName { get; set; }
        public float IncidentPoints { get; set; }
        
        // --- 预留字段: 未来used for自定义 Pawn 个人任务 ---
        public string QuestTitle { get; set; }
        public string QuestDescription { get; set; }
        public string QuestRewardDescription { get; set; }
        public string QuestCallbackId { get; set; }
        // -------------------------------------------
        
        public bool IsValid { get; set; }

        public class ApiAction
        {
            public string action;
            public string defName;
            public int amount;
            public string reason;
            // Additional parameters for Quest
            public string title;
            public string description;
            public string rewardDescription;
            public string callbackId;
        }

        public static LLMRpgApiResponse Parse(string rawResponse)
        {
            var result = new LLMRpgApiResponse();
            if (string.IsNullOrWhiteSpace(rawResponse)) return result;

            try
            {
                string jsonContent = null;
                var codeBlockMatch = Regex.Match(rawResponse, @"```(?:json)?\s*\n?(.*?)\n?```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (codeBlockMatch.Success) jsonContent = codeBlockMatch.Groups[1].Value.Trim();
                else
                {
                    jsonContent = ExtractFirstBalancedJsonObject(rawResponse);
                }

                if (!string.IsNullOrEmpty(jsonContent))
                {
                    ParseActions(jsonContent, result.Actions);
                    int jsonIndex = rawResponse.IndexOf(jsonContent);
                    if (jsonIndex > 0)
                    {
                        string content = rawResponse.Substring(0, jsonIndex).Trim();
                        content = Regex.Replace(content, "```json\\s*\\n?", "", RegexOptions.IgnoreCase);
                        content = Regex.Replace(content, "```\\s*$", "", RegexOptions.IgnoreCase);
                        result.DialogueContent = SanitizeDialogueContent(content.Trim());
                    }
                    else
                    {
                        string content = rawResponse.Replace(jsonContent, "").Replace("```json", "").Replace("```", "").Trim();
                        result.DialogueContent = SanitizeDialogueContent(content);
                    }

                    if (string.IsNullOrWhiteSpace(result.DialogueContent))
                    {
                        result.DialogueContent = SanitizeDialogueContent(ExtractLegacyDialogueContent(jsonContent));
                    }
                }
                else
                {
                    result.DialogueContent = SanitizeDialogueContent(rawResponse.Trim());
                }

                if (result.Actions.Count == 0)
                {
                    TryExtractInlineActions(rawResponse, result.Actions);
                }
                result.IsValid = true;
            }
            catch (Exception ex)
            {
                result.DialogueContent = SanitizeDialogueContent(rawResponse.Trim());
                Log.Error($"[RimChat] RPG JSON parse error: {ex}");
            }

            return result;
        }

        private static void ParseActions(string jsonContent, List<ApiAction> actions)
        {
            if (actions == null) return;

            string actionArrayJson = ExtractJsonArray(jsonContent, "actions");
            if (string.IsNullOrEmpty(actionArrayJson))
            {
                return;
            }

            foreach (string actionObject in SplitJsonObjects(actionArrayJson))
            {
                string normalizedAction = NormalizeActionName(
                    CoalesceActionName(
                        ExtractStringField(actionObject, "action"),
                        ExtractStringField(actionObject, "name")));
                if (string.IsNullOrEmpty(normalizedAction))
                {
                    continue;
                }

                // Accept both legacy "params" and mainstream "parameters" wrappers.
                string paramsObject = ExtractJsonObject(actionObject, "params");
                if (string.IsNullOrWhiteSpace(paramsObject))
                {
                    paramsObject = ExtractJsonObject(actionObject, "parameters");
                }
                string parameterSource = string.IsNullOrWhiteSpace(paramsObject) ? actionObject : paramsObject;

                var api = new ApiAction
                {
                    action = normalizedAction,
                    defName = CoalesceField(parameterSource, actionObject, "defName"),
                    reason = CoalesceField(parameterSource, actionObject, "reason"),
                    title = CoalesceField(parameterSource, actionObject, "title"),
                    description = CoalesceField(parameterSource, actionObject, "description"),
                    rewardDescription = CoalesceField(parameterSource, actionObject, "rewardDescription"),
                    callbackId = CoalesceField(parameterSource, actionObject, "callbackId")
                };

                int? amount = ExtractIntField(parameterSource, "amount") ?? ExtractIntField(actionObject, "amount");
                if (amount.HasValue)
                {
                    api.amount = amount.Value;
                }

                actions.Add(api);
            }
        }

        private static void TryExtractInlineActions(string rawResponse, List<ApiAction> actions)
        {
            if (actions == null || actions.Count > 0 || string.IsNullOrWhiteSpace(rawResponse))
            {
                return;
            }

            MatchCollection matches = Regex.Matches(
                rawResponse,
                @"(?:Use\s+Action|使用动作)\s*[:：]\s*([A-Za-z_][A-Za-z0-9_]*)[^\r\n\)]*",
                RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                string actionName = NormalizeActionName(match.Groups[1].Value);
                if (string.IsNullOrWhiteSpace(actionName) || HasAction(actions, actionName))
                {
                    continue;
                }

                actions.Add(new ApiAction
                {
                    action = actionName,
                    defName = ExtractInlineDefName(match.Value)
                });
            }
        }

        private static bool HasAction(List<ApiAction> actions, string actionName)
        {
            for (int i = 0; i < actions.Count; i++)
            {
                if (string.Equals(actions[i].action, actionName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ExtractInlineDefName(string actionSegment)
        {
            if (string.IsNullOrWhiteSpace(actionSegment))
            {
                return null;
            }

            Match match = Regex.Match(actionSegment, @"defName\s*=\s*([A-Za-z0-9_\.]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static string ExtractFirstBalancedJsonObject(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            int start = raw.IndexOf('{');
            if (start < 0)
            {
                return null;
            }

            bool inString = false;
            int depth = 0;
            for (int i = start; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '"' && (i == 0 || raw[i - 1] != '\\'))
                {
                    inString = !inString;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return raw.Substring(start, i - start + 1).Trim();
                    }
                }
            }

            return null;
        }

        private static string ExtractJsonObject(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            string pattern = $"\"{key}\"";
            int keyIndex = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
            {
                return null;
            }

            int colonIndex = json.IndexOf(':', keyIndex + pattern.Length);
            if (colonIndex < 0)
            {
                return null;
            }

            int objectStart = json.IndexOf('{', colonIndex + 1);
            if (objectStart < 0)
            {
                return null;
            }

            bool inString = false;
            int depth = 0;
            for (int i = objectStart; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                {
                    inString = !inString;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return json.Substring(objectStart, i - objectStart + 1);
                    }
                }
            }

            return null;
        }

        private static string ExtractJsonArray(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            string pattern = $"\"{key}\"";
            int keyIndex = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
            {
                return null;
            }

            int colonIndex = json.IndexOf(':', keyIndex + pattern.Length);
            if (colonIndex < 0)
            {
                return null;
            }

            int arrayStart = json.IndexOf('[', colonIndex + 1);
            if (arrayStart < 0)
            {
                return null;
            }

            bool inString = false;
            int depth = 0;
            for (int i = arrayStart; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                {
                    inString = !inString;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return json.Substring(arrayStart, i - arrayStart + 1);
                    }
                }
            }

            return null;
        }

        private static List<string> SplitJsonObjects(string arrayJson)
        {
            var result = new List<string>();
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

            bool inString = false;
            int depth = 0;
            int start = -1;
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (c == '"' && (i == 0 || content[i - 1] != '\\'))
                {
                    inString = !inString;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0)
                    {
                        start = i;
                    }
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        result.Add(content.Substring(start, i - start + 1));
                        start = -1;
                    }
                }
            }

            return result;
        }

        private static string ExtractStringField(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            string pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"";
            Match match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            return UnescapeJson(match.Groups[1].Value);
        }

        private static int? ExtractIntField(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            string pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*(-?\\d+)";
            Match match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            if (int.TryParse(match.Groups[1].Value, out int value))
            {
                return value;
            }

            return null;
        }

        private static string CoalesceActionName(string primary, string secondary)
        {
            if (!string.IsNullOrWhiteSpace(primary))
            {
                return primary;
            }

            return secondary;
        }

        private static string CoalesceField(string preferredJson, string fallbackJson, string key)
        {
            string value = ExtractStringField(preferredJson, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return ExtractStringField(fallbackJson, key);
        }

        private static string NormalizeActionName(string actionName)
        {
            if (string.IsNullOrWhiteSpace(actionName))
            {
                return null;
            }

            string normalized = actionName.Trim().Replace("-", "_").ToLowerInvariant();
            switch (normalized)
            {
                case "romanceattempt":
                case "romance_attempt":
                case "romance":
                case "fall_in_love":
                case "start_romance":
                case "恋爱":
                    return "RomanceAttempt";
                case "marriageproposal":
                case "marriage_proposal":
                case "propose_marriage":
                case "marry":
                case "结婚":
                    return "MarriageProposal";
                case "breakup":
                case "break_up":
                case "split_up":
                case "分手":
                    return "Breakup";
                case "divorce":
                case "离婚":
                    return "Divorce";
                case "date":
                case "dating":
                case "约会":
                    return "Date";
                case "trygainmemory":
                case "try_gain_memory":
                    return "TryGainMemory";
                case "tryaffectsocialgoodwill":
                case "try_affect_social_goodwill":
                    return "TryAffectSocialGoodwill";
                case "reduceresistance":
                case "reduce_resistance":
                    return "ReduceResistance";
                case "reducewill":
                case "reduce_will":
                    return "ReduceWill";
                case "recruit":
                case "action4":
                case "action_4":
                case "action 4":
                case "第4个动作":
                case "第四个动作":
                    return "Recruit";
                case "trytakeorderedjob":
                case "try_take_ordered_job":
                    return "TryTakeOrderedJob";
                case "triggerincident":
                case "trigger_incident":
                    return "TriggerIncident";
                case "grantinspiration":
                case "grant_inspiration":
                    return "GrantInspiration";
                case "exitdialoguecooldown":
                case "exit_dialogue_cooldown":
                case "exit_dialogue_with_cooldown":
                    return "ExitDialogueCooldown";
                case "exitdialogue":
                case "exit_dialogue":
                    return "ExitDialogue";
                default:
                    return actionName.Trim();
            }
        }

        private static string UnescapeJson(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }

            return str.Replace("\\\"", "\"")
                      .Replace("\\\\", "\\")
                      .Replace("\\n", "\n")
                      .Replace("\\r", "\r")
                      .Replace("\\t", "\t");
        }

        private static string SanitizeDialogueContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            string sanitized = content;
            sanitized = Regex.Replace(sanitized, @"\(\s*(?:Use\s+Action|使用动作)\s*[:：][^)\r\n]*\)", string.Empty, RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"（\s*(?:Use\s+Action|使用动作)\s*[:：][^）\r\n]*）", string.Empty, RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"^[ \t]*(?:Use\s+Action|使用动作)\s*[:：][^\r\n]*$", string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"^\s*\*\*<[^>\r\n]+>\*\*\s*$", string.Empty, RegexOptions.Multiline);
            sanitized = Regex.Replace(sanitized, @"^\s*<[^>\r\n]+>\s*$", string.Empty, RegexOptions.Multiline);
            sanitized = Regex.Replace(sanitized, @"^\s*\{[\s\r\n]*""defName""\s*:\s*""[^""]+""[\s\r\n]*\}\s*$", string.Empty, RegexOptions.Multiline);
            sanitized = Regex.Replace(sanitized, @"\n{3,}", "\n\n");
            return sanitized.Trim();
        }

        private static string ExtractLegacyDialogueContent(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                return string.Empty;
            }

            string dialogue = ExtractStringField(jsonContent, "dialogue");
            if (!string.IsNullOrWhiteSpace(dialogue))
            {
                return dialogue;
            }

            string response = ExtractStringField(jsonContent, "response");
            if (!string.IsNullOrWhiteSpace(response))
            {
                return response;
            }

            string content = ExtractStringField(jsonContent, "content");
            if (!string.IsNullOrWhiteSpace(content) && content.IndexOf("\"actions\"", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return content;
            }

            return string.Empty;
        }

    }
}

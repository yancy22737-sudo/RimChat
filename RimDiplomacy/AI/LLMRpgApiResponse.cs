using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Verse;

namespace RimDiplomacy.AI
{
    public class LLMRpgApiResponse
    {
        public string DialogueContent { get; set; }
        public float FavorabilityDelta { get; set; }
        public float TrustDelta { get; set; }
        public float FearDelta { get; set; }
        public float RespectDelta { get; set; }
        public float DependencyDelta { get; set; }
        public List<ApiAction> Actions { get; set; } = new List<ApiAction>();
        
        // New fields for Incidents and Quests
        public string IncidentDefName { get; set; }
        public float IncidentPoints { get; set; }
        
        // --- 预留字段：未来用于自定义 Pawn 个人任务 ---
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
                    var jsonMatch = Regex.Match(rawResponse, @"(\{[\s\S]*?\})", RegexOptions.Singleline);
                    if (jsonMatch.Success) jsonContent = jsonMatch.Groups[1].Value.Trim();
                }

                if (!string.IsNullOrEmpty(jsonContent))
                {
                    result.FavorabilityDelta = ExtractFloatValue(jsonContent, "\"favorability_delta\"\\s*:\\s*(-?\\d+\\.?\\d*)");
                    result.TrustDelta = ExtractFloatValue(jsonContent, "\"trust_delta\"\\s*:\\s*(-?\\d+\\.?\\d*)");
                    result.FearDelta = ExtractFloatValue(jsonContent, "\"fear_delta\"\\s*:\\s*(-?\\d+\\.?\\d*)");
                    result.RespectDelta = ExtractFloatValue(jsonContent, "\"respect_delta\"\\s*:\\s*(-?\\d+\\.?\\d*)");
                    result.DependencyDelta = ExtractFloatValue(jsonContent, "\"dependency_delta\"\\s*:\\s*(-?\\d+\\.?\\d*)");

                    // Refined Regex for parsing actions with more fields
                    var actionMatches = Regex.Matches(jsonContent, @"\{\s*""action""\s*:\s*""([^""]+)""(?:,\s*""defName""\s*:\s*""([^""]+)"")?(?:,\s*""amount""\s*:\s*(-?\d+))?(?:,\s*""title""\s*:\s*""([^""]+)"")?(?:,\s*""description""\s*:\s*""([^""]+)"")?(?:,\s*""rewardDescription""\s*:\s*""([^""]+)"")?(?:,\s*""callbackId""\s*:\s*""([^""]+)"")?\s*\}", RegexOptions.IgnoreCase);
                    foreach (Match m in actionMatches)
                    {
                        var api = new ApiAction();
                        api.action = m.Groups[1].Value;
                        if (m.Groups[2].Success) api.defName = m.Groups[2].Value;
                        if (m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out int amt)) api.amount = amt;
                        if (m.Groups[4].Success) api.title = m.Groups[4].Value;
                        if (m.Groups[5].Success) api.description = m.Groups[5].Value;
                        if (m.Groups[6].Success) api.rewardDescription = m.Groups[6].Value;
                        if (m.Groups[7].Success) api.callbackId = m.Groups[7].Value;
                        result.Actions.Add(api);
                    }
                    
                    int jsonIndex = rawResponse.IndexOf(jsonContent);
                    if (jsonIndex > 0)
                    {
                        string content = rawResponse.Substring(0, jsonIndex).Trim();
                        content = Regex.Replace(content, "```json\\s*\\n?", "", RegexOptions.IgnoreCase);
                        content = Regex.Replace(content, "```\\s*$", "", RegexOptions.IgnoreCase);
                        result.DialogueContent = content.Trim();
                    }
                    else
                    {
                        result.DialogueContent = rawResponse.Replace(jsonContent, "").Replace("```json", "").Replace("```", "").Trim();
                    }
                }
                else
                {
                    result.DialogueContent = rawResponse.Trim();
                }
                result.IsValid = true;
            }
            catch (Exception ex)
            {
                result.DialogueContent = rawResponse.Trim();
                Log.Error($"[RimDiplomacy] RPG JSON parse error: {ex}");
            }

            return result;
        }

        private static float ExtractFloatValue(string json, string pattern)
        {
            var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
            if (match.Success && float.TryParse(match.Groups[1].Value, out float value))
            {
                return value;
            }
            return 0f;
        }
    }
}

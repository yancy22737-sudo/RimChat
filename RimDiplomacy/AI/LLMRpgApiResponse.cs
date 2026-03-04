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
        public bool IsValid { get; set; }

        public class ApiAction
        {
            public string action;
            public string defName;
            public int amount;
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

                    var actionMatches = Regex.Matches(jsonContent, @"\{\s*""action""\s*:\s*""([^""]+)""(?:,\s*""defName""\s*:\s*""([^""]+)"")?(?:,\s*""amount""\s*:\s*(-?\d+))?\s*\}", RegexOptions.IgnoreCase);
                    foreach (Match m in actionMatches)
                    {
                        var api = new ApiAction();
                        api.action = m.Groups[1].Value;
                        if (m.Groups[2].Success) api.defName = m.Groups[2].Value;
                        if (m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out int amt)) api.amount = amt;
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

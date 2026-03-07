using System;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;
using RimChat.Relation;

namespace RimChat.AI
{
    /// <summary>
    /// LLM关系响应解析器
    /// 负责解析AI返回的结构化JSON，提取关系值变化
    /// </summary>
    public class LLMRelationResponse
    {
        /// <summary>
        /// 对话内容（纯文本部分）
        /// </summary>
        public string DialogueContent { get; set; }
        
        /// <summary>
        /// 信任值变化量
        /// </summary>
        public float TrustDelta { get; set; }
        
        /// <summary>
        /// 亲密度变化量
        /// </summary>
        public float IntimacyDelta { get; set; }
        
        /// <summary>
        /// 互惠值变化量
        /// </summary>
        public float ReciprocityDelta { get; set; }
        
        /// <summary>
        /// 尊重值变化量
        /// </summary>
        public float RespectDelta { get; set; }
        
        /// <summary>
        /// 影响值变化量
        /// </summary>
        public float InfluenceDelta { get; set; }
        
        /// <summary>
        /// 变化原因说明
        /// </summary>
        public string ChangeReason { get; set; }
        
        /// <summary>
        /// 解析是否成功
        /// </summary>
        public bool IsValid { get; set; }
        
        /// <summary>
        /// 解析错误信息
        /// </summary>
        public string ErrorMessage { get; set; }

        // ========== 常量 ==========
        
        /// <summary>
        /// 单次变化最大限制
        /// </summary>
        public const float MaxDeltaPerResponse = 15f;
        
        /// <summary>
        /// 单次变化最小限制（防止微小变化）
        /// </summary>
        public const float MinSignificantDelta = 0.5f;

        /// <summary>
        /// 解析LLM响应文本
        /// </summary>
        public static LLMRelationResponse Parse(string rawResponse)
        {
            var result = new LLMRelationResponse();
            
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                result.IsValid = false;
                result.ErrorMessage = "响应内容为空";
                return result;
            }

            try
            {
                // 提取JSON部分
                string jsonContent = ExtractJsonContent(rawResponse);
                
                if (string.IsNullOrEmpty(jsonContent))
                {
                    // 没有找到JSON，可能是不带结构化输出的旧格式
                    result.DialogueContent = rawResponse.Trim();
                    result.IsValid = true;
                    result.ErrorMessage = "未检测到关系值变化JSON，使用默认值0";
                    return result;
                }

                // 解析JSON内容
                ParseJsonContent(jsonContent, result);
                
                // 提取对话内容（JSON之前的部分）
                result.DialogueContent = ExtractDialogueContent(rawResponse, jsonContent);
                
                // 验证和限制变化值
                ValidateAndClampDeltas(result);
                
                result.IsValid = true;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.ErrorMessage = $"解析失败: {ex.Message}";
                result.DialogueContent = rawResponse.Trim();
            }

            return result;
        }

        /// <summary>
        /// 从响应中提取JSON内容
        /// </summary>
        private static string ExtractJsonContent(string rawResponse)
        {
            // 尝试匹配 ```json ... ``` 代码块
            var codeBlockPattern = @"```(?:json)?\s*\n?(.*?)\n?```";
            var codeBlockMatch = Regex.Match(rawResponse, codeBlockPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
            if (codeBlockMatch.Success)
            {
                return codeBlockMatch.Groups[1].Value.Trim();
            }

            // 尝试匹配 { ... } 花括号内容
            var jsonPattern = @"(\{[\s\S]*?""trust_delta""[\s\S]*?\})";
            var jsonMatch = Regex.Match(rawResponse, jsonPattern, RegexOptions.Singleline);
            
            if (jsonMatch.Success)
            {
                return jsonMatch.Groups[1].Value.Trim();
            }

            return null;
        }

        /// <summary>
        /// 解析JSON内容
        /// </summary>
        private static void ParseJsonContent(string json, LLMRelationResponse result)
        {
            // 使用正则表达式提取各字段值
            result.TrustDelta = ExtractFloatValue(json, "\"trust_delta\"\\s*:\\s*(-?\\d+\\.?\\d*)");
            result.IntimacyDelta = ExtractFloatValue(json, "\"intimacy_delta\"\\s*:\\s*(-?\\d+\\.?\\d*)");
            result.ReciprocityDelta = ExtractFloatValue(json, "\"reciprocity_delta\"\\s*:\\s*(-?\\d+\\.?\\d*)");
            result.RespectDelta = ExtractFloatValue(json, "\"respect_delta\"\\s*:\\s*(-?\\d+\\.?\\d*)");
            result.InfluenceDelta = ExtractFloatValue(json, "\"influence_delta\"\\s*:\\s*(-?\\d+\\.?\\d*)");
            result.ChangeReason = ExtractStringValue(json, "\"reason\"\\s*:\\s*\"([^\"]+)\"");
        }

        /// <summary>
        /// 提取浮点数值
        /// </summary>
        private static float ExtractFloatValue(string json, string pattern)
        {
            var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
            if (match.Success && float.TryParse(match.Groups[1].Value, out float value))
            {
                return value;
            }
            return 0f;
        }

        /// <summary>
        /// 提取字符串值
        /// </summary>
        private static string ExtractStringValue(string json, string pattern)
        {
            var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
            return string.Empty;
        }

        /// <summary>
        /// 提取对话内容（JSON之前的部分）
        /// </summary>
        private static string ExtractDialogueContent(string rawResponse, string jsonContent)
        {
            int jsonIndex = rawResponse.IndexOf(jsonContent);
            if (jsonIndex > 0)
            {
                string content = rawResponse.Substring(0, jsonIndex).Trim();
                // 移除可能的代码块标记
                content = Regex.Replace(content, "```json\\s*\\n?", "", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, "```\\s*$", "", RegexOptions.IgnoreCase);
                return content.Trim();
            }
            return rawResponse.Trim();
        }

        /// <summary>
        /// 验证和限制变化值
        /// </summary>
        private static void ValidateAndClampDeltas(LLMRelationResponse result)
        {
            result.TrustDelta = ClampDelta(result.TrustDelta);
            result.IntimacyDelta = ClampDelta(result.IntimacyDelta);
            result.ReciprocityDelta = ClampDelta(result.ReciprocityDelta);
            result.RespectDelta = ClampDelta(result.RespectDelta);
            result.InfluenceDelta = ClampDelta(result.InfluenceDelta);
        }

        /// <summary>
        /// 钳制变化值到有效范围
        /// </summary>
        private static float ClampDelta(float delta)
        {
            // 忽略微小变化
            if (Math.Abs(delta) < MinSignificantDelta)
            {
                return 0f;
            }
            
            // 限制最大变化
            return Math.Max(-MaxDeltaPerResponse, Math.Min(MaxDeltaPerResponse, delta));
        }

        /// <summary>
        /// 应用解析结果到关系值对象
        /// </summary>
        public void ApplyTo(FactionRelationValues relations)
        {
            if (!IsValid)
                return;

            relations.UpdateFromLLMResponse(
                TrustDelta,
                IntimacyDelta,
                ReciprocityDelta,
                RespectDelta,
                InfluenceDelta
            );
        }

        /// <summary>
        /// 获取变化摘要
        /// </summary>
        public string GetChangeSummary()
        {
            if (!IsValid)
                return $"解析失败: {ErrorMessage}";

            var changes = new System.Collections.Generic.List<string>();
            
            if (TrustDelta != 0) changes.Add($"信任{(TrustDelta > 0 ? "+" : "")}{TrustDelta:F1}");
            if (IntimacyDelta != 0) changes.Add($"亲密{(IntimacyDelta > 0 ? "+" : "")}{IntimacyDelta:F1}");
            if (ReciprocityDelta != 0) changes.Add($"互惠{(ReciprocityDelta > 0 ? "+" : "")}{ReciprocityDelta:F1}");
            if (RespectDelta != 0) changes.Add($"尊重{(RespectDelta > 0 ? "+" : "")}{RespectDelta:F1}");
            if (InfluenceDelta != 0) changes.Add($"影响{(InfluenceDelta > 0 ? "+" : "")}{InfluenceDelta:F1}");

            if (changes.Count == 0)
                return "关系值无变化";

            return string.Join(", ", changes);
        }

        /// <summary>
        /// 检查是否有显著变化
        /// </summary>
        public bool HasSignificantChanges()
        {
            return Math.Abs(TrustDelta) >= MinSignificantDelta ||
                   Math.Abs(IntimacyDelta) >= MinSignificantDelta ||
                   Math.Abs(ReciprocityDelta) >= MinSignificantDelta ||
                   Math.Abs(RespectDelta) >= MinSignificantDelta ||
                   Math.Abs(InfluenceDelta) >= MinSignificantDelta;
        }

        /// <summary>
        /// 获取总变化幅度
        /// </summary>
        public float GetTotalChangeMagnitude()
        {
            return Math.Abs(TrustDelta) + 
                   Math.Abs(IntimacyDelta) + 
                   Math.Abs(ReciprocityDelta) + 
                   Math.Abs(RespectDelta) + 
                   Math.Abs(InfluenceDelta);
        }
    }

    /// <summary>
    /// 关系响应解析结果事件参数
    /// </summary>
    public class RelationResponseParsedEventArgs : EventArgs
    {
        public Faction Faction { get; set; }
        public LLMRelationResponse Response { get; set; }
        public FactionRelationValues PreviousValues { get; set; }
        public FactionRelationValues NewValues { get; set; }
    }
}

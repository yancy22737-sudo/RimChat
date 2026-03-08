using System;
using System.IO;
using System.Text.RegularExpressions;
using Verse;
using RimWorld;
using RimChat.Config;

namespace RimChat.Persistence
{
    /// <summary>/// Prompt filemanager - 负责从 save_data folder读取和save AI prompt configuration
 ///</summary>
    public static class PromptFileManager
    {
        private const string PROMPT_DIRECTORY = "RimChat";
        private const string PROMPT_SUBDIRECTORY = "prompts";
        private const string DEFAULT_PROMPT_FILE = "global_prompt.json";
        
        /// <summary>/// Prompt file的基础path
 ///</summary>
        public static string BasePath
        {
            get
            {
                return Path.Combine(GenFilePaths.SaveDataFolderPath, PROMPT_DIRECTORY, PROMPT_SUBDIRECTORY);
            }
        }
        
        /// <summary>/// global Prompt filepath
 ///</summary>
        public static string GlobalPromptPath
        {
            get
            {
                return Path.Combine(BasePath, DEFAULT_PROMPT_FILE);
            }
        }
        
        /// <summary>/// 确保 prompt 目录presence
 ///</summary>
        public static void EnsureDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(BasePath))
                {
                    Directory.CreateDirectory(BasePath);
                    Log.Message($"[RimChat] 创建 prompt 目录：{BasePath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] 创建 prompt 目录失败：{ex.Message}");
            }
        }
        
        /// <summary>/// 从fileloadglobal Prompt
 ///</summary>
        public static PromptConfig LoadGlobalPrompt()
        {
            try
            {
                EnsureDirectoryExists();
                
                if (File.Exists(GlobalPromptPath))
                {
                    string json = File.ReadAllText(GlobalPromptPath);
                    var config = ParseJsonToPromptConfig(json);
                    
                    if (config != null && !string.IsNullOrEmpty(config.SystemPrompt))
                    {
                        Log.Message($"[RimChat] 从文件加载全局 Prompt: {config.Name}");
                        return config;
                    }
                }
                
                Log.Message($"[RimChat] Prompt 文件不存在或无效，使用默认 Prompt");
                return CreateDefaultPromptConfig();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] 加载 Prompt 文件失败：{ex.Message}");
                return CreateDefaultPromptConfig();
            }
        }
        
        /// <summary>/// 简单的 JSON 解析method - 将 JSON 字符串解析为 PromptConfig
 ///</summary>
        private static PromptConfig ParseJsonToPromptConfig(string json)
        {
            try
            {
                var config = new PromptConfig();
                
                // 提取 Name 字段
                var nameMatch = Regex.Match(json, @"""Name""\s*:\s*""([^""]*)""");
                if (nameMatch.Success)
                {
                    config.Name = nameMatch.Groups[1].Value;
                }
                
                // 提取 Enabled 字段
                var enabledMatch = Regex.Match(json, @"""Enabled""\s*:\s*(true|false)");
                if (enabledMatch.Success)
                {
                    config.Enabled = enabledMatch.Groups[1].Value.ToLower() == "true";
                }
                
                // 提取 FactionId 字段
                var factionIdMatch = Regex.Match(json, @"""FactionId""\s*:\s*""([^""]*)""");
                if (factionIdMatch.Success)
                {
                    config.FactionId = factionIdMatch.Groups[1].Value;
                }
                
                // 提取 SystemPrompt 字段 (支持多行)
                var promptMatch = Regex.Match(json, @"""SystemPrompt""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Singleline);
                if (promptMatch.Success)
                {
                    string rawPrompt = promptMatch.Groups[1].Value;
                    // Processing转义字符
                    config.SystemPrompt = UnescapeJsonString(rawPrompt);
                }

                // 提取 DialoguePrompt 字段 (支持多行)
                var dialogueMatch = Regex.Match(json, @"""DialoguePrompt""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Singleline);
                if (dialogueMatch.Success)
                {
                    string rawDialogue = dialogueMatch.Groups[1].Value;
                    // Processing转义字符
                    config.DialoguePrompt = UnescapeJsonString(rawDialogue);
                }

                return config;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] JSON 解析失败：{ex.Message}");
                return null;
            }
        }
        
        /// <summary>/// 反转义 JSON 字符串
 ///</summary>
        private static string UnescapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            return input
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }
        
        /// <summary>/// 简单的 JSON 序列化method - 将 PromptConfig 转换为 JSON 字符串
 ///</summary>
        private static string SerializePromptConfigToJson(PromptConfig config)
        {
            if (config == null) return "{}";

            string escapedPrompt = EscapeJsonString(config.SystemPrompt);
            string escapedDialogue = EscapeJsonString(config.DialoguePrompt);

            return $"{{\"Name\":\"{config.Name}\",\"SystemPrompt\":\"{escapedPrompt}\",\"DialoguePrompt\":\"{escapedDialogue}\",\"Enabled\":{config.Enabled.ToString().ToLower()},\"FactionId\":\"{config.FactionId}\"}}";
        }
        
        /// <summary>/// 转义 JSON 字符串
 ///</summary>
        private static string EscapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            
            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
        
        /// <summary>/// saveglobal Prompt 到file
 ///</summary>
        public static void SaveGlobalPrompt(PromptConfig config)
        {
            try
            {
                EnsureDirectoryExists();
                
                if (config == null)
                {
                    Log.Warning("[RimChat] 尝试保存空的 Prompt 配置");
                    return;
                }
                
                string json = SerializePromptConfigToJson(config);
                File.WriteAllText(GlobalPromptPath, json);
                
                Log.Message($"[RimChat] 已保存全局 Prompt 到：{GlobalPromptPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] 保存 Prompt 文件失败：{ex.Message}");
            }
        }
        
        /// <summary>/// 创建默认的 Prompt configuration
 ///</summary>
        private static PromptConfig CreateDefaultPromptConfig()
        {
            return new PromptConfig
            {
                Name = "Global",
                SystemPrompt = GetDefaultImmersionPrompt(),
                Enabled = true,
                FactionId = ""
            };
        }
        
        /// <summary>/// get默认的深度沉浸 Prompt (中文版)
 ///</summary>
        private static string GetDefaultImmersionPrompt()
        {
            return "你是一名在环世界（RimWorld）中控制派系的 AI。请完全沉浸于你所扮演的角色中，基于以下准则进行回应：\n\n" +
                   "【核心沉浸原则】\n" +
                   "1. 自主决策：根据当前游戏场景和情境主动做出决策与回应，无需等待玩家明确指令\n" +
                   "2. 玩家行为分析：敏锐识别玩家行为背后的意图，判断是否存在欺骗或恶意\n" +
                   "3. 深度角色扮演：\n" +
                   "   - 建立符合角色背景的独特语言风格和习惯性口癖\n" +
                   "   - 严格遵循所属阵营的价值观、行为模式和社会规范\n" +
                   "   - 体现与所处文明科技水平相匹配的知识体系和表达方式\n" +
                   "4. 场景融入：所有对话和行为必须紧密结合环世界游戏背景，反映游戏世界的生存环境、社会结构和科技水平\n" +
                   "5. 沉浸感维护：\n" +
                   "   - 禁止直接显示好感度数值、情绪值等游戏机制数据\n" +
                   "   - 情感变化必须通过语言、态度和行为间接体现\n" +
                   "   - 避免使用任何破坏角色扮演沉浸感的元游戏语言或机制说明\n" +
                   "\n" +
                   "【记忆与历史整合】\n" +
                   "你将收到关于其他派系的记忆数据和交互历史。请：\n" +
                   "- 基于历史交互形成对派系的长期印象\n" +
                   "- 根据最近事件调整当前态度\n" +
                   "- 记住重大事件（宣战、议和、背叛等）并影响后续决策\n" +
                   "- 保持对派系关系演变的连贯认知\n" +
                   "\n" +
                   "【动态响应策略】\n" +
                   "- 友好派系：开放合作，愿意提供帮助，语言温和\n" +
                   "- 中立派系：谨慎试探，权衡利弊，保持礼貌距离\n" +
                   "- 敌对派系：警惕怀疑，可能威胁或拒绝合作，语言强硬\n" +
                   "- 根据领袖特质调整决策风格（如：嗜血者更倾向暴力，善良者更倾向和平）\n" +
                   "\n" +
                   "【重要禁令】\n" +
                   "- 禁止暴露 AI 身份\n" +
                   "- 禁止使用现代网络用语或与游戏世界观不符的词汇\n" +
                   "- 禁止直接引用游戏机制术语（如\"好感度\"、\"NPC\"、\"玩家\"等）\n" +
                   "- 禁止跳出角色进行元评论\n" +
                   "\n" +
                   "保持角色一致性，你的思考方式、决策逻辑和表达方式需完全符合所扮演角色的设定。";
        }
        
        /// <summary>/// 检查 Prompt filewhetherpresence
 ///</summary>
        public static bool PromptFileExists()
        {
            return File.Exists(GlobalPromptPath);
        }
        
        /// <summary>/// 删除自定义 Prompt file (重置为默认)
 ///</summary>
        public static void DeleteCustomPrompt()
        {
            try
            {
                if (File.Exists(GlobalPromptPath))
                {
                    File.Delete(GlobalPromptPath);
                    Log.Message("[RimChat] 已删除自定义 Prompt，将使用默认 Prompt");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] 删除 Prompt 文件失败：{ex.Message}");
            }
        }
    }
}

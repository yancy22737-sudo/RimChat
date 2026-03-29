using System;

namespace RimChat.Prompting
{
    internal static class ToolPromptRenderer
    {
        private const string SummarySystemPrompt = @"你是摘要压缩器。
任务：将提供的对话上下文压缩为简洁摘要。
禁止：新增事实、改写立场、添加角色扮演语气、输出解释性文本。只做信息压缩。

输出格式（严格遵守）：
第一行：Summary: <一句话摘要，不超过80字>
后续行（可选，最多3条）：
- <关键事实1>
- <关键事实2>

禁止输出JSON、markdown代码块、额外说明或引号包裹。";

        private const string ArchiveCompressionSystemPrompt = @"你是存档压缩器。
任务：将提供的对话记录压缩为一段简洁的事实性陈述。
禁止：添加解释、情感描述、角色扮演语气。

输出格式：仅输出一行不超过200字的事实陈述。";

        internal static string RenderSummaryPrompt(string summaryContext, string factionName)
        {
            if (string.IsNullOrWhiteSpace(factionName))
            {
                factionName = "Unknown";
            }

            return $"{SummarySystemPrompt}\n\n背景：派系={factionName}\n{summaryContext ?? string.Empty}";
        }

        internal static string RenderArchiveCompressionPrompt(
            string npcName,
            string interlocutorName,
            string sessionTranscript)
        {
            if (string.IsNullOrWhiteSpace(npcName))
            {
                npcName = "UnknownNPC";
            }

            if (string.IsNullOrWhiteSpace(interlocutorName))
            {
                interlocutorName = "Unknown";
            }

            return $"{ArchiveCompressionSystemPrompt}\n\nNPC={npcName}\n对话方={interlocutorName}\n对话记录：\n{sessionTranscript ?? string.Empty}";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: legacy prompt seed migration in RimChatSettings.
    /// Responsibility: split legacy combined prompt text into entry-sized seed blocks.
    /// </summary>
    public partial class RimChatSettings
    {
        private sealed class LegacyPromptEntrySeed
        {
            public string Name = "Entry";
            public string Content = string.Empty;
        }

        private static List<LegacyPromptEntrySeed> SplitLegacyPromptEntrySeeds(string baseName, string content)
        {
            var result = new List<LegacyPromptEntrySeed>();
            if (string.IsNullOrWhiteSpace(content))
            {
                return result;
            }

            string normalized = content.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            string[] lines = normalized.Split('\n');
            string currentName = string.IsNullOrWhiteSpace(baseName) ? "Entry" : baseName.Trim();
            var buffer = new StringBuilder();
            bool hasSectionSplit = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string rawLine = lines[i] ?? string.Empty;
                string trimmed = rawLine.Trim();
                if (IsLegacyPromptSectionHeader(trimmed))
                {
                    AddLegacyPromptEntrySeed(result, currentName, buffer);
                    buffer.Clear();
                    currentName = BuildLegacyPromptEntryName(baseName, trimmed);
                    hasSectionSplit = true;
                }

                if (buffer.Length > 0)
                {
                    buffer.Append('\n');
                }

                buffer.Append(rawLine);
            }

            AddLegacyPromptEntrySeed(result, currentName, buffer);
            if (!hasSectionSplit || result.Count == 0)
            {
                result.Clear();
                result.Add(new LegacyPromptEntrySeed
                {
                    Name = string.IsNullOrWhiteSpace(baseName) ? "Entry" : baseName.Trim(),
                    Content = normalized
                });
            }

            return result;
        }

        private static bool IsLegacyPromptSectionHeader(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            if (line.Length > 2 && line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                return true;
            }

            if (line.Length > 6 && line.StartsWith("===", StringComparison.Ordinal) && line.EndsWith("===", StringComparison.Ordinal))
            {
                return true;
            }

            return line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal);
        }

        private static string BuildLegacyPromptEntryName(string baseName, string headerLine)
        {
            string prefix = string.IsNullOrWhiteSpace(baseName) ? "Entry" : baseName.Trim();
            if (string.IsNullOrWhiteSpace(headerLine))
            {
                return prefix;
            }

            string cleaned = headerLine.Trim().Trim('[', ']').Trim('=').Trim('#').Trim();
            if (cleaned.Length > 42)
            {
                cleaned = cleaned.Substring(0, 42).Trim();
            }

            return string.IsNullOrWhiteSpace(cleaned) ? prefix : $"{prefix} - {cleaned}";
        }

        private static void AddLegacyPromptEntrySeed(List<LegacyPromptEntrySeed> list, string name, StringBuilder buffer)
        {
            if (list == null || buffer == null)
            {
                return;
            }

            string text = buffer.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            list.Add(new LegacyPromptEntrySeed
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Entry" : name.Trim(),
                Content = text
            });
        }
    }
}

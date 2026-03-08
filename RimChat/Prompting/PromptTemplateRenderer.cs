using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RimChat.Prompting
{
    /// <summary>/// Dependencies: regex-based variable parsing.
 /// Responsibility: render reusable prompt templates with {{variable}} placeholders.
 ///</summary>
    internal static class PromptTemplateRenderer
    {
        private static readonly Regex PlaceholderRegex =
            new Regex(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.Compiled);

        public static string Render(string templateText, IReadOnlyDictionary<string, string> variables)
        {
            if (string.IsNullOrWhiteSpace(templateText))
            {
                return string.Empty;
            }

            if (variables == null || variables.Count == 0 || templateText.IndexOf("{{", StringComparison.Ordinal) < 0)
            {
                return templateText.Trim();
            }

            string rendered = PlaceholderRegex.Replace(templateText, match =>
            {
                string variableName = NormalizeVariableName(match.Groups[1].Value);
                if (variableName.Length == 0)
                {
                    return match.Value;
                }

                if (variables.TryGetValue(variableName, out string value))
                {
                    return value ?? string.Empty;
                }

                return match.Value;
            });

            return rendered.Trim();
        }

        private static string NormalizeVariableName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return string.Empty;
            }

            return rawName.Trim().ToLowerInvariant();
        }
    }
}

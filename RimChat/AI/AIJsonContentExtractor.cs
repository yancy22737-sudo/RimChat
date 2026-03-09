using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RimChat.AI
{
    /// <summary>
    /// Dependencies: .NET regex runtime.
    /// Responsibility: robustly extract model text content from JSON payloads.
    /// </summary>
    public static class AIJsonContentExtractor
    {
        private static readonly string[] CandidateTextKeys =
        {
            "content",
            "text",
            "output_text",
            "response"
        };

        private static readonly Regex ErrorRegex =
            new Regex("\"error\"\\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Dictionary<string, Regex> KeyRegexCache =
            new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase);

        private static readonly object RegexCacheLock = new object();

        public static bool IsErrorPayload(string json)
        {
            return !string.IsNullOrWhiteSpace(json) && ErrorRegex.IsMatch(json);
        }

        public static bool TryExtractPrimaryText(string json, out string content)
        {
            content = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            for (int i = 0; i < CandidateTextKeys.Length; i++)
            {
                if (!TryExtractStringValue(json, CandidateTextKeys[i], out string value))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    content = value;
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractStringValue(string json, string key, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            Regex regex = GetKeyRegex(key);
            Match match = regex.Match(json);
            if (!match.Success)
            {
                return false;
            }

            string captured = match.Groups["value"]?.Value ?? string.Empty;
            value = UnescapeJsonString(captured).Trim();
            return true;
        }

        private static Regex GetKeyRegex(string key)
        {
            lock (RegexCacheLock)
            {
                if (KeyRegexCache.TryGetValue(key, out Regex cached))
                {
                    return cached;
                }

                string pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"";
                var created = new Regex(
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);
                KeyRegexCache[key] = created;
                return created;
            }
        }

        private static string UnescapeJsonString(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char ch = raw[i];
                if (ch != '\\' || i + 1 >= raw.Length)
                {
                    sb.Append(ch);
                    continue;
                }

                char escaped = raw[++i];
                switch (escaped)
                {
                    case '"':
                    case '\\':
                    case '/':
                        sb.Append(escaped);
                        break;
                    case 'b':
                        sb.Append('\b');
                        break;
                    case 'f':
                        sb.Append('\f');
                        break;
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    case 'u':
                        if (TryParseUnicodeEscape(raw, i + 1, out char unicodeChar))
                        {
                            sb.Append(unicodeChar);
                            i += 4;
                        }
                        else
                        {
                            sb.Append("\\u");
                        }
                        break;
                    default:
                        sb.Append(escaped);
                        break;
                }
            }

            return sb.ToString();
        }

        private static bool TryParseUnicodeEscape(string raw, int hexStart, out char unicodeChar)
        {
            unicodeChar = default(char);
            if (hexStart + 4 > raw.Length)
            {
                return false;
            }

            string hex = raw.Substring(hexStart, 4);
            if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort parsed))
            {
                return false;
            }

            unicodeChar = (char)parsed;
            return true;
        }
    }
}

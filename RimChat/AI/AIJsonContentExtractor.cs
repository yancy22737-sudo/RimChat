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
            "output_text",
            "response",
            "content",
            "text"
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
            MatchCollection matches = regex.Matches(json);
            if (matches == null || matches.Count == 0)
            {
                return false;
            }

            if (!TrySelectBestCandidate(json, key, matches, out string bestValue))
            {
                return false;
            }

            value = bestValue;
            return true;
        }

        private static bool TrySelectBestCandidate(string json, string key, MatchCollection matches, out string value)
        {
            value = string.Empty;
            int bestScore = int.MinValue;
            string bestValue = string.Empty;
            for (int i = 0; i < matches.Count; i++)
            {
                if (!TryDecodeMatchCandidate(matches[i], out string candidate))
                {
                    continue;
                }

                int score = ScoreMatchCandidate(json, key, matches[i].Index, candidate.Length);
                if (score > bestScore || (score == bestScore && candidate.Length > bestValue.Length))
                {
                    bestScore = score;
                    bestValue = candidate;
                }
            }

            if (string.IsNullOrWhiteSpace(bestValue))
            {
                return false;
            }

            value = bestValue;
            return true;
        }

        private static bool TryDecodeMatchCandidate(Match match, out string candidate)
        {
            candidate = string.Empty;
            if (match == null || !match.Success)
            {
                return false;
            }

            string captured = match.Groups["value"]?.Value ?? string.Empty;
            candidate = UnescapeJsonString(captured).Trim();
            return !string.IsNullOrWhiteSpace(candidate);
        }

        private static int ScoreMatchCandidate(string json, string key, int matchIndex, int valueLength)
        {
            return GetKeyPriorityScore(key)
                + GetContextScore(json, matchIndex)
                + GetPositionScore(json?.Length ?? 0, matchIndex)
                + GetLengthScore(valueLength);
        }

        private static int GetKeyPriorityScore(string key)
        {
            string keyLower = (key ?? string.Empty).ToLowerInvariant();
            if (keyLower == "output_text" || keyLower == "response")
            {
                return 60;
            }
            if (keyLower == "content")
            {
                return 30;
            }
            if (keyLower == "text")
            {
                return 15;
            }
            return 0;
        }

        private static int GetContextScore(string json, int matchIndex)
        {
            int score = 0;
            int start = Math.Max(0, matchIndex - 240);
            int length = Math.Max(0, matchIndex - start);
            string context = length > 0 ? json.Substring(start, length).ToLowerInvariant() : string.Empty;
            if (context.Contains("\"role\"") && context.Contains("assistant"))
            {
                score += 120;
            }
            if (context.Contains("\"role\"") && context.Contains("user"))
            {
                score -= 90;
            }
            if (context.Contains("\"choices\""))
            {
                score += 25;
            }
            if (context.Contains("\"messages\""))
            {
                score -= 20;
            }
            return score;
        }

        private static int GetPositionScore(int jsonLength, int matchIndex)
        {
            if (jsonLength <= 0)
            {
                return 0;
            }

            int score = 0;
            int half = jsonLength / 2;
            if (matchIndex >= half)
            {
                score += 20;
            }
            if (matchIndex >= (jsonLength * 3) / 4)
            {
                score += 10;
            }
            return score;
        }

        private static int GetLengthScore(int valueLength)
        {
            if (valueLength >= 200)
            {
                return 30;
            }
            if (valueLength >= 80)
            {
                return 20;
            }
            if (valueLength >= 40)
            {
                return 10;
            }
            if (valueLength < 8)
            {
                return -20;
            }
            return 0;
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

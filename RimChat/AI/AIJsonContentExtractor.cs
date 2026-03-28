using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RimChat.AI
{
    public sealed class PrimaryTextExtractionResult
    {
        public bool IsSuccess { get; set; }
        public string Content { get; set; } = string.Empty;
        public string ReasonTag { get; set; } = "unknown";
        public string MatchedPath { get; set; } = string.Empty;
    }

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
            "text",
            "generated_text",
            "answer",
            "reasoning_content"
        };

        private static readonly Regex ErrorRegex =
            new Regex("\"error\"\\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ContentArrayStartRegex =
            new Regex("\"content\"\\s*:\\s*\\[", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TextFieldRegex =
            new Regex("\"text\"\\s*:\\s*\"(?<value>(?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, Regex> KeyRegexCache =
            new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase);

        private static readonly object RegexCacheLock = new object();

        public static bool IsErrorPayload(string json)
        {
            return !string.IsNullOrWhiteSpace(json) && ErrorRegex.IsMatch(json);
        }

        public static PrimaryTextExtractionResult TryExtractPrimaryText(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return BuildFailure("invalid_payload");
            }

            string trimmed = json.Trim();
            if (TryExtractFromSsePayload(trimmed, out PrimaryTextExtractionResult sseResult))
            {
                return sseResult;
            }

            PrimaryTextExtractionResult jsonResult = TryExtractPrimaryTextFromJsonPayload(trimmed);
            if (jsonResult.IsSuccess)
            {
                return jsonResult;
            }

            if (!LooksLikeJsonPayload(trimmed))
            {
                string raw = SanitizeText(trimmed);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    return new PrimaryTextExtractionResult
                    {
                        IsSuccess = true,
                        Content = raw,
                        ReasonTag = "ok",
                        MatchedPath = "raw_text"
                    };
                }
            }

            return jsonResult;
        }

        private static PrimaryTextExtractionResult TryExtractPrimaryTextFromJsonPayload(string json)
        {
            var candidates = new List<TextCandidate>();
            CollectStringKeyCandidates(json, candidates);
            CollectContentArrayCandidates(json, candidates);
            if (candidates.Count == 0)
            {
                return BuildFailure("no_extractable_text");
            }

            List<TextCandidate> ordered = candidates
                .OrderByDescending(candidate => ScoreMatchCandidate(json, candidate))
                .ThenByDescending(candidate => candidate.Value.Length)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                string sanitized = SanitizeText(ordered[i].Value);
                if (string.IsNullOrWhiteSpace(sanitized))
                {
                    continue;
                }

                return new PrimaryTextExtractionResult
                {
                    IsSuccess = true,
                    Content = sanitized,
                    ReasonTag = "ok",
                    MatchedPath = ordered[i].Path
                };
            }

            return BuildFailure("empty_primary_text");
        }

        private static bool TryExtractFromSsePayload(string payload, out PrimaryTextExtractionResult result)
        {
            result = BuildFailure("sse_no_extractable_text");
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            if (payload.IndexOf("data:", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            string[] lines = payload.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
            {
                return false;
            }

            var segments = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i]?.Trim() ?? string.Empty;
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string data = line.Substring(5).Trim();
                if (string.IsNullOrWhiteSpace(data) || string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (LooksLikeJsonPayload(data))
                {
                    PrimaryTextExtractionResult chunk = TryExtractPrimaryTextFromJsonPayload(data);
                    if (chunk.IsSuccess && !string.IsNullOrWhiteSpace(chunk.Content))
                    {
                        segments.Add(chunk.Content.Trim());
                    }
                    continue;
                }

                string plainChunk = SanitizeText(data);
                if (!string.IsNullOrWhiteSpace(plainChunk))
                {
                    segments.Add(plainChunk);
                }
            }

            if (segments.Count == 0)
            {
                return true;
            }

            result = new PrimaryTextExtractionResult
            {
                IsSuccess = true,
                Content = string.Join(" ", segments).Trim(),
                ReasonTag = "ok",
                MatchedPath = "sse.data"
            };
            return true;
        }

        private static bool LooksLikeJsonPayload(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.TrimStart();
            return trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal);
        }

        private static PrimaryTextExtractionResult BuildFailure(string reasonTag)
        {
            return new PrimaryTextExtractionResult
            {
                IsSuccess = false,
                Content = string.Empty,
                ReasonTag = string.IsNullOrWhiteSpace(reasonTag) ? "unknown" : reasonTag,
                MatchedPath = string.Empty
            };
        }

        private static string SanitizeText(string value)
        {
            string sanitized = ModelOutputSanitizer.StripReasoningTags(value ?? string.Empty).Trim();
            return sanitized;
        }

        private static void CollectStringKeyCandidates(string json, List<TextCandidate> candidates)
        {
            for (int i = 0; i < CandidateTextKeys.Length; i++)
            {
                CollectStringKeyCandidates(json, CandidateTextKeys[i], candidates);
            }
        }

        private static void CollectStringKeyCandidates(string json, string key, List<TextCandidate> candidates)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            Regex regex = GetKeyRegex(key);
            MatchCollection matches = regex.Matches(json);
            if (matches == null || matches.Count == 0)
            {
                return;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                if (!TryDecodeMatchCandidate(matches[i], out string decoded))
                {
                    continue;
                }

                candidates.Add(new TextCandidate
                {
                    Key = key,
                    Value = decoded,
                    MatchIndex = matches[i].Index,
                    Path = key
                });
            }
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

        private static void CollectContentArrayCandidates(string json, List<TextCandidate> candidates)
        {
            MatchCollection starts = ContentArrayStartRegex.Matches(json);
            if (starts == null || starts.Count == 0)
            {
                return;
            }

            for (int i = 0; i < starts.Count; i++)
            {
                Match match = starts[i];
                if (match == null || !match.Success)
                {
                    continue;
                }

                int bracketStart = json.IndexOf('[', match.Index);
                if (bracketStart < 0)
                {
                    continue;
                }

                if (!TryExtractJsonArrayBlock(json, bracketStart, out string arrayBlock))
                {
                    continue;
                }

                MatchCollection textMatches = TextFieldRegex.Matches(arrayBlock);
                if (textMatches == null || textMatches.Count == 0)
                {
                    continue;
                }

                for (int j = 0; j < textMatches.Count; j++)
                {
                    if (!TryDecodeMatchCandidate(textMatches[j], out string decoded))
                    {
                        continue;
                    }

                    candidates.Add(new TextCandidate
                    {
                        Key = "text",
                        Value = decoded,
                        MatchIndex = match.Index + textMatches[j].Index,
                        Path = "content[].text"
                    });
                }
            }
        }

        private static bool TryExtractJsonArrayBlock(string json, int startIndex, out string block)
        {
            block = string.Empty;
            if (string.IsNullOrWhiteSpace(json) || startIndex < 0 || startIndex >= json.Length || json[startIndex] != '[')
            {
                return false;
            }

            bool inString = false;
            bool escape = false;
            int depth = 0;
            for (int i = startIndex; i < json.Length; i++)
            {
                char current = json[i];
                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                    }
                    else if (current == '\\')
                    {
                        escape = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == '[')
                {
                    depth++;
                    continue;
                }

                if (current != ']')
                {
                    continue;
                }

                depth--;
                if (depth != 0)
                {
                    continue;
                }

                block = json.Substring(startIndex, i - startIndex + 1);
                return true;
            }

            return false;
        }

        private static int ScoreMatchCandidate(string json, TextCandidate candidate)
        {
            if (candidate == null)
            {
                return int.MinValue;
            }

            return GetKeyPriorityScore(candidate.Key)
                + GetContextScore(json, candidate.MatchIndex)
                + GetPositionScore(json?.Length ?? 0, candidate.MatchIndex)
                + GetLengthScore(candidate.Value?.Length ?? 0);
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
                return 25;
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

                string pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\"(?<value>(?:[^\"\\\\]|\\\\.)*)\"";
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

        private sealed class TextCandidate
        {
            public string Key { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public int MatchIndex { get; set; }
        }
    }
}

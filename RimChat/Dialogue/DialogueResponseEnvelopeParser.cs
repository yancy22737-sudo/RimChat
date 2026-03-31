using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.AI;

namespace RimChat.Dialogue
{
    /// <summary>
    /// Dependencies: ModelOutputSanitizer and RPG action parser.
    /// Responsibility: parse model output into a single dialogue envelope with a structured-first contract.
    /// </summary>
    public static class DialogueResponseEnvelopeParser
    {
        private static readonly HashSet<string> AllowedStructuredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "visible_dialogue",
            "actions",
            "meta",
            "debug",
            "dialogue_text"
        };

        public static DialogueResponseEnvelope Parse(string response, DialogueUsageChannel usageChannel)
        {
            string raw = response ?? string.Empty;
            string sanitized = ModelOutputSanitizer.StripReasoningTags(raw).Trim();
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return BuildFailure(raw, "empty_response");
            }

            if (TryParseStructuredEnvelope(raw, sanitized, usageChannel, out DialogueResponseEnvelope structuredEnvelope))
            {
                return structuredEnvelope;
            }

            if (TryParseLegacyEnvelope(raw, sanitized, usageChannel, out DialogueResponseEnvelope legacyEnvelope))
            {
                return legacyEnvelope;
            }

            return BuildFailure(raw, "unsupported_dialogue_contract");
        }

        private static bool TryParseStructuredEnvelope(
            string raw,
            string sanitized,
            DialogueUsageChannel usageChannel,
            out DialogueResponseEnvelope envelope)
        {
            envelope = null;
            string payload = StripMarkdownCodeFence(sanitized);
            if (!LooksLikeSingleJsonObject(payload))
            {
                return false;
            }

            string visibleDialogue = ExtractFirstNonEmptyString(payload, "visible_dialogue", "dialogue_text");
            if (string.IsNullOrWhiteSpace(visibleDialogue))
            {
                envelope = BuildFailure(raw, "missing_visible_dialogue");
                return true;
            }

            List<string> topLevelKeys = ExtractTopLevelKeys(payload);
            if (topLevelKeys.Count == 0)
            {
                envelope = BuildFailure(raw, "invalid_structured_payload");
                return true;
            }

            string unexpectedKey = topLevelKeys.FirstOrDefault(key => !AllowedStructuredKeys.Contains(key));
            if (!string.IsNullOrWhiteSpace(unexpectedKey))
            {
                envelope = BuildFailure(raw, "unexpected_top_level_key_" + unexpectedKey);
                return true;
            }

            bool hasActionsKey = topLevelKeys.Any(key => string.Equals(key, "actions", StringComparison.OrdinalIgnoreCase));
            string actionsJson = hasActionsKey
                ? ExtractTopLevelArray(payload, "actions")
                : string.Empty;
            if (hasActionsKey && string.IsNullOrWhiteSpace(actionsJson))
            {
                envelope = BuildFailure(raw, "invalid_actions_array");
                return true;
            }

            envelope = BuildSuccess(
                raw,
                visibleDialogue,
                actionsJson,
                DialogueResponseProtocolKind.StructuredJson,
                usageChannel);
            return true;
        }

        private static bool TryParseLegacyEnvelope(
            string raw,
            string sanitized,
            DialogueUsageChannel usageChannel,
            out DialogueResponseEnvelope envelope)
        {
            envelope = null;
            ModelOutputSanitizer.SplitVisibleAndTrailingActions(
                sanitized,
                out string visibleDialogue,
                out string actionsJson);

            if (string.IsNullOrWhiteSpace(visibleDialogue))
            {
                return false;
            }

            envelope = BuildSuccess(
                raw,
                visibleDialogue,
                actionsJson,
                DialogueResponseProtocolKind.LegacyText,
                usageChannel);
            return true;
        }

        private static DialogueResponseEnvelope BuildSuccess(
            string raw,
            string visibleDialogue,
            string actionsJson,
            DialogueResponseProtocolKind protocolKind,
            DialogueUsageChannel usageChannel)
        {
            return new DialogueResponseEnvelope
            {
                RawResponse = raw ?? string.Empty,
                VisibleDialogue = (visibleDialogue ?? string.Empty).Trim(),
                ActionsJson = (actionsJson ?? string.Empty).Trim(),
                Actions = usageChannel == DialogueUsageChannel.Rpg
                    ? LLMRpgApiResponse.ParseActionsFromJson(actionsJson)
                    : new List<LLMRpgApiResponse.ApiAction>(),
                IsValid = true,
                FailureReason = string.Empty,
                ProtocolKind = protocolKind
            };
        }

        private static DialogueResponseEnvelope BuildFailure(string raw, string reason)
        {
            return new DialogueResponseEnvelope
            {
                RawResponse = raw ?? string.Empty,
                VisibleDialogue = string.Empty,
                ActionsJson = string.Empty,
                Actions = new List<LLMRpgApiResponse.ApiAction>(),
                IsValid = false,
                FailureReason = reason ?? "invalid_dialogue_contract",
                ProtocolKind = DialogueResponseProtocolKind.Unknown
            };
        }

        private static string StripMarkdownCodeFence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal) ||
                !trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                return trimmed;
            }

            int firstNewLine = trimmed.IndexOf('\n');
            if (firstNewLine < 0 || firstNewLine >= trimmed.Length - 3)
            {
                return trimmed;
            }

            string body = trimmed.Substring(firstNewLine + 1);
            if (body.EndsWith("```", StringComparison.Ordinal))
            {
                body = body.Substring(0, body.Length - 3);
            }

            return body.Trim();
        }

        private static bool LooksLikeSingleJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) ||
                !trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return false;
            }

            return FindMatchingBracket(trimmed, 0, '{', '}') == trimmed.Length - 1;
        }

        private static string ExtractFirstNonEmptyString(string json, params string[] keys)
        {
            if (keys == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < keys.Length; i++)
            {
                string value = ExtractTopLevelString(json, keys[i]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static List<string> ExtractTopLevelKeys(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            bool inString = false;
            bool escaped = false;
            int depth = 0;

            for (int i = 0; i < json.Length; i++)
            {
                char current = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    if (depth == 1)
                    {
                        int stringEnd = FindStringEnd(json, i + 1);
                        if (stringEnd > i)
                        {
                            string key = UnescapeJson(json.Substring(i + 1, stringEnd - i - 1));
                            int cursor = SkipWhitespace(json, stringEnd + 1);
                            if (cursor < json.Length && json[cursor] == ':')
                            {
                                result.Add(key);
                            }
                        }
                    }

                    inString = true;
                    continue;
                }

                if (current == '{')
                {
                    depth++;
                    continue;
                }

                if (current == '}')
                {
                    depth = Math.Max(0, depth - 1);
                }
            }

            return result;
        }

        private static string ExtractTopLevelString(string json, string key)
        {
            int valueStart = FindTopLevelKeyValueStart(json, key);
            if (valueStart < 0 || valueStart >= json.Length || json[valueStart] != '"')
            {
                return string.Empty;
            }

            int stringEnd = FindStringEnd(json, valueStart + 1);
            if (stringEnd <= valueStart)
            {
                return string.Empty;
            }

            return UnescapeJson(json.Substring(valueStart + 1, stringEnd - valueStart - 1));
        }

        private static string ExtractTopLevelArray(string json, string key)
        {
            int valueStart = FindTopLevelKeyValueStart(json, key);
            if (valueStart < 0 || valueStart >= json.Length || json[valueStart] != '[')
            {
                return string.Empty;
            }

            int arrayEnd = FindMatchingBracket(json, valueStart, '[', ']');
            if (arrayEnd <= valueStart)
            {
                return string.Empty;
            }

            return json.Substring(valueStart, arrayEnd - valueStart + 1).Trim();
        }

        private static int FindTopLevelKeyValueStart(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return -1;
            }

            string needle = "\"" + key + "\"";
            bool inString = false;
            bool escaped = false;
            int depth = 0;

            for (int i = 0; i <= json.Length - needle.Length; i++)
            {
                char current = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    if (depth == 1 &&
                        string.Compare(json, i, needle, 0, needle.Length, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        int cursor = SkipWhitespace(json, i + needle.Length);
                        if (cursor >= json.Length || json[cursor] != ':')
                        {
                            continue;
                        }

                        cursor = SkipWhitespace(json, cursor + 1);
                        return cursor < json.Length ? cursor : -1;
                    }

                    inString = true;
                    continue;
                }

                if (current == '{')
                {
                    depth++;
                    continue;
                }

                if (current == '}')
                {
                    depth = Math.Max(0, depth - 1);
                }
            }

            return -1;
        }

        private static int FindMatchingBracket(string text, int start, char open, char close)
        {
            bool inString = false;
            bool escaped = false;
            int depth = 0;

            for (int i = start; i < text.Length; i++)
            {
                char current = text[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
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

                if (current == open)
                {
                    depth++;
                    continue;
                }

                if (current != close)
                {
                    continue;
                }

                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindStringEnd(string text, int start)
        {
            bool escaped = false;
            for (int i = start; i < text.Length; i++)
            {
                char current = text[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    return i;
                }
            }

            return -1;
        }

        private static int SkipWhitespace(string text, int index)
        {
            int cursor = Math.Max(0, index);
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }

            return cursor;
        }

        private static string UnescapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\\"", "\"")
                        .Replace("\\\\", "\\")
                        .Replace("\\n", "\n")
                        .Replace("\\r", "\r")
                        .Replace("\\t", "\t");
        }
    }
}

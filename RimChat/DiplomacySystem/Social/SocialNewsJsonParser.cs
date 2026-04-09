using System;
using System.Linq;
using UnityEngine;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: UnityEngine.JsonUtility.
 /// Responsibility: validate and parse strict JSON payloads for social-circle world news.
 ///</summary>
    internal static class SocialNewsJsonParser
    {
        public static bool TryParse(string response, out SocialNewsDraft draft, out string error, string primaryClaim = "", string quoteAttributionHint = "")
        {
            draft = null;
            error = string.Empty;
            string json = ExtractJsonObject(response);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "missing_json_object";
                return false;
            }

            SocialNewsPayload payload = Deserialize(json, out error);
            if (payload == null)
            {
                return false;
            }

            draft = SanitizeDraft(payload.ToDraft());
            if (!HasRequiredFields(draft))
            {
                draft = null;
                error = "missing_required_fields";
                return false;
            }

            if (!HasExplicitClaim(draft.Headline, allowShortText: false))
            {
                draft = null;
                error = "headline_missing_explicit_claim";
                return false;
            }

            if (!HasExplicitClaim(draft.Quote, allowShortText: true))
            {
                draft = null;
                error = "quote_missing_explicit_claim";
                return false;
            }

            if (string.IsNullOrWhiteSpace(draft.QuoteAttribution))
            {
                draft = null;
                error = "quote_attribution_missing";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(primaryClaim) &&
                !ContainsClaimAnchor(draft.Headline, primaryClaim) &&
                !ContainsClaimAnchor(draft.Quote, primaryClaim))
            {
                draft = null;
                error = "claim_not_reflected";
                return false;
            }

            return true;
        }

        private static SocialNewsPayload Deserialize(string json, out string error)
        {
            error = string.Empty;
            try
            {
                SocialNewsPayload payload = JsonUtility.FromJson<SocialNewsPayload>(json);
                if (payload != null)
                {
                    return payload;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }

            error = "json_deserialize_failed";
            return null;
        }

        private static string ExtractJsonObject(string response)
        {
            string source = NormalizeResponse(response);
            int start = source.IndexOf('{');
            int end = source.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return string.Empty;
            }

            return source.Substring(start, end - start + 1).Trim();
        }

        private static string NormalizeResponse(string response)
        {
            string text = (response ?? string.Empty).Trim();
            if (!text.StartsWith("```", StringComparison.Ordinal))
            {
                return text;
            }

            return text
                .Replace("```json", string.Empty)
                .Replace("```JSON", string.Empty)
                .Replace("```", string.Empty)
                .Trim();
        }

        private static bool HasRequiredFields(SocialNewsDraft draft)
        {
            return !string.IsNullOrWhiteSpace(draft?.Headline)
                && !string.IsNullOrWhiteSpace(draft.Lead)
                && !string.IsNullOrWhiteSpace(draft.Cause)
                && !string.IsNullOrWhiteSpace(draft.Process)
                && !string.IsNullOrWhiteSpace(draft.Outlook);
        }

        private static string TryExtractHeadlineSentence(string value)
        {
            string text = NormalizeField(value);
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            char[] separators = { '。', '！', '？', '.', '!', '?', ';', '；' };
            int index = text.IndexOfAny(separators);
            if (index <= 0)
            {
                return text;
            }

            return text.Substring(0, index).Trim();
        }

        private static string TrimHeadline(string value)
        {
            string text = NormalizeField(value);
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim().Trim('"', '"', '"');
            return trimmed.Length <= 48 ? trimmed : trimmed.Substring(0, 48).Trim();
        }

        private static bool HasExplicitClaim(string value, bool allowShortText)
        {
            string text = NormalizeField(value);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (!allowShortText && text.Length < 8)
            {
                return false;
            }

            string lowered = text.ToLowerInvariant();
            string[] emptyFragments =
            {
                "波澜",
                "发酵",
                "争论",
                "口径",
                "信号",
                "态度",
                "局势",
                "风声",
                "discussion",
                "signal",
                "position",
                "stance",
                "attitude",
                "rumor",
                "tension"
            };
            string[] concreteFragments =
            {
                "要求",
                "主张",
                "表示",
                "宣布",
                "拒绝",
                "支持",
                "反对",
                "停止",
                "继续",
                "允许",
                "禁止",
                "开放",
                "封锁",
                "停火",
                "谈判",
                "贸易",
                "援助",
                "袭击",
                "撤军",
                "增兵",
                "赔偿",
                "合作",
                "结盟",
                "归还",
                "交付",
                "demand",
                "claim",
                "announce",
                "declared",
                "refuse",
                "reject",
                "support",
                "oppose",
                "stop",
                "continue",
                "allow",
                "ban",
                "open",
                "blockade",
                "truce",
                "trade",
                "aid",
                "raid",
                "withdraw",
                "deploy",
                "compensation",
                "cooperate",
                "alliance",
                "return",
                "deliver"
            };

            bool hasConcrete = false;
            foreach (string fragment in concreteFragments)
            {
                if (lowered.Contains(fragment))
                {
                    hasConcrete = true;
                    break;
                }
            }

            if (hasConcrete)
            {
                return true;
            }

            foreach (string fragment in emptyFragments)
            {
                if (lowered.Contains(fragment))
                {
                    return false;
                }
            }

            return text.Contains("：") || text.Contains(":") || text.Contains("\u201C") || text.Contains("\u201D") || text.Contains("将") || text.Contains("会");
        }

        private static bool ContainsClaimAnchor(string text, string primaryClaim)
        {
            string normalizedText = NormalizeAnchorText(text);
            string normalizedClaim = NormalizeAnchorText(primaryClaim);
            if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(normalizedClaim))
            {
                return false;
            }

            if (normalizedText.Contains(normalizedClaim))
            {
                return true;
            }

            string[] anchors = ExtractAnchors(normalizedClaim);
            if (anchors.Length == 0)
            {
                return false;
            }

            return anchors.Any(anchor => normalizedText.Contains(anchor));
        }

        private static string[] ExtractAnchors(string claim)
        {
            if (string.IsNullOrWhiteSpace(claim))
            {
                return Array.Empty<string>();
            }

            var anchors = new System.Collections.Generic.List<string>();
            var current = new System.Text.StringBuilder();
            foreach (char c in claim)
            {
                if (IsAnchorSeparator(c))
                {
                    if (current.Length >= 2)
                    {
                        anchors.Add(current.ToString());
                    }
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length >= 2)
            {
                anchors.Add(current.ToString());
            }

            return anchors.Take(6).ToArray();
        }

        private static bool IsAnchorSeparator(char c)
        {
            return c == ' ' || c == '，' || c == ',' || c == '。' || c == '.' ||
                   c == '：' || c == ':' || c == '；' || c == ';' || c == '、' ||
                   c == '"' || c == '"' || c == '"' || c == '\'' ||
                   c == '！' || c == '!' || c == '？' || c == '?' ||
                   c == '（' || c == '）' || c == '(' || c == ')' ||
                   c == '/' || c == '—' || c == '-';
        }

        private static string NormalizeAnchorText(string value)
        {
            return NormalizeField(value)
                .ToLowerInvariant()
                .Replace("\"", string.Empty)
                .Replace("\u201C", string.Empty)
                .Replace("\u201D", string.Empty)
                .Replace("'", string.Empty)
                .Trim();
        }

        private static SocialNewsDraft SanitizeDraft(SocialNewsDraft draft)
        {
            if (draft == null)
            {
                return null;
            }

            draft.Quote = SanitizeQuote(draft.Quote);
            draft.QuoteAttribution = SanitizeAttribution(draft.QuoteAttribution, draft.Quote);
            draft.LocationName = SanitizeLocationName(draft.LocationName);
            return draft;
        }

        private static string SanitizeQuote(string quote)
        {
            string value = NormalizeField(quote);
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string[] blockedFragments =
            {
                "消息源：",
                "消息来源：",
                "来源：",
                "source:",
                "Source:",
                "公开社交圈转述",
                "内部人士",
                "（公开社交圈转述）",
                "(公开社交圈转述)",
                "（消息源",
                "(source"
            };

            foreach (string fragment in blockedFragments)
            {
                int index = value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    value = value.Substring(0, index).Trim();
                }
            }

            return value.Trim().Trim('"', '"', '"');
        }

        private static string SanitizeAttribution(string attribution, string quote)
        {
            string value = NormalizeField(attribution);
            if (string.IsNullOrWhiteSpace(quote))
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Trim('"', '"', '"');
        }

        private static string SanitizeLocationName(string locationName)
        {
            return NormalizeField(locationName).Trim('"', '"', '"');
        }

        private static string NormalizeField(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r", " ")
                .Trim();
        }

        [Serializable]
        private sealed class SocialNewsPayload
        {
            public string headline = string.Empty;
            public string lead = string.Empty;
            public string cause = string.Empty;
            public string process = string.Empty;
            public string outlook = string.Empty;
            public string quote = string.Empty;
            public string quote_attribution = string.Empty;
            public string narrative_mode = string.Empty;
            public string location_name = string.Empty;

            public SocialNewsDraft ToDraft()
            {
                return new SocialNewsDraft
                {
                    Headline = NormalizeField(headline),
                    Lead = NormalizeField(lead),
                    Cause = NormalizeField(cause),
                    Process = NormalizeField(process),
                    Outlook = NormalizeField(outlook),
                    Quote = NormalizeField(quote),
                    QuoteAttribution = NormalizeField(quote_attribution),
                    NarrativeMode = NormalizeField(narrative_mode),
                    LocationName = NormalizeField(location_name)
                };
            }

        }
    }
}

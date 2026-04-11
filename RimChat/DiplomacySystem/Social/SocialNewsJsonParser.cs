using System;
using System.Collections.Generic;
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

        private static bool ContainsClaimAnchor(string text, string primaryClaim)
        {
            string normalizedText = NormalizeAnchorText(text);
            string normalizedClaim = NormalizeAnchorText(primaryClaim);
            if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(normalizedClaim))
            {
                return false;
            }

            // Exact substring match shortcut
            if (normalizedText.Contains(normalizedClaim))
            {
                return true;
            }

            // For very short claims, require exact substring match
            if (normalizedClaim.Length < 4)
            {
                return normalizedText.Contains(normalizedClaim);
            }

            // Bigram overlap: compare 2-char sliding windows between claim and text.
            // This tolerates AI paraphrasing where exact phrases differ but key
            // entities/actions overlap (e.g. "萨托尔警告" → "萨托尔部落发出…通牒").
            HashSet<string> claimBigrams = ExtractBigrams(normalizedClaim);
            HashSet<string> textBigrams = ExtractBigrams(normalizedText);
            if (claimBigrams.Count == 0)
            {
                return false;
            }

            int overlap = 0;
            foreach (string bg in claimBigrams)
            {
                if (textBigrams.Contains(bg))
                {
                    overlap++;
                    if (overlap >= 2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static HashSet<string> ExtractBigrams(string text)
        {
            var bigrams = new HashSet<string>();
            if (string.IsNullOrEmpty(text) || text.Length < 2)
            {
                return bigrams;
            }

            for (int i = 0; i <= text.Length - 2; i++)
            {
                bigrams.Add(text.Substring(i, 2));
            }

            return bigrams;
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

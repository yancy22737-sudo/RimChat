using System;
using UnityEngine;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: UnityEngine.JsonUtility.
 /// Responsibility: validate and parse strict JSON payloads for social-circle world news.
 ///</summary>
    internal static class SocialNewsJsonParser
    {
        public static bool TryParse(string response, out SocialNewsDraft draft, out string error)
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

            draft = payload.ToDraft();
            if (HasRequiredFields(draft))
            {
                return true;
            }

            draft = null;
            error = "missing_required_fields";
            return false;
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
                    QuoteAttribution = NormalizeField(quote_attribution)
                };
            }

            private static string NormalizeField(string value)
            {
                return (value ?? string.Empty)
                    .Replace("\r", " ")
                    .Trim();
            }
        }
    }
}

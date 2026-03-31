using System.Collections.Generic;
using System.Text;
using RimChat.AI;

namespace RimChat.Dialogue
{
    public enum DialogueResponseProtocolKind
    {
        Unknown = 0,
        StructuredJson = 1,
        LegacyText = 2
    }

    /// <summary>
    /// Stage-A parsed response envelope. UI/action mutation happens in stage B only.
    /// </summary>
    public sealed class DialogueResponseEnvelope
    {
        public string RawResponse { get; set; }
        public string VisibleDialogue { get; set; }
        public string ActionsJson { get; set; }
        public List<LLMRpgApiResponse.ApiAction> Actions { get; set; } = new List<LLMRpgApiResponse.ApiAction>();
        public bool IsStaleDropped { get; set; }
        public string DropReason { get; set; }
        public bool IsValid { get; set; }
        public string FailureReason { get; set; }
        public DialogueResponseProtocolKind ProtocolKind { get; set; }

        public string DialogueText
        {
            get => VisibleDialogue ?? string.Empty;
            set => VisibleDialogue = value ?? string.Empty;
        }

        public string ToLegacyText()
        {
            return ModelOutputSanitizer.ComposeVisibleAndTrailingActions(
                VisibleDialogue ?? string.Empty,
                ActionsJson ?? string.Empty);
        }

        public string ToStructuredResponseText()
        {
            string visibleDialogue = EscapeJsonString(VisibleDialogue ?? string.Empty);
            string actionsJson = string.IsNullOrWhiteSpace(ActionsJson)
                ? string.Empty
                : (ActionsJson ?? string.Empty).Trim();

            var builder = new StringBuilder();
            builder.Append("{\"visible_dialogue\":\"");
            builder.Append(visibleDialogue);
            builder.Append("\"");
            if (!string.IsNullOrWhiteSpace(actionsJson))
            {
                builder.Append(",\"actions\":");
                builder.Append(actionsJson);
            }

            builder.Append("}");
            return builder.ToString();
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                switch (current)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(current))
                        {
                            builder.Append("\\u");
                            builder.Append(((int)current).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(current);
                        }

                        break;
                }
            }

            return builder.ToString();
        }
    }
}

using System;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: none.
    /// Responsibility: classify prompt render failures with template/channel/position metadata.
    /// </summary>
    internal enum PromptRenderErrorCode
    {
        ParseError = 1000,
        RuntimeError = 1100,
        UnknownVariable = 1101,
        NullObjectAccess = 1102,
        TemplateBlocked = 1200
    }

    /// <summary>
    /// Dependencies: PromptRenderErrorCode.
    /// Responsibility: carry structured prompt diagnostics for UI and logs.
    /// </summary>
    internal sealed class PromptRenderDiagnostic
    {
        public PromptRenderErrorCode ErrorCode { get; set; } = PromptRenderErrorCode.RuntimeError;
        public string Message { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
    }

    /// <summary>
    /// Dependencies: PromptRenderDiagnostic.
    /// Responsibility: terminate prompt rendering without fallback.
    /// </summary>
    internal sealed class PromptRenderException : Exception
    {
        public string TemplateId { get; }
        public string Channel { get; }
        public int ErrorLine { get; }
        public int ErrorColumn { get; }
        public PromptRenderErrorCode ErrorCode { get; }

        public PromptRenderException(
            string templateId,
            string channel,
            PromptRenderDiagnostic diagnostic,
            Exception innerException = null)
            : base(ComposeMessage(templateId, channel, diagnostic), innerException)
        {
            TemplateId = templateId ?? string.Empty;
            Channel = channel ?? string.Empty;
            ErrorCode = diagnostic?.ErrorCode ?? PromptRenderErrorCode.RuntimeError;
            ErrorLine = diagnostic?.Line ?? 0;
            ErrorColumn = diagnostic?.Column ?? 0;
        }

        private static string ComposeMessage(
            string templateId,
            string channel,
            PromptRenderDiagnostic diagnostic)
        {
            string id = string.IsNullOrWhiteSpace(templateId) ? "unknown" : templateId.Trim();
            string ch = string.IsNullOrWhiteSpace(channel) ? "unknown" : channel.Trim();
            string message = diagnostic?.Message ?? "Prompt render failed.";
            int line = diagnostic?.Line ?? 0;
            int column = diagnostic?.Column ?? 0;
            int code = (int)(diagnostic?.ErrorCode ?? PromptRenderErrorCode.RuntimeError);
            return $"[TemplateID:{id}] [Channel:{ch}] [Line:{line}] [Column:{column}] [ErrorCode:{code}] {message}";
        }
    }
}

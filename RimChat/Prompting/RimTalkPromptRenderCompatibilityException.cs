using System;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: native RimTalk render diagnostics.
    /// Responsibility: signal native RimTalk render compatibility failures that must fail fast.
    /// </summary>
    internal sealed class RimTalkPromptRenderCompatibilityException : Exception
    {
        public RimTalkNativeRenderDiagnostic Diagnostic { get; }

        public RimTalkPromptRenderCompatibilityException(string message, RimTalkNativeRenderDiagnostic diagnostic)
            : base(message)
        {
            Diagnostic = diagnostic;
        }
    }
}

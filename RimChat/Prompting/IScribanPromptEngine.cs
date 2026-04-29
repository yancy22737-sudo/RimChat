using System.Collections.Generic;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: PromptRenderContext and PromptRenderException.
    /// Responsibility: render Scriban templates in strict mode with deterministic diagnostics.
    /// </summary>
    internal interface IScribanPromptEngine
    {
        string RenderOrThrow(
            string templateId,
            string channel,
            string templateText,
            PromptRenderContext context);

        string RenderLenient(
            string templateId,
            string channel,
            string templateText,
            PromptRenderContext context);

        void ValidateOrThrow(
            string templateId,
            string channel,
            string templateText,
            PromptRenderContext context);

        PromptRenderContext BuildValidationContext(
            string templateId,
            string channel,
            IEnumerable<string> variablePaths);
    }
}

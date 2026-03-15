using System;
using System.Collections.Generic;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: IScribanPromptEngine implementation.
    /// Responsibility: expose strict template rendering entrypoints.
    /// </summary>
    internal static class PromptTemplateRenderer
    {
        private static readonly IScribanPromptEngine Engine = ScribanPromptEngine.Instance;

        public static string RenderOrThrow(
            string templateId,
            string channel,
            string templateText,
            PromptRenderContext context)
        {
            return Engine.RenderOrThrow(templateId, channel, templateText, context);
        }

        public static PromptRenderContext BuildValidationContext(
            string templateId,
            string channel,
            IEnumerable<string> variables)
        {
            return Engine.BuildValidationContext(templateId, channel, variables);
        }

        public static void ValidateOrThrow(
            string templateId,
            string channel,
            string templateText,
            PromptRenderContext context)
        {
            Engine.ValidateOrThrow(templateId, channel, templateText, context);
        }
    }
}

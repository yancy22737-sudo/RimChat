using System;
using System.Collections.Generic;
using System.Linq;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: IScribanPromptEngine implementation.
    /// Responsibility: expose strict template rendering entrypoints for legacy callers.
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

        public static string Render(
            string templateId,
            string channel,
            string templateText,
            IReadOnlyDictionary<string, object> variables)
        {
            PromptRenderContext context = PromptRenderContext.Create(templateId, channel);
            context.SetValues(variables);
            return Engine.RenderOrThrow(templateId, channel, templateText, context);
        }

        public static string Render(
            string templateText,
            IReadOnlyDictionary<string, string> variables)
        {
            var values = variables?.ToDictionary(
                pair => pair.Key,
                pair => (object)pair.Value,
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            return Render("adhoc.template", "unknown", templateText, values);
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

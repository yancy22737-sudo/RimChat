using System;
using System.Collections.Generic;
using System.Linq;
using Scriban;
using Scriban.Runtime;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: Scriban template parser/runtime.
    /// Responsibility: parse and render prompt templates with strict failure semantics.
    /// </summary>
    internal sealed class ScribanPromptEngine : IScribanPromptEngine
    {
        public static ScribanPromptEngine Instance { get; } = new ScribanPromptEngine();

        private ScribanPromptEngine()
        {
        }

        public string RenderOrThrow(
            string templateId,
            string channel,
            string templateText,
            PromptRenderContext context)
        {
            if (PromptTemplateBlockRegistry.TryGetReason(templateId, channel, out string reason))
            {
                throw BuildException(
                    templateId,
                    channel,
                    PromptRenderErrorCode.TemplateBlocked,
                    reason,
                    0,
                    0);
            }

            Template template = ParseOrThrow(templateId, channel, templateText);
            TemplateContext runtimeContext = BuildTemplateContext(context);
            try
            {
                string rendered = template.Render(runtimeContext);
                return rendered?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw BuildRuntimeException(templateId, channel, ex);
            }
        }

        public void ValidateOrThrow(
            string templateId,
            string channel,
            string templateText,
            PromptRenderContext context)
        {
            ParseOrThrow(templateId, channel, templateText);
        }

        public PromptRenderContext BuildValidationContext(
            string templateId,
            string channel,
            IEnumerable<string> variablePaths)
        {
            var context = PromptRenderContext.Create(templateId, channel);
            if (variablePaths == null)
            {
                return context;
            }

            foreach (string path in variablePaths.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                context.SetValue(path, "validation");
            }

            return context;
        }

        private static Template ParseOrThrow(string templateId, string channel, string templateText)
        {
            string source = templateText ?? string.Empty;
            Template template = Template.Parse(source);
            if (!template.HasErrors)
            {
                return template;
            }

            object first = template.Messages?.FirstOrDefault();
            string message = first?.ToString() ?? "Scriban parse failed.";
            (int line, int column) = ExtractPosition(first, "Span");
            throw BuildException(templateId, channel, PromptRenderErrorCode.ParseError, message, line, column);
        }

        private static TemplateContext BuildTemplateContext(PromptRenderContext context)
        {
            var templateContext = new TemplateContext
            {
                StrictVariables = true,
                EnableRelaxedFunctionAccess = false,
                EnableRelaxedIndexerAccess = false,
                EnableRelaxedMemberAccess = false,
                EnableRelaxedTargetAccess = false
            };
            templateContext.PushGlobal(context?.Root ?? PromptRenderContext.Create("adhoc", "unknown").Root);
            return templateContext;
        }

        private static PromptRenderException BuildRuntimeException(
            string templateId,
            string channel,
            Exception exception)
        {
            PromptRenderErrorCode code = ResolveRuntimeCode(exception?.Message);
            (int line, int column) = ExtractPosition(exception, "Span");
            string message = exception?.Message ?? "Scriban render failed.";
            return BuildException(templateId, channel, code, message, line, column, exception);
        }

        private static PromptRenderErrorCode ResolveRuntimeCode(string message)
        {
            string lower = (message ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("null"))
            {
                return PromptRenderErrorCode.NullObjectAccess;
            }

            if ((lower.Contains("variable") || lower.Contains("member")) &&
                (lower.Contains("not found") || lower.Contains("does not exist")))
            {
                return PromptRenderErrorCode.UnknownVariable;
            }

            return PromptRenderErrorCode.RuntimeError;
        }

        private static (int line, int column) ExtractPosition(object value, string spanPropertyName)
        {
            if (value == null)
            {
                return (0, 0);
            }

            try
            {
                object span = value.GetType().GetProperty(spanPropertyName)?.GetValue(value, null);
                if (span == null)
                {
                    return (0, 0);
                }

                object start = span.GetType().GetProperty("Start")?.GetValue(span, null);
                if (start == null)
                {
                    return (0, 0);
                }

                int rawLine = ReadIntProperty(start, "Line");
                int rawColumn = ReadIntProperty(start, "Column");
                return (Math.Max(1, rawLine), Math.Max(1, rawColumn));
            }
            catch
            {
                return (0, 0);
            }
        }

        private static int ReadIntProperty(object target, string propertyName)
        {
            if (target == null)
            {
                return 0;
            }

            object raw = target.GetType().GetProperty(propertyName)?.GetValue(target, null);
            if (raw == null)
            {
                return 0;
            }

            return raw is int value ? value : 0;
        }

        private static PromptRenderException BuildException(
            string templateId,
            string channel,
            PromptRenderErrorCode code,
            string message,
            int line,
            int column,
            Exception inner = null)
        {
            return new PromptRenderException(
                templateId,
                channel,
                new PromptRenderDiagnostic
                {
                    ErrorCode = code,
                    Message = message ?? string.Empty,
                    Line = Math.Max(0, line),
                    Column = Math.Max(0, column)
                },
                inner);
        }
    }
}

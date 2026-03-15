using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Scriban;
using Scriban.Runtime;
using Verse;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: Scriban template parser/runtime.
    /// Responsibility: parse and render prompt templates with strict failure semantics.
    /// </summary>
    internal sealed class ScribanPromptEngine : IScribanPromptEngine
    {
        private const int TemplateCacheCapacity = 128;
        private const long TelemetryLogEveryRenderCount = 200;
        private static readonly PromptTemplateCache TemplateCache = new PromptTemplateCache(TemplateCacheCapacity);
        private static readonly object RuntimeProbeLock = new object();
        private static bool RuntimeProbeLogged;

        public static ScribanPromptEngine Instance { get; } = new ScribanPromptEngine();

        private ScribanPromptEngine()
        {
        }

        public static PromptRenderTelemetrySnapshot GetTelemetrySnapshot()
        {
            return PromptRenderTelemetry.CaptureSnapshot();
        }

        public string RenderOrThrow(
            string templateId,
            string channel,
            string templateText,
            PromptRenderContext context)
        {
            EnsureRuntimeProbeLogged();
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

            Template template = ResolveTemplateOrThrow(templateId, channel, templateText);
            TemplateContext runtimeContext = BuildTemplateContext(context);
            long startedTicks = Stopwatch.GetTimestamp();
            try
            {
                string rendered = template.Render(runtimeContext);
                return rendered?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw BuildRuntimeException(templateId, channel, ex);
            }
            finally
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - startedTicks;
                long renderCount = PromptRenderTelemetry.RecordRenderTicks(elapsedTicks);
                TryWriteTelemetryLog(renderCount);
            }
        }

        public void ValidateOrThrow(
            string templateId,
            string channel,
            string templateText,
            PromptRenderContext context)
        {
            ResolveTemplateOrThrow(templateId, channel, templateText);
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

        private static Template ResolveTemplateOrThrow(string templateId, string channel, string templateText)
        {
            string source = templateText ?? string.Empty;
            string cacheKey = BuildCacheKey(templateId, channel, source);
            if (TemplateCache.TryGet(cacheKey, out Template cached))
            {
                PromptRenderTelemetry.RecordCacheHit();
                return cached;
            }

            PromptRenderTelemetry.RecordCacheMiss();
            long startedTicks = Stopwatch.GetTimestamp();
            Template parsed = ParseTemplateOrThrow(templateId, channel, source);
            PromptRenderTelemetry.RecordParseTicks(Stopwatch.GetTimestamp() - startedTicks);
            if (TemplateCache.Set(cacheKey, parsed))
            {
                PromptRenderTelemetry.RecordCacheEviction();
            }

            return parsed;
        }

        private static string BuildCacheKey(string templateId, string channel, string source)
        {
            return (templateId ?? string.Empty) + "|" + (channel ?? string.Empty) + "|" + (source ?? string.Empty);
        }

        private static Template ParseTemplateOrThrow(string templateId, string channel, string source)
        {
            Template template = Template.Parse(source);
            if (!template.HasErrors)
            {
                return template;
            }

            string message = "Scriban parse failed.";
            int line = 0;
            int column = 0;
            TryExtractParseDiagnostic(template, ref message, ref line, ref column);
            throw BuildException(
                templateId,
                channel,
                PromptRenderErrorCode.ParseError,
                message,
                line,
                column);
        }

        private static void TryExtractParseDiagnostic(Template template, ref string message, ref int line, ref int column)
        {
            if (template == null)
            {
                return;
            }

            try
            {
                PropertyInfo messagesProperty = template.GetType().GetProperty("Messages");
                object messages = messagesProperty?.GetValue(template, null);
                if (!(messages is System.Collections.IEnumerable enumerable))
                {
                    return;
                }

                foreach (object item in enumerable)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    message = item.ToString() ?? message;
                    (line, column) = ExtractPosition(item, "Span");
                    return;
                }
            }
            catch
            {
                // Keep default parse message when runtime API differs.
            }
        }

        private static void TryWriteTelemetryLog(long renderCount)
        {
            if (renderCount <= 0 || renderCount % TelemetryLogEveryRenderCount != 0)
            {
                return;
            }

            PromptRenderTelemetrySnapshot snapshot = PromptRenderTelemetry.CaptureSnapshot();
            Log.Message(
                $"[RimChat] Scriban telemetry: hit={snapshot.CacheHitRatePercent:F1}% " +
                $"hits={snapshot.CacheHits} misses={snapshot.CacheMisses} evictions={snapshot.CacheEvictions} " +
                $"avg_parse_ms={snapshot.AverageParseMilliseconds:F3} avg_render_ms={snapshot.AverageRenderMilliseconds:F3}");
        }

        private static TemplateContext BuildTemplateContext(PromptRenderContext context)
        {
            var templateContext = new TemplateContext();
            TrySetTemplateContextBool(templateContext, "StrictVariables", true);
            TrySetTemplateContextBool(templateContext, "EnableRelaxedFunctionAccess", false);
            TrySetTemplateContextBool(templateContext, "EnableRelaxedIndexerAccess", false);
            TrySetTemplateContextBool(templateContext, "EnableRelaxedMemberAccess", false);
            TrySetTemplateContextBool(templateContext, "EnableRelaxedTargetAccess", false);
            templateContext.PushGlobal(context?.Root ?? PromptRenderContext.Create("adhoc", "unknown").Root);
            return templateContext;
        }

        private static bool TrySetTemplateContextBool(TemplateContext context, string propertyName, bool value)
        {
            if (context == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            try
            {
                var property = context.GetType().GetProperty(propertyName);
                if (property == null || !property.CanWrite || property.PropertyType != typeof(bool))
                {
                    return false;
                }

                property.SetValue(context, value, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureRuntimeProbeLogged()
        {
            if (RuntimeProbeLogged)
            {
                return;
            }

            lock (RuntimeProbeLock)
            {
                if (RuntimeProbeLogged)
                {
                    return;
                }

                Assembly assembly = typeof(Template).Assembly;
                string path = string.IsNullOrWhiteSpace(assembly?.Location) ? "<unknown>" : assembly.Location;
                string version = assembly?.GetName()?.Version?.ToString() ?? "unknown";
                bool hasStrict = HasWritableBoolProperty(typeof(TemplateContext), "StrictVariables");
                bool hasRelaxedFn = HasWritableBoolProperty(typeof(TemplateContext), "EnableRelaxedFunctionAccess");
                bool hasRelaxedIndexer = HasWritableBoolProperty(typeof(TemplateContext), "EnableRelaxedIndexerAccess");
                bool hasRelaxedMember = HasWritableBoolProperty(typeof(TemplateContext), "EnableRelaxedMemberAccess");
                bool hasRelaxedTarget = HasWritableBoolProperty(typeof(TemplateContext), "EnableRelaxedTargetAccess");
                bool hasMessageBag = typeof(Template).Assembly.GetType("Scriban.LogMessageBag", false) != null;

                Log.Message(
                    $"[RimChat] Scriban runtime probe: version={version} path={path} " +
                    $"strict={hasStrict} relaxed_fn={hasRelaxedFn} relaxed_indexer={hasRelaxedIndexer} " +
                    $"relaxed_member={hasRelaxedMember} relaxed_target={hasRelaxedTarget} message_bag={hasMessageBag}");

                if (!hasStrict || !hasRelaxedFn || !hasRelaxedIndexer || !hasRelaxedMember || !hasRelaxedTarget)
                {
                    Log.Warning(
                        "[RimChat] Scriban runtime API differs from expected surface. " +
                        "Using compatibility-safe configuration path to preserve strict render behavior.");
                }

                RuntimeProbeLogged = true;
            }
        }

        private static bool HasWritableBoolProperty(Type type, string propertyName)
        {
            if (type == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            try
            {
                PropertyInfo property = type.GetProperty(propertyName);
                return property != null && property.CanWrite && property.PropertyType == typeof(bool);
            }
            catch
            {
                return false;
            }
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

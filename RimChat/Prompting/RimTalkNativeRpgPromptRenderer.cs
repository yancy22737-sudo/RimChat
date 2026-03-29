using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimChat.Config;
using RimChat.Memory;
using RimChat.Persistence;
using RimWorld;
using Verse;

namespace RimChat.Prompting
{
    internal sealed class RimTalkNativeRenderDiagnostic
    {
        public string BoundMethod = string.Empty;
        public string BoundMethodVariant = string.Empty;
        public string PromptChannel = string.Empty;
        public string CurrentPawnLabel = string.Empty;
        public int PawnCount;
        public int AllPawnCount;
        public int ScopedPawnIndex = -1;
        public bool ContextBuilt;
        public bool IsCompatibilityFailure;
        public string FailureStage = string.Empty;
        public string ErrorMessage = string.Empty;
        public int RemainingTokenCount;
        public string RemainingTokensPreview = string.Empty;
    }

    /// <summary>
    /// Dependencies: RimTalk runtime types loaded by RimWorld.
    /// Responsibility: run the final RPG prompt text through RimTalk's native Scriban parser.
    /// </summary>
    internal static class RimTalkNativeRpgPromptRenderer
    {
        private const BindingFlags StaticPublic = BindingFlags.Static | BindingFlags.Public;
        private const BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;
        private const long DuplicateFailureLogCooldownMs = 15000;
        private static readonly Regex RemainingTokenRegex = new Regex(@"\{\{\s*[^}]+\s*\}\}", RegexOptions.Compiled);
        private static readonly Regex PawnTokenRegex = new Regex(@"\{\{\s*pawn\.", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RimTalkNamespaceTokenRegex = new Regex(
            @"\{\{\s*(?:pawn|dialogue|world|system)\.rimtalk\.[^}]+\}\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LegacyRimTalkTokenRegex = new Regex(
            @"\{\{\s*(?:context|prompt|chat\.history|chat\.history_simplified|json\.format)\s*\}\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RimChatOwnVariableRegex = new Regex(
            @"\{\{\s*(?:dialogue|world|system)\.(?!rimtalk\.)[^}]+\}\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Dictionary<string, long> failureLogTicksBySignature =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly object renderMethodBindLock = new object();
        private static MethodInfo cachedRenderMethod;
        private static string cachedRenderMethodVariant = string.Empty;
        private static bool renderMethodResolved;
        private static string renderMethodBindError = string.Empty;

        public static bool TryRenderRpgPrompt(
            string promptText,
            string promptChannel,
            DialogueScenarioContext scenarioContext,
            out string rendered,
            out RimTalkNativeRenderDiagnostic diagnostic)
        {
            rendered = promptText ?? string.Empty;
            diagnostic = new RimTalkNativeRenderDiagnostic();
            diagnostic.PromptChannel = ResolvePromptChannel(promptChannel, scenarioContext);

            if (string.IsNullOrWhiteSpace(promptText) || scenarioContext?.IsRpg != true)
            {
                return false;
            }

            return TryRenderWithNativeScriban(promptText, scenarioContext, diagnostic, out rendered);
        }

        public static bool TryRenderDiplomacyPrompt(
            string promptText,
            string promptChannel,
            DialogueScenarioContext scenarioContext,
            out string rendered,
            out RimTalkNativeRenderDiagnostic diagnostic)
        {
            rendered = promptText ?? string.Empty;
            diagnostic = new RimTalkNativeRenderDiagnostic();
            diagnostic.PromptChannel = ResolvePromptChannel(promptChannel, scenarioContext);

            if (string.IsNullOrWhiteSpace(promptText) || scenarioContext == null)
            {
                return false;
            }

            return TryRenderWithNativeScriban(promptText, scenarioContext, diagnostic, out rendered);
        }

        private static bool TryRenderWithNativeScriban(
            string promptText,
            DialogueScenarioContext scenarioContext,
            RimTalkNativeRenderDiagnostic diagnostic,
            out string rendered)
        {
            if (!TryResolveRenderMethod(out MethodInfo renderMethod, out string methodVariant, out string bindError))
            {
                diagnostic.IsCompatibilityFailure = true;
                diagnostic.FailureStage = "resolve_render_method";
                diagnostic.ErrorMessage = string.IsNullOrWhiteSpace(bindError)
                    ? "Missing compatible RimTalk.Prompt.ScribanParser.Render."
                    : bindError;
                rendered = CleanInvalidRimTalkTokens(promptText);
                diagnostic.RemainingTokenCount = CountRemainingTokens(rendered);
                if (diagnostic.RemainingTokenCount > 0)
                {
                    diagnostic.RemainingTokensPreview = BuildRemainingTokenPreview(rendered);
                }

                LogFailure(diagnostic);
                return false;
            }

            if (HasUnresolvableTokens(promptText))
            {
                diagnostic.ErrorMessage = "Prompt contains unresolvable tokens; skipping native render for fail-safe.";
                rendered = CleanInvalidRimTalkTokens(promptText);
                LogFailure(diagnostic);
                return false;
            }

            diagnostic.BoundMethod = BuildMethodSignature(renderMethod);
            diagnostic.BoundMethodVariant = methodVariant ?? string.Empty;
            PawnBindingSpec binding = ResolvePawnBindingSpec(scenarioContext, diagnostic);
            object promptContext = BuildPromptContext(promptText, scenarioContext, binding, diagnostic);
            diagnostic.ContextBuilt = promptContext != null;
            if (promptContext == null)
            {
                diagnostic.ErrorMessage = AppendError(
                    diagnostic.ErrorMessage,
                    "PromptContext build failed.");
                rendered = promptText;
                LogFailure(diagnostic);
                return false;
            }

            try
            {
                if (!TryInvokeRenderMethod(renderMethod, promptText, promptContext, out string nativeRendered, out string invokeError))
                {
                    diagnostic.ErrorMessage = AppendError(
                        diagnostic.ErrorMessage,
                        "Scriban invoke failed: " + invokeError);
                    rendered = CleanInvalidRimTalkTokens(promptText);
                    LogFailure(diagnostic);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(nativeRendered))
                {
                    diagnostic.ErrorMessage = "Native RimTalk render returned empty text.";
                    rendered = CleanInvalidRimTalkTokens(promptText);
                    LogFailure(diagnostic);
                    return false;
                }

                diagnostic.RemainingTokenCount = CountRemainingTokens(nativeRendered);
                if (diagnostic.RemainingTokenCount > 0)
                {
                    diagnostic.RemainingTokensPreview = BuildRemainingTokenPreview(nativeRendered);
                    if (ShouldEmitFailureLog(diagnostic, "remaining_tokens"))
                    {
                        Log.Warning(
                            "[RimChat] RimTalk native render completed with remaining tokens. " +
                            $"channel={diagnostic.PromptChannel}, current_pawn={diagnostic.CurrentPawnLabel}, " +
                            $"pawn_count={diagnostic.PawnCount}, all_pawn_count={diagnostic.AllPawnCount}, " +
                            $"scoped_pawn_index={diagnostic.ScopedPawnIndex}, " +
                            $"bound_method={diagnostic.BoundMethod}, context_built={diagnostic.ContextBuilt}, " +
                            $"remaining_tokens={diagnostic.RemainingTokenCount}, " +
                            $"remaining_preview={diagnostic.RemainingTokensPreview}.");
                    }
                    rendered = CleanInvalidRimTalkTokens(nativeRendered);
                    return true;
                }

                rendered = nativeRendered;
                return true;
            }
            catch (Exception ex)
            {
                diagnostic.ErrorMessage = AppendError(
                    diagnostic.ErrorMessage,
                    "Scriban render failed: " + ex.GetBaseException().Message);
                rendered = CleanInvalidRimTalkTokens(promptText);
                LogFailure(diagnostic);
                return false;
            }
        }

        private static bool TryResolveRenderMethod(
            out MethodInfo renderMethod,
            out string methodVariant,
            out string error)
        {
            if (renderMethodResolved)
            {
                renderMethod = cachedRenderMethod;
                methodVariant = cachedRenderMethodVariant;
                error = renderMethodBindError;
                return renderMethod != null;
            }

            lock (renderMethodBindLock)
            {
                if (!renderMethodResolved)
                {
                    ResolveRenderMethodCore(
                        out cachedRenderMethod,
                        out cachedRenderMethodVariant,
                        out renderMethodBindError);
                    renderMethodResolved = true;
                }
            }

            renderMethod = cachedRenderMethod;
            methodVariant = cachedRenderMethodVariant;
            error = renderMethodBindError;
            return renderMethod != null;
        }

        private static void ResolveRenderMethodCore(
            out MethodInfo renderMethod,
            out string methodVariant,
            out string error)
        {
            renderMethod = null;
            methodVariant = string.Empty;
            error = string.Empty;

            Type scribanParserType = AccessTools.TypeByName("RimTalk.Prompt.ScribanParser");
            if (scribanParserType == null)
            {
                error = "Missing type RimTalk.Prompt.ScribanParser.";
                return;
            }

            Type promptContextType = AccessTools.TypeByName("RimTalk.Prompt.PromptContext");
            MethodInfo[] candidates = scribanParserType.GetMethods(StaticPublic)
                .Where(method => string.Equals(method.Name, "Render", StringComparison.Ordinal))
                .ToArray();
            foreach (MethodInfo candidate in candidates)
            {
                if (TryBuildRenderVariant(candidate, promptContextType, out string variant))
                {
                    renderMethod = candidate;
                    methodVariant = variant;
                    return;
                }
            }

            if (candidates.Length == 0)
            {
                error = "Missing RimTalk.Prompt.ScribanParser.Render.";
                return;
            }

            string signatures = string.Join(" | ", candidates.Select(BuildMethodSignature));
            error = "No compatible ScribanParser.Render signature. available=" + signatures;
        }

        private static bool TryInvokeRenderMethod(
            MethodInfo renderMethod,
            string promptText,
            object promptContext,
            out string rendered,
            out string error)
        {
            rendered = string.Empty;
            error = string.Empty;
            if (renderMethod == null)
            {
                error = "Render method is null.";
                return false;
            }

            if (!TryBuildRenderArguments(renderMethod, promptText, promptContext, out object[] args))
            {
                error = "Unsupported Render argument layout: " + BuildMethodSignature(renderMethod);
                return false;
            }

            object raw = renderMethod.Invoke(null, args);
            rendered = raw?.ToString() ?? string.Empty;
            return true;
        }

        private static bool TryBuildRenderArguments(
            MethodInfo method,
            string promptText,
            object promptContext,
            out object[] args)
        {
            args = null;
            ParameterInfo[] parameters = method?.GetParameters() ?? Array.Empty<ParameterInfo>();
            if (parameters.Length < 2 || parameters.Length > 3)
            {
                return false;
            }

            args = new object[parameters.Length];
            bool assignedText = false;
            bool assignedContext = false;
            bool assignedBool = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                Type parameterType = parameters[i].ParameterType;
                if (parameterType == typeof(string) && !assignedText)
                {
                    args[i] = promptText ?? string.Empty;
                    assignedText = true;
                    continue;
                }

                if (parameterType == typeof(bool) && !assignedBool)
                {
                    args[i] = true;
                    assignedBool = true;
                    continue;
                }

                if (!assignedContext && IsPromptContextParameter(parameterType, promptContext))
                {
                    args[i] = promptContext;
                    assignedContext = true;
                    continue;
                }

                return false;
            }

            if (!assignedText || !assignedContext)
            {
                return false;
            }

            return parameters.Length != 3 || assignedBool;
        }

        private static bool TryBuildRenderVariant(MethodInfo method, Type promptContextType, out string variant)
        {
            variant = string.Empty;
            ParameterInfo[] parameters = method?.GetParameters() ?? Array.Empty<ParameterInfo>();
            if (parameters.Length < 2 || parameters.Length > 3)
            {
                return false;
            }

            bool hasString = parameters.Any(parameter => parameter.ParameterType == typeof(string));
            bool hasContext = parameters.Any(parameter => IsPromptContextParameter(parameter.ParameterType, promptContextType));
            bool boolOk = parameters.Length != 3 || parameters.Any(parameter => parameter.ParameterType == typeof(bool));
            if (!hasString || !hasContext || !boolOk)
            {
                return false;
            }

            variant = parameters.Length == 3 ? "render_v3" : "render_v2";
            return true;
        }

        private static bool IsPromptContextParameter(Type parameterType, object promptContext)
        {
            if (parameterType == null)
            {
                return false;
            }

            if (promptContext != null && parameterType.IsInstanceOfType(promptContext))
            {
                return true;
            }

            return parameterType.FullName?.IndexOf(
                "RimTalk.Prompt.PromptContext",
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPromptContextParameter(Type parameterType, Type promptContextType)
        {
            if (parameterType == null)
            {
                return false;
            }

            if (promptContextType != null && parameterType.IsAssignableFrom(promptContextType))
            {
                return true;
            }

            return parameterType.FullName?.IndexOf(
                "RimTalk.Prompt.PromptContext",
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasUnresolvableTokens(string promptText)
        {
            if (string.IsNullOrWhiteSpace(promptText) || !promptText.Contains("{{"))
            {
                return false;
            }

            if (RemainingTokenRegex.IsMatch(promptText))
            {
                string preview = BuildRemainingTokenPreview(promptText);
                if (preview.Contains("runtime.") || preview.Contains("system.custom."))
                {
                    return true;
                }
            }

            return false;
        }

        private static object BuildPromptContext(
            string promptText,
            DialogueScenarioContext scenarioContext,
            PawnBindingSpec binding,
            RimTalkNativeRenderDiagnostic diagnostic)
        {
            Type promptContextType = AccessTools.TypeByName("RimTalk.Prompt.PromptContext");
            if (promptContextType == null)
            {
                diagnostic.ErrorMessage = AppendError(
                    diagnostic.ErrorMessage,
                    "Missing RimTalk.Prompt.PromptContext.");
                return null;
            }

            try
            {
                object context = Activator.CreateInstance(promptContextType);
                ApplyBindingDiagnostic(binding, diagnostic);
                SetProperty(context, "CurrentPawn", binding.CurrentPawn);
                SetProperty(context, "Pawns", binding.Pawns);
                SetProperty(context, "AllPawns", binding.AllPawns);
                SetProperty(context, "ScopedPawnIndex", binding.ScopedPawnIndex);
                SetProperty(context, "Map", binding.CurrentPawn?.MapHeld ?? Find.CurrentMap);
                SetProperty(context, "DialogueType", "conversation");
                SetProperty(context, "DialogueStatus", scenarioContext.IsProactive ? "proactive" : "manual");
                SetProperty(context, "IsPreview", false);
                TrySetVariableStore(context, diagnostic);
                TrySetChatHistory(context, scenarioContext, diagnostic);
                SetProperty(context, "PawnContext", BuildPawnContextText(binding.CurrentPawn));
                SetProperty(context, "DialoguePrompt", BuildDialoguePromptText(promptText));
                TryReportNullPawnTokenBinding(promptText, binding.CurrentPawn, diagnostic);
                return context;
            }
            catch (Exception ex)
            {
                diagnostic.ErrorMessage = AppendError(
                    diagnostic.ErrorMessage,
                    "PromptContext init exception: " + ex.GetBaseException().Message);
                return null;
            }
        }

        private static void TrySetVariableStore(object promptContext, RimTalkNativeRenderDiagnostic diagnostic)
        {
            Type variableStoreType = AccessTools.TypeByName("RimTalk.Prompt.VariableStore");
            if (variableStoreType == null)
            {
                diagnostic.ErrorMessage = AppendError(
                    diagnostic.ErrorMessage,
                    "Missing RimTalk.Prompt.VariableStore.");
                return;
            }

            try
            {
                object store = Activator.CreateInstance(variableStoreType);
                Type systemType = AccessTools.TypeByName("Scriban.Runtime.ScriptObject");
                if (systemType != null)
                {
                    object systemObject = Activator.CreateInstance(systemType);
                    object customObject = Activator.CreateInstance(systemType);
                    SetProperty(systemObject, "custom", customObject);
                    SetProperty(store, "system", systemObject);
                }

                SetProperty(promptContext, "VariableStore", store);
            }
            catch (Exception ex)
            {
                diagnostic.ErrorMessage = AppendError(
                    diagnostic.ErrorMessage,
                    "VariableStore init failed: " + ex.GetBaseException().Message);
            }
        }

        private static void TrySetChatHistory(
            object promptContext,
            DialogueScenarioContext scenarioContext,
            RimTalkNativeRenderDiagnostic diagnostic)
        {
            PropertyInfo property = promptContext?.GetType().GetProperty("ChatHistory", InstancePublic);
            if (property == null || !property.CanWrite)
            {
                return;
            }

            try
            {
                Type roleType = AccessTools.TypeByName("RimTalk.Data.Role");
                Type historyType = property.PropertyType;
                if (roleType == null || historyType == null)
                {
                    diagnostic.ErrorMessage = AppendError(
                        diagnostic.ErrorMessage,
                        "ChatHistory type bind failed.");
                    return;
                }

                object history = Activator.CreateInstance(historyType);
                MethodInfo addMethod = historyType.GetMethod("Add", InstancePublic);
                if (history == null || addMethod == null)
                {
                    diagnostic.ErrorMessage = AppendError(
                        diagnostic.ErrorMessage,
                        "ChatHistory list init failed.");
                    return;
                }

                Type tupleType = addMethod.GetParameters()[0].ParameterType;
                bool allowMemoryCompressionScheduling = RpgPromptTurnContextScope.Current?.AllowMemoryCompressionScheduling ?? true;
                bool allowMemoryColdLoad = RpgPromptTurnContextScope.Current?.AllowMemoryColdLoad ?? true;
                string memoryText = RpgNpcDialogueArchiveManager.Instance.BuildPromptMemoryBlock(
                    scenarioContext.Target,
                    scenarioContext.Initiator,
                    8,
                    900,
                    allowCompressionScheduling: allowMemoryCompressionScheduling,
                    allowCacheLoad: allowMemoryColdLoad);
                if (!string.IsNullOrWhiteSpace(memoryText))
                {
                    addMethod.Invoke(
                        history,
                        new[] { CreateHistoryEntry(tupleType, roleType, "System", memoryText.Trim()) });
                }

                string currentTurnUserIntent = RpgPromptTurnContextScope.Current?.CurrentTurnUserIntent ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(currentTurnUserIntent))
                {
                    addMethod.Invoke(
                        history,
                        new[] { CreateHistoryEntry(tupleType, roleType, "User", currentTurnUserIntent.Trim()) });
                }

                property.SetValue(promptContext, history, null);
            }
            catch (Exception ex)
            {
                diagnostic.ErrorMessage = AppendError(
                    diagnostic.ErrorMessage,
                    "ChatHistory init failed: " + ex.GetBaseException().Message);
            }
        }

        private static object CreateHistoryEntry(
            Type tupleType,
            Type roleType,
            string roleName,
            string text)
        {
            object role = Enum.Parse(roleType, roleName, true);
            return Activator.CreateInstance(tupleType, role, text ?? string.Empty);
        }

        private static PawnBindingSpec ResolvePawnBindingSpec(
            DialogueScenarioContext scenarioContext,
            RimTalkNativeRenderDiagnostic diagnostic)
        {
            string channel = diagnostic?.PromptChannel ?? string.Empty;
            Pawn currentPawn = ResolveCurrentPawnByChannel(channel, scenarioContext);
            if (channel == RimTalkPromptEntryChannelCatalog.RpgArchiveCompression &&
                scenarioContext?.Initiator == null)
            {
                diagnostic.ErrorMessage = AppendError(
                    diagnostic.ErrorMessage,
                    "Archive compression has no bindable interlocutor pawn.");
                Log.Warning("[RimChat] rpg_archive_compression missing interlocutor pawn; binding NPC only.");
            }

            if (currentPawn == null)
            {
                diagnostic.ErrorMessage = AppendError(
                    diagnostic.ErrorMessage,
                    "PromptContext binding has null CurrentPawn.");
            }

            List<Pawn> ordered = BuildPawnList(currentPawn, scenarioContext?.Initiator);
            int scopedPawnIndex = currentPawn == null ? -1 : ordered.FindIndex(pawn => pawn == currentPawn);
            if (scopedPawnIndex < 0 && ordered.Count > 0)
            {
                scopedPawnIndex = 0;
            }

            return new PawnBindingSpec
            {
                CurrentPawn = currentPawn,
                Pawns = ordered,
                AllPawns = new List<Pawn>(ordered),
                ScopedPawnIndex = scopedPawnIndex
            };
        }

        private static Pawn ResolveCurrentPawnByChannel(string promptChannel, DialogueScenarioContext scenarioContext)
        {
            switch (promptChannel ?? string.Empty)
            {
                case RimTalkPromptEntryChannelCatalog.RpgArchiveCompression:
                case RimTalkPromptEntryChannelCatalog.PersonaBootstrap:
                case RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue:
                case RimTalkPromptEntryChannelCatalog.RpgDialogue:
                    return scenarioContext?.Target;
                default:
                    return scenarioContext?.Target;
            }
        }

        private static List<Pawn> BuildPawnList(Pawn currentPawn, Pawn otherPawn)
        {
            var pawns = new List<Pawn>(2);
            if (currentPawn != null)
            {
                pawns.Add(currentPawn);
            }

            if (otherPawn != null && !pawns.Contains(otherPawn))
            {
                pawns.Add(otherPawn);
            }

            return pawns;
        }

        private static string BuildDialoguePromptText(string promptText)
        {
            string currentTurnUserIntent = RpgPromptTurnContextScope.Current?.CurrentTurnUserIntent ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(currentTurnUserIntent))
            {
                return currentTurnUserIntent.Trim();
            }

            return string.IsNullOrWhiteSpace(promptText)
                ? string.Empty
                : promptText.Trim();
        }

        private static string BuildPawnContextText(Pawn pawn)
        {
            if (pawn == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Pawn: " + pawn.LabelShortCap);
            if (pawn.Faction != null)
            {
                sb.AppendLine("Faction: " + pawn.Faction.Name);
            }

            if (pawn.story?.traits?.allTraits != null && pawn.story.traits.allTraits.Count > 0)
            {
                var traitLabels = new List<string>();
                for (int i = 0; i < pawn.story.traits.allTraits.Count && traitLabels.Count < 4; i++)
                {
                    string label = pawn.story.traits.allTraits[i]?.LabelCap;
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        traitLabels.Add(label);
                    }
                }

                if (traitLabels.Count > 0)
                {
                    sb.AppendLine("Traits: " + string.Join(", ", traitLabels));
                }
            }

            if (pawn.CurJob?.def?.label != null)
            {
                sb.AppendLine("Job: " + pawn.CurJob.def.label);
            }

            return sb.ToString().Trim();
        }

        private static void SetProperty(object target, string name, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            PropertyInfo property = target.GetType().GetProperty(name, InstancePublic);
            if (property == null || !property.CanWrite)
            {
                return;
            }

            if (value == null)
            {
                if (!property.PropertyType.IsValueType)
                {
                    property.SetValue(target, null, null);
                }

                return;
            }

            if (property.PropertyType.IsInstanceOfType(value))
            {
                property.SetValue(target, value, null);
                return;
            }

            if (property.PropertyType == typeof(int) && value is int intValue)
            {
                property.SetValue(target, intValue, null);
                return;
            }

            if (property.PropertyType == typeof(string) && value is string text)
            {
                property.SetValue(target, text, null);
                return;
            }

            if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType) && value is IEnumerable enumerable)
            {
                property.SetValue(target, enumerable, null);
            }
        }

        private static int CountRemainingTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            return RemainingTokenRegex.Matches(text).Count;
        }

        private static string BuildRemainingTokenPreview(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            IEnumerable<string> preview = RemainingTokenRegex.Matches(text)
                .Cast<Match>()
                .Select(match => match.Value?.Trim() ?? string.Empty)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct(StringComparer.Ordinal)
                .Take(6);
            return string.Join(" | ", preview);
        }

        private static void ApplyBindingDiagnostic(PawnBindingSpec binding, RimTalkNativeRenderDiagnostic diagnostic)
        {
            if (binding == null || diagnostic == null)
            {
                return;
            }

            diagnostic.CurrentPawnLabel = ResolvePawnLabel(binding.CurrentPawn);
            diagnostic.PawnCount = binding.Pawns?.Count ?? 0;
            diagnostic.AllPawnCount = binding.AllPawns?.Count ?? 0;
            diagnostic.ScopedPawnIndex = binding.ScopedPawnIndex;
        }

        private static string ResolvePawnLabel(Pawn pawn)
        {
            if (pawn == null)
            {
                return "null";
            }

            return pawn.LabelShortCap ?? pawn.LabelShort ?? pawn.Name?.ToStringShort ?? "unknown";
        }

        private static void TryReportNullPawnTokenBinding(
            string promptText,
            Pawn currentPawn,
            RimTalkNativeRenderDiagnostic diagnostic)
        {
            if (currentPawn != null || !ContainsPawnToken(promptText))
            {
                return;
            }

            diagnostic.ErrorMessage = AppendError(
                diagnostic.ErrorMessage,
                "Prompt contains pawn.* token but CurrentPawn is null.");
            Log.Warning(
                "[RimChat] Native RPG render has pawn token with null CurrentPawn. " +
                $"channel={diagnostic?.PromptChannel ?? string.Empty}, " +
                $"remaining_tokens={diagnostic?.RemainingTokenCount ?? 0}, " +
                $"error={diagnostic?.ErrorMessage ?? string.Empty}");
        }

        private static bool ContainsPawnToken(string promptText)
        {
            if (string.IsNullOrWhiteSpace(promptText))
            {
                return false;
            }

            return PawnTokenRegex.IsMatch(promptText);
        }

        private static string ResolvePromptChannel(string promptChannel, DialogueScenarioContext scenarioContext)
        {
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            if (!string.Equals(normalized, RimTalkPromptEntryChannelCatalog.Any, StringComparison.Ordinal))
            {
                return normalized;
            }

            string tagged = ResolveChannelTag(scenarioContext);
            if (!string.IsNullOrWhiteSpace(tagged))
            {
                return tagged;
            }

            return scenarioContext?.IsProactive == true
                ? RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue
                : RimTalkPromptEntryChannelCatalog.RpgDialogue;
        }

        private static string ResolveChannelTag(DialogueScenarioContext scenarioContext)
        {
            if (scenarioContext?.Tags == null)
            {
                return string.Empty;
            }

            foreach (string tag in scenarioContext.Tags)
            {
                if (string.IsNullOrWhiteSpace(tag) ||
                    !tag.StartsWith("channel:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string channel = tag.Substring("channel:".Length).Trim();
                string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(channel);
                if (!string.Equals(normalized, RimTalkPromptEntryChannelCatalog.Any, StringComparison.Ordinal))
                {
                    return normalized;
                }
            }

            return string.Empty;
        }

        private static void LogFailure(RimTalkNativeRenderDiagnostic diagnostic)
        {
            if (!ShouldEmitFailureLog(diagnostic, "native_render_failed"))
            {
                return;
            }

            Log.Warning(
                "[RimChat] RimTalk native RPG render failed. " +
                $"channel={diagnostic?.PromptChannel ?? string.Empty}, " +
                $"current_pawn={diagnostic?.CurrentPawnLabel ?? string.Empty}, " +
                $"pawn_count={(diagnostic?.PawnCount ?? 0)}, " +
                $"all_pawn_count={(diagnostic?.AllPawnCount ?? 0)}, " +
                $"scoped_pawn_index={(diagnostic?.ScopedPawnIndex ?? -1)}, " +
                $"bound_method={diagnostic?.BoundMethod ?? string.Empty}, " +
                $"bound_variant={diagnostic?.BoundMethodVariant ?? string.Empty}, " +
                $"context_built={(diagnostic?.ContextBuilt ?? false)}, " +
                $"compat_failure={(diagnostic?.IsCompatibilityFailure ?? false)}, " +
                $"failure_stage={diagnostic?.FailureStage ?? string.Empty}, " +
                $"remaining_tokens={(diagnostic?.RemainingTokenCount ?? 0)}, " +
                $"remaining_preview={diagnostic?.RemainingTokensPreview ?? string.Empty}, " +
                $"error={diagnostic?.ErrorMessage ?? string.Empty}");
        }

        private static bool ShouldEmitFailureLog(RimTalkNativeRenderDiagnostic diagnostic, string kind)
        {
            long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            string signature = BuildFailureSignature(diagnostic, kind);
            if (failureLogTicksBySignature.TryGetValue(signature, out long lastTick) &&
                now - lastTick < DuplicateFailureLogCooldownMs)
            {
                return false;
            }

            failureLogTicksBySignature[signature] = now;
            TrimFailureLogCache(now);
            return true;
        }

        private static string BuildFailureSignature(RimTalkNativeRenderDiagnostic diagnostic, string kind)
        {
            return string.Join("|",
                kind ?? string.Empty,
                diagnostic?.PromptChannel ?? string.Empty,
                diagnostic?.ErrorMessage ?? string.Empty,
                diagnostic?.RemainingTokensPreview ?? string.Empty,
                diagnostic?.RemainingTokenCount.ToString() ?? "0");
        }

        private static void TrimFailureLogCache(long now)
        {
            if (failureLogTicksBySignature.Count <= 256)
            {
                return;
            }

            long staleThreshold = now - DuplicateFailureLogCooldownMs * 8;
            var staleKeys = new List<string>();
            foreach (var pair in failureLogTicksBySignature)
            {
                if (pair.Value <= staleThreshold)
                {
                    staleKeys.Add(pair.Key);
                }
            }

            foreach (string key in staleKeys)
            {
                failureLogTicksBySignature.Remove(key);
            }
        }

        private static string AppendError(string current, string next)
        {
            if (string.IsNullOrWhiteSpace(next))
            {
                return current ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(current))
            {
                return next.Trim();
            }

            return current.Trim() + " | " + next.Trim();
        }

        private static string BuildMethodSignature(MethodInfo method)
        {
            if (method == null)
            {
                return string.Empty;
            }

            ParameterInfo[] parameters = method.GetParameters();
            var sb = new StringBuilder();
            sb.Append(method.DeclaringType?.FullName ?? "unknown");
            sb.Append(".");
            sb.Append(method.Name);
            sb.Append("(");
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(parameters[i].ParameterType.Name);
            }

            sb.Append(")");
            return sb.ToString();
        }

        private static string CleanInvalidRimTalkTokens(string promptText)
        {
            if (string.IsNullOrWhiteSpace(promptText))
            {
                return promptText ?? string.Empty;
            }

            string result = RimTalkNamespaceTokenRegex.Replace(promptText, string.Empty);
            result = LegacyRimTalkTokenRegex.Replace(result, string.Empty);
            result = RimChatOwnVariableRegex.Replace(result, string.Empty);
            return result.Trim();
        }

        private sealed class PawnBindingSpec
        {
            public Pawn CurrentPawn;
            public List<Pawn> Pawns = new List<Pawn>();
            public List<Pawn> AllPawns = new List<Pawn>();
            public int ScopedPawnIndex = -1;
        }
    }
}

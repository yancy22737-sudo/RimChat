using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using RimChat.Config;
using RimChat.Core;
using RimChat.Util;
using RimWorld;
using Verse;

namespace RimChat.Compat
{
    /// <summary>/// Dependencies: RimTalk runtime types via reflection, RimChat settings.
 /// Responsibility: provide optional RimTalk Prompt API/Scriban compatibility without compile-time dependency.
 ///</summary>
    public static partial class RimTalkCompatBridge
    {
        public const string KeyLastSessionSummary = "rimchat_last_session_summary";
        public const string KeyLastDiplomacySummary = "rimchat_last_diplomacy_summary";
        public const string KeyLastRpgSummary = "rimchat_last_rpg_summary";
        public const string KeyRecentSessionSummaries = "rimchat_recent_session_summaries";

        private const int MaxSummaryLength = 520;
        private const string RimChatCompatModId = "rimchat.compat";
        private const string RimChatCompatUserEntryModId = "rimchat.compat.user";
        private const string RimChatCompatPresetEntryName = "RimChat Compat Variables";
        private const string RimTalkDialoguePromptEntryName = "Dialogue Prompt";
        private const string RimTalkPromptApiTypeName = "RimTalk.API.RimTalkPromptAPI";
        private const string RimTalkPromptManagerTypeName = "RimTalk.Prompt.PromptManager";
        private const string RimTalkPromptContextTypeName = "RimTalk.Prompt.PromptContext";
        private const string RimTalkScribanParserTypeName = "RimTalk.Prompt.ScribanParser";
        private const string RimTalkContextHookRegistryTypeName = "RimTalk.API.ContextHookRegistry";

        private static readonly object SyncRoot = new object();
        private static readonly HashSet<string> RegisteredRimChatVariables =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loggedSummaryGlobalsInitialized;
        private static bool _loggedCompatPresetEnsured;
        private static bool _loggedCompatPresetNoActivePreset;
        private static bool _loggedCompatPresetCreateApiMissing;
        private static bool _loggedCompatPresetCreateFailed;

        private static bool _bindingResolved;
        private static bool _bindingAvailable;
        private static string _lastBindingReason = string.Empty;
        private static Type _promptApiType;
        private static Type _promptManagerType;
        private static Type _promptContextType;
        private static Type _contextHookRegistryType;
        private static MethodInfo _setGlobalVariableMethod;
        private static MethodInfo _getGlobalVariableMethod;
        private static MethodInfo _getVariableStoreMethod;
        private static MethodInfo _getActivePresetMethod;
        private static MethodInfo _getRegisteredCustomVariablesMethod;
        private static MethodInfo _createPromptEntryMethod;
        private static MethodInfo _addPromptEntryMethod;
        private static MethodInfo _insertPromptEntryAfterNameMethod;
        private static MethodInfo _renderMethod;
        private static MethodInfo _registerContextVariableApiMethod;
        private static MethodInfo _registerContextVariableMethod;
        private static MethodInfo _hasContextVariableMethod;
        private static Type _contextVariableProviderType;

        public static void TryWarmup()
        {
            if (!IsPromptCompatEnabled())
            {
                return;
            }

            if (!EnsureBound())
            {
                return;
            }

            DisableLegacyUserPresetEntries();
            EnsureRimChatContextVariablesRegistered();
            EnsureSummaryGlobalsInitialized();
            MaybeEnsureCompatPresetEntryRegistered();
        }

        public static void ResetSessionSummaryGlobalsForSaveIsolation()
        {
            if (!IsPromptCompatEnabled() || !EnsureBound())
            {
                return;
            }

            try
            {
                EnsureRimChatContextVariablesRegistered();
                EnsureSummaryGlobalsInitialized();
                MaybeEnsureCompatPresetEntryRegistered();

                WriteGlobal(KeyLastSessionSummary, string.Empty);
                WriteGlobal(KeyLastDiplomacySummary, string.Empty);
                WriteGlobal(KeyLastRpgSummary, string.Empty);
                WriteGlobal(KeyRecentSessionSummaries, string.Empty);
                DebugLogger.Debug("RimTalk summary globals reset for save isolation.");
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"RimTalk summary global reset failed silently. {ex.Message}");
            }
        }

        public static bool IsRuntimeAvailable()
        {
            return IsPromptCompatEnabled() && EnsureBound();
        }

        public static RimTalkRuntimeStatus GetRuntimeStatus()
        {
            bool enabled = IsPromptCompatEnabled();
            bool available = enabled && EnsureBound();
            string reason = enabled
                ? (available ? "ok" : _lastBindingReason)
                : "prompt_compat_disabled";
            return new RimTalkRuntimeStatus
            {
                PromptCompatEnabled = enabled,
                RuntimeAvailable = available,
                Reason = reason ?? string.Empty
            };
        }

        public static List<RimTalkRegisteredVariable> GetRegisteredVariablesSnapshot()
        {
            var list = new List<RimTalkRegisteredVariable>();
            AppendBuiltInRimChatVariableItems(list);

            if (!EnsureBound() || _getRegisteredCustomVariablesMethod == null)
            {
                return list;
            }

            try
            {
                IEnumerable values = _getRegisteredCustomVariablesMethod.Invoke(null, null) as IEnumerable;
                if (values == null)
                {
                    return list;
                }

                foreach (object item in values)
                {
                    string name = GetTupleItemString(item, "Item1");
                    if (string.IsNullOrWhiteSpace(name) || list.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    list.Add(new RimTalkRegisteredVariable
                    {
                        Name = name,
                        ModId = GetTupleItemString(item, "Item2"),
                        Description = GetTupleItemString(item, "Item3"),
                        Type = GetTupleItemString(item, "Item4")
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"RimTalk variable snapshot failed silently. {ex.Message}");
            }

            return list
                .OrderBy(v => v.Type ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(v => v.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static RimTalkPromptEntryWriteResult TryAddOrUpdateUserPromptEntry(
            string entryName,
            string content,
            string roleName,
            string positionName,
            int inChatDepth,
            string afterEntryName)
        {
            if (string.IsNullOrWhiteSpace(entryName) || string.IsNullOrWhiteSpace(content))
            {
                return RimTalkPromptEntryWriteResult.InvalidInput;
            }

            if (!IsPromptCompatEnabled() || !EnsureBound())
            {
                return RimTalkPromptEntryWriteResult.BridgeUnavailable;
            }

            object activePreset = GetActivePreset();
            if (activePreset == null)
            {
                return RimTalkPromptEntryWriteResult.ActivePresetUnavailable;
            }

            object existingEntry = FindUserPresetEntry(activePreset, entryName);
            if (existingEntry != null)
            {
                ApplyEntryEditableFields(existingEntry, content, roleName, positionName, inChatDepth);
                SetPropertyOrField(existingEntry, "Enabled", true);
                return RimTalkPromptEntryWriteResult.Success;
            }

            object createdEntry = CreateUserPresetEntry(entryName, content, roleName, positionName, inChatDepth);
            if (createdEntry == null)
            {
                return RimTalkPromptEntryWriteResult.CreateFailed;
            }

            bool inserted = TryInsertUserEntry(createdEntry, afterEntryName);
            return inserted ? RimTalkPromptEntryWriteResult.Success : RimTalkPromptEntryWriteResult.InsertFailed;
        }

        public static string RenderCompatTemplate(
            string templateText,
            Pawn initiator,
            Pawn target,
            Faction faction,
            string channel)
        {
            if (string.IsNullOrWhiteSpace(templateText))
            {
                return string.Empty;
            }

            if (!EnsureBound())
            {
                return templateText;
            }

            EnsureRimChatContextVariablesRegistered();
            EnsureSummaryGlobalsInitialized();
            MaybeEnsureCompatPresetEntryRegistered();

            try
            {
                object context = CreatePromptContext(initiator, target, faction, channel);
                if (context == null || _renderMethod == null)
                {
                    return templateText;
                }

                object rendered = InvokeRender(templateText, context);
                if (rendered is string renderedText && !string.IsNullOrWhiteSpace(renderedText))
                {
                    return renderedText;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"RimTalk compat render failed, fallback to raw template. {ex.Message}");
            }

            return templateText;
        }

        public static bool TryRenderPawnPersonaCopyTemplate(Pawn pawn, string templateText, out string renderedText)
        {
            renderedText = string.Empty;
            if (!TryBuildPawnPersonaRenderContext(pawn, templateText, out object context, out string normalizedTemplate))
            {
                return false;
            }

            return TryRenderPawnPersonaTemplate(normalizedTemplate, context, out renderedText);
        }

        private static bool TryBuildPawnPersonaRenderContext(
            Pawn pawn,
            string templateText,
            out object context,
            out string normalizedTemplate)
        {
            context = null;
            normalizedTemplate = NormalizePawnPersonaCopyTemplate(templateText);
            if (pawn == null || string.IsNullOrWhiteSpace(normalizedTemplate))
            {
                return false;
            }

            bool fullyBound = EnsureBound();
            if (!fullyBound && (_renderMethod == null || _promptContextType == null))
            {
                return false;
            }

            EnsureRimChatContextVariablesRegistered();
            EnsureSummaryGlobalsInitialized();
            MaybeEnsureCompatPresetEntryRegistered();
            context = CreatePromptContext(pawn, pawn, pawn.Faction, "rpg");
            return context != null && _renderMethod != null;
        }

        private static bool TryRenderPawnPersonaTemplate(
            string normalizedTemplate,
            object context,
            out string renderedText)
        {
            renderedText = string.Empty;
            try
            {
                object rendered = InvokeRender(normalizedTemplate, context);
                if (!(rendered is string text) || string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                renderedText = text.Trim().Replace("\r", " ").Replace("\n", " ");
                return renderedText.Length > 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"RimTalk persona copy render failed silently. {ex.Message}");
                return false;
            }
        }

        public static string RenderActivePresetModEntries(
            Pawn initiator,
            Pawn target,
            Faction faction,
            string channel)
        {
            if (!IsPromptCompatEnabled(channel) || !EnsureBound())
            {
                return string.Empty;
            }

            EnsureRimChatContextVariablesRegistered();
            EnsureSummaryGlobalsInitialized();
            MaybeEnsureCompatPresetEntryRegistered();

            try
            {
                object context = CreatePromptContext(initiator, target, faction, channel);
                if (context == null)
                {
                    return string.Empty;
                }

                object activePreset = GetActivePreset();
                if (activePreset == null)
                {
                    return string.Empty;
                }

                IEnumerable entries = GetPropertyOrFieldValue(activePreset, "Entries") as IEnumerable;
                if (entries == null)
                {
                    return string.Empty;
                }

                var blocks = new List<string>();
                int totalChars = 0;
                RimChatSettings settings = RimChatMod.Settings;
                int maxInjectedPresetEntries = settings?.GetRimTalkPresetInjectionMaxEntriesClamped(channel) ?? RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
                int maxInjectedPresetChars = settings?.GetRimTalkPresetInjectionMaxCharsClamped(channel) ?? RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
                foreach (object entry in entries)
                {
                    if (!ShouldInjectPresetEntry(entry))
                    {
                        continue;
                    }

                    string content = GetStringPropertyOrField(entry, "Content");
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        continue;
                    }

                    object renderedRaw = InvokeRender(content, context);
                    string rendered = renderedRaw as string;
                    if (string.IsNullOrWhiteSpace(rendered))
                    {
                        continue;
                    }

                    string entryName = GetStringPropertyOrField(entry, "Name");
                    string sourceModId = GetStringPropertyOrField(entry, "SourceModId");
                    string block = $"[{entryName ?? "Mod Entry"} | {sourceModId}] {rendered.Trim()}";

                    if (maxInjectedPresetChars > RimChatSettings.RimTalkPresetInjectionLimitUnlimited &&
                        totalChars + block.Length > maxInjectedPresetChars)
                    {
                        break;
                    }

                    blocks.Add(block);
                    totalChars += block.Length;
                    if (maxInjectedPresetEntries > RimChatSettings.RimTalkPresetInjectionLimitUnlimited &&
                        blocks.Count >= maxInjectedPresetEntries)
                    {
                        break;
                    }
                }

                if (blocks.Count == 0)
                {
                    return string.Empty;
                }

                return "=== RIMTALK ACTIVE PRESET MOD ENTRIES ===\n" + string.Join("\n\n", blocks);
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"RimTalk preset entry render failed silently. {ex.Message}");
                return string.Empty;
            }
        }

        public static void PushSessionSummary(string summaryText, RimTalkSummaryChannel channel)
        {
            if (string.IsNullOrWhiteSpace(summaryText))
            {
                return;
            }

            if (!IsPromptCompatEnabled() || !EnsureBound())
            {
                return;
            }

            if (!IsSummaryPushEnabled())
            {
                return;
            }

            EnsureRimChatContextVariablesRegistered();
            EnsureSummaryGlobalsInitialized();
            MaybeEnsureCompatPresetEntryRegistered();

            try
            {
                string normalized = NormalizeSummary(summaryText);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    return;
                }

                WriteGlobal(KeyLastSessionSummary, normalized);

                switch (channel)
                {
                    case RimTalkSummaryChannel.Diplomacy:
                        WriteGlobal(KeyLastDiplomacySummary, normalized);
                        break;
                    case RimTalkSummaryChannel.Rpg:
                        WriteGlobal(KeyLastRpgSummary, normalized);
                        break;
                }

                int limit = RimChatMod.Settings?.GetRimTalkSummaryHistoryLimitClamped() ?? 10;
                List<string> items = ParseHistory(ReadGlobal(KeyRecentSessionSummaries));
                items.Insert(0, BuildHistoryItem(channel, normalized));
                List<string> deduped = DeduplicateHistory(items);
                if (deduped.Count > limit)
                {
                    deduped = deduped.Take(limit).ToList();
                }

                WriteGlobal(KeyRecentSessionSummaries, string.Join("\n", deduped));
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"RimTalk summary push failed silently. {ex.Message}");
            }
        }

        private static object InvokeRender(string templateText, object context)
        {
            ParameterInfo[] parameters = _renderMethod.GetParameters();
            if (parameters.Length >= 3 && parameters[2].ParameterType == typeof(bool))
            {
                return _renderMethod.Invoke(null, new object[] { templateText, context, false });
            }

            return _renderMethod.Invoke(null, new object[] { templateText, context });
        }

        private static void DisableLegacyUserPresetEntries()
        {
            try
            {
                IEnumerable entries = GetActivePresetEntries();
                if (entries == null)
                {
                    return;
                }

                int disabledCount = 0;
                foreach (object entry in entries)
                {
                    if (TryDisableLegacyUserEntry(entry))
                    {
                        disabledCount++;
                    }
                }

                if (disabledCount > 0)
                {
                    DebugLogger.Debug($"RimTalk legacy user prompt entries disabled: {disabledCount}.");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"RimTalk legacy user prompt entry cleanup failed silently. {ex.Message}");
            }
        }

        private static IEnumerable GetActivePresetEntries()
        {
            object activePreset = GetActivePreset();
            if (activePreset == null)
            {
                return null;
            }

            return GetPropertyOrFieldValue(activePreset, "Entries") as IEnumerable;
        }

        private static bool TryDisableLegacyUserEntry(object entry)
        {
            if (!IsLegacyUserEntry(entry))
            {
                return false;
            }

            if (!GetBoolPropertyOrField(entry, "Enabled", true))
            {
                return false;
            }

            return SetPropertyOrField(entry, "Enabled", false);
        }

        private static bool IsLegacyUserEntry(object entry)
        {
            string sourceModId = GetStringPropertyOrField(entry, "SourceModId");
            return string.Equals(sourceModId, RimChatCompatUserEntryModId, StringComparison.OrdinalIgnoreCase);
        }

        private static object CreatePromptContext(Pawn initiator, Pawn target, Faction faction, string channel)
        {
            if (_promptContextType == null)
            {
                return null;
            }

            object context = Activator.CreateInstance(_promptContextType);
            Pawn currentPawn = target ?? initiator ?? faction?.leader;
            Map map = target?.Map ?? initiator?.Map ?? Find.CurrentMap;
            List<Pawn> allPawns = BuildPawnsList(initiator, target, faction);

            SetPropertyIfExists(context, "CurrentPawn", currentPawn);
            SetPropertyIfExists(context, "AllPawns", allPawns);
            SetPropertyIfExists(context, "Map", map);
            SetPropertyIfExists(context, "DialogueType", channel ?? string.Empty);
            SetPropertyIfExists(context, "DialogueStatus", "manual");
            SetPropertyIfExists(context, "DialoguePrompt", string.Empty);
            SetPropertyIfExists(context, "PawnContext", string.Empty);
            SetPropertyIfExists(context, "IsPreview", false);

            if (_getVariableStoreMethod != null)
            {
                object variableStore = _getVariableStoreMethod.Invoke(null, null);
                SetPropertyIfExists(context, "VariableStore", variableStore);
            }

            return context;
        }

        private static List<Pawn> BuildPawnsList(Pawn initiator, Pawn target, Faction faction)
        {
            var pawns = new List<Pawn>();
            if (target != null)
            {
                pawns.Add(target);
            }

            if (initiator != null && initiator != target)
            {
                pawns.Add(initiator);
            }

            Pawn leader = faction?.leader;
            if (leader != null && !pawns.Contains(leader))
            {
                pawns.Add(leader);
            }

            return pawns;
        }

        private static void SetPropertyIfExists(object target, string propertyName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return;
            }

            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite)
            {
                return;
            }

            if (value == null)
            {
                if (!property.PropertyType.IsValueType || Nullable.GetUnderlyingType(property.PropertyType) != null)
                {
                    property.SetValue(target, null);
                }
                return;
            }

            if (property.PropertyType.IsAssignableFrom(value.GetType()))
            {
                property.SetValue(target, value);
            }
        }

        private static bool EnsureBound()
        {
            if (_bindingResolved)
            {
                return _bindingAvailable;
            }

            lock (SyncRoot)
            {
                if (_bindingResolved)
                {
                    return _bindingAvailable;
                }

                try
                {
                    _promptApiType = ResolveType(RimTalkPromptApiTypeName);
                    _promptManagerType = ResolveType(RimTalkPromptManagerTypeName);
                    _promptContextType = ResolveType(RimTalkPromptContextTypeName);
                    _contextHookRegistryType = ResolveType(RimTalkContextHookRegistryTypeName);
                    Type scribanType = ResolveType(RimTalkScribanParserTypeName);

                    if (_promptApiType == null || _promptContextType == null || scribanType == null)
                    {
                        _lastBindingReason = "RimTalk runtime types not found.";
                        _bindingAvailable = false;
                        return false;
                    }

                    _setGlobalVariableMethod = _promptApiType.GetMethod(
                        "SetGlobalVariable",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(string), typeof(string) },
                        null);

                    _getGlobalVariableMethod = _promptApiType.GetMethod(
                        "GetGlobalVariable",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(string), typeof(string) },
                        null);

                    _getVariableStoreMethod = _promptApiType.GetMethod(
                        "GetVariableStore",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        Type.EmptyTypes,
                        null);

                    _getActivePresetMethod = _promptApiType.GetMethod(
                        "GetActivePreset",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        Type.EmptyTypes,
                        null);

                    _getRegisteredCustomVariablesMethod = _promptApiType.GetMethod(
                        "GetRegisteredCustomVariables",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        Type.EmptyTypes,
                        null);

                    _createPromptEntryMethod = ResolveCreatePromptEntryMethod(_promptApiType);
                    _addPromptEntryMethod = ResolveSinglePromptEntryApiMethod("AddPromptEntry");
                    _insertPromptEntryAfterNameMethod = _promptApiType.GetMethod(
                        "InsertPromptEntryAfterName",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { _createPromptEntryMethod?.ReturnType ?? typeof(object), typeof(string) },
                        null);
                    if (_insertPromptEntryAfterNameMethod == null)
                    {
                        _insertPromptEntryAfterNameMethod = ResolveSinglePromptEntryApiMethod("InsertPromptEntryAfterName");
                    }

                    _renderMethod = ResolveRenderMethod(scribanType, _promptContextType);
                    BindContextVariableRegistrationMethods();
                    _bindingAvailable =
                        _setGlobalVariableMethod != null &&
                        _getGlobalVariableMethod != null &&
                        _renderMethod != null;

                    if (_bindingAvailable)
                    {
                        _lastBindingReason = string.Empty;
                        EnsureRimChatContextVariablesRegistered();
                        EnsureSummaryGlobalsInitialized();
                        MaybeEnsureCompatPresetEntryRegistered();
                        DebugLogger.Debug("RimTalk compat bridge bound successfully.");
                    }
                    else
                    {
                        _lastBindingReason = "RimTalk API methods unresolved.";
                    }
                }
                catch (Exception ex)
                {
                    _bindingAvailable = false;
                    _lastBindingReason = ex.Message ?? "RimTalk bind failed.";
                    DebugLogger.Debug($"RimTalk binding failed silently. {ex.Message}");
                }
                finally
                {
                    _bindingResolved = _bindingAvailable;
                }
            }

            return _bindingAvailable;
        }

        private static void BindContextVariableRegistrationMethods()
        {
            _registerContextVariableApiMethod = null;
            _contextVariableProviderType = null;

            if (_promptApiType != null)
            {
                foreach (MethodInfo method in _promptApiType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!string.Equals(method.Name, "RegisterContextVariable", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length < 3)
                    {
                        continue;
                    }

                    if (parameters[0].ParameterType == typeof(string) &&
                        parameters[1].ParameterType == typeof(string) &&
                        typeof(Delegate).IsAssignableFrom(parameters[2].ParameterType))
                    {
                        _registerContextVariableApiMethod = method;
                        _contextVariableProviderType = parameters[2].ParameterType;
                        break;
                    }
                }
            }

            if (_contextHookRegistryType == null)
            {
                return;
            }

            foreach (MethodInfo method in _contextHookRegistryType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "RegisterContextVariable", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 3)
                {
                    continue;
                }

                if (parameters[0].ParameterType == typeof(string) &&
                    parameters[1].ParameterType == typeof(string) &&
                    typeof(Delegate).IsAssignableFrom(parameters[2].ParameterType))
                {
                    _registerContextVariableMethod = method;
                    break;
                }
            }

            _hasContextVariableMethod = _contextHookRegistryType.GetMethod(
                "HasContextVariable",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
        }

        private static void EnsureRimChatContextVariablesRegistered()
        {
            if (_registerContextVariableApiMethod == null && _registerContextVariableMethod == null)
            {
                return;
            }

            RegisterContextVariableMirror(KeyLastSessionSummary, "Latest RimChat session summary.");
            RegisterContextVariableMirror(KeyLastDiplomacySummary, "Latest RimChat diplomacy session summary.");
            RegisterContextVariableMirror(KeyLastRpgSummary, "Latest RimChat RPG session summary.");
            RegisterContextVariableMirror(KeyRecentSessionSummaries, "Recent RimChat session summaries (rolling).");
        }

        private static void RegisterContextVariableMirror(string variableName, string description)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return;
            }

            if (RegisteredRimChatVariables.Contains(variableName))
            {
                return;
            }

            if (IsContextVariableAlreadyRegistered(variableName))
            {
                RegisteredRimChatVariables.Add(variableName);
                return;
            }

            bool registered =
                TryRegisterContextVariableViaPromptApi(variableName, description) ||
                TryRegisterContextVariableViaContextRegistry(variableName, description);

            if (registered)
            {
                RegisteredRimChatVariables.Add(variableName);
                DebugLogger.Debug($"RimTalk compat variable registered: {variableName}");
            }
        }

        private static void EnsureSummaryGlobalsInitialized()
        {
            if (_setGlobalVariableMethod == null || _getGlobalVariableMethod == null)
            {
                return;
            }

            EnsureGlobalVariableKey(KeyLastSessionSummary);
            EnsureGlobalVariableKey(KeyLastDiplomacySummary);
            EnsureGlobalVariableKey(KeyLastRpgSummary);
            EnsureGlobalVariableKey(KeyRecentSessionSummaries);

            if (!_loggedSummaryGlobalsInitialized)
            {
                _loggedSummaryGlobalsInitialized = true;
                DebugLogger.Debug("RimTalk summary globals initialized.");
            }
        }

        private static void EnsureGlobalVariableKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            string current = ReadGlobal(key);
            WriteGlobal(key, current);
        }

        private static string ReadGlobal(string key)
        {
            if (_getGlobalVariableMethod == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            object value = _getGlobalVariableMethod.Invoke(null, new object[] { key, string.Empty });
            return value as string ?? string.Empty;
        }

        private static void WriteGlobal(string key, string value)
        {
            if (_setGlobalVariableMethod == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _setGlobalVariableMethod.Invoke(null, new object[] { key, value ?? string.Empty });
        }

        private static List<string> ParseHistory(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            return raw
                .Replace("\r", string.Empty)
                .Split('\n')
                .Select(line => line?.Trim() ?? string.Empty)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }

        private static List<string> DeduplicateHistory(List<string> items)
        {
            if (items == null || items.Count <= 1)
            {
                return items ?? new List<string>();
            }

            var deduped = new List<string>(items.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                string item = items[i];
                if (string.IsNullOrWhiteSpace(item) || !seen.Add(item))
                {
                    continue;
                }

                deduped.Add(item);
            }

            return deduped;
        }

        private static string BuildHistoryItem(RimTalkSummaryChannel channel, string summary)
        {
            string prefix;
            switch (channel)
            {
                case RimTalkSummaryChannel.Diplomacy:
                    prefix = "[Diplomacy]";
                    break;
                case RimTalkSummaryChannel.Rpg:
                    prefix = "[RPG]";
                    break;
                default:
                    prefix = "[Session]";
                    break;
            }

            return $"{prefix} {summary}";
        }

        private static string NormalizeSummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
            {
                return string.Empty;
            }

            string singleLine = string.Join(
                " ",
                summary.Replace("\r", "\n")
                    .Split('\n')
                    .Select(part => part?.Trim() ?? string.Empty)
                    .Where(part => part.Length > 0));

            if (singleLine.Length > MaxSummaryLength)
            {
                return singleLine.Substring(0, MaxSummaryLength - 3) + "...";
            }

            return singleLine;
        }

        private static string NormalizePawnPersonaCopyTemplate(string templateText)
        {
            string trimmed = templateText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            if (trimmed.IndexOf("{{", StringComparison.Ordinal) >= 0)
            {
                return trimmed;
            }

            string token = trimmed.Trim('{', '}').Trim();
            return string.IsNullOrWhiteSpace(token) ? string.Empty : "{{" + token + "}}";
        }

        private static bool IsPromptCompatEnabled(string channel = null)
        {
            RimChatSettings settings = RimChatMod.Settings;
            if (settings == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(channel))
            {
                return settings.IsAnyRimTalkPromptCompatEnabled();
            }

            return settings.IsRimTalkPromptCompatEnabled(channel);
        }

        private static bool IsSummaryPushEnabled()
        {
            RimChatSettings settings = RimChatMod.Settings;
            return settings != null && settings.IsRimTalkSummaryPushEnabled();
        }

        private static bool IsAutoPresetSyncEnabled()
        {
            RimChatSettings settings = RimChatMod.Settings;
            return settings != null && settings.IsRimTalkAutoPresetSyncEnabled();
        }

        private static void MaybeEnsureCompatPresetEntryRegistered()
        {
            if (!IsAutoPresetSyncEnabled())
            {
                return;
            }

            EnsureCompatPresetEntryRegistered();
        }
    }

    public enum RimTalkSummaryChannel
    {
        Unknown = 0,
        Diplomacy = 1,
        Rpg = 2
    }
}

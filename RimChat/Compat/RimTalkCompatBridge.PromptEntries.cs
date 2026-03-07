using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimChat.Util;

namespace RimChat.Compat
{
    /// <summary>
    /// Dependencies: RimTalk Prompt API reflection handles resolved in bridge core.
    /// Responsibility: prompt entry creation/insertion, preset compat entry ensure, and variable item models.
    /// </summary>
    public static partial class RimTalkCompatBridge
    {
        private static void EnsureCompatPresetEntryRegistered()
        {
            object activePreset = GetActivePreset();
            if (activePreset == null)
            {
                if (!_loggedCompatPresetNoActivePreset)
                {
                    _loggedCompatPresetNoActivePreset = true;
                    DebugLogger.Debug("RimTalk compat preset ensure skipped: no active preset.");
                }
                return;
            }

            object existing = FindRimChatCompatPresetEntry(activePreset);
            if (existing != null)
            {
                ApplyEntryEditableFields(existing, BuildCompatPresetEntryContent(), "System", "Relative", 0);
                SetPropertyOrField(existing, "Name", RimChatCompatPresetEntryName);
                SetPropertyOrField(existing, "SourceModId", RimChatCompatModId);
                SetPropertyOrField(existing, "Enabled", true);

                if (!_loggedCompatPresetEnsured)
                {
                    _loggedCompatPresetEnsured = true;
                    DebugLogger.Debug("RimTalk compat preset entry ensured.");
                }
                return;
            }

            object created = CreateCompatPresetEntry();
            if (created == null)
            {
                if (!_loggedCompatPresetCreateFailed)
                {
                    _loggedCompatPresetCreateFailed = true;
                    DebugLogger.Debug("RimTalk compat preset ensure failed: could not create entry.");
                }
                return;
            }

            bool inserted = TryInsertEntry(created, RimTalkDialoguePromptEntryName);
            if (!inserted)
            {
                if (!_loggedCompatPresetCreateFailed)
                {
                    _loggedCompatPresetCreateFailed = true;
                    DebugLogger.Debug("RimTalk compat preset ensure failed: insert/add entry failed.");
                }
                return;
            }

            if (!_loggedCompatPresetEnsured)
            {
                _loggedCompatPresetEnsured = true;
                DebugLogger.Debug("RimTalk compat preset entry ensured.");
            }
        }

        private static object FindRimChatCompatPresetEntry(object activePreset)
        {
            IEnumerable entries = GetPropertyOrFieldValue(activePreset, "Entries") as IEnumerable;
            if (entries == null)
            {
                return null;
            }

            object bySource = null;
            object byName = null;
            foreach (object entry in entries)
            {
                string sourceModId = GetStringPropertyOrField(entry, "SourceModId");
                string name = GetStringPropertyOrField(entry, "Name");

                if (string.Equals(sourceModId, RimChatCompatModId, StringComparison.OrdinalIgnoreCase))
                {
                    bySource = entry;
                    break;
                }

                if (byName == null &&
                    string.Equals(name, RimChatCompatPresetEntryName, StringComparison.OrdinalIgnoreCase))
                {
                    byName = entry;
                }
            }

            return bySource ?? byName;
        }

        private static object CreateCompatPresetEntry()
        {
            return CreatePromptEntryInternal(
                RimChatCompatPresetEntryName,
                BuildCompatPresetEntryContent(),
                "System",
                "Relative",
                0,
                RimChatCompatModId);
        }

        private static string BuildCompatPresetEntryContent()
        {
            return string.Join("\n", new[]
            {
                "=== RIMCHAT SESSION MEMORY ===",
                "Latest session: {{rimchat_last_session_summary}}",
                "Latest diplomacy: {{rimchat_last_diplomacy_summary}}",
                "Latest RPG: {{rimchat_last_rpg_summary}}",
                "Recent summaries:",
                "{{rimchat_recent_session_summaries}}"
            });
        }

        private static MethodInfo ResolveCreatePromptEntryMethod(Type promptApiType)
        {
            if (promptApiType == null)
            {
                return null;
            }

            BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            MethodInfo best = null;
            int bestScore = int.MinValue;

            foreach (MethodInfo method in promptApiType.GetMethods(flags))
            {
                if (!string.Equals(method.Name, "CreatePromptEntry", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 2)
                {
                    continue;
                }

                if (parameters[0].ParameterType != typeof(string) || parameters[1].ParameterType != typeof(string))
                {
                    continue;
                }

                int score = parameters.Length * 10;
                if (parameters.Length >= 3 && parameters[2].ParameterType.IsEnum)
                {
                    score += 5;
                }

                if (parameters.Length >= 4 && parameters[3].ParameterType.IsEnum)
                {
                    score += 5;
                }

                if (parameters.Any(p => string.Equals(p.Name, "sourceModId", StringComparison.OrdinalIgnoreCase)))
                {
                    score += 3;
                }

                if (score > bestScore)
                {
                    best = method;
                    bestScore = score;
                }
            }

            return best;
        }

        private static MethodInfo ResolveSinglePromptEntryApiMethod(string methodName)
        {
            if (_promptApiType == null || string.IsNullOrWhiteSpace(methodName))
            {
                return null;
            }

            Type preferredEntryType = _createPromptEntryMethod?.ReturnType;
            MethodInfo fallback = null;
            BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

            foreach (MethodInfo method in _promptApiType.GetMethods(flags))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0)
                {
                    continue;
                }

                if (string.Equals(methodName, "AddPromptEntry", StringComparison.Ordinal))
                {
                    if (parameters.Length != 1)
                    {
                        continue;
                    }
                }
                else if (string.Equals(methodName, "InsertPromptEntryAfterName", StringComparison.Ordinal))
                {
                    if (parameters.Length != 2 || parameters[1].ParameterType != typeof(string))
                    {
                        continue;
                    }
                }

                if (preferredEntryType != null && preferredEntryType != typeof(object))
                {
                    if (parameters[0].ParameterType == preferredEntryType)
                    {
                        return method;
                    }
                }

                if (fallback == null)
                {
                    fallback = method;
                }
            }

            return fallback;
        }

        private static void AppendBuiltInRimChatVariableItems(List<RimTalkRegisteredVariable> list)
        {
            if (list == null)
            {
                return;
            }

            AddBuiltinVariable(list, KeyLastSessionSummary, "Latest RimChat session summary.");
            AddBuiltinVariable(list, KeyLastDiplomacySummary, "Latest RimChat diplomacy summary.");
            AddBuiltinVariable(list, KeyLastRpgSummary, "Latest RimChat RPG summary.");
            AddBuiltinVariable(list, KeyRecentSessionSummaries, "Recent RimChat summaries (rolling).");
        }

        private static void AddBuiltinVariable(List<RimTalkRegisteredVariable> list, string name, string description)
        {
            if (list.Any(v => string.Equals(v?.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            list.Add(new RimTalkRegisteredVariable
            {
                Name = name,
                ModId = RimChatCompatModId,
                Description = description,
                Type = "Context"
            });
        }

        private static object FindUserPresetEntry(object activePreset, string entryName)
        {
            if (activePreset == null || string.IsNullOrWhiteSpace(entryName))
            {
                return null;
            }

            IEnumerable entries = GetPropertyOrFieldValue(activePreset, "Entries") as IEnumerable;
            if (entries == null)
            {
                return null;
            }

            foreach (object entry in entries)
            {
                string name = GetStringPropertyOrField(entry, "Name");
                if (!string.Equals(name, entryName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string sourceModId = GetStringPropertyOrField(entry, "SourceModId");
                if (string.Equals(sourceModId, RimChatCompatUserEntryModId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private static object CreateUserPresetEntry(
            string entryName,
            string content,
            string roleName,
            string positionName,
            int inChatDepth)
        {
            return CreatePromptEntryInternal(
                entryName,
                content,
                roleName,
                positionName,
                inChatDepth,
                RimChatCompatUserEntryModId);
        }

        private static object CreatePromptEntryInternal(
            string entryName,
            string content,
            string roleName,
            string positionName,
            int inChatDepth,
            string sourceModId)
        {
            try
            {
                if (_createPromptEntryMethod != null)
                {
                    object[] args = BuildCreatePromptEntryArguments(
                        _createPromptEntryMethod,
                        entryName,
                        content,
                        roleName,
                        positionName,
                        inChatDepth,
                        sourceModId);

                    object entry = _createPromptEntryMethod.Invoke(null, args);
                    if (entry != null)
                    {
                        ApplyEntryEditableFields(entry, content, roleName, positionName, inChatDepth);
                        SetPropertyOrField(entry, "Name", entryName);
                        SetPropertyOrField(entry, "SourceModId", sourceModId);
                        SetPropertyOrField(entry, "Enabled", true);
                        return entry;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_loggedCompatPresetCreateApiMissing)
                {
                    _loggedCompatPresetCreateApiMissing = true;
                    DebugLogger.Debug($"RimTalk compat create entry API invocation failed, fallback to ctor. {ex.Message}");
                }
            }

            Type promptEntryType = ResolvePromptEntryRuntimeType(GetActivePreset());
            if (promptEntryType == null)
            {
                if (!_loggedCompatPresetCreateApiMissing)
                {
                    _loggedCompatPresetCreateApiMissing = true;
                    DebugLogger.Debug("RimTalk compat create entry skipped: prompt entry type unresolved.");
                }
                return null;
            }

            try
            {
                object created = Activator.CreateInstance(promptEntryType);
                if (created == null)
                {
                    return null;
                }

                SetPropertyOrField(created, "Name", entryName);
                SetPropertyOrField(created, "SourceModId", sourceModId);
                ApplyEntryEditableFields(created, content, roleName, positionName, inChatDepth);
                SetPropertyOrField(created, "Enabled", true);
                return created;
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"RimTalk compat create entry via ctor failed silently. {ex.Message}");
                return null;
            }
        }

        private static object[] BuildCreatePromptEntryArguments(
            MethodInfo createMethod,
            string entryName,
            string content,
            string roleName,
            string positionName,
            int inChatDepth,
            string sourceModId)
        {
            ParameterInfo[] parameters = createMethod.GetParameters();
            var args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                object argument = null;

                switch (i)
                {
                    case 0:
                        argument = entryName ?? string.Empty;
                        break;
                    case 1:
                        argument = content ?? string.Empty;
                        break;
                    case 2:
                        argument = parameter.ParameterType.IsEnum
                            ? CreateEnumValue(parameter.ParameterType, roleName, 0)
                            : roleName ?? string.Empty;
                        break;
                    case 3:
                        argument = parameter.ParameterType.IsEnum
                            ? CreateEnumValue(parameter.ParameterType, positionName, 0)
                            : positionName ?? string.Empty;
                        break;
                    case 4:
                        argument = inChatDepth;
                        break;
                    case 5:
                        argument = sourceModId ?? string.Empty;
                        break;
                }

                if (argument == null)
                {
                    if (parameter.HasDefaultValue)
                    {
                        argument = parameter.DefaultValue;
                    }
                    else if (parameter.ParameterType.IsValueType)
                    {
                        argument = Activator.CreateInstance(parameter.ParameterType);
                    }
                }

                args[i] = argument;
            }

            return args;
        }

        private static Type ResolvePromptEntryRuntimeType(object activePreset)
        {
            Type entryType = _createPromptEntryMethod?.ReturnType;
            if (entryType != null && entryType != typeof(object))
            {
                return entryType;
            }

            Type addType = _addPromptEntryMethod?.GetParameters().FirstOrDefault()?.ParameterType;
            if (addType != null && addType != typeof(object))
            {
                return addType;
            }

            Type insertType = _insertPromptEntryAfterNameMethod?.GetParameters().FirstOrDefault()?.ParameterType;
            if (insertType != null && insertType != typeof(object))
            {
                return insertType;
            }

            IEnumerable entries = GetPropertyOrFieldValue(activePreset, "Entries") as IEnumerable;
            if (entries == null)
            {
                return null;
            }

            foreach (object item in entries)
            {
                if (item != null)
                {
                    return item.GetType();
                }
            }

            return null;
        }

        private static void ApplyEntryEditableFields(
            object entry,
            string content,
            string roleName,
            string positionName,
            int inChatDepth)
        {
            if (entry == null)
            {
                return;
            }

            SetPropertyOrField(entry, "Content", content ?? string.Empty);

            object roleValue = ParseExistingEntryEnum(entry, "Role", roleName);
            if (roleValue != null)
            {
                SetPropertyOrField(entry, "Role", roleValue);
            }

            object positionValue = ParseExistingEntryEnum(entry, "Position", positionName);
            if (positionValue != null)
            {
                SetPropertyOrField(entry, "Position", positionValue);
            }

            SetPropertyOrField(entry, "InChatDepth", Math.Max(0, inChatDepth));
            SetPropertyOrField(entry, "Enabled", true);
        }

        private static bool TryInsertUserEntry(object createdEntry, string afterEntryName)
        {
            bool inserted = TryInsertEntry(createdEntry, afterEntryName);
            if (inserted)
            {
                return true;
            }

            object activePreset = GetActivePreset();
            return FindUserPresetEntry(activePreset, GetStringPropertyOrField(createdEntry, "Name")) != null;
        }

        private static bool TryInsertEntry(object createdEntry, string afterEntryName)
        {
            if (createdEntry == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(afterEntryName) && _insertPromptEntryAfterNameMethod != null)
            {
                try
                {
                    object result = _insertPromptEntryAfterNameMethod.Invoke(null, new[] { createdEntry, afterEntryName });
                    if (!(result is bool matchedAnchor))
                    {
                        return true;
                    }

                    if (matchedAnchor)
                    {
                        return true;
                    }

                    return IsEntryPresent(GetActivePreset(), createdEntry);
                }
                catch (Exception ex)
                {
                    DebugLogger.Debug($"RimTalk compat insert-after-name failed, fallback to add. {ex.Message}");
                }
            }

            if (_addPromptEntryMethod == null)
            {
                return false;
            }

            try
            {
                object result = _addPromptEntryMethod.Invoke(null, new[] { createdEntry });
                if (result is bool added)
                {
                    return added;
                }

                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"RimTalk compat add-entry failed silently. {ex.Message}");
                return false;
            }
        }

        private static bool IsEntryPresent(object activePreset, object entry)
        {
            if (activePreset == null || entry == null)
            {
                return false;
            }

            string expectedId = GetStringPropertyOrField(entry, "Id");
            string expectedName = GetStringPropertyOrField(entry, "Name");
            string expectedSource = GetStringPropertyOrField(entry, "SourceModId");

            IEnumerable entries = GetPropertyOrFieldValue(activePreset, "Entries") as IEnumerable;
            if (entries == null)
            {
                return false;
            }

            foreach (object candidate in entries)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (ReferenceEquals(candidate, entry))
                {
                    return true;
                }

                string candidateId = GetStringPropertyOrField(candidate, "Id");
                if (!string.IsNullOrWhiteSpace(expectedId) &&
                    string.Equals(candidateId, expectedId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(expectedName) &&
                    string.Equals(GetStringPropertyOrField(candidate, "Name"), expectedName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(GetStringPropertyOrField(candidate, "SourceModId"), expectedSource, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

}

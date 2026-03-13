using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using RimChat.Util;

namespace RimChat.Compat
{
    /// <summary>/// Dependencies: reflection over RimTalk runtime assemblies.
 /// Responsibility: reflection-heavy helpers for variable registration and preset entry extraction.
 ///</summary>
    public static partial class RimTalkCompatBridge
    {
        private static readonly HashSet<string> BuiltinPresetEntryNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Base Instruction",
                "JSON Format",
                "Pawn Profiles",
                "Chat History",
                "Dialogue Prompt"
            };

        private static bool IsContextVariableAlreadyRegistered(string variableName)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return false;
            }

            if (_hasContextVariableMethod != null)
            {
                try
                {
                    object existsRaw = _hasContextVariableMethod.Invoke(null, new object[] { variableName });
                    if (existsRaw is bool exists && exists)
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Debug($"RimTalk has-variable check failed silently. {ex.Message}");
                }
            }

            if (_getRegisteredCustomVariablesMethod == null)
            {
                return false;
            }

            try
            {
                IEnumerable values = _getRegisteredCustomVariablesMethod.Invoke(null, null) as IEnumerable;
                if (values == null)
                {
                    return false;
                }

                foreach (object item in values)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    string name = GetTupleItemString(item, "Item1");
                    if (string.Equals(name, variableName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"RimTalk variable enumeration failed silently. {ex.Message}");
            }

            return false;
        }

        private static bool TryRegisterContextVariableViaPromptApi(string variableName, string description)
        {
            if (_registerContextVariableApiMethod == null || _contextVariableProviderType == null)
            {
                return false;
            }

            try
            {
                Delegate provider = BuildContextVariableProviderDelegate(variableName, _contextVariableProviderType);
                if (provider == null)
                {
                    return false;
                }

                object[] args = BuildRegisterContextVariableArguments(
                    _registerContextVariableApiMethod,
                    apiSignature: true,
                    variableName,
                    provider,
                    description);
                _registerContextVariableApiMethod.Invoke(null, args);

                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"RimTalk API variable registration failed silently. {ex.Message}");
                return false;
            }
        }

        private static bool TryRegisterContextVariableViaContextRegistry(string variableName, string description)
        {
            if (_registerContextVariableMethod == null)
            {
                return false;
            }

            try
            {
                ParameterInfo[] parameters = _registerContextVariableMethod.GetParameters();
                Type providerType = parameters.Length >= 3 ? parameters[2].ParameterType : typeof(Delegate);
                Delegate provider = BuildContextVariableProviderDelegate(variableName, providerType);
                if (provider == null)
                {
                    return false;
                }

                object[] args = BuildRegisterContextVariableArguments(
                    _registerContextVariableMethod,
                    apiSignature: false,
                    variableName,
                    provider,
                    description);
                _registerContextVariableMethod.Invoke(null, args);

                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"RimTalk registry variable registration failed silently. {ex.Message}");
                return false;
            }
        }

        private static object[] BuildRegisterContextVariableArguments(
            MethodInfo method,
            bool apiSignature,
            string variableName,
            Delegate provider,
            string description)
        {
            ParameterInfo[] parameters = method?.GetParameters() ?? Array.Empty<ParameterInfo>();
            object[] args = new object[parameters.Length];
            if (parameters.Length == 0)
            {
                return args;
            }

            if (apiSignature)
            {
                if (parameters.Length > 0)
                {
                    args[0] = RimChatCompatModId;
                }

                if (parameters.Length > 1)
                {
                    args[1] = variableName;
                }
            }
            else
            {
                if (parameters.Length > 0)
                {
                    args[0] = variableName;
                }

                if (parameters.Length > 1)
                {
                    args[1] = RimChatCompatModId;
                }
            }

            if (parameters.Length > 2)
            {
                args[2] = provider;
            }

            bool descriptionAssigned = false;
            for (int i = 3; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                Type type = parameter.ParameterType;
                if (type == typeof(string))
                {
                    if (!descriptionAssigned)
                    {
                        args[i] = description ?? string.Empty;
                        descriptionAssigned = true;
                        continue;
                    }

                    if (TryGetParameterDefaultValue(parameter, out object stringDefault))
                    {
                        args[i] = stringDefault;
                        continue;
                    }

                    args[i] = string.Empty;
                    continue;
                }

                if (TryGetParameterDefaultValue(parameter, out object defaultValue))
                {
                    args[i] = defaultValue;
                    continue;
                }

                if (type == typeof(int))
                {
                    args[i] = 100;
                }
                else
                {
                    args[i] = GetFallbackParameterValue(type);
                }
            }

            return args;
        }

        private static bool TryGetParameterDefaultValue(ParameterInfo parameter, out object value)
        {
            value = null;
            if (parameter == null || !parameter.HasDefaultValue)
            {
                return false;
            }

            value = parameter.DefaultValue;
            if (value == DBNull.Value)
            {
                value = GetFallbackParameterValue(parameter.ParameterType);
            }

            return true;
        }

        private static object GetFallbackParameterValue(Type type)
        {
            if (type == null || !type.IsValueType)
            {
                return null;
            }

            return Activator.CreateInstance(type);
        }

        private static Delegate BuildContextVariableProviderDelegate(string variableName, Type delegateType)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return null;
            }

            if (delegateType == null || delegateType == typeof(Delegate))
            {
                return new Func<object, string>(_ => ReadGlobalByName(variableName));
            }

            if (!typeof(Delegate).IsAssignableFrom(delegateType))
            {
                return null;
            }

            MethodInfo invokeMethod = delegateType.GetMethod("Invoke");
            if (invokeMethod == null || invokeMethod.ReturnType != typeof(string))
            {
                return null;
            }

            ParameterInfo[] parameters = invokeMethod.GetParameters();
            if (parameters.Length > 1)
            {
                return null;
            }

            MethodInfo resolver = typeof(RimTalkCompatBridge).GetMethod(
                nameof(ReadGlobalByName),
                BindingFlags.NonPublic | BindingFlags.Static);
            if (resolver == null)
            {
                return null;
            }

            if (parameters.Length == 0)
            {
                return Expression
                    .Lambda(delegateType, Expression.Call(resolver, Expression.Constant(variableName)))
                    .Compile();
            }

            ParameterExpression parameter = Expression.Parameter(parameters[0].ParameterType, "ctx");
            MethodCallExpression body = Expression.Call(resolver, Expression.Constant(variableName));
            return Expression.Lambda(delegateType, body, parameter).Compile();
        }

        private static string ReadGlobalByName(string key)
        {
            return ReadGlobal(key);
        }

        private static MethodInfo ResolveRenderMethod(Type scribanType, Type promptContextType)
        {
            if (scribanType == null || promptContextType == null)
            {
                return null;
            }

            BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            MethodInfo strict = scribanType.GetMethod(
                "Render",
                flags,
                null,
                new[] { typeof(string), promptContextType, typeof(bool) },
                null);
            if (strict != null)
            {
                return strict;
            }

            MethodInfo fallback = scribanType.GetMethod(
                "Render",
                flags,
                null,
                new[] { typeof(string), promptContextType },
                null);
            if (fallback != null)
            {
                return fallback;
            }

            foreach (MethodInfo method in scribanType.GetMethods(flags))
            {
                if (!string.Equals(method.Name, "Render", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] pars = method.GetParameters();
                if (pars.Length < 2)
                {
                    continue;
                }

                if (pars[0].ParameterType == typeof(string) && pars[1].ParameterType.IsAssignableFrom(promptContextType))
                {
                    return method;
                }
            }

            return null;
        }

        private static Type ResolveType(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return null;
            }

            Type direct = Type.GetType(fullName, throwOnError: false);
            if (direct != null)
            {
                return direct;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null)
                {
                    continue;
                }

                Type resolved = assembly.GetType(fullName, throwOnError: false);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            return null;
        }

        private static object GetActivePreset()
        {
            if (_getActivePresetMethod != null)
            {
                return _getActivePresetMethod.Invoke(null, null);
            }

            if (_promptManagerType == null)
            {
                return null;
            }

            PropertyInfo instanceProp = _promptManagerType.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static);
            if (instanceProp == null)
            {
                return null;
            }

            object manager = instanceProp.GetValue(null, null);
            if (manager == null)
            {
                return null;
            }

            MethodInfo method = _promptManagerType.GetMethod(
                "GetActivePreset",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            if (method == null)
            {
                return null;
            }

            return method.Invoke(manager, null);
        }

        private static bool ShouldInjectPresetEntry(object entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (!GetBoolPropertyOrField(entry, "Enabled", true))
            {
                return false;
            }

            if (GetBoolPropertyOrField(entry, "IsMainChatHistory", false))
            {
                return false;
            }

            string entryName = GetStringPropertyOrField(entry, "Name");
            if (BuiltinPresetEntryNames.Contains(entryName))
            {
                return false;
            }

            string sourceModId = GetStringPropertyOrField(entry, "SourceModId");
            if (string.Equals(sourceModId, RimChatCompatModId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            object position = GetPropertyOrFieldValue(entry, "Position");
            string positionText = position?.ToString();
            if (!string.IsNullOrWhiteSpace(positionText) &&
                !string.Equals(positionText, "Relative", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static object GetPropertyOrFieldValue(object target, string name)
        {
            if (target == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null)
            {
                return property.GetValue(target, null);
            }

            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return field?.GetValue(target);
        }

        private static string GetStringPropertyOrField(object target, string name)
        {
            object value = GetPropertyOrFieldValue(target, name);
            return value as string ?? string.Empty;
        }

        private static bool GetBoolPropertyOrField(object target, string name, bool defaultValue)
        {
            object value = GetPropertyOrFieldValue(target, name);
            if (value is bool b)
            {
                return b;
            }

            return defaultValue;
        }

        private static string GetTupleItemString(object value, string memberName)
        {
            if (value == null || string.IsNullOrWhiteSpace(memberName))
            {
                return string.Empty;
            }

            Type type = value.GetType();
            FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.GetValue(value) is string fieldValue)
            {
                return fieldValue;
            }

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.GetValue(value, null) is string propertyValue)
            {
                return propertyValue;
            }

            return string.Empty;
        }
    }
}

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RimChat.DiplomacySystem;
using Verse;

namespace RimChat.Memory
{
    /// <summary>
    /// Dependencies: SaveContextTracker, Verse save metadata reflection, and RPG persistent slot provider.
    /// Responsibility: resolve one strict per-save key shared by archive compression and leader-memory persistence.
    /// </summary>
    internal static class SaveScopeKeyResolver
    {
        private const string DefaultSaveName = "Default";
        private const BindingFlags InstanceStringMemberBinding =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        private const BindingFlags StaticStringMemberBinding =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

        public static bool TryResolveStrict(out string saveKey, out string reason)
        {
            saveKey = string.Empty;
            reason = string.Empty;
            if (TryResolveFromPersistentSlot(out saveKey) ||
                TryResolveFromCurrentGameInfo(out saveKey) ||
                TryResolveFromMetaHeader(out saveKey) ||
                TryResolveFromTrackedSaveName(out saveKey))
            {
                return true;
            }

            reason = BuildResolutionDiagnostic();
            return false;
        }

        public static string ResolveOrThrow()
        {
            if (TryResolveStrict(out string saveKey, out _))
            {
                return saveKey;
            }

            throw new InvalidOperationException(
                "Failed to resolve active save identifier; refusing to write into shared Default bucket. " +
                $"Diagnostic={BuildResolutionDiagnostic()}");
        }

        private static bool TryResolveFromTrackedSaveName(out string saveKey)
        {
            return TryBuildSaveKeyFromName(SaveContextTracker.GetCurrentSaveName(), out saveKey);
        }

        private static bool TryResolveFromCurrentGameInfo(out string saveKey)
        {
            saveKey = string.Empty;
            object gameInfo = Current.Game?.Info;
            if (gameInfo == null)
            {
                return false;
            }

            return TryBuildSaveKeyFromName(ReadBestSaveNameFromObject(gameInfo), out saveKey);
        }

        private static bool TryResolveFromMetaHeader(out string saveKey)
        {
            return TryBuildSaveKeyFromName(TryResolveLoadedGameNameFromMetaHeader(), out saveKey);
        }

        private static bool TryResolveFromPersistentSlot(out string saveKey)
        {
            saveKey = string.Empty;
            try
            {
                string slotId = GameComponent_RPGManager.Instance?.GetPersistentRpgSaveSlotId();
                string sanitized = string.IsNullOrWhiteSpace(slotId) ? string.Empty : slotId.SanitizeFileName();
                if (string.IsNullOrWhiteSpace(sanitized))
                {
                    return false;
                }

                saveKey = $"Save_{sanitized}".SanitizeFileName();
                return !string.IsNullOrWhiteSpace(saveKey);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryBuildSaveKeyFromName(string rawName, out string saveKey)
        {
            saveKey = string.Empty;
            string saveName = string.IsNullOrWhiteSpace(rawName) ? string.Empty : rawName.SanitizeFileName();
            if (string.IsNullOrWhiteSpace(saveName) ||
                string.Equals(saveName, DefaultSaveName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string hashKey = $"Save_{ComputeStableHash(saveName).ToString(CultureInfo.InvariantCulture)}";
            saveKey = $"{hashKey}_{saveName}".SanitizeFileName();
            return !string.IsNullOrWhiteSpace(saveKey);
        }

        private static string ReadBestSaveNameFromObject(object gameInfo)
        {
            string[] members = { "name", "Name", "fileName", "FileName" };
            for (int i = 0; i < members.Length; i++)
            {
                string value = ReadStringMember(gameInfo, members[i]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return TryResolveNameFromAnyStringMember(gameInfo);
        }

        private static string ReadStringMember(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
            {
                return string.Empty;
            }

            try
            {
                PropertyInfo prop = target.GetType().GetProperty(memberName, InstanceStringMemberBinding);
                if (prop?.PropertyType == typeof(string))
                {
                    string value = prop.GetValue(target, null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                FieldInfo field = target.GetType().GetField(memberName, InstanceStringMemberBinding);
                if (field?.FieldType == typeof(string))
                {
                    string value = field.GetValue(target) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string TryResolveNameFromAnyStringMember(object target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            try
            {
                Type type = target.GetType();
                foreach (PropertyInfo prop in type.GetProperties(InstanceStringMemberBinding))
                {
                    if (prop.PropertyType != typeof(string) || prop.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    string value = prop.GetValue(target) as string;
                    if (!string.IsNullOrWhiteSpace(value) && IsLikelySaveNameMember(prop.Name))
                    {
                        return value;
                    }
                }

                foreach (FieldInfo field in type.GetFields(InstanceStringMemberBinding))
                {
                    if (field.FieldType != typeof(string))
                    {
                        continue;
                    }

                    string value = field.GetValue(target) as string;
                    if (!string.IsNullOrWhiteSpace(value) && IsLikelySaveNameMember(field.Name))
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static bool IsLikelySaveNameMember(string memberName)
        {
            if (string.IsNullOrWhiteSpace(memberName))
            {
                return false;
            }

            string lower = memberName.ToLowerInvariant();
            return lower.Contains("name") || lower.Contains("file");
        }

        private static string TryResolveLoadedGameNameFromMetaHeader()
        {
            try
            {
                Type headerType = FindTypeInLoadedAssemblies("Verse.ScribeMetaHeaderUtility");
                if (headerType == null)
                {
                    return string.Empty;
                }

                PropertyInfo prop = headerType.GetProperty("loadedGameName", StaticStringMemberBinding);
                if (prop != null)
                {
                    string value = prop.GetValue(null, null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                FieldInfo field = headerType.GetField("loadedGameName", StaticStringMemberBinding);
                if (field != null)
                {
                    string value = field.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string BuildResolutionDiagnostic()
        {
            object gameInfo = Current.Game?.Info;
            string[] instanceMembers = { "name", "Name", "fileName", "FileName", "permadeathModeUniqueName" };
            string gameInfoValues = string.Join(", ",
                instanceMembers.Select(member => $"{member}='{ReadStringMember(gameInfo, member)}'"));
            string gameInfoType = gameInfo?.GetType().FullName ?? "null";
            string trackedSaveName = SaveContextTracker.GetCurrentSaveName();
            string metaHeader = TryResolveLoadedGameNameFromMetaHeader();
            string slot = string.Empty;
            try
            {
                slot = GameComponent_RPGManager.Instance?.GetPersistentRpgSaveSlotId() ?? string.Empty;
            }
            catch
            {
            }

            return $"gameInfoType={gameInfoType}; gameInfo={gameInfoValues}; tracked='{trackedSaveName}'; metaHeader='{metaHeader}'; slot='{slot}'";
        }

        private static Type FindTypeInLoadedAssemblies(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return null;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = assembly.GetType(fullName, false, true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static uint ComputeStableHash(string text)
        {
            string input = string.IsNullOrWhiteSpace(text) ? DefaultSaveName : text;
            uint hash = 2166136261;
            for (int i = 0; i < input.Length; i++)
            {
                hash ^= input[i];
                hash *= 16777619;
            }

            return hash;
        }
    }
}

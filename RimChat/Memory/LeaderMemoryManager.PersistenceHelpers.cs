using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Verse;

namespace RimChat.Memory
{
    /// <summary>/// Dependencies: save metadata reflection and filesystem migration helpers.
 /// Responsibility: resolve per-save folder keys and normalize/migrate leader memory payloads.
 ///</summary>
    public partial class LeaderMemoryManager
    {
        private const BindingFlags InstanceStringMemberBinding =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

        private const BindingFlags StaticStringMemberBinding =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

        private bool ShouldRefreshResolvedSaveKey()
        {
            if (string.IsNullOrWhiteSpace(_resolvedSaveKey))
            {
                return true;
            }

            string saveName = GetCurrentSaveName();
            if (string.Equals(saveName, "Default", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string expected = $"{GetHashSaveKey()}_{saveName}".SanitizeFileName();
            return !string.Equals(_resolvedSaveKey, expected, StringComparison.Ordinal);
        }

        private string ResolveCurrentSaveKey()
        {
            if (Current.Game == null)
            {
                return "Default";
            }

            string saveName = GetCurrentSaveName();
            string hashKey = GetHashSaveKey();
            return $"{hashKey}_{saveName}".SanitizeFileName();
        }

        private string GetCurrentSaveName()
        {
            object gameInfo = Current.Game?.Info;
            if (gameInfo == null)
            {
                string loadedGameName = TryResolveLoadedGameNameFromMetaHeader();
                return string.IsNullOrWhiteSpace(loadedGameName)
                    ? "Default"
                    : loadedGameName.SanitizeFileName();
            }

            string name = ReadStringMember(gameInfo, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = ReadStringMember(gameInfo, "Name");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = ReadStringMember(gameInfo, "fileName");
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                name = TryResolveNameFromAnyStringMember(gameInfo);
            }

            return string.IsNullOrWhiteSpace(name) ? "Default" : name.SanitizeFileName();
        }

        private static string ReadStringMember(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
            {
                return string.Empty;
            }

            try
            {
                var prop = target.GetType().GetProperty(memberName, InstanceStringMemberBinding);
                if (prop != null)
                {
                    string value = prop.GetValue(target) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                var field = target.GetType().GetField(memberName, InstanceStringMemberBinding);
                if (field != null)
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

        private string GetHashSaveKey()
        {
            string saveName = GetCurrentSaveName();
            return $"Save_{ComputeStableHash(saveName).ToString(CultureInfo.InvariantCulture)}".SanitizeFileName();
        }

        private static void NormalizeMemoryData(FactionLeaderMemory memory)
        {
            if (memory == null)
            {
                return;
            }

            memory.DialogueHistory = (memory.DialogueHistory ?? new List<DialogueRecord>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Message))
                .GroupBy(item => $"{item.GameTick}|{item.IsPlayer}|{item.Message.Trim()}")
                .Select(group => group.First())
                .OrderBy(item => item.GameTick)
                .ToList();

            if (memory.DialogueHistory.Count > 200)
            {
                memory.DialogueHistory.RemoveRange(0, memory.DialogueHistory.Count - 200);
            }

            memory.SignificantEvents = (memory.SignificantEvents ?? new List<SignificantEventMemory>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Description))
                .GroupBy(item => $"{item.OccurredTick}|{item.EventType}|{item.InvolvedFactionId}|{item.Description.Trim()}")
                .Select(group => group.OrderByDescending(item => item.Timestamp).First())
                .OrderByDescending(item => item.OccurredTick)
                .Take(MaxSignificantEvents)
                .ToList();
        }

        private static uint ComputeStableHash(string text)
        {
            string input = string.IsNullOrWhiteSpace(text) ? "Default" : text;
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

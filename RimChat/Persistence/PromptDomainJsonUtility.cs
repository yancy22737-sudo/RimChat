using System;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: System.IO, UTF-8 encoding, and Unity JsonUtility.
    /// Responsibility: load, overlay, clone, and persist prompt-domain JSON payloads.
    /// </summary>
    internal static class PromptDomainJsonUtility
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static T LoadMerged<T>(string defaultPath, string customPath) where T : class, new()
        {
            T defaultPayload = LoadSingle<T>(defaultPath);
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            {
                T customPayload = LoadSingle<T>(customPath);
                if (customPayload != null)
                {
                    return customPayload;
                }
            }

            return defaultPayload ?? new T();
        }

        public static T LoadSingle<T>(string path) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new T();
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new T();
                }

                if (ReflectionJsonFieldDeserializer.TryDeserialize(json, out T reflectionValue) && reflectionValue != null)
                {
                    return reflectionValue;
                }

                return JsonUtility.FromJson<T>(json) ?? new T();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to load prompt-domain JSON from {path}: {ex.Message}");
                return new T();
            }
        }

        public static void OverwriteFromFile<T>(string path, T target) where T : class
        {
            if (target == null || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    JsonUtility.FromJsonOverwrite(json, target);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to load prompt-domain JSON from {path}: {ex.Message}");
            }
        }

        public static bool TryDeserialize<T>(string json, out T payload) where T : class, new()
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                if (ReflectionJsonFieldDeserializer.TryDeserialize(json, out payload) && payload != null)
                {
                    return true;
                }

                payload = JsonUtility.FromJson<T>(json) ?? new T();
                return payload != null;
            }
            catch
            {
                payload = null;
                return false;
            }
        }

        public static string Serialize<T>(T payload, bool prettyPrint = true) where T : class
        {
            return JsonUtility.ToJson(payload, prettyPrint);
        }

        public static void WriteToFile<T>(string path, T payload, bool prettyPrint = true) where T : class
        {
            if (string.IsNullOrWhiteSpace(path) || payload == null)
            {
                return;
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, Serialize(payload, prettyPrint), Utf8NoBom);
        }

        public static T Clone<T>(T payload) where T : class, new()
        {
            if (payload == null)
            {
                return new T();
            }

            string json = JsonUtility.ToJson(payload);
            return JsonUtility.FromJson<T>(json) ?? new T();
        }
    }
}

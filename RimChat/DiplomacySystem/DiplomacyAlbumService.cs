using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using RimChat.Memory;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: RimWorld save metadata, file IO, and Unity OpenURL.
    /// Responsibility: persist manually saved images into album directory and open containing folders.
    /// </summary>
    public static class DiplomacyAlbumService
    {
        private const string PromptNpcFolderName = "Prompt";
        private const string PromptNpcSubFolderName = "NPC";
        private const string AlbumSubFolderName = "diplomacy_album";
        private static readonly BindingFlags InstanceStringMemberBinding =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

        public static bool SaveToAlbum(
            string sourcePath,
            AlbumImageEntry metadata,
            out AlbumImageEntry savedEntry,
            out string error)
        {
            savedEntry = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "source image file not found";
                return false;
            }

            try
            {
                string albumDir = ResolveAlbumDirectory();
                if (!Directory.Exists(albumDir))
                {
                    Directory.CreateDirectory(albumDir);
                }

                string extension = NormalizeImageExtension(Path.GetExtension(sourcePath));
                string fileName = BuildUniqueFileName(albumDir, metadata, extension);
                string targetPath = Path.Combine(albumDir, fileName);
                File.Copy(sourcePath, targetPath, false);

                savedEntry = CreateEntry(metadata, sourcePath, targetPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException()?.Message ?? ex.Message;
                return false;
            }
        }

        public static bool OpenImageDirectory(AlbumImageEntry item, out string error)
        {
            error = string.Empty;
            string path = item?.AlbumPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "album path is empty";
                return false;
            }

            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                error = "image directory not found";
                return false;
            }

            try
            {
                string normalized = directory.Replace('\\', '/');
                Application.OpenURL("file:///" + normalized.TrimStart('/'));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException()?.Message ?? ex.Message;
                return false;
            }
        }

        public static string ResolveAlbumDirectory()
        {
            return Path.Combine(ResolvePromptNpcRootPath(), ResolveCurrentSaveKey(), AlbumSubFolderName);
        }

        private static AlbumImageEntry CreateEntry(AlbumImageEntry source, string sourcePath, string albumPath)
        {
            var entry = new AlbumImageEntry
            {
                Id = !string.IsNullOrWhiteSpace(source?.Id) ? source.Id : Guid.NewGuid().ToString("N"),
                SavedTick = source?.SavedTick ?? (Find.TickManager?.TicksGame ?? 0),
                SourcePath = sourcePath ?? string.Empty,
                AlbumPath = albumPath ?? string.Empty,
                Caption = source?.Caption ?? string.Empty,
                FactionId = source?.FactionId ?? string.Empty,
                NegotiatorId = source?.NegotiatorId ?? string.Empty,
                Size = source?.Size ?? string.Empty,
                SourceType = AlbumImageEntry.NormalizeSourceType(source?.SourceType)
            };
            return entry;
        }

        private static string BuildUniqueFileName(string folder, AlbumImageEntry metadata, string extension)
        {
            string baseName = BuildBaseFileName(metadata);
            string candidate = baseName + extension;
            int suffix = 1;
            while (File.Exists(Path.Combine(folder, candidate)))
            {
                candidate = $"{baseName}_{suffix}{extension}";
                suffix++;
            }

            return candidate;
        }

        private static string BuildBaseFileName(AlbumImageEntry metadata)
        {
            string title = (metadata?.Caption ?? "album").Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "album";
            }

            title = title.SanitizeFileName();
            if (title.Length > 48)
            {
                title = title.Substring(0, 48);
            }

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            return $"{title}_{stamp}";
        }

        private static string NormalizeImageExtension(string extension)
        {
            string normalized = (extension ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case ".jpg":
                case ".jpeg":
                case ".bmp":
                case ".gif":
                case ".webp":
                case ".png":
                    return normalized;
                default:
                    return ".png";
            }
        }

        private static string ResolvePromptNpcRootPath()
        {
            try
            {
                var mod = LoadedModManager.GetMod<Core.RimChatMod>();
                if (mod?.Content != null)
                {
                    string root = Path.Combine(mod.Content.RootDir, PromptNpcFolderName, PromptNpcSubFolderName);
                    if (!Directory.Exists(root))
                    {
                        Directory.CreateDirectory(root);
                    }
                    return root;
                }
            }
            catch
            {
            }

            string fallback = Path.Combine(GenFilePaths.ConfigFolderPath, "RimChat", PromptNpcFolderName, PromptNpcSubFolderName);
            if (!Directory.Exists(fallback))
            {
                Directory.CreateDirectory(fallback);
            }
            return fallback;
        }

        private static string ResolveCurrentSaveKey()
        {
            return SaveScopeKeyResolver.ResolveOrThrow();
        }

        private static string ResolveCurrentSaveName()
        {
            object gameInfo = Current.Game?.Info;
            if (gameInfo == null)
            {
                return "Default";
            }

            string[] members = { "name", "Name", "fileName", "FileName" };
            for (int i = 0; i < members.Length; i++)
            {
                string value = ReadStringMember(gameInfo, members[i]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.SanitizeFileName();
                }
            }

            return "Default";
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
                    return prop.GetValue(target, null) as string ?? string.Empty;
                }

                FieldInfo field = target.GetType().GetField(memberName, InstanceStringMemberBinding);
                if (field?.FieldType == typeof(string))
                {
                    return field.GetValue(target) as string ?? string.Empty;
                }
            }
            catch
            {
            }

            return string.Empty;
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

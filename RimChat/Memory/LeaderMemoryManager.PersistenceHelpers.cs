using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Verse;

namespace RimChat.Memory
{
    /// <summary>
    /// Dependencies: SaveScopeKeyResolver and filesystem migration helpers.
    /// Responsibility: keep leader-memory persistence on one strict save-key contract.
    /// </summary>
    public partial class LeaderMemoryManager
    {
        private struct JsonCopyStats
        {
            public int Copied;
            public int SkippedExisting;
        }

        private bool ShouldRefreshResolvedSaveKey()
        {
            if (string.IsNullOrWhiteSpace(_resolvedSaveKey))
            {
                return true;
            }

            if (!SaveScopeKeyResolver.TryResolveStrict(out string expected, out _))
            {
                return true;
            }

            return !string.Equals(_resolvedSaveKey, expected, StringComparison.Ordinal);
        }

        private string ResolveCurrentSaveKey()
        {
            return SaveScopeKeyResolver.ResolveOrThrow();
        }

        private void TryMigrateLegacyMemories(string currentSaveKey)
        {
            if (string.IsNullOrWhiteSpace(currentSaveKey))
            {
                return;
            }

            string targetDir = CurrentSaveDataPath;
            string markerPath = Path.Combine(targetDir, $".migration_complete_{currentSaveKey}.marker");
            if (File.Exists(markerPath))
            {
                return;
            }

            Directory.CreateDirectory(targetDir);
            List<string> legacyDirs = CollectLegacyMemorySourceDirectories(targetDir);
            if (legacyDirs.Count == 0 || HasClaimedDefaultBucketForAnotherSave(currentSaveKey, legacyDirs))
            {
                return;
            }

            string backupRoot = Path.Combine(
                CurrentPromptNpcRootPath,
                LegacyMigrationBackupDirName,
                $"{DateTime.UtcNow:yyyyMMddHHmmss}_{currentSaveKey}_leader_memories");

            int copied = 0;
            int skippedExisting = 0;
            for (int i = 0; i < legacyDirs.Count; i++)
            {
                string sourceDir = legacyDirs[i];
                string backupDir = Path.Combine(backupRoot, $"source_{i}");
                Directory.CreateDirectory(backupDir);
                CopyJsonFiles(sourceDir, backupDir, overwrite: true);
                JsonCopyStats stats = CopyJsonFiles(sourceDir, targetDir, overwrite: false);
                copied += stats.Copied;
                skippedExisting += stats.SkippedExisting;
            }

            File.WriteAllText(markerPath, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            TryClaimDefaultBucket(currentSaveKey, legacyDirs);
            Log.Message(
                "[RimChat] Migrated legacy leader memory files. " +
                $"copied={copied}, skipped_existing={skippedExisting}, target={currentSaveKey}.");
        }

        private List<string> CollectLegacyMemorySourceDirectories(string targetDir)
        {
            var dirs = new List<string>();
            string rootLevelLegacyDir = Path.Combine(CurrentPromptNpcRootPath, LeaderMemorySubDir);
            TryAddLegacySourceDir(dirs, rootLevelLegacyDir, targetDir);

            string[] saveDirs = Directory.Exists(CurrentPromptNpcRootPath)
                ? Directory.GetDirectories(CurrentPromptNpcRootPath, "Save_*")
                : Array.Empty<string>();
            for (int i = 0; i < saveDirs.Length; i++)
            {
                string dirName = Path.GetFileName(saveDirs[i]);
                if (!dirName.EndsWith($"_{DefaultSaveName}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string legacyDir = Path.Combine(saveDirs[i], LeaderMemorySubDir);
                TryAddLegacySourceDir(dirs, legacyDir, targetDir);
            }

            return dirs;
        }

        private static void TryAddLegacySourceDir(List<string> dirs, string sourceDir, string targetDir)
        {
            if (string.IsNullOrWhiteSpace(sourceDir) || !DirectoryHasJsonFiles(sourceDir))
            {
                return;
            }

            if (string.Equals(sourceDir, targetDir, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (dirs.Any(existing => string.Equals(existing, sourceDir, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            dirs.Add(sourceDir);
        }

        private static JsonCopyStats CopyJsonFiles(string sourceDir, string targetDir, bool overwrite)
        {
            var stats = new JsonCopyStats();
            if (!DirectoryHasJsonFiles(sourceDir))
            {
                return stats;
            }

            Directory.CreateDirectory(targetDir);
            string[] files = Directory.GetFiles(sourceDir, "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                string targetPath = Path.Combine(targetDir, fileName);
                if (!overwrite && File.Exists(targetPath))
                {
                    stats.SkippedExisting++;
                    continue;
                }

                File.Copy(files[i], targetPath, overwrite);
                stats.Copied++;
            }

            return stats;
        }

        private bool HasClaimedDefaultBucketForAnotherSave(string currentSaveKey, List<string> legacyDirs)
        {
            if (legacyDirs == null || legacyDirs.Count == 0 || !legacyDirs.Any(IsDefaultBucketPath))
            {
                return false;
            }

            string claimPath = Path.Combine(CurrentPromptNpcRootPath, LegacyDefaultBucketClaimMarker);
            if (!File.Exists(claimPath))
            {
                return false;
            }

            string claimedSaveKey = File.ReadAllText(claimPath).Trim();
            if (string.IsNullOrWhiteSpace(claimedSaveKey))
            {
                return false;
            }

            return !string.Equals(claimedSaveKey, currentSaveKey, StringComparison.Ordinal);
        }

        private void TryClaimDefaultBucket(string currentSaveKey, List<string> legacyDirs)
        {
            if (legacyDirs == null || legacyDirs.Count == 0 || !legacyDirs.Any(IsDefaultBucketPath))
            {
                return;
            }

            string claimPath = Path.Combine(CurrentPromptNpcRootPath, LegacyDefaultBucketClaimMarker);
            if (!File.Exists(claimPath))
            {
                File.WriteAllText(claimPath, currentSaveKey);
            }
        }

        private static bool IsDefaultBucketPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string normalized = path.Replace('\\', '/');
            return normalized.Contains("/Save_") &&
                normalized.EndsWith($"/{LeaderMemorySubDir}", StringComparison.OrdinalIgnoreCase) &&
                normalized.Contains($"_{DefaultSaveName}/");
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
    }
}

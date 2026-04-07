using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimChat.Persistence;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    internal sealed partial class PromptPresetService : IPromptPresetService
    {
        [Serializable]
        private sealed class JsonStringWrapper
        {
            public string value = string.Empty;
        }

        [Serializable]
        private sealed class LegacyPromptPresetStoreConfig
        {
            public List<LegacyPromptPresetConfig> Presets = new List<LegacyPromptPresetConfig>();
        }

        [Serializable]
        private sealed class LegacyPromptPresetConfig
        {
            public LegacyPromptPresetChannelPayloads ChannelPayloads = new LegacyPromptPresetChannelPayloads();
        }

        [Serializable]
        private sealed class LegacyPromptPresetChannelPayloads
        {
            public RimTalkPromptEntryDefaultsConfig PromptSectionCatalog = RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
            public bool EnableRimTalkPromptCompat = true;
            public int RimTalkPresetInjectionMaxEntries = RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
            public int RimTalkPresetInjectionMaxChars = RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
            public string RimTalkCompatTemplate = string.Empty;
            public RimTalkChannelCompatConfig RimTalkDiplomacy = null;
            public RimTalkChannelCompatConfig RimTalkRpg = null;
        }

        private const int CurrentSchemaVersion = 2;
        private const int LegacyRpgNodeMigrationVersion = 2;
        private const int LegacySocialNewsNodeMigrationVersion = 3;
        private const string ImmutableDefaultPresetId = "rimchat_default_preset";
        private const string ImmutableDefaultPresetName = "Default";
        private const string PresetStoreFileName = "PromptPresets_Custom.json";
        private const string CorruptStoreFileSuffix = ".corrupt";
        private static readonly string ConfigStoreDirectory = Path.Combine(
            GenFilePaths.ConfigFolderPath,
            "RimChat",
            PromptDomainFileCatalog.PromptFolderName,
            PromptDomainFileCatalog.CustomSubFolderName);

        public PromptPresetStoreConfig LoadAll(RimChatSettings settings)
        {
            bool readCorrupted;
            PromptPresetStoreConfig store = ReadStoreFile(out readCorrupted);
            if (store == null)
            {
                store = new PromptPresetStoreConfig();
            }

            NormalizeStore(settings, store, persistWhenEmpty: !readCorrupted);
            if (readCorrupted)
            {
                Log.Warning("[RimChat] Prompt preset store load failed due to corruption. Using in-memory defaults without overwriting disk data.");
            }

            return store;
        }

        public void SaveAll(PromptPresetStoreConfig store)
        {
            PromptPresetStoreConfig normalized = store ?? new PromptPresetStoreConfig();
            normalized.SchemaVersion = CurrentSchemaVersion;
            normalized.Presets ??= new List<PromptPresetConfig>();
            EnforceImmutableDefaultPreset(normalized);
            if (string.IsNullOrWhiteSpace(normalized.DefaultPresetId) ||
                !normalized.Presets.Any(p => string.Equals(p.Id, normalized.DefaultPresetId, StringComparison.Ordinal)))
            {
                normalized.DefaultPresetId = ResolveDefaultPresetId(normalized.Presets);
            }

            EnsureStoreDirectory();
            string path = GetStorePath();
            string tempPath = path + ".tmp";
            string json = ReflectionJsonFieldSerializer.Serialize(normalized, prettyPrint: true);
            Log.Message($"[RimChat][PresetDiag] SaveAll begin. presets={normalized.Presets.Count}, active={normalized.ActivePresetId}, default={normalized.DefaultPresetId}, path={path}");
            if (normalized.Presets.Count > 0 &&
                json.IndexOf("\"Presets\"", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("[RimChat] Prompt preset store serialization dropped preset list. Save aborted.");
            }

            try
            {
                AtomicWriteText(path, tempPath, json);
                MirrorStoreToLegacyPath(path);
                Log.Message($"[RimChat][PresetDiag] SaveAll done. presets={normalized.Presets.Count}, bytes={new FileInfo(path).Length}");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Best-effort cleanup; keep original failure reason.
                }
            }
        }

        public PromptPresetConfig CreateFromLegacy(RimChatSettings settings, string name)
        {
            settings?.FlushPromptEditorsToStorageForPreset(persistToFiles: false);
            PromptPresetConfig preset = BuildPresetShell(name);
            preset.ChannelPayloads = CaptureCurrentPayload(settings);
            return preset;
        }

        public PromptPresetConfig Duplicate(RimChatSettings settings, PromptPresetConfig source, string name)
        {
            PromptPresetConfig duplicated = BuildPresetShell(name);
            duplicated.ChannelPayloads = source?.ChannelPayloads?.Clone() ?? CaptureCurrentPayload(settings);
            return duplicated;
        }

        public bool Activate(RimChatSettings settings, PromptPresetStoreConfig store, string presetId, out string error)
        {
            error = string.Empty;
            if (settings == null)
            {
                error = "Settings unavailable.";
                return false;
            }

            PromptPresetConfig target = store?.Presets?.FirstOrDefault(p => string.Equals(p.Id, presetId, StringComparison.Ordinal));
            if (target == null)
            {
                error = "Preset not found.";
                return false;
            }

            try
            {
                ApplyPayloadToSettings(settings, target.ChannelPayloads, persistToFiles: true);
                target.IsActive = true;
                if (!IsImmutableDefaultId(target.Id))
                {
                    target.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                }

                if (store?.Presets != null)
                {
                    for (int i = 0; i < store.Presets.Count; i++)
                    {
                        PromptPresetConfig preset = store.Presets[i];
                        if (!string.Equals(preset.Id, target.Id, StringComparison.Ordinal))
                        {
                            preset.IsActive = false;
                        }
                    }
                }

                if (store != null)
                {
                    store.ActivePresetId = target.Id;
                }

                settings.RefreshPromptEditorStateFromStorage();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool IsDefaultPreset(PromptPresetStoreConfig store, string presetId)
        {
            if (store?.Presets == null || store.Presets.Count == 0 || string.IsNullOrWhiteSpace(presetId))
            {
                return false;
            }

            return IsImmutableDefaultId(presetId);
        }

        public bool EnsureEditablePresetForMutation(
            RimChatSettings settings,
            PromptPresetStoreConfig store,
            string selectedPresetId,
            string forkNamePrefix,
            out PromptPresetConfig editablePreset,
            out bool forked,
            out string error)
        {
            editablePreset = null;
            forked = false;
            error = string.Empty;
            if (settings == null)
            {
                error = "Settings unavailable.";
                return false;
            }

            if (store?.Presets == null || store.Presets.Count == 0)
            {
                error = "Preset store unavailable.";
                return false;
            }

            PromptPresetConfig selected = store.Presets.FirstOrDefault(p => string.Equals(p.Id, selectedPresetId, StringComparison.Ordinal))
                                        ?? store.Presets.FirstOrDefault(p => string.Equals(p.Id, store.ActivePresetId, StringComparison.Ordinal))
                                        ?? store.Presets.FirstOrDefault(p => p.IsActive)
                                        ?? store.Presets[0];
            if (selected == null)
            {
                error = "Selected preset unavailable.";
                return false;
            }

            if (!IsDefaultPreset(store, selected.Id))
            {
                editablePreset = selected;
                return true;
            }

            string prefix = string.IsNullOrWhiteSpace(forkNamePrefix) ? "Custom" : forkNamePrefix.Trim();
            string autoForkName = EnsureUniqueName(store.Presets, BuildTimestampPresetName(prefix, DateTime.Now));
            PromptPresetConfig forkPreset = Duplicate(settings, selected, autoForkName);
            if (forkPreset == null)
            {
                error = "Failed to create fork preset.";
                return false;
            }

            store.Presets.Add(forkPreset);
            if (!Activate(settings, store, forkPreset.Id, out string activateError))
            {
                store.Presets.RemoveAll(p => string.Equals(p.Id, forkPreset.Id, StringComparison.Ordinal));
                error = activateError ?? "Failed to activate fork preset.";
                return false;
            }

            SaveAll(store);
            editablePreset = forkPreset;
            forked = true;
            return true;
        }

        public bool SyncPresetPayloadFromSettings(
            RimChatSettings settings,
            PromptPresetStoreConfig store,
            string presetId,
            out string error)
        {
            error = string.Empty;
            if (settings == null)
            {
                error = "Settings unavailable.";
                return false;
            }

            if (store?.Presets == null || store.Presets.Count == 0)
            {
                error = "Preset store unavailable.";
                return false;
            }

            PromptPresetConfig target = store.Presets.FirstOrDefault(p => string.Equals(p.Id, presetId, StringComparison.Ordinal))
                                      ?? store.Presets.FirstOrDefault(p => string.Equals(p.Id, store.ActivePresetId, StringComparison.Ordinal))
                                      ?? store.Presets.FirstOrDefault(p => p.IsActive)
                                      ?? store.Presets[0];
            if (target == null)
            {
                error = "Preset not found.";
                return false;
            }

            if (IsImmutableDefaultId(target.Id))
            {
                error = "Default preset is read-only.";
                return false;
            }

            target.ChannelPayloads = CaptureCurrentPayload(settings);
            target.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
            return true;
        }

        public void ApplyPayloadToSettings(RimChatSettings settings, PromptPresetChannelPayloads payload, bool persistToFiles)
        {
            if (settings == null)
            {
                return;
            }

            PromptPresetChannelPayloads data = payload?.Clone() ?? CreateCanonicalDefaultPayload();
            NormalizePayload(data);
            if (persistToFiles)
            {
                ApplyPayloadToCustomFiles(data);
                PersistRpgPromptCustomStore(data);
            }

            ApplyRimTalkCompatSettings(settings, data, persistToFiles);
        }

        public bool ExportPreset(string filePath, PromptPresetConfig preset, out string error)
        {
            error = string.Empty;
            if (preset == null)
            {
                error = "Preset is null.";
                return false;
            }

            try
            {
                string normalizedPath = NormalizePresetFilePath(filePath);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    error = "File path is empty.";
                    return false;
                }

                string dir = Path.GetDirectoryName(normalizedPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = ReflectionJsonFieldSerializer.Serialize(preset, prettyPrint: true);
                if (json.IndexOf("\"ChannelPayloads\"", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException("[RimChat] Prompt preset export serialization dropped channel payloads.");
                }

                File.WriteAllText(normalizedPath, json);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool ImportPreset(string filePath, PromptPresetStoreConfig store, out PromptPresetConfig imported, out string error)
        {
            imported = null;
            error = string.Empty;
            string normalizedPath = NormalizePresetFilePath(filePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                error = "File path is empty.";
                return false;
            }

            if (!File.Exists(normalizedPath))
            {
                error = "File not found.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(normalizedPath);
                if (json.IndexOf("\"UnifiedPromptCatalog\"", StringComparison.Ordinal) < 0)
                {
                    error = "Unsupported legacy preset format. Please export with unified preset schema.";
                    return false;
                }

                if (!ReflectionJsonFieldDeserializer.TryDeserialize(json, out PromptPresetConfig parsed) ||
                    parsed == null)
                {
                    error = "Invalid preset file.";
                    return false;
                }

                ApplyLegacyPayloadFromJson(parsed, json, "preset.import");
                NormalizePreset(parsed);
                parsed.Id = Guid.NewGuid().ToString("N");
                parsed.IsActive = false;
                parsed.Name = EnsureUniqueName(store?.Presets, parsed.Name);
                parsed.CreatedAtUtc = DateTime.UtcNow.ToString("o");
                parsed.UpdatedAtUtc = parsed.CreatedAtUtc;
                imported = parsed;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string NormalizePresetFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return string.Empty;
            }

            return filePath.Trim().Trim('"');
        }

        public List<PromptPresetSummary> BuildSummaries(PromptPresetStoreConfig store)
        {
            var list = new List<PromptPresetSummary>();
            List<PromptPresetConfig> presets = store?.Presets ?? new List<PromptPresetConfig>();
            for (int i = 0; i < presets.Count; i++)
            {
                PromptPresetConfig preset = presets[i];
                PromptPresetChannelPayloads payload = preset.ChannelPayloads ?? new PromptPresetChannelPayloads();
                list.Add(new PromptPresetSummary
                {
                    Id = preset.Id,
                    Name = preset.Name,
                    IsActive = preset.IsActive,
                    IsDefault = IsDefaultPreset(store, preset.Id),
                    DiplomacyChars = (payload.Diplomacy?.SystemPromptCustomJson?.Length ?? 0) +
                                     (payload.Diplomacy?.DialoguePromptCustomJson?.Length ?? 0),
                    RpgChars = payload.Rpg?.PawnPromptCustomJson?.Length ?? 0,
                    PromptSectionChars = PromptDomainJsonUtility.Serialize(payload.UnifiedPromptCatalog, prettyPrint: false)?.Length ?? 0
                });
            }

            return list;
        }

        private static PromptPresetConfig BuildPresetShell(string name)
        {
            string now = DateTime.UtcNow.ToString("o");
            return new PromptPresetConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrWhiteSpace(name) ? "Preset" : name.Trim(),
                IsActive = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ChannelPayloads = new PromptPresetChannelPayloads()
            };
        }

        private static void NormalizeStore(RimChatSettings settings, PromptPresetStoreConfig store, bool persistWhenEmpty = true)
        {
            store.SchemaVersion = CurrentSchemaVersion;
            store.Presets ??= new List<PromptPresetConfig>();
            for (int i = 0; i < store.Presets.Count; i++)
            {
                NormalizePreset(store.Presets[i]);
                if (!IsImmutableDefaultId(store.Presets[i].Id) &&
                    !HasMeaningfulPayload(store.Presets[i].ChannelPayloads))
                {
                    store.Presets[i].ChannelPayloads = CaptureCurrentPayload(settings);
                    store.Presets[i].UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                }

                if (!IsImmutableDefaultId(store.Presets[i].Id))
                {
                    ApplyLegacyRpgNodeMigrationIfNeeded(store.Presets[i]);
                    ApplyLegacySocialNewsNodeMigrationIfNeeded(store.Presets[i]);
                }
            }

            EnforceImmutableDefaultPreset(store);

            if (store.Presets.Count == 0)
            {
                PromptPresetService factory = new PromptPresetService();
                PromptPresetConfig canonical = CreateCanonicalDefaultPreset(ImmutableDefaultPresetName);
                canonical.IsActive = true;
                store.Presets.Add(canonical);
                store.DefaultPresetId = canonical.Id;
                PromptPresetChannelPayloads legacyPayload = CaptureCurrentPayload(settings);
                if (ShouldCreateMigratedPreset(legacyPayload, canonical.ChannelPayloads))
                {
                    PromptPresetConfig migrated = BuildPresetShell("Migrated");
                    migrated.ChannelPayloads = legacyPayload;
                    store.Presets.Add(migrated);
                }

                store.ActivePresetId = canonical.Id;
                if (persistWhenEmpty)
                {
                    factory.SaveAll(store);
                }
                return;
            }

            PromptPresetConfig active = store.Presets.FirstOrDefault(p => string.Equals(p.Id, store.ActivePresetId, StringComparison.Ordinal))
                                      ?? store.Presets.FirstOrDefault(p => p.IsActive)
                                      ?? store.Presets[0];
            store.ActivePresetId = active.Id;
            if (string.IsNullOrWhiteSpace(store.DefaultPresetId) ||
                !store.Presets.Any(p => string.Equals(p.Id, store.DefaultPresetId, StringComparison.Ordinal)))
            {
                store.DefaultPresetId = ResolveDefaultPresetId(store.Presets);
            }
            store.DefaultPresetId = ImmutableDefaultPresetId;

            for (int i = 0; i < store.Presets.Count; i++)
            {
                store.Presets[i].IsActive = string.Equals(store.Presets[i].Id, active.Id, StringComparison.Ordinal);
            }
        }

        private static void NormalizePreset(PromptPresetConfig preset)
        {
            if (preset == null)
            {
                return;
            }

            preset.Id = string.IsNullOrWhiteSpace(preset.Id) ? Guid.NewGuid().ToString("N") : preset.Id.Trim();
            preset.Name = string.IsNullOrWhiteSpace(preset.Name) ? "Preset" : preset.Name.Trim();
            preset.ChannelPayloads ??= new PromptPresetChannelPayloads();
            NormalizePayload(preset.ChannelPayloads);

            if (string.IsNullOrWhiteSpace(preset.CreatedAtUtc))
            {
                preset.CreatedAtUtc = DateTime.UtcNow.ToString("o");
            }

            if (string.IsNullOrWhiteSpace(preset.UpdatedAtUtc))
            {
                preset.UpdatedAtUtc = preset.CreatedAtUtc;
            }
        }

        private static PromptPresetStoreConfig ReadStoreFile(out bool readCorrupted)
        {
            readCorrupted = false;
            TryMigrateLegacyStoreToConfigPath();
            string primaryPath = GetStorePath();
            string legacyPath = GetLegacyStorePath();

            bool primaryCorrupted;
            PromptPresetStoreConfig primaryStore = TryReadStoreFile(primaryPath, out primaryCorrupted);
            if (primaryCorrupted)
            {
                readCorrupted = true;
            }

            bool hasLegacy = !string.IsNullOrWhiteSpace(legacyPath) &&
                             !string.Equals(primaryPath, legacyPath, StringComparison.OrdinalIgnoreCase) &&
                             File.Exists(legacyPath);
            bool legacyCorrupted = false;
            PromptPresetStoreConfig legacyStore = null;
            if (hasLegacy)
            {
                legacyStore = TryReadStoreFile(legacyPath, out legacyCorrupted);
                if (legacyCorrupted)
                {
                    readCorrupted = true;
                }
            }

            PromptPresetStoreConfig chosen = ChooseRicherStore(primaryStore, legacyStore);
            if (chosen == null)
            {
                return null;
            }

            // If legacy has richer data than primary, self-heal primary for next launch.
            if (hasLegacy && ReferenceEquals(chosen, legacyStore))
            {
                try
                {
                    EnsureStoreDirectory();
                    SaveStoreToPath(primaryPath, chosen);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat] Failed to self-heal preset store from legacy path: {ex.Message}");
                }
            }

            return chosen;
        }

        private static PromptPresetStoreConfig TryReadStoreFile(string path, out bool corrupted)
        {
            corrupted = false;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                PromptPresetStoreConfig store = JsonUtility.FromJson<PromptPresetStoreConfig>(json);
                if (store == null)
                {
                    corrupted = true;
                    Log.Warning($"[RimChat] Prompt preset store JSON parsed to null. path={path}");
                    QuarantineCorruptedStoreFile(path, "json parsed to null");
                    return null;
                }

                if (json.IndexOf("\"Presets\"", StringComparison.Ordinal) < 0 &&
                    (json.IndexOf("\"ActivePresetId\"", StringComparison.Ordinal) >= 0 ||
                     json.IndexOf("\"DefaultPresetId\"", StringComparison.Ordinal) >= 0))
                {
                    Log.Warning($"[RimChat] Prompt preset store file has IDs but missing preset list. path={path}");
                }

                int rawPresetCountHint = CountPresetObjectsHint(json);
                int parsedPresetCount = store.Presets?.Count ?? 0;
                if (rawPresetCountHint > parsedPresetCount &&
                    TryRecoverPresetListFromRawJson(json, out List<PromptPresetConfig> recovered) &&
                    recovered.Count >= rawPresetCountHint)
                {
                    store.Presets = recovered;
                    Log.Warning(
                        $"[RimChat][PresetDiag] Recovered preset list from raw JSON. " +
                        $"path={path}, parsed={parsedPresetCount}, recovered={recovered.Count}");
                }

                ApplyLegacyPayloadsFromStoreJson(store, json);
                Log.Message($"[RimChat][PresetDiag] ReadStore success. path={path}, presets={store.Presets?.Count ?? 0}");
                return store;
            }
            catch (Exception ex)
            {
                corrupted = true;
                QuarantineCorruptedStoreFile(path, ex.Message);
                Log.Warning($"[RimChat] Failed to read prompt preset store: {ex.Message}. path={path}");
                return null;
            }
        }

        private static int CountPresetObjectsHint(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return 0;
            }

            return CountOccurrences(rawJson, "\"CreatedAtUtc\"");
        }

        private static int CountOccurrences(string text, string token)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
            {
                return 0;
            }

            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }

            return count;
        }

        private static bool TryRecoverPresetListFromRawJson(string rawJson, out List<PromptPresetConfig> recovered)
        {
            recovered = new List<PromptPresetConfig>();
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return false;
            }

            int keyIndex = rawJson.IndexOf("\"Presets\"", StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return false;
            }

            int arrayStart = rawJson.IndexOf('[', keyIndex);
            if (arrayStart < 0)
            {
                return false;
            }

            if (!TryFindMatchingBracket(rawJson, arrayStart, '[', ']', out int arrayEnd))
            {
                return false;
            }

            int cursor = arrayStart + 1;
            while (cursor < arrayEnd)
            {
                int objStart = FindNextNonStringChar(rawJson, cursor, arrayEnd, '{');
                if (objStart < 0 || objStart >= arrayEnd)
                {
                    break;
                }

                if (!TryFindMatchingBracket(rawJson, objStart, '{', '}', out int objEnd))
                {
                    return false;
                }

                string objectJson = rawJson.Substring(objStart, objEnd - objStart + 1);
                PromptPresetConfig parsed = null;
                ReflectionJsonFieldDeserializer.TryDeserialize(objectJson, out parsed);

                if (parsed != null)
                {
                    recovered.Add(parsed);
                }

                cursor = objEnd + 1;
            }

            return recovered.Count > 0;
        }

        private static int FindNextNonStringChar(string text, int start, int endExclusive, char target)
        {
            bool inString = false;
            bool escaped = false;
            for (int i = Math.Max(0, start); i < endExclusive; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == target)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryFindMatchingBracket(string text, int startIndex, char open, char close, out int endIndex)
        {
            endIndex = -1;
            if (string.IsNullOrEmpty(text) || startIndex < 0 || startIndex >= text.Length || text[startIndex] != open)
            {
                return false;
            }

            bool inString = false;
            bool escaped = false;
            int depth = 0;
            for (int i = startIndex; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == open)
                {
                    depth++;
                    continue;
                }

                if (c == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        endIndex = i;
                        return true;
                    }
                }
            }

            return false;
        }

        private static PromptPresetStoreConfig ChooseRicherStore(
            PromptPresetStoreConfig primary,
            PromptPresetStoreConfig legacy)
        {
            if (primary == null)
            {
                return legacy;
            }

            if (legacy == null)
            {
                return primary;
            }

            int primaryCount = primary.Presets?.Count ?? 0;
            int legacyCount = legacy.Presets?.Count ?? 0;
            if (legacyCount > primaryCount)
            {
                return legacy;
            }

            if (primaryCount > legacyCount)
            {
                return primary;
            }

            return primary;
        }

        private static void QuarantineCorruptedStoreFile(string path, string reason)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            string corruptPath = path + CorruptStoreFileSuffix + "." + timestamp;
            int suffix = 1;
            while (File.Exists(corruptPath))
            {
                suffix++;
                corruptPath = path + CorruptStoreFileSuffix + "." + timestamp + "." + suffix;
            }

            try
            {
                File.Move(path, corruptPath);
                Log.Error($"[RimChat] Quarantined corrupted preset store: {corruptPath}. reason={reason}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to quarantine corrupted preset store '{path}': {ex.Message}. reason={reason}");
            }
        }

        private static string GetStorePath()
        {
            return Path.Combine(ConfigStoreDirectory, PresetStoreFileName);
        }

        private static string GetLegacyStorePath()
        {
            return PromptDomainFileCatalog.GetCustomPath(PresetStoreFileName);
        }

        private static void EnsureStoreDirectory()
        {
            string path = GetStorePath();
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static void AtomicWriteText(string path, string tempPath, string content)
        {
            File.WriteAllText(tempPath, content);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat][PresetDiag] File.Replace failed, fallback to copy+delete. path={path}, error={ex.Message}");
                    File.Copy(tempPath, path, overwrite: true);
                    File.Delete(tempPath);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }

        private static void SaveStoreToPath(string path, PromptPresetStoreConfig store)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = ReflectionJsonFieldSerializer.Serialize(store, prettyPrint: true);
            string tempPath = path + ".tmp";
            try
            {
                AtomicWriteText(path, tempPath, json);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static void MirrorStoreToLegacyPath(string primaryPath)
        {
            string legacyPath = GetLegacyStorePath();
            if (string.IsNullOrWhiteSpace(legacyPath) ||
                string.Equals(primaryPath, legacyPath, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(primaryPath))
            {
                return;
            }

            try
            {
                string legacyDir = Path.GetDirectoryName(legacyPath);
                if (!string.IsNullOrWhiteSpace(legacyDir) && !Directory.Exists(legacyDir))
                {
                    Directory.CreateDirectory(legacyDir);
                }

                File.Copy(primaryPath, legacyPath, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to mirror preset store to legacy path: {ex.Message}");
            }
        }

        private static void TryMigrateLegacyStoreToConfigPath()
        {
            string targetPath = GetStorePath();
            if (File.Exists(targetPath))
            {
                return;
            }

            string legacyPath = GetLegacyStorePath();
            if (string.IsNullOrWhiteSpace(legacyPath) || !File.Exists(legacyPath))
            {
                return;
            }

            try
            {
                EnsureStoreDirectory();
                File.Copy(legacyPath, targetPath, overwrite: false);
                Log.Message($"[RimChat] Migrated preset store to config path: {targetPath}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to migrate legacy preset store: {ex.Message}");
            }
        }

        private static PromptPresetChannelPayloads CaptureCurrentPayload(RimChatSettings settings)
        {
            return new PromptPresetChannelPayloads
            {
                Diplomacy = new PromptChannelPayload
                {
                    SystemPromptCustomJson = ReadOrEmpty(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName)),
                    DialoguePromptCustomJson = ReadOrEmpty(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName)),
                    SocialCirclePromptCustomJson = ReadOrEmpty(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName)),
                    FactionPromptsCustomJson = ReadOrEmpty(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.FactionPromptCustomFileName))
                },
                Rpg = new PromptChannelPayload
                {
                    PawnPromptCustomJson = ReadOrEmpty(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.PawnPromptCustomFileName))
                },
                UnifiedPromptCatalog = settings?.GetPromptUnifiedCatalogClone() ?? PromptUnifiedCatalog.CreateFallback(),
                RimTalkSummaryHistoryLimit = settings?.GetRimTalkSummaryHistoryLimitClamped() ?? 10,
                RimTalkAutoPushSessionSummary = settings?.RimTalkAutoPushSessionSummary ?? false,
                RimTalkAutoInjectCompatPreset = settings?.RimTalkAutoInjectCompatPreset ?? false,
                RimTalkPersonaCopyTemplate = settings?.GetRimTalkPersonaCopyTemplateOrDefault() ?? RimChatSettings.DefaultRimTalkPersonaCopyTemplate
            };
        }

        private static PromptPresetConfig CreateCanonicalDefaultPreset(string name)
        {
            PromptPresetConfig preset = BuildPresetShell(name);
            preset.Id = ImmutableDefaultPresetId;
            preset.Name = ImmutableDefaultPresetName;
            preset.ChannelPayloads = CreateCanonicalDefaultPayload();
            return preset;
        }

        private static bool IsImmutableDefaultId(string presetId)
        {
            return string.Equals(presetId, ImmutableDefaultPresetId, StringComparison.Ordinal);
        }

        private static void EnforceImmutableDefaultPreset(PromptPresetStoreConfig store)
        {
            if (store == null)
            {
                return;
            }

            store.Presets ??= new List<PromptPresetConfig>();
            PromptPresetConfig canonical = CreateCanonicalDefaultPreset(ImmutableDefaultPresetName);
            PromptPresetConfig existing = store.Presets.FirstOrDefault(p => IsImmutableDefaultId(p?.Id));
            if (existing == null)
            {
                store.Presets.Insert(0, canonical);
                existing = canonical;
            }
            else
            {
                existing.Id = ImmutableDefaultPresetId;
                existing.Name = ImmutableDefaultPresetName;
                existing.ChannelPayloads = canonical.ChannelPayloads.Clone();
                existing.CreatedAtUtc = string.IsNullOrWhiteSpace(existing.CreatedAtUtc)
                    ? canonical.CreatedAtUtc
                    : existing.CreatedAtUtc;
                existing.UpdatedAtUtc = existing.CreatedAtUtc;
            }

            for (int i = store.Presets.Count - 1; i >= 0; i--)
            {
                PromptPresetConfig current = store.Presets[i];
                if (current == null)
                {
                    continue;
                }

                if (IsImmutableDefaultId(current.Id) && !ReferenceEquals(current, existing))
                {
                    store.Presets.RemoveAt(i);
                }
            }

            store.DefaultPresetId = ImmutableDefaultPresetId;
        }

        private static PromptPresetChannelPayloads CreateCanonicalDefaultPayload()
        {
            var payload = new PromptPresetChannelPayloads
            {
                Diplomacy = new PromptChannelPayload
                {
                    SystemPromptCustomJson = ReadDefaultOrEmpty(PromptDomainFileCatalog.SystemPromptDefaultFileName),
                    DialoguePromptCustomJson = ReadDefaultOrEmpty(PromptDomainFileCatalog.DiplomacyPromptDefaultFileName),
                    SocialCirclePromptCustomJson = ReadDefaultOrEmpty(PromptDomainFileCatalog.SocialCirclePromptDefaultFileName),
                    FactionPromptsCustomJson = ReadDefaultOrEmpty(PromptDomainFileCatalog.FactionPromptDefaultFileName)
                },
                Rpg = new PromptChannelPayload
                {
                    PawnPromptCustomJson = ReadDefaultOrEmpty(PromptDomainFileCatalog.PawnPromptDefaultFileName)
                },
                UnifiedPromptCatalog = LoadCanonicalDefaultUnifiedCatalog(),
                RimTalkSummaryHistoryLimit = 10,
                RimTalkAutoPushSessionSummary = false,
                RimTalkAutoInjectCompatPreset = false,
                RimTalkPersonaCopyTemplate = RimChatSettings.DefaultRimTalkPersonaCopyTemplate
            };
            NormalizePayload(payload);
            return payload;
        }

        private static void NormalizePayload(PromptPresetChannelPayloads payload)
        {
            if (payload == null)
            {
                return;
            }

            payload.Diplomacy ??= new PromptChannelPayload();
            payload.Rpg ??= new PromptChannelPayload();
            payload.UnifiedPromptCatalog ??= PromptUnifiedCatalog.CreateFallback();
            payload.UnifiedPromptCatalog.NormalizeWith(PromptUnifiedCatalog.CreateFallback());
            payload.RimTalkSummaryHistoryLimit = Mathf.Clamp(
                payload.RimTalkSummaryHistoryLimit,
                RimChatSettings.RimTalkSummaryHistoryMin,
                RimChatSettings.RimTalkSummaryHistoryMax);
            payload.RimTalkPersonaCopyTemplate = string.IsNullOrWhiteSpace(payload.RimTalkPersonaCopyTemplate)
                ? RimChatSettings.DefaultRimTalkPersonaCopyTemplate
                : payload.RimTalkPersonaCopyTemplate.Trim();
            payload.Diplomacy.SystemPromptCustomJson ??= string.Empty;
            payload.Diplomacy.DialoguePromptCustomJson ??= string.Empty;
            payload.Diplomacy.SocialCirclePromptCustomJson ??= string.Empty;
            payload.Diplomacy.FactionPromptsCustomJson ??= string.Empty;
            payload.Rpg.PawnPromptCustomJson ??= string.Empty;
        }

        private static void ApplyLegacyRpgNodeMigrationIfNeeded(PromptPresetConfig preset)
        {
            if (preset?.ChannelPayloads?.UnifiedPromptCatalog == null)
            {
                return;
            }

            PromptUnifiedCatalog catalog = preset.ChannelPayloads.UnifiedPromptCatalog;
            if (catalog.MigrationVersion >= LegacyRpgNodeMigrationVersion)
            {
                return;
            }

            PromptUnifiedCatalog authoritative = PromptUnifiedCatalog.CreateFallback();
            int overriddenCount = 0;
            string[] channels =
            {
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue
            };
            string[] nodeIds =
            {
                "rpg_relationship_profile",
                "rpg_kinship_boundary",
                "rpg_role_setting_fallback"
            };

            foreach (string channel in channels)
            {
                foreach (string nodeId in nodeIds)
                {
                    string authoritativeValue = authoritative.ResolveNode(channel, nodeId);
                    if (!string.IsNullOrWhiteSpace(authoritativeValue))
                    {
                        catalog.SetNode(channel, nodeId, authoritativeValue);
                        overriddenCount++;
                    }
                }
            }

            catalog.MigrationVersion = LegacyRpgNodeMigrationVersion;
            Log.Message($"[RimChat] Legacy RPG node migration applied to preset '{preset.Id}': {overriddenCount} nodes overridden, new migrationVersion={catalog.MigrationVersion}.");
        }

        private static void ApplyLegacySocialNewsNodeMigrationIfNeeded(PromptPresetConfig preset)
        {
            if (preset?.ChannelPayloads?.UnifiedPromptCatalog == null)
            {
                return;
            }

            PromptUnifiedCatalog catalog = preset.ChannelPayloads.UnifiedPromptCatalog;
            if (catalog.MigrationVersion >= LegacySocialNewsNodeMigrationVersion)
            {
                return;
            }

            PromptUnifiedCatalog authoritative = PromptUnifiedCatalog.CreateFallback();
            int overriddenCount = 0;
            overriddenCount += TryOverrideLegacySocialNewsNode(catalog, authoritative, "social_news_style", "文风：中性新闻播报");
            overriddenCount += TryOverrideLegacySocialNewsNode(catalog, authoritative, "social_news_json_contract", "如果 quote 为空，quote_attribution 也必须为空。", "narrative_mode");
            overriddenCount += TryOverrideLegacySocialNewsNode(catalog, authoritative, "social_news_fact", "基于以下事实种子生成一条社交圈世界新闻卡片。", "narrative_mode={{ world.social.narrative_mode }}");
            catalog.MigrationVersion = LegacySocialNewsNodeMigrationVersion;
            if (overriddenCount > 0)
            {
                Log.Message($"[RimChat] Legacy social news node migration applied to preset '{preset.Id}': {overriddenCount} nodes overridden, new migrationVersion={catalog.MigrationVersion}.");
            }
        }

        private static int TryOverrideLegacySocialNewsNode(
            PromptUnifiedCatalog catalog,
            PromptUnifiedCatalog authoritative,
            string nodeId,
            string legacyMarker,
            string missingMarker = null)
        {
            string current = catalog.ResolveNode(RimTalkPromptEntryChannelCatalog.Any, nodeId)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(current) || !current.Contains(legacyMarker))
            {
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(missingMarker) && current.Contains(missingMarker))
            {
                return 0;
            }

            string authoritativeValue = authoritative.ResolveNode(RimTalkPromptEntryChannelCatalog.Any, nodeId);
            if (string.IsNullOrWhiteSpace(authoritativeValue))
            {
                return 0;
            }

            catalog.SetNode(RimTalkPromptEntryChannelCatalog.Any, nodeId, authoritativeValue);
            return 1;
        }

        private static bool ShouldCreateMigratedPreset(
            PromptPresetChannelPayloads legacyPayload,
            PromptPresetChannelPayloads canonicalPayload)
        {
            NormalizePayload(legacyPayload);
            NormalizePayload(canonicalPayload);
            if (!HasMeaningfulLegacyPayload(legacyPayload))
            {
                return false;
            }

            return !ArePayloadsEquivalent(legacyPayload, canonicalPayload);
        }

        private static bool HasMeaningfulLegacyPayload(PromptPresetChannelPayloads payload)
        {
            if (payload == null)
            {
                return false;
            }

            bool hasCustomPromptFiles = !string.IsNullOrWhiteSpace(payload.Diplomacy?.SystemPromptCustomJson) ||
                                        !string.IsNullOrWhiteSpace(payload.Diplomacy?.DialoguePromptCustomJson) ||
                                        !string.IsNullOrWhiteSpace(payload.Diplomacy?.SocialCirclePromptCustomJson) ||
                                        !string.IsNullOrWhiteSpace(payload.Diplomacy?.FactionPromptsCustomJson) ||
                                        !string.IsNullOrWhiteSpace(payload.Rpg?.PawnPromptCustomJson);
            if (hasCustomPromptFiles)
            {
                return true;
            }

            if (payload.RimTalkSummaryHistoryLimit != 10 ||
                payload.RimTalkAutoPushSessionSummary ||
                payload.RimTalkAutoInjectCompatPreset ||
                !AreUnifiedCatalogsEquivalent(payload.UnifiedPromptCatalog, LoadCanonicalDefaultUnifiedCatalog()) ||
                !string.Equals(
                    NormalizeText(payload.RimTalkPersonaCopyTemplate),
                    NormalizeText(RimChatSettings.DefaultRimTalkPersonaCopyTemplate),
                    StringComparison.Ordinal))
            {
                return true;
            }
            return false;
        }

        private static PromptUnifiedCatalog LoadCanonicalDefaultUnifiedCatalog()
        {
            PromptUnifiedCatalog loaded = null;
            string defaultPath = PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.PromptUnifiedDefaultFileName);
            string thoughtChainText = string.Empty;
            if (!string.IsNullOrWhiteSpace(defaultPath) && File.Exists(defaultPath))
            {
                try
                {
                    string rawJson = File.ReadAllText(defaultPath);
                    loaded = JsonUtility.FromJson<PromptUnifiedCatalog>(rawJson);
                    thoughtChainText = TryExtractNodeContentFromRawJson(rawJson, "thought_chain_node_template");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat] Failed to parse default unified prompt catalog: {ex.Message}");
                }
            }

            loaded ??= PromptUnifiedCatalog.CreateFallback();
            loaded.NormalizeWith(PromptUnifiedCatalog.CreateFallback());
            loaded.LegacyMigrated = true;
            if (!string.IsNullOrWhiteSpace(thoughtChainText) &&
                string.IsNullOrWhiteSpace(loaded.ResolveNode(RimTalkPromptEntryChannelCatalog.Any, "thought_chain_node_template")))
            {
                loaded.SetNode(RimTalkPromptEntryChannelCatalog.Any, "thought_chain_node_template", thoughtChainText);
            }

            return loaded;
        }

        private static string TryExtractNodeContentFromRawJson(string rawJson, string nodeId)
        {
            if (string.IsNullOrWhiteSpace(rawJson) || string.IsNullOrWhiteSpace(nodeId))
            {
                return string.Empty;
            }

            string escapedNodeId = Regex.Escape(nodeId.Trim());
            string pattern = $"\"NodeId\"\\s*:\\s*\"{escapedNodeId}\"\\s*,\\s*\"Content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"";
            Match match = Regex.Match(rawJson, pattern, RegexOptions.Singleline);
            if (!match.Success || match.Groups.Count < 2)
            {
                return string.Empty;
            }

            string escapedContent = match.Groups[1].Value ?? string.Empty;
            string wrapperJson = "{\"value\":\"" + escapedContent + "\"}";
            JsonStringWrapper wrapper = JsonUtility.FromJson<JsonStringWrapper>(wrapperJson);
            return wrapper?.value ?? string.Empty;
        }

        private static bool ArePayloadsEquivalent(PromptPresetChannelPayloads left, PromptPresetChannelPayloads right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            return string.Equals(NormalizeText(left.Diplomacy?.SystemPromptCustomJson), NormalizeText(right.Diplomacy?.SystemPromptCustomJson), StringComparison.Ordinal) &&
                   string.Equals(NormalizeText(left.Diplomacy?.DialoguePromptCustomJson), NormalizeText(right.Diplomacy?.DialoguePromptCustomJson), StringComparison.Ordinal) &&
                   string.Equals(NormalizeText(left.Diplomacy?.SocialCirclePromptCustomJson), NormalizeText(right.Diplomacy?.SocialCirclePromptCustomJson), StringComparison.Ordinal) &&
                   string.Equals(NormalizeText(left.Diplomacy?.FactionPromptsCustomJson), NormalizeText(right.Diplomacy?.FactionPromptsCustomJson), StringComparison.Ordinal) &&
                   string.Equals(NormalizeText(left.Rpg?.PawnPromptCustomJson), NormalizeText(right.Rpg?.PawnPromptCustomJson), StringComparison.Ordinal) &&
                   left.RimTalkSummaryHistoryLimit == right.RimTalkSummaryHistoryLimit &&
                   left.RimTalkAutoPushSessionSummary == right.RimTalkAutoPushSessionSummary &&
                   left.RimTalkAutoInjectCompatPreset == right.RimTalkAutoInjectCompatPreset &&
                   AreUnifiedCatalogsEquivalent(left.UnifiedPromptCatalog, right.UnifiedPromptCatalog) &&
                   string.Equals(NormalizeText(left.RimTalkPersonaCopyTemplate), NormalizeText(right.RimTalkPersonaCopyTemplate), StringComparison.Ordinal);
        }

        private static bool AreUnifiedCatalogsEquivalent(
            PromptUnifiedCatalog left,
            PromptUnifiedCatalog right)
        {
            PromptUnifiedCatalog normalizedLeft = left?.Clone() ?? PromptUnifiedCatalog.CreateFallback();
            PromptUnifiedCatalog normalizedRight = right?.Clone() ?? PromptUnifiedCatalog.CreateFallback();
            normalizedLeft.NormalizeWith(PromptUnifiedCatalog.CreateFallback());
            normalizedRight.NormalizeWith(PromptUnifiedCatalog.CreateFallback());
            return string.Equals(
                PromptDomainJsonUtility.Serialize(normalizedLeft, prettyPrint: false) ?? string.Empty,
                PromptDomainJsonUtility.Serialize(normalizedRight, prettyPrint: false) ?? string.Empty,
                StringComparison.Ordinal);
        }

        private static bool AreChannelConfigsEquivalent(RimTalkChannelCompatConfig left, RimTalkChannelCompatConfig right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            List<RimTalkPromptEntryConfig> leftEntries = left.PromptEntries ?? new List<RimTalkPromptEntryConfig>();
            List<RimTalkPromptEntryConfig> rightEntries = right.PromptEntries ?? new List<RimTalkPromptEntryConfig>();
            if (leftEntries.Count != rightEntries.Count)
            {
                return false;
            }

            for (int i = 0; i < leftEntries.Count; i++)
            {
                if (!ArePromptEntriesEquivalent(leftEntries[i], rightEntries[i]))
                {
                    return false;
                }
            }

            return left.EnablePromptCompat == right.EnablePromptCompat &&
                   left.PresetInjectionMaxEntries == right.PresetInjectionMaxEntries &&
                   left.PresetInjectionMaxChars == right.PresetInjectionMaxChars &&
                   string.Equals(NormalizeText(left.CompatTemplate), NormalizeText(right.CompatTemplate), StringComparison.Ordinal);
        }

        private static bool ArePromptEntriesEquivalent(RimTalkPromptEntryConfig left, RimTalkPromptEntryConfig right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            return string.Equals(NormalizeText(left.SectionId), NormalizeText(right.SectionId), StringComparison.Ordinal) &&
                   string.Equals(NormalizeText(left.Name), NormalizeText(right.Name), StringComparison.Ordinal) &&
                   string.Equals(NormalizeText(left.Role), NormalizeText(right.Role), StringComparison.Ordinal) &&
                   string.Equals(NormalizeText(left.CustomRole), NormalizeText(right.CustomRole), StringComparison.Ordinal) &&
                   string.Equals(NormalizeText(left.Position), NormalizeText(right.Position), StringComparison.Ordinal) &&
                   left.InChatDepth == right.InChatDepth &&
                   left.Enabled == right.Enabled &&
                   string.Equals(NormalizeText(left.PromptChannel), NormalizeText(right.PromptChannel), StringComparison.Ordinal) &&
                   string.Equals(NormalizeText(left.Content), NormalizeText(right.Content), StringComparison.Ordinal);
        }

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static void ApplyPayloadToCustomFiles(PromptPresetChannelPayloads payload)
        {
            PromptPresetChannelPayloads data = payload ?? new PromptPresetChannelPayloads();
            WriteIfNotNull(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName), data.Diplomacy?.SystemPromptCustomJson);
            WriteIfNotNull(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName), data.Diplomacy?.DialoguePromptCustomJson);
            WriteIfNotNull(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName), data.Diplomacy?.SocialCirclePromptCustomJson);
            WriteIfNotNull(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.FactionPromptCustomFileName), data.Diplomacy?.FactionPromptsCustomJson);
        }

        private static void PersistRpgPromptCustomStore(PromptPresetChannelPayloads payload)
        {
            PromptPresetChannelPayloads data = payload ?? new PromptPresetChannelPayloads();
            RpgPromptCustomConfig config = ParseRpgPromptCustomConfig(data.Rpg?.PawnPromptCustomJson)
                                           ?? new RpgPromptCustomConfig();
            config.RimTalkSummaryHistoryLimit = Mathf.Clamp(
                data.RimTalkSummaryHistoryLimit,
                RimChatSettings.RimTalkSummaryHistoryMin,
                RimChatSettings.RimTalkSummaryHistoryMax);
            config.RimTalkAutoPushSessionSummary = data.RimTalkAutoPushSessionSummary;
            config.RimTalkAutoInjectCompatPreset = data.RimTalkAutoInjectCompatPreset;
            config.RimTalkPersonaCopyTemplate = string.IsNullOrWhiteSpace(data.RimTalkPersonaCopyTemplate)
                ? RimChatSettings.DefaultRimTalkPersonaCopyTemplate
                : data.RimTalkPersonaCopyTemplate;
            RpgPromptCustomStore.Save(config);
        }

        private static void ApplyRimTalkCompatSettings(RimChatSettings settings, PromptPresetChannelPayloads payload, bool persistToFiles)
        {
            PromptPresetChannelPayloads data = payload ?? new PromptPresetChannelPayloads();
            settings.RimTalkSummaryHistoryLimit = Mathf.Clamp(data.RimTalkSummaryHistoryLimit, RimChatSettings.RimTalkSummaryHistoryMin, RimChatSettings.RimTalkSummaryHistoryMax);
            settings.RimTalkAutoPushSessionSummary = data.RimTalkAutoPushSessionSummary;
            settings.RimTalkAutoInjectCompatPreset = data.RimTalkAutoInjectCompatPreset;
            settings.RimTalkPersonaCopyTemplate = string.IsNullOrWhiteSpace(data.RimTalkPersonaCopyTemplate)
                ? RimChatSettings.DefaultRimTalkPersonaCopyTemplate
                : data.RimTalkPersonaCopyTemplate;
            settings.SetPromptUnifiedCatalog(data.UnifiedPromptCatalog, persistToFiles: persistToFiles);
        }

        private static string ReadOrEmpty(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            return File.ReadAllText(path);
        }

        private static string ReadDefaultOrEmpty(string fileName)
        {
            return ReadOrEmpty(PromptDomainFileCatalog.GetDefaultPath(fileName));
        }

        private static void WriteIfNotNull(string path, string payload)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            string trimmed = payload.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) &&
                !trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                Log.Warning($"[RimChat] Skip writing preset payload because content is not JSON. Path: {path}");
                return;
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, payload);
        }

        private static RpgPromptCustomConfig ParseRpgPromptCustomConfig(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            string trimmed = json.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<RpgPromptCustomConfig>(trimmed);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to parse RPG prompt custom payload for preset activation: {ex.Message}");
                return null;
            }
        }

        private static string EnsureUniqueName(List<PromptPresetConfig> presets, string name)
        {
            string baseName = string.IsNullOrWhiteSpace(name) ? "Preset" : name.Trim();
            List<PromptPresetConfig> all = presets ?? new List<PromptPresetConfig>();
            string candidate = baseName;
            int n = 2;
            while (all.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{baseName} {n}";
                n++;
            }

            return candidate;
        }

        private static void ApplyLegacyPayloadsFromStoreJson(PromptPresetStoreConfig store, string rawJson)
        {
            if (store?.Presets == null ||
                string.IsNullOrWhiteSpace(rawJson) ||
                !ShouldApplyLegacyPayloadOverlay(rawJson))
            {
                return;
            }

            LegacyPromptPresetStoreConfig legacy = JsonUtility.FromJson<LegacyPromptPresetStoreConfig>(rawJson);
            if (legacy?.Presets == null)
            {
                return;
            }

            int count = Math.Min(store.Presets.Count, legacy.Presets.Count);
            for (int i = 0; i < count; i++)
            {
                ApplyLegacyPayload(store.Presets[i], legacy.Presets[i]?.ChannelPayloads, $"preset.store.{i}");
            }
        }

        private static void ApplyLegacyPayloadFromJson(PromptPresetConfig preset, string rawJson, string sourceId)
        {
            if (preset == null ||
                string.IsNullOrWhiteSpace(rawJson) ||
                !ShouldApplyLegacyPayloadOverlay(rawJson))
            {
                return;
            }

            LegacyPromptPresetConfig legacy = JsonUtility.FromJson<LegacyPromptPresetConfig>(rawJson);
            ApplyLegacyPayload(preset, legacy?.ChannelPayloads, sourceId);
        }

        private static bool ShouldApplyLegacyPayloadOverlay(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return false;
            }

            return rawJson.IndexOf("\"PromptSectionCatalog\"", StringComparison.Ordinal) >= 0 ||
                   rawJson.IndexOf("\"EnableRimTalkPromptCompat\"", StringComparison.Ordinal) >= 0 ||
                   rawJson.IndexOf("\"RimTalkPresetInjectionMaxEntries\"", StringComparison.Ordinal) >= 0 ||
                   rawJson.IndexOf("\"RimTalkPresetInjectionMaxChars\"", StringComparison.Ordinal) >= 0 ||
                   rawJson.IndexOf("\"RimTalkCompatTemplate\"", StringComparison.Ordinal) >= 0;
        }

        private static void ApplyLegacyPayload(
            PromptPresetConfig preset,
            LegacyPromptPresetChannelPayloads legacyPayload,
            string sourceId)
        {
            if (preset?.ChannelPayloads == null || legacyPayload == null)
            {
                return;
            }

            RimTalkPromptEntryDefaultsConfig sections = PromptLegacyCompatMigration.ApplyLegacyPayloadToPromptSections(
                legacyPayload.PromptSectionCatalog,
                legacyPayload.EnableRimTalkPromptCompat,
                legacyPayload.RimTalkPresetInjectionMaxEntries,
                legacyPayload.RimTalkPresetInjectionMaxChars,
                legacyPayload.RimTalkCompatTemplate,
                legacyPayload.RimTalkDiplomacy,
                legacyPayload.RimTalkRpg,
                sourceId);
            PromptUnifiedCatalog unified = preset.ChannelPayloads.UnifiedPromptCatalog?.Clone() ?? PromptUnifiedCatalog.CreateFallback();
            ApplyLegacySectionsToUnifiedCatalog(unified, sections);
            preset.ChannelPayloads.UnifiedPromptCatalog = unified;
        }

        private static void ApplyLegacySectionsToUnifiedCatalog(
            PromptUnifiedCatalog unified,
            RimTalkPromptEntryDefaultsConfig sections)
        {
            if (unified == null)
            {
                return;
            }

            RimTalkPromptEntryDefaultsConfig normalized = PromptLegacyCompatMigration.NormalizePromptSections(sections);
            foreach (RimTalkPromptChannelDefaultsConfig channel in normalized.Channels ?? new List<RimTalkPromptChannelDefaultsConfig>())
            {
                if (channel == null || string.IsNullOrWhiteSpace(channel.PromptChannel))
                {
                    continue;
                }

                foreach (RimTalkPromptSectionDefaultConfig section in channel.Sections ?? new List<RimTalkPromptSectionDefaultConfig>())
                {
                    if (section == null || string.IsNullOrWhiteSpace(section.SectionId))
                    {
                        continue;
                    }

                    unified.SetSection(channel.PromptChannel, section.SectionId, section.Content ?? string.Empty);
                }
            }
        }

        private static bool HasMeaningfulPayload(PromptPresetChannelPayloads payload)
        {
            if (payload == null)
            {
                return false;
            }

            bool diplomacy = !string.IsNullOrWhiteSpace(payload.Diplomacy?.SystemPromptCustomJson) ||
                             !string.IsNullOrWhiteSpace(payload.Diplomacy?.DialoguePromptCustomJson) ||
                             !string.IsNullOrWhiteSpace(payload.Diplomacy?.SocialCirclePromptCustomJson) ||
                             !string.IsNullOrWhiteSpace(payload.Diplomacy?.FactionPromptsCustomJson);
            bool rpg = !string.IsNullOrWhiteSpace(payload.Rpg?.PawnPromptCustomJson);
            bool unified = !AreUnifiedCatalogsEquivalent(payload.UnifiedPromptCatalog, PromptUnifiedCatalog.CreateFallback());
            return diplomacy || rpg || unified;
        }
    }
}

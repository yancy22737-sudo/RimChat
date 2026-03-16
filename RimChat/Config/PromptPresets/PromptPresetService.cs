using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimChat.Persistence;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    internal sealed class PromptPresetService : IPromptPresetService
    {
        private const int CurrentSchemaVersion = 1;
        private const string PresetStoreFileName = "PromptPresets_Custom.json";

        public PromptPresetStoreConfig LoadAll(RimChatSettings settings)
        {
            PromptPresetStoreConfig store = ReadStoreFile();
            if (store == null)
            {
                store = new PromptPresetStoreConfig();
            }

            NormalizeStore(settings, store);
            return store;
        }

        public void SaveAll(PromptPresetStoreConfig store)
        {
            PromptPresetStoreConfig normalized = store ?? new PromptPresetStoreConfig();
            normalized.SchemaVersion = CurrentSchemaVersion;
            EnsureStoreDirectory();
            string path = GetStorePath();
            File.WriteAllText(path, JsonUtility.ToJson(normalized, true));
        }

        public PromptPresetConfig CreateFromLegacy(RimChatSettings settings, string name)
        {
            settings?.FlushPromptEditorsToStorageForPreset();
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
                target.UpdatedAtUtc = DateTime.UtcNow.ToString("o");

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

            ApplyRimTalkCompatSettings(settings, data);
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
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(filePath, JsonUtility.ToJson(preset, true));
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
            if (!File.Exists(filePath))
            {
                error = "File not found.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                PromptPresetConfig parsed = JsonUtility.FromJson<PromptPresetConfig>(json);
                if (parsed == null)
                {
                    error = "Invalid preset file.";
                    return false;
                }

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
                    DiplomacyChars = (payload.Diplomacy?.SystemPromptCustomJson?.Length ?? 0) +
                                     (payload.Diplomacy?.DialoguePromptCustomJson?.Length ?? 0),
                    RpgChars = payload.Rpg?.PawnPromptCustomJson?.Length ?? 0,
                    RimTalkDiplomacyChars = payload.RimTalkDiplomacy?.CompatTemplate?.Length ?? 0,
                    RimTalkRpgChars = payload.RimTalkRpg?.CompatTemplate?.Length ?? 0
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

        private static void NormalizeStore(RimChatSettings settings, PromptPresetStoreConfig store)
        {
            store.SchemaVersion = CurrentSchemaVersion;
            store.Presets ??= new List<PromptPresetConfig>();
            for (int i = 0; i < store.Presets.Count; i++)
            {
                NormalizePreset(store.Presets[i]);
                if (!HasMeaningfulPayload(store.Presets[i].ChannelPayloads))
                {
                    store.Presets[i].ChannelPayloads = CaptureCurrentPayload(settings);
                    store.Presets[i].UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                }
            }

            if (store.Presets.Count == 0)
            {
                PromptPresetService factory = new PromptPresetService();
                PromptPresetConfig canonical = CreateCanonicalDefaultPreset("Default");
                canonical.IsActive = true;
                store.Presets.Add(canonical);
                PromptPresetChannelPayloads legacyPayload = CaptureCurrentPayload(settings);
                if (ShouldCreateMigratedPreset(legacyPayload, canonical.ChannelPayloads))
                {
                    PromptPresetConfig migrated = BuildPresetShell("Migrated");
                    migrated.ChannelPayloads = legacyPayload;
                    store.Presets.Add(migrated);
                }

                store.ActivePresetId = canonical.Id;
                factory.SaveAll(store);
                return;
            }

            PromptPresetConfig active = store.Presets.FirstOrDefault(p => string.Equals(p.Id, store.ActivePresetId, StringComparison.Ordinal))
                                      ?? store.Presets.FirstOrDefault(p => p.IsActive)
                                      ?? store.Presets[0];
            store.ActivePresetId = active.Id;

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

        private static PromptPresetStoreConfig ReadStoreFile()
        {
            string path = GetStorePath();
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<PromptPresetStoreConfig>(json);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to read prompt preset store: {ex.Message}");
                return null;
            }
        }

        private static string GetStorePath()
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
                RimTalkDiplomacy = settings?.GetRimTalkChannelConfigClone(RimTalkPromptChannel.Diplomacy) ?? RimTalkChannelCompatConfig.CreateDefault(),
                RimTalkRpg = settings?.GetRimTalkChannelConfigClone(RimTalkPromptChannel.Rpg) ?? RimTalkChannelCompatConfig.CreateDefault(),
                RimTalkSummaryHistoryLimit = settings?.GetRimTalkSummaryHistoryLimitClamped() ?? 10,
                RimTalkAutoPushSessionSummary = settings?.RimTalkAutoPushSessionSummary ?? false,
                RimTalkAutoInjectCompatPreset = settings?.RimTalkAutoInjectCompatPreset ?? false,
                RimTalkPersonaCopyTemplate = settings?.GetRimTalkPersonaCopyTemplateOrDefault() ?? RimChatSettings.DefaultRimTalkPersonaCopyTemplate
            };
        }

        private static PromptPresetConfig CreateCanonicalDefaultPreset(string name)
        {
            PromptPresetConfig preset = BuildPresetShell(name);
            preset.ChannelPayloads = CreateCanonicalDefaultPayload();
            return preset;
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
                RimTalkDiplomacy = RimChatSettings.CreateCanonicalDefaultRimTalkChannelConfig(RimTalkPromptChannel.Diplomacy),
                RimTalkRpg = RimChatSettings.CreateCanonicalDefaultRimTalkChannelConfig(RimTalkPromptChannel.Rpg),
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
            payload.RimTalkDiplomacy = PromptLegacyCompatMigration.NormalizeChannelConfig(
                payload.RimTalkDiplomacy,
                "diplomacy",
                "preset.diplomacy");
            payload.RimTalkRpg = PromptLegacyCompatMigration.NormalizeChannelConfig(
                payload.RimTalkRpg,
                "rpg",
                "preset.rpg");
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
                !string.Equals(
                    NormalizeText(payload.RimTalkPersonaCopyTemplate),
                    NormalizeText(RimChatSettings.DefaultRimTalkPersonaCopyTemplate),
                    StringComparison.Ordinal))
            {
                return true;
            }

            return HasMeaningfulLegacyChannelConfig(payload.RimTalkDiplomacy) ||
                   HasMeaningfulLegacyChannelConfig(payload.RimTalkRpg);
        }

        private static bool HasMeaningfulLegacyChannelConfig(RimTalkChannelCompatConfig config)
        {
            if (config == null)
            {
                return false;
            }

            if (!config.EnablePromptCompat ||
                config.PresetInjectionMaxEntries != RimChatSettings.RimTalkPresetInjectionLimitUnlimited ||
                config.PresetInjectionMaxChars != RimChatSettings.RimTalkPresetInjectionLimitUnlimited ||
                !string.Equals(
                    NormalizeText(config.CompatTemplate),
                    NormalizeText(RimChatSettings.DefaultRimTalkCompatTemplate),
                    StringComparison.Ordinal))
            {
                return true;
            }

            List<RimTalkPromptEntryConfig> entries = config.PromptEntries ?? new List<RimTalkPromptEntryConfig>();
            if (entries.Count > 1)
            {
                return true;
            }

            RimTalkPromptEntryConfig entry = entries.FirstOrDefault(item => item != null);
            if (entry == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(entry.SectionId) ||
                   (!string.IsNullOrWhiteSpace(entry.PromptChannel) &&
                    !string.Equals(entry.PromptChannel, RimTalkPromptEntryChannelCatalog.Any, StringComparison.OrdinalIgnoreCase)) ||
                   !string.Equals(NormalizeText(entry.Name), "Compat Template", StringComparison.Ordinal) ||
                   !string.Equals(
                       NormalizeText(entry.Content),
                       NormalizeText(RimChatSettings.DefaultRimTalkCompatTemplate),
                       StringComparison.Ordinal);
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
                   string.Equals(NormalizeText(left.RimTalkPersonaCopyTemplate), NormalizeText(right.RimTalkPersonaCopyTemplate), StringComparison.Ordinal) &&
                   AreChannelConfigsEquivalent(left.RimTalkDiplomacy, right.RimTalkDiplomacy) &&
                   AreChannelConfigsEquivalent(left.RimTalkRpg, right.RimTalkRpg);
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
            config.RimTalkDiplomacy = PromptLegacyCompatMigration.NormalizeChannelConfig(
                data.RimTalkDiplomacy,
                "diplomacy",
                "preset.diplomacy");
            config.RimTalkRpg = PromptLegacyCompatMigration.NormalizeChannelConfig(
                data.RimTalkRpg,
                "rpg",
                "preset.rpg");
            config.EnableRimTalkPromptCompat = false;
            config.RimTalkPresetInjectionMaxEntries = RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
            config.RimTalkPresetInjectionMaxChars = RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
            config.RimTalkCompatTemplate = string.Empty;
            config.RimTalkChannelSplitMigrated = true;
            PromptLegacyCompatMigration.ResetLegacyFields(config);
            RpgPromptCustomStore.Save(config);
        }

        private static void ApplyRimTalkCompatSettings(RimChatSettings settings, PromptPresetChannelPayloads payload)
        {
            PromptPresetChannelPayloads data = payload ?? new PromptPresetChannelPayloads();
            settings.RimTalkSummaryHistoryLimit = Mathf.Clamp(data.RimTalkSummaryHistoryLimit, RimChatSettings.RimTalkSummaryHistoryMin, RimChatSettings.RimTalkSummaryHistoryMax);
            settings.RimTalkAutoPushSessionSummary = data.RimTalkAutoPushSessionSummary;
            settings.RimTalkAutoInjectCompatPreset = data.RimTalkAutoInjectCompatPreset;
            settings.RimTalkPersonaCopyTemplate = string.IsNullOrWhiteSpace(data.RimTalkPersonaCopyTemplate)
                ? RimChatSettings.DefaultRimTalkPersonaCopyTemplate
                : data.RimTalkPersonaCopyTemplate;
            settings.SetRimTalkChannelConfig(RimTalkPromptChannel.Diplomacy, data.RimTalkDiplomacy?.Clone() ?? RimTalkChannelCompatConfig.CreateDefault());
            settings.SetRimTalkChannelConfig(RimTalkPromptChannel.Rpg, data.RimTalkRpg?.Clone() ?? RimTalkChannelCompatConfig.CreateDefault());
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
            bool rimTalk = !string.IsNullOrWhiteSpace(payload.RimTalkDiplomacy?.CompatTemplate) ||
                           !string.IsNullOrWhiteSpace(payload.RimTalkRpg?.CompatTemplate);
            return diplomacy || rpg || rimTalk;
        }
    }
}

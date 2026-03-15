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
                ApplyPayloadToCustomFiles(target.ChannelPayloads);
                ApplyRimTalkCompatSettings(settings, target.ChannelPayloads);
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
                settings.FlushPromptEditorsToStorageForPreset();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
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
                PromptPresetConfig migrated = factory.CreateFromLegacy(settings, "Default");
                migrated.IsActive = true;
                store.Presets.Add(migrated);
                store.ActivePresetId = migrated.Id;
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
            preset.ChannelPayloads.Diplomacy ??= new PromptChannelPayload();
            preset.ChannelPayloads.Rpg ??= new PromptChannelPayload();
            preset.ChannelPayloads.RimTalkDiplomacy ??= RimTalkChannelCompatConfig.CreateDefault();
            preset.ChannelPayloads.RimTalkRpg ??= RimTalkChannelCompatConfig.CreateDefault();
            preset.ChannelPayloads.RimTalkDiplomacy.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            preset.ChannelPayloads.RimTalkRpg.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            if (string.IsNullOrWhiteSpace(preset.ChannelPayloads.RimTalkPersonaCopyTemplate))
            {
                preset.ChannelPayloads.RimTalkPersonaCopyTemplate = RimChatSettings.DefaultRimTalkPersonaCopyTemplate;
            }

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

        private static void ApplyPayloadToCustomFiles(PromptPresetChannelPayloads payload)
        {
            PromptPresetChannelPayloads data = payload ?? new PromptPresetChannelPayloads();
            WriteIfNotNull(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName), data.Diplomacy?.SystemPromptCustomJson);
            WriteIfNotNull(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName), data.Diplomacy?.DialoguePromptCustomJson);
            WriteIfNotNull(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName), data.Diplomacy?.SocialCirclePromptCustomJson);
            WriteIfNotNull(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.FactionPromptCustomFileName), data.Diplomacy?.FactionPromptsCustomJson);
            WriteIfNotNull(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.PawnPromptCustomFileName), data.Rpg?.PawnPromptCustomJson);
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

        private static void WriteIfNotNull(string path, string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
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

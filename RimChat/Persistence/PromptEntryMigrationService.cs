using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimChat.Prompting;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: PromptTemplatePreset, FactionPromptConfig.
    /// Responsibility: one-time migration from old faction configs to flat entry presets.
    /// </summary>
    internal static class PromptEntryMigrationService
    {
        private static bool migrationApplied;

        public static bool HasMigrated()
        {
            return migrationApplied;
        }

        public static void SetMigrated()
        {
            migrationApplied = true;
        }

        /// <summary>
        /// Convert a legacy FactionPromptConfig into a flat PromptTemplatePreset.
        /// Each template field becomes one entry in the preset.
        /// </summary>
        public static PromptTemplatePreset MigrateFromFactionPromptConfig(
            FactionPromptConfig config)
        {
            if (config == null)
            {
                return new PromptTemplatePreset();
            }

            var preset = new PromptTemplatePreset
            {
                Id = "faction_" + (config.FactionDefName ?? "unknown"),
                Name = "Faction Prompts — " + (config.DisplayName ?? config.FactionDefName ?? string.Empty),
                Description = "Auto-generated from legacy FactionPromptConfig fields.",
                IsActive = false
            };

            int order = 0;
            foreach (PromptTemplateField field in config.TemplateFields ?? Enumerable.Empty<PromptTemplateField>())
            {
                if (field == null || !field.IsEnabled)
                {
                    continue;
                }

                preset.Entries.Add(new PromptTemplateEntry
                {
                    Id = "faction_field." + (field.FieldName ?? string.Empty),
                    Name = field.FieldName ?? string.Empty,
                    Content = field.FieldValue ?? string.Empty,
                    Role = PromptEntryRole.System,
                    Position = PromptEntryPosition.Relative,
                    SourceModId = "rimchat.faction_config",
                    Channel = "faction_prompt",
                    Order = order++
                });
            }

            return preset;
        }

        /// <summary>
        /// Build a flat preset from a list of named template texts.
        /// Used for section catalog and unified node schema migration.
        /// </summary>
        public static PromptTemplatePreset MigrateFromTemplateList(
            string presetId,
            string presetName,
            string channel,
            List<(string id, string name, string template)> items)
        {
            var preset = new PromptTemplatePreset
            {
                Id = presetId ?? Guid.NewGuid().ToString("N"),
                Name = presetName ?? "Migrated Preset",
                Description = "Auto-generated from template list migration.",
                IsActive = false
            };

            int order = 0;
            foreach (var item in items)
            {
                if (item.template == null)
                {
                    continue;
                }

                preset.Entries.Add(new PromptTemplateEntry
                {
                    Id = item.id ?? Guid.NewGuid().ToString("N"),
                    Name = item.name ?? string.Empty,
                    Content = item.template,
                    Role = PromptEntryRole.System,
                    Position = PromptEntryPosition.Relative,
                    Enabled = true,
                    Channel = channel ?? string.Empty,
                    Order = order++
                });
            }

            return preset;
        }
    }
}

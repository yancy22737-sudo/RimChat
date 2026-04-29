using System;
using System.Collections.Generic;
using System.Linq;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: PromptTemplateEntry.
    /// Responsibility: named collection of prompt template entries with ordering and query methods.
    /// </summary>
    public sealed class PromptTemplatePreset
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public bool IsActive;
        public List<PromptTemplateEntry> Entries = new List<PromptTemplateEntry>();
        public HashSet<string> DeletedModEntryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public List<PromptTemplateEntry> GetRelativeEntries()
        {
            return Entries
                .Where(e => e != null && e.Position == PromptEntryPosition.Relative)
                .OrderBy(e => e.Order)
                .ToList();
        }

        public List<PromptTemplateEntry> GetInChatEntries()
        {
            return Entries
                .Where(e => e != null && e.Position == PromptEntryPosition.InChat)
                .OrderByDescending(e => e.InChatDepth)
                .ToList();
        }

        public List<PromptTemplateEntry> GetEntriesByChannel(string channel)
        {
            return Entries
                .Where(e => e != null && string.Equals(e.Channel, channel, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Order)
                .ToList();
        }

        public void InsertEntry(int index, PromptTemplateEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(entry.Id))
            {
                entry.Id = Guid.NewGuid().ToString("N");
            }

            index = Math.Max(0, Math.Min(index, Entries.Count));
            Entries.Insert(index, entry);
            ReindexOrders();
        }

        public void RemoveEntry(string entryId)
        {
            PromptTemplateEntry entry = Entries.FirstOrDefault(e => e != null && e.Id == entryId);
            if (entry != null)
            {
                if (!string.IsNullOrEmpty(entry.SourceModId))
                {
                    DeletedModEntryIds.Add(entry.Id);
                }

                Entries.Remove(entry);
                ReindexOrders();
            }
        }

        public void MoveEntry(string entryId, int newIndex)
        {
            PromptTemplateEntry entry = Entries.FirstOrDefault(e => e != null && e.Id == entryId);
            if (entry == null)
            {
                return;
            }

            Entries.Remove(entry);
            newIndex = Math.Max(0, Math.Min(newIndex, Entries.Count));
            Entries.Insert(newIndex, entry);
            ReindexOrders();
        }

        public PromptTemplatePreset Clone()
        {
            return new PromptTemplatePreset
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = Name + " (Copy)",
                Description = Description,
                IsActive = false,
                Entries = Entries.Where(e => e != null).Select(e => e.Clone()).ToList(),
                DeletedModEntryIds = new HashSet<string>(DeletedModEntryIds, StringComparer.OrdinalIgnoreCase)
            };
        }

        private void ReindexOrders()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i] != null)
                {
                    Entries[i].Order = i;
                }
            }
        }
    }
}

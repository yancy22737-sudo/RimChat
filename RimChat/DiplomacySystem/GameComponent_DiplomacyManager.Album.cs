using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: GameComponent_DiplomacyManager state and album entry model.
    /// Responsibility: hold save-scoped manual album index and expose safe album operations.
    /// </summary>
    public partial class GameComponent_DiplomacyManager
    {
        private List<AlbumImageEntry> albumEntries = new List<AlbumImageEntry>();

        public bool AddAlbumEntry(AlbumImageEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.AlbumPath))
            {
                return false;
            }

            albumEntries ??= new List<AlbumImageEntry>();
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                entry.Id = System.Guid.NewGuid().ToString("N");
            }

            int existingIndex = albumEntries.FindIndex(item =>
                item != null &&
                !string.IsNullOrWhiteSpace(item.AlbumPath) &&
                string.Equals(item.AlbumPath, entry.AlbumPath, System.StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                albumEntries[existingIndex] = entry;
                return true;
            }

            albumEntries.Add(entry);
            return true;
        }

        public List<AlbumImageEntry> GetAlbumEntries()
        {
            albumEntries ??= new List<AlbumImageEntry>();
            return albumEntries
                .Where(item => item != null)
                .OrderByDescending(item => item.SavedTick)
                .ToList();
        }

        public int PruneMissingAlbumFiles()
        {
            albumEntries ??= new List<AlbumImageEntry>();
            int before = albumEntries.Count;
            albumEntries.RemoveAll(item =>
                item == null ||
                string.IsNullOrWhiteSpace(item.AlbumPath) ||
                !File.Exists(item.AlbumPath));
            return before - albumEntries.Count;
        }

        public bool RemoveAlbumEntry(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            albumEntries ??= new List<AlbumImageEntry>();
            int before = albumEntries.Count;
            albumEntries.RemoveAll(item =>
                item != null &&
                string.Equals(item.Id, id, System.StringComparison.OrdinalIgnoreCase));
            return albumEntries.Count != before;
        }
    }
}

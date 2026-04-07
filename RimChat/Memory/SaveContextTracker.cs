using System.IO;
using RimChat.DiplomacySystem;

namespace RimChat.Memory
{
    /// <summary>
    /// Dependencies: filesystem name normalization, RPG persistent slot identity, and StringExtensions.SanitizeFileName.
    /// Responsibility: retain latest observed save filename for diagnostics while exposing the stable RimChat binding id as the primary save context.
    /// </summary>
    public static class SaveContextTracker
    {
        private static string currentSaveName = string.Empty;

        public static void Reset()
        {
            currentSaveName = string.Empty;
        }

        public static void CaptureSaveName(string rawSaveName)
        {
            string normalized = NormalizeSaveName(rawSaveName);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            currentSaveName = normalized;
        }

        public static string GetCurrentSaveName()
        {
            return currentSaveName ?? string.Empty;
        }

        public static string GetStableBindingId()
        {
            string slotId = GameComponent_RPGManager.Instance?.GetPersistentRpgSaveSlotId();
            return string.IsNullOrWhiteSpace(slotId) ? string.Empty : slotId.SanitizeFileName();
        }

        private static string NormalizeSaveName(string rawSaveName)
        {
            if (string.IsNullOrWhiteSpace(rawSaveName))
            {
                return string.Empty;
            }

            string fileName = Path.GetFileName(rawSaveName.Trim());
            string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string effective = string.IsNullOrWhiteSpace(withoutExtension)
                ? fileName
                : withoutExtension;
            return (effective ?? string.Empty).SanitizeFileName();
        }
    }
}

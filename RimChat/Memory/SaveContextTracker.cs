using System.IO;

namespace RimChat.Memory
{
    /// <summary>
    /// Dependencies: filesystem name normalization and StringExtensions.SanitizeFileName.
    /// Responsibility: hold the latest observed active save filename captured from game load/save entry points.
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

using System.IO;
using System.Linq;

namespace RimChat.Memory
{
    /// <summary>/// Dependencies: System.IO.Path invalid filename characters.
 /// Responsibility: sanitize runtime names into safe file/folder fragments for persistent storage.
 ///</summary>
    public static class StringExtensions
    {
        public static string SanitizeFileName(this string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return "Unknown";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            string result = new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            if (result.Length > 100)
            {
                result = result.Substring(0, 100);
            }

            return result;
        }
    }
}


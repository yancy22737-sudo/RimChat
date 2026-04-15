using System;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: prompt workspace runtime scheduling and panel data caching.
    /// Responsibility: centralize performance-related state for editor versioning,
    /// validation debounce, and deferred preview build scheduling.
    /// </summary>
    internal sealed class PromptWorkspacePerformanceState
    {
        public long EditorTextVersion;
        public DateTime LastEditorTextChangedUtc = DateTime.MinValue;

        public bool ValidationPending;
        public DateTime ValidationEarliestRunUtc = DateTime.MinValue;
        public long LastValidatedTextVersion = -1;
        public string LastValidatedContextSignature = string.Empty;
        public long ValidationResultVersion;

        public long LayoutVersion;

        public bool PreviewStartPending = true;
        public int PreviewStartDelayFrames = 1;
    }
}

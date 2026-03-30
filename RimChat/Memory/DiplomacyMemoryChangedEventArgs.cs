using System;

namespace RimChat.Memory
{
    /// <summary>
    /// Dependencies: none.
    /// Responsibility: describe one diplomacy-memory mutation so UI and request owners can refresh from the same runtime source.
    /// </summary>
    public sealed class DiplomacyMemoryChangedEventArgs : EventArgs
    {
        public string FactionId { get; set; } = string.Empty;
        public int Revision { get; set; }
        public bool AffectsCurrentSession { get; set; }
        public bool AffectsPersistentHistory { get; set; }
        public bool AffectsAiPrompt { get; set; }
    }
}

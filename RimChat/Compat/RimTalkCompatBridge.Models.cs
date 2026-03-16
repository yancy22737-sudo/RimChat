namespace RimChat.Compat
{
    /// <summary>/// Dependencies: RimTalk compatibility bridge result mapping.
 /// Responsibility: operation result and variable item models for RimTalk compat UI.
 ///</summary>
    public enum RimTalkPromptEntryWriteResult
    {
        Success = 0,
        InvalidInput = 1,
        BridgeUnavailable = 2,
        ActivePresetUnavailable = 3,
        CreateFailed = 4,
        InsertFailed = 5
    }

    /// <summary>/// Dependencies: RimTalk custom variable enumeration.
 /// Responsibility: represent a single variable row in RimChat settings.
 ///</summary>
    public sealed class RimTalkRegisteredVariable
    {
        public string Name { get; set; }
        public string ModId { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public bool IsAvailable { get; set; }
    }

    /// <summary>/// Dependencies: RimTalk bridge runtime state.
 /// Responsibility: expose concise runtime availability diagnostics for settings UI.
 ///</summary>
    public sealed class RimTalkRuntimeStatus
    {
        public bool PromptCompatEnabled { get; set; }
        public bool RuntimeAvailable { get; set; }
        public string Reason { get; set; }
    }
}

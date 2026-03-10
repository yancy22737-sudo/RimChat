using System.Collections.Generic;

namespace RimChat.Memory
{
    /// <summary>/// Dependencies: none.
 /// Responsibility: represent one NPC-scoped RPG dialogue archive persisted as an external JSON file.
 ///</summary>
    public sealed class RpgNpcDialogueArchive
    {
        public int PawnLoadId = -1;
        public string PawnName = string.Empty;
        public string FactionId = string.Empty;
        public string FactionName = string.Empty;
        public int LastInterlocutorPawnLoadId = -1;
        public string LastInterlocutorName = string.Empty;
        public int LastInteractionTick = 0;
        public int CooldownUntilTick = 0;
        public string PersonaPrompt = string.Empty;
        public long NextTurnSequence = 1L;
        public long CreatedTimestamp = 0L;
        public long LastSavedTimestamp = 0L;
        public List<RpgNpcDialogueSessionArchive> Sessions = new List<RpgNpcDialogueSessionArchive>();
    }

    /// <summary>/// Dependencies: none.
 /// Responsibility: represent one RPG dialogue session with optional compressed summary.
 ///</summary>
    public sealed class RpgNpcDialogueSessionArchive
    {
        public string SessionId = string.Empty;
        public int StartedTick = 0;
        public int EndedTick = 0;
        public int TurnCount = 0;
        public bool IsFinalized = false;
        public int InterlocutorPawnLoadId = -1;
        public string InterlocutorName = string.Empty;
        public string SummaryText = string.Empty;
        public string SummaryState = RpgNpcDialogueSessionSummaryState.Pending;
        public int LastSummaryAttemptTick = 0;
        public List<RpgNpcDialogueTurnArchive> Turns = new List<RpgNpcDialogueTurnArchive>();
    }

    /// <summary>/// Dependencies: none.
 /// Responsibility: centralize summary state constants for session-level RPG archive compression.
 ///</summary>
    public static class RpgNpcDialogueSessionSummaryState
    {
        public const string Pending = "pending";
        public const string Compressed = "compressed";
        public const string SummaryFailed = "summary_failed";
    }

    /// <summary>/// Dependencies: none.
 /// Responsibility: store one RPG turn for NPC-scoped archive.
 ///</summary>
    public sealed class RpgNpcDialogueTurnArchive
    {
        public bool IsPlayer;
        public long TurnSequence = 0L;
        public int SpeakerPawnLoadId = -1;
        public string SpeakerName = string.Empty;
        public int InterlocutorPawnLoadId = -1;
        public string InterlocutorName = string.Empty;
        public string Text = string.Empty;
        public int GameTick = 0;
    }
}

using System.Collections.Generic;
using RimDiplomacy.Relation;

namespace RimDiplomacy.Memory
{
    /// <summary>
    /// Dependencies: RPGRelationValues.
    /// Responsibility: represent one NPC-scoped RPG dialogue archive persisted as an external JSON file.
    /// </summary>
    public sealed class RpgNpcDialogueArchive
    {
        public int PawnLoadId = -1;
        public string PawnName = string.Empty;
        public string FactionId = string.Empty;
        public string FactionName = string.Empty;
        public int LastInteractionTick = 0;
        public int CooldownUntilTick = 0;
        public string PersonaPrompt = string.Empty;
        public RPGRelationValues RelationValues = new RPGRelationValues();
        public long CreatedTimestamp = 0L;
        public long LastSavedTimestamp = 0L;
        public List<RpgNpcDialogueTurnArchive> Turns = new List<RpgNpcDialogueTurnArchive>();
    }

    /// <summary>
    /// Dependencies: none.
    /// Responsibility: store one RPG turn for NPC-scoped archive.
    /// </summary>
    public sealed class RpgNpcDialogueTurnArchive
    {
        public bool IsPlayer;
        public string Text = string.Empty;
        public int GameTick = 0;
    }
}

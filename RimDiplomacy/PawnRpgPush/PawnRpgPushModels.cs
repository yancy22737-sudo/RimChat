using RimDiplomacy.NpcDialogue;
using RimWorld;
using Verse;

namespace RimDiplomacy.PawnRpgPush
{
    /// <summary>
    /// Dependencies: RimWorld.Faction, RimDiplomacy.NpcDialogue enums.
    /// Responsibility: Carry one PawnRPG proactive trigger before queueing or generation.
    /// </summary>
    public class PawnRpgTriggerContext
    {
        public Faction Faction;
        public NpcDialogueTriggerType TriggerType;
        public NpcDialogueCategory Category;
        public string SourceTag = string.Empty;
        public string Reason = string.Empty;
        public int Severity = 1;
        public int CreatedTick;
        public string Metadata = string.Empty;
    }

    /// <summary>
    /// Dependencies: RimWorld.Faction, Verse.Scribe.
    /// Responsibility: Persist one delayed PawnRPG proactive trigger.
    /// </summary>
    public class QueuedPawnRpgTrigger : IExposable
    {
        public Faction faction;
        public NpcDialogueTriggerType triggerType;
        public NpcDialogueCategory category;
        public string sourceTag = string.Empty;
        public string reason = string.Empty;
        public int severity = 1;
        public int createdTick;
        public int enqueuedTick;
        public int dueTick;
        public int expireTick;
        public string metadata = string.Empty;

        public PawnRpgTriggerContext ToContext()
        {
            return new PawnRpgTriggerContext
            {
                Faction = faction,
                TriggerType = triggerType,
                Category = category,
                SourceTag = sourceTag,
                Reason = reason,
                Severity = severity,
                CreatedTick = createdTick,
                Metadata = metadata
            };
        }

        public static QueuedPawnRpgTrigger FromContext(PawnRpgTriggerContext context, int nowTick, int dueTickValue, int expireTickValue)
        {
            return new QueuedPawnRpgTrigger
            {
                faction = context?.Faction,
                triggerType = context?.TriggerType ?? NpcDialogueTriggerType.Ambient,
                category = context?.Category ?? NpcDialogueCategory.Social,
                sourceTag = context?.SourceTag ?? string.Empty,
                reason = context?.Reason ?? string.Empty,
                severity = context?.Severity ?? 1,
                createdTick = context?.CreatedTick ?? nowTick,
                enqueuedTick = nowTick,
                dueTick = dueTickValue,
                expireTick = expireTickValue,
                metadata = context?.Metadata ?? string.Empty
            };
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref triggerType, "triggerType", NpcDialogueTriggerType.Ambient);
            Scribe_Values.Look(ref category, "category", NpcDialogueCategory.Social);
            Scribe_Values.Look(ref sourceTag, "sourceTag", string.Empty);
            Scribe_Values.Look(ref reason, "reason", string.Empty);
            Scribe_Values.Look(ref severity, "severity", 1);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref enqueuedTick, "enqueuedTick", 0);
            Scribe_Values.Look(ref dueTick, "dueTick", 0);
            Scribe_Values.Look(ref expireTick, "expireTick", 0);
            Scribe_Values.Look(ref metadata, "metadata", string.Empty);
        }
    }

    /// <summary>
    /// Dependencies: Verse.Pawn, Verse.Scribe.
    /// Responsibility: Persist per-NPC successful-delivery cooldown anchor.
    /// </summary>
    public class PawnRpgNpcPushState : IExposable
    {
        public Pawn pawn;
        public int lastNpcEvaluateTick;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref lastNpcEvaluateTick, "lastNpcEvaluateTick", 0);
        }
    }

    /// <summary>
    /// Dependencies: RimWorld.Faction, Verse.Scribe.
    /// Responsibility: Persist per-faction threat-edge state to avoid warning spam.
    /// </summary>
    public class PawnRpgThreatState : IExposable
    {
        public Faction faction;
        public bool hadThreat;

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref hadThreat, "hadThreat", false);
        }
    }
}

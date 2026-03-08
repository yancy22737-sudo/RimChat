using RimWorld;
using Verse;

namespace RimChat.NpcDialogue
{
    /// <summary>/// Dependencies: RimWorld.Faction, Verse.Scribe.
 /// Responsibility: Define trigger/queue/state models for NPC proactive dialogue.
 ///</summary>
    public enum NpcDialogueTriggerType
    {
        Ambient = 0,
        Conditional = 1,
        Causal = 2
    }

    /// <summary>/// Dependencies: RimWorld.Faction, Verse.Scribe.
 /// Responsibility: Classify proactive dialogue into social/task/warning categories.
 ///</summary>
    public enum NpcDialogueCategory
    {
        Social = 0,
        DiplomacyTask = 1,
        WarningThreat = 2
    }

    /// <summary>/// Dependencies: RimWorld.Faction.
 /// Responsibility: Carry one in-memory trigger context before it is queued or generated.
 ///</summary>
    public class NpcDialogueTriggerContext
    {
        public Faction Faction;
        public NpcDialogueTriggerType TriggerType;
        public NpcDialogueCategory Category;
        public string Reason = string.Empty;
        public string SourceTag = string.Empty;
        public int Severity = 1;
        public int CreatedTick;
        public float GoodwillDelta;
    }

    /// <summary>/// Dependencies: RimWorld.Faction, Verse.IExposable.
 /// Responsibility: Persist one delayed proactive trigger item.
 ///</summary>
    public class QueuedNpcDialogueTrigger : IExposable
    {
        public Faction faction;
        public NpcDialogueTriggerType triggerType;
        public NpcDialogueCategory category;
        public string reason = string.Empty;
        public string sourceTag = string.Empty;
        public int severity = 1;
        public int createdTick;
        public int enqueuedTick;
        public int dueTick;
        public int expireTick;
        public float goodwillDelta;

        public NpcDialogueTriggerContext ToContext()
        {
            return new NpcDialogueTriggerContext
            {
                Faction = faction,
                TriggerType = triggerType,
                Category = category,
                Reason = reason,
                SourceTag = sourceTag,
                Severity = severity,
                CreatedTick = createdTick,
                GoodwillDelta = goodwillDelta
            };
        }

        public static QueuedNpcDialogueTrigger FromContext(
            NpcDialogueTriggerContext context,
            int nowTick,
            int dueTickValue,
            int expireTickValue)
        {
            return new QueuedNpcDialogueTrigger
            {
                faction = context?.Faction,
                triggerType = context?.TriggerType ?? NpcDialogueTriggerType.Ambient,
                category = context?.Category ?? NpcDialogueCategory.Social,
                reason = context?.Reason ?? string.Empty,
                sourceTag = context?.SourceTag ?? string.Empty,
                severity = context?.Severity ?? 1,
                createdTick = context?.CreatedTick ?? nowTick,
                enqueuedTick = nowTick,
                dueTick = dueTickValue,
                expireTick = expireTickValue,
                goodwillDelta = context?.GoodwillDelta ?? 0f
            };
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref triggerType, "triggerType", NpcDialogueTriggerType.Ambient);
            Scribe_Values.Look(ref category, "category", NpcDialogueCategory.Social);
            Scribe_Values.Look(ref reason, "reason", string.Empty);
            Scribe_Values.Look(ref sourceTag, "sourceTag", string.Empty);
            Scribe_Values.Look(ref severity, "severity", 1);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref enqueuedTick, "enqueuedTick", 0);
            Scribe_Values.Look(ref dueTick, "dueTick", 0);
            Scribe_Values.Look(ref expireTick, "expireTick", 0);
            Scribe_Values.Look(ref goodwillDelta, "goodwillDelta", 0f);
        }
    }

    /// <summary>/// Dependencies: RimWorld.Faction, Verse.IExposable.
 /// Responsibility: Persist per-faction push cooldown and interaction state.
 ///</summary>
    public class FactionNpcPushState : IExposable
    {
        public Faction faction;
        public int nextAllowedTick;
        public int lastPushTick;
        public int lastInteractionTick;
        public int lastNegativeSpikeTick;

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref nextAllowedTick, "nextAllowedTick", 0);
            Scribe_Values.Look(ref lastPushTick, "lastPushTick", 0);
            Scribe_Values.Look(ref lastInteractionTick, "lastInteractionTick", 0);
            Scribe_Values.Look(ref lastNegativeSpikeTick, "lastNegativeSpikeTick", 0);
        }
    }
}


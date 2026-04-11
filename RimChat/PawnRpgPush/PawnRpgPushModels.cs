using RimChat.NpcDialogue;
using RimChat.Util;
using RimWorld;
using Verse;
using System.Linq;

namespace RimChat.PawnRpgPush
{
    /// <summary>/// Dependencies: RimWorld.Faction, RimChat.NpcDialogue enums.
 /// Responsibility: Carry one PawnRPG proactive trigger before queueing or generation.
 ///</summary>
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

    /// <summary>/// Dependencies: RimWorld.Faction, Verse.Scribe.
 /// Responsibility: Persist one delayed PawnRPG proactive trigger.
 ///</summary>
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
            string factionId = faction?.GetUniqueLoadID() ?? string.Empty;
            Scribe_Values.Look(ref factionId, "factionId", string.Empty);
            // Remove legacy <faction> reference node from old saves without registering
            // in CrossRefHandler — prevents "Not all loadIDs consumed" on dead factions.
            LegacyScribeHelper.RemoveLegacyReferenceNode("faction");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (!string.IsNullOrEmpty(factionId))
                {
                    faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.GetUniqueLoadID() == factionId);
                }
            }
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

    /// <summary>/// Dependencies: Verse.Pawn, Verse.Scribe.
 /// Responsibility: Persist per-NPC successful-delivery cooldown anchor.
 ///</summary>
    public class PawnRpgNpcPushState : IExposable
    {
        public Pawn pawn;
        public int lastNpcEvaluateTick;
        public int pawnThingId = -1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnThingId, "pawnThingId", -1);
            // Remove legacy <pawn> reference node from old saves without registering
            // in CrossRefHandler — prevents "Not all loadIDs consumed" on destroyed pawns.
            LegacyScribeHelper.RemoveLegacyReferenceNode("pawn");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pawnThingId > 0 && (pawn == null || pawn.Destroyed || pawn.Dead))
                {
                    pawn = PawnsFinder.AllMapsWorldAndTemporary_Alive
                        .FirstOrDefault(p => p != null && p.thingIDNumber == pawnThingId && !p.Destroyed && !p.Dead);
                }
                // If pawn still null after resolve, CleanupInvalidState() will remove this entry.
                // Do NOT fall back to Scribe_References.Look for legacy saves —
                // recycled pawns would cause "Could not resolve reference" errors.
            }
            else if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (pawn != null && !pawn.Destroyed && !pawn.Dead)
                {
                    pawnThingId = pawn.thingIDNumber;
                }
                // No longer write <pawn> reference node — pawnThingId replaces it.
            }
            Scribe_Values.Look(ref lastNpcEvaluateTick, "lastNpcEvaluateTick", 0);
        }
    }

    /// <summary>/// Dependencies: RimWorld.Faction, Verse.Scribe.
 /// Responsibility: Persist per-faction threat-edge state to avoid warning spam.
 ///</summary>
    public class PawnRpgThreatState : IExposable
    {
        public Faction faction;
        public bool hadThreat;

        public void ExposeData()
        {
            string factionId = faction?.GetUniqueLoadID() ?? string.Empty;
            Scribe_Values.Look(ref factionId, "factionId", string.Empty);
            // Remove legacy <faction> reference node from old saves without registering
            // in CrossRefHandler — prevents "Not all loadIDs consumed" on dead factions.
            LegacyScribeHelper.RemoveLegacyReferenceNode("faction");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (!string.IsNullOrEmpty(factionId))
                {
                    faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.GetUniqueLoadID() == factionId);
                }
            }
            Scribe_Values.Look(ref hadThreat, "hadThreat", false);
        }
    }

    /// <summary>/// Dependencies: Verse pawn references and world pawn resolver.
    /// Responsibility: Persist one configured PawnRPG proactive protagonist with ref+id fallback.
    ///</summary>
    public class PawnRpgProtagonistEntry : IExposable
    {
        public Pawn pawn;
        public int pawnThingId = -1;

        public bool HasConfiguredIdentifier =>
            pawn != null ||
            pawnThingId > 0;

        public static PawnRpgProtagonistEntry FromPawn(Pawn source)
        {
            return new PawnRpgProtagonistEntry
            {
                pawn = source,
                pawnThingId = source?.thingIDNumber ?? -1
            };
        }

        public Pawn TryResolvePawn()
        {
            if (pawn != null && !pawn.Destroyed)
            {
                return pawn;
            }

            if (pawnThingId <= 0)
            {
                return null;
            }

            Pawn resolved = PawnsFinder.AllMapsWorldAndTemporary_Alive
                .FirstOrDefault(p => p != null && p.thingIDNumber == pawnThingId);
            if (resolved != null)
            {
                pawn = resolved;
            }

            return pawn;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnThingId, "pawnThingId", -1);
            // Remove legacy <pawn> reference node from old saves without registering
            // in CrossRefHandler — prevents "Not all loadIDs consumed" on destroyed pawns.
            LegacyScribeHelper.RemoveLegacyReferenceNode("pawn");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Pawn resolved = PawnsFinder.AllMapsWorldAndTemporary_Alive
                    .FirstOrDefault(p => p != null && p.thingIDNumber == pawnThingId);
                if (resolved != null)
                {
                    pawn = resolved;
                }
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit && pawn != null && pawnThingId <= 0)
            {
                pawnThingId = pawn.thingIDNumber;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    public static partial class DiplomacyEventManager
    {
        private static readonly string[] MiliraRaidIncidentDefCandidates =
        {
            "milira-raid",
            "milira_raid",
            "MiliraRaid",
            "Milira_Raid",
            "RimChat_MiliraRaid"
        };

        private static bool EnsureRaidTemplates(Faction faction, out string reason)
        {
            reason = string.Empty;
            if (HasUsableCombatPawnGroupMaker(faction, out _))
            {
                return true;
            }

            if (!TryInjectDefaultCombatPawnGroupMaker(faction, out string injectReason))
            {
                reason = injectReason;
                return false;
            }

            if (HasUsableCombatPawnGroupMaker(faction, out _))
            {
                Log.Warning($"[RimChat] Applied default raid combat template for faction {faction?.Name}: {injectReason}");
                return true;
            }

            reason = "default raid combat template injection did not produce a usable Combat pawnGroupMaker.";
            return false;
        }

        private static bool TryInjectDefaultCombatPawnGroupMaker(Faction faction, out string reason)
        {
            reason = string.Empty;
            FactionDef factionDef = faction?.def;
            if (factionDef == null)
            {
                reason = "faction def is null.";
                return false;
            }

            List<PawnGroupMaker> makers = factionDef.pawnGroupMakers;
            if (makers == null)
            {
                makers = new List<PawnGroupMaker>();
                factionDef.pawnGroupMakers = makers;
            }

            List<PawnGenOption> fallbackOptions = BuildDefaultCombatOptions(faction, makers);
            if (fallbackOptions.Count == 0)
            {
                reason = "cannot resolve pawn kind for default Combat pawnGroupMaker.";
                return false;
            }

            var fallbackMaker = new PawnGroupMaker
            {
                kindDef = PawnGroupKindDefOf.Combat,
                commonality = 1f,
                options = fallbackOptions,
                maxTotalPoints = 100000f
            };
            makers.Add(fallbackMaker);
            reason = $"injected Combat pawnGroupMaker with {fallbackOptions.Count} option(s).";
            return true;
        }

        private static List<PawnGenOption> BuildDefaultCombatOptions(Faction faction, List<PawnGroupMaker> makers)
        {
            List<PawnGenOption> cloned = makers
                .Where(m => m?.options != null && m.options.Count > 0)
                .SelectMany(m => m.options)
                .Where(o => o?.kind != null)
                .Take(12)
                .Select(ClonePawnGenOption)
                .Where(o => o != null)
                .ToList();
            if (cloned.Count > 0)
            {
                return cloned;
            }

            PawnKindDef fallbackKind = ResolveFallbackRaidPawnKind(faction, makers);
            if (fallbackKind == null)
            {
                return new List<PawnGenOption>();
            }

            return new List<PawnGenOption>
            {
                new PawnGenOption
                {
                    kind = fallbackKind,
                    selectionWeight = 1f
                }
            };
        }

        private static PawnGenOption ClonePawnGenOption(PawnGenOption source)
        {
            if (source?.kind == null)
            {
                return null;
            }

            float weight = source.selectionWeight > 0f ? source.selectionWeight : 1f;
            return new PawnGenOption
            {
                kind = source.kind,
                selectionWeight = weight
            };
        }

        private static PawnKindDef ResolveFallbackRaidPawnKind(Faction faction, List<PawnGroupMaker> makers)
        {
            PawnKindDef kindFromFaction = faction?.def?.basicMemberKind;
            if (kindFromFaction != null)
            {
                return kindFromFaction;
            }

            PawnKindDef leaderKind = faction?.def?.fixedLeaderKinds?.FirstOrDefault(k => k != null);
            if (leaderKind != null)
            {
                return leaderKind;
            }

            PawnKindDef existingKind = makers?
                .Where(m => m?.options != null)
                .SelectMany(m => m.options)
                .Select(o => o?.kind)
                .FirstOrDefault(k => k != null);
            if (existingKind != null)
            {
                return existingKind;
            }

            PawnKindDef factionOwnedKind = DefDatabase<PawnKindDef>.AllDefsListForReading
                .FirstOrDefault(k => k != null && k.defaultFactionDef == faction?.def);
            if (factionOwnedKind != null)
            {
                return factionOwnedKind;
            }

            PawnKindDef villager = DefDatabase<PawnKindDef>.GetNamedSilentFail("Villager");
            if (villager != null)
            {
                return villager;
            }

            return DefDatabase<PawnKindDef>.AllDefsListForReading
                .FirstOrDefault(k => k?.RaceProps != null && k.RaceProps.Humanlike);
        }

        private static IncidentParms BuildRaidIncidentParmsWithDefaults(
            IncidentDef incidentDef,
            Map map,
            Faction faction,
            float raidPoints,
            RaidStrategyDef strategy,
            PawnsArrivalModeDef arrivalMode)
        {
            IncidentParms parms = null;
            try
            {
                parms = StorytellerUtility.DefaultParmsNow(incidentDef.category, map);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to build storyteller raid parms: {ex.Message}");
            }

            if (parms == null)
            {
                parms = new IncidentParms();
            }

            parms.target = map;
            parms.faction = faction;
            parms.points = raidPoints > 0f ? raidPoints : ResolveBaseRaidPointsFromStoryteller(map);
            parms.raidStrategy = strategy;
            parms.raidArrivalMode = arrivalMode;
            if (parms.raidStrategy != null && parms.raidArrivalMode == null)
            {
                parms.raidArrivalMode = GetFallbackArrivalMode(parms.raidStrategy);
            }
            parms.forced = true;
            return parms;
        }

        private static bool EnsureUsableCombatPawnGroupMakerForParms(Faction faction, IncidentParms raidParms, out string reason)
        {
            reason = string.Empty;
            if (HasUsableCombatPawnGroupMakerForParms(faction, raidParms, out _))
            {
                return true;
            }

            if (TryRaiseRaidPointsToMeetCombatMinimum(faction, raidParms, out string pointAdjustReason)
                && HasUsableCombatPawnGroupMakerForParms(faction, raidParms, out _))
            {
                Log.Warning($"[RimChat] Raised raid points for faction {faction?.Name}: {pointAdjustReason}");
                return true;
            }

            if (!TryInjectEmergencyCombatPawnGroupMakerForParms(faction, raidParms, out string injectReason))
            {
                reason = !string.IsNullOrEmpty(injectReason)
                    ? injectReason
                    : pointAdjustReason;
                return false;
            }

            if (HasUsableCombatPawnGroupMakerForParms(faction, raidParms, out _))
            {
                Log.Warning($"[RimChat] Injected emergency raid combat template for faction {faction?.Name}: {injectReason}");
                return true;
            }

            reason = "emergency raid combat template injection did not produce a usable combat maker for current parms.";
            return false;
        }

        private static bool HasUsableCombatPawnGroupMakerForParms(Faction faction, IncidentParms raidParms, out string reason)
        {
            reason = string.Empty;
            if (faction?.def == null)
            {
                reason = "faction def is null.";
                return false;
            }

            PawnGroupMakerParms groupParms = BuildRaidGroupMakerParms(raidParms, out string buildReason);
            if (groupParms == null)
            {
                reason = buildReason;
                return false;
            }

            if (groupParms.faction == null)
            {
                reason = "group parms faction is null.";
                return false;
            }

            List<PawnGroupMaker> combatMakers = faction.def.pawnGroupMakers?
                .Where(m => m?.kindDef == PawnGroupKindDefOf.Combat && m.options != null && m.options.Count > 0)
                .ToList() ?? new List<PawnGroupMaker>();
            if (combatMakers.Count == 0)
            {
                reason = "no combat makers with options.";
                return false;
            }

            if (SafeHasAnyPreviewKinds(groupParms))
            {
                return true;
            }

            reason = $"combat makers exist ({combatMakers.Count}) but none can generate for current raid parms.";
            return false;
        }

        private static PawnGroupMakerParms BuildRaidGroupMakerParms(IncidentParms raidParms, out string reason)
        {
            reason = string.Empty;
            if (raidParms == null)
            {
                reason = "raid parms is null.";
                return null;
            }

            try
            {
                PawnGroupMakerParms groupParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(PawnGroupKindDefOf.Combat, raidParms, true);
                if (groupParms == null)
                {
                    reason = "IncidentParmsUtility returned null.";
                    return null;
                }

                groupParms.groupKind = PawnGroupKindDefOf.Combat;
                if (groupParms.faction == null)
                {
                    groupParms.faction = raidParms.faction;
                }

                if (groupParms.points <= 0f)
                {
                    groupParms.points = raidParms.points > 0f ? raidParms.points : 35f;
                }

                return groupParms;
            }
            catch (Exception ex)
            {
                reason = $"failed to build PawnGroupMakerParms: {ex.Message}";
                return null;
            }
        }

        private static bool SafeCanGenerateFrom(PawnGroupMaker maker, PawnGroupMakerParms parms)
        {
            if (maker == null || parms == null)
            {
                return false;
            }

            try
            {
                return maker.CanGenerateFrom(parms);
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeHasPreviewKinds(PawnGroupMaker maker, PawnGroupMakerParms parms)
        {
            if (maker == null || parms == null)
            {
                return false;
            }

            try
            {
                IEnumerable<PawnKindDef> preview = maker.GeneratePawnKindsExample(parms);
                return preview != null && preview.Any(k => k != null);
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeHasAnyPreviewKinds(PawnGroupMakerParms parms)
        {
            if (parms == null)
            {
                return false;
            }

            try
            {
                IEnumerable<PawnKindDef> preview = PawnGroupMakerUtility.GeneratePawnKindsExample(parms);
                return preview != null && preview.Any(k => k != null);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryRaiseRaidPointsToMeetCombatMinimum(Faction faction, IncidentParms raidParms, out string reason)
        {
            reason = string.Empty;
            if (faction?.def == null || raidParms == null)
            {
                reason = "faction def or raid parms is null.";
                return false;
            }

            PawnGroupMakerParms groupParms = BuildRaidGroupMakerParms(raidParms, out string buildReason);
            if (groupParms == null)
            {
                reason = buildReason;
                return false;
            }

            List<float> mins = faction.def.pawnGroupMakers?
                .Where(m => m?.kindDef == PawnGroupKindDefOf.Combat && m.options != null && m.options.Count > 0)
                .Select(m => SafeMinPointsToGenerateAnything(m, faction.def, groupParms))
                .Where(v => v > 0f && !float.IsNaN(v) && !float.IsInfinity(v))
                .ToList() ?? new List<float>();
            if (mins.Count == 0)
            {
                reason = "no combat makers reported min points.";
                return false;
            }

            float currentPoints = raidParms.points > 0f ? raidParms.points : 35f;
            float minRequired = mins.Min() + 1f;
            float factionMin = SafeMinPointsToGeneratePawnGroup(faction.def, groupParms);
            if (factionMin > 0f)
            {
                minRequired = Math.Max(minRequired, factionMin + 1f);
            }
            float[] multipliers = { 1f, 1.5f, 2.5f, 4f, 6f };
            for (int i = 0; i < multipliers.Length; i++)
            {
                float candidate = Math.Max(minRequired, currentPoints * multipliers[i]);
                candidate = Math.Max(candidate, 120f);
                raidParms.points = candidate;
                if (HasUsableCombatPawnGroupMakerForParms(faction, raidParms, out _))
                {
                    reason = $"raised points to {candidate:F1} (minRequired={minRequired:F1}, x{multipliers[i]:F1}).";
                    return true;
                }
            }

            reason = $"tried point escalation from {currentPoints:F1} with minRequired={minRequired:F1}, but no usable combat maker.";
            return false;
        }

        private static float SafeMinPointsToGenerateAnything(PawnGroupMaker maker, FactionDef factionDef, PawnGroupMakerParms parms)
        {
            if (maker == null || factionDef == null || parms == null)
            {
                return 0f;
            }

            try
            {
                return maker.MinPointsToGenerateAnything(factionDef, parms);
            }
            catch
            {
                return 0f;
            }
        }

        private static float SafeMinPointsToGeneratePawnGroup(FactionDef factionDef, PawnGroupMakerParms parms)
        {
            if (factionDef == null || parms == null)
            {
                return 0f;
            }

            try
            {
                return factionDef.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat, parms);
            }
            catch
            {
                return 0f;
            }
        }

        private static bool TryInjectEmergencyCombatPawnGroupMakerForParms(Faction faction, IncidentParms raidParms, out string reason)
        {
            reason = string.Empty;
            FactionDef factionDef = faction?.def;
            if (factionDef == null)
            {
                reason = "faction def is null.";
                return false;
            }

            List<PawnGroupMaker> makers = factionDef.pawnGroupMakers;
            if (makers == null)
            {
                makers = new List<PawnGroupMaker>();
                factionDef.pawnGroupMakers = makers;
            }

            PawnGroupMakerParms groupParms = BuildRaidGroupMakerParms(raidParms, out string buildReason);
            if (groupParms == null)
            {
                reason = buildReason;
                return false;
            }

            List<PawnGenOption> emergencyOptions = BuildEmergencyCombatOptions(faction, groupParms);
            if (emergencyOptions.Count == 0)
            {
                reason = "cannot resolve any emergency pawn kinds.";
                return false;
            }

            var emergencyMaker = new PawnGroupMaker
            {
                kindDef = PawnGroupKindDefOf.Combat,
                commonality = 1000f,
                options = emergencyOptions,
                maxTotalPoints = 1000000f
            };
            makers.Add(emergencyMaker);

            if (HasUsableCombatPawnGroupMakerForParms(faction, raidParms, out _))
            {
                reason = $"added emergency combat maker with {emergencyOptions.Count} options.";
                return true;
            }

            reason = "added emergency combat maker but it is still not usable for current raid parms.";
            return false;
        }

        private static List<PawnGenOption> BuildEmergencyCombatOptions(Faction faction, PawnGroupMakerParms groupParms)
        {
            List<PawnKindDef> candidates = BuildEmergencyCombatKinds(faction);
            return candidates
                .Where(kind => CanKindGenerateForParms(kind, groupParms))
                .Take(12)
                .Select(kind => new PawnGenOption
                {
                    kind = kind,
                    selectionWeight = kind.combatPower > 0f ? kind.combatPower : 1f
                })
                .ToList();
        }

        private static List<PawnKindDef> BuildEmergencyCombatKinds(Faction faction)
        {
            var result = new List<PawnKindDef>();
            var seen = new HashSet<PawnKindDef>();

            void AddCandidate(PawnKindDef kind)
            {
                if (!IsEmergencyRaidKindCandidate(kind))
                {
                    return;
                }

                if (seen.Add(kind))
                {
                    result.Add(kind);
                }
            }

            AddCandidate(faction?.def?.basicMemberKind);
            if (faction?.def?.fixedLeaderKinds != null)
            {
                for (int i = 0; i < faction.def.fixedLeaderKinds.Count; i++)
                {
                    AddCandidate(faction.def.fixedLeaderKinds[i]);
                }
            }

            List<PawnKindDef> allKinds = DefDatabase<PawnKindDef>.AllDefsListForReading;
            for (int i = 0; i < allKinds.Count; i++)
            {
                PawnKindDef kind = allKinds[i];
                if (kind?.defaultFactionDef == faction?.def)
                {
                    AddCandidate(kind);
                }
            }

            for (int i = 0; i < allKinds.Count; i++)
            {
                PawnKindDef kind = allKinds[i];
                if (kind != null && kind.defaultFactionDef == null)
                {
                    AddCandidate(kind);
                }
            }

            AddCandidate(DefDatabase<PawnKindDef>.GetNamedSilentFail("Villager"));
            return result;
        }

        private static bool CanKindGenerateForParms(PawnKindDef kind, PawnGroupMakerParms groupParms)
        {
            if (!IsEmergencyRaidKindCandidate(kind) || groupParms == null)
            {
                return false;
            }

            var testMaker = new PawnGroupMaker
            {
                kindDef = PawnGroupKindDefOf.Combat,
                commonality = 1f,
                maxTotalPoints = 1000000f,
                options = new List<PawnGenOption>
                {
                    new PawnGenOption
                    {
                        kind = kind,
                        selectionWeight = kind.combatPower > 0f ? kind.combatPower : 1f
                    }
                }
            };
            return SafeCanGenerateFrom(testMaker, groupParms) && SafeHasPreviewKinds(testMaker, groupParms);
        }

        private static bool IsEmergencyRaidKindCandidate(PawnKindDef kind)
        {
            return kind != null
                && kind.RaceProps != null
                && kind.RaceProps.Humanlike
                && kind.combatPower > 0f
                && !kind.factionLeader;
        }

        private static bool TryExecuteMiliraRaidFallback(Map map, Faction faction, float raidPoints, out string reason)
        {
            reason = "skipped";
            if (!IsMiliraFaction(faction))
            {
                reason = "not milira faction";
                return false;
            }

            IncidentDef fallbackIncident = GetMiliraRaidIncidentDef(out string incidentReason);
            if (fallbackIncident == null)
            {
                reason = incidentReason;
                return false;
            }

            if (fallbackIncident.Worker == null)
            {
                reason = $"incident {fallbackIncident.defName} has null worker";
                return false;
            }

            IncidentParms seedParms = BuildRaidIncidentParmsWithDefaults(
                fallbackIncident,
                map,
                faction,
                raidPoints,
                strategy: null,
                arrivalMode: null);
            PawnGroupMakerParms seedGroupParms = BuildRaidGroupMakerParms(seedParms, out _);
            float minRequiredPoints = SafeMinPointsToGeneratePawnGroup(faction?.def, seedGroupParms);

            List<float> pointCandidates = BuildMiliraFallbackPointCandidates(raidPoints, minRequiredPoints);
            var attemptNotes = new List<string>();
            for (int i = 0; i < pointCandidates.Count; i++)
            {
                float candidatePoints = pointCandidates[i];
                IncidentParms fallbackParms = BuildRaidIncidentParmsWithDefaults(
                    fallbackIncident,
                    map,
                    faction,
                    candidatePoints,
                    strategy: null,
                    arrivalMode: null);

                bool ensureOk = EnsureUsableCombatPawnGroupMakerForParms(faction, fallbackParms, out string ensureReason);
                if (string.IsNullOrEmpty(ensureReason))
                {
                    ensureReason = ensureOk ? "ok" : "failed";
                }

                if (!ensureOk)
                {
                    attemptNotes.Add($"points={fallbackParms.points:F1}, ensure={ensureReason}");
                    continue;
                }

                if (!fallbackIncident.Worker.CanFireNow(fallbackParms))
                {
                    attemptNotes.Add($"points={fallbackParms.points:F1}, canFire=false, ensure={ensureReason}");
                    continue;
                }

                if (fallbackIncident.Worker.TryExecute(fallbackParms))
                {
                    reason = $"executed incident {fallbackIncident.defName} at points={fallbackParms.points:F1}";
                    Log.Warning($"[RimChat] Milira raid fallback triggered: incident={fallbackIncident.defName}, faction={faction?.Name}, points={fallbackParms.points:F1}");
                    return true;
                }

                attemptNotes.Add($"points={fallbackParms.points:F1}, tryExecute=false, ensure={ensureReason}");
            }

            reason = $"incident {fallbackIncident.defName} failed. attempts={string.Join(" | ", attemptNotes)}";
            return false;
        }

        private static List<float> BuildMiliraFallbackPointCandidates(float requestedPoints, float minRequiredPoints)
        {
            float basePoints = requestedPoints > 0f ? requestedPoints : 90f;
            float minFloor = minRequiredPoints > 0f ? minRequiredPoints + 1f : 0f;
            float[] raw = new[]
            {
                Math.Max(basePoints, minFloor),
                Math.Max(basePoints * 1.5f, Math.Max(120f, minFloor)),
                Math.Max(basePoints * 2.5f, Math.Max(220f, minFloor)),
                Math.Max(basePoints * 4f, Math.Max(400f, minFloor)),
                Math.Max(basePoints * 6f, Math.Max(700f, minFloor)),
                Math.Max(basePoints * 9f, Math.Max(1100f, minFloor))
            };

            var candidates = new List<float>();
            var seen = new HashSet<int>();
            for (int i = 0; i < raw.Length; i++)
            {
                float value = raw[i];
                int key = (int)Math.Round(value);
                if (seen.Add(key))
                {
                    candidates.Add(value);
                }
            }

            return candidates;
        }

        private static IncidentDef GetMiliraRaidIncidentDef(out string reason)
        {
            reason = "not found";
            for (int i = 0; i < MiliraRaidIncidentDefCandidates.Length; i++)
            {
                string candidate = MiliraRaidIncidentDefCandidates[i];
                IncidentDef byName = DefDatabase<IncidentDef>.GetNamedSilentFail(candidate);
                if (byName != null)
                {
                    reason = $"resolved by defName={candidate}";
                    return byName;
                }
            }

            IncidentDef fuzzyMatch = DefDatabase<IncidentDef>.AllDefsListForReading
                .FirstOrDefault(def => ContainsIgnoreCase(def?.defName, "milira") && ContainsIgnoreCase(def?.defName, "raid"));
            if (fuzzyMatch != null)
            {
                reason = $"resolved by fuzzy defName={fuzzyMatch.defName}";
                return fuzzyMatch;
            }

            reason = $"missing candidates: {string.Join(", ", MiliraRaidIncidentDefCandidates)}";
            return null;
        }

        private static bool IsMiliraFaction(Faction faction)
        {
            string defName = faction?.def?.defName;
            if (ContainsIgnoreCase(defName, "milira") || ContainsIgnoreCase(defName, "mirila"))
            {
                return true;
            }

            string factionName = faction?.Name;
            return ContainsIgnoreCase(factionName, "milira") || ContainsIgnoreCase(factionName, "mirila");
        }

        private static bool ContainsIgnoreCase(string source, string token)
        {
            return !string.IsNullOrEmpty(source)
                && !string.IsNullOrEmpty(token)
                && source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

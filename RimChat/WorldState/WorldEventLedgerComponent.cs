using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RimChat.Config;
using RimChat.Persistence;
using RimWorld;
using Verse;

namespace RimChat.WorldState
{
    /// <summary>/// Dependencies: Verse.GameComponent, LetterStack, and RaidThreatSnapshotProvider.
     /// Responsibility: collect and persist recent world events and raid battle intel for prompt injection.
     ///</summary>
    public class WorldEventLedgerComponent : GameComponent
    {
        private const int DefaultMaxStoredRecords = 50;
        private const int LetterScanInterval = 250;
        private const int RaidScanInterval = 250;
        private const int LetterScanOffsetTicks = 0;
        private const int RaidScanOffsetTicks = 0;
        private const int MaxLettersPerScanPass = 24;
        private const int OldEventAgeThresholdTicks = 60000 * 60 * 24;
        private const int MaxCompressedSummaryLength = 100;
        private const int MaxProcessedLetterIds = 512;

        private static int _globalEventRevision = 1;
        public static int GlobalEventRevision => _globalEventRevision;

        public class OngoingRaidBattleState : IExposable
        {
            public int StartTick;
            public int LastUpdatedTick;
            public int MapId;
            public string MapLabel;
            public string AttackerFactionId;
            public string AttackerFactionName;
            public string DefenderFactionId;
            public string DefenderFactionName;
            public int AttackerDeaths;
            public int DefenderDeaths;
            public int DefenderDownedPeak;

            public OngoingRaidBattleState()
            {
                MapLabel = string.Empty;
                AttackerFactionId = string.Empty;
                AttackerFactionName = string.Empty;
                DefenderFactionId = string.Empty;
                DefenderFactionName = string.Empty;
            }

            public void ExposeData()
            {
                Scribe_Values.Look(ref StartTick, "startTick", 0);
                Scribe_Values.Look(ref LastUpdatedTick, "lastUpdatedTick", 0);
                Scribe_Values.Look(ref MapId, "mapId", -1);
                Scribe_Values.Look(ref MapLabel, "mapLabel", string.Empty);
                Scribe_Values.Look(ref AttackerFactionId, "attackerFactionId", string.Empty);
                Scribe_Values.Look(ref AttackerFactionName, "attackerFactionName", string.Empty);
                Scribe_Values.Look(ref DefenderFactionId, "defenderFactionId", string.Empty);
                Scribe_Values.Look(ref DefenderFactionName, "defenderFactionName", string.Empty);
                Scribe_Values.Look(ref AttackerDeaths, "attackerDeaths", 0);
                Scribe_Values.Look(ref DefenderDeaths, "defenderDeaths", 0);
                Scribe_Values.Look(ref DefenderDownedPeak, "defenderDownedPeak", 0);
            }
        }

        public static WorldEventLedgerComponent Instance => Current.Game?.GetComponent<WorldEventLedgerComponent>();

        private List<WorldEventRecord> worldEvents = new List<WorldEventRecord>();
        private List<RaidBattleReportRecord> raidBattleReports = new List<RaidBattleReportRecord>();
        private List<OngoingRaidBattleState> ongoingRaidBattles = new List<OngoingRaidBattleState>();
        private List<int> processedLetterIds = new List<int>();

        private readonly HashSet<int> processedLetterIdSet = new HashSet<int>();
        private readonly IRaidSnapshotProvider raidSnapshotProvider = RaidThreatSnapshotProvider.Instance;
        private int lastLetterScanTick = -LetterScanInterval;
        private int lastRaidScanTick = -RaidScanInterval;
        private int letterScanCursor;
        private const int CompressionPerTickBudget = 3;
        private int compressionTickMarker = -1;
        private int compressionThisTickCount = 0;

        public WorldEventLedgerComponent(Game game) : base()
        {
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            RimChatTrackedEntityRegistry.Reset();
            RimChatTrackedEntityRegistry.PrimeFromCurrentGame();
            raidSnapshotProvider.Reset();
            RebuildRuntimeCaches();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            RimChatTrackedEntityRegistry.Reset();
            RimChatTrackedEntityRegistry.PrimeFromCurrentGame();
            raidSnapshotProvider.Reset();
            RebuildRuntimeCaches();
            CleanupLoadedData();
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref worldEvents, "worldEvents", LookMode.Deep);
            Scribe_Collections.Look(ref raidBattleReports, "raidBattleReports", LookMode.Deep);
            Scribe_Collections.Look(ref ongoingRaidBattles, "ongoingRaidBattles", LookMode.Deep);
            Scribe_Collections.Look(ref processedLetterIds, "processedLetterIds", LookMode.Value);
            Scribe_Values.Look(ref lastLetterScanTick, "lastLetterScanTick", -LetterScanInterval);
            Scribe_Values.Look(ref lastRaidScanTick, "lastRaidScanTick", -RaidScanInterval);
            Scribe_Values.Look(ref _globalEventRevision, "worldEventRevision", 1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                worldEvents ??= new List<WorldEventRecord>();
                raidBattleReports ??= new List<RaidBattleReportRecord>();
                ongoingRaidBattles ??= new List<OngoingRaidBattleState>();
                processedLetterIds ??= new List<int>();
                raidSnapshotProvider.Reset();
                RebuildRuntimeCaches();
                CleanupLoadedData();
            }
        }

        public override void GameComponentTick()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.TickManager == null)
            {
                return;
            }

            int tick = Find.TickManager.TicksGame;
            if (ShouldRunScheduledTask(tick, lastLetterScanTick, LetterScanInterval, LetterScanOffsetTicks))
            {
                lastLetterScanTick = tick;
                PollLetterStackEvents(tick);
            }

            if (ShouldRunScheduledTask(tick, lastRaidScanTick, RaidScanInterval, RaidScanOffsetTicks))
            {
                lastRaidScanTick = tick;
                UpdateRaidBattleStates(tick);
            }
        }

        /// <summary>
        /// Immediately collect current world events and raid battle states.
        /// Used for manual force-generate to ensure latest events are available.
        /// </summary>
        public void CollectNow()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.TickManager == null)
            {
                return;
            }

            int tick = Find.TickManager.TicksGame;
            PollLetterStackEvents(tick);
            UpdateRaidBattleStates(tick, forceRefresh: true);
            lastLetterScanTick = tick;
            lastRaidScanTick = tick;
        }

        public List<WorldEventRecord> GetRecentWorldEvents(Faction observerFaction, int daysWindow, bool includePublic, bool includeDirect)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int minTick = now - Math.Max(1, daysWindow) * GenDate.TicksPerDay;
            string observerId = GetFactionId(observerFaction);

            return worldEvents
                .Where(record => record != null && record.OccurredTick >= minTick)
                .Where(record => CanObserverSeeEvent(observerId, record, includePublic, includeDirect))
                .OrderByDescending(record => record.OccurredTick)
                .ToList();
        }

        public List<RaidBattleReportRecord> GetRecentRaidBattleReports(Faction observerFaction, int daysWindow, bool includeDirect)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int minTick = now - Math.Max(1, daysWindow) * GenDate.TicksPerDay;
            string observerId = GetFactionId(observerFaction);

            return raidBattleReports
                .Where(record => record != null && record.BattleEndTick >= minTick)
                .Where(record => includeDirect && IsKnownToFaction(observerId, record.KnownFactionIds))
                .OrderByDescending(record => record.BattleEndTick)
                .ToList();
        }

        public void NotifyPawnKilled(Pawn victim, DamageInfo? dinfo)
        {
            Map map = victim?.MapHeld;
            if (map == null || !map.IsPlayerHome || victim?.Faction == null)
            {
                return;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            Faction attackerByDamage = dinfo?.Instigator?.Faction;
            OngoingRaidBattleState state = ResolveBattleForKill(map, victim.Faction, attackerByDamage, tick);
            if (state == null)
            {
                return;
            }

            if (victim.Faction == Faction.OfPlayer)
            {
                state.DefenderDeaths++;
            }
            else if (string.Equals(GetFactionId(victim.Faction), state.AttackerFactionId, StringComparison.Ordinal))
            {
                state.AttackerDeaths++;
            }

            state.LastUpdatedTick = tick;
        }

        public void RecordRaidIntent(Faction attackerFaction, bool delayed, string strategyDefName, string arrivalModeDefName)
        {
            if (attackerFaction == null)
            {
                return;
            }

            Map map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string strategy = string.IsNullOrWhiteSpace(strategyDefName) ? "default" : strategyDefName;
            string arrival = string.IsNullOrWhiteSpace(arrivalModeDefName) ? "default" : arrivalModeDefName;
            string mode = delayed ? "scheduled" : "immediate";
            string summary = $"{attackerFaction.Name} initiated a raid ({mode}, strategy={strategy}, arrival={arrival}).";

            RecordDirectEvent(
                "raid_intent",
                tick,
                map,
                summary,
                new[] { GetFactionId(attackerFaction), GetFactionId(Faction.OfPlayer) },
                $"raid-intent:{attackerFaction.GetUniqueLoadID()}:{tick}:{strategy}:{arrival}:{mode}");
        }

        public void RecordIncidentIntent(Faction sourceFaction, string incidentDefName, Map map)
        {
            if (sourceFaction == null || string.IsNullOrWhiteSpace(incidentDefName))
            {
                return;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            string summary = $"{sourceFaction.Name} triggered incident {incidentDefName}.";
            RecordDirectEvent(
                "incident_intent",
                tick,
                map,
                summary,
                new[] { GetFactionId(sourceFaction), GetFactionId(Faction.OfPlayer) },
                $"incident-intent:{sourceFaction.GetUniqueLoadID()}:{incidentDefName}:{tick}");
        }

        private void PollLetterStackEvents(int tick)
        {
            List<Letter> letters = Find.LetterStack?.LettersListForReading;
            if (letters == null || letters.Count == 0)
            {
                letterScanCursor = 0;
                return;
            }

            if (letterScanCursor < 0 || letterScanCursor >= letters.Count)
            {
                letterScanCursor = 0;
            }

            int remainingBudget = MaxLettersPerScanPass;
            while (letterScanCursor < letters.Count && remainingBudget > 0)
            {
                Letter letter = letters[letterScanCursor];
                letterScanCursor++;
                remainingBudget--;
                if (letter == null || processedLetterIdSet.Contains(letter.ID))
                {
                    continue;
                }

                processedLetterIds.Add(letter.ID);
                processedLetterIdSet.Add(letter.ID);
                TryAddMapEventFromLetter(letter, tick);
            }

            if (letterScanCursor >= letters.Count)
            {
                letterScanCursor = 0;
                TrimProcessedLetterIds();
            }
        }

        private void TryAddMapEventFromLetter(Letter letter, int tick)
        {
            Map map = ResolvePlayerHomeMapFromLetter(letter);
            if (map == null)
            {
                return;
            }

            string summary = BuildLetterSummary(letter);
            if (string.IsNullOrWhiteSpace(summary))
            {
                return;
            }

            string eventType = DetectEventType(summary);
            string key = $"letter:{letter.ID}:{eventType}";
            if (HasRecentSourceKey(key))
            {
                return;
            }

            var record = new WorldEventRecord
            {
                EventType = eventType,
                OccurredTick = tick,
                MapId = map.uniqueID,
                MapLabel = map.Parent?.LabelCap ?? map.Biome?.LabelCap ?? $"Map#{map.uniqueID}",
                IsPublic = true,
                Summary = summary,
                SourceKey = key,
                KnownFactionIds = new List<string>()
            };

            AddWorldEventRecord(record);
        }

        private void UpdateRaidBattleStates(int tick, bool forceRefresh = false)
        {
            bool available = forceRefresh
                ? raidSnapshotProvider.TryForceRefreshNow(tick, out RaidThreatSnapshot snapshot)
                : raidSnapshotProvider.TryGetSnapshot(tick, out snapshot);
            if (!available || snapshot?.Maps == null || snapshot.Maps.Count == 0)
            {
                return;
            }

            var activeKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < snapshot.Maps.Count; i++)
            {
                RaidThreatMapSnapshot mapSnapshot = snapshot.Maps[i];
                Map map = mapSnapshot?.Map;
                IReadOnlyList<Faction> hostileFactions = mapSnapshot?.HostileFactions;
                if (map == null || hostileFactions == null || hostileFactions.Count == 0)
                {
                    continue;
                }

                for (int j = 0; j < hostileFactions.Count; j++)
                {
                    Faction attacker = hostileFactions[j];
                    OngoingRaidBattleState state = GetOrCreateBattleState(map, attacker, tick);
                    state.DefenderDownedPeak = Math.Max(state.DefenderDownedPeak, mapSnapshot.PlayerDownedCount);
                    state.LastUpdatedTick = tick;
                    activeKeys.Add(GetBattleKey(map.uniqueID, state.AttackerFactionId));
                }
            }

            FinalizeEndedBattles(activeKeys, tick);
        }

        private void FinalizeEndedBattles(HashSet<string> activeKeys, int tick)
        {
            if (ongoingRaidBattles == null || ongoingRaidBattles.Count == 0)
            {
                return;
            }

            for (int i = ongoingRaidBattles.Count - 1; i >= 0; i--)
            {
                OngoingRaidBattleState state = ongoingRaidBattles[i];
                if (state == null)
                {
                    ongoingRaidBattles.RemoveAt(i);
                    continue;
                }

                string key = GetBattleKey(state.MapId, state.AttackerFactionId);
                if (activeKeys.Contains(key))
                {
                    continue;
                }

                AddRaidBattleReport(state, tick);
                ongoingRaidBattles.RemoveAt(i);
            }
        }

        private void AddRaidBattleReport(OngoingRaidBattleState state, int battleEndTick)
        {
            string summary =
                $"{state.AttackerFactionName} raid on {state.MapLabel} ended. " +
                $"Deaths(attacker={state.AttackerDeaths}, defender={state.DefenderDeaths}), " +
                $"defender downed={state.DefenderDownedPeak}.";

            var report = new RaidBattleReportRecord
            {
                BattleStartTick = state.StartTick,
                BattleEndTick = battleEndTick,
                MapId = state.MapId,
                MapLabel = state.MapLabel,
                AttackerFactionId = state.AttackerFactionId,
                AttackerFactionName = state.AttackerFactionName,
                DefenderFactionId = state.DefenderFactionId,
                DefenderFactionName = state.DefenderFactionName,
                AttackerDeaths = state.AttackerDeaths,
                DefenderDeaths = state.DefenderDeaths,
                DefenderDowned = state.DefenderDownedPeak,
                Summary = summary,
                KnownFactionIds = new List<string>
                {
                    state.AttackerFactionId,
                    state.DefenderFactionId
                }
            };

            raidBattleReports.Add(report);
            TrimRaidBattleReports();
        }

        private OngoingRaidBattleState ResolveBattleForKill(Map map, Faction victimFaction, Faction attackerFactionByDamage, int tick)
        {
            if (map == null || victimFaction == null)
            {
                return null;
            }

            if (victimFaction == Faction.OfPlayer)
            {
                OngoingRaidBattleState byAttacker = GetBattleState(map.uniqueID, attackerFactionByDamage);
                if (byAttacker != null)
                {
                    return byAttacker;
                }

                return ongoingRaidBattles?
                    .Where(state => state != null && state.MapId == map.uniqueID)
                    .OrderByDescending(state => state.LastUpdatedTick)
                    .FirstOrDefault();
            }

            OngoingRaidBattleState byVictimFaction = GetBattleState(map.uniqueID, victimFaction);
            if (byVictimFaction != null)
            {
                return byVictimFaction;
            }

            if (victimFaction.HostileTo(Faction.OfPlayer))
            {
                return GetOrCreateBattleState(map, victimFaction, tick);
            }

            return null;
        }

        private OngoingRaidBattleState GetBattleState(int mapId, Faction attackerFaction)
        {
            if (attackerFaction == null)
            {
                return null;
            }

            string attackerId = GetFactionId(attackerFaction);
            return ongoingRaidBattles?.FirstOrDefault(
                state => state != null && state.MapId == mapId &&
                         string.Equals(state.AttackerFactionId, attackerId, StringComparison.Ordinal));
        }

        private OngoingRaidBattleState GetOrCreateBattleState(Map map, Faction attackerFaction, int tick)
        {
            OngoingRaidBattleState state = GetBattleState(map.uniqueID, attackerFaction);
            if (state != null)
            {
                return state;
            }

            state = new OngoingRaidBattleState
            {
                StartTick = tick,
                LastUpdatedTick = tick,
                MapId = map.uniqueID,
                MapLabel = map.Parent?.LabelCap ?? map.Biome?.LabelCap ?? $"Map#{map.uniqueID}",
                AttackerFactionId = GetFactionId(attackerFaction),
                AttackerFactionName = attackerFaction?.Name ?? "UnknownFaction",
                DefenderFactionId = GetFactionId(Faction.OfPlayer),
                DefenderFactionName = Faction.OfPlayer?.Name ?? "PlayerFaction",
                AttackerDeaths = 0,
                DefenderDeaths = 0,
                DefenderDownedPeak = raidSnapshotProvider.ResolvePlayerDownedCount(map, tick)
            };

            ongoingRaidBattles.Add(state);
            return state;
        }

        private Map ResolvePlayerHomeMapFromLetter(Letter letter)
        {
            Map letterMap = TryResolveMapFromLetterTargets(letter);
            if (letterMap != null && letterMap.IsPlayerHome)
            {
                return letterMap;
            }

            return Find.AnyPlayerHomeMap
                ?? Find.Maps?.FirstOrDefault(map => map != null && map.IsPlayerHome);
        }

        private Map TryResolveMapFromLetterTargets(Letter letter)
        {
            if (letter == null)
            {
                return null;
            }

            try
            {
                var lookTargets = letter.lookTargets;
                if (lookTargets == null || !lookTargets.IsValid)
                {
                    return null;
                }

                var target = lookTargets.PrimaryTarget;
                return target.IsValid ? target.Map : null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Skipping letter map extraction for letter {letter.ID}: {ex.GetType().Name}");
                return null;
            }
        }

        private string BuildLetterSummary(Letter letter)
        {
            if (letter == null)
            {
                return string.Empty;
            }

            string label = letter.Label.ToString().Trim();
            string text = string.Empty;
            if (letter is ChoiceLetter choiceLetter)
            {
                text = choiceLetter.Text.ToString();
            }

            text = (text ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (text.Length > 220)
            {
                text = text.Substring(0, 220);
            }

            if (string.IsNullOrWhiteSpace(label) && string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return label;
            }

            return $"{label}: {text}";
        }

        private string DetectEventType(string summary)
        {
            string normalized = (summary ?? string.Empty).ToLowerInvariant();
            if (ContainsAny(normalized, "cold snap", "寒潮"))
            {
                return "cold_snap";
            }

            if (ContainsAny(normalized, "heat wave", "热浪"))
            {
                return "heat_wave";
            }

            if (ContainsAny(normalized, "blight", "枯萎"))
            {
                return "blight";
            }

            if (ContainsAny(normalized, "raid", "袭击", "attacking"))
            {
                return "raid";
            }

            if (ContainsAny(normalized, "died", "killed", "死亡"))
            {
                return "colonist_death";
            }

            return "map_event";
        }

        private bool ContainsAny(string text, params string[] tokens)
        {
            if (string.IsNullOrEmpty(text) || tokens == null)
            {
                return false;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (!string.IsNullOrEmpty(token) && text.Contains(token))
                {
                    return true;
                }
            }

            return false;
        }

        private void RecordDirectEvent(
            string eventType,
            int tick,
            Map map,
            string summary,
            IEnumerable<string> knownFactionIds,
            string sourceKey)
        {
            if (HasRecentSourceKey(sourceKey))
            {
                return;
            }

            var record = new WorldEventRecord
            {
                EventType = string.IsNullOrWhiteSpace(eventType) ? "map_event" : eventType,
                OccurredTick = tick,
                MapId = map?.uniqueID ?? -1,
                MapLabel = map?.Parent?.LabelCap ?? map?.Biome?.LabelCap ?? "UnknownMap",
                IsPublic = false,
                Summary = summary ?? string.Empty,
                SourceKey = sourceKey ?? string.Empty,
                KnownFactionIds = (knownFactionIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct()
                    .ToList()
            };

            AddWorldEventRecord(record);
        }

        private bool HasRecentSourceKey(string sourceKey)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
            {
                return false;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            int minTick = now - GenDate.TicksPerDay;
            return worldEvents != null &&
                   worldEvents.Any(record =>
                       record != null &&
                       record.OccurredTick >= minTick &&
                       string.Equals(record.SourceKey, sourceKey, StringComparison.Ordinal));
        }

        private void AddWorldEventRecord(WorldEventRecord record)
        {
            if (record == null)
            {
                return;
            }

            TryCompressRecordImmediate(record);
            worldEvents.Add(record);
            TrimWorldEvents();
            NotifyEventAdded();
        }

        private void NotifyEventAdded()
        {
            Interlocked.Increment(ref _globalEventRevision);
        }

        private bool CanObserverSeeEvent(string observerId, WorldEventRecord record, bool includePublic, bool includeDirect)
        {
            if (record == null)
            {
                return false;
            }

            if (record.IsPublic && includePublic)
            {
                return true;
            }

            return includeDirect && IsKnownToFaction(observerId, record.KnownFactionIds);
        }

        private bool IsKnownToFaction(string observerId, List<string> knownFactionIds)
        {
            if (string.IsNullOrWhiteSpace(observerId) || knownFactionIds == null || knownFactionIds.Count == 0)
            {
                return false;
            }

            return knownFactionIds.Any(id => string.Equals(id, observerId, StringComparison.Ordinal));
        }

        private static bool ShouldRunScheduledTask(int tick, int lastRunTick, int interval, int offset)
        {
            if (tick <= 0 || interval <= 0)
            {
                return false;
            }

            if (!IsOnScheduleSlot(tick, interval, offset))
            {
                return false;
            }

            return lastRunTick <= 0 || tick - lastRunTick >= interval;
        }

        private static bool IsOnScheduleSlot(int tick, int interval, int offset)
        {
            int normalized = (tick - offset) % interval;
            if (normalized < 0)
            {
                normalized += interval;
            }

            return normalized == 0;
        }

        private string GetFactionId(Faction faction)
        {
            return faction?.GetUniqueLoadID() ?? string.Empty;
        }

        private string GetBattleKey(int mapId, string attackerFactionId)
        {
            return $"{mapId}|{attackerFactionId ?? string.Empty}";
        }

        private void RebuildRuntimeCaches()
        {
            processedLetterIdSet.Clear();
            if (processedLetterIds == null)
            {
                processedLetterIds = new List<int>();
            }

            for (int i = 0; i < processedLetterIds.Count; i++)
            {
                processedLetterIdSet.Add(processedLetterIds[i]);
            }
        }

        private void CleanupLoadedData()
        {
            worldEvents = (worldEvents ?? new List<WorldEventRecord>())
                .Where(record => record != null)
                .ToList();
            raidBattleReports = (raidBattleReports ?? new List<RaidBattleReportRecord>())
                .Where(record => record != null)
                .ToList();
            ongoingRaidBattles = (ongoingRaidBattles ?? new List<OngoingRaidBattleState>())
                .Where(state => state != null && state.MapId >= 0 && !string.IsNullOrWhiteSpace(state.AttackerFactionId))
                .ToList();

            TrimWorldEvents();
            TrimRaidBattleReports();
            TrimProcessedLetterIds();
        }

        private void TrimWorldEvents()
        {
            int maxRecords = ResolveMaxStoredRecords();
            if (worldEvents == null || worldEvents.Count <= maxRecords)
            {
                return;
            }

            worldEvents = worldEvents
                .OrderBy(record => record.OccurredTick)
                .Skip(Math.Max(0, worldEvents.Count - maxRecords))
                .ToList();
        }

        private void TrimRaidBattleReports()
        {
            int maxRecords = ResolveMaxStoredRecords();
            if (raidBattleReports == null || raidBattleReports.Count <= maxRecords)
            {
                return;
            }

            raidBattleReports = raidBattleReports
                .OrderBy(record => record.BattleEndTick)
                .Skip(Math.Max(0, raidBattleReports.Count - maxRecords))
                .ToList();
        }

        private void TrimProcessedLetterIds()
        {
            if (processedLetterIds == null || processedLetterIds.Count <= MaxProcessedLetterIds)
            {
                return;
            }

            int removeCount = processedLetterIds.Count - MaxProcessedLetterIds;
            processedLetterIds.RemoveRange(0, removeCount);
            RebuildRuntimeCaches();
        }

        private int ResolveMaxStoredRecords()
        {
            try
            {
                EventIntelPromptConfig config = PromptPersistenceService.Instance
                    ?.LoadConfig()
                    ?.EnvironmentPrompt
                    ?.EventIntelPrompt;
                if (config == null)
                {
                    return DefaultMaxStoredRecords;
                }

                return Math.Max(10, config.MaxStoredRecords);
            }
            catch
            {
                return DefaultMaxStoredRecords;
            }
        }

        private void TryCompressOldWorldEvents(int tick)
        {
            if (worldEvents == null || worldEvents.Count == 0)
            {
                return;
            }

            int cutoffTick = tick - OldEventAgeThresholdTicks;
            int compressed = 0;
            int currentTick = tick;

            for (int i = 0; i < worldEvents.Count; i++)
            {
                WorldEventRecord record = worldEvents[i];
                if (record == null || record.IsCompressed)
                {
                    continue;
                }

                if (record.OccurredTick >= cutoffTick)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(record.Summary) || record.Summary.Length <= MaxCompressedSummaryLength)
                {
                    continue;
                }

                string original = record.Summary;
                record.Summary = CompressWorldEventSummaryImmediate(record, currentTick);
                record.IsCompressed = true;
                if (record.Summary != original)
                {
                    compressed++;
                }
            }

            if (compressed > 0)
            {
                Log.Message($"[RimChat] Compressed {compressed} old world event summaries.");
            }
        }

        private void TryCompressRecordImmediate(WorldEventRecord record)
        {
            if (record == null || record.IsCompressed)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(record.Summary) || record.Summary.Length <= MaxCompressedSummaryLength)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (compressionTickMarker != currentTick)
            {
                compressionTickMarker = currentTick;
                compressionThisTickCount = 0;
            }

            if (compressionThisTickCount >= CompressionPerTickBudget)
            {
                return;
            }

            compressionThisTickCount++;
            record.Summary = CompressWorldEventSummaryImmediate(record, currentTick);
            record.IsCompressed = true;
        }

        private string CompressWorldEventSummaryImmediate(WorldEventRecord record, int currentTick)
        {
            string text = record.Summary ?? string.Empty;
            if (text.Length <= MaxCompressedSummaryLength)
            {
                return text;
            }

            string ageText = BuildRelativeTickText(record.OccurredTick, currentTick);
            string type = string.IsNullOrWhiteSpace(record.EventType) ? "event" : record.EventType;
            string mapInfo = string.IsNullOrWhiteSpace(record.MapLabel) ? string.Empty : $" at {record.MapLabel}";

            string prefix = $"{type}{mapInfo} {ageText}: ";
            int remaining = MaxCompressedSummaryLength - prefix.Length;

            string trimmed = text.Trim();
            trimmed = trimmed.Replace("\n", " ").Replace("\r", " ").Trim();
            trimmed = StripRedundantPhrases(trimmed);

            int maxContent = Math.Max(20, remaining);
            if (trimmed.Length <= maxContent)
            {
                return prefix + trimmed;
            }

            string result = prefix + trimmed.Substring(0, maxContent - 3) + "...";
            return result.Length > MaxCompressedSummaryLength
                ? result.Substring(0, MaxCompressedSummaryLength - 3) + "..."
                : result;
        }

        private string StripRedundantPhrases(string text)
        {
            string[] redundant = { "has been ", "was ", "is now ", "The colony ", "Your colony ", "The ", "Your " };
            string result = text;
            foreach (string phrase in redundant)
            {
                if (result.StartsWith(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(phrase.Length);
                    break;
                }
            }

            return result.Trim();
        }

        private string BuildRelativeTickText(int eventTick, int currentTick)
        {
            int diff = currentTick - eventTick;
            if (diff < 0)
            {
                diff = 0;
            }

            if (diff < GenDate.TicksPerHour)
            {
                int minutes = diff / GenDate.TicksPerHour * 60;
                return minutes <= 1 ? "just now" : $"{minutes}m ago";
            }

            if (diff < GenDate.TicksPerDay)
            {
                int hours = diff / GenDate.TicksPerDay;
                return hours == 1 ? "1h ago" : $"{hours}h ago";
            }

            int days = diff / GenDate.TicksPerDay;
            return days == 1 ? "1d ago" : $"{days}d ago";
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimDiplomacy.DiplomacySystem;
using RimDiplomacy.Relation;
using RimWorld;
using Verse;

namespace RimDiplomacy.Memory
{
    /// <summary>
    /// Dependencies: GameComponent_RPGManager, RimWorld save path, NPC dialogue turn feed.
    /// Responsibility: persist RPG dialogue archives per NPC into independent JSON files.
    /// </summary>
    public sealed class RpgNpcDialogueArchiveManager
    {
        private const string SaveRootDir = "RimDiplomacy";
        private const string SaveSubDir = "save_data";
        private const string NpcArchiveSubDir = "rpg_npc_dialogues";
        private const int MaxTurnsPerNpc = 300;

        private static RpgNpcDialogueArchiveManager _instance;
        public static RpgNpcDialogueArchiveManager Instance => _instance ?? (_instance = new RpgNpcDialogueArchiveManager());

        private readonly Dictionary<int, RpgNpcDialogueArchive> _archiveCache = new Dictionary<int, RpgNpcDialogueArchive>();
        private readonly object _syncRoot = new object();
        private bool _cacheLoaded;
        private string _loadedSaveKey = string.Empty;

        public void OnNewGame()
        {
            lock (_syncRoot)
            {
                _archiveCache.Clear();
                _cacheLoaded = false;
                _loadedSaveKey = string.Empty;
                EnsureCacheLoaded();
            }
        }

        public void OnLoadedGame()
        {
            lock (_syncRoot)
            {
                _archiveCache.Clear();
                _cacheLoaded = false;
                _loadedSaveKey = string.Empty;
            }
        }

        public void OnAfterGameLoad()
        {
            lock (_syncRoot)
            {
                EnsureCacheLoaded();
                ApplyArchivesToRuntime();
            }
        }

        public void OnBeforeGameSave()
        {
            lock (_syncRoot)
            {
                EnsureCacheLoaded();
                foreach (RpgNpcDialogueArchive archive in _archiveCache.Values)
                {
                    SaveArchiveToFile(archive);
                }
            }
        }

        public void RecordTurn(Pawn initiator, Pawn targetNpc, bool isPlayerSpeaker, string text, int tick)
        {
            if (targetNpc == null || targetNpc.Destroyed || targetNpc.Dead || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            lock (_syncRoot)
            {
                EnsureCacheLoaded();
                RpgNpcDialogueArchive archive = GetOrCreateArchive(targetNpc, tick);
                archive.LastInteractionTick = tick;
                archive.PawnName = ResolvePawnName(targetNpc);
                archive.FactionId = BuildFactionId(targetNpc.Faction);
                archive.FactionName = targetNpc.Faction?.Name ?? string.Empty;
                archive.Turns.Add(new RpgNpcDialogueTurnArchive
                {
                    IsPlayer = isPlayerSpeaker,
                    Text = text.Trim(),
                    GameTick = tick
                });
                TrimTurns(archive);
                CaptureRuntimeRpgState(targetNpc, archive);
            }
        }

        private string CurrentSaveKey
        {
            get
            {
                if (Current.Game == null)
                {
                    return "Default";
                }

                try
                {
                    object gameInfo = Current.Game.Info;
                    if (gameInfo != null)
                    {
                        var nameProperty = gameInfo.GetType().GetProperty("name");
                        string value = nameProperty?.GetValue(gameInfo) as string;
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value.SanitizeFileName();
                        }
                    }
                }
                catch
                {
                }

                return $"Save_{Current.Game.GetHashCode()}".SanitizeFileName();
            }
        }

        private string CurrentArchiveDirPath =>
            Path.Combine(GenFilePaths.SaveDataFolderPath, SaveRootDir, SaveSubDir, CurrentSaveKey, NpcArchiveSubDir);

        private void EnsureCacheLoaded()
        {
            string currentSaveKey = CurrentSaveKey;
            if (_cacheLoaded && string.Equals(_loadedSaveKey, currentSaveKey, StringComparison.Ordinal))
            {
                return;
            }

            _archiveCache.Clear();
            EnsureDataDirectoryExists();
            LoadAllArchivesFromFiles();
            _loadedSaveKey = currentSaveKey;
            _cacheLoaded = true;
        }

        private void EnsureDataDirectoryExists()
        {
            if (!Directory.Exists(CurrentArchiveDirPath))
            {
                Directory.CreateDirectory(CurrentArchiveDirPath);
            }
        }

        private void LoadAllArchivesFromFiles()
        {
            if (!Directory.Exists(CurrentArchiveDirPath))
            {
                return;
            }

            string[] files = Directory.GetFiles(CurrentArchiveDirPath, "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    string json = File.ReadAllText(files[i]);
                    RpgNpcDialogueArchive archive = RpgNpcDialogueArchiveJsonCodec.ParseJson(json);
                    if (archive != null && archive.PawnLoadId > 0)
                    {
                        _archiveCache[archive.PawnLoadId] = archive;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimDiplomacy] Failed to load RPG NPC archive file '{files[i]}': {ex.Message}");
                }
            }
        }

        private RpgNpcDialogueArchive GetOrCreateArchive(Pawn pawn, int tick)
        {
            int pawnId = pawn?.thingIDNumber ?? -1;
            if (pawnId <= 0)
            {
                return null;
            }

            if (_archiveCache.TryGetValue(pawnId, out RpgNpcDialogueArchive existing))
            {
                return existing;
            }

            var archive = new RpgNpcDialogueArchive
            {
                PawnLoadId = pawnId,
                PawnName = ResolvePawnName(pawn),
                FactionId = BuildFactionId(pawn?.Faction),
                FactionName = pawn?.Faction?.Name ?? string.Empty,
                CreatedTimestamp = DateTime.UtcNow.Ticks,
                LastInteractionTick = tick
            };
            _archiveCache[pawnId] = archive;
            return archive;
        }

        private void CaptureRuntimeRpgState(Pawn pawn, RpgNpcDialogueArchive archive)
        {
            GameComponent_RPGManager rpgManager = GameComponent_RPGManager.Instance;
            if (rpgManager == null || pawn == null || archive == null)
            {
                return;
            }

            archive.PersonaPrompt = rpgManager.GetPawnPersonaPrompt(pawn) ?? string.Empty;
            archive.CooldownUntilTick = rpgManager.GetDialogueCooldownUntilTick(pawn);
            if (rpgManager.TryGetRelation(pawn, out RPGRelationValues relationValues))
            {
                archive.RelationValues = CloneRelationValues(relationValues);
            }
        }

        private static void TrimTurns(RpgNpcDialogueArchive archive)
        {
            if (archive?.Turns == null || archive.Turns.Count <= MaxTurnsPerNpc)
            {
                return;
            }

            int removeCount = archive.Turns.Count - MaxTurnsPerNpc;
            archive.Turns.RemoveRange(0, removeCount);
        }

        private void SaveArchiveToFile(RpgNpcDialogueArchive archive)
        {
            if (archive == null || archive.PawnLoadId <= 0)
            {
                return;
            }

            try
            {
                EnsureDataDirectoryExists();
                archive.LastSavedTimestamp = DateTime.UtcNow.Ticks;
                string filePath = Path.Combine(CurrentArchiveDirPath, $"npc_{archive.PawnLoadId}.json");
                string json = RpgNpcDialogueArchiveJsonCodec.ConvertToJson(archive);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] Failed to save RPG NPC archive {archive.PawnLoadId}: {ex.Message}");
            }
        }

        private void ApplyArchivesToRuntime()
        {
            GameComponent_RPGManager rpgManager = GameComponent_RPGManager.Instance;
            if (rpgManager == null || _archiveCache.Count == 0)
            {
                return;
            }

            foreach (RpgNpcDialogueArchive archive in _archiveCache.Values)
            {
                if (archive == null || archive.PawnLoadId <= 0)
                {
                    continue;
                }

                Pawn pawn = FindPawnByLoadId(archive.PawnLoadId);
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(archive.PersonaPrompt))
                {
                    rpgManager.SetPawnPersonaPrompt(pawn, archive.PersonaPrompt);
                }

                if (archive.RelationValues != null)
                {
                    rpgManager.SetRelationValues(pawn, archive.RelationValues);
                }

                rpgManager.SetDialogueCooldownUntilTick(pawn, archive.CooldownUntilTick);
            }
        }

        private static Pawn FindPawnByLoadId(int pawnLoadId)
        {
            if (pawnLoadId <= 0)
            {
                return null;
            }

            IEnumerable<Pawn> worldPawns = Find.WorldPawns?.AllPawnsAliveOrDead;
            if (worldPawns != null)
            {
                Pawn found = worldPawns.FirstOrDefault(pawn => pawn != null && pawn.thingIDNumber == pawnLoadId);
                if (found != null)
                {
                    return found;
                }
            }

            if (Find.Maps == null)
            {
                return null;
            }

            foreach (Map map in Find.Maps)
            {
                Pawn found = map?.mapPawns?.AllPawnsSpawned?.FirstOrDefault(pawn => pawn != null && pawn.thingIDNumber == pawnLoadId);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string ResolvePawnName(Pawn pawn)
        {
            if (pawn == null)
            {
                return "UnknownPawn";
            }

            return pawn.LabelShort ?? pawn.Name?.ToStringShort ?? pawn.Name?.ToStringFull ?? "UnknownPawn";
        }

        private static string BuildFactionId(Faction faction)
        {
            if (faction == null)
            {
                return string.Empty;
            }

            if (faction.def != null && !string.IsNullOrEmpty(faction.def.defName))
            {
                return $"{faction.def.defName}_{faction.loadID}";
            }

            return $"custom_{faction.loadID}";
        }

        private static RPGRelationValues CloneRelationValues(RPGRelationValues source)
        {
            if (source == null)
            {
                return new RPGRelationValues();
            }

            return new RPGRelationValues
            {
                Favorability = source.Favorability,
                Trust = source.Trust,
                Fear = source.Fear,
                Respect = source.Respect,
                Dependency = source.Dependency
            };
        }
    }
}

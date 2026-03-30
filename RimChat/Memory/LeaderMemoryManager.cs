using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimChat.Core;
using Verse;
using RimWorld;

namespace RimChat.Memory
{
    /// <summary>/// factionleadermemorymanager
 /// 负责管理所有factionleadermemory的save和load
 /// 每个存档有独立的folder, 每个leader单独一个 JSON file
 ///</summary>
    public partial class LeaderMemoryManager
    {
        private const string InitSnapshotPrefix = "[init-snapshot]";
        private const string SessionBackfillPrefix = "[session-backfill]";
        private const int MaxSignificantEvents = 80;
        private const string SaveRootDir = "RimChat";
        private const string SaveSubDir = "save_data";
        private const string PromptFolderName = "Prompt";
        private const string NpcPromptSubDir = "NPC";
        private const string LeaderMemorySubDir = "leader_memories";
        private const string DefaultSaveName = "Default";
        private const string LegacyMigrationBackupDirName = "_migration_backup";
        private const string LegacyDefaultBucketClaimMarker = ".legacy_default_bucket_claimed";

        private static LeaderMemoryManager _instance;
        public static LeaderMemoryManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LeaderMemoryManager();
                }
                return _instance;
            }
        }

        /// <summary>/// 当前存档的memory数据目录
 ///</summary>
        private string CurrentSaveDataPath
        {
            get
            {
                return Path.Combine(CurrentPromptNpcRootPath, CurrentSaveKey, LeaderMemorySubDir);
            }
        }

        private string CurrentSaveKey
        {
            get
            {
                if (ShouldRefreshResolvedSaveKey())
                {
                    _resolvedSaveKey = ResolveCurrentSaveKey();
                }
                return _resolvedSaveKey;
            }
        }

        private string CurrentPromptNpcRootPath
        {
            get
            {
                try
                {
                    ModContentPack mod = LoadedModManager.GetMod<RimChatMod>()?.Content;
                    if (mod != null)
                    {
                        string path = Path.Combine(mod.RootDir, PromptFolderName, NpcPromptSubDir);
                        if (!Directory.Exists(path))
                        {
                            Directory.CreateDirectory(path);
                        }
                        return path;
                    }
                }
                catch
                {
                }

                string fallback = Path.Combine(GenFilePaths.ConfigFolderPath, SaveRootDir, PromptFolderName, NpcPromptSubDir);
                if (!Directory.Exists(fallback))
                {
                    Directory.CreateDirectory(fallback);
                }
                return fallback;
            }
        }

        /// <summary>/// 内存中的memory缓存
 ///</summary>
        private Dictionary<string, FactionLeaderMemory> _memoryCache = new Dictionary<string, FactionLeaderMemory>();
        private readonly Dictionary<string, int> diplomacyMemoryRevisions = new Dictionary<string, int>(StringComparer.Ordinal);
        public event Action<DiplomacyMemoryChangedEventArgs> DiplomacyMemoryChanged;

        /// <summary>/// 缓存whether已load
 ///</summary>
        private bool _cacheLoaded = false;
        private readonly object _summarySyncRoot = new object();
        private string _resolvedSaveKey = string.Empty;

        public int GetFactionMemoryRevision(Faction faction)
        {
            if (faction == null)
            {
                return 0;
            }

            EnsureCacheLoaded();
            string factionId = GetUniqueFactionId(faction);
            return GetFactionMemoryRevision(factionId);
        }

        internal int GetFactionMemoryRevision(string factionId)
        {
            if (string.IsNullOrWhiteSpace(factionId))
            {
                return 0;
            }

            if (!diplomacyMemoryRevisions.TryGetValue(factionId, out int revision))
            {
                revision = 0;
                diplomacyMemoryRevisions[factionId] = revision;
            }

            return revision;
        }

        internal DiplomacyMemoryChangedEventArgs PublishDiplomacyMemoryChanged(
            Faction faction,
            bool affectsCurrentSession,
            bool affectsPersistentHistory,
            bool affectsAiPrompt)
        {
            if (faction == null)
            {
                return null;
            }

            EnsureCacheLoaded();
            string factionId = GetUniqueFactionId(faction);
            int revision = GetFactionMemoryRevision(factionId) + 1;
            diplomacyMemoryRevisions[factionId] = revision;

            var args = new DiplomacyMemoryChangedEventArgs
            {
                FactionId = factionId,
                Revision = revision,
                AffectsCurrentSession = affectsCurrentSession,
                AffectsPersistentHistory = affectsPersistentHistory,
                AffectsAiPrompt = affectsAiPrompt
            };

            DiplomacyMemoryChanged?.Invoke(args);
            return args;
        }

        /// <summary>/// 确保数据目录presence
 ///</summary>
        public void EnsureDataDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(CurrentSaveDataPath))
                {
                    Directory.CreateDirectory(CurrentSaveDataPath);
                    Log.Message($"[RimChat] Created memory data directory: {CurrentSaveDataPath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to create data directory: {ex.Message}");
            }
        }

        private bool TryValidatePersistenceContext(string operationName)
        {
            try
            {
                _ = CurrentSaveKey;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                Log.Error($"[RimChat] Leader memory persistence blocked in {operationName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>/// get指定factionleader的memory
 ///</summary>
        public FactionLeaderMemory GetMemory(Faction faction)
        {
            if (faction == null) return null;

            EnsureCacheLoaded();

            var factionId = GetUniqueFactionId(faction);
            
            if (!_memoryCache.TryGetValue(factionId, out var memory))
            {
                // Do not block gameplay with lazy disk reads; create a runtime memory object when missing.
                memory = new FactionLeaderMemory(faction);
                _memoryCache[factionId] = memory;
            }
            
            return memory;
        }

        /// <summary>/// save指定factionleader的memory
 ///</summary>
        public void SaveMemory(Faction faction)
        {
            if (faction == null) return;
            if (!TryValidatePersistenceContext(nameof(SaveMemory))) return;

            var factionId = GetUniqueFactionId(faction);
            
            if (!_memoryCache.TryGetValue(factionId, out var memory))
            {
                Log.Warning($"[RimChat] Attempted to save memory for {faction.Name}, but no memory found in cache");
                return;
            }

            // 刷新信息
            memory.RefreshLeaderInfo();
            NormalizeMemoryData(memory);
            memory.LastSavedTimestamp = DateTime.UtcNow.Ticks;
            
            // Save到file
            SaveMemoryToFile(faction, memory);
            
            // 调试log
            Log.Message($"[RimChat] Saved memory for {faction.Name}: {memory.DialogueHistory.Count} dialogues, {memory.FactionMemories.Count} factions, {memory.SignificantEvents.Count} events");
        }

        /// <summary>/// save所有factionleader的memory
 ///</summary>
        public void SaveAllMemories()
        {
            if (!TryValidatePersistenceContext(nameof(SaveAllMemories))) return;
            EnsureCacheLoaded();

            foreach (var kvp in _memoryCache)
            {
                var faction = Find.FactionManager.AllFactions.FirstOrDefault(f => GetUniqueFactionId(f) == kvp.Key);
                if (faction != null && !faction.defeated)
                {
                    SaveMemory(faction);
                }
            }

            Log.Message("[RimChat] All faction leader memories saved");
        }

        /// <summary>/// 从dialogue更新memory (但不save到file, 只更新内存)
 ///</summary>
        public void UpdateFromDialogue(Faction faction, List<DialogueMessageData> messages)
        {
            if (faction == null || messages == null || messages.Count == 0)
            {
                return;
            }

            var memory = GetMemory(faction);
            if (memory != null)
            {
                memory.UpdateFromDialogue(messages);
                memory.UpdateRelationSnapshot(faction);
                
                // 只添加新的dialoguerecord (检查whether已presence)
                // 通过比较 GameTick 来判断whether是新message
                int lastSavedTick = memory.DialogueHistory.Count > 0 
                    ? memory.DialogueHistory[memory.DialogueHistory.Count - 1].GameTick 
                    : -1;
                
                foreach (var msg in messages)
                {
                    int msgTick = msg.GetGameTick();
                    // 只save之前未save过的message
                    if (msgTick > lastSavedTick)
                    {
                        memory.DialogueHistory.Add(new DialogueRecord
                        {
                            IsPlayer = msg.isPlayer,
                            Message = msg.message,
                            GameTick = msgTick
                        });
                    }
                }
                
                // 限制dialoguerecord数量, 避免file过大
                if (memory.DialogueHistory.Count > 200)
                {
                    memory.DialogueHistory.RemoveRange(0, memory.DialogueHistory.Count - 200);
                }
                
                // 注意: 这里不save到file, 只在存档save时统一save
            }
        }

        /// <summary>/// record重要event (只更新内存)
 ///</summary>
        public void RecordSignificantEvent(Faction faction, SignificantEventType eventType, Faction involvedFaction, string description)
        {
            var memory = GetMemory(faction);
            if (memory != null)
            {
                memory.AddSignificantEvent(eventType, involvedFaction, description);
                // 注意: 这里不save到file, 只在存档save时统一save
            }
        }

        public void AddRpgDepartSummary(Faction faction, CrossChannelSummaryRecord record, int maxEntries)
        {
            UpsertSummaryInternal(faction, record, maxEntries, useRpgPool: true);
        }

        public void AddDiplomacySessionSummary(Faction faction, CrossChannelSummaryRecord record, int maxEntries)
        {
            UpsertSummaryInternal(faction, record, maxEntries, useRpgPool: false);
        }

        public void UpsertRpgDepartSummary(Faction faction, CrossChannelSummaryRecord record, int maxEntries)
        {
            UpsertSummaryInternal(faction, record, maxEntries, useRpgPool: true);
        }

        public void UpsertDiplomacySessionSummary(Faction faction, CrossChannelSummaryRecord record, int maxEntries)
        {
            UpsertSummaryInternal(faction, record, maxEntries, useRpgPool: false);
        }

        private void UpsertSummaryInternal(Faction faction, CrossChannelSummaryRecord record, int maxEntries, bool useRpgPool)
        {
            if (faction == null || record == null || string.IsNullOrWhiteSpace(record.SummaryText))
            {
                return;
            }

            if (!TryBuildSanitizedSummaryRecord(record, out CrossChannelSummaryRecord sanitizedRecord, out string reasonTag))
            {
                Log.Warning($"[RimChat] summary_drop_invalid source={record.Source} contentHash={record.ContentHash ?? string.Empty} factionId={record.FactionId ?? string.Empty} reason={reasonTag ?? "invalid_summary"}");
                TryQueueSummaryRepair(faction, record, maxEntries, useRpgPool, reasonTag ?? "invalid_summary");
                return;
            }

            lock (_summarySyncRoot)
            {
                var memory = GetMemory(faction);
                if (memory == null)
                {
                    return;
                }

                if (useRpgPool)
                {
                    memory.UpsertRpgDepartSummary(sanitizedRecord, maxEntries);
                }
                else
                {
                    memory.UpsertDiplomacySessionSummary(sanitizedRecord, maxEntries);
                }
            }
        }

        private static List<Faction> GetActiveFactions()
        {
            return Find.FactionManager.AllFactions
                .Where(f => f != null && !f.IsPlayer && !f.defeated && !f.def.hidden)
                .ToList();
        }

        private static bool HasMarkerEvent(FactionLeaderMemory memory, string prefix)
        {
            if (memory?.SignificantEvents == null || memory.SignificantEvents.Count == 0)
            {
                return false;
            }

            return memory.SignificantEvents.Any(evt =>
                evt != null &&
                !string.IsNullOrWhiteSpace(evt.Description) &&
                evt.Description.StartsWith(prefix, StringComparison.Ordinal));
        }

        private static void TrimSignificantEvents(FactionLeaderMemory memory)
        {
            if (memory?.SignificantEvents == null || memory.SignificantEvents.Count <= MaxSignificantEvents)
            {
                return;
            }

            memory.SignificantEvents = memory.SignificantEvents
                .OrderByDescending(evt => evt?.OccurredTick ?? 0)
                .Take(MaxSignificantEvents)
                .ToList();
        }

        private bool EnsureBaselineSnapshot(Faction faction, FactionLeaderMemory memory, string sourceTag)
        {
            if (faction == null || memory == null)
            {
                return false;
            }

            bool changed = false;
            int currentTick = Find.TickManager?.TicksGame ?? 0;

            string marker = $"{InitSnapshotPrefix}:{sourceTag}";
            if (HasMarkerEvent(memory, marker))
            {
                return changed;
            }

            if (memory.SignificantEvents == null)
            {
                memory.SignificantEvents = new List<SignificantEventMemory>();
            }

            string relationKind = faction.RelationKindWith(Faction.OfPlayer).ToString();
            memory.SignificantEvents.Add(new SignificantEventMemory
            {
                EventType = SignificantEventType.GoodwillChanged,
                InvolvedFactionId = Faction.OfPlayer?.GetUniqueLoadID() ?? "PlayerFaction",
                InvolvedFactionName = Faction.OfPlayer?.Name ?? "PlayerFaction",
                Description = $"{marker} goodwill={faction.PlayerGoodwill}, relation={relationKind}.",
                OccurredTick = currentTick,
                Timestamp = DateTime.UtcNow.Ticks
            });

            TrimSignificantEvents(memory);
            memory.LastUpdatedTick = currentTick;
            return true;
        }

        private int RefreshBaselineSnapshotsAfterLoad()
        {
            int touchedFactions = 0;
            foreach (Faction faction in GetActiveFactions())
            {
                FactionLeaderMemory memory = GetMemory(faction);
                if (memory == null)
                {
                    continue;
                }

                if (EnsureBaselineSnapshot(faction, memory, "loaded_game"))
                {
                    SaveMemory(faction);
                    touchedFactions++;
                }
            }

            return touchedFactions;
        }

        /// <summary>/// load缓存
 ///</summary>
        private void EnsureCacheLoaded()
        {
            if (_cacheLoaded) return;

            try
            {
                EnsureDataDirectoryExists();
                TryMigrateLegacyMemories(CurrentSaveKey);
                LoadAllMemoriesFromFiles();
                _cacheLoaded = true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to load memory cache: {ex.Message}");
            }
        }

        /// <summary>/// 从fileload所有memory
 ///</summary>
        private void LoadAllMemoriesFromFiles()
        {
            string sourceDir = ResolveMemorySourceDirectory();
            if (!Directory.Exists(sourceDir)) return;

            var files = Directory.GetFiles(sourceDir, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var memory = ParseJsonToMemory(json);
                    
                    if (memory != null && !string.IsNullOrEmpty(memory.OwnerFactionId))
                    {
                        NormalizeMemoryData(memory);
                        _memoryCache[memory.OwnerFactionId] = memory;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimChat] Failed to load memory file {file}: {ex.Message}");
                }
            }

            Log.Message($"[RimChat] Loaded {_memoryCache.Count} faction leader memories from {files.Length} files");
        }

        private string ResolveMemorySourceDirectory()
        {
            return CurrentSaveDataPath;
        }

        private static bool DirectoryHasJsonFiles(string path)
        {
            return Directory.Exists(path) && Directory.GetFiles(path, "*.json").Length > 0;
        }

        /// <summary>/// 从fileload指定faction的memory
 ///</summary>
        private FactionLeaderMemory LoadMemoryFromFile(Faction faction)
        {
            var fileName = GetMemoryFileName(faction);
            var filePath = ResolveMemoryFilePath(fileName);

            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var memory = ParseJsonToMemory(json);
                
                if (memory != null)
                {
                    NormalizeMemoryData(memory);
                    Log.Message($"[RimChat] Loaded memory for {faction.Name} from {fileName}");
                    return memory;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to load memory for {faction.Name}: {ex.Message}");
            }

            return null;
        }

        private string ResolveMemoryFilePath(string fileName)
        {
            return Path.Combine(CurrentSaveDataPath, fileName);
        }

        /// <summary>/// savememory到file
 ///</summary>
        private void SaveMemoryToFile(Faction faction, FactionLeaderMemory memory)
        {
            try
            {
                EnsureDataDirectoryExists();

                var fileName = GetMemoryFileName(faction);
                var filePath = Path.Combine(CurrentSaveDataPath, fileName);

                var json = ConvertMemoryToJson(memory);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to save memory for {faction.Name}: {ex.Message}");
            }
        }

        /// <summary>/// getmemoryfile名
 ///</summary>
        private string GetMemoryFileName(Faction faction)
        {
            // 使用faction ID 和name生成file名
            var safeName = faction.Name.SanitizeFileName();
            return $"{safeName}_{faction.loadID}.json";
        }

        /// <summary>/// get唯一faction ID
 ///</summary>
        private string GetUniqueFactionId(Faction faction)
        {
            if (faction.def != null && !string.IsNullOrEmpty(faction.def.defName))
            {
                return $"{faction.def.defName}_{faction.loadID}";
            }
            return $"custom_{faction.loadID}";
        }

        /// <summary>/// 将memory对象转换为 JSON 字符串
 ///</summary>
        private string ConvertMemoryToJson(FactionLeaderMemory memory)
        {
            return LeaderMemoryJsonCodec.ConvertMemoryToJson(memory);
        }

        /// <summary>/// 从 JSON 字符串解析memory对象
 ///</summary>
        private FactionLeaderMemory ParseJsonToMemory(string json)
        {
            return LeaderMemoryJsonCodec.ParseJsonToMemory(json);
        }

        /// <summary>/// 清理无效存档的memory数据 (暂时禁用)
 ///</summary>
        public void CleanupInvalidSaveData()
        {
            // 暂时禁用清理功能, pending正确的 API 实现
            // Var baseDir = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimChat", "save_data");
            // If (!Directory.Exists(baseDir)) return;
            // ...清理逻辑
        }

        /// <summary>/// 新游戏启动时的initialize
 ///</summary>
        public void OnNewGame()
        {
            _memoryCache.Clear();
            _cacheLoaded = false;
            _resolvedSaveKey = string.Empty;
            EnsureDataDirectoryExists();
            
            // 为新游戏的所有faction创建初始memory
            var allFactions = GetActiveFactions();

            foreach (var faction in allFactions)
            {
                var memory = GetMemory(faction);
                EnsureBaselineSnapshot(faction, memory, "new_game");
            }

            Log.Message("[RimChat] Initialized faction leader memories for new game");
        }

        /// <summary>/// 游戏load时的initialize
 ///</summary>
        public void OnLoadedGame()
        {
            _memoryCache.Clear();
            _cacheLoaded = false;
            _resolvedSaveKey = string.Empty;
            EnsureDataDirectoryExists();
            EnsureCacheLoaded();
            Log.Message("[RimChat] Initialized faction leader memory manager for saved game");
        }

        /// <summary>/// 游戏loadcompleted后, 从fileloadmemory数据
 ///</summary>
        public void OnAfterGameLoad()
        {
            OnAfterGameLoad(null);
        }

        /// <summary>/// 游戏loadcompleted后, 从fileloadmemory数据并回填存档已有session数据
 ///</summary>
        public void OnAfterGameLoad(IEnumerable<FactionDialogueSession> loadedSessions)
        {
            _memoryCache.Clear();
            _cacheLoaded = false;
            _resolvedSaveKey = string.Empty;
            EnsureDataDirectoryExists();
            EnsureCacheLoaded();

            int touched = RefreshBaselineSnapshotsAfterLoad();
            Log.Message($"[RimChat] Loaded {_memoryCache.Count} faction leader memories from save, refreshed {touched} factions");
        }

        /// <summary>/// 游戏save前调用, save所有memory数据
 ///</summary>
        public void OnBeforeGameSave()
        {
            if (!TryValidatePersistenceContext(nameof(OnBeforeGameSave)))
            {
                return;
            }

            SaveAllMemories();
        }
    }
}

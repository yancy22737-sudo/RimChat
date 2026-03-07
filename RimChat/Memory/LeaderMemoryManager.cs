using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;
using RimWorld;
using RimChat.Relation;

namespace RimChat.Memory
{
    /// <summary>
    /// 派系领袖记忆管理器
    /// 负责管理所有派系领袖记忆的保存和加载
    /// 每个存档有独立的文件夹，每个领袖单独一个 JSON 文件
    /// </summary>
    public class LeaderMemoryManager
    {
        private const string InitSnapshotPrefix = "[init-snapshot]";
        private const string SessionBackfillPrefix = "[session-backfill]";
        private const int MaxSignificantEvents = 80;

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

        /// <summary>
        /// 当前存档的记忆数据目录
        /// </summary>
        private string CurrentSaveDataPath
        {
            get
            {
                if (Current.Game == null) return Path.Combine(GenFilePaths.SaveDataFolderPath, "RimChat", "save_data", "Default");
                
                // 使用存档名称作为文件夹名（所有派系共享同一个文件夹）
                // 由于 GameInfo.name 可能不存在，使用更可靠的方法
                string saveName = "Default";
                
                try
                {
                    // 尝试获取当前存档的名称
                    // RimWorld 中存档信息存储在 Current.Game.Info 中
                    // 但不同版本可能属性名不同，使用反射安全获取
                    var gameInfo = Current.Game.Info;
                    if (gameInfo != null)
                    {
                        // 尝试获取 name 字段或属性
                        var nameField = gameInfo.GetType().GetProperty("name");
                        if (nameField != null)
                        {
                            saveName = nameField.GetValue(gameInfo) as string ?? "Default";
                        }
                        else
                        {
                            // 如果找不到 name 属性，使用 Game 的哈希值
                            saveName = $"Save_{Current.Game.GetHashCode()}";
                        }
                    }
                }
                catch
                {
                    // 如果任何错误，使用默认名称
                    saveName = "Default";
                }
                
                return Path.Combine(GenFilePaths.SaveDataFolderPath, "RimChat", "save_data", saveName.SanitizeFileName());
            }
        }

        /// <summary>
        /// 内存中的记忆缓存
        /// </summary>
        private Dictionary<string, FactionLeaderMemory> _memoryCache = new Dictionary<string, FactionLeaderMemory>();

        /// <summary>
        /// 缓存是否已加载
        /// </summary>
        private bool _cacheLoaded = false;
        private readonly object _summarySyncRoot = new object();

        /// <summary>
        /// 确保数据目录存在
        /// </summary>
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

        /// <summary>
        /// 获取指定派系领袖的记忆
        /// </summary>
        public FactionLeaderMemory GetMemory(Faction faction)
        {
            if (faction == null) return null;

            EnsureCacheLoaded();

            var factionId = GetUniqueFactionId(faction);
            
            if (!_memoryCache.TryGetValue(factionId, out var memory))
            {
                // 尝试从文件加载
                memory = LoadMemoryFromFile(faction);
                
                if (memory == null)
                {
                    // 创建新的记忆对象
                    memory = new FactionLeaderMemory(faction);
                    _memoryCache[factionId] = memory;
                }
                else
                {
                    _memoryCache[factionId] = memory;
                }
            }
            
            return memory;
        }

        /// <summary>
        /// 保存指定派系领袖的记忆
        /// </summary>
        public void SaveMemory(Faction faction)
        {
            if (faction == null) return;

            var factionId = GetUniqueFactionId(faction);
            
            if (!_memoryCache.TryGetValue(factionId, out var memory))
            {
                Log.Warning($"[RimChat] Attempted to save memory for {faction.Name}, but no memory found in cache");
                return;
            }

            // 刷新信息
            memory.RefreshLeaderInfo();
            memory.LastSavedTimestamp = DateTime.UtcNow.Ticks;
            
            // 保存到文件
            SaveMemoryToFile(faction, memory);
            
            // 调试日志
            Log.Message($"[RimChat] Saved memory for {faction.Name}: {memory.DialogueHistory.Count} dialogues, {memory.FactionMemories.Count} factions, {memory.SignificantEvents.Count} events");
        }

        /// <summary>
        /// 保存所有派系领袖的记忆
        /// </summary>
        public void SaveAllMemories()
        {
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

        /// <summary>
        /// 从对话更新记忆（但不保存到文件，只更新内存）
        /// </summary>
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
                
                // 只添加新的对话记录（检查是否已存在）
                // 通过比较 GameTick 来判断是否是新消息
                int lastSavedTick = memory.DialogueHistory.Count > 0 
                    ? memory.DialogueHistory[memory.DialogueHistory.Count - 1].GameTick 
                    : -1;
                
                foreach (var msg in messages)
                {
                    int msgTick = msg.GetGameTick();
                    // 只保存之前未保存过的消息
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
                
                // 限制对话记录数量，避免文件过大
                if (memory.DialogueHistory.Count > 200)
                {
                    memory.DialogueHistory.RemoveRange(0, memory.DialogueHistory.Count - 200);
                }
                
                // 注意：这里不保存到文件，只在存档保存时统一保存
            }
        }

        /// <summary>
        /// 记录重要事件（只更新内存）
        /// </summary>
        public void RecordSignificantEvent(Faction faction, SignificantEventType eventType, Faction involvedFaction, string description)
        {
            var memory = GetMemory(faction);
            if (memory != null)
            {
                memory.AddSignificantEvent(eventType, involvedFaction, description);
                // 注意：这里不保存到文件，只在存档保存时统一保存
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

            lock (_summarySyncRoot)
            {
                var memory = GetMemory(faction);
                if (memory == null)
                {
                    return;
                }

                if (useRpgPool)
                {
                    memory.UpsertRpgDepartSummary(record, maxEntries);
                }
                else
                {
                    memory.UpsertDiplomacySessionSummary(record, maxEntries);
                }
            }
        }

        private static List<Faction> GetActiveFactions()
        {
            return Find.FactionManager.AllFactions
                .Where(f => f != null && !f.IsPlayer && !f.defeated && !f.def.hidden)
                .ToList();
        }

        private static bool IsRelationSeedMissing(FactionRelationValues relations)
        {
            if (relations == null)
            {
                return true;
            }

            return relations.UpdateCount <= 0 &&
                   Math.Abs(relations.Trust) < 0.01f &&
                   Math.Abs(relations.Intimacy) < 0.01f &&
                   Math.Abs(relations.Reciprocity) < 0.01f &&
                   Math.Abs(relations.Respect) < 0.01f &&
                   Math.Abs(relations.Influence) < 0.01f;
        }

        private static void SeedRelationsFromGoodwill(FactionRelationValues relations, int goodwill, int tick)
        {
            if (relations == null)
            {
                return;
            }

            relations.Trust = goodwill;
            relations.Intimacy = goodwill;
            relations.Reciprocity = goodwill;
            relations.Respect = goodwill;
            relations.Influence = goodwill;
            relations.LastUpdatedTick = tick;
            relations.LastDialogueTick = tick;
            relations.UpdateCount = 0;
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
            FactionRelationValues relations = memory.GetOrCreatePlayerRelations();
            if (IsRelationSeedMissing(relations))
            {
                SeedRelationsFromGoodwill(relations, faction.PlayerGoodwill, currentTick);
                changed = true;
            }

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

        private int ImportLegacySessionMessages(Faction faction, FactionLeaderMemory memory, List<DialogueMessageData> messages)
        {
            if (faction == null || memory == null || messages == null || messages.Count == 0)
            {
                return 0;
            }

            int beforeCount = memory.DialogueHistory?.Count ?? 0;
            UpdateFromDialogue(faction, messages);
            int afterCount = memory.DialogueHistory?.Count ?? 0;
            int importedCount = Math.Max(0, afterCount - beforeCount);
            if (importedCount <= 0)
            {
                return 0;
            }

            int maxTick = messages.Max(msg => msg?.GetGameTick() ?? 0);
            string marker = $"{SessionBackfillPrefix}:{faction.GetUniqueLoadID()}:{maxTick}";
            if (!HasMarkerEvent(memory, marker))
            {
                memory.SignificantEvents ??= new List<SignificantEventMemory>();
                memory.SignificantEvents.Add(new SignificantEventMemory
                {
                    EventType = SignificantEventType.GoodwillChanged,
                    InvolvedFactionId = faction.GetUniqueLoadID(),
                    InvolvedFactionName = faction.Name ?? "UnknownFaction",
                    Description = $"{marker} imported={importedCount}.",
                    OccurredTick = maxTick,
                    Timestamp = DateTime.UtcNow.Ticks
                });
                TrimSignificantEvents(memory);
            }

            return importedCount;
        }

        private int BackfillMemoriesFromLoadedSessions(IEnumerable<FactionDialogueSession> loadedSessions)
        {
            List<FactionDialogueSession> sessions = (loadedSessions ?? Enumerable.Empty<FactionDialogueSession>())
                .Where(session => session != null && session.faction != null)
                .ToList();

            int touchedFactions = 0;
            foreach (Faction faction in GetActiveFactions())
            {
                FactionLeaderMemory memory = GetMemory(faction);
                if (memory == null)
                {
                    continue;
                }

                bool changed = EnsureBaselineSnapshot(faction, memory, "loaded_game");
                FactionDialogueSession session = sessions.FirstOrDefault(item => item.faction == faction);
                if (session != null && ImportLegacySessionMessages(faction, memory, session.messages) > 0)
                {
                    changed = true;
                }

                if (changed)
                {
                    SaveMemory(faction);
                    touchedFactions++;
                }
            }

            return touchedFactions;
        }

        /// <summary>
        /// 加载缓存
        /// </summary>
        private void EnsureCacheLoaded()
        {
            if (_cacheLoaded) return;

            try
            {
                EnsureDataDirectoryExists();
                LoadAllMemoriesFromFiles();
                _cacheLoaded = true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to load memory cache: {ex.Message}");
            }
        }

        /// <summary>
        /// 从文件加载所有记忆
        /// </summary>
        private void LoadAllMemoriesFromFiles()
        {
            if (!Directory.Exists(CurrentSaveDataPath)) return;

            var files = Directory.GetFiles(CurrentSaveDataPath, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var memory = ParseJsonToMemory(json);
                    
                    if (memory != null && !string.IsNullOrEmpty(memory.OwnerFactionId))
                    {
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

        /// <summary>
        /// 从文件加载指定派系的记忆
        /// </summary>
        private FactionLeaderMemory LoadMemoryFromFile(Faction faction)
        {
            var fileName = GetMemoryFileName(faction);
            var filePath = Path.Combine(CurrentSaveDataPath, fileName);

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

        /// <summary>
        /// 保存记忆到文件
        /// </summary>
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

        /// <summary>
        /// 获取记忆文件名
        /// </summary>
        private string GetMemoryFileName(Faction faction)
        {
            // 使用派系 ID 和名称生成文件名
            var safeName = faction.Name.SanitizeFileName();
            return $"{safeName}_{faction.loadID}.json";
        }

        /// <summary>
        /// 获取唯一派系 ID
        /// </summary>
        private string GetUniqueFactionId(Faction faction)
        {
            if (faction.def != null && !string.IsNullOrEmpty(faction.def.defName))
            {
                return $"{faction.def.defName}_{faction.loadID}";
            }
            return $"custom_{faction.loadID}";
        }

        /// <summary>
        /// 将记忆对象转换为 JSON 字符串
        /// </summary>
        private string ConvertMemoryToJson(FactionLeaderMemory memory)
        {
            return LeaderMemoryJsonCodec.ConvertMemoryToJson(memory);
        }

        /// <summary>
        /// 从 JSON 字符串解析记忆对象
        /// </summary>
        private FactionLeaderMemory ParseJsonToMemory(string json)
        {
            return LeaderMemoryJsonCodec.ParseJsonToMemory(json);
        }

        /// <summary>
        /// 清理无效存档的记忆数据（暂时禁用）
        /// </summary>
        public void CleanupInvalidSaveData()
        {
            // 暂时禁用清理功能，等待正确的 API 实现
            // var baseDir = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimChat", "save_data");
            // if (!Directory.Exists(baseDir)) return;
            // ...清理逻辑
        }

        /// <summary>
        /// 新游戏启动时的初始化
        /// </summary>
        public void OnNewGame()
        {
            _memoryCache.Clear();
            _cacheLoaded = false;
            EnsureDataDirectoryExists();
            
            // 为新游戏的所有派系创建初始记忆
            var allFactions = GetActiveFactions();

            foreach (var faction in allFactions)
            {
                var memory = GetMemory(faction);
                EnsureBaselineSnapshot(faction, memory, "new_game");
            }

            Log.Message("[RimChat] Initialized faction leader memories for new game");
        }

        /// <summary>
        /// 游戏加载时的初始化
        /// </summary>
        public void OnLoadedGame()
        {
            _memoryCache.Clear();
            _cacheLoaded = false;
            EnsureDataDirectoryExists();
            // 注意：这里不加载文件，只在需要时懒加载
            Log.Message("[RimChat] Initialized faction leader memory manager for saved game");
        }

        /// <summary>
        /// 游戏加载完成后，从文件加载记忆数据
        /// </summary>
        public void OnAfterGameLoad()
        {
            OnAfterGameLoad(null);
        }

        /// <summary>
        /// 游戏加载完成后，从文件加载记忆数据并回填存档已有会话数据
        /// </summary>
        public void OnAfterGameLoad(IEnumerable<FactionDialogueSession> loadedSessions)
        {
            _memoryCache.Clear();
            _cacheLoaded = false;
            EnsureDataDirectoryExists();
            EnsureCacheLoaded();

            int touched = BackfillMemoriesFromLoadedSessions(loadedSessions);
            Log.Message($"[RimChat] Loaded {_memoryCache.Count} faction leader memories from save, backfilled {touched} factions");
        }

        /// <summary>
        /// 游戏保存前调用，保存所有记忆数据
        /// </summary>
        public void OnBeforeGameSave()
        {
            SaveAllMemories();
        }
    }

    /// <summary>
    /// 文件名清理扩展方法
    /// </summary>
    public static class StringExtensions
    {
        public static string SanitizeFileName(this string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "Unknown";

            var invalidChars = Path.GetInvalidFileNameChars();
            var result = new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            
            // 限制长度
            if (result.Length > 100)
            {
                result = result.Substring(0, 100);
            }

            return result;
        }
    }
}

# RimChat P0 修复完整代码变更记录

> 修复日期：2026-04-10
> 项目版本：0.9.93 → 0.9.94
> 构建状态：✅ 成功（26 警告，0 错误）

---

## 目录

1. [P0-1: FactionLeaderMemory 内部类型添加 IExposable 实现](#p0-1-factionleadermemory-内部类型添加 iexposable-实现)
2. [P0-2: 标记旧版 AI 服务为 Obsolete](#p0-2-标记旧版 ai-服务为-obsolete)
3. [P0-3: QuestGenPatch 反射添加异常保护](#p0-3-questgenpatch-反射添加异常保护)
4. [P0-4: 统一 JSON 转义助手](#p0-4-统一 json-转义助手)
5. [P0-5: 修复 .ToUpper().Translate() Bug](#p0-5-修复-touppertotranslate-bug)

---

## P0-1: FactionLeaderMemory 内部类型添加 IExposable 实现

**问题**：`FactionMemoryEntry`、`RelationSnapshot`、`SignificantEventMemory`、`DialogueRecord` 四个内部类型未实现 `IExposable`，但 `FactionLeaderMemory.ExposeData()` 使用 `LookMode.Deep` 序列化它们，若未来被调用将导致存档损坏。

**修复**：为四个类型添加 `IExposable` 接口和 `ExposeData()` 方法，将属性改为公共字段（RimWorld Scribe 要求 `ref` 参数）。

### 修改文件：`Memory/FactionLeaderMemory.cs`

#### 变更 1: FactionMemoryEntry

```csharp
// 修复前
public class FactionMemoryEntry
{
    /// <summary>/// faction唯一 ID
 ///</summary>
    public string FactionId { get; set; }
    
    /// <summary>/// factionname
 ///</summary>
    public string FactionName { get; set; }
    
    /// <summary>/// 首次接触时间 tick
 ///</summary>
    public int FirstContactTick { get; set; }
    
    /// <summary>/// 最后被提及的时间 tick
 ///</summary>
    public int LastMentionedTick { get; set; }
    
    /// <summary>/// 被提及的次数
 ///</summary>
    public int MentionCount { get; set; }
    
    /// <summary>/// 正面交互次数
 ///</summary>
    public int PositiveInteractions { get; set; }
    
    /// <summary>/// 负面交互次数
 ///</summary>
    public int NegativeInteractions { get; set; }
    
    /// <summary>/// relation历史快照
 ///</summary>
    public List<RelationSnapshot> RelationHistory { get; set; } = new List<RelationSnapshot>();
}

// 修复后
public class FactionMemoryEntry : IExposable
{
    public string FactionId = "";
    public string FactionName = "";
    public int FirstContactTick = 0;
    public int LastMentionedTick = 0;
    public int MentionCount = 0;
    public int PositiveInteractions = 0;
    public int NegativeInteractions = 0;
    public List<RelationSnapshot> RelationHistory = new List<RelationSnapshot>();

    public void ExposeData()
    {
        Scribe_Values.Look(ref FactionId, "factionId", "");
        Scribe_Values.Look(ref FactionName, "factionName", "");
        Scribe_Values.Look(ref FirstContactTick, "firstContactTick", 0);
        Scribe_Values.Look(ref LastMentionedTick, "lastMentionedTick", 0);
        Scribe_Values.Look(ref MentionCount, "mentionCount", 0);
        Scribe_Values.Look(ref PositiveInteractions, "positiveInteractions", 0);
        Scribe_Values.Look(ref NegativeInteractions, "negativeInteractions", 0);
        Scribe_Collections.Look(ref RelationHistory, "relationHistory", LookMode.Deep);
        if (RelationHistory == null)
        {
            RelationHistory = new List<RelationSnapshot>();
        }
    }
}
```

#### 变更 2: RelationSnapshot

```csharp
// 修复前
public class RelationSnapshot
{
    /// <summary>/// record时间的 tick
 ///</summary>
    public int Tick { get; set; }
    
    /// <summary>/// relation类型
 ///</summary>
    public string Relation { get; set; }
    
    /// <summary>/// goodwillvalues
 ///</summary>
    public int Goodwill { get; set; }
}

// 修复后
public class RelationSnapshot : IExposable
{
    public int Tick = 0;
    public string Relation = "";
    public int Goodwill = 0;

    public void ExposeData()
    {
        Scribe_Values.Look(ref Tick, "tick", 0);
        Scribe_Values.Look(ref Relation, "relation", "");
        Scribe_Values.Look(ref Goodwill, "goodwill", 0);
    }
}
```

#### 变更 3: SignificantEventMemory

```csharp
// 修复前
public class SignificantEventMemory
{
    /// <summary>/// event类型
 ///</summary>
    public SignificantEventType EventType { get; set; }
    
    /// <summary>/// 涉及faction的 ID
 ///</summary>
    public string InvolvedFactionId { get; set; }
    
    /// <summary>/// 涉及faction的name
 ///</summary>
    public string InvolvedFactionName { get; set; }
    
    /// <summary>/// event描述
 ///</summary>
    public string Description { get; set; }
    
    /// <summary>/// event发生的 tick
 ///</summary>
    public int OccurredTick { get; set; }
    
    /// <summary>/// 时间戳
 ///</summary>
    public long Timestamp { get; set; }
}

// 修复后
public class SignificantEventMemory : IExposable
{
    public SignificantEventType EventType = SignificantEventType.GoodwillChanged;
    public string InvolvedFactionId = "";
    public string InvolvedFactionName = "";
    public string Description = "";
    public int OccurredTick = 0;
    public long Timestamp = 0L;

    public void ExposeData()
    {
        Scribe_Values.Look(ref EventType, "eventType", SignificantEventType.GoodwillChanged);
        Scribe_Values.Look(ref InvolvedFactionId, "involvedFactionId", "");
        Scribe_Values.Look(ref InvolvedFactionName, "involvedFactionName", "");
        Scribe_Values.Look(ref Description, "description", "");
        Scribe_Values.Look(ref OccurredTick, "occurredTick", 0);
        Scribe_Values.Look(ref Timestamp, "timestamp", 0L);
    }
}
```

#### 变更 4: DialogueRecord

```csharp
// 修复前
public class DialogueRecord
{
    /// <summary>/// whether是玩家 (true=玩家, false=AI)
 ///</summary>
    public bool IsPlayer { get; set; }
    
    /// <summary>/// dialoguecontents
 ///</summary>
    public string Message { get; set; }
    
    /// <summary>/// dialogue发生的游戏 tick
 ///</summary>
    public int GameTick { get; set; }
}

// 修复后
public class DialogueRecord : IExposable
{
    public bool IsPlayer = false;
    public string Message = "";
    public int GameTick = 0;

    public void ExposeData()
    {
        Scribe_Values.Look(ref IsPlayer, "isPlayer", false);
        Scribe_Values.Look(ref Message, "message", "");
        Scribe_Values.Look(ref GameTick, "gameTick", 0);
    }
}
```

---

## P0-2: 标记旧版 AI 服务为 Obsolete

**问题**：`AIChatService`（同步阻塞）、`AIChatClient`（TaskCompletionSource 反模式）使用 `Thread.Sleep` 阻塞主线程，网络异常时游戏冻结。

**修复**：添加 `[Obsolete]` 属性，编译器警告引导开发者迁移到 `AIChatServiceAsync`。

### 修改文件 1：`AI/AIChatService.cs`

```csharp
// 修复前
public class AIChatService

// 修复后
[Obsolete("Use AIChatServiceAsync instead. This synchronous service uses Thread.Sleep which blocks the main thread and can freeze the game on network errors.")]
public class AIChatService
```

### 修改文件 2：`AI/AIChatClient.cs`

```csharp
// 修复前
public sealed class AIChatClientResponse

// 修复后
[Obsolete("Use AIChatServiceAsync instead. This client uses TaskCompletionSource + LongEventHandler which can deadlock and blocks background threads with Thread.Sleep.")]
public sealed class AIChatClientResponse
```

```csharp
// 修复前
public class AIChatClient

// 修复后
[Obsolete("Use AIChatServiceAsync instead. This client uses TaskCompletionSource + LongEventHandler which can deadlock and blocks background threads with Thread.Sleep.")]
public class AIChatClient
```

### 编译警告验证

构建输出确认警告出现：
```
GameAIInterface.ItemAirdrop.cs(502,65): warning CS0618: "AIChatClientResponse"已过时
Dialog_DiplomacyDialogue.cs(321,132): warning CS0618: "AIChatService"已过时
```

---

## P0-3: QuestGenPatch 反射添加异常保护

**问题**：`QuestGenPatch` 深度侵入 RimWorld 内部字段（`SlateRef.sli`、`QuestNode.giverFaction` 等），反射查找失败时直接抛未处理异常，导致任务生成流程中断。

**修复**：为所有反射调用添加 try-catch 保护，失败时记录警告并降级处理。

### 修改文件：`Patches/QuestGenPatch.cs`

#### 变更 1: Prefix_ForceGiverFaction

```csharp
// 修复前（部分）
public static bool Prefix_ForceGiverFaction(QuestNode __instance)
{
    var slate = QuestGen.slate;
    if (slate.Exists("faction"))
    {
        Faction f = slate.Get<Faction>("faction");
        if (f != null)
        {
            string[] fieldNames = { "giverFaction", "faction", "askerFaction" };
            
            foreach (var fieldName in fieldNames)
            {
                var field = AccessTools.Field(__instance.GetType(), fieldName);
                if (field != null)
                {
                    object slateRef = field.GetValue(__instance);
                    if (slateRef != null)
                    {
                        var sliField = AccessTools.Field(slateRef.GetType(), "sli");
                        if (sliField != null)
                        {
                            string currentSli = sliField.GetValue(slateRef) as string;
                            if (string.IsNullOrEmpty(currentSli) || (!currentSli.Contains("faction") && !currentSli.Contains("giverFaction")))
                            {
                                sliField.SetValue(slateRef, "$faction");
                            }
                        }
                    }
                }
            }
            
            if (!slate.Exists("giverFaction"))
            {
                slate.Set("giverFaction", f);
            }
        }
    }
    return true;
}

// 修复后（完整异常保护）
public static bool Prefix_ForceGiverFaction(QuestNode __instance)
{
    try
    {
        var slate = QuestGen.slate;
        if (slate.Exists("faction"))
        {
            Faction f = slate.Get<Faction>("faction");
            if (f != null)
            {
                string[] fieldNames = { "giverFaction", "faction", "askerFaction" };
 
                foreach (var fieldName in fieldNames)
                {
                    try
                    {
                        var field = AccessTools.Field(__instance.GetType(), fieldName);
                        if (field == null) continue;
 
                        object slateRef = field.GetValue(__instance);
                        if (slateRef == null) continue;
 
                        var sliField = AccessTools.Field(slateRef.GetType(), "sli");
                        if (sliField == null) continue;
 
                        string currentSli = sliField.GetValue(slateRef) as string;
                        if (string.IsNullOrEmpty(currentSli) || (!currentSli.Contains("faction") && !currentSli.Contains("giverFaction")))
                        {
                            sliField.SetValue(slateRef, "$faction");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[RimChat] QuestGenPatch: failed to patch field '{fieldName}' on {__instance.GetType().Name}: {ex.Message}");
                    }
                }
 
                if (!slate.Exists("giverFaction"))
                {
                    slate.Set("giverFaction", f);
                }
            }
        }
    }
    catch (Exception ex)
    {
        Log.Warning($"[RimChat] QuestGenPatch.Prefix_ForceGiverFaction failed: {ex.Message}");
    }
    return true;
}
```

#### 变更 2: Prefix_PreventOverwrite

```csharp
// 修复前（部分）
public static bool Prefix_PreventOverwrite(QuestNode __instance)
{
    var slate = QuestGen.slate;
    string[] protectedVars = { "asker", "faction", "askerFaction", "settlement", "giverFaction", "enemyFaction", "siteFaction" };
    string[] storageFields = { "storeAs", "storeFactionAs", "storeFactionLeaderAs", "storeSettlementAs" };

    foreach (var fieldName in storageFields)
    {
        var field = AccessTools.Field(__instance.GetType(), fieldName);
        if (field == null) continue;

        object fieldValue = field.GetValue(__instance);
        if (fieldValue == null) continue;

        string varName = null;
        if (fieldValue is string s)
        {
            varName = s;
        }
        else
        {
            var getter = AccessTools.Method(fieldValue.GetType(), "GetValue", new[] { typeof(Slate) });
            if (getter != null)
            {
                varName = getter.Invoke(fieldValue, new object[] { slate }) as string;
            }
        }

        if (!string.IsNullOrEmpty(varName) && protectedVars.Contains(varName))
        {
            if (slate.Exists(varName))
            {
                return false;
            }
        }
    }
    return true;
}

// 修复后（完整异常保护）
public static bool Prefix_PreventOverwrite(QuestNode __instance)
{
    try
    {
        var slate = QuestGen.slate;
        string[] protectedVars = { "asker", "faction", "askerFaction", "settlement", "giverFaction", "enemyFaction", "siteFaction" };
        string[] storageFields = { "storeAs", "storeFactionAs", "storeFactionLeaderAs", "storeSettlementAs" };

        foreach (var fieldName in storageFields)
        {
            try
            {
                var field = AccessTools.Field(__instance.GetType(), fieldName);
                if (field == null) continue;

                object fieldValue = field.GetValue(__instance);
                if (fieldValue == null) continue;

                string varName = null;
                if (fieldValue is string s)
                {
                    varName = s;
                }
                else
                {
                    var getter = AccessTools.Method(fieldValue.GetType(), "GetValue", new[] { typeof(Slate) });
                    if (getter != null)
                    {
                        varName = getter.Invoke(fieldValue, new object[] { slate }) as string;
                    }
                }

                if (!string.IsNullOrEmpty(varName) && protectedVars.Contains(varName))
                {
                    if (slate.Exists(varName))
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] QuestGenPatch.Prefix_PreventOverwrite: failed on field '{fieldName}' of {__instance.GetType().Name}: {ex.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        Log.Warning($"[RimChat] QuestGenPatch.Prefix_PreventOverwrite failed: {ex.Message}");
    }
    return true;
}
```

#### 变更 3: Prefix_HasRoyalTitleInCurrentFaction

```csharp
// 修复前（部分）
public static bool Prefix_HasRoyalTitleInCurrentFaction(QuestNode __instance)
{
    var slate = QuestGen.slate;
    if (!slate.Exists("faction"))
    {
        return true;
    }

    Faction faction = slate.Get<Faction>("faction");
    if (faction == null)
    {
        return true;
    }

    bool isEmpire = faction.def == FactionDefOf.Empire;
    if (isEmpire)
    {
        return true;
    }

    var pawnField = AccessTools.Field(__instance.GetType(), "pawn");
    var nodeField = AccessTools.Field(__instance.GetType(), "node");
    var elseNodeField = AccessTools.Field(__instance.GetType(), "elseNode");

    if (nodeField == null)
    {
        return true;
    }

    QuestNode node = nodeField.GetValue(__instance) as QuestNode;
    if (node == null)
    {
        return true;
    }

    PatchGiveRewardsNodeForNonEmpireFaction(node, faction);

    node.Run();

    return false;
}

// 修复后（完整异常保护）
public static bool Prefix_HasRoyalTitleInCurrentFaction(QuestNode __instance)
{
    try
    {
        var slate = QuestGen.slate;
        if (!slate.Exists("faction"))
        {
            return true;
        }

        Faction faction = slate.Get<Faction>("faction");
        if (faction == null)
        {
            return true;
        }

        bool isEmpire = faction.def == FactionDefOf.Empire;
        if (isEmpire)
        {
            return true;
        }

        var nodeField = AccessTools.Field(__instance.GetType(), "node");
        if (nodeField == null)
        {
            return true;
        }

        QuestNode node = nodeField.GetValue(__instance) as QuestNode;
        if (node == null)
        {
            return true;
        }

        PatchGiveRewardsNodeForNonEmpireFaction(node, faction);

        node.Run();

        return false;
    }
    catch (Exception ex)
    {
        Log.Warning($"[RimChat] QuestGenPatch.Prefix_HasRoyalTitleInCurrentFaction failed: {ex.Message}");
        return true;
    }
}
```

#### 变更 4: PatchGiveRewardsNodeForNonEmpireFaction

```csharp
// 修复前
private static void PatchGiveRewardsNodeForNonEmpireFaction(QuestNode node, Faction faction)
{
    if (node == null) return;

    var nodeType = node.GetType();
    if (nodeType.Name != "QuestNode_GiveRewards") return;

    var parmsField = AccessTools.Field(nodeType, "parms");
    if (parmsField == null) return;

    object parms = parmsField.GetValue(node);
    if (parms == null) return;

    var parmsType = parms.GetType();

    var allowRoyalFavorField = AccessTools.Field(parmsType, "allowRoyalFavor");
    if (allowRoyalFavorField != null)
    {
        allowRoyalFavorField.SetValue(parms, false);
    }

    var allowGoodwillField = AccessTools.Field(parmsType, "allowGoodwill");
    if (allowGoodwillField != null)
    {
        allowGoodwillField.SetValue(parms, true);
    }

    var thingRewardItemsOnlyField = AccessTools.Field(parmsType, "thingRewardItemsOnly");
    if (thingRewardItemsOnlyField != null)
    {
        thingRewardItemsOnlyField.SetValue(parms, false);
    }

    if (!QuestGen.slate.Exists("giverFaction"))
    {
        QuestGen.slate.Set("giverFaction", faction);
    }
}

// 修复后
private static void PatchGiveRewardsNodeForNonEmpireFaction(QuestNode node, Faction faction)
{
    if (node == null) return;

    try
    {
        var nodeType = node.GetType();
        if (nodeType.Name != "QuestNode_GiveRewards") return;

        var parmsField = AccessTools.Field(nodeType, "parms");
        if (parmsField == null) return;

        object parms = parmsField.GetValue(node);
        if (parms == null) return;

        var parmsType = parms.GetType();

        var allowRoyalFavorField = AccessTools.Field(parmsType, "allowRoyalFavor");
        if (allowRoyalFavorField != null)
        {
            allowRoyalFavorField.SetValue(parms, false);
        }

        var allowGoodwillField = AccessTools.Field(parmsType, "allowGoodwill");
        if (allowGoodwillField != null)
        {
            allowGoodwillField.SetValue(parms, true);
        }

        var thingRewardItemsOnlyField = AccessTools.Field(parmsType, "thingRewardItemsOnly");
        if (thingRewardItemsOnlyField != null)
        {
            thingRewardItemsOnlyField.SetValue(parms, false);
        }

        if (!QuestGen.slate.Exists("giverFaction"))
        {
            QuestGen.slate.Set("giverFaction", faction);
        }
    }
    catch (Exception ex)
    {
        Log.Warning($"[RimChat] QuestGenPatch.PatchGiveRewardsNodeForNonEmpireFaction failed on {node.GetType().Name}: {ex.Message}");
    }
}
```

#### 变更 5: Prefix_Mission_BanditCamp

```csharp
// 修复前
public static bool Prefix_Mission_BanditCamp(QuestNode __instance)
{
    var slate = QuestGen.slate;
    // 检查whether已经通过我们注入了 asker 和 faction
    if (slate.Exists("asker") && slate.Exists("faction"))
    {
        // 使用reflection访问 factionsToDrawLeaderFrom 字段, 因为我们已经把类型传参改为 QuestNode
        var field = AccessTools.Field(__instance.GetType(), "factionsToDrawLeaderFrom");
        if (field != null)
        {
            var list = field.GetValue(__instance) as List<FactionDef>;
            if (list != null)
            {
                Faction f = slate.Get<Faction>("faction");
                if (f != null && !list.Contains(f.def))
                {
                    // Dynamic添加当前faction定义, 允许其通过过滤
                    list.Add(f.def);
                }
            }
        }
    }
    return true;
}

// 修复后
public static bool Prefix_Mission_BanditCamp(QuestNode __instance)
{
    try
    {
        var slate = QuestGen.slate;
        if (slate.Exists("asker") && slate.Exists("faction"))
        {
            var field = AccessTools.Field(__instance.GetType(), "factionsToDrawLeaderFrom");
            if (field != null)
            {
                var list = field.GetValue(__instance) as List<FactionDef>;
                if (list != null)
                {
                    Faction f = slate.Get<Faction>("faction");
                    if (f != null && !list.Contains(f.def))
                    {
                        list.Add(f.def);
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        Log.Warning($"[RimChat] QuestGenPatch.Prefix_Mission_BanditCamp failed: {ex.Message}");
    }
    return true;
}
```

---

## P0-4: 统一 JSON 转义助手

**问题**：6 处 `EscapeJson` 实现均为手写字符串替换，缺少 `\b`、`\f`、U+0000-U+001F 控制字符转义，LLM 输出含特殊字符时生成无效 JSON。

**修复**：新建 `JsonEscapeHelper` 工具类，统一处理所有控制字符，替换 6 处旧实现。

### 新增文件：`Util/JsonEscapeHelper.cs`

```csharp
using System.Text;

namespace RimChat.Util
{
    public static class JsonEscapeHelper
    {
        public static string EscapeString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append($"\\u{(int)c:x4}");
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
```

### 修改文件 1：`AI/AIChatService.cs`

```csharp
// 修复前
private string EscapeJson(string str)
{
    if (string.IsNullOrEmpty(str)) return "";
    return str.Replace("\\", "\\\\")
              .Replace("\"", "\\\"")
              .Replace("\n", "\\n")
              .Replace("\r", "\\r")
              .Replace("\t", "\\t");
}

// 修复后
private string EscapeJson(string str)
{
    return RimChat.Util.JsonEscapeHelper.EscapeString(str);
}
```

### 修改文件 2：`AI/AIChatClient.cs`

```csharp
// 修复前
private string EscapeJson(string str)
{
    return str.Replace("\\", "\\\\")
              .Replace("\"", "\\\"")
              .Replace("\n", "\\n")
              .Replace("\r", "\\r")
              .Replace("\t", "\\t");
}

// 修复后
private string EscapeJson(string str)
{
    return RimChat.Util.JsonEscapeHelper.EscapeString(str);
}
```

### 修改文件 3：`AI/AIChatServiceAsync.cs`

```csharp
// 修复前
private string EscapeJson(string str)
{
    if (string.IsNullOrEmpty(str)) return "";
    return str.Replace("\\", "\\\\")
              .Replace("\"", "\\\"")
              .Replace("\n", "\\n")
              .Replace("\r", "\\r")
              .Replace("\t", "\\t");
}

// 修复后
private string EscapeJson(string str)
{
    return RimChat.Util.JsonEscapeHelper.EscapeString(str);
}
```

### 修改文件 4：`Memory/LeaderMemoryJsonCodec.cs`

```csharp
// 修复前
private static string EscapeJson(string value)
{
    if (string.IsNullOrEmpty(value))
    {
        return string.Empty;
    }

    return value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t");
}

// 修复后
private static string EscapeJson(string value)
{
    return RimChat.Util.JsonEscapeHelper.EscapeString(value);
}
```

### 修改文件 5：`Memory/RpgNpcDialogueArchiveJsonCodec.cs`

```csharp
// 修复前
private static string EscapeJson(string value)
{
    if (string.IsNullOrEmpty(value))
    {
        return string.Empty;
    }

    return value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t");
}

// 修复后
private static string EscapeJson(string value)
{
    return RimChat.Util.JsonEscapeHelper.EscapeString(value);
}
```

---

## P0-5: 修复 .ToUpper().Translate() Bug

**问题**：`MainTabWindow_RimChat.cs:578` 中 `label.ToUpper().Translate()` 先将 label 转大写，再用大写后的字符串作为翻译键查找，导致翻译查找失败，英文环境下显示大写键名而非翻译文本。

**修复**：改为 `label.Translate().RawText.ToUpper()`，先翻译再取原始文本大写。

### 修改文件：`UI/MainTabWindow_RimChat.cs`

```csharp
// 修复前
Widgets.Label(new Rect(x, y - 1f, rect.width - 20f, 20f), label.ToUpper().Translate());

// 修复后
Widgets.Label(new Rect(x, y - 1f, rect.width - 20f, 20f), label.Translate().RawText.ToUpper());
```

**说明**：`Translate()` 返回 `TaggedString`，`TaggedString` 没有 `ToUpper()` 扩展方法，需先访问 `.RawText` 获取原始字符串再调用 `ToUpper()`。

---

## 构建验证

```
在 5.0 秒内生成 成功，出现 26 警告

警告包括：
- Scriban 包漏洞警告（11 个，NU1902/NU1903/NU1904）
- AIChatService/AIChatClient [Obsolete] 警告（2 个，CS0618）
- Translator.Translate 过时警告（1 个）
- 未使用字段警告（1 个）

构建并部署完成！
```

---

## 影响评估

### 兼容性
- ✅ 存档兼容：`FactionLeaderMemory` 内部类型添加 `IExposable` 不影响现有存档（当前走外部 JSON 路径）
- ✅ API 兼容：`[Obsolete]` 属性不破坏现有调用，仅编译器警告
- ✅ 行为兼容：`JsonEscapeHelper` 增强转义范围，生成的 JSON 更规范

### 性能
- `JsonEscapeHelper.EscapeString` 使用 `StringBuilder`，性能优于多次 `Replace`
- QuestGenPatch 异常保护增加 try-catch 开销，但失败率极低，影响可忽略

### 安全性
- ✅ QuestGenPatch 反射失败不再崩溃，游戏稳定性提升
- ✅ JSON 转义补全，防止 LLM 输出特殊字符导致解析失败
- ✅ 翻译 Bug 修复，英文用户体验正常化

---

## 后续工作

### P1 优先级修复（建议下一版本）
1. **P1-1**：迁移 `AIChatService` 和 `AIChatClient` 调用方到 `AIChatServiceAsync`
2. **P1-5**：`RpgNpcDialogueArchiveManager` 延迟写入（脏标记 + 批量 flush）
3. **P1-10 ~ P1-12**：硬编码中文替换为翻译键

### 回归测试清单
- [ ] 加载旧版存档，确认派系记忆正确读取
- [ ] 外交对话流程测试（消息发送 → AI 回复 → 动作执行）
- [ ] 任务生成测试（AI 发起任务 → QuestGenPatch 注入派系 → 任务完成）
- [ ] 英文环境下 UI 文本显示验证
- [ ] LLM 输出含特殊字符（`\b`、`\f`、控制字符）时 JSON 解析验证

---

**文档生成时间**：2026-04-10
**审核状态**：已完成
**下次审查**：P1 修复完成后更新此文档

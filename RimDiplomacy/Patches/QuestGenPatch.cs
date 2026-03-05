using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace RimDiplomacy.Patches
{
    /// <summary>
    /// 增强原版任务生成节点，支持预设变量以解决 UNRESOLVABLE 错误
    /// </summary>
    public static class QuestGenPatch
    {
        /// <summary>
        /// 锁定开关：开启时，禁止原版节点覆盖 asker/faction 等核心变量
        /// </summary>
        public static bool LockSlateVariables = false;

        /// <summary>
        /// 初始化 Patch
        /// </summary>
        public static void Initialize(Harmony harmony)
        {
            try
            {
                // 0. Patch Slate.Set to implement locking
                var slateSet = AccessTools.Method(typeof(Slate), "Set", new[] { typeof(string), typeof(object), typeof(bool) });
                if (slateSet != null)
                {
                    harmony.Patch(slateSet, prefix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Prefix_SlateSet)));
                }

                // 1. Patch QuestNode_GetNearbySettlement.RunInt
                var target1 = AccessTools.Method(typeof(QuestNode_GetNearbySettlement), "RunInt");
                if (target1 != null)
                {
                    harmony.Patch(target1, prefix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Prefix_GetNearbySettlement)));
                }

                // 2. Patch QuestNode_GetFactionOf.RunInt
                var target2 = AccessTools.Method(typeof(QuestNode_GetFactionOf), "RunInt");
                if (target2 != null)
                {
                    harmony.Patch(target2, prefix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Prefix_GetFactionOf)));
                }

                // 3. Patch QuestNode_Root_Mission_BanditCamp.RunInt (Royalty DLC)
                Type banditCampType = AccessTools.TypeByName("RimWorld.QuestGen.QuestNode_Root_Mission_BanditCamp");
                if (banditCampType != null)
                {
                    var target3 = AccessTools.Method(banditCampType, "RunInt");
                    if (target3 != null)
                    {
                        harmony.Patch(target3, prefix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Prefix_Mission_BanditCamp)));
                    }
                }

                // 4. Patch producer nodes to prevent overwriting 'asker' and 'faction'
                string[] producerNodes = { 
                    "RimWorld.QuestGen.QuestNode_GetPawn", 
                    "RimWorld.QuestGen.QuestNode_GetFaction",
                    "RimWorld.QuestGen.QuestNode_GetSiteFaction"
                };

                foreach (var nodeName in producerNodes)
                {
                    Type nodeType = AccessTools.TypeByName(nodeName);
                    if (nodeType != null)
                    {
                        var method = AccessTools.Method(nodeType, "RunInt");
                        if (method != null)
                        {
                            harmony.Patch(method, prefix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Prefix_PreventOverwrite)));
                        }
                    }
                }
                // 5. Patch QuestNode_GiveRewards to force giverFaction
                var giveRewardsType = AccessTools.TypeByName("RimWorld.QuestGen.QuestNode_GiveRewards");
                if (giveRewardsType != null)
                {
                    var method = AccessTools.Method(giveRewardsType, "RunInt");
                    if (method != null)
                    {
                        harmony.Patch(method, prefix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Prefix_ForceGiverFaction)));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] Failed to apply some QuestGen patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Slate.Set 前缀补丁：在 LockSlateVariables 为 true 时保护核心变量
        /// </summary>
        public static bool Prefix_SlateSet(Slate __instance, string name, object var)
        {
            if (LockSlateVariables)
            {
                if (name == "asker" || name == "faction" || name == "askerFaction" || name == "giverFaction" || name == "enemyFaction")
                {
                    // 核心逻辑：如果变量已存在且不为 null，禁止任何覆盖操作
                    // 如果变量不存在或当前值为 null，则允许设置（方便我们的补丁填充缺失变量）
                    if (__instance.Exists(name))
                    {
                        try
                        {
                            object current = __instance.Get<object>(name);
                            if (current != null)
                            {
                                // Log.Message($"[RimDiplomacy] Blocked overwriting '{name}' in Slate (Current: {current}, New: {var ?? "null"})");
                                return false;
                            }
                        }
                        catch { /* 忽略类型转换异常 */ }
                    }

                    // 如果新值是 null，且我们处于锁定模式，通常也是不被允许的
                    if (var == null) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 强制设置奖励派系，防止 Royalty 任务默认指向帝国
        /// </summary>
        public static bool Prefix_ForceGiverFaction(QuestNode __instance)
        {
            var slate = QuestGen.slate;
            if (slate.Exists("faction"))
            {
                Faction f = slate.Get<Faction>("faction");
                if (f != null)
                {
                    // 1. 尝试直接修改节点的字段 (如果支持)
                    var field = AccessTools.Field(__instance.GetType(), "giverFaction");
                    if (field != null)
                    {
                        object slateRef = field.GetValue(__instance);
                        if (slateRef != null)
                        {
                            // 尝试设置 SlateRef 内部的变量名
                            var sliField = AccessTools.Field(slateRef.GetType(), "sli");
                            if (sliField != null)
                            {
                                string currentSli = sliField.GetValue(slateRef) as string;
                                // 如果没有设置或设置为别的，强制设为 $faction 或 $giverFaction
                                if (string.IsNullOrEmpty(currentSli) || (!currentSli.Contains("faction") && !currentSli.Contains("giverFaction")))
                                {
                                    sliField.SetValue(slateRef, "$faction");
                                }
                            }
                        }
                    }

                    // 2. 核心逻辑：确保 Slate 中存在 giverFaction 变量，因为原版节点会优先从这里读取
                    if (!slate.Exists("giverFaction"))
                    {
                        slate.Set("giverFaction", f);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// 通用前缀补丁：防止生产者节点覆盖已有的重要变量
        /// </summary>
        public static bool Prefix_PreventOverwrite(QuestNode __instance)
        {
            var slate = QuestGen.slate;
            // 受保护的核心变量名
            string[] protectedVars = { "asker", "faction", "askerFaction", "settlement", "giverFaction", "enemyFaction" };
            // 原版生产者节点中常见的存储字段名
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
                    // 绝大多数存储字段是 SlateRef<string> 类型
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
                        // 变量已由 RimDiplomacy 预设，拦截原版生成逻辑以保护上下文
                        // Log.Message($"[RimDiplomacy] QuestGen: Preserved '{varName}' from being overwritten by {__instance.GetType().Name}");
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Patch QuestNode_GetNearbySettlement.RunInt
        /// 如果 Slate 中已经存在有效的 settlement，则跳过搜索逻辑，防止被覆盖为 null
        /// </summary>
        public static bool Prefix_GetNearbySettlement(QuestNode_GetNearbySettlement __instance)
        {
            var slate = QuestGen.slate;
            string storeAs = __instance.storeAs.GetValue(slate);

            if (!string.IsNullOrEmpty(storeAs) && slate.Exists(storeAs))
            {
                object existing = slate.Get<object>(storeAs);
                if (existing is Settlement s && s.Spawned)
                {
                    // 顺便处理一下 askerFaction 逻辑，因为原版节点也会尝试设置它
                    string storeFactionLeaderAs = __instance.storeFactionLeaderAs.GetValue(slate);
                    if (!string.IsNullOrEmpty(storeFactionLeaderAs) && !slate.Exists(storeFactionLeaderAs))
                    {
                        if (s.Faction?.leader != null)
                            slate.Set(storeFactionLeaderAs, s.Faction.leader);
                    }

                    return false; // 拦截原版逻辑
                }
            }
            return true;
        }

        /// <summary>
        /// Patch QuestNode_GetFactionOf.RunInt
        /// 如果 Slate 中已经存在有效的 faction，则跳过逻辑
        /// </summary>
        public static bool Prefix_GetFactionOf(QuestNode_GetFactionOf __instance)
        {
            var slate = QuestGen.slate;
            string storeAs = __instance.storeAs.GetValue(slate);

            if (!string.IsNullOrEmpty(storeAs) && slate.Exists(storeAs))
            {
                object existing = slate.Get<object>(storeAs);
                if (existing is Faction)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Patch QuestNode_Root_Mission_BanditCamp.RunInt
        /// 如果发起派系不是帝国、文明外来者或粗鲁外来者，原版逻辑会直接报错并跳过。
        /// 我们通过 Patch 允许任何派系发起此任务。
        /// </summary>
        public static bool Prefix_Mission_BanditCamp(QuestNode __instance)
        {
            var slate = QuestGen.slate;
            // 检查是否已经通过我们注入了 asker 和 faction
            if (slate.Exists("asker") && slate.Exists("faction"))
            {
                // 使用反射访问 factionsToDrawLeaderFrom 字段，因为我们已经把类型传参改为 QuestNode
                var field = AccessTools.Field(__instance.GetType(), "factionsToDrawLeaderFrom");
                if (field != null)
                {
                    var list = field.GetValue(__instance) as List<FactionDef>;
                    if (list != null)
                    {
                        Faction f = slate.Get<Faction>("faction");
                        if (f != null && !list.Contains(f.def))
                        {
                            // 动态添加当前派系定义，允许其通过过滤
                            list.Add(f.def);
                        }
                    }
                }
            }
            return true;
        }
    }
}

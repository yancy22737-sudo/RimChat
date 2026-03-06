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
            // 0. Patch Slate.Set to implement locking (Disabled: Generic open methods cannot be patched simply)
            /*
            try {
                var slateSet = AccessTools.Method(typeof(Slate), "Set");
                if (slateSet != null)
                    harmony.Patch(slateSet, prefix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Prefix_SlateSet)));
            } catch (Exception ex) { Log.Warning($"[RimDiplomacy] Failed patch Slate.Set: {ex.Message}"); }
            */

            // 1. Patch QuestNode_GetNearbySettlement.RunInt
            try {
                var target1 = AccessTools.Method(typeof(QuestNode_GetNearbySettlement), "RunInt");
                if (target1 != null)
                    harmony.Patch(target1, prefix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Prefix_GetNearbySettlement)));
            } catch (Exception ex) { Log.Warning($"[RimDiplomacy] Failed patch GetNearbySettlement: {ex.Message}"); }

            // 2. Patch QuestNode_GetFactionOf.RunInt
            try {
                var target2 = AccessTools.Method(typeof(QuestNode_GetFactionOf), "RunInt");
                if (target2 != null)
                    harmony.Patch(target2, prefix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Prefix_GetFactionOf)));
            } catch (Exception ex) { Log.Warning($"[RimDiplomacy] Failed patch GetFactionOf: {ex.Message}"); }

            // 3. Patch QuestNode_Root_Mission_BanditCamp
            try {
                Type banditCampType = AccessTools.TypeByName("RimWorld.QuestGen.QuestNode_Root_Mission_BanditCamp");
                if (banditCampType != null)
                {
                    var target3 = AccessTools.Method(banditCampType, "RunInt");
                    if (target3 != null)
                        harmony.Patch(target3, prefix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Prefix_Mission_BanditCamp)));
                    
                    var target4 = AccessTools.Method(banditCampType, "GetRequiredPawnCount");
                    if (target4 != null)
                        harmony.Patch(target4, postfix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Postfix_GetRequiredPawnCount)));
                }
            } catch (Exception ex) { Log.Warning($"[RimDiplomacy] Failed patch BanditCamp: {ex.Message}"); }

            // 4. Patch producer nodes to prevent overwriting 'asker' and 'faction'
            string[] producerNodes = { 
                "RimWorld.QuestGen.QuestNode_GetPawn", 
                "RimWorld.QuestGen.QuestNode_GetFaction",
                "RimWorld.QuestGen.QuestNode_GetSiteFaction"
            };
            foreach (var nodeName in producerNodes)
            {
                try {
                    Type nodeType = AccessTools.TypeByName(nodeName);
                    if (nodeType != null)
                    {
                        var method = AccessTools.Method(nodeType, "RunInt");
                        if (method != null)
                            harmony.Patch(method, prefix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Prefix_PreventOverwrite)));
                    }
                } catch (Exception ex) { Log.Warning($"[RimDiplomacy] Failed patch {nodeName}: {ex.Message}"); }
            }

            // 5. Patch QuestNode_GiveRewards to force giverFaction
            try {
                var giveRewardsType = AccessTools.TypeByName("RimWorld.QuestGen.QuestNode_GiveRewards");
                if (giveRewardsType != null)
                {
                    var method = AccessTools.Method(giveRewardsType, "RunInt");
                    if (method != null)
                        harmony.Patch(method, prefix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Prefix_ForceGiverFaction)));
                }
            } catch (Exception ex) { Log.Warning($"[RimDiplomacy] Failed patch GiveRewards: {ex.Message}"); }

            // 6. Patch QuestNode_HasRoyalTitleInCurrentFaction
            try {
                Type hasRoyalTitleType = AccessTools.TypeByName("RimWorld.QuestGen.QuestNode_HasRoyalTitleInCurrentFaction");
                if (hasRoyalTitleType != null)
                {
                    var method = AccessTools.Method(hasRoyalTitleType, "RunInt");
                    if (method != null)
                        harmony.Patch(method, prefix: new HarmonyMethod(typeof(QuestGenPatch), nameof(Prefix_HasRoyalTitleInCurrentFaction)));
                }
            } catch (Exception ex) { Log.Warning($"[RimDiplomacy] Failed patch HasRoyalTitleInCurrentFaction: {ex.Message}"); }
        }

        /// <summary>
        /// Slate.Set 前缀补丁：在 LockSlateVariables 为 true 时保护核心变量
        /// </summary>
        public static bool Prefix_SlateSet(Slate __instance, string name, object var)
        {
            if (LockSlateVariables)
            {
                // 保护派系相关变量
                if (name == "asker" || name == "faction" || name == "askerFaction" || name == "giverFaction" || name == "enemyFaction" || name == "siteFaction")
                {
                    if (__instance.Exists(name))
                    {
                        try
                        {
                            object current = __instance.Get<object>(name);
                            if (current != null)
                            {
                                return false;
                            }
                        }
                        catch { }
                    }

                    if (var == null) return false;
                }
                
                // 保护数值变量（如 colonistCount, requiredPawnCount 等）
                // 如果已经设置了有效值，不允许原版脚本覆盖为无效值
                if (name == "colonistCount" || name == "requiredPawnCount")
                {
                    if (__instance.Exists(name))
                    {
                        try
                        {
                            int current = __instance.Get<int>(name);
                            // 如果当前值有效（>0），阻止原版脚本覆盖
                            if (current > 0)
                            {
                                // 检查新值是否为无效值（-1 或 0）
                                if (var != null)
                                {
                                    int newInt = -1;
                                    if (var is int i)
                                    {
                                        newInt = i;
                                    }
                                    else if (var.GetType().IsValueType)
                                    {
                                        // 尝试转换其他数值类型
                                        try { newInt = Convert.ToInt32(var); } catch { }
                                    }
                                    
                                    if (newInt <= 0)
                                    {
                                        return false; // 阻止覆盖为无效值
                                    }
                                }
                            }
                        }
                        catch { }
                    }
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
                    // 尝试直接修改节点的字段 (如果支持)
                    // 检查所有可能的派系引用字段
                    string[] fieldNames = { "giverFaction", "faction", "askerFaction" };
                    
                    foreach (var fieldName in fieldNames)
                    {
                        var field = AccessTools.Field(__instance.GetType(), fieldName);
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
                                        // Log.Message($"[RimDiplomacy] Patched {fieldName} in {__instance.GetType().Name} to use $faction");
                                    }
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
        /// Patch QuestNode_HasRoyalTitleInCurrentFaction.RunInt
        /// 原版逻辑：检查 $asker 是否有皇家头衔，有则走 node 分支（好感度+声望），无则走 elseNode 分支（仅物品）
        /// 问题：非帝国派系发起任务时，$asker 没有皇家头衔，走 elseNode 分支，不发放好感度！
        /// 解决：当 faction 不是帝国时，强制走 node 分支，但禁用 allowRoyalFavor
        /// </summary>
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

        /// <summary>
        /// 修改 QuestNode_GiveRewards 节点，禁用 allowRoyalFavor 但保留 allowGoodwill
        /// </summary>
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

        /// <summary>
        /// Postfix For QuestNode_Root_Mission.GetRequiredPawnCount
        /// 原版逻辑会根据 population 来决定需要多少人出任务。如果 population 较小(如3)，则返回 -1。
        /// 对于 AI 弹出的任务，如果 GameAIInterface 设置了合理的 requiredPawnCount（如3甚至2），
        /// 我们应该尊重 slate 的设置，而不是返回 -1 导致立刻报错 "invalid required pawn count"。
        /// </summary>
        public static void Postfix_GetRequiredPawnCount(ref int __result)
        {
            var slate = QuestGen.slate;
            if (slate != null && slate.Exists("requiredPawnCount"))
            {
                int slateCount = slate.Get<int>("requiredPawnCount");
                // 如果 GameAIInterface 等外部强行赋予了有效的人数需求，覆盖原版的 -1
                if (slateCount > 0)
                {
                    __result = slateCount;
                }
            }
        }
    }
}

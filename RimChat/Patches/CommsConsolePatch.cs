using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using RimChat.Core;
using RimChat.UI;

namespace RimChat.Patches
{
    /// <summary>
    /// 拦截原版通讯台，替换为边缘外交对话窗口
    /// 使用动态方法查找来确保兼容性
    /// </summary>
    public static class CommsConsolePatch
    {
        /// <summary>
        /// 初始化 Patch
        /// </summary>
        public static void Initialize(Harmony harmony)
        {
            try
            {
                Log.Message("[RimChat] === Initializing CommsConsole Patch ===");

                // 查找 Building_CommsConsole 类型
                var commsConsoleType = AccessTools.TypeByName("RimWorld.Building_CommsConsole");
                if (commsConsoleType == null)
                {
                    Log.Warning("[RimChat] Could not find Building_CommsConsole type");
                    return;
                }

                Log.Message($"[RimChat] Found Building_CommsConsole: {commsConsoleType.FullName}");
                Log.Message("[RimChat] Building_CommsConsole methods:");
                foreach (var method in commsConsoleType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    Log.Message($"  - {method.Name}({string.Join(", ", Array.ConvertAll(method.GetParameters(), p => p.ParameterType.Name))})");
                }

                // 查找 CompUsable 类型
                var compUsableType = AccessTools.TypeByName("Verse.CompUsable");
                if (compUsableType != null)
                {
                    Log.Message($"[RimChat] Found CompUsable: {compUsableType.FullName}");
                    Log.Message("[RimChat] CompUsable methods:");
                    foreach (var method in compUsableType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        Log.Message($"  - {method.Name}({string.Join(", ", Array.ConvertAll(method.GetParameters(), p => p.ParameterType.Name))})");
                    }
                }

                // 查找 JobDriver_UseCommsConsole 类型
                var jobDriverType = AccessTools.TypeByName("RimWorld.JobDriver_UseCommsConsole");
                if (jobDriverType != null)
                {
                    Log.Message($"[RimChat] Found JobDriver_UseCommsConsole: {jobDriverType.FullName}");
                    Log.Message("[RimChat] JobDriver_UseCommsConsole methods:");
                    foreach (var method in jobDriverType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        Log.Message($"  - {method.Name}({string.Join(", ", Array.ConvertAll(method.GetParameters(), p => p.ParameterType.Name))})");
                    }
                }

                // 尝试 Patch Building_CommsConsole 的所有方法
                PatchCommsConsoleMethods(harmony, commsConsoleType);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error initializing patch: {ex}");
            }
        }

        private static void PatchCommsConsoleMethods(Harmony harmony, Type commsConsoleType)
        {
            // Patch GetFloatMenuOptions 方法
            var getFloatMenuOptionsMethod = commsConsoleType.GetMethod("GetFloatMenuOptions", BindingFlags.Public | BindingFlags.Instance);
            if (getFloatMenuOptionsMethod != null)
            {
                Log.Message($"[RimChat] Patching Building_CommsConsole.GetFloatMenuOptions method");
                var postfixMethod = typeof(CommsConsolePatch).GetMethod("GetFloatMenuOptionsPostfix", BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(getFloatMenuOptionsMethod, postfix: new HarmonyMethod(postfixMethod));
            }
        }

        /// <summary>
        /// GetFloatMenuOptions 方法的后缀 Patch
        /// </summary>
        private static IEnumerable<FloatMenuOption> GetFloatMenuOptionsPostfix(IEnumerable<FloatMenuOption> __result, Building_CommsConsole __instance, Pawn myPawn)
        {
            if (!RimChatMod.Instance?.InstanceSettings?.ReplaceCommsConsole ?? false)
            {
                foreach (var option in __result)
                {
                    yield return option;
                }
                yield break;
            }

            if (__result == null || myPawn == null)
            {
                foreach (var option in __result ?? Enumerable.Empty<FloatMenuOption>())
                {
                    yield return option;
                }
                yield break;
            }

            // 遍历所有选项，找到呼叫派系的选项并修改点击行为
            foreach (var option in __result)
            {
                if (option != null)
                {
                    string label = option.Label;
                    
                    // 检查是否是呼叫派系相关的选项（排除召唤 Boss 和 许可权）
                    string labelLower = label.ToLower();
                    if ((labelLower.Contains("call") || labelLower.Contains("呼叫") || labelLower.Contains("contact") || labelLower.Contains("联系")) &&
                        !labelLower.Contains("boss") && !labelLower.Contains("diabolus") && !labelLower.Contains("召唤") && !labelLower.Contains("rimchat") &&
                        !labelLower.Contains("permit") && !labelLower.Contains("laborer") && !labelLower.Contains("trooper") && !labelLower.Contains("aerodrone"))
                    {
                        // 尝试从选项中提取派系信息
                        Faction targetFaction = ExtractFactionFromOption(option, __instance);
                        
                        if (targetFaction != null)
                         {
                             // 保存原始 action
                             Action originalAction = option.action;
                             
                             // 修改点击行为：先执行原版功能让小人走过去，然后打开外交对话
                             option.action = () =>
                             {
                                 // 给小人分配一个使用通讯台的 Job（让小人走过去）
                                 Job job = JobMaker.MakeJob(JobDefOf.UseCommsConsole, __instance);
                                 job.playerForced = true;
                                 myPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                                 
                                 // 注册回调，在小人到达后直接打开外交对话
                                 __instance.Map.GetComponent<CommsConsoleCallback>()?.RegisterCallback(myPawn, __instance, targetFaction);
                             };
                             
                            yield return option;
                            continue;
                        }
                    }
                }
                
                yield return option;
            }
        }

        /// <summary>
        /// 从 FloatMenuOption 中提取派系信息
        /// </summary>
        private static Faction ExtractFactionFromOption(FloatMenuOption option, Building_CommsConsole console)
        {
            // 尝试从通讯台的 CommTargets 中匹配派系
            var commTargets = console.GetCommTargets(Find.Selector.SingleSelectedThing as Pawn);
            if (commTargets != null)
            {
                foreach (var target in commTargets)
                {
                    if (target is Faction faction)
                    {
                        // 检查选项标签是否包含派系名称
                        if (option.Label.Contains(faction.Name))
                        {
                            return faction;
                        }
                    }
                }
            }
            
            return null;
        }
    }

    /// <summary>
    /// 通讯台回调组件，用于在小人到达通讯台后显示外交对话
    /// </summary>
    public class CommsConsoleCallback : MapComponent
    {
        private Dictionary<Pawn, (Building_CommsConsole console, Faction faction)> pendingCallbacks = 
            new Dictionary<Pawn, (Building_CommsConsole, Faction)>();

        public CommsConsoleCallback(Map map) : base(map) { }

        public void RegisterCallback(Pawn pawn, Building_CommsConsole console, Faction faction)
        {
            pendingCallbacks[pawn] = (console, faction);
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            
            var toRemove = new List<Pawn>();
            foreach (var kvp in pendingCallbacks)
            {
                var pawn = kvp.Key;
                var (console, faction) = kvp.Value;
                
                // 检查小人是否到达通讯台
                if (pawn.jobs?.curJob?.targetA.Thing == console && 
                    pawn.jobs.curJob.def == JobDefOf.UseCommsConsole &&
                    pawn.Position.DistanceTo(console.InteractionCell) <= 1.5f)
                {
                    // 直接打开外交对话窗口
                    var dialogueWindow = new Dialog_DiplomacyDialogue(faction, pawn);
                    Find.WindowStack.Add(dialogueWindow);
                    
                    // 取消当前 Job
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    
                    toRemove.Add(pawn);
                }
            }
            
            foreach (var pawn in toRemove)
            {
                pendingCallbacks.Remove(pawn);
            }
        }
    }
}

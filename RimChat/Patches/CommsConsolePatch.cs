using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using RimChat.Core;
using RimChat.Dialogue;
using RimChat.UI;

namespace RimChat.Patches
{
    /// <summary>/// 拦截原版通讯台, 替换为边缘diplomacydialoguewindow
 /// 使用dynamicmethodlookup来确保compatibility性
 ///</summary>
    public static class CommsConsolePatch
    {
        private static int lastNoPatchLogTick = int.MinValue;
        private const int PatchDebugLogCooldownTicks = 180;
        private const int InterceptLogCooldownTicks = 300;
        private const int SkipLogCooldownTicks = 300;
        private static readonly Dictionary<string, int> lastInterceptLogTickByKey =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> lastSkipLogTickByKey =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private enum CommsOptionBypassReason
        {
            NullOption = 0,
            NullAction = 1,
            NonVanillaAction = 2,
            InvalidFaction = 3
        }

        /// <summary>/// initialize Patch
 ///</summary>
        public static void Initialize(Harmony harmony)
        {
            try
            {
                Log.Message("[RimChat] === Initializing CommsConsole Patch ===");

                // Lookup Building_CommsConsole 类型
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

                // Lookup CompUsable 类型
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

                // Lookup JobDriver_UseCommsConsole 类型
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

                // 尝试 Patch Building_CommsConsole 的所有method
                PatchCommsConsoleMethods(harmony, commsConsoleType);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error initializing patch: {ex}");
            }
        }

        private static void PatchCommsConsoleMethods(Harmony harmony, Type commsConsoleType)
        {
            // Patch GetFloatMenuOptions method
            var getFloatMenuOptionsMethod = commsConsoleType.GetMethod("GetFloatMenuOptions", BindingFlags.Public | BindingFlags.Instance);
            if (getFloatMenuOptionsMethod != null)
            {
                Log.Message($"[RimChat] Patching Building_CommsConsole.GetFloatMenuOptions method");
                var postfixMethod = typeof(CommsConsolePatch).GetMethod("GetFloatMenuOptionsPostfix", BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(getFloatMenuOptionsMethod, postfix: new HarmonyMethod(postfixMethod));
            }
        }

        /// <summary>/// GetFloatMenuOptions method的后缀 Patch
 ///</summary>
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

            int patchedCount = 0;
            // Fail fast: patch only vanilla comms actions with a valid faction target.
            foreach (var option in __result)
            {
                if (option == null)
                {
                    TryLogBypassReason(myPawn, option, CommsOptionBypassReason.NullOption);
                    yield return option;
                    continue;
                }

                if (option.action == null)
                {
                    TryLogBypassReason(myPawn, option, CommsOptionBypassReason.NullAction);
                    yield return option;
                    continue;
                }

                if (!IsVanillaCommsAction(option.action))
                {
                    TryLogBypassReason(myPawn, option, CommsOptionBypassReason.NonVanillaAction);
                    yield return option;
                    continue;
                }

                Faction targetFaction = ExtractFactionFromOption(option, __instance, myPawn);
                if (targetFaction == null)
                {
                    TryLogBypassReason(myPawn, option, CommsOptionBypassReason.InvalidFaction);
                    yield return option;
                    continue;
                }

                patchedCount++;
                option.action = () =>
                {
                    DialogueOpenIntent intent = DialogueOpenIntent.CreateDiplomacy(targetFaction, myPawn, myPawn.Map);
                    if (DialogueWindowCoordinator.TryOpen(intent, out string reason))
                    {
                        return;
                    }

                    Log.Warning($"[RimChat] Comms immediate open rejected: pawn={myPawn.LabelShortCap}, faction={targetFaction.Name}, reason={reason ?? "unknown"}");
                    if (Find.WindowStack != null && !targetFaction.defeated)
                    {
                        Log.Warning($"[RimChat] Applying direct diplomacy open fallback: source=comms_immediate, faction={targetFaction.Name}");
                        Find.WindowStack.Add(new Dialog_DiplomacyDialogue(targetFaction, myPawn));
                    }
                };
                if (ShouldLogInterceptDebug(myPawn, targetFaction))
                {
                    Log.Message($"[RimChat] Comms option intercepted: pawn={myPawn.LabelShortCap}, faction={targetFaction.Name}");
                }
                yield return option;
            }

            if (patchedCount == 0 && ShouldLogNoPatchDebug())
            {
                Log.Warning($"[RimChat] Comms menu patch found no faction options: pawn={myPawn?.LabelShortCap ?? "null"}, map={__instance?.Map?.uniqueID ?? -1}");
            }
        }

        /// <summary>/// 从 FloatMenuOption 中提取faction信息
 ///</summary>
        private static Faction ExtractFactionFromOption(FloatMenuOption option, Building_CommsConsole console, Pawn myPawn)
        {
            if (option == null || console == null || myPawn == null)
            {
                return null;
            }

            Faction fromAction = ExtractFactionFromAction(option);
            if (IsValidDialogueFaction(fromAction))
            {
                return fromAction;
            }

            string label = option.Label ?? string.Empty;
            var commTargets = console.GetCommTargets(myPawn);
            foreach (var target in commTargets ?? Enumerable.Empty<ICommunicable>())
            {
                if (!(target is Faction faction))
                {
                    continue;
                }

                if (IsValidDialogueFaction(faction) && IsLabelLikelyFactionEntry(label, faction.Name))
                {
                    return faction;
                }
            }

            return null;
        }

        private static Faction ExtractFactionFromAction(FloatMenuOption option)
        {
            if (option?.action == null)
            {
                return null;
            }

            object target = option.action.Target;
            if (target == null)
            {
                return null;
            }

            try
            {
                foreach (FieldInfo field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!typeof(Faction).IsAssignableFrom(field.FieldType))
                    {
                        continue;
                    }

                    Faction candidate = field.GetValue(target) as Faction;
                    if (IsValidDialogueFaction(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to inspect comms option action closure: {ex.Message}");
            }

            return null;
        }

        private static bool IsVanillaCommsAction(Action action)
        {
            if (action == null)
            {
                return false;
            }

            MethodInfo method = action.Method;
            Type declaringType = method?.DeclaringType;
            Assembly assembly = declaringType?.Assembly ?? method?.Module?.Assembly;
            if (assembly == null)
            {
                return false;
            }

            string assemblyName = assembly.GetName().Name ?? string.Empty;
            if (!string.Equals(assemblyName, "Assembly-CSharp", StringComparison.Ordinal))
            {
                return false;
            }

            string declaringTypeName = declaringType?.FullName ?? string.Empty;
            return declaringTypeName.IndexOf("RimWorld.Building_CommsConsole", StringComparison.Ordinal) >= 0 ||
                declaringTypeName.IndexOf("Building_CommsConsole", StringComparison.Ordinal) >= 0;
        }

        private static bool IsLabelLikelyFactionEntry(string label, string factionName)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(factionName))
            {
                return false;
            }

            if (label.IndexOf(factionName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string compactLabel = label.Replace(" ", string.Empty);
            string compactFaction = factionName.Replace(" ", string.Empty);
            return compactLabel.IndexOf(compactFaction, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsValidDialogueFaction(Faction faction)
        {
            return faction != null && !faction.IsPlayer && !faction.defeated;
        }

        private static bool ShouldLogNoPatchDebug()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (lastNoPatchLogTick == int.MinValue ||
                currentTick - lastNoPatchLogTick >= PatchDebugLogCooldownTicks)
            {
                lastNoPatchLogTick = currentTick;
                return true;
            }

            return false;
        }

        private static bool ShouldLogInterceptDebug(Pawn pawn, Faction faction)
        {
            if (pawn == null || faction == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            string key = $"{pawn.GetUniqueLoadID()}::{faction.loadID}";
            if (lastInterceptLogTickByKey.TryGetValue(key, out int lastTick) &&
                currentTick - lastTick < InterceptLogCooldownTicks)
            {
                return false;
            }

            lastInterceptLogTickByKey[key] = currentTick;
            TrimInterceptLogCache(currentTick);
            return true;
        }

        private static void TryLogBypassReason(Pawn pawn, FloatMenuOption option, CommsOptionBypassReason reason)
        {
            if (!ShouldLogBypassDebug(pawn, option, reason))
            {
                return;
            }

            string pawnLabel = pawn?.LabelShortCap ?? "null";
            string optionLabel = option?.Label ?? "<null>";
            string actionInfo = option?.action?.Method?.DeclaringType?.FullName ?? "<no-action>";
            Log.Message($"[RimChat] Comms option bypassed: reason={reason}, pawn={pawnLabel}, label={optionLabel}, actionType={actionInfo}");
        }

        private static bool ShouldLogBypassDebug(Pawn pawn, FloatMenuOption option, CommsOptionBypassReason reason)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            string pawnId = pawn?.GetUniqueLoadID() ?? "null";
            string label = option?.Label ?? "<null>";
            string key = $"{pawnId}::{reason}::{label}";
            if (lastSkipLogTickByKey.TryGetValue(key, out int lastTick) &&
                currentTick - lastTick < SkipLogCooldownTicks)
            {
                return false;
            }

            lastSkipLogTickByKey[key] = currentTick;
            TrimSkipLogCache(currentTick);
            return true;
        }

        private static void TrimInterceptLogCache(int currentTick)
        {
            if (lastInterceptLogTickByKey.Count <= 256)
            {
                return;
            }

            int staleThreshold = currentTick - InterceptLogCooldownTicks * 8;
            var staleKeys = new List<string>();
            foreach (var pair in lastInterceptLogTickByKey)
            {
                if (pair.Value <= staleThreshold)
                {
                    staleKeys.Add(pair.Key);
                }
            }

            foreach (string key in staleKeys)
            {
                lastInterceptLogTickByKey.Remove(key);
            }
        }

        private static void TrimSkipLogCache(int currentTick)
        {
            if (lastSkipLogTickByKey.Count <= 256)
            {
                return;
            }

            int staleThreshold = currentTick - SkipLogCooldownTicks * 8;
            var staleKeys = new List<string>();
            foreach (var pair in lastSkipLogTickByKey)
            {
                if (pair.Value <= staleThreshold)
                {
                    staleKeys.Add(pair.Key);
                }
            }

            foreach (string key in staleKeys)
            {
                lastSkipLogTickByKey.Remove(key);
            }
        }
    }

    /// <summary>/// 通讯台回调component, used for在小人到达通讯台后displaydiplomacydialogue
 ///</summary>
    public class CommsConsoleCallback : MapComponent
    {
        private sealed class PendingCommsOpenRequest
        {
            public string PawnId;
            public string ConsoleId;
            public string ConsoleThingId;
            public int FactionLoadId;
            public int MapUniqueId;
            public int RegisteredTick;
            public int LastFailureLogTick;
        }

        private readonly Dictionary<string, PendingCommsOpenRequest> pendingCallbacks =
            new Dictionary<string, PendingCommsOpenRequest>(StringComparer.Ordinal);
        private const int PendingRequestTimeoutTicks = 2500;
        private const int FailureLogCooldownTicks = 120;

        public CommsConsoleCallback(Map map) : base(map) { }

        public void RegisterCallback(Pawn pawn, Building_CommsConsole console, Faction faction)
        {
            if (pawn == null || console == null || faction == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            pendingCallbacks[pawnId] = new PendingCommsOpenRequest
            {
                PawnId = pawnId,
                ConsoleId = console.GetUniqueLoadID(),
                ConsoleThingId = console.ThingID,
                FactionLoadId = faction.loadID,
                MapUniqueId = map?.uniqueID ?? pawn.Map?.uniqueID ?? -1,
                RegisteredTick = Find.TickManager?.TicksGame ?? 0,
                LastFailureLogTick = int.MinValue
            };
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            var toRemove = new List<string>();
            foreach (var kvp in pendingCallbacks)
            {
                PendingCommsOpenRequest request = kvp.Value;
                if (!TryResolveRequest(request, out Pawn pawn, out Building_CommsConsole console, out Faction faction))
                {
                    if (IsRequestExpired(request, currentTick))
                    {
                        toRemove.Add(kvp.Key);
                    }
                    continue;
                }

                if (!IsPawnStillUsingCommsConsole(pawn, console))
                {
                    if (IsRequestExpired(request, currentTick))
                    {
                        toRemove.Add(kvp.Key);
                    }

                    continue;
                }

                if (pawn.jobs?.curJob?.def == JobDefOf.UseCommsConsole &&
                    pawn.Position.DistanceTo(console.InteractionCell) <= 1.5f)
                {
                    DialogueOpenIntent intent = DialogueOpenIntent.CreateDiplomacy(faction, pawn, pawn.Map);
                    bool opened = DialogueWindowCoordinator.TryOpen(intent, out string reason);
                    
                    if (opened)
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        toRemove.Add(kvp.Key);
                    }
                    else if (TryOpenDiplomacyDirectFallback(faction, pawn, "comms_callback", reason))
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        toRemove.Add(kvp.Key);
                    }
                    else if (ShouldLogFailure(request, currentTick))
                    {
                        Log.Warning($"[RimChat] Comms dialogue open skipped: pawn={pawn.LabelShortCap}, faction={faction?.Name ?? "null"}, reason={reason ?? "unknown"}");
                        request.LastFailureLogTick = currentTick;
                    }
                }
            }
            
            foreach (string pawnId in toRemove)
            {
                pendingCallbacks.Remove(pawnId);
            }
        }

        private static bool TryResolveRequest(
            PendingCommsOpenRequest request,
            out Pawn pawn,
            out Building_CommsConsole console,
            out Faction faction)
        {
            pawn = null;
            console = null;
            faction = null;
            if (request == null)
            {
                return false;
            }

            if (!DialogueContextResolver.TryResolvePawn(request.PawnId, out pawn))
            {
                return false;
            }

            if (!DialogueContextResolver.TryResolveFaction(request.FactionLoadId, out faction))
            {
                return false;
            }

            if (!DialogueContextResolver.TryResolveMap(request.MapUniqueId, out Map map))
            {
                map = pawn.Map;
            }

            if (map == null || string.IsNullOrWhiteSpace(request.ConsoleId))
            {
                return false;
            }

            console = map.listerBuildings?.allBuildingsColonist?
                .OfType<Building_CommsConsole>()
                .FirstOrDefault(building =>
                    building != null &&
                    string.Equals(building.GetUniqueLoadID(), request.ConsoleId, StringComparison.Ordinal));

            if (console == null)
            {
                console = map.listerThings?.AllThings?
                    .OfType<Building_CommsConsole>()
                    .FirstOrDefault(building =>
                        building != null &&
                        (string.Equals(building.ThingID, request.ConsoleThingId, StringComparison.Ordinal) ||
                         string.Equals(building.GetUniqueLoadID(), request.ConsoleId, StringComparison.Ordinal)));
            }

            return console != null;
        }

        private static bool IsPawnStillUsingCommsConsole(Pawn pawn, Building_CommsConsole console)
        {
            if (!DialogueContextResolver.IsPawnValid(pawn) || console == null)
            {
                return false;
            }

            Job currentJob = pawn.jobs?.curJob;
            if (currentJob == null || currentJob.def != JobDefOf.UseCommsConsole)
            {
                return false;
            }

            if (currentJob.targetA.Thing == console)
            {
                return true;
            }

            // Fallback: targetA might be stale for one tick while pawn is already at interaction cell.
            return pawn.Position.DistanceTo(console.InteractionCell) <= 2.5f;
        }

        private static bool IsRequestExpired(PendingCommsOpenRequest request, int currentTick)
        {
            if (request == null)
            {
                return true;
            }

            int startTick = request.RegisteredTick;
            return startTick > 0 && currentTick - startTick > PendingRequestTimeoutTicks;
        }

        private static bool ShouldLogFailure(PendingCommsOpenRequest request, int currentTick)
        {
            if (request == null)
            {
                return false;
            }

            return request.LastFailureLogTick == int.MinValue ||
                currentTick - request.LastFailureLogTick >= FailureLogCooldownTicks;
        }

        private static bool TryOpenDiplomacyDirectFallback(Faction faction, Pawn negotiator, string source, string reason)
        {
            if (Find.WindowStack == null || faction == null || faction.defeated)
            {
                return false;
            }

            Log.Warning($"[RimChat] Comms dialogue open rejected: faction={faction.Name}, reason={reason ?? "unknown"}");
            Log.Warning($"[RimChat] Applying direct diplomacy open fallback: source={source}, faction={faction.Name}");
            Find.WindowStack.Add(new Dialog_DiplomacyDialogue(faction, negotiator));
            return true;
        }
    }
}

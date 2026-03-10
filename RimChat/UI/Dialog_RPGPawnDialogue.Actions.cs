using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimChat.AI;
using RimChat.Config;
using RimChat.Core;
using RimChat.DiplomacySystem;
using RimChat.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Responsibilities: execute RPG actions, normalize action names, and render compact action feedback.
 /// Dependencies: LLMRpgApiResponse, GameComponent_RPGManager, RimWorld defs/workers.
 ///</summary>
    public partial class Dialog_RPGPawnDialogue
    {
        private struct ActionFeedbackEntry
        {
            public string Text;
            public Color Color;
            public float CreatedAt;
            public float Duration;
        }

        private readonly List<ActionFeedbackEntry> actionFeedbackEntries = new List<ActionFeedbackEntry>();
        private static readonly Color ActionSuccessColor = new Color(0.45f, 0.9f, 0.55f, 1f);
        private static readonly Color ActionFailureColor = new Color(0.95f, 0.6f, 0.45f, 1f);
        private static readonly Color ActionErrorColor = new Color(0.95f, 0.4f, 0.4f, 1f);
        private static readonly Color ActionInfoColor = new Color(0.55f, 0.78f, 0.98f, 1f);
        private const float ActionFeedbackDefaultDuration = 3.8f;
        private const int ActionFeedbackMaxCount = 8;

        private void ApplyRPGAPIAndShowPopup(LLMRpgApiResponse apiRes)
        {
            if (apiRes == null)
            {
                return;
            }

            ExecuteRpgActions(apiRes.Actions);
        }

        private void ExecuteRpgActions(List<LLMRpgApiResponse.ApiAction> actions)
        {
            if (actions == null || actions.Count == 0)
            {
                return;
            }

            foreach (var action in actions)
            {
                ExecuteRpgAction(action);
            }
        }

        private void ExecuteRpgAction(LLMRpgApiResponse.ApiAction action)
        {
            string normalizedName = NormalizeRpgActionName(action?.action);
            if (string.IsNullOrEmpty(normalizedName))
            {
                return;
            }

            LogRpgActionDebug($"RPG action received: raw={action?.action ?? "null"}, normalized={normalizedName}");

            try
            {
                bool success = normalizedName switch
                {
                    "ExitDialogue" => ExecuteExitDialogue(action?.reason, false),
                    "ExitDialogueCooldown" => ExecuteExitDialogue(action?.reason, true),
                    "RomanceAttempt" => ExecuteRomanceAttempt(action),
                    "MarriageProposal" => ExecuteMarriageProposal(action),
                    "Breakup" => ExecuteBreakup(action),
                    "Divorce" => ExecuteDivorce(action),
                    "Date" => ExecuteDate(action),
                    "TryGainMemory" => ExecuteTryGainMemory(action),
                    "TryAffectSocialGoodwill" => ExecuteTryAffectSocialGoodwill(action),
                    "ReduceResistance" => ExecuteReduceResistance(action),
                    "ReduceWill" => ExecuteReduceWill(action),
                    "Recruit" => ExecuteRecruit(),
                    "TryTakeOrderedJob" => ExecuteTryTakeOrderedJob(action),
                    "TriggerIncident" => ExecuteTriggerIncident(action),
                    "GrantInspiration" => ExecuteGrantInspiration(action),
                    _ => ExecuteUnknownAction(normalizedName, action)
                };

                if (success)
                {
                    NotifyActionSuccess(normalizedName);
                }
            }
            catch (Exception ex)
            {
                NotifyActionError(normalizedName);
                Log.Warning($"[RimChat] RPG action execution failed: {action?.action}, error={ex}");
            }
        }

        private bool ExecuteExitDialogue(string reason, bool withCooldown)
        {
            if (withCooldown)
            {
                var manager = Current.Game?.GetComponent<GameComponent_RPGManager>();
                int cooldownTicks = manager?.GetRpgDialogueExitCooldownTicks() ?? 60000;
                manager?.StartRpgDialogueCooldown(target, cooldownTicks);
                float days = cooldownTicks / 60000f;
                AddActionFeedback("RimChat_RPGActionToast_CooldownApplied".Translate(days.ToString("F1")), ActionInfoColor, 4.6f);
            }

            HandleNpcExitDialogue(reason);
            return true;
        }

        private bool ExecuteRomanceAttempt(LLMRpgApiResponse.ApiAction action)
        {
            if (!CanApplyRelationshipAction("RomanceAttempt"))
            {
                return false;
            }

            PawnRelationDef loverDef = ResolveRelationDef("RomanceAttempt", PawnRelationDefOf.Lover, "Lover");
            if (loverDef == null)
            {
                return false;
            }

            PawnRelationDef exLoverDef = ResolveRelationDef("RomanceAttempt", PawnRelationDefOf.ExLover, "ExLover");
            PawnRelationDef exSpouseDef = ResolveRelationDef("RomanceAttempt", PawnRelationDefOf.ExSpouse, "ExSpouse");

            if (HasPairRelation(PawnRelationDefOf.Spouse) || HasPairRelation(PawnRelationDefOf.Fiance) || HasPairRelation(loverDef))
            {
                return true;
            }

            RemovePairRelation(exLoverDef);
            RemovePairRelation(exSpouseDef);
            AddPairRelation(loverDef);
            return true;
        }

        private bool ExecuteMarriageProposal(LLMRpgApiResponse.ApiAction action)
        {
            if (!CanApplyRelationshipAction("MarriageProposal"))
            {
                return false;
            }

            PawnRelationDef spouseDef = ResolveRelationDef("MarriageProposal", PawnRelationDefOf.Spouse, "Spouse");
            if (spouseDef == null)
            {
                return false;
            }

            PawnRelationDef exSpouseDef = ResolveRelationDef("MarriageProposal", PawnRelationDefOf.ExSpouse, "ExSpouse");
            PawnRelationDef loverDef = ResolveRelationDef("MarriageProposal", PawnRelationDefOf.Lover, "Lover");
            PawnRelationDef fianceDef = ResolveRelationDef("MarriageProposal", PawnRelationDefOf.Fiance, "Fiance");
            PawnRelationDef exLoverDef = ResolveRelationDef("MarriageProposal", PawnRelationDefOf.ExLover, "ExLover");

            ClearOtherSpousesForMarriage(target, initiator, spouseDef, exSpouseDef);
            ClearOtherSpousesForMarriage(initiator, target, spouseDef, exSpouseDef);

            RemovePairRelation(exSpouseDef);
            RemovePairRelation(exLoverDef);
            RemovePairRelation(fianceDef);
            RemovePairRelation(loverDef);
            AddPairRelation(spouseDef);
            return true;
        }

        private bool ExecuteBreakup(LLMRpgApiResponse.ApiAction action)
        {
            if (!CanApplyRelationshipAction("Breakup"))
            {
                return false;
            }

            PawnRelationDef spouseDef = ResolveRelationDef("Breakup", PawnRelationDefOf.Spouse, "Spouse");
            PawnRelationDef exSpouseDef = ResolveRelationDef("Breakup", PawnRelationDefOf.ExSpouse, "ExSpouse");
            PawnRelationDef loverDef = ResolveRelationDef("Breakup", PawnRelationDefOf.Lover, "Lover");
            PawnRelationDef fianceDef = ResolveRelationDef("Breakup", PawnRelationDefOf.Fiance, "Fiance");
            PawnRelationDef exLoverDef = ResolveRelationDef("Breakup", PawnRelationDefOf.ExLover, "ExLover");

            bool hadMarriage = HasPairRelation(spouseDef);
            bool hadRomance = HasPairRelation(loverDef) || HasPairRelation(fianceDef);

            if (hadMarriage)
            {
                RemovePairRelation(spouseDef);
                AddPairRelation(exSpouseDef);
            }

            RemovePairRelation(loverDef);
            RemovePairRelation(fianceDef);

            if (!hadMarriage)
            {
                AddPairRelation(exLoverDef);
            }

            if (!hadMarriage && !hadRomance)
            {
                AddPairRelation(exLoverDef);
            }

            return true;
        }

        private bool ExecuteDate(LLMRpgApiResponse.ApiAction action)
        {
            // "Date" has no dedicated hard state in vanilla; map it to guaranteed romantic state.
            return ExecuteRomanceAttempt(action);
        }

        private bool ExecuteDivorce(LLMRpgApiResponse.ApiAction action)
        {
            if (!CanApplyRelationshipAction("Divorce"))
            {
                NotifyActionFailure("Divorce", "RimChat_RPGActionFail_InvalidTarget".Translate());
                return false;
            }

            PawnRelationDef spouseDef = ResolveRelationDef("Divorce", PawnRelationDefOf.Spouse, "Spouse");
            PawnRelationDef exSpouseDef = ResolveRelationDef("Divorce", PawnRelationDefOf.ExSpouse, "ExSpouse");
            if (spouseDef == null || exSpouseDef == null)
            {
                return false;
            }

            RemovePairRelation(PawnRelationDefOf.Fiance);
            RemovePairRelation(PawnRelationDefOf.Lover);
            RemovePairRelation(spouseDef);
            AddPairRelation(exSpouseDef);
            return true;
        }

        private bool ExecuteTryAffectSocialGoodwill(LLMRpgApiResponse.ApiAction action)
        {
            if (target?.Faction == null || initiator?.Faction == null)
            {
                NotifyActionFailure("TryAffectSocialGoodwill", "RimChat_RPGActionFail_MissingFaction".Translate());
                return false;
            }

            target.Faction.TryAffectGoodwillWith(initiator.Faction, action.amount, true, true, null);
            return true;
        }

        private bool ExecuteReduceResistance(LLMRpgApiResponse.ApiAction action)
        {
            if (target?.guest == null || !target.IsPrisoner || action.amount <= 0)
            {
                NotifyActionFailure("ReduceResistance", "RimChat_RPGActionFail_InvalidTarget".Translate());
                return false;
            }

            target.guest.resistance = Math.Max(0f, target.guest.resistance - action.amount);
            return true;
        }

        private bool ExecuteReduceWill(LLMRpgApiResponse.ApiAction action)
        {
            if (target?.guest == null || !target.IsPrisoner || action.amount <= 0)
            {
                NotifyActionFailure("ReduceWill", "RimChat_RPGActionFail_InvalidTarget".Translate());
                return false;
            }

            target.guest.will = Math.Max(0f, target.guest.will - action.amount);
            return true;
        }

        private bool ExecuteRecruit()
        {
            if (target == null || initiator?.Faction == null || target.Faction == initiator.Faction)
            {
                NotifyActionFailure("Recruit", "RimChat_RPGActionFail_InvalidTarget".Translate());
                return false;
            }

            RecruitUtility.Recruit(target, initiator.Faction, initiator);
            return true;
        }

        private bool ExecuteTryTakeOrderedJob(LLMRpgApiResponse.ApiAction action)
        {
            if (!string.Equals(action?.defName, "AttackMelee", StringComparison.OrdinalIgnoreCase))
            {
                NotifyActionFailure("TryTakeOrderedJob", "RimChat_RPGActionFail_UnsupportedJob".Translate(action?.defName ?? "null"));
                return false;
            }

            var attackJob = new Verse.AI.Job(JobDefOf.AttackMelee, initiator);
            target?.jobs?.TryTakeOrderedJob(attackJob, Verse.AI.JobTag.Misc);
            return true;
        }

        private bool ExecuteTriggerIncident(LLMRpgApiResponse.ApiAction action)
        {
            IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail(action?.defName);
            Map map = target?.MapHeld ?? Find.CurrentMap;
            if (def == null || map == null)
            {
                NotifyActionFailure("TriggerIncident", "RimChat_RPGActionFail_InvalidDefName".Translate(action?.defName ?? "null"));
                return false;
            }

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(def.category, map);
            parms.faction = target?.Faction;
            if (action.amount > 0)
            {
                parms.points = action.amount;
            }

            bool executed = def.Worker.TryExecute(parms);
            if (!executed)
            {
                NotifyActionFailure("TriggerIncident", "RimChat_RPGActionFail_GameRejected".Translate());
            }
            return executed;
        }

        private bool ExecuteGrantInspiration(LLMRpgApiResponse.ApiAction action)
        {
            object handler = target?.mindState?.inspirationHandler;
            if (handler == null)
            {
                NotifyActionFailure("GrantInspiration", "RimChat_RPGActionFail_InvalidTarget".Translate());
                return false;
            }

            List<InspirationDef> candidates = BuildInspirationCandidates(action?.defName);
            if (candidates.Count == 0)
            {
                NotifyActionFailure("GrantInspiration", "RimChat_RPGActionFail_InvalidDefName".Translate(action?.defName ?? "null"));
                return false;
            }

            foreach (InspirationDef def in candidates)
            {
                if (!TryStartInspiration(handler, def, action?.reason))
                {
                    continue;
                }

                LogRpgActionDebug($"GrantInspiration success: def={def.defName}, pawn={target?.LabelShort ?? "null"}");
                return true;
            }

            LogRpgActionDebug($"GrantInspiration rejected for all candidates. requestedDef={action?.defName ?? "null"}");
            NotifyActionFailure("GrantInspiration", "RimChat_RPGActionFail_GameRejected".Translate());
            return false;
        }

        private bool ExecuteUnknownAction(string normalizedName, LLMRpgApiResponse.ApiAction action)
        {
            string rawAction = action?.action ?? normalizedName;
            AddActionFeedback("RimChat_RPGActionToast_Unknown".Translate(rawAction), ActionFailureColor);
            Log.Message($"[RimChat] Unknown RPG action ignored: {rawAction}");
            return false;
        }

        private bool CanApplyRelationshipAction(string actionName)
        {
            if (target == null || initiator == null || target.relations == null || initiator.relations == null || target == initiator)
            {
                NotifyActionFailure(actionName, "RimChat_RPGActionFail_InvalidTarget".Translate());
                return false;
            }

            return true;
        }

        private PawnRelationDef ResolveRelationDef(string actionName, PawnRelationDef relationDef, string defName)
        {
            PawnRelationDef resolved = relationDef ?? DefDatabase<PawnRelationDef>.GetNamedSilentFail(defName);
            if (resolved == null)
            {
                NotifyActionFailure(actionName, "RimChat_RPGActionFail_InvalidDefName".Translate(defName));
            }
            return resolved;
        }

        private bool HasPairRelation(PawnRelationDef relationDef)
        {
            if (relationDef == null || target?.relations == null || initiator == null)
            {
                return false;
            }

            return target.relations.DirectRelationExists(relationDef, initiator);
        }

        private void AddPairRelation(PawnRelationDef relationDef)
        {
            if (relationDef == null || target?.relations == null || initiator == null)
            {
                return;
            }

            if (!target.relations.DirectRelationExists(relationDef, initiator))
            {
                target.relations.AddDirectRelation(relationDef, initiator);
            }
        }

        private void RemovePairRelation(PawnRelationDef relationDef)
        {
            if (relationDef == null || target?.relations == null || initiator == null)
            {
                return;
            }

            if (target.relations.DirectRelationExists(relationDef, initiator))
            {
                target.relations.RemoveDirectRelation(relationDef, initiator);
            }
        }

        private void ClearOtherSpousesForMarriage(Pawn pawn, Pawn keepPartner, PawnRelationDef spouseDef, PawnRelationDef exSpouseDef)
        {
            if (pawn?.relations?.DirectRelations == null || spouseDef == null)
            {
                return;
            }

            List<Pawn> otherSpouses = pawn.relations.DirectRelations
                .Where(r => r.def == spouseDef && r.otherPawn != null && r.otherPawn != keepPartner)
                .Select(r => r.otherPawn)
                .Distinct()
                .ToList();

            foreach (Pawn otherSpouse in otherSpouses)
            {
                if (pawn.relations.DirectRelationExists(spouseDef, otherSpouse))
                {
                    pawn.relations.RemoveDirectRelation(spouseDef, otherSpouse);
                }

                if (exSpouseDef != null && !pawn.relations.DirectRelationExists(exSpouseDef, otherSpouse))
                {
                    pawn.relations.AddDirectRelation(exSpouseDef, otherSpouse);
                }
            }
        }

        private List<InspirationDef> BuildInspirationCandidates(string defName)
        {
            var result = new List<InspirationDef>();
            if (!string.IsNullOrWhiteSpace(defName))
            {
                InspirationDef preferred = DefDatabase<InspirationDef>.GetNamedSilentFail(defName);
                if (preferred != null)
                {
                    result.Add(preferred);
                }
                else
                {
                    LogRpgActionDebug($"GrantInspiration invalid defName from LLM: {defName}");
                }
            }

            var defs = DefDatabase<InspirationDef>.AllDefsListForReading;
            if (defs == null || defs.Count == 0)
            {
                return result;
            }

            foreach (InspirationDef def in defs.InRandomOrder())
            {
                if (def == null || result.Contains(def))
                {
                    continue;
                }
                result.Add(def);
            }

            return result;
        }

        private bool TryStartInspiration(object handler, InspirationDef inspirationDef, string reason)
        {
            if (handler == null || inspirationDef == null)
            {
                return false;
            }

            MethodInfo[] methods = handler
                .GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => string.Equals(m.Name, "TryStartInspiration", StringComparison.Ordinal))
                .OrderBy(m => m.GetParameters().Length)
                .ToArray();

            foreach (MethodInfo method in methods)
            {
                object[] args = BuildInspirationInvokeArgs(method, inspirationDef, reason);
                if (args == null)
                {
                    continue;
                }

                try
                {
                    object invokeResult = method.Invoke(handler, args);
                    if (invokeResult is bool started)
                    {
                        return started;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    LogRpgActionDebug($"GrantInspiration invoke failed on {method}: {ex.Message}");
                }
            }

            return false;
        }

        private object[] BuildInspirationInvokeArgs(MethodInfo method, InspirationDef inspirationDef, string reason)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                return null;
            }

            if (!parameters[0].ParameterType.IsAssignableFrom(typeof(InspirationDef)) &&
                !typeof(InspirationDef).IsAssignableFrom(parameters[0].ParameterType))
            {
                return null;
            }

            object[] args = new object[parameters.Length];
            args[0] = inspirationDef;
            for (int i = 1; i < parameters.Length; i++)
            {
                Type paramType = parameters[i].ParameterType;
                if (paramType == typeof(string))
                {
                    args[i] = reason ?? string.Empty;
                }
                else if (parameters[i].HasDefaultValue)
                {
                    args[i] = parameters[i].DefaultValue;
                }
                else if (paramType == typeof(bool))
                {
                    args[i] = false;
                }
                else if (!paramType.IsValueType || Nullable.GetUnderlyingType(paramType) != null)
                {
                    args[i] = null;
                }
                else
                {
                    args[i] = Activator.CreateInstance(paramType);
                }
            }

            return args;
        }

        private void LogRpgActionDebug(string message)
        {
            if (RimChatMod.Settings?.EnableDebugLogging != true)
            {
                return;
            }

            Log.Message($"[RimChat] {message}");
        }

        private void HandleNpcExitDialogue(string reason)
        {
            isDialogueEndedByNpc = true;
            dialogueEndReason = reason ?? string.Empty;
            GUI.FocusControl(null);
        }

        private void NotifyActionSuccess(string actionName)
        {
            string actionLabel = GetRpgActionLabel(actionName);
            AddActionFeedback("RimChat_RPGActionToast_Success".Translate(actionLabel), ActionSuccessColor);
            LogRpgActionDebug($"RPG action success: {actionName}");
        }

        private void NotifyActionFailure(string actionName, string reason)
        {
            string actionLabel = GetRpgActionLabel(actionName);
            AddActionFeedback("RimChat_RPGActionToast_Failure".Translate(actionLabel, reason), ActionFailureColor);
            LogRpgActionDebug($"RPG action failed: {actionName}, reason={reason}");
        }

        private void NotifyActionError(string actionName)
        {
            string actionLabel = GetRpgActionLabel(actionName);
            AddActionFeedback("RimChat_RPGActionToast_Error".Translate(actionLabel), ActionErrorColor);
            Log.Warning($"[RimChat] RPG action error: {actionName}");
        }

        private string GetRpgActionLabel(string actionName)
        {
            string key = $"RimChat_RPGActionLabel_{actionName}";
            TaggedString translated = key.Translate();
            return translated.RawText == key ? actionName : translated.RawText;
        }

        private void AddActionFeedback(string text, Color color, float duration = ActionFeedbackDefaultDuration)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            actionFeedbackEntries.Add(new ActionFeedbackEntry
            {
                Text = text,
                Color = color,
                Duration = duration,
                CreatedAt = Time.realtimeSinceStartup
            });

            if (actionFeedbackEntries.Count > ActionFeedbackMaxCount)
            {
                actionFeedbackEntries.RemoveAt(0);
            }
        }

        private void AddSystemFeedback(string text, float duration = 4.2f)
        {
            AddActionFeedback(text, ActionInfoColor, duration);
        }

        private void DrawActionFeedback(Rect contentRect)
        {
            RemoveExpiredActionFeedback();
            if (actionFeedbackEntries.Count == 0)
            {
                return;
            }

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Text.Anchor = TextAnchor.UpperRight;
            Text.Font = GameFont.Tiny;

            float lineHeight = 18f;
            float width = Mathf.Min(460f, contentRect.width * 0.45f);
            float panelHeight = actionFeedbackEntries.Count * lineHeight + 8f;
            Rect panelRect = new Rect(contentRect.xMax - width - 8f, contentRect.y + 2f, width + 8f, panelHeight);
            Widgets.DrawBoxSolid(panelRect, new Color(0.05f, 0.07f, 0.09f, 0.6f));

            float y = panelRect.y + 4f;

            for (int i = actionFeedbackEntries.Count - 1; i >= 0; i--)
            {
                ActionFeedbackEntry entry = actionFeedbackEntries[i];
                float age = Time.realtimeSinceStartup - entry.CreatedAt;
                float alpha = Mathf.Clamp01(1f - age / entry.Duration);
                GUI.color = new Color(entry.Color.r, entry.Color.g, entry.Color.b, alpha);
                Rect rect = new Rect(contentRect.xMax - width - 4f, y, width, lineHeight);
                Widgets.Label(rect, entry.Text);
                y += lineHeight;
            }

            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            GUI.color = Color.white;
        }

        private void RemoveExpiredActionFeedback()
        {
            float now = Time.realtimeSinceStartup;
            actionFeedbackEntries.RemoveAll(entry => now - entry.CreatedAt > entry.Duration);
        }

    }
}


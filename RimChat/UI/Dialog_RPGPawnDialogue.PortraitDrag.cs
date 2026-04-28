using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>Dependencies: Dialog_RPGPawnDialogue portrait layout, pawn fields, RPG action execution.
    /// Responsibility: draggable initiator portrait with spring physics, collision detection, and dynamic action menu.
    ///</summary>
    public partial class Dialog_RPGPawnDialogue
    {
        // ── drag state ──
        private bool isDraggingInitiator;
        private bool isSpringReturning;
        private Vector2 dragCenter;       // current portrait center position
        private Vector2 dragVelocity;     // spring velocity
        private Vector2 restCenter;       // target rest position
        private float dragScale = 1f;
        private float dragScaleVel;

        // ── collision state ──
        private bool collisionTriggeredThisDrag;
        private float collisionShakeTime;
        private float collisionFlashAlpha;
        private const float CollisionShakeDuration = 0.35f;
        private const float CollisionShakeMagnitude = 8f;
        private const float CollisionFlashPeak = 0.6f;

        // ── physics constants ──
        private const float SpringConstant = 10f;
        private const float SpringDamping = 5f;
        private const float DragFollowSpeed = 18f;
        private const float ScaleSpeed = 8f;
        private const float CollisionOverlapThreshold = 0.30f;

        // ── dynamic collision menu ──
        private static readonly (string labelKey, string actionName, Func<Pawn, Pawn, bool> canShow)[] CollisionMenuActions =
        {
            ("RimChat_DragMenu_Romance",      "RomanceAttempt",      (t, i) => !HasLoveRelation(t, i)),
            ("RimChat_DragMenu_Marry",        "MarriageProposal",    (t, i) => !HasSpouseRelation(t, i)),
            ("RimChat_DragMenu_Breakup",      "Breakup",             (t, i) => HasLoveRelation(t, i)),
            ("RimChat_DragMenu_Divorce",      "Divorce",             (t, i) => HasSpouseRelation(t, i)),
            ("RimChat_DragMenu_Date",         "Date",                (t, i) => true),
            ("RimChat_DragMenu_Gift",         "TryGainMemory",       (t, i) => true),
            ("RimChat_DragMenu_ReduceResist", "ReduceResistance",    (t, i) => t.IsPrisoner),
            ("RimChat_DragMenu_ReduceWill",   "ReduceWill",          (t, i) => t.IsPrisoner),
            ("RimChat_DragMenu_Recruit",      "Recruit",             (t, i) => t.Faction != Faction.OfPlayer),
            ("RimChat_DragMenu_Inspiration",  "GrantInspiration",    (t, i) => true),
            ("RimChat_DragMenu_Incident",     "TriggerIncident",     (t, i) => true),
        };

        private static bool HasLoveRelation(Pawn target, Pawn initiator)
        {
            return target?.relations?.DirectRelationExists(PawnRelationDefOf.Lover, initiator) == true
                || target?.relations?.DirectRelationExists(PawnRelationDefOf.Fiance, initiator) == true;
        }

        private static bool HasSpouseRelation(Pawn target, Pawn initiator)
        {
            return target?.relations?.DirectRelationExists(PawnRelationDefOf.Spouse, initiator) == true;
        }

        // ── portrait drag integration ──

        private Rect GetInitiatorDragRect(Rect inRect)
        {
            if (isDraggingInitiator || isSpringReturning)
            {
                float w = PortraitWidth * dragScale;
                float h = PortraitHeight * dragScale;
                return new Rect(dragCenter.x - w * 0.5f, dragCenter.y - h * 0.5f, w, h);
            }

            return GetInitiatorPortraitRect(inRect);
        }

        private void UpdatePortraitDrag(Rect inRect, float deltaTime)
        {
            Rect originalRect = GetInitiatorPortraitRect(inRect);
            Vector2 originalCenter = originalRect.center;
            restCenter = originalCenter;

            if (!isDraggingInitiator && !isSpringReturning)
            {
                dragCenter = originalCenter;
                dragVelocity = Vector2.zero;
                dragScale = 1f;
                return;
            }

            Event e = Event.current;
            Vector2 mousePos = e.mousePosition;

            if (isDraggingInitiator)
            {
                // Spring-follow the mouse
                Vector2 targetPos = mousePos;
                Vector2 force = (targetPos - dragCenter) * DragFollowSpeed;
                force -= dragVelocity * SpringDamping * 0.5f;
                dragVelocity += force * deltaTime;
                dragCenter += dragVelocity * deltaTime;

                float targetScale = 1.08f;
                dragScale = Mathf.SmoothDamp(dragScale, targetScale, ref dragScaleVel, 1f / ScaleSpeed, 99f, deltaTime);

                // Check for mouse release
                if (e.type == EventType.MouseUp || e.type == EventType.MouseDrag && !Input.GetMouseButton(0))
                {
                    isDraggingInitiator = false;
                    isSpringReturning = true;
                    collisionTriggeredThisDrag = false;
                }
            }
            else if (isSpringReturning)
            {
                // Spring back to rest position
                Vector2 force = (restCenter - dragCenter) * SpringConstant;
                force -= dragVelocity * SpringDamping;
                dragVelocity += force * deltaTime;
                dragCenter += dragVelocity * deltaTime;

                float targetScale = 1f;
                dragScale = Mathf.SmoothDamp(dragScale, targetScale, ref dragScaleVel, 1f / ScaleSpeed, 99f, deltaTime);

                // Settle at rest
                float dist = Vector2.Distance(dragCenter, restCenter);
                if (dist < 1f && dragVelocity.magnitude < 2f)
                {
                    dragCenter = restCenter;
                    dragVelocity = Vector2.zero;
                    dragScale = 1f;
                    isSpringReturning = false;
                }
            }

            // Collision detection
            UpdateCollisionDetection(inRect);

            // Collision animation
            UpdateCollisionAnimation(deltaTime);
        }

        private void UpdateCollisionDetection(Rect inRect)
        {
            if (collisionTriggeredThisDrag || !isDraggingInitiator)
                return;

            Rect targetRect = GetTargetPortraitRect(inRect);
            Rect initiatorRect = GetInitiatorDragRect(inRect);

            if (!initiatorRect.Overlaps(targetRect))
                return;

            Rect overlap = RectIntersect(initiatorRect, targetRect);
            float initiatorArea = initiatorRect.width * initiatorRect.height;
            float overlapArea = overlap.width * overlap.height;
            float ratio = overlapArea / initiatorArea;

            if (ratio >= CollisionOverlapThreshold)
            {
                collisionTriggeredThisDrag = true;
                collisionFlashAlpha = CollisionFlashPeak;
                collisionShakeTime = CollisionShakeDuration;
                ShowCollisionMenu();
            }
        }

        private void UpdateCollisionAnimation(float deltaTime)
        {
            if (collisionShakeTime > 0f)
            {
                collisionShakeTime -= deltaTime;
                collisionFlashAlpha = Mathf.Max(0f, collisionFlashAlpha - deltaTime * 2f);
            }
        }

        private Rect RectIntersect(Rect a, Rect b)
        {
            float x = Mathf.Max(a.xMin, b.xMin);
            float y = Mathf.Max(a.yMin, b.yMin);
            float w = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - x);
            float h = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - y);
            return new Rect(x, y, w, h);
        }

        // ── drawing ──

        private void DrawInitiatorPortraitWithDrag(Rect inRect)
        {
            Rect drawRect = GetInitiatorDragRect(inRect);
            Rect originalRect = GetInitiatorPortraitRect(inRect);

            // Collision shake offset
            Vector2 shakeOffset = Vector2.zero;
            if (collisionShakeTime > 0f)
            {
                float intensity = collisionShakeTime / CollisionShakeDuration;
                float mag = CollisionShakeMagnitude * intensity;
                shakeOffset = new Vector2(
                    (float)((Verse.Rand.Value - 0.5) * 2.0 * mag),
                    (float)((Verse.Rand.Value - 0.5) * 2.0 * mag));
            }

            Rect shakenRect = new Rect(
                drawRect.x + shakeOffset.x,
                drawRect.y + shakeOffset.y,
                drawRect.width,
                drawRect.height);

            // Flash overlay
            float baseAlpha = globalFadeAlpha * initiatorFadeAlpha;
            Color portraitColor = Color.white;
            if (collisionFlashAlpha > 0.01f)
            {
                float flash = collisionFlashAlpha * Mathf.Clamp01(collisionShakeTime / (CollisionShakeDuration * 0.5f));
                portraitColor = Color.Lerp(Color.white, new Color(1f, 0.85f, 0.3f, 1f), flash);
            }

            // Draw drag shadow on top of the original position
            if (isDraggingInitiator || isSpringReturning)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.15f * baseAlpha);
                DrawPawnPortrait(new Rect(originalRect.x + 4f, originalRect.y + 4f, originalRect.width, originalRect.height), initiator, true);
            }

            // Draw the dynamic portrait
            GUI.color = new Color(portraitColor.r, portraitColor.g, portraitColor.b, baseAlpha);
            DrawPawnPortrait(shakenRect, initiator, true);

            GUI.color = new Color(1f, 1f, 1f, globalFadeAlpha);
        }

        // ── input ──

        private bool TryStartInitiatorDrag(Rect inRect)
        {
            if (isDraggingInitiator || isSpringReturning)
                return false;

            Rect initiatorRect = GetInitiatorPortraitRect(inRect);
            if (!initiatorRect.Contains(Event.current.mousePosition))
                return false;

            isDraggingInitiator = true;
            isSpringReturning = false;
            collisionTriggeredThisDrag = false;
            dragCenter = initiatorRect.center;
            dragVelocity = Vector2.zero;
            dragScale = 1f;
            Event.current.Use();
            return true;
        }

        // ── collision menu ──

        private void ShowCollisionMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (var (labelKey, actionName, canShow) in CollisionMenuActions)
            {
                if (!canShow(target, initiator))
                    continue;

                string capturedAction = actionName;
                options.Add(new FloatMenuOption(labelKey.Translate(), () =>
                {
                    ExecuteCollisionAction(capturedAction);
                }));
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }

            // Auto-release after showing menu
            isDraggingInitiator = false;
            isSpringReturning = true;
        }

        private void ExecuteCollisionAction(string actionName)
        {
            var action = new AI.LLMRpgApiResponse.ApiAction { action = actionName };

            // Provide required parameters for actions that need them
            switch (actionName)
            {
                case "TryGainMemory":
                    action.defName = "RimChat_BriefJoy";
                    break;
                case "ReduceResistance":
                case "ReduceWill":
                    action.amount = 10;
                    break;
                case "TriggerIncident":
                    action.defName = "RaidEnemy"; // fallback; may be replaced by a more appropriate def
                    break;
            }

            bool success = ExecuteRpgAction(action);
            if (success && !string.Equals(actionName, "TryGainMemory", StringComparison.Ordinal))
            {
                NotifyActionSuccess(actionName, action);
            }
        }
    }
}

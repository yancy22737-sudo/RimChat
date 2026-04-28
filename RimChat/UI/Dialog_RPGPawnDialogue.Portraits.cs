using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: dialogue window layout constants and pawn portrait renderer.
 /// Responsibility: centralize PawnRPG portrait layout so other overlays can share the same anchors.
 ///</summary>
    public partial class Dialog_RPGPawnDialogue
    {
        private const float PortraitHorizontalMargin = 50f;
        private const float PortraitVerticalOverlap = 150f;

        private void DrawPortraits(Rect inRect)
        {
            Rect targetPortraitRect = GetTargetPortraitRect(inRect);
            GUI.color = new Color(1f, 1f, 1f, globalFadeAlpha * targetFadeAlpha * inspectPaneAlpha);
            DrawPawnPortrait(targetPortraitRect, target, false);

            Rect initiatorPortraitRect = GetInitiatorPortraitRect(inRect);
            GUI.color = new Color(1f, 1f, 1f, globalFadeAlpha * initiatorFadeAlpha);
            DrawPawnPortrait(initiatorPortraitRect, initiator, true);

            GUI.color = new Color(1f, 1f, 1f, globalFadeAlpha);
        }

        private Rect GetTargetPortraitRect(Rect inRect)
        {
            return new Rect(
                PortraitHorizontalMargin,
                GetPortraitTopY(inRect),
                PortraitWidth,
                PortraitHeight);
        }

        private Rect GetInitiatorPortraitRect(Rect inRect)
        {
            return new Rect(
                inRect.width - PortraitWidth - PortraitHorizontalMargin,
                GetPortraitTopY(inRect),
                PortraitWidth,
                PortraitHeight);
        }

        private float GetPortraitTopY(Rect inRect)
        {
            return inRect.height - DialogueBoxHeight - PortraitHeight + PortraitVerticalOverlap;
        }
    }
}

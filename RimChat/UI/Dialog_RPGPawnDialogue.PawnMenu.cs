using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>Dependencies: Dialog_RPGPawnDialogue pawn fields (initiator, target), vanilla ITab_Pawn_* and InspectPaneUtility.
    /// Responsibility: interactive pawn name label with measured click area, FloatMenu, inspect-pane-aware interaction.
    ///</summary>
    public partial class Dialog_RPGPawnDialogue
    {
        private float inspectPaneAlpha = 1f;
        private bool inspectPaneOpenedByMenu;
        private const float InspectPaneAlphaSpeed = 5f;

        private static readonly (string labelKey, Type itabType)[] PawnMenuOptions =
        {
            ("RimChat_PawnMenu_Log",       typeof(ITab_Pawn_Log)),
            ("RimChat_PawnMenu_Gear",      typeof(ITab_Pawn_Gear)),
            ("RimChat_PawnMenu_Social",    typeof(ITab_Pawn_Social)),
            ("RimChat_PawnMenu_Character", typeof(ITab_Pawn_Character)),
            ("RimChat_PawnMenu_Needs",     typeof(ITab_Pawn_Needs)),
            ("RimChat_PawnMenu_Health",    typeof(ITab_Pawn_Health)),
        };

        /// <summary>True only when the inspect pane was opened through our FloatMenu and is still active.</summary>
        private bool IsInspectPaneShowing()
        {
            if (!inspectPaneOpenedByMenu)
                return false;

            if (Find.MainTabsRoot.OpenTab != MainButtonDefOf.Inspect)
            {
                inspectPaneOpenedByMenu = false;
                return false;
            }

            if (Find.Selector.NumSelected == 0)
            {
                inspectPaneOpenedByMenu = false;
                return false;
            }

            return true;
        }

        /// <summary>Left half of screen above the dialogue box — simple, generous hover zone.</summary>
        private Rect GetInspectPaneOverlapRect()
        {
            float dialogueTop = Verse.UI.screenHeight - DialogueBoxHeight;
            float width = Verse.UI.screenWidth * 0.5f;
            return new Rect(0f, 0f, width, dialogueTop);
        }

        private void DrawPawnNameWithMenu(Rect nameRect, Pawn pawn, string displayName, bool rightAligned)
        {
            bool hovered = Mouse.IsOver(nameRect);

            // Warm gold highlight on hover to indicate interactivity
            Color nameColor = hovered ? new Color(1f, 0.92f, 0.55f, 1f) : new Color(0.88f, 0.88f, 0.88f, 1f);
            string colorHex = ColorUtility.ToHtmlStringRGB(nameColor);

            // Measure text to narrow click area to just the name width
            GameFont prevFont = Text.Font;
            Text.Font = GameFont.Medium;
            Vector2 textSize = Text.CalcSize(displayName);
            Text.Font = prevFont;
            float measuredWidth = textSize.x * 2.1f + 20f;

            Rect clickRect;
            if (rightAligned)
            {
                Text.Anchor = TextAnchor.UpperRight;
                clickRect = new Rect(nameRect.xMax - measuredWidth, nameRect.y, measuredWidth, nameRect.height);
            }
            else
            {
                clickRect = new Rect(nameRect.x, nameRect.y, measuredWidth, nameRect.height);
            }

            Widgets.Label(nameRect, $"<size=44><b><color=#{colorHex}>{displayName}</color></b></size>");
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonInvisible(clickRect))
            {
                ShowPawnMenu(pawn);
            }

            if (hovered)
            {
                TooltipHandler.TipRegion(clickRect, "RimChat_PawnMenu_HoverTooltip".Translate());
            }
        }

        private void ShowPawnMenu(Pawn pawn)
        {
            var self = this;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (var (labelKey, itabType) in PawnMenuOptions)
            {
                Type capturedType = itabType;
                options.Add(new FloatMenuOption(labelKey.Translate(), () =>
                {
                    self.OpenPawnTab(pawn, capturedType);
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenPawnTab(Pawn pawn, Type itabType)
        {
            Find.Selector.ClearSelection();
            Find.Selector.Select(pawn);
            Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, true);
            InspectPaneUtility.OpenTab(itabType);
            inspectPaneOpenedByMenu = true;
        }
    }
}

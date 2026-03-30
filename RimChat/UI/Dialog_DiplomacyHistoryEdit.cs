using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: Verse text widgets and diplomacy-history validation callbacks.
    /// Responsibility: collect one edited dialogue-history message and fail fast on empty saves.
    /// </summary>
    public sealed class Dialog_DiplomacyHistoryEdit : Window
    {
        private readonly Action<string> onSave;
        private string messageText;

        public override Vector2 InitialSize => new Vector2(640f, 360f);

        public Dialog_DiplomacyHistoryEdit(string initialText, Action<string> onSave)
        {
            this.onSave = onSave;
            messageText = initialText ?? string.Empty;
            draggable = true;
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            DrawTitle(new Rect(inRect.x, inRect.y, inRect.width, 30f));
            DrawEditor(new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 88f));
            DrawButtons(new Rect(inRect.x, inRect.yMax - 32f, inRect.width, 28f));
        }

        private void DrawTitle(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(rect, "RimChat_DiplomacyHistoryEditTitle".Translate());
            Text.Font = GameFont.Small;
        }

        private void DrawEditor(Rect rect)
        {
            GUI.color = new Color(0.84f, 0.88f, 0.94f, 0.96f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "RimChat_DiplomacyHistoryEditLabel".Translate());
            GUI.color = Color.white;
            messageText = Widgets.TextArea(new Rect(rect.x, rect.y + 28f, rect.width, rect.height - 28f), messageText ?? string.Empty);
        }

        private void DrawButtons(Rect rect)
        {
            Rect cancelRect = new Rect(rect.xMax - 248f, rect.y, 116f, rect.height);
            Rect saveRect = new Rect(rect.xMax - 124f, rect.y, 116f, rect.height);
            if (Widgets.ButtonText(cancelRect, "RimChat_CancelButton".Translate()))
            {
                Close();
                return;
            }

            if (!Widgets.ButtonText(saveRect, "RimChat_SaveButton".Translate()))
            {
                return;
            }

            string trimmed = messageText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                Messages.Message("RimChat_DiplomacyHistoryEditEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            onSave?.Invoke(trimmed);
            Close();
        }
    }
}

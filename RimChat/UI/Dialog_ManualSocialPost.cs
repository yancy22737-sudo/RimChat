using System;
using RimChat.DiplomacySystem;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: manual social-post publish limits and Verse window/input widgets.
    /// Responsibility: collect the minimal title/body payload for a player-authored social-circle post.
    /// </summary>
    public sealed class Dialog_ManualSocialPost : Window
    {
        private readonly Action<string, string> onSubmit;
        private string titleText = string.Empty;
        private string bodyText = string.Empty;

        public override Vector2 InitialSize => new Vector2(640f, 420f);

        public Dialog_ManualSocialPost(Action<string, string> onSubmit)
        {
            this.onSubmit = onSubmit;
            draggable = true;
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            const float bottomBarHeight = 36f;
            const float bottomGap = 12f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "RimChat_ManualSocialPostWindowTitle".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + 42f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "RimChat_ManualSocialPostTitleLabel".Translate());
            y += 26f;

            string editedTitle = Widgets.TextField(
                new Rect(inRect.x, y, inRect.width, 30f),
                titleText ?? string.Empty);
            titleText = TrimToLimit(editedTitle, GameComponent_DiplomacyManager.ManualSocialPostTitleMaxLength);
            y += 42f;

            Widgets.Label(
                new Rect(inRect.x, y, inRect.width, 24f),
                "RimChat_ManualSocialPostBodyLabel".Translate());
            y += 26f;

            float reservedBottom = bottomBarHeight + bottomGap + 28f;
            Rect bodyRect = new Rect(inRect.x, y, inRect.width, Mathf.Max(120f, inRect.yMax - y - reservedBottom));
            string editedBody = Widgets.TextArea(bodyRect, bodyText ?? string.Empty);
            bodyText = TrimToLimit(editedBody, GameComponent_DiplomacyManager.ManualSocialPostBodyMaxLength);

            Rect hintRect = new Rect(inRect.x, bodyRect.yMax + 6f, inRect.width, 20f);
            GUI.color = new Color(0.8f, 0.82f, 0.88f);
            Widgets.Label(
                hintRect,
                "RimChat_ManualSocialPostLengthHint".Translate(
                    GameComponent_DiplomacyManager.ManualSocialPostTitleMaxLength,
                    GameComponent_DiplomacyManager.ManualSocialPostBodyMaxLength));
            GUI.color = Color.white;

            float buttonY = inRect.yMax - bottomBarHeight;
            Rect cancelRect = new Rect(inRect.xMax - 248f, buttonY, 116f, 28f);
            Rect publishRect = new Rect(inRect.xMax - 124f, buttonY, 116f, 28f);
            if (Widgets.ButtonText(cancelRect, "RimChat_CloseButton".Translate()))
            {
                Close();
                return;
            }

            if (!Widgets.ButtonText(publishRect, "RimChat_ManualSocialPostPublishButton".Translate()))
            {
                return;
            }

            string validationError = Validate();
            if (!string.IsNullOrEmpty(validationError))
            {
                Messages.Message(validationError, MessageTypeDefOf.RejectInput, false);
                return;
            }

            onSubmit?.Invoke(titleText.Trim(), bodyText.Trim());
            Close();
        }

        private string Validate()
        {
            if (string.IsNullOrWhiteSpace(titleText))
            {
                return "RimChat_ManualSocialPostMissingTitle".Translate();
            }

            if (string.IsNullOrWhiteSpace(bodyText))
            {
                return "RimChat_ManualSocialPostMissingBody".Translate();
            }

            if ((titleText?.Trim().Length ?? 0) > GameComponent_DiplomacyManager.ManualSocialPostTitleMaxLength)
            {
                return "RimChat_ManualSocialPostTitleTooLong".Translate(GameComponent_DiplomacyManager.ManualSocialPostTitleMaxLength);
            }

            if ((bodyText?.Trim().Length ?? 0) > GameComponent_DiplomacyManager.ManualSocialPostBodyMaxLength)
            {
                return "RimChat_ManualSocialPostBodyTooLong".Translate(GameComponent_DiplomacyManager.ManualSocialPostBodyMaxLength);
            }

            return string.Empty;
        }

        private static string TrimToLimit(string text, int maxLength)
        {
            string value = text ?? string.Empty;
            if (value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength);
        }
    }
}

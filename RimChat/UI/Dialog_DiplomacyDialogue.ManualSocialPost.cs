using RimChat.DiplomacySystem;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: diplomacy manager manual social-post API and the dialogue social-circle tab.
    /// Responsibility: bridge the social-circle toolbar button to the manual post compose window.
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private void OpenManualSocialPostDialog()
        {
            if (Find.WindowStack == null)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_ManualSocialPost(HandleManualSocialPostSubmitted));
        }

        private void HandleManualSocialPostSubmitted(string title, string body)
        {
            GameComponent_DiplomacyManager manager = GameComponent_DiplomacyManager.Instance;
            if (manager == null)
            {
                Messages.Message("RimChat_SocialUnavailable".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            ManualSocialPostResult result = manager.TryPublishManualPlayerSocialPost(title, body);
            if (!result.Success)
            {
                string reason = GameComponent_DiplomacyManager.GetManualSocialPostFailureReasonLabel(result.FailureReason);
                Messages.Message("RimChat_ManualSocialPostPublishFailed".Translate(reason), MessageTypeDefOf.RejectInput, false);
                return;
            }

            ShowSocialToast("RimChat_ManualSocialPostPublishToast".Translate(result.TriggeredFactionCount));
            Messages.Message("RimChat_ManualSocialPostPublishSuccess".Translate(result.TriggeredFactionCount), MessageTypeDefOf.PositiveEvent, false);
            socialReadMarked = false;
            socialPostScrollPosition = Vector2.zero;
        }
    }
}

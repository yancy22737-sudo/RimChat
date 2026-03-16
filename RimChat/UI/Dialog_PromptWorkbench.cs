using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: RimChatSettings prompt workbench renderer.
    /// Responsibility: host prompt workbench in a dedicated large standalone window.
    /// </summary>
    public sealed class Dialog_PromptWorkbench : Window
    {
        private readonly RimChat.Config.RimChatSettings settings;

        public override Vector2 InitialSize
        {
            get
            {
                float width = Mathf.Min(Screen.width * 0.9f, 1200f);
                float height = Mathf.Min(Screen.height * 0.9f, 800f);
                return new Vector2(width, height);
            }
        }

        public Dialog_PromptWorkbench(RimChat.Config.RimChatSettings settingsRef)
        {
            settings = settingsRef;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            doCloseX = true;
            doCloseButton = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (settings == null)
            {
                Widgets.Label(inRect, "RimChat_PromptRenderFailed".Translate());
                return;
            }

            settings.DrawPromptWorkbenchWindow(inRect);
        }
    }
}

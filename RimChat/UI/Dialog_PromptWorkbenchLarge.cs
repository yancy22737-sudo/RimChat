using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: RimChat prompt section workspace renderer and RimWorld window stack lifecycle.
    /// Responsibility: host prompt workspace in a dedicated large-size window for stable editing space.
    /// </summary>
    public sealed class Dialog_PromptWorkbenchLarge : Window
    {
        private readonly RimChat.Config.RimChatSettings _settings;

        public override Vector2 InitialSize => ResolveInitialSize();

        internal Dialog_PromptWorkbenchLarge(RimChat.Config.RimChatSettings settings)
        {
            _settings = settings;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            doCloseX = true;
            doCloseButton = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (_settings == null)
            {
                Widgets.Label(inRect, "RimChat_PromptRenderFailed".Translate());
                return;
            }

            _settings.DrawTab_PromptSettingsDirect(inRect);
        }

        public override void PreClose()
        {
            _settings?.FlushPromptWorkspaceEdits();
            base.PreClose();
        }

        private static Vector2 ResolveInitialSize()
        {
            float width = Mathf.Clamp(Verse.UI.screenWidth * 0.9f, 1220f, 1580f);
            float height = Mathf.Clamp(Verse.UI.screenHeight * 0.9f, 760f, 960f);
            return new Vector2(width, height);
        }
    }
}

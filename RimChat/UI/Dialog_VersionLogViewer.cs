using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: Verse window stack and Unity GUI widgets.
    /// Responsibility: show version-log text in a scrollable read-only window.
    /// </summary>
    public class Dialog_VersionLogViewer : Window
    {
        private readonly string titleText;
        private readonly string contentText;
        private Vector2 scrollPosition = Vector2.zero;

        public Dialog_VersionLogViewer(string title, string content)
        {
            titleText = string.IsNullOrWhiteSpace(title)
                ? "RimChat_VersionLogWindowTitle".Translate().ToString()
                : title;
            contentText = content ?? string.Empty;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            doCloseX = true;
            doCloseButton = false;
        }

        public override Vector2 InitialSize => new Vector2(980f, 700f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 32f);
            Widgets.Label(titleRect, titleText);
            Text.Font = GameFont.Small;

            Rect bodyRect = new Rect(inRect.x, titleRect.yMax + 8f, inRect.width, inRect.height - 76f);
            Widgets.DrawMenuSection(bodyRect);
            Rect viewportRect = bodyRect.ContractedBy(6f);
            float contentWidth = Mathf.Max(100f, viewportRect.width - 16f);
            float contentHeight = Mathf.Max(viewportRect.height, Text.CalcHeight(contentText, contentWidth) + 8f);
            Rect viewRect = new Rect(0f, 0f, contentWidth, contentHeight);
            Widgets.BeginScrollView(viewportRect, ref scrollPosition, viewRect);
            Widgets.Label(new Rect(0f, 0f, viewRect.width, viewRect.height), contentText);
            Widgets.EndScrollView();

            Rect closeRect = new Rect(inRect.xMax - 120f, inRect.yMax - 30f, 120f, 28f);
            if (Widgets.ButtonText(closeRect, "RimChat_CloseButton".Translate()))
            {
                Close();
            }
        }
    }
}


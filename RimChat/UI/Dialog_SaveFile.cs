using System;
using System.IO;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    public class Dialog_SaveFile : Window
    {
        private string _filePath;
        private readonly Action<string> _onSave;

        public override Vector2 InitialSize => new Vector2(500f, 140f);

        public Dialog_SaveFile(string defaultPath, Action<string> onSave)
        {
            _filePath = defaultPath;
            _onSave = onSave;
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 标题
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.7f, 0.4f);
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 28f), "RimChat_SaveFileTitle".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            float y = 35f;

            // Filepathlabel
            Widgets.Label(new Rect(inRect.x, y, 80f, 24f), "RimChat_FilePathLabel".Translate());

            // Filepathinput框
            Rect pathRect = new Rect(inRect.x + 85f, y, inRect.width - 100f, 24f);
            _filePath = Widgets.TextField(pathRect, _filePath);

            y += 35f;

            // Button行
            float btnWidth = 100f;
            float btnY = inRect.yMax - 35f;

            // Savebutton
            Rect saveRect = new Rect(inRect.xMax - btnWidth * 2 - 10f, btnY, btnWidth, 30f);
            GUI.color = new Color(0.3f, 0.7f, 0.3f);
            if (Widgets.ButtonText(saveRect, "RimChat_SaveButton".Translate()))
            {
                if (ValidatePath(_filePath))
                {
                    _onSave?.Invoke(_filePath);
                    Close();
                }
            }
            GUI.color = Color.white;

            // 取消button
            Rect cancelRect = new Rect(inRect.xMax - btnWidth, btnY, btnWidth, 30f);
            if (Widgets.ButtonText(cancelRect, "RimChat_CancelButton".Translate()))
            {
                Close();
            }

            // 提示text
            y += 5f;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f), $"Default: Desktop");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private bool ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Messages.Message("File path cannot be empty", MessageTypeDefOf.NegativeEvent);
                return false;
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                return true;
            }
            catch (Exception ex)
            {
                Messages.Message($"Invalid path: {ex.Message}", MessageTypeDefOf.NegativeEvent);
                return false;
            }
        }
    }
}

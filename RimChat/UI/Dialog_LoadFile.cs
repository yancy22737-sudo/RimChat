using System;
using System.IO;
using System.Reflection;
using System.Threading;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    public class Dialog_LoadFile : Window
    {
        private string _filePath;
        private readonly Action<string> _onLoad;

        public override Vector2 InitialSize => new Vector2(640f, 180f);

        public Dialog_LoadFile(string defaultPath, Action<string> onLoad)
        {
            _filePath = defaultPath;
            _onLoad = onLoad;
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.7f, 0.4f);
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 28f), "RimChat_LoadFileTitle".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            float y = inRect.y + 40f;
            float labelWidth = 92f;
            float browseWidth = 108f;
            float gap = 8f;

            Widgets.Label(new Rect(inRect.x, y, labelWidth, 24f), "RimChat_FilePathLabel".Translate());

            float pathWidth = inRect.width - labelWidth - browseWidth - (gap * 2f);
            Rect pathRect = new Rect(inRect.x + labelWidth + gap, y, pathWidth, 24f);
            _filePath = Widgets.TextField(pathRect, _filePath);
            Rect browseRect = new Rect(pathRect.xMax + gap, y, browseWidth, 24f);
            if (Widgets.ButtonText(browseRect, "RimChat_BrowseButton".Translate()))
            {
                TryBrowseFilePath();
            }

            y += 34f;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f), "RimChat_DefaultDesktopHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            float btnWidth = 120f;
            float btnGap = 10f;
            float btnY = inRect.yMax - 36f;
            Rect loadRect = new Rect(inRect.xMax - (btnWidth * 2f) - btnGap, btnY, btnWidth, 30f);
            GUI.color = new Color(0.3f, 0.6f, 0.9f);
            if (Widgets.ButtonText(loadRect, "RimChat_LoadButton".Translate()))
            {
                if (ValidateFile(_filePath))
                {
                    _onLoad?.Invoke(_filePath);
                    Close();
                }
            }
            GUI.color = Color.white;

            Rect cancelRect = new Rect(inRect.xMax - btnWidth, btnY, btnWidth, 30f);
            if (Widgets.ButtonText(cancelRect, "RimChat_CancelButton".Translate()))
            {
                Close();
            }
        }

        private bool ValidateFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Messages.Message("RimChat_FilePathEmpty".Translate(), MessageTypeDefOf.NegativeEvent);
                return false;
            }

            if (!File.Exists(path))
            {
                Messages.Message("RimChat_FileNotFound".Translate(path), MessageTypeDefOf.NegativeEvent);
                return false;
            }

            return true;
        }

        private void TryBrowseFilePath()
        {
            if (TryOpenNativeJsonFileDialog(_filePath, out string selectedPath, out string errorMessage))
            {
                if (!string.IsNullOrWhiteSpace(selectedPath))
                {
                    _filePath = selectedPath;
                }

                return;
            }

            string directory = ResolveBrowseDirectory(_filePath);
            if (TryOpenDirectoryInExplorer(directory, out string openDirError))
            {
                Messages.Message(
                    "RimChat_FilePickerFallbackOpened".Translate(directory),
                    MessageTypeDefOf.NeutralEvent,
                    false);
                return;
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                Messages.Message("RimChat_InvalidPath".Translate(errorMessage), MessageTypeDefOf.NegativeEvent);
                return;
            }

            if (!string.IsNullOrWhiteSpace(openDirError))
            {
                Messages.Message("RimChat_InvalidPath".Translate(openDirError), MessageTypeDefOf.NegativeEvent);
                return;
            }

            Messages.Message("RimChat_FilePickerUnavailable".Translate(), MessageTypeDefOf.NegativeEvent);
        }

        private static bool TryOpenNativeJsonFileDialog(
            string currentPath,
            out string selectedPath,
            out string errorMessage)
        {
            selectedPath = string.Empty;
            errorMessage = string.Empty;
            string threadSelectedPath = string.Empty;
            string threadError = string.Empty;
            bool handled = false;

            var thread = new Thread(() =>
            {
                handled = TryOpenNativeJsonFileDialogCore(currentPath, out threadSelectedPath, out threadError);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            selectedPath = threadSelectedPath;
            errorMessage = threadError;
            return handled;
        }

        private static bool TryOpenNativeJsonFileDialogCore(
            string currentPath,
            out string selectedPath,
            out string errorMessage)
        {
            selectedPath = string.Empty;
            errorMessage = string.Empty;

            try
            {
                Assembly formsAssembly = Assembly.Load("System.Windows.Forms");
                Type dialogType = formsAssembly?.GetType("System.Windows.Forms.OpenFileDialog", false);
                if (dialogType == null)
                {
                    return false;
                }

                using (IDisposable dialog = Activator.CreateInstance(dialogType) as IDisposable)
                {
                    if (dialog == null)
                    {
                        errorMessage = "OpenFileDialog unavailable.";
                        return false;
                    }

                    ApplyOpenFileDialogDefaults(dialogType, dialog, currentPath);
                    object result = dialogType.GetMethod("ShowDialog", Type.EmptyTypes)?.Invoke(dialog, null);
                    if (!IsDialogResultOk(result))
                    {
                        return true;
                    }

                    selectedPath = ReadStringProperty(dialogType, dialog, "FileName");
                    return true;
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.GetBaseException()?.Message ?? ex.Message;
                return false;
            }
        }

        private static void ApplyOpenFileDialogDefaults(Type dialogType, object dialog, string currentPath)
        {
            SetProperty(dialogType, dialog, "Filter", "JSON files (*.json)|*.json|All files (*.*)|*.*");
            SetProperty(dialogType, dialog, "FilterIndex", 1);
            SetProperty(dialogType, dialog, "CheckFileExists", true);
            SetProperty(dialogType, dialog, "CheckPathExists", true);
            SetProperty(dialogType, dialog, "RestoreDirectory", true);

            string seedPath = string.IsNullOrWhiteSpace(currentPath) ? string.Empty : currentPath.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(seedPath))
            {
                return;
            }

            string initialDirectory = File.Exists(seedPath)
                ? Path.GetDirectoryName(seedPath)
                : (Directory.Exists(seedPath) ? seedPath : Path.GetDirectoryName(seedPath));
            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            {
                SetProperty(dialogType, dialog, "InitialDirectory", initialDirectory);
            }

            string fileName = Path.GetFileName(seedPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                SetProperty(dialogType, dialog, "FileName", fileName);
            }
        }

        private static bool TryOpenDirectoryInExplorer(string directory, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            try
            {
                string normalizedDir = directory.Replace('\\', '/');
                string uri = "file:///" + normalizedDir.TrimStart('/');
                Application.OpenURL(uri);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.GetBaseException()?.Message ?? ex.Message;
                return false;
            }
        }

        private static string ResolveBrowseDirectory(string currentPath)
        {
            string seedPath = currentPath?.Trim().Trim('"') ?? string.Empty;
            if (File.Exists(seedPath))
            {
                return Path.GetDirectoryName(seedPath) ?? GetDesktopDirectory();
            }

            if (Directory.Exists(seedPath))
            {
                return seedPath;
            }

            string parent = Path.GetDirectoryName(seedPath);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                return parent;
            }

            return GetDesktopDirectory();
        }

        private static string GetDesktopDirectory()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        private static bool IsDialogResultOk(object result)
        {
            if (result == null)
            {
                return false;
            }

            if (result is int numeric)
            {
                return numeric == 1;
            }

            return string.Equals(result.ToString(), "OK", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadStringProperty(Type type, object instance, string propertyName)
        {
            PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            return property?.GetValue(instance, null) as string ?? string.Empty;
        }

        private static void SetProperty(Type type, object instance, string propertyName, object value)
        {
            PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property?.CanWrite != true)
            {
                return;
            }

            property.SetValue(instance, value, null);
        }
    }
}

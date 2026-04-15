namespace RimChat.UI.UGui
{
    /// <summary>
    /// Dependencies: RimChatSettings for persistence.
    /// Responsibility: feature flags controlling UGUI rendering paths.
    /// When disabled, panels fall back to IMGUI direct rendering.
    /// </summary>
    internal static class UGuiFeatureFlags
    {
        /// <summary>
        /// Master switch: enable UGUI Canvas rendering for panels that support it.
        /// When false, all panels use IMGUI direct rendering (original behavior).
        /// </summary>
        internal static bool UseUguiRendering => _useUguiRendering;

        /// <summary>
        /// Enable UGUI rendering specifically for the prompt preview panel.
        /// Requires UseUguiRendering to be true.
        /// </summary>
        internal static bool UseUguiPreviewPanel => UseUguiRendering && _useUguiPreviewPanel;

        /// <summary>
        /// Enable UGUI rendering for the side panel (tab bar).
        /// Requires UseUguiRendering to be true.
        /// </summary>
        internal static bool UseUguiSidePanel => UseUguiRendering && _useUguiSidePanel;

        /// <summary>
        /// Enable UGUI rendering for chat message flow.
        /// Requires UseUguiRendering to be true.
        /// </summary>
        internal static bool UseUguiChatMessages => UseUguiRendering && _useUguiChatMessages;

        /// <summary>
        /// Enable UGUI rendering for the workspace header panel.
        /// Requires UseUguiRendering to be true.
        /// </summary>
        internal static bool UseUguiHeaderPanel => UseUguiRendering && _useUguiHeaderPanel;

        /// <summary>
        /// Enable UGUI rendering for the workspace preset (left) panel.
        /// Requires UseUguiRendering to be true.
        /// </summary>
        internal static bool UseUguiPresetPanel => UseUguiRendering && _useUguiPresetPanel;

        /// <summary>
        /// Enable UGUI rendering for the workspace editor chrome (toolbar, metadata, validation).
        /// Requires UseUguiRendering to be true.
        /// </summary>
        internal static bool UseUguiEditorPanel => UseUguiRendering && _useUguiEditorPanel;

        // Backing fields - these are read from RimChatSettings
        private static bool _useUguiRendering = true;
        private static bool _useUguiPreviewPanel = true;
        private static bool _useUguiSidePanel = true;
        private static bool _useUguiChatMessages;
        private static bool _useUguiHeaderPanel = true;
        private static bool _useUguiPresetPanel = true;
        private static bool _useUguiEditorPanel = true;

        /// <summary>
        /// Synchronize flags from settings. Called once at mod load.
        /// </summary>
        internal static void SyncFromSettings(
            bool masterSwitch,
            bool previewPanel,
            bool sidePanel,
            bool chatMessages,
            bool headerPanel,
            bool presetPanel,
            bool editorPanel)
        {
            _useUguiRendering = masterSwitch;
            _useUguiPreviewPanel = previewPanel;
            _useUguiSidePanel = sidePanel;
            _useUguiChatMessages = chatMessages;
            _useUguiHeaderPanel = headerPanel;
            _useUguiPresetPanel = presetPanel;
            _useUguiEditorPanel = editorPanel;
        }
    }
}

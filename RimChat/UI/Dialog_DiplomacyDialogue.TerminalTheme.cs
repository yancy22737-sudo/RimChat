using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimChat.UI
{
    public partial class Dialog_DiplomacyDialogue
    {
        // Bezel frame insets — asymmetric, matching the actual transparent region.
        // Original texture: 1402x1122, transparent X(232..1287) Y(167..994)
        // Scale factor: 960/1402 ≈ 0.685, 720/1122 ≈ 0.642
        private const float BezelInsetLeft = 159f;    // 232 * 0.685
        private const float BezelInsetRight = 79f;    // (1402-1287) * 0.685
        private const float BezelInsetTop = 107f;     // 167 * 0.642
        private const float BezelInsetBottom = 82f;   // (1122-994) * 0.642

        // Note: doWindowBackground = false is set in the constructor
        // to disable the default semi-transparent black background.
        // The bezel texture provides its own background.

        // CRT bezel textures (standard + spacer variant)
        private static Texture2D TexCRTBezel;
        private static Texture2D TexCRTBezelSpacer;

        // Texture switch hotspot — rect in original texture coords (1402x1122): (40,665)→(148,783)
        // Scaled to window 960x720: (27,427)→(101,503)
        private static readonly Rect SwitchHotspotWindow = new Rect(27f, 404f, 74f, 76f);
        private static bool _forceSpacerBezel;
        private static bool _spacerAutoSwitched;
        private static bool _switchHotspotWasHovering;

        // CRT overlay material (barrel distortion + scanlines + green tint + vignette)
        private static Material MatCRT;

        // Procedural scanline texture fallback (no shader needed)
        private static Texture2D _scanlineOverlay;
        private static Texture2D ScanlineOverlay
        {
            get
            {
                if (_scanlineOverlay == null)
                    _scanlineOverlay = CreateScanlineTexture();
                return _scanlineOverlay;
            }
        }

        /// <summary>
        /// Called from the main static constructor to load CRT resources.
        /// </summary>
        private static void InitTerminalTheme()
        {
            TexCRTBezel = ContentFinder<Texture2D>.Get("UI/RimChat/Terminal/terminal", false);
            TexCRTBezelSpacer = ContentFinder<Texture2D>.Get("UI/RimChat/Terminal/terminal_spacer", false);

            Shader crtShader = Shader.Find("RimChat/CRT");
            if (crtShader != null)
            {
                MatCRT = new Material(crtShader);
            }
        }

        /// <summary>
        /// Shrink a rect inward by the bezel frame insets (asymmetric).
        /// Content should be drawn inside this rect to avoid being covered by the bezel.
        /// </summary>
        private static Rect ShrinkForBezel(Rect rect)
        {
            return new Rect(
                rect.x + BezelInsetLeft,
                rect.y + BezelInsetTop,
                rect.width - BezelInsetLeft - BezelInsetRight,
                rect.height - BezelInsetTop - BezelInsetBottom);
        }

        /// <summary>
        /// Draw CRT overlay (green tint + scanlines + vignette) on the content area.
        /// Drawn AFTER content but BEFORE hover cards.
        /// </summary>
        private static void DrawCRTOverlay(Rect contentRect)
        {
            if (MatCRT != null)
            {
                DrawCRTWithShader(contentRect);
            }
            else
            {
                DrawCRTProcedural(contentRect);
            }
        }

        /// <summary>
        /// Draw the bezel frame as the OUTERMOST background layer.
        /// Call this BEFORE drawing any content.
        /// </summary>
        private static void DrawCRTBezelBackground(Rect windowRect)
        {
            Texture2D tex = GetActiveBezelTexture();
            if (tex == null) return;
            GUI.DrawTexture(windowRect, tex);

            // Auto-switch to spacer bezel on first Spacer research completion
            if (!_spacerAutoSwitched && IsSpacerTechLevel())
            {
                _forceSpacerBezel = true;
                _spacerAutoSwitched = true;
            }

            // Texture switch hotspot (only visible at Spacer tech level)
            if (IsSpacerTechLevel() && TexCRTBezelSpacer != null)
            {
                Rect hotspot = new Rect(
                    windowRect.x + SwitchHotspotWindow.x,
                    windowRect.y + SwitchHotspotWindow.y,
                    SwitchHotspotWindow.width,
                    SwitchHotspotWindow.height);

                bool hovering = Mouse.IsOver(hotspot);

                if (hovering)
                {
                    // Hover highlight
                    Color prev = GUI.color;
                    GUI.color = new Color(0.3f, 0.8f, 0.5f, 0.18f);
                    GUI.DrawTexture(hotspot, BaseContent.WhiteTex);
                    GUI.color = prev;

                    // Play hover sound once
                    if (!_switchHotspotWasHovering)
                    {
                        SoundDefOf.Mouseover_ButtonToggle.PlayOneShotOnCamera();
                    }

                    // Click to toggle
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                    {
                        _forceSpacerBezel = !_forceSpacerBezel;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        Event.current.Use();
                    }

                    _switchHotspotWasHovering = true;
                }
                else
                {
                    _switchHotspotWasHovering = false;
                }
            }
        }

        private static Texture2D GetActiveBezelTexture()
        {
            if (_forceSpacerBezel && TexCRTBezelSpacer != null)
                return TexCRTBezelSpacer;
            return TexCRTBezel;
        }

        private static bool IsSpacerTechLevel()
        {
            return DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Any(r => r.techLevel >= TechLevel.Spacer && r.IsFinished);
        }

        /// <summary>
        /// Shader-based CRT effect (full fidelity with barrel distortion).
        /// </summary>
        private static void DrawCRTWithShader(Rect rect)
        {
            MatCRT.SetFloat("_Distortion", 0.18f);
            MatCRT.SetFloat("_ScanlineIntensity", 0.10f);
            MatCRT.SetFloat("_ScanlineCount", 600f);
            MatCRT.SetFloat("_VignetteIntensity", 0.35f);
            MatCRT.SetFloat("_GreenTint", 0.65f);
            MatCRT.SetFloat("_ChromaticAberration", 1.5f);
            MatCRT.SetFloat("_NoiseIntensity", 0.05f);

            Graphics.DrawTexture(rect, BaseContent.WhiteTex, new Rect(0, 0, 1, 1), 0, 0, 0, 0, MatCRT);
        }

        /// <summary>
        /// Procedural CRT effect (no shader, no barrel distortion).
        /// Green tint + scanlines + vignette using draw calls.
        /// </summary>
        private static void DrawCRTProcedural(Rect rect)
        {
            Color prevColor = GUI.color;

            // Green phosphor tint overlay (subtle, preserves readability)
            GUI.color = new Color(0.05f, 0.2f, 0.08f, 0.12f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);

            // Scanline overlay (very subtle)
            GUI.color = new Color(0, 0, 0, 0.04f);
            float uScale = rect.width / 4f;
            float vScale = rect.height / 4f;
            GUI.DrawTextureWithTexCoords(rect, ScanlineOverlay, new Rect(0, 0, uScale, vScale));

            GUI.color = prevColor;

            // Vignette (darker edges)
            DrawVignette(rect);
        }

        /// <summary>
        /// Create a 4x4 scanline texture (2 transparent rows + 2 dark rows).
        /// </summary>
        private static Texture2D CreateScanlineTexture()
        {
            var tex = new Texture2D(4, 4, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Point;
            Color clear = new Color(0, 0, 0, 0);
            Color dark = new Color(0, 0, 0, 1f);

            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    tex.SetPixel(x, y, (y % 2 == 0) ? clear : dark);
                }
            }

            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Draw a vignette effect (darker at edges) using gradient strips.
        /// </summary>
        private static void DrawVignette(Rect rect)
        {
            Color prevColor = GUI.color;
            float edgeAlpha = 0.15f;
            float edgeWidth = rect.width * 0.08f;
            float edgeHeight = rect.height * 0.08f;

            GUI.color = new Color(0, 0, 0, edgeAlpha);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, edgeHeight), BaseContent.WhiteTex);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - edgeHeight, rect.width, edgeHeight), BaseContent.WhiteTex);
            GUI.DrawTexture(new Rect(rect.x, rect.y, edgeWidth, rect.height), BaseContent.WhiteTex);
            GUI.DrawTexture(new Rect(rect.xMax - edgeWidth, rect.y, edgeWidth, rect.height), BaseContent.WhiteTex);

            GUI.color = prevColor;
        }
    }
}

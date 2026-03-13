using System.Text;
using RimChat.Config;
using RimChat.DiplomacySystem;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: diplomacy image settings/service and current negotiator context.
    /// Responsibility: collect selfie parameters, trigger image generation, and open preview dialog.
    /// </summary>
    public sealed class Dialog_DiplomacySelfieConfig : Window
    {
        private readonly Faction faction;
        private readonly Pawn negotiator;
        private string promptText = string.Empty;
        private string sizeText = DiplomacyImageApiConfig.DefaultImageSize;
        private string captionText = string.Empty;
        private bool watermark;
        private bool isGenerating;
        private string status = string.Empty;
        private Vector2 scrollPos = Vector2.zero;
        private readonly SelfiePromptInjectionBuilder.Switches injectionSwitches = new SelfiePromptInjectionBuilder.Switches();

        public override Vector2 InitialSize => new Vector2(620f, 500f);

        public Dialog_DiplomacySelfieConfig(Faction faction, Pawn negotiator)
        {
            this.faction = faction;
            this.negotiator = negotiator;
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;

            RimChatSettings settings = Core.RimChatMod.Settings;
            DiplomacyImageApiConfig imageConfig = settings?.DiplomacyImageApi;
            if (imageConfig != null)
            {
                sizeText = imageConfig.DefaultSize;
                watermark = imageConfig.DefaultWatermark;
            }

            captionText = "RimChat_SelfieDefaultCaption".Translate(negotiator?.LabelShort ?? "Pawn");
            promptText = "RimChat_SelfieDefaultPrompt".Translate(negotiator?.LabelShort ?? "Pawn");
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "RimChat_SelfieConfigTitle".Translate());

            Rect formRect = new Rect(inRect.x, inRect.y + 34f, inRect.width, inRect.height - 84f);
            DrawForm(formRect);

            Rect statusRect = new Rect(inRect.x, inRect.yMax - 48f, inRect.width - 130f, 20f);
            DrawStatus(statusRect);

            Rect runRect = new Rect(inRect.xMax - 120f, inRect.yMax - 56f, 120f, 28f);
            GUI.enabled = !isGenerating;
            if (Widgets.ButtonText(runRect, "RimChat_SelfieGenerate".Translate()))
            {
                StartGenerate();
            }

            GUI.enabled = true;
        }

        private void DrawForm(Rect rect)
        {
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, 700f);
            Widgets.BeginScrollView(rect, ref scrollPos, viewRect);
            float y = 0f;

            Widgets.Label(new Rect(0f, y, viewRect.width, 24f), "RimChat_SelfiePrompt".Translate());
            y += 26f;
            promptText = Widgets.TextArea(new Rect(0f, y, viewRect.width, 180f), promptText);
            y += 188f;

            Widgets.Label(new Rect(0f, y, 120f, 24f), "RimChat_SelfieSize".Translate());
            sizeText = Widgets.TextField(new Rect(120f, y, 170f, 24f), sizeText);
            y += 30f;

            Widgets.Label(new Rect(0f, y, 120f, 24f), "RimChat_SelfieCaption".Translate());
            captionText = Widgets.TextField(new Rect(120f, y, viewRect.width - 120f, 24f), captionText);
            y += 30f;

            Widgets.CheckboxLabeled(new Rect(0f, y, viewRect.width, 24f), "RimChat_SelfieWatermark".Translate(), ref watermark);
            y += 32f;

            y += DrawInjectionSwitches(new Rect(0f, y, viewRect.width, 162f));
            y += 10f;
            DrawContextPreview(new Rect(0f, y, viewRect.width, 120f));
            Widgets.EndScrollView();
        }

        private float DrawInjectionSwitches(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.13f, 0.95f));
            GUI.color = new Color(0.85f, 0.88f, 0.94f);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 24f), "RimChat_SelfieInjectSection".Translate());
            GUI.color = Color.white;

            float leftX = rect.x + 8f;
            float rightX = rect.x + rect.width * 0.5f;
            float rowY = rect.y + 32f;
            float leftWidth = rect.width * 0.5f - 12f;
            float rightWidth = rect.width * 0.5f - 12f;

            Widgets.CheckboxLabeled(new Rect(leftX, rowY, leftWidth, 24f), "RimChat_SelfieInjectApparel".Translate(), ref injectionSwitches.IncludeApparel); rowY += 24f;
            Widgets.CheckboxLabeled(new Rect(leftX, rowY, leftWidth, 24f), "RimChat_SelfieInjectBodyType".Translate(), ref injectionSwitches.IncludeBodyType); rowY += 24f;
            Widgets.CheckboxLabeled(new Rect(leftX, rowY, leftWidth, 24f), "RimChat_SelfieInjectHair".Translate(), ref injectionSwitches.IncludeHair);

            float rightY = rect.y + 32f;
            Widgets.CheckboxLabeled(new Rect(rightX, rightY, rightWidth, 24f), "RimChat_SelfieInjectWeapon".Translate(), ref injectionSwitches.IncludeWeapon); rightY += 24f;
            Widgets.CheckboxLabeled(new Rect(rightX, rightY, rightWidth, 24f), "RimChat_SelfieInjectImplants".Translate(), ref injectionSwitches.IncludeImplants); rightY += 24f;
            Widgets.CheckboxLabeled(new Rect(rightX, rightY, rightWidth, 24f), "RimChat_SelfieInjectStatus".Translate(), ref injectionSwitches.IncludeStatus);

            return rect.height;
        }

        private void DrawContextPreview(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.14f, 0.95f));
            GUI.color = new Color(0.82f, 0.86f, 0.92f);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 24f), "RimChat_SelfieContextPreview".Translate());
            GUI.color = Color.white;

            Text.Font = GameFont.Tiny;
            string preview = BuildContextSummary();
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 30f, rect.width - 16f, rect.height - 34f), preview);
            Text.Font = GameFont.Small;
        }

        private string BuildContextSummary()
        {
            string negotiatorName = negotiator?.LabelShort ?? "Unknown";
            string factionName = faction?.Name ?? "Unknown";
            int goodwill = faction?.PlayerGoodwill ?? 0;
            return "RimChat_SelfieContextSummary".Translate(negotiatorName, factionName, goodwill);
        }

        private void DrawStatus(Rect rect)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return;
            }

            GUI.color = isGenerating ? new Color(0.9f, 0.85f, 0.4f) : new Color(0.9f, 0.9f, 0.92f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(rect, status);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private void StartGenerate()
        {
            if (isGenerating)
            {
                return;
            }

            RimChatSettings settings = Core.RimChatMod.Settings;
            DiplomacyImageApiConfig imageConfig = settings?.DiplomacyImageApi;
            if (imageConfig == null || !imageConfig.IsConfigured())
            {
                status = "RimChat_SelfieConfigInvalid".Translate();
                return;
            }

            isGenerating = true;
            status = "RimChat_SelfieGenerating".Translate();

            var request = new DiplomacyImageGenerationRequest
            {
                Faction = faction,
                Endpoint = imageConfig.Endpoint,
                ApiKey = imageConfig.ApiKey,
                Model = imageConfig.Model,
                Prompt = BuildSelfiePrompt(),
                Caption = string.IsNullOrWhiteSpace(captionText) ? "RimChat_SelfieDefaultCaption".Translate(negotiator?.LabelShort ?? "Pawn") : captionText.Trim(),
                Size = DiplomacyImageApiConfig.NormalizeImageSize(sizeText, imageConfig.DefaultSize),
                Watermark = watermark,
                TimeoutSeconds = imageConfig.TimeoutSeconds
            };

            DiplomacyImageGenerationService.Instance.GenerateImage(request, OnGenerated);
        }

        private string BuildSelfiePrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine(promptText?.Trim() ?? string.Empty);
            sb.AppendLine();
            sb.AppendLine("Character profile:");
            sb.AppendLine($"- Name: {negotiator?.LabelShort ?? "Unknown"}");
            sb.AppendLine($"- Gender: {negotiator?.gender.ToString() ?? "Unknown"}");
            sb.AppendLine($"- Faction context: {faction?.Name ?? "Unknown"}");
            sb.AppendLine("- Style: RimWorld grounded illustration, clear face, natural pose.");

            string injection = SelfiePromptInjectionBuilder.Build(negotiator, faction, injectionSwitches);
            if (!string.IsNullOrWhiteSpace(injection))
            {
                sb.AppendLine();
                sb.AppendLine(injection);
            }
            return sb.ToString().Trim();
        }

        private void OnGenerated(DiplomacyImageGenerationResult result)
        {
            isGenerating = false;
            if (result == null || !result.Success)
            {
                status = "RimChat_SelfieGenerateFailed".Translate(result?.Error ?? "unknown");
                return;
            }

            status = "RimChat_SelfieGenerateSuccess".Translate();
            AlbumImageEntry metadata = new AlbumImageEntry
            {
                SavedTick = Find.TickManager?.TicksGame ?? 0,
                SourcePath = result.LocalPath,
                Caption = result.Caption ?? string.Empty,
                FactionId = faction?.Name ?? string.Empty,
                NegotiatorId = negotiator?.ThingID ?? string.Empty,
                Size = sizeText ?? string.Empty,
                SourceType = AlbumImageEntry.SourceSelfie
            };

            Find.WindowStack.Add(new Dialog_DiplomacySelfiePreview(result.LocalPath, result.Caption, metadata));
            Close();
        }
    }
}

using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>/// Dependencies: RimWorld/Verse settings widgets and diplomacy image prompt template models.
 /// Responsibility: render standalone diplomacy image API tab and maintain image template defaults/migration.
 ///</summary>
    public partial class RimChatSettings : ModSettings
    {
        private Vector2 _imageApiTabScroll = Vector2.zero;
        private Vector2 _imageTemplateTextScroll = Vector2.zero;
        private int _selectedImageTemplateIndex = 0;

        private void EnsureDiplomacyImageDefaults()
        {
            DiplomacyImageApi ??= new DiplomacyImageApiConfig();
            DiplomacyImageApi.ApplyFallbackDefaults(ResolveDefaultApiEndpointForImage(), ResolveDefaultApiModelForImage());
            DiplomacyImageApi.Normalize();
            EnsureSendImageCaptionDefaults();
            DiplomacyImagePromptTemplates ??= new List<DiplomacyImagePromptTemplate>();
            DiplomacyImageTemplateDefaults.EnsureDefaults(DiplomacyImagePromptTemplates);
            EnsureImageTemplateIds();
            if (_selectedImageTemplateIndex < 0 || _selectedImageTemplateIndex >= DiplomacyImagePromptTemplates.Count)
            {
                _selectedImageTemplateIndex = 0;
            }
        }

        private void DrawTab_DiplomacyImageApi(Rect rect)
        {
            EnsureDiplomacyImageDefaults();
            float viewWidth = Mathf.Max(300f, rect.width - 16f);
            float viewHeight = CalculateImageApiContentHeight(viewWidth);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(rect, ref _imageApiTabScroll, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(new Rect(0f, 0f, viewRect.width, viewRect.height));

            DrawImageApiConnectionSection(listing);
            listing.Gap(6f);
            listing.GapLine();
            DrawImageTemplateEditorSection(listing);

            listing.End();
            Widgets.EndScrollView();
        }

        private float CalculateImageApiContentHeight(float width)
        {
            int templateCount = DiplomacyImagePromptTemplates?.Count ?? 0;
            float selectorHeight = Mathf.Max(56f, templateCount * 24f + 10f);
            float captionWidth = Mathf.Max(140f, width - 28f);
            float styleHeight = Mathf.Max(84f, Text.CalcHeight(SendImageCaptionStylePrompt ?? string.Empty, captionWidth) + 22f);
            float fallbackHeight = Mathf.Max(84f, Text.CalcHeight(SendImageCaptionFallbackTemplate ?? string.Empty, captionWidth) + 22f);
            DiplomacyImagePromptTemplate selected = GetSelectedImageTemplate();
            float templateTextHeight = 170f;
            if (selected != null)
            {
                float textWidth = Mathf.Max(140f, width - 20f);
                float dynamicHeight = Text.CalcHeight(selected.Text ?? string.Empty, textWidth - 20f) + 22f;
                templateTextHeight = Mathf.Max(170f, dynamicHeight);
            }

            // Keep generous safety space so the multiline template editor is never clipped
            // by page-content height underestimation in different UI scales.
            float estimatedHeight = 780f + selectorHeight + templateTextHeight + styleHeight + fallbackHeight;
            return Mathf.Max(estimatedHeight, 820f);
        }

        private void DrawImageApiConnectionSection(Listing_Standard listing)
        {
            listing.CheckboxLabeled("RimChat_ImageApiEnabled".Translate(), ref DiplomacyImageApi.IsEnabled);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("RimChat_ImageApiExperimentalHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.Label("RimChat_ImageApiEndpoint".Translate());
            DiplomacyImageApi.Endpoint = Widgets.TextField(listing.GetRect(26f), DiplomacyImageApi.Endpoint ?? string.Empty);

            listing.Label("RimChat_ImageApiKey".Translate());
            DiplomacyImageApi.ApiKey = Widgets.TextField(listing.GetRect(26f), DiplomacyImageApi.ApiKey ?? string.Empty);

            listing.Label("RimChat_ImageApiModel".Translate());
            DiplomacyImageApi.Model = Widgets.TextField(listing.GetRect(26f), DiplomacyImageApi.Model ?? string.Empty);

            listing.Label("RimChat_ImageApiDefaultSize".Translate());
            DiplomacyImageApi.DefaultSize = Widgets.TextField(listing.GetRect(26f), DiplomacyImageApi.DefaultSize ?? string.Empty);

            listing.CheckboxLabeled("RimChat_ImageApiDefaultWatermark".Translate(), ref DiplomacyImageApi.DefaultWatermark);
            listing.Label("RimChat_ImageApiTimeout".Translate(DiplomacyImageApi.TimeoutSeconds));
            DiplomacyImageApi.TimeoutSeconds = Mathf.RoundToInt(listing.Slider(DiplomacyImageApi.TimeoutSeconds, 10f, 300f));
            listing.Gap(4f);
            listing.Label("RimChat_SendImageCaptionStylePromptLabel".Translate());
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("RimChat_SendImageCaptionStylePromptHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Rect styleRect = listing.GetRect(86f);
            Widgets.DrawBox(styleRect);
            SendImageCaptionStylePrompt = Widgets.TextArea(styleRect.ContractedBy(4f), SendImageCaptionStylePrompt ?? string.Empty);

            listing.Label("RimChat_SendImageCaptionFallbackTemplateLabel".Translate());
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("RimChat_SendImageCaptionFallbackTemplateHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Rect fallbackRect = listing.GetRect(86f);
            Widgets.DrawBox(fallbackRect);
            SendImageCaptionFallbackTemplate = Widgets.TextArea(fallbackRect.ContractedBy(4f), SendImageCaptionFallbackTemplate ?? string.Empty);

            DiplomacyImageApi.Normalize();
            EnsureSendImageCaptionDefaults();
        }

        private void EnsureSendImageCaptionDefaults()
        {
            SendImageCaptionStylePrompt = (SendImageCaptionStylePrompt ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(SendImageCaptionStylePrompt))
            {
                SendImageCaptionStylePrompt = PromptTextConstants.SendImageCaptionStylePromptDefault;
            }

            SendImageCaptionFallbackTemplate = (SendImageCaptionFallbackTemplate ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(SendImageCaptionFallbackTemplate))
            {
                SendImageCaptionFallbackTemplate = PromptTextConstants.SendImageCaptionFallbackTemplateDefault;
            }
        }

        private string ResolveDefaultApiEndpointForImage()
        {
            ApiConfig config = ResolvePrimaryCloudApiConfig();
            string endpoint = config?.GetEffectiveEndpoint();
            return string.IsNullOrWhiteSpace(endpoint)
                ? DiplomacyImageApiConfig.DefaultVolcEngineImageEndpoint
                : endpoint;
        }

        private string ResolveDefaultApiModelForImage()
        {
            ApiConfig config = ResolvePrimaryCloudApiConfig();
            string model = config?.GetEffectiveModelName();
            return string.IsNullOrWhiteSpace(model)
                ? DiplomacyImageApiConfig.DefaultVolcEngineImageModel
                : model;
        }

        private ApiConfig ResolvePrimaryCloudApiConfig()
        {
            if (CloudConfigs == null || CloudConfigs.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < CloudConfigs.Count; i++)
            {
                ApiConfig config = CloudConfigs[i];
                if (config != null && config.IsEnabled)
                {
                    return config;
                }
            }

            return CloudConfigs[0];
        }

        private void DrawImageTemplateEditorSection(Listing_Standard listing)
        {
            listing.Label("RimChat_ImageTemplateSection".Translate());
            DrawImageTemplateToolbar(listing);
            DrawImageTemplateSelector(listing);

            DiplomacyImagePromptTemplate selected = GetSelectedImageTemplate();
            if (selected == null)
            {
                return;
            }

            listing.Gap(4f);
            listing.CheckboxLabeled("RimChat_ImageTemplateEnabled".Translate(), ref selected.Enabled);

            listing.Label("RimChat_ImageTemplateId".Translate());
            selected.Id = Widgets.TextField(listing.GetRect(26f), selected.Id ?? string.Empty);

            listing.Label("RimChat_ImageTemplateName".Translate());
            selected.Name = Widgets.TextField(listing.GetRect(26f), selected.Name ?? string.Empty);

            listing.Label("RimChat_ImageTemplateDescription".Translate());
            selected.Description = Widgets.TextField(listing.GetRect(26f), selected.Description ?? string.Empty);

            listing.Label("RimChat_ImageTemplateText".Translate());
            Rect textRect = listing.GetRect(170f);
            Widgets.DrawBox(textRect);
            Rect editorRect = textRect.ContractedBy(4f);
            selected.Text = Widgets.TextArea(editorRect, selected.Text ?? string.Empty);
            EnsureImageTemplateIds();
        }

        private void DrawImageTemplateToolbar(Listing_Standard listing)
        {
            Rect row = listing.GetRect(26f);
            float buttonWidth = 120f;
            Rect addRect = new Rect(row.x, row.y, buttonWidth, row.height);
            Rect deleteRect = new Rect(addRect.xMax + 8f, row.y, buttonWidth, row.height);

            if (Widgets.ButtonText(addRect, "RimChat_ImageTemplateAdd".Translate()))
            {
                DiplomacyImagePromptTemplates.Add(new DiplomacyImagePromptTemplate
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "RimChat_ImageTemplateNewName".Translate(),
                    Text = string.Empty,
                    Description = string.Empty,
                    Enabled = true
                });
                _selectedImageTemplateIndex = DiplomacyImagePromptTemplates.Count - 1;
                _imageTemplateTextScroll = Vector2.zero;
            }

            bool canDelete = DiplomacyImagePromptTemplates.Count > 1;
            if (!canDelete)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
            }
            if (Widgets.ButtonText(deleteRect, "RimChat_ImageTemplateDelete".Translate()) && canDelete)
            {
                int index = Mathf.Clamp(_selectedImageTemplateIndex, 0, DiplomacyImagePromptTemplates.Count - 1);
                DiplomacyImagePromptTemplates.RemoveAt(index);
                _selectedImageTemplateIndex = Mathf.Clamp(index - 1, 0, DiplomacyImagePromptTemplates.Count - 1);
                _imageTemplateTextScroll = Vector2.zero;
            }
            GUI.color = Color.white;
        }

        private void DrawImageTemplateSelector(Listing_Standard listing)
        {
            for (int i = 0; i < DiplomacyImagePromptTemplates.Count; i++)
            {
                DiplomacyImagePromptTemplate template = DiplomacyImagePromptTemplates[i];
                if (template == null)
                {
                    continue;
                }

                Rect row = listing.GetRect(24f);
                bool selected = i == _selectedImageTemplateIndex;
                if (selected)
                {
                    Widgets.DrawBoxSolid(row, new Color(0.23f, 0.32f, 0.44f, 0.85f));
                }

                string name = string.IsNullOrWhiteSpace(template.Name) ? template.Id : template.Name;
                string state = template.Enabled
                    ? "RimChat_CommsToggleStatusOn".Translate().ToString()
                    : "RimChat_CommsToggleStatusOff".Translate().ToString();
                Widgets.Label(row, $"[{state}] {name}");
                if (Widgets.ButtonInvisible(row))
                {
                    _selectedImageTemplateIndex = i;
                    _imageTemplateTextScroll = Vector2.zero;
                }
            }
        }

        private DiplomacyImagePromptTemplate GetSelectedImageTemplate()
        {
            if (DiplomacyImagePromptTemplates == null || DiplomacyImagePromptTemplates.Count == 0)
            {
                return null;
            }

            _selectedImageTemplateIndex = Mathf.Clamp(_selectedImageTemplateIndex, 0, DiplomacyImagePromptTemplates.Count - 1);
            return DiplomacyImagePromptTemplates[_selectedImageTemplateIndex];
        }

        private void EnsureImageTemplateIds()
        {
            if (DiplomacyImagePromptTemplates == null)
            {
                return;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < DiplomacyImagePromptTemplates.Count; i++)
            {
                DiplomacyImagePromptTemplate template = DiplomacyImagePromptTemplates[i];
                if (template == null)
                {
                    continue;
                }

                string id = (template.Id ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = Guid.NewGuid().ToString("N");
                }

                if (used.Contains(id))
                {
                    id = $"{id}_{i + 1}";
                }

                template.Id = id;
                used.Add(id);
            }
        }
    }
}

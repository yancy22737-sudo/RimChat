using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.AI;
using RimChat.Config;
using RimChat.Core;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimWorld;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: diplomacy image config/templates, ARK image generation service, and dialogue session message APIs.
 /// Responsibility: intercept send_image action, build prompt payload, and append inline image message/failure notices.
 ///</summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private bool TryHandleSendImageAction(
            AIAction action,
            FactionDialogueSession currentSession,
            Faction currentFaction,
            ref bool imageQueuedThisTurn)
        {
            if (action == null || !string.Equals(action.ActionType, AIActionNames.SendImage, StringComparison.Ordinal))
            {
                return false;
            }

            if (imageQueuedThisTurn)
            {
                currentSession?.AddMessage("System", "RimChat_SendImageOnlyOnePerTurn".Translate(), false, DialogueMessageType.System);
                return true;
            }

            imageQueuedThisTurn = true;
            if (currentSession == null || currentFaction == null)
            {
                return true;
            }

            RimChatSettings settings = RimChatMod.Settings;
            DiplomacyImageApiConfig imageConfig = settings?.DiplomacyImageApi;
            if (imageConfig == null || !imageConfig.IsConfigured())
            {
                currentSession.AddMessage("System", "RimChat_SendImageConfigInvalid".Translate(), false, DialogueMessageType.System);
                return true;
            }

            Dictionary<string, object> parameters = action.Parameters ?? new Dictionary<string, object>();
            string templateId = ReadStringParameter(parameters, "template_id");
            if (string.IsNullOrWhiteSpace(templateId))
            {
                templateId = ReadStringParameter(parameters, "templateId");
            }

            templateId = ResolveRequestedOrFallbackTemplateId(settings, templateId);
            if (string.IsNullOrWhiteSpace(templateId))
            {
                currentSession.AddMessage("System", "RimChat_SendImageTemplateRequired".Translate(), false, DialogueMessageType.System);
                return true;
            }

            DiplomacyImagePromptTemplate template = ResolveTemplate(settings, templateId);
            if (template == null || !template.Enabled)
            {
                currentSession.AddMessage("System", "RimChat_SendImageTemplateMissing".Translate(templateId), false, DialogueMessageType.System);
                return true;
            }

            string extraPrompt = ReadStringParameter(parameters, "extra_prompt");
            string caption = ReadStringParameter(parameters, "caption");
            string size = ReadStringParameter(parameters, "size");
            size = DiplomacyImageApiConfig.NormalizeImageSize(size, imageConfig.DefaultSize);

            bool watermark = imageConfig.DefaultWatermark;
            if (TryReadBoolParameter(parameters, "watermark", out bool watermarkOverride))
            {
                watermark = watermarkOverride;
            }

            string finalPrompt = DiplomacyImagePromptBuilder.BuildPrompt(currentFaction, template, extraPrompt);
            var request = new DiplomacyImageGenerationRequest
            {
                Faction = currentFaction,
                Endpoint = imageConfig.Endpoint,
                ApiKey = imageConfig.ApiKey,
                Model = imageConfig.Model,
                Prompt = finalPrompt,
                Caption = caption,
                Size = size,
                Watermark = watermark,
                TimeoutSeconds = imageConfig.TimeoutSeconds,
                Mode = imageConfig.Mode,
                SchemaPreset = imageConfig.SchemaPreset,
                AuthMode = imageConfig.AuthMode,
                ApiKeyHeaderName = imageConfig.ApiKeyHeaderName,
                ApiKeyQueryName = imageConfig.ApiKeyQueryName,
                ResponseUrlPath = imageConfig.ResponseUrlPath,
                ResponseB64Path = imageConfig.ResponseB64Path,
                AsyncSubmitPath = imageConfig.AsyncSubmitPath,
                AsyncStatusPathTemplate = imageConfig.AsyncStatusPathTemplate,
                AsyncImageFetchPath = imageConfig.AsyncImageFetchPath,
                PollIntervalMs = imageConfig.PollIntervalMs,
                PollMaxAttempts = imageConfig.PollMaxAttempts
            };

            currentSession.BeginImageRequest();
            currentSession.AddMessage("System", "RimChat_SendImageQueued".Translate(), false, DialogueMessageType.System);
            DiplomacyImageGenerationService.Instance.GenerateImage(request, result =>
            {
                currentSession.EndImageRequest();
                if (result == null || !result.Success)
                {
                    string reason = result?.Error ?? "Unknown image generation error.";
                    currentSession.AddMessage("System", "RimChat_SendImageFailed".Translate(reason), false, DialogueMessageType.System);
                    return;
                }

                string senderName = GetSenderName(currentFaction);
                string imageCaption = ResolveSendImageCaption(settings, currentFaction, template, result.Caption);
                currentSession.AddImageMessage(senderName, imageCaption, false, result.LocalPath, result.SourceUrl);
                SaveFactionMemory(currentSession, currentFaction);
            });

            return true;
        }

        private static DiplomacyImagePromptTemplate ResolveTemplate(RimChatSettings settings, string templateId)
        {
            if (settings == null || string.IsNullOrWhiteSpace(templateId))
            {
                return null;
            }

            List<DiplomacyImagePromptTemplate> templates = settings.DiplomacyImagePromptTemplates;
            if (templates == null || templates.Count == 0)
            {
                return null;
            }

            string resolved = DiplomacyImageTemplateDefaults.ResolveTemplateId(templates, templateId);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                templateId = resolved;
            }

            return templates.FirstOrDefault(item =>
                item != null &&
                string.Equals(item.Id, templateId, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveRequestedOrFallbackTemplateId(RimChatSettings settings, string requestedTemplateId)
        {
            if (settings == null)
            {
                return string.Empty;
            }

            settings.DiplomacyImagePromptTemplates ??= new List<DiplomacyImagePromptTemplate>();
            DiplomacyImageTemplateDefaults.EnsureDefaults(settings.DiplomacyImagePromptTemplates);
            if (settings.DiplomacyImagePromptTemplates.Count == 0)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(requestedTemplateId))
            {
                string resolved = DiplomacyImageTemplateDefaults.ResolveTemplateId(
                    settings.DiplomacyImagePromptTemplates,
                    requestedTemplateId);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            return DiplomacyImageTemplateDefaults.ResolvePreferredEnabledTemplateId(settings.DiplomacyImagePromptTemplates);
        }

        private static string ReadStringParameter(Dictionary<string, object> parameters, string key)
        {
            if (parameters == null || string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (!parameters.TryGetValue(key, out object value) || value == null)
            {
                return string.Empty;
            }

            return value.ToString().Trim();
        }

        private static bool TryReadBoolParameter(Dictionary<string, object> parameters, string key, out bool value)
        {
            value = false;
            if (parameters == null || !parameters.TryGetValue(key, out object raw) || raw == null)
            {
                return false;
            }

            switch (raw)
            {
                case bool boolValue:
                    value = boolValue;
                    return true;
                case int intValue:
                    value = intValue != 0;
                    return true;
                case long longValue:
                    value = longValue != 0;
                    return true;
            }

            return bool.TryParse(raw.ToString(), out value);
        }

        private static string ResolveSendImageCaption(
            RimChatSettings settings,
            Faction faction,
            DiplomacyImagePromptTemplate template,
            string aiCaption)
        {
            string trimmed = (aiCaption ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }

            string fallback = RenderSendImageCaptionFallback(settings, faction, template);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }

            string factionName = faction?.Name ?? "Faction";
            return "RimChat_SendImageDefaultCaption".Translate(factionName);
        }

        private static string RenderSendImageCaptionFallback(
            RimChatSettings settings,
            Faction faction,
            DiplomacyImagePromptTemplate template)
        {
            string rawTemplate = (settings?.SendImageCaptionFallbackTemplate ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawTemplate))
            {
                rawTemplate = PromptTextConstants.SendImageCaptionFallbackTemplateDefault;
            }

            string leaderName = ResolveFactionLeaderName(faction);
            string factionName = faction?.Name ?? string.Empty;
            string templateName = template?.Name ?? string.Empty;
            return rawTemplate
                .Replace("{leader}", leaderName)
                .Replace("{faction}", factionName)
                .Replace("{template_name}", templateName)
                .Trim();
        }

        private static string ResolveFactionLeaderName(Faction faction)
        {
            Pawn leader = faction?.leader;
            if (leader?.Name != null)
            {
                string byName = leader.Name.ToStringShort;
                if (!string.IsNullOrWhiteSpace(byName))
                {
                    return byName;
                }
            }

            if (!string.IsNullOrWhiteSpace(leader?.LabelShort))
            {
                return leader.LabelShort;
            }

            return faction?.Name ?? "Faction";
        }
    }
}

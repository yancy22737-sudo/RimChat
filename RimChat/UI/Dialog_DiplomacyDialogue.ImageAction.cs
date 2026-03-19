using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.AI;
using RimChat.Config;
using RimChat.Core;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimChat.Persistence;
using RimChat.Prompting;
using RimChat.Util;
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

            if (ImageGenerationAvailability.IsBlocked())
            {
                currentSession?.AddMessage("System", ImageGenerationAvailability.GetBlockedMessage(), false, DialogueMessageType.System);
                return true;
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
            string requestedTemplateId = ReadStringParameter(parameters, "template_id");
            if (string.IsNullOrWhiteSpace(requestedTemplateId))
            {
                requestedTemplateId = ReadStringParameter(parameters, "templateId");
            }

            PromptUnifiedTemplateAliasConfig resolvedAlias = ResolveRequestedOrFallbackTemplateAlias(settings, requestedTemplateId);
            if (resolvedAlias == null)
            {
                string failedId = string.IsNullOrWhiteSpace(requestedTemplateId)
                    ? RimTalkPromptEntryChannelCatalog.ImageGeneration
                    : requestedTemplateId;
                currentSession.AddMessage("System", "RimChat_SendImageTemplateMissing".Translate(failedId), false, DialogueMessageType.System);
                return true;
            }

            DiplomacyImagePromptTemplate template = BuildTemplateFromAlias(resolvedAlias);

            string extraPrompt = ReadStringParameter(parameters, "extra_prompt");
            string caption = ReadStringParameter(parameters, "caption");
            string size = ReadStringParameter(parameters, "size");
            size = DiplomacyImageApiConfig.NormalizeImageSize(size, imageConfig.DefaultSize);

            bool watermark = imageConfig.DefaultWatermark;
            if (TryReadBoolParameter(parameters, "watermark", out bool watermarkOverride))
            {
                watermark = watermarkOverride;
            }

            string finalPrompt = BuildUnifiedImagePrompt(settings, currentFaction, template, extraPrompt);
            LogSendImagePromptDebug(
                currentFaction,
                requestedTemplateId,
                template,
                extraPrompt,
                finalPrompt,
                size,
                watermark);
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
            DateTime sendImageStartedAtUtc = DateTime.UtcNow;
            string debugRequestText = BuildSendImageDebugRequestText(
                currentFaction,
                requestedTemplateId,
                template,
                size,
                watermark,
                finalPrompt);

            currentSession.BeginImageRequest();
            currentSession.AddMessage("System", "RimChat_SendImageQueued".Translate(), false, DialogueMessageType.System);
            DiplomacyImageGenerationService.Instance.GenerateImage(request, result =>
            {
                currentSession.EndImageRequest();
                long durationMs = Math.Max(0L, (long)(DateTime.UtcNow - sendImageStartedAtUtc).TotalMilliseconds);
                if (result == null || !result.Success)
                {
                    string reason = result?.Error ?? "Unknown image generation error.";
                    AIChatServiceAsync.RecordExternalDebugRecord(
                        AIRequestDebugSource.SendImage,
                        DialogueUsageChannel.Diplomacy,
                        imageConfig.Model,
                        AIRequestDebugStatus.Error,
                        durationMs,
                        0,
                        debugRequestText,
                        string.Empty,
                        reason,
                        sendImageStartedAtUtc);
                    currentSession.AddMessage("System", "RimChat_SendImageFailed".Translate(reason), false, DialogueMessageType.System);
                    return;
                }

                string debugResponseText = $"localPath={result.LocalPath ?? string.Empty}; sourceUrl={result.SourceUrl ?? string.Empty}; caption={result.Caption ?? string.Empty}";
                AIChatServiceAsync.RecordExternalDebugRecord(
                    AIRequestDebugSource.SendImage,
                    DialogueUsageChannel.Diplomacy,
                    imageConfig.Model,
                    AIRequestDebugStatus.Success,
                    durationMs,
                    200,
                    debugRequestText,
                    debugResponseText,
                    string.Empty,
                    sendImageStartedAtUtc);
                string senderName = GetSenderName(currentFaction);
                string imageCaption = ResolveSendImageCaption(settings, currentFaction, template, result.Caption);
                currentSession.AddImageMessage(senderName, imageCaption, false, result.LocalPath, result.SourceUrl);
                SaveFactionMemory(currentSession, currentFaction);
            });

            return true;
        }

        private static void LogSendImagePromptDebug(
            Faction faction,
            string requestedTemplateId,
            DiplomacyImagePromptTemplate resolvedTemplate,
            string extraPrompt,
            string finalPrompt,
            string size,
            bool watermark)
        {
            if (!DebugLogger.LogRequests)
            {
                return;
            }

            string factionName = faction?.Name ?? "<null>";
            string resolvedId = resolvedTemplate?.Id ?? "<null>";
            string resolvedName = resolvedTemplate?.Name ?? "<null>";
            int templateLength = (resolvedTemplate?.Text ?? string.Empty).Length;
            int extraLength = (extraPrompt ?? string.Empty).Length;
            int finalLength = (finalPrompt ?? string.Empty).Length;
            string preview = BuildPromptPreview(finalPrompt, 600);

            Log.Message(
                "[RimChat] send_image prompt debug: "
                + $"faction='{factionName}', requested_template_id='{requestedTemplateId ?? string.Empty}', "
                + $"resolved_template_id='{resolvedId}', resolved_template_name='{resolvedName}', "
                + $"template_text_len={templateLength}, extra_prompt_len={extraLength}, "
                + $"final_prompt_len={finalLength}, size='{size ?? string.Empty}', watermark={watermark}.");
            Log.Message($"[RimChat] send_image prompt preview: {preview}");
        }

        private static string BuildPromptPreview(string value, int maxLength)
        {
            string text = (value ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "<empty>";
            }

            if (text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength) + "...(truncated)";
        }

        private static string BuildSendImageDebugRequestText(
            Faction faction,
            string requestedTemplateId,
            DiplomacyImagePromptTemplate resolvedTemplate,
            string size,
            bool watermark,
            string finalPrompt)
        {
            string factionName = faction?.Name ?? string.Empty;
            string resolvedTemplateId = resolvedTemplate?.Id ?? string.Empty;
            string resolvedTemplateName = resolvedTemplate?.Name ?? string.Empty;
            string promptPreview = BuildPromptPreview(finalPrompt, 1200);
            return $"faction={factionName}; requested_template_id={requestedTemplateId ?? string.Empty}; resolved_template_id={resolvedTemplateId}; resolved_template_name={resolvedTemplateName}; size={size ?? string.Empty}; watermark={watermark}; prompt_preview={promptPreview}";
        }

        private static PromptUnifiedTemplateAliasConfig ResolveRequestedOrFallbackTemplateAlias(
            RimChatSettings settings,
            string requestedTemplateId)
        {
            if (settings == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(requestedTemplateId))
            {
                PromptUnifiedTemplateAliasConfig requested = settings.ResolvePromptTemplateAlias(
                    RimTalkPromptEntryChannelCatalog.ImageGeneration,
                    requestedTemplateId);
                if (requested != null && requested.Enabled)
                {
                    return requested;
                }
            }

            return settings.ResolvePreferredPromptTemplateAlias(
                RimTalkPromptEntryChannelCatalog.ImageGeneration,
                DiplomacyImageTemplateDefaults.DefaultTemplateId);
        }

        private static DiplomacyImagePromptTemplate BuildTemplateFromAlias(PromptUnifiedTemplateAliasConfig alias)
        {
            if (alias == null)
            {
                return null;
            }

            return new DiplomacyImagePromptTemplate
            {
                Id = alias.TemplateId,
                Name = alias.Name,
                Description = alias.Description,
                Text = alias.Content,
                Enabled = alias.Enabled
            };
        }

        private static string BuildUnifiedImagePrompt(
            RimChatSettings settings,
            Faction faction,
            DiplomacyImagePromptTemplate template,
            string extraPrompt)
        {
            string payload = DiplomacyImagePromptBuilder.BuildPrompt(faction, template, extraPrompt);
            DialogueScenarioContext context = DialogueScenarioContext.CreateDiplomacy(
                faction,
                false,
                new[] { "channel:image_generation", "phase:send_image" });
            var variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["dialogue.primary_objective"] = "Generate one image prompt consistent with faction identity and current dialogue context.",
                ["dialogue.optional_followup"] = "Keep image request grounded and executable by the configured image API.",
                ["dialogue.latest_unresolved_intent"] = string.Empty,
                ["dialogue.template_id"] = template?.Id ?? string.Empty,
                ["dialogue.template_name"] = template?.Name ?? string.Empty,
                ["dialogue.extra_prompt"] = extraPrompt ?? string.Empty
            };
            return PromptPersistenceService.Instance.BuildUnifiedChannelSystemPrompt(
                RimTalkPromptChannel.Diplomacy,
                RimTalkPromptEntryChannelCatalog.ImageGeneration,
                context,
                null,
                variables,
                "image_prompt_payload",
                payload);
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

            return RenderSendImageCaptionFallback(settings, faction, template);
        }

        private static string RenderSendImageCaptionFallback(
            RimChatSettings settings,
            Faction faction,
            DiplomacyImagePromptTemplate template)
        {
            string rawTemplate = (settings?.SendImageCaptionFallbackTemplate ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawTemplate))
            {
                throw new PromptRenderException(
                    "prompt_templates.image_caption_fallback",
                    "image",
                    new PromptRenderDiagnostic
                    {
                        ErrorCode = PromptRenderErrorCode.TemplateMissing,
                        Message = "Image caption fallback template is required in strict mode."
                    });
            }

            string leaderName = ResolveFactionLeaderName(faction);
            string factionName = faction?.Name ?? string.Empty;
            string templateName = template?.Name ?? string.Empty;
            const string templateId = "prompt_templates.image_caption_fallback";
            const string channel = "image";

            PromptRenderContext renderContext = PromptRenderContext.Create(templateId, channel);
            renderContext
                .SetValue("ctx.channel", channel)
                .SetValue("pawn.leader.name", leaderName)
                .SetValue("world.faction.name", factionName)
                .SetValue("dialogue.template_name", templateName);

            string rendered = PromptTemplateRenderer
                .RenderOrThrow(templateId, channel, rawTemplate, renderContext)
                ?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(rendered))
            {
                return rendered;
            }

            throw new PromptRenderException(
                templateId,
                channel,
                new PromptRenderDiagnostic
                {
                    ErrorCode = PromptRenderErrorCode.RuntimeError,
                    Message = "Image caption template rendered empty output."
                });
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

using System;
using System.Collections.Generic;
using System.Text;
using RimChat.AI;
using RimChat.Core;
using RimChat.DiplomacySystem;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
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
        private bool _isTestingImageConnection = false;
        private string _imageConnectionTestStatus = string.Empty;

        private void EnsureDiplomacyImageDefaults()
        {
            DiplomacyImageApi ??= new DiplomacyImageApiConfig();
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

            listing.End();
            Widgets.EndScrollView();
        }

        private float CalculateImageApiContentHeight(float width)
        {
            float estimatedHeight = 720f;
            return Mathf.Max(estimatedHeight, 760f);
        }

        private void DrawImageApiConnectionSection(Listing_Standard listing)
        {
            listing.CheckboxLabeled("RimChat_ImageApiEnabled".Translate(), ref DiplomacyImageApi.IsEnabled);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("这里应填写支持图生图的图片 API；自拍功能会把游戏中小人的渲染图作为输入图发送。普通纯文生图接口可能无法用于自拍。");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            DrawImageProviderPresetSelector(listing);
            DrawImageApiTextField(listing, "RimChat_ImageApiEndpoint", ref DiplomacyImageApi.Endpoint, "https://...");
            if (string.Equals(DiplomacyImageApi.AuthMode, DiplomacyImageApiConfig.AuthModeNone, StringComparison.OrdinalIgnoreCase))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                listing.Label("RimChat_ImageApiNoAuthHint".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            else
            {
                DrawImageApiTextField(listing, "RimChat_ImageApiKey", ref DiplomacyImageApi.ApiKey, "RimChat_Placeholder_ApiKey".Translate().ToString());
            }

            DrawImageApiTextField(listing, "RimChat_ImageApiModel", ref DiplomacyImageApi.Model, "model-id");

            listing.Label("RimChat_ImageApiDefaultSize".Translate());
            DiplomacyImageApi.DefaultSize = DrawTextFieldWithPlaceholder(listing.GetRect(26f), DiplomacyImageApi.DefaultSize ?? string.Empty, "2560x1440");

            listing.CheckboxLabeled("RimChat_ImageApiDefaultWatermark".Translate(), ref DiplomacyImageApi.DefaultWatermark);
            if (string.Equals(DiplomacyImageApi.ProviderPreset, DiplomacyImageApiConfig.ProviderPresetComfyUiLocal, StringComparison.OrdinalIgnoreCase))
            {
                listing.Label("ComfyUI 图生图加载节点名");
                DiplomacyImageApi.ComfyUiImageLoaderNode = DrawTextFieldWithPlaceholder(listing.GetRect(26f), DiplomacyImageApi.ComfyUiImageLoaderNode ?? string.Empty, "LoadImageBase64");
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                listing.Label("填写你本地 ComfyUI 用于接收 base64 输入图的节点 class_type，例如 LoadImageBase64。这里只改节点名，不改其他 workflow 结构。");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            listing.Label("RimChat_ImageApiTimeout".Translate(DiplomacyImageApi.TimeoutSeconds));
            DiplomacyImageApi.TimeoutSeconds = Mathf.RoundToInt(listing.Slider(DiplomacyImageApi.TimeoutSeconds, 10f, 300f));
            DrawImageConnectionTestButton(listing);

            if (string.Equals(DiplomacyImageApi.ProviderPreset, DiplomacyImageApiConfig.ProviderPresetCustom, StringComparison.OrdinalIgnoreCase))
            {
                listing.CheckboxLabeled("RimChat_ImageApiAdvancedToggle".Translate(), ref DiplomacyImageApi.ShowAdvanced);
                if (DiplomacyImageApi.ShowAdvanced)
                {
                    DrawImageModeSelector(listing);
                    DrawImageSchemaPresetSelector(listing);
                    DrawImageAuthModeSelector(listing);
                    DrawImageApiTextField(listing, "RimChat_ImageApiAuthHeaderName", ref DiplomacyImageApi.ApiKeyHeaderName, "X-API-Key");
                    DrawImageApiTextField(listing, "RimChat_ImageApiAuthQueryName", ref DiplomacyImageApi.ApiKeyQueryName, "api_key");
                    DrawImageApiTextField(listing, "RimChat_ImageApiResponseUrlPath", ref DiplomacyImageApi.ResponseUrlPath, "url,data[0].url");
                    DrawImageApiTextField(listing, "RimChat_ImageApiResponseB64Path", ref DiplomacyImageApi.ResponseB64Path, "b64_json,data[0].b64_json");
                    DrawImageApiTextField(listing, "RimChat_ImageApiAsyncSubmitPath", ref DiplomacyImageApi.AsyncSubmitPath, "/prompt");
                    DrawImageApiTextField(listing, "RimChat_ImageApiAsyncStatusPath", ref DiplomacyImageApi.AsyncStatusPathTemplate, "/history/{job_id}");
                    DrawImageApiTextField(listing, "RimChat_ImageApiAsyncFetchPath", ref DiplomacyImageApi.AsyncImageFetchPath, "/view");
                    listing.Label("RimChat_ImageApiPollInterval".Translate(DiplomacyImageApi.PollIntervalMs));
                    DiplomacyImageApi.PollIntervalMs = Mathf.RoundToInt(listing.Slider(DiplomacyImageApi.PollIntervalMs, 250f, 10000f));
                    listing.Label("RimChat_ImageApiPollAttempts".Translate(DiplomacyImageApi.PollMaxAttempts));
                    DiplomacyImageApi.PollMaxAttempts = Mathf.RoundToInt(listing.Slider(DiplomacyImageApi.PollMaxAttempts, 1f, 600f));
                }
            }

        }

        private void DrawImageApiTextField(Listing_Standard listing, string labelKey, ref string value, string placeholder)
        {
            listing.Label(labelKey.Translate());
            Rect rect = listing.GetRect(26f);
            value = DrawTextFieldWithPlaceholder(rect, value ?? string.Empty, placeholder ?? string.Empty);
        }

        private void DrawImageProviderPresetSelector(Listing_Standard listing)
        {
            listing.Label("RimChat_ImageApiProviderPreset".Translate());
            Rect rect = listing.GetRect(24f);
            if (Widgets.ButtonText(rect, GetImageProviderPresetLabel(DiplomacyImageApi.ProviderPreset)))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("RimChat_ImageApiProviderArk".Translate(), () => ApplyImageProviderPreset(DiplomacyImageApiConfig.ProviderPresetArk)),
                    new FloatMenuOption("RimChat_ImageApiProviderOpenAI".Translate(), () => ApplyImageProviderPreset(DiplomacyImageApiConfig.ProviderPresetOpenAI)),
                    new FloatMenuOption("RimChat_ImageApiProviderSiliconFlow".Translate(), () => ApplyImageProviderPreset(DiplomacyImageApiConfig.ProviderPresetSiliconFlow)),
                    new FloatMenuOption("RimChat_ImageApiProviderComfyUiLocal".Translate(), () => ApplyImageProviderPreset(DiplomacyImageApiConfig.ProviderPresetComfyUiLocal)),
                    new FloatMenuOption("RimChat_ImageApiProviderCustom".Translate(), () => ApplyImageProviderPreset(DiplomacyImageApiConfig.ProviderPresetCustom))
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("RimChat_ImageApiProviderPresetHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void ApplyImageProviderPreset(string preset)
        {
            string normalized = DiplomacyImageApiConfig.NormalizeProviderPreset(preset);
            if (string.Equals(DiplomacyImageApi.ProviderPreset, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DiplomacyImageApi.ProviderPreset = normalized;
            if (!string.Equals(normalized, DiplomacyImageApiConfig.ProviderPresetCustom, StringComparison.OrdinalIgnoreCase))
            {
                DiplomacyImageApi.ShowAdvanced = false;
                DiplomacyImageApi.Endpoint = string.Empty;
                DiplomacyImageApi.Model = string.Empty;
            }

            DiplomacyImageApi.ApplyProviderPresetDefaults();
            DiplomacyImageApi.Normalize();
        }

        private void DrawImageModeSelector(Listing_Standard listing)
        {
            listing.Label("RimChat_ImageApiMode".Translate());
            Rect rect = listing.GetRect(24f);
            if (Widgets.ButtonText(rect, GetImageModeLabel(DiplomacyImageApi.Mode)))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("RimChat_ImageApiModeSyncUrl".Translate(), () => DiplomacyImageApi.Mode = DiplomacyImageApiConfig.ModeSyncUrl),
                    new FloatMenuOption("RimChat_ImageApiModeSyncPayload".Translate(), () => DiplomacyImageApi.Mode = DiplomacyImageApiConfig.ModeSyncPayload),
                    new FloatMenuOption("RimChat_ImageApiModeAsyncJob".Translate(), () => DiplomacyImageApi.Mode = DiplomacyImageApiConfig.ModeAsyncJob)
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("RimChat_ImageApiModeHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawImageSchemaPresetSelector(Listing_Standard listing)
        {
            listing.Label("RimChat_ImageApiSchemaPreset".Translate());
            Rect rect = listing.GetRect(24f);
            if (Widgets.ButtonText(rect, GetImageSchemaPresetLabel(DiplomacyImageApi.SchemaPreset)))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("RimChat_ImageApiSchemaArk".Translate(), () => DiplomacyImageApi.SchemaPreset = DiplomacyImageApiConfig.SchemaPresetArk),
                    new FloatMenuOption("RimChat_ImageApiSchemaOpenAI".Translate(), () => DiplomacyImageApi.SchemaPreset = DiplomacyImageApiConfig.SchemaPresetOpenAI),
                    new FloatMenuOption("RimChat_ImageApiSchemaComfyUI".Translate(), () => DiplomacyImageApi.SchemaPreset = DiplomacyImageApiConfig.SchemaPresetComfyUi),
                    new FloatMenuOption("RimChat_ImageApiSchemaCustom".Translate(), () => DiplomacyImageApi.SchemaPreset = DiplomacyImageApiConfig.SchemaPresetCustom)
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("RimChat_ImageApiSchemaPresetHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawImageAuthModeSelector(Listing_Standard listing)
        {
            listing.Label("RimChat_ImageApiAuthMode".Translate());
            Rect rect = listing.GetRect(24f);
            if (Widgets.ButtonText(rect, GetImageAuthModeLabel(DiplomacyImageApi.AuthMode)))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("RimChat_ImageApiAuthBearer".Translate(), () => DiplomacyImageApi.AuthMode = DiplomacyImageApiConfig.AuthModeBearer),
                    new FloatMenuOption("RimChat_ImageApiAuthApiKeyHeader".Translate(), () => DiplomacyImageApi.AuthMode = DiplomacyImageApiConfig.AuthModeApiKeyHeader),
                    new FloatMenuOption("RimChat_ImageApiAuthQueryKey".Translate(), () => DiplomacyImageApi.AuthMode = DiplomacyImageApiConfig.AuthModeQueryKey),
                    new FloatMenuOption("RimChat_ImageApiAuthNone".Translate(), () => DiplomacyImageApi.AuthMode = DiplomacyImageApiConfig.AuthModeNone)
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void DrawImageConnectionTestButton(Listing_Standard listing)
        {
            Rect buttonRect = listing.GetRect(30f);
            string label = _isTestingImageConnection
                ? "RimChat_TestingConnection".Translate()
                : "RimChat_TestConnectionButton".Translate();

            GUI.color = _isTestingImageConnection ? Color.gray : Color.white;
            bool clicked = Widgets.ButtonText(buttonRect, label, active: !_isTestingImageConnection);
            GUI.color = Color.white;
            if (clicked && !_isTestingImageConnection)
            {
                StartImageConnectionTest();
            }

            if (string.IsNullOrWhiteSpace(_imageConnectionTestStatus))
            {
                return;
            }

            GUI.color = GetImageConnectionStatusColor();
            listing.Label(_imageConnectionTestStatus);
            GUI.color = Color.white;
        }

        private void StartImageConnectionTest()
        {
            _isTestingImageConnection = true;
            _imageConnectionTestStatus = "RimChat_ConnectionTesting".Translate();
            if (AIChatServiceAsync.Instance == null)
            {
                _imageConnectionTestStatus = "RimChat_ConnectionFailed".Translate("Coroutine host unavailable.");
                _isTestingImageConnection = false;
                return;
            }

            AIChatServiceAsync.Instance.StartCoroutine(TestImageConnectionCoroutine());
        }

        private System.Collections.IEnumerator TestImageConnectionCoroutine()
        {
            DiplomacyImageApi.Normalize();
            if (!DiplomacyImageApi.IsConfigured())
            {
                _imageConnectionTestStatus = "RimChat_ConnectionFailed".Translate("RimChat_SendImageConfigInvalid".Translate());
                _isTestingImageConnection = false;
                yield break;
            }
            DiplomacyImageGenerationRequest request = BuildImageProbeRequest();
            bool succeeded = false;
            string reason = string.Empty;
            yield return ProbeImageConnectionCoroutine(request, (ok, why) =>
            {
                succeeded = ok;
                reason = why ?? string.Empty;
            });

            _imageConnectionTestStatus = succeeded
                ? "RimChat_ConnectionSuccess".Translate()
                : "RimChat_ConnectionFailed".Translate(reason);
            _isTestingImageConnection = false;
        }

        private DiplomacyImageGenerationRequest BuildImageProbeRequest()
        {
            return new DiplomacyImageGenerationRequest
            {
                Endpoint = DiplomacyImageApi.Endpoint,
                ApiKey = DiplomacyImageApi.ApiKey,
                Model = DiplomacyImageApi.Model,
                Prompt = "Connectivity test image. Keep it simple.",
                Size = DiplomacyImageApi.DefaultSize,
                Watermark = DiplomacyImageApi.DefaultWatermark,
                TimeoutSeconds = Mathf.Clamp(DiplomacyImageApi.TimeoutSeconds, 10, 60),
                Mode = DiplomacyImageApi.Mode,
                SchemaPreset = DiplomacyImageApi.SchemaPreset,
                AuthMode = DiplomacyImageApi.AuthMode,
                ApiKeyHeaderName = DiplomacyImageApi.ApiKeyHeaderName,
                ApiKeyQueryName = DiplomacyImageApi.ApiKeyQueryName,
                ResponseUrlPath = DiplomacyImageApi.ResponseUrlPath,
                ResponseB64Path = DiplomacyImageApi.ResponseB64Path,
                AsyncSubmitPath = DiplomacyImageApi.AsyncSubmitPath,
                AsyncStatusPathTemplate = DiplomacyImageApi.AsyncStatusPathTemplate,
                AsyncImageFetchPath = DiplomacyImageApi.AsyncImageFetchPath,
                ComfyUiImageLoaderNode = DiplomacyImageApi.ComfyUiImageLoaderNode,
                PollIntervalMs = DiplomacyImageApi.PollIntervalMs,
                PollMaxAttempts = DiplomacyImageApi.PollMaxAttempts
            };
        }

        private System.Collections.IEnumerator ProbeImageConnectionCoroutine(
            DiplomacyImageGenerationRequest request,
            Action<bool, string> onFinished)
        {
            string mode = DiplomacyImageApiConfig.NormalizeMode(request.Mode);
            if (string.Equals(mode, DiplomacyImageApiConfig.ModeAsyncJob, StringComparison.OrdinalIgnoreCase))
            {
                yield return ProbeAsyncImageConnectionCoroutine(request, onFinished);
                yield break;
            }

            yield return ProbeSyncImageConnectionCoroutine(request, onFinished);
        }

        private System.Collections.IEnumerator ProbeSyncImageConnectionCoroutine(
            DiplomacyImageGenerationRequest request,
            Action<bool, string> onFinished)
        {
            request.Normalize();
            string url = DiplomacyImageProviderCompat.BuildAuthAppliedUrl(request.Endpoint, request);
            string body = DiplomacyImageProviderCompat.BuildSchemaAwareRequestBody(request, true, BuildArkProbeBody, BuildArkProbeBodyWithoutSize);
            ProbeResult probe = default;
            yield return SendImageProbeRequestCoroutine(url, "POST", body, request, result => probe = result);
            if (probe.ResponseCode == 400 && IsSizeProbeFailure(probe.Error, probe.ResponseBody))
            {
                string fallbackBody = DiplomacyImageProviderCompat.BuildSchemaAwareRequestBody(request, false, BuildArkProbeBody, BuildArkProbeBodyWithoutSize);
                yield return SendImageProbeRequestCoroutine(url, "POST", fallbackBody, request, result => probe = result);
            }

            if (probe.IsAuthError)
            {
                onFinished?.Invoke(false, "RimChat_InvalidAPIKey".Translate());
                yield break;
            }

            onFinished?.Invoke(probe.IsReachable, probe.ToReason());
        }

        private static bool IsSizeProbeFailure(string error, string responseBody)
        {
            string merged = $"{error} {responseBody}".ToLowerInvariant();
            return merged.Contains("\"size\"")
                || merged.Contains("parameter `size`")
                || merged.Contains("must be at least")
                || merged.Contains("invalid parameter");
        }

        private System.Collections.IEnumerator ProbeAsyncImageConnectionCoroutine(
            DiplomacyImageGenerationRequest request,
            Action<bool, string> onFinished)
        {
            request.Normalize();
            string submitUrl = DiplomacyImageProviderCompat.ResolveAsyncSubmitUrl(request);
            string body = DiplomacyImageProviderCompat.BuildAsyncSubmitBody(
                request,
                req => DiplomacyImageProviderCompat.BuildSchemaAwareRequestBody(req, true, BuildArkProbeBody, BuildArkProbeBodyWithoutSize));
            ProbeResult probe = default;
            yield return SendImageProbeRequestCoroutine(submitUrl, "POST", body, request, result => probe = result);
            if (probe.IsAuthError)
            {
                onFinished?.Invoke(false, "RimChat_InvalidAPIKey".Translate());
                yield break;
            }

            if (probe.IsSuccess && DiplomacyImageProviderCompat.TryExtractPromptId(probe.ResponseBody, out _))
            {
                onFinished?.Invoke(true, string.Empty);
                yield break;
            }

            onFinished?.Invoke(probe.IsReachable, probe.ToReason());
        }

        private System.Collections.IEnumerator SendImageProbeRequestCoroutine(
            string url,
            string method,
            string body,
            DiplomacyImageGenerationRequest request,
            Action<ProbeResult> onFinished)
        {
            string resolvedUrl = DiplomacyImageProviderCompat.BuildAuthAppliedUrl(url, request);
            using (var web = new UnityWebRequest(resolvedUrl, method))
            {
                web.downloadHandler = new DownloadHandlerBuffer();
                web.timeout = Mathf.Clamp(request.TimeoutSeconds, 5, 60);
                DiplomacyImageProviderCompat.ApplyAuth(web, request);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    byte[] postData = Encoding.UTF8.GetBytes(body);
                    web.uploadHandler = new UploadHandlerRaw(postData);
                    web.SetRequestHeader("Content-Type", "application/json");
                }

                yield return web.SendWebRequest();
                onFinished?.Invoke(new ProbeResult(
                    web.result,
                    web.responseCode,
                    web.error ?? string.Empty,
                    web.downloadHandler?.text ?? string.Empty));
            }
        }

        private static string BuildArkProbeBody(DiplomacyImageGenerationRequest request)
        {
            string size = DiplomacyImageApiConfig.NormalizeImageSize(request.Size, DiplomacyImageApiConfig.DefaultImageSize);
            string watermark = request.Watermark ? "true" : "false";
            return "{"
                + $"\"model\":\"{EscapeProbeJson(request.Model)}\","
                + $"\"prompt\":\"{EscapeProbeJson(request.Prompt)}\","
                + "\"sequential_image_generation\":\"disabled\","
                + "\"response_format\":\"url\","
                + "\"stream\":false,"
                + $"\"size\":\"{EscapeProbeJson(size)}\","
                + $"\"watermark\":{watermark}"
                + "}";
        }

        private static string BuildArkProbeBodyWithoutSize(DiplomacyImageGenerationRequest request)
        {
            string watermark = request.Watermark ? "true" : "false";
            return "{"
                + $"\"model\":\"{EscapeProbeJson(request.Model)}\","
                + $"\"prompt\":\"{EscapeProbeJson(request.Prompt)}\","
                + "\"sequential_image_generation\":\"disabled\","
                + "\"response_format\":\"url\","
                + "\"stream\":false,"
                + $"\"watermark\":{watermark}"
                + "}";
        }

        private static string EscapeProbeJson(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private static string GetImageModeLabel(string mode)
        {
            switch (DiplomacyImageApiConfig.NormalizeMode(mode))
            {
                case DiplomacyImageApiConfig.ModeSyncPayload:
                    return "RimChat_ImageApiModeSyncPayload".Translate();
                case DiplomacyImageApiConfig.ModeAsyncJob:
                    return "RimChat_ImageApiModeAsyncJob".Translate();
                default:
                    return "RimChat_ImageApiModeSyncUrl".Translate();
            }
        }

        private static string GetImageSchemaPresetLabel(string preset)
        {
            string normalized = DiplomacyImageApiConfig.NormalizeSchemaPreset(preset, string.Empty);
            if (string.Equals(normalized, DiplomacyImageApiConfig.SchemaPresetOpenAI, StringComparison.OrdinalIgnoreCase))
            {
                return "RimChat_ImageApiSchemaOpenAI".Translate();
            }
            if (string.Equals(normalized, DiplomacyImageApiConfig.SchemaPresetComfyUi, StringComparison.OrdinalIgnoreCase))
            {
                return "RimChat_ImageApiSchemaComfyUI".Translate();
            }
            if (string.Equals(normalized, DiplomacyImageApiConfig.SchemaPresetCustom, StringComparison.OrdinalIgnoreCase))
            {
                return "RimChat_ImageApiSchemaCustom".Translate();
            }

            return "RimChat_ImageApiSchemaArk".Translate();
        }

        private static string GetImageAuthModeLabel(string mode)
        {
            string normalized = DiplomacyImageApiConfig.NormalizeAuthMode(mode, DiplomacyImageApiConfig.SchemaPresetArk);
            if (string.Equals(normalized, DiplomacyImageApiConfig.AuthModeApiKeyHeader, StringComparison.OrdinalIgnoreCase))
            {
                return "RimChat_ImageApiAuthApiKeyHeader".Translate();
            }
            if (string.Equals(normalized, DiplomacyImageApiConfig.AuthModeQueryKey, StringComparison.OrdinalIgnoreCase))
            {
                return "RimChat_ImageApiAuthQueryKey".Translate();
            }
            if (string.Equals(normalized, DiplomacyImageApiConfig.AuthModeNone, StringComparison.OrdinalIgnoreCase))
            {
                return "RimChat_ImageApiAuthNone".Translate();
            }

            return "RimChat_ImageApiAuthBearer".Translate();
        }

        private static string GetImageProviderPresetLabel(string preset)
        {
            string normalized = DiplomacyImageApiConfig.NormalizeProviderPreset(preset);
            if (string.Equals(normalized, DiplomacyImageApiConfig.ProviderPresetOpenAI, StringComparison.OrdinalIgnoreCase))
            {
                return "RimChat_ImageApiProviderOpenAI".Translate();
            }
            if (string.Equals(normalized, DiplomacyImageApiConfig.ProviderPresetSiliconFlow, StringComparison.OrdinalIgnoreCase))
            {
                return "RimChat_ImageApiProviderSiliconFlow".Translate();
            }
            if (string.Equals(normalized, DiplomacyImageApiConfig.ProviderPresetComfyUiLocal, StringComparison.OrdinalIgnoreCase))
            {
                return "RimChat_ImageApiProviderComfyUiLocal".Translate();
            }
            if (string.Equals(normalized, DiplomacyImageApiConfig.ProviderPresetCustom, StringComparison.OrdinalIgnoreCase))
            {
                return "RimChat_ImageApiProviderCustom".Translate();
            }

            return "RimChat_ImageApiProviderArk".Translate();
        }

        private Color GetImageConnectionStatusColor()
        {
            if (_imageConnectionTestStatus.Contains("RimChat_ConnectionSuccess".Translate().ToString()))
            {
                return Color.green;
            }
            if (_imageConnectionTestStatus.Contains("RimChat_ConnectionFailed".Translate().ToString()))
            {
                return Color.red;
            }
            return Color.yellow;
        }

        private readonly struct ProbeResult
        {
            public readonly UnityWebRequest.Result Result;
            public readonly long ResponseCode;
            public readonly string Error;
            public readonly string ResponseBody;

            public ProbeResult(UnityWebRequest.Result result, long responseCode, string error, string responseBody)
            {
                Result = result;
                ResponseCode = responseCode;
                Error = error ?? string.Empty;
                ResponseBody = responseBody ?? string.Empty;
            }

            public bool IsSuccess => Result == UnityWebRequest.Result.Success || ResponseCode == 200;
            public bool IsAuthError => ResponseCode == 401 || ResponseCode == 403;
            public bool IsReachable => ResponseCode > 0 && ResponseCode != 404 && !IsAuthError;

            public string ToReason()
            {
                if (ResponseCode > 0)
                {
                    return $"HTTP {ResponseCode}";
                }
                return string.IsNullOrWhiteSpace(Error) ? "unknown error" : Error;
            }
        }

        private void EnsureSendImageCaptionDefaults()
        {
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

using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using RimChat.AI;
using RimChat.Config;
using RimChat.Core;
using RimChat.Memory;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: AIChatServiceAsync coroutine host, UnityWebRequest, RimWorld save metadata, and image API settings.
 /// Responsibility: call ARK image endpoint, download URL response image, and persist local cache path for chat inline rendering.
 ///</summary>
    public sealed class DiplomacyImageGenerationService
    {
        private const string PromptNpcFolderName = "Prompt";
        private const string PromptNpcSubFolderName = "NPC";
        private const string ImageCacheSubFolderName = "diplomacy_images";
        private static readonly Regex UrlFieldRegex = new Regex("\"url\"\\s*:\\s*\"(?<url>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly BindingFlags InstanceStringMemberBinding =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

        private static DiplomacyImageGenerationService _instance;
        public static DiplomacyImageGenerationService Instance => _instance ??= new DiplomacyImageGenerationService();

        public void GenerateImage(
            DiplomacyImageGenerationRequest request,
            Action<DiplomacyImageGenerationResult> onCompleted)
        {
            if (ImageGenerationAvailability.IsBlocked())
            {
                onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail(ImageGenerationAvailability.GetBlockedMessage()));
                return;
            }

            if (request == null)
            {
                onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail("Image request is null."));
                return;
            }

            request.Normalize();
            if (string.IsNullOrWhiteSpace(request.Endpoint))
            {
                onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail("Image endpoint is empty."));
                return;
            }

            if (string.IsNullOrWhiteSpace(request.ApiKey))
            {
                onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail("Image API key is empty."));
                return;
            }

            if (string.IsNullOrWhiteSpace(request.Model))
            {
                onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail("Image model is empty."));
                return;
            }

            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail("Image prompt is empty."));
                return;
            }

            AIChatServiceAsync.Instance.StartCoroutine(GenerateImageCoroutine(request, onCompleted));
        }

        private IEnumerator GenerateImageCoroutine(
            DiplomacyImageGenerationRequest request,
            Action<DiplomacyImageGenerationResult> onCompleted)
        {
            if (string.Equals(DiplomacyImageApiConfig.NormalizeMode(request.Mode), DiplomacyImageApiConfig.ModeAsyncJob, StringComparison.OrdinalIgnoreCase))
            {
                yield return GenerateAsyncJobImageCoroutine(request, onCompleted);
                yield break;
            }

            yield return GenerateSyncImageCoroutine(request, onCompleted);
        }

        private IEnumerator GenerateSyncImageCoroutine(
            DiplomacyImageGenerationRequest request,
            Action<DiplomacyImageGenerationResult> onCompleted)
        {
            string responseBody = string.Empty;
            string submitUrl = DiplomacyImageProviderCompat.BuildAuthAppliedUrl(request.Endpoint, request);
            string[] requestBodies =
            {
                DiplomacyImageProviderCompat.BuildSchemaAwareRequestBody(request, true, BuildArkRequestBody, BuildArkRequestBodyWithoutSize),
                DiplomacyImageProviderCompat.BuildSchemaAwareRequestBody(request, false, BuildArkRequestBody, BuildArkRequestBodyWithoutSize)
            };

            for (int attempt = 0; attempt < requestBodies.Length; attempt++)
            {
                byte[] postData = Encoding.UTF8.GetBytes(requestBodies[attempt]);
                using (var requestWeb = new UnityWebRequest(submitUrl, "POST"))
                {
                    requestWeb.uploadHandler = new UploadHandlerRaw(postData);
                    requestWeb.downloadHandler = new DownloadHandlerBuffer();
                    requestWeb.timeout = request.TimeoutSeconds;
                    requestWeb.SetRequestHeader("Content-Type", "application/json");
                    DiplomacyImageProviderCompat.ApplyAuth(requestWeb, request);

                    yield return requestWeb.SendWebRequest();
                    responseBody = requestWeb.downloadHandler?.text ?? string.Empty;
                    if (requestWeb.result == UnityWebRequest.Result.Success)
                    {
                        break;
                    }

                    bool shouldRetryWithoutSize =
                        ShouldRetryWithoutSize(request, attempt, requestWeb.responseCode, requestWeb.error, responseBody);
                    if (shouldRetryWithoutSize)
                    {
                        Log.Warning("[RimChat] send_image retrying without size field due to size validation failure.");
                        continue;
                    }

                    string error = ComposeWebError("image generation", requestWeb, responseBody);
                    onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail(error));
                    yield break;
                }
            }

            if (!DiplomacyImageProviderCompat.TryExtractImagePayload(responseBody, request, TryExtractImageUrl, out string imageUrl, out byte[] inlineBytes))
            {
                onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail("Image API returned no usable image payload."));
                yield break;
            }

            yield return PersistImageResult(request, imageUrl, inlineBytes, onCompleted);
        }

        private IEnumerator GenerateAsyncJobImageCoroutine(
            DiplomacyImageGenerationRequest request,
            Action<DiplomacyImageGenerationResult> onCompleted)
        {
            string submitUrl = DiplomacyImageProviderCompat.ResolveAsyncSubmitUrl(request);
            string submitBody = DiplomacyImageProviderCompat.BuildAsyncSubmitBody(
                request,
                req => DiplomacyImageProviderCompat.BuildSchemaAwareRequestBody(req, true, BuildArkRequestBody, BuildArkRequestBodyWithoutSize));
            byte[] postData = Encoding.UTF8.GetBytes(submitBody);
            string submitResponse = string.Empty;
            using (var submitWeb = new UnityWebRequest(DiplomacyImageProviderCompat.BuildAuthAppliedUrl(submitUrl, request), "POST"))
            {
                submitWeb.uploadHandler = new UploadHandlerRaw(postData);
                submitWeb.downloadHandler = new DownloadHandlerBuffer();
                submitWeb.timeout = request.TimeoutSeconds;
                submitWeb.SetRequestHeader("Content-Type", "application/json");
                DiplomacyImageProviderCompat.ApplyAuth(submitWeb, request);
                yield return submitWeb.SendWebRequest();
                submitResponse = submitWeb.downloadHandler?.text ?? string.Empty;
                if (submitWeb.result != UnityWebRequest.Result.Success)
                {
                    string error = BuildAsyncSubmitError(request, submitWeb, submitResponse);
                    onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail(error));
                    yield break;
                }
            }

            if (!DiplomacyImageProviderCompat.TryExtractPromptId(submitResponse, out string promptId))
            {
                onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail("Async image API returned no prompt_id."));
                yield break;
            }

            string statusUrl = DiplomacyImageProviderCompat.ResolveAsyncStatusUrl(request, promptId);
            for (int attempt = 0; attempt < request.PollMaxAttempts; attempt++)
            {
                using (var statusWeb = UnityWebRequest.Get(DiplomacyImageProviderCompat.BuildAuthAppliedUrl(statusUrl, request)))
                {
                    statusWeb.timeout = request.TimeoutSeconds;
                    DiplomacyImageProviderCompat.ApplyAuth(statusWeb, request);
                    yield return statusWeb.SendWebRequest();
                    string statusBody = statusWeb.downloadHandler?.text ?? string.Empty;
                    if (statusWeb.result == UnityWebRequest.Result.Success &&
                        DiplomacyImageProviderCompat.TryExtractComfyImageAddress(statusBody, request, out string comfyUrl))
                    {
                        yield return PersistImageResult(request, comfyUrl, null, onCompleted);
                        yield break;
                    }
                }

                yield return new WaitForSeconds(Mathf.Max(0.1f, request.PollIntervalMs / 1000f));
            }

            onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail("Async image job timed out before image became available."));
        }

        private IEnumerator PersistImageResult(
            DiplomacyImageGenerationRequest request,
            string imageUrl,
            byte[] inlineBytes,
            Action<DiplomacyImageGenerationResult> onCompleted)
        {
            byte[] imageBytes = inlineBytes;
            if (imageBytes == null || imageBytes.Length == 0)
            {
                using (var downloadWeb = UnityWebRequest.Get(DiplomacyImageProviderCompat.BuildAuthAppliedUrl(imageUrl, request)))
                {
                    downloadWeb.timeout = request.TimeoutSeconds;
                    DiplomacyImageProviderCompat.ApplyAuth(downloadWeb, request);
                    yield return downloadWeb.SendWebRequest();
                    if (downloadWeb.result != UnityWebRequest.Result.Success)
                    {
                        string downloadError = ComposeWebError("image download", downloadWeb, downloadWeb.downloadHandler?.text);
                        onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail(downloadError));
                        yield break;
                    }

                    imageBytes = downloadWeb.downloadHandler?.data;
                    if (imageBytes == null || imageBytes.Length == 0)
                    {
                        onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail("Downloaded image bytes are empty."));
                        yield break;
                    }
                }
            }

            if (!TrySaveImageBytes(request.Faction, imageBytes, out string localPath, out string saveError))
            {
                onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail(saveError));
                yield break;
            }

            onCompleted?.Invoke(DiplomacyImageGenerationResult.Ok(localPath, imageUrl, request.Caption));
        }

        private static string BuildArkRequestBody(DiplomacyImageGenerationRequest request)
        {
            string model = EscapeJson(request.Model);
            string prompt = EscapeJson(request.Prompt);
            string normalizedSize = DiplomacyImageApiConfig.NormalizeImageSize(
                request.Size,
                DiplomacyImageApiConfig.DefaultImageSize);
            request.Size = normalizedSize;
            string size = EscapeJson(normalizedSize);
            string watermark = request.Watermark ? "true" : "false";
            Log.Message($"[RimChat] send_image request normalized size={normalizedSize}");
            return "{"
                + $"\"model\":\"{model}\","
                + $"\"prompt\":\"{prompt}\","
                + "\"sequential_image_generation\":\"disabled\","
                + "\"response_format\":\"url\","
                + "\"stream\":false,"
                + $"\"size\":\"{size}\","
                + $"\"watermark\":{watermark}"
                + "}";
        }

        private static string BuildArkRequestBodyWithoutSize(DiplomacyImageGenerationRequest request)
        {
            string model = EscapeJson(request.Model);
            string prompt = EscapeJson(request.Prompt);
            string watermark = request.Watermark ? "true" : "false";
            return "{"
                + $"\"model\":\"{model}\","
                + $"\"prompt\":\"{prompt}\","
                + "\"sequential_image_generation\":\"disabled\","
                + "\"response_format\":\"url\","
                + "\"stream\":false,"
                + $"\"watermark\":{watermark}"
                + "}";
        }


        private static bool IsSizeValidationError(long responseCode, string requestError, string responseBody)
        {
            if (responseCode != 400)
            {
                return false;
            }

            string merged = $"{requestError} {responseBody}".ToLowerInvariant();
            return merged.Contains("parameter `size`")
                || merged.Contains("\"size\"")
                || merged.Contains("invalid parameter")
                || merged.Contains("must be at least");
        }

        private static bool ShouldRetryWithoutSize(
            DiplomacyImageGenerationRequest request,
            int attempt,
            long responseCode,
            string requestError,
            string responseBody)
        {
            if (attempt != 0)
            {
                return false;
            }

            if (IsSizeValidationError(responseCode, requestError, responseBody))
            {
                return true;
            }

            bool isArkSchema = string.Equals(
                request?.SchemaPreset,
                DiplomacyImageApiConfig.SchemaPresetArk,
                StringComparison.OrdinalIgnoreCase);
            if (isArkSchema)
            {
                return false;
            }

            // Some OpenAI-compatible providers may return 5xx/empty body for unsupported size.
            if (responseCode >= 500)
            {
                return true;
            }

            string merged = $"{requestError} {responseBody}".ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(merged))
            {
                return false;
            }

            return merged.Contains("invalid size")
                || merged.Contains("unsupported size")
                || merged.Contains("image size")
                || merged.Contains("resolution");
        }

        private static bool TryExtractImageUrl(string responseBody, out string imageUrl)
        {
            imageUrl = string.Empty;
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return false;
            }

            Match match = UrlFieldRegex.Match(responseBody);
            if (match.Success)
            {
                string value = match.Groups["url"].Value;
                value = value.Replace("\\/", "/").Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    imageUrl = value;
                    return true;
                }
            }

            int httpIndex = responseBody.IndexOf("http", StringComparison.OrdinalIgnoreCase);
            if (httpIndex < 0)
            {
                return false;
            }

            int endIndex = responseBody.IndexOfAny(new[] { '"', '\'', ' ', '\n', '\r', '\t' }, httpIndex);
            if (endIndex <= httpIndex)
            {
                endIndex = responseBody.Length;
            }

            string fallback = responseBody.Substring(httpIndex, endIndex - httpIndex).Trim();
            if (fallback.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                fallback.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                imageUrl = fallback;
                return true;
            }

            return false;
        }

        private static bool TrySaveImageBytes(
            Faction faction,
            byte[] imageBytes,
            out string localPath,
            out string error)
        {
            localPath = string.Empty;
            error = string.Empty;

            try
            {
                string cacheDir = ResolveImageCacheDirectory();
                if (!Directory.Exists(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }

                string factionName = (faction?.Name ?? "UnknownFaction").SanitizeFileName();
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                int salt = Rand.RangeInclusive(1000, 9999);
                string fileName = $"{factionName}_{stamp}_{salt}.png";
                string path = Path.Combine(cacheDir, fileName);
                File.WriteAllBytes(path, imageBytes);
                localPath = path;
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to persist image cache: {ex.Message}";
                return false;
            }
        }

        private static string ResolveImageCacheDirectory()
        {
            string saveKey = ResolveCurrentSaveKey();
            string root = ResolvePromptNpcRootPath();
            return Path.Combine(root, saveKey, ImageCacheSubFolderName);
        }

        private static string ResolvePromptNpcRootPath()
        {
            try
            {
                var mod = LoadedModManager.GetMod<RimChatMod>();
                if (mod?.Content != null)
                {
                    string root = Path.Combine(mod.Content.RootDir, PromptNpcFolderName, PromptNpcSubFolderName);
                    if (!Directory.Exists(root))
                    {
                        Directory.CreateDirectory(root);
                    }
                    return root;
                }
            }
            catch
            {
            }

            string fallback = Path.Combine(GenFilePaths.ConfigFolderPath, "RimChat", PromptNpcFolderName, PromptNpcSubFolderName);
            if (!Directory.Exists(fallback))
            {
                Directory.CreateDirectory(fallback);
            }
            return fallback;
        }

        private static string ResolveCurrentSaveKey()
        {
            return SaveScopeKeyResolver.ResolveOrThrow();
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string ComposeWebError(string stage, UnityWebRequest request, string responseBody)
        {
            string reason = request?.error ?? "unknown error";
            long code = request?.responseCode ?? 0;
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                return $"{stage} failed ({code}): {reason}. response={responseBody}";
            }
            return $"{stage} failed ({code}): {reason}";
        }

        private static string BuildAsyncSubmitError(
            DiplomacyImageGenerationRequest request,
            UnityWebRequest webRequest,
            string responseBody)
        {
            string generic = ComposeWebError("async image submit", webRequest, responseBody);
            bool comfySchema = string.Equals(
                request?.SchemaPreset,
                DiplomacyImageApiConfig.SchemaPresetComfyUi,
                StringComparison.OrdinalIgnoreCase);
            if (!comfySchema)
            {
                return generic;
            }

            string lower = (responseBody ?? string.Empty).ToLowerInvariant();
            bool ckptValidationFailed =
                lower.Contains("prompt_outputs_failed_validation")
                && lower.Contains("ckpt_name")
                && (lower.Contains("not in[]") || lower.Contains("value_not_in_list"));
            if (!ckptValidationFailed)
            {
                return generic;
            }

            string model = request?.Model;
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "<empty>";
            }

            return "RimChat_ComfyCheckpointMissing".Translate(model);
        }
    }

    /// <summary>/// Dependencies: diplomacy image API settings and action parameter extraction layer.
 /// Responsibility: carry normalized image generation request data into API generation coroutine.
 ///</summary>
    public sealed class DiplomacyImageGenerationRequest
    {
        public Faction Faction;
        public string Endpoint = string.Empty;
        public string ApiKey = string.Empty;
        public string Model = string.Empty;
        public string Prompt = string.Empty;
        public string Caption = string.Empty;
        public string Size = DiplomacyImageApiConfig.DefaultImageSize;
        public bool Watermark;
        public int TimeoutSeconds = 120;
        public string Mode = DiplomacyImageApiConfig.ModeSyncUrl;
        public string SchemaPreset = DiplomacyImageApiConfig.SchemaPresetArk;
        public string AuthMode = DiplomacyImageApiConfig.AuthModeBearer;
        public string ApiKeyHeaderName = "X-API-Key";
        public string ApiKeyQueryName = "api_key";
        public string ResponseUrlPath = "url,data[0].url,images[0].url,output[0].url";
        public string ResponseB64Path = "b64_json,data[0].b64_json,images[0].b64_json";
        public string AsyncSubmitPath = "/prompt";
        public string AsyncStatusPathTemplate = "/history/{job_id}";
        public string AsyncImageFetchPath = "/view";
        public int PollIntervalMs = 1000;
        public int PollMaxAttempts = 180;

        public void Normalize()
        {
            Endpoint = DiplomacyImageApiConfig.NormalizeText(Endpoint);
            ApiKey = DiplomacyImageApiConfig.NormalizeText(ApiKey);
            Model = DiplomacyImageApiConfig.NormalizeText(Model);
            Prompt = (Prompt ?? string.Empty).Trim();
            Caption = (Caption ?? string.Empty).Trim();
            Size = DiplomacyImageApiConfig.NormalizeImageSize(Size, DiplomacyImageApiConfig.DefaultImageSize);
            Mode = DiplomacyImageApiConfig.NormalizeMode(Mode);
            SchemaPreset = DiplomacyImageApiConfig.NormalizeSchemaPreset(SchemaPreset, Endpoint);
            AuthMode = DiplomacyImageApiConfig.NormalizeAuthMode(AuthMode, SchemaPreset);
            ApiKeyHeaderName = DiplomacyImageApiConfig.NormalizeText(ApiKeyHeaderName);
            ApiKeyQueryName = DiplomacyImageApiConfig.NormalizeText(ApiKeyQueryName);
            ResponseUrlPath = DiplomacyImageApiConfig.NormalizeText(ResponseUrlPath);
            ResponseB64Path = DiplomacyImageApiConfig.NormalizeText(ResponseB64Path);
            AsyncSubmitPath = DiplomacyImageApiConfig.NormalizeText(AsyncSubmitPath);
            AsyncStatusPathTemplate = DiplomacyImageApiConfig.NormalizeText(AsyncStatusPathTemplate);
            AsyncImageFetchPath = DiplomacyImageApiConfig.NormalizeText(AsyncImageFetchPath);

            TimeoutSeconds = Math.Max(10, Math.Min(300, TimeoutSeconds));
            PollIntervalMs = Math.Max(250, Math.Min(10000, PollIntervalMs));
            PollMaxAttempts = Math.Max(1, Math.Min(600, PollMaxAttempts));
        }
    }

    /// <summary>/// Dependencies: none.
 /// Responsibility: represent image generation completion state for UI/session update.
 ///</summary>
    public sealed class DiplomacyImageGenerationResult
    {
        public bool Success;
        public string Error = string.Empty;
        public string LocalPath = string.Empty;
        public string SourceUrl = string.Empty;
        public string Caption = string.Empty;

        public static DiplomacyImageGenerationResult Ok(string localPath, string sourceUrl, string caption)
        {
            return new DiplomacyImageGenerationResult
            {
                Success = true,
                LocalPath = localPath ?? string.Empty,
                SourceUrl = sourceUrl ?? string.Empty,
                Caption = caption ?? string.Empty
            };
        }

        public static DiplomacyImageGenerationResult Fail(string error)
        {
            return new DiplomacyImageGenerationResult
            {
                Success = false,
                Error = string.IsNullOrWhiteSpace(error) ? "Unknown image generation error." : error.Trim()
            };
        }
    }
}

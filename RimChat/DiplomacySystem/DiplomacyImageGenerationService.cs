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
            string requestBody = BuildArkRequestBody(request);
            byte[] postData = Encoding.UTF8.GetBytes(requestBody);

            using (var requestWeb = new UnityWebRequest(request.Endpoint, "POST"))
            {
                requestWeb.uploadHandler = new UploadHandlerRaw(postData);
                requestWeb.downloadHandler = new DownloadHandlerBuffer();
                requestWeb.timeout = request.TimeoutSeconds;
                requestWeb.SetRequestHeader("Content-Type", "application/json");
                requestWeb.SetRequestHeader("Authorization", $"Bearer {request.ApiKey}");

                yield return requestWeb.SendWebRequest();
                if (requestWeb.result != UnityWebRequest.Result.Success)
                {
                    string error = ComposeWebError("image generation", requestWeb, requestWeb.downloadHandler?.text);
                    onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail(error));
                    yield break;
                }

                string responseBody = requestWeb.downloadHandler?.text ?? string.Empty;
                if (!TryExtractImageUrl(responseBody, out string imageUrl))
                {
                    onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail("Image API returned no usable URL."));
                    yield break;
                }

                using (var downloadWeb = UnityWebRequest.Get(imageUrl))
                {
                    downloadWeb.timeout = request.TimeoutSeconds;
                    yield return downloadWeb.SendWebRequest();
                    if (downloadWeb.result != UnityWebRequest.Result.Success)
                    {
                        string downloadError = ComposeWebError("image download", downloadWeb, downloadWeb.downloadHandler?.text);
                        onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail(downloadError));
                        yield break;
                    }

                    byte[] imageBytes = downloadWeb.downloadHandler?.data;
                    if (imageBytes == null || imageBytes.Length == 0)
                    {
                        onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail("Downloaded image bytes are empty."));
                        yield break;
                    }

                    if (!TrySaveImageBytes(request.Faction, imageBytes, out string localPath, out string saveError))
                    {
                        onCompleted?.Invoke(DiplomacyImageGenerationResult.Fail(saveError));
                        yield break;
                    }

                    onCompleted?.Invoke(DiplomacyImageGenerationResult.Ok(localPath, imageUrl, request.Caption));
                }
            }
        }

        private static string BuildArkRequestBody(DiplomacyImageGenerationRequest request)
        {
            string model = EscapeJson(request.Model);
            string prompt = EscapeJson(request.Prompt);
            string size = EscapeJson(request.Size);
            string watermark = request.Watermark ? "true" : "false";
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
            string saveName = ResolveCurrentSaveName();
            uint hash = ComputeStableHash(saveName);
            string key = $"Save_{hash}_{saveName}".SanitizeFileName();
            return string.IsNullOrWhiteSpace(key) ? "Save_Default" : key;
        }

        private static string ResolveCurrentSaveName()
        {
            object gameInfo = Current.Game?.Info;
            if (gameInfo == null)
            {
                return "Default";
            }

            string[] members = { "name", "Name", "fileName", "FileName" };
            for (int i = 0; i < members.Length; i++)
            {
                string value = ReadStringMember(gameInfo, members[i]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.SanitizeFileName();
                }
            }

            return "Default";
        }

        private static string ReadStringMember(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
            {
                return string.Empty;
            }

            try
            {
                PropertyInfo prop = target.GetType().GetProperty(memberName, InstanceStringMemberBinding);
                if (prop?.PropertyType == typeof(string))
                {
                    string value = prop.GetValue(target, null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                FieldInfo field = target.GetType().GetField(memberName, InstanceStringMemberBinding);
                if (field?.FieldType == typeof(string))
                {
                    string value = field.GetValue(target) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static uint ComputeStableHash(string text)
        {
            string input = string.IsNullOrWhiteSpace(text) ? "Default" : text;
            uint hash = 2166136261;
            for (int i = 0; i < input.Length; i++)
            {
                hash ^= input[i];
                hash *= 16777619;
            }
            return hash;
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
        public string Size = "1024x1024";
        public bool Watermark;
        public int TimeoutSeconds = 120;

        public void Normalize()
        {
            Endpoint = DiplomacyImageApiConfig.NormalizeText(Endpoint);
            ApiKey = DiplomacyImageApiConfig.NormalizeText(ApiKey);
            Model = DiplomacyImageApiConfig.NormalizeText(Model);
            Prompt = (Prompt ?? string.Empty).Trim();
            Caption = (Caption ?? string.Empty).Trim();
            Size = DiplomacyImageApiConfig.NormalizeText(Size);
            if (string.IsNullOrWhiteSpace(Size))
            {
                Size = "1024x1024";
            }

            TimeoutSeconds = Math.Max(10, Math.Min(300, TimeoutSeconds));
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

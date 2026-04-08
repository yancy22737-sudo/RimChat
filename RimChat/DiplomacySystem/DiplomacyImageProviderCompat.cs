using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimChat.Config;
using UnityEngine;
using UnityEngine.Networking;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: image request config model, UnityWebRequest, and lightweight regex extractors.
 /// Responsibility: provide provider-compat helpers for auth routing, response parsing, and async image job URLs.
 ///</summary>
    internal static class DiplomacyImageProviderCompat
    {
        internal delegate bool UrlExtractor(string responseBody, out string imageUrl);

        private static readonly Regex PromptIdRegex = new Regex("\"prompt_id\"\\s*:\\s*\"(?<id>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FilenameRegex = new Regex("\"filename\"\\s*:\\s*\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SubfolderRegex = new Regex("\"subfolder\"\\s*:\\s*\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TypeRegex = new Regex("\"type\"\\s*:\\s*\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string BuildSchemaAwareRequestBody(
            DiplomacyImageGenerationRequest request,
            bool includeSize,
            Func<DiplomacyImageGenerationRequest, string> arkBuilder,
            Func<DiplomacyImageGenerationRequest, string> arkWithoutSizeBuilder)
        {
            if (string.Equals(request.SchemaPreset, DiplomacyImageApiConfig.SchemaPresetOpenAI, StringComparison.OrdinalIgnoreCase))
            {
                return BuildOpenAICompatibleBody(request, includeSize);
            }

            return includeSize ? arkBuilder(request) : arkWithoutSizeBuilder(request);
        }

        public static string BuildAsyncSubmitBody(
            DiplomacyImageGenerationRequest request,
            Func<DiplomacyImageGenerationRequest, string> syncBodyBuilder)
        {
            if (!string.Equals(request.SchemaPreset, DiplomacyImageApiConfig.SchemaPresetComfyUi, StringComparison.OrdinalIgnoreCase))
            {
                return syncBodyBuilder(request);
            }

            string imageInput = ResolveSourceImageInput(request);
            if (string.IsNullOrWhiteSpace(imageInput))
            {
                ParseSize(request.Size, out int width, out int height);
                string model = EscapeJson(request.Model);
                string prompt = EscapeJson(request.Prompt);
                return "{"
                    + "\"prompt\":{"
                    + "\"3\":{\"class_type\":\"KSampler\",\"inputs\":{\"seed\":1,\"steps\":20,\"cfg\":7,\"sampler_name\":\"euler\",\"scheduler\":\"normal\",\"denoise\":1,\"model\":[\"4\",0],\"positive\":[\"6\",0],\"negative\":[\"7\",0],\"latent_image\":[\"5\",0]}},"
                    + $"\"4\":{{\"class_type\":\"CheckpointLoaderSimple\",\"inputs\":{{\"ckpt_name\":\"{model}\"}}}},"
                    + $"\"5\":{{\"class_type\":\"EmptyLatentImage\",\"inputs\":{{\"width\":{width},\"height\":{height},\"batch_size\":1}}}},"
                    + $"\"6\":{{\"class_type\":\"CLIPTextEncode\",\"inputs\":{{\"text\":\"{prompt}\",\"clip\":[\"4\",1]}}}},"
                    + "\"7\":{\"class_type\":\"CLIPTextEncode\",\"inputs\":{\"text\":\"\",\"clip\":[\"4\",1]}},"
                    + "\"8\":{\"class_type\":\"VAEDecode\",\"inputs\":{\"samples\":[\"3\",0],\"vae\":[\"4\",2]}},"
                    + "\"9\":{\"class_type\":\"SaveImage\",\"inputs\":{\"filename_prefix\":\"RimChat\",\"images\":[\"8\",0]}}"
                    + "},"
                    + "\"client_id\":\"rimchat\""
                    + "}";
            }

            ParseSize(request.Size, out int imageWidth, out int imageHeight);
            string imageModel = EscapeJson(request.Model);
            string imagePrompt = EscapeJson(request.Prompt);
            string imageSource = EscapeJson(imageInput);
            string imageLoaderNode = EscapeJson(ResolveComfyUiImageLoaderNode(request));
            return "{"
                + "\"prompt\":{"
                + $"\"1\":{{\"class_type\":\"{imageLoaderNode}\",\"inputs\":{{\"image\":\"{imageSource}\"}}}},"
                + $"\"2\":{{\"class_type\":\"CheckpointLoaderSimple\",\"inputs\":{{\"ckpt_name\":\"{imageModel}\"}}}},"
                + $"\"3\":{{\"class_type\":\"CLIPTextEncode\",\"inputs\":{{\"text\":\"{imagePrompt}\",\"clip\":[\"2\",1]}}}},"
                + "\"4\":{\"class_type\":\"CLIPTextEncode\",\"inputs\":{\"text\":\"\",\"clip\":[\"2\",1]}},"
                + "\"5\":{\"class_type\":\"VAEEncode\",\"inputs\":{\"pixels\":[\"1\",0],\"vae\":[\"2\",2]}},"
                + "\"6\":{\"class_type\":\"KSampler\",\"inputs\":{\"seed\":1,\"steps\":20,\"cfg\":7,\"sampler_name\":\"euler\",\"scheduler\":\"normal\",\"denoise\":0.65,\"model\":[\"2\",0],\"positive\":[\"3\",0],\"negative\":[\"4\",0],\"latent_image\":[\"5\",0]}},"
                + "\"7\":{\"class_type\":\"VAEDecode\",\"inputs\":{\"samples\":[\"6\",0],\"vae\":[\"2\",2]}},"
                + "\"8\":{\"class_type\":\"SaveImage\",\"inputs\":{\"filename_prefix\":\"RimChat\",\"images\":[\"7\",0]}}"
                + "},"
                + "\"client_id\":\"rimchat\""
                + "}";
        }

        public static bool TryExtractImagePayload(
            string responseBody,
            DiplomacyImageGenerationRequest request,
            UrlExtractor fallbackUrlExtractor,
            out string imageUrl,
            out byte[] inlineBytes)
        {
            imageUrl = string.Empty;
            inlineBytes = null;

            List<string> urlKeys = BuildKeyCandidates(request.ResponseUrlPath, new[] { "url" });
            if (TryExtractValueByKeys(responseBody, urlKeys, out string urlValue))
            {
                string normalized = UnescapeJsonValue(urlValue);
                if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    imageUrl = normalized;
                    return true;
                }
            }

            if (fallbackUrlExtractor(responseBody, out imageUrl))
            {
                return true;
            }

            List<string> b64Keys = BuildKeyCandidates(request.ResponseB64Path, new[] { "b64_json", "b64", "image_base64" });
            if (TryExtractValueByKeys(responseBody, b64Keys, out string b64Value) && TryDecodeBase64Image(b64Value, out inlineBytes))
            {
                imageUrl = "inline://base64";
                return true;
            }

            return false;
        }

        public static bool TryExtractPromptId(string response, out string promptId)
        {
            promptId = string.Empty;
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            Match match = PromptIdRegex.Match(response);
            if (!match.Success)
            {
                return false;
            }

            promptId = UnescapeJsonValue(match.Groups["id"].Value);
            return !string.IsNullOrWhiteSpace(promptId);
        }

        public static bool TryExtractComfyImageAddress(string response, DiplomacyImageGenerationRequest request, out string imageUrl)
        {
            imageUrl = string.Empty;
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            Match fileMatch = FilenameRegex.Match(response);
            Match typeMatch = TypeRegex.Match(response);
            if (!fileMatch.Success || !typeMatch.Success)
            {
                return false;
            }

            string filename = UnescapeJsonValue(fileMatch.Groups["value"].Value);
            string subfolder = UnescapeJsonValue(SubfolderRegex.Match(response).Groups["value"].Value);
            string type = UnescapeJsonValue(typeMatch.Groups["value"].Value);
            if (string.IsNullOrWhiteSpace(filename) || string.IsNullOrWhiteSpace(type))
            {
                return false;
            }

            string baseUrl = ResolveBaseEndpointForAsync(request.Endpoint, request.AsyncSubmitPath);
            string fetchPath = string.IsNullOrWhiteSpace(request.AsyncImageFetchPath) ? "/view" : request.AsyncImageFetchPath.Trim();
            string separator = fetchPath.Contains("?") ? "&" : "?";
            imageUrl = CombineUrl(baseUrl, fetchPath)
                + $"{separator}filename={UnityWebRequest.EscapeURL(filename)}&subfolder={UnityWebRequest.EscapeURL(subfolder)}&type={UnityWebRequest.EscapeURL(type)}";
            return true;
        }

        public static string ResolveAsyncSubmitUrl(DiplomacyImageGenerationRequest request)
        {
            string endpoint = request.Endpoint ?? string.Empty;
            if (endpoint.IndexOf("/prompt", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return endpoint;
            }

            string submitPath = string.IsNullOrWhiteSpace(request.AsyncSubmitPath) ? "/prompt" : request.AsyncSubmitPath;
            return CombineUrl(endpoint, submitPath);
        }

        public static string ResolveAsyncStatusUrl(DiplomacyImageGenerationRequest request, string promptId)
        {
            string template = string.IsNullOrWhiteSpace(request.AsyncStatusPathTemplate)
                ? "/history/{job_id}"
                : request.AsyncStatusPathTemplate;
            template = template.Replace("{job_id}", promptId).Replace("{prompt_id}", promptId);
            string baseUrl = ResolveBaseEndpointForAsync(request.Endpoint, request.AsyncSubmitPath);
            return CombineUrl(baseUrl, template);
        }

        public static void ApplyAuth(UnityWebRequest requestWeb, DiplomacyImageGenerationRequest request)
        {
            if (requestWeb == null || request == null)
            {
                return;
            }

            string authMode = DiplomacyImageApiConfig.NormalizeAuthMode(request.AuthMode, request.SchemaPreset);
            if (string.Equals(authMode, DiplomacyImageApiConfig.AuthModeNone, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string key = request.ApiKey?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (string.Equals(authMode, DiplomacyImageApiConfig.AuthModeBearer, StringComparison.OrdinalIgnoreCase))
            {
                requestWeb.SetRequestHeader("Authorization", $"Bearer {key}");
                return;
            }

            if (string.Equals(authMode, DiplomacyImageApiConfig.AuthModeApiKeyHeader, StringComparison.OrdinalIgnoreCase))
            {
                string headerName = string.IsNullOrWhiteSpace(request.ApiKeyHeaderName) ? "X-API-Key" : request.ApiKeyHeaderName.Trim();
                requestWeb.SetRequestHeader(headerName, key);
            }
        }

        public static string BuildAuthAppliedUrl(string url, DiplomacyImageGenerationRequest request)
        {
            string normalized = (url ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }

            string authMode = DiplomacyImageApiConfig.NormalizeAuthMode(request.AuthMode, request.SchemaPreset);
            if (!string.Equals(authMode, DiplomacyImageApiConfig.AuthModeQueryKey, StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            string key = request.ApiKey?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return normalized;
            }

            string keyName = string.IsNullOrWhiteSpace(request.ApiKeyQueryName) ? "api_key" : request.ApiKeyQueryName.Trim();
            return AppendQueryParameter(normalized, keyName, key);
        }

        private static string BuildOpenAICompatibleBody(DiplomacyImageGenerationRequest request, bool includeSize)
        {
            string model = EscapeJson(request.Model);
            string prompt = EscapeJson(request.Prompt);
            string responseFormat = string.Equals(request.Mode, DiplomacyImageApiConfig.ModeSyncUrl, StringComparison.OrdinalIgnoreCase)
                ? "url"
                : "b64_json";
            string imageBlock = BuildOpenAiImageToImageBlock(request);
            if (!includeSize)
            {
                return "{"
                    + $"\"model\":\"{model}\","
                    + $"\"prompt\":\"{prompt}\","
                    + imageBlock
                    + $"\"response_format\":\"{responseFormat}\""
                    + "}";
            }

            string size = EscapeJson(request.Size);
            return "{"
                + $"\"model\":\"{model}\","
                + $"\"prompt\":\"{prompt}\","
                + imageBlock
                + $"\"response_format\":\"{responseFormat}\","
                + $"\"size\":\"{size}\""
                + "}";
        }

        private static string BuildOpenAiImageToImageBlock(DiplomacyImageGenerationRequest request)
        {
            string imageInput = ResolveSourceImageInput(request);
            if (string.IsNullOrWhiteSpace(imageInput))
            {
                return string.Empty;
            }

            return $"\"image\":\"{EscapeJson(imageInput)}\",";
        }

        private static string ResolveSourceImageInput(DiplomacyImageGenerationRequest request)
        {
            if (request == null || !request.PreferImageToImage)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(request.SourceImageInput))
            {
                return request.SourceImageInput.Trim();
            }

            if (request.SourceImageBytes == null || request.SourceImageBytes.Length == 0)
            {
                return string.Empty;
            }

            string mimeType = string.IsNullOrWhiteSpace(request.SourceImageMimeType) ? "image/png" : request.SourceImageMimeType.Trim();
            string base64 = Convert.ToBase64String(request.SourceImageBytes);
            return $"data:{mimeType};base64,{base64}";
        }

        private static string ResolveComfyUiImageLoaderNode(DiplomacyImageGenerationRequest request)
        {
            string configured = request?.ComfyUiImageLoaderNode?.Trim();
            return string.IsNullOrWhiteSpace(configured) ? "LoadImageBase64" : configured;
        }

        private static bool TryDecodeBase64Image(string raw, out byte[] bytes)
        {
            bytes = null;
            string value = UnescapeJsonValue(raw);
            int marker = value.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                value = value.Substring(marker + "base64,".Length);
            }

            value = value.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                bytes = Convert.FromBase64String(value);
                return bytes != null && bytes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryExtractValueByKeys(string source, List<string> keys, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(source) || keys == null || keys.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < keys.Count; i++)
            {
                string key = Regex.Escape(keys[i]);
                Match match = Regex.Match(source, $"\"{key}\"\\s*:\\s*\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    value = match.Groups["value"].Value;
                    return !string.IsNullOrWhiteSpace(value);
                }
            }

            return false;
        }

        private static List<string> BuildKeyCandidates(string pathSpec, string[] fallback)
        {
            var keys = new List<string>();
            AddPathKeys(pathSpec, keys);
            if (keys.Count == 0 && fallback != null)
            {
                for (int i = 0; i < fallback.Length; i++)
                {
                    AddPathKeys(fallback[i], keys);
                }
            }

            if (keys.Count == 0)
            {
                keys.Add("url");
            }

            return keys;
        }

        private static void AddPathKeys(string pathSpec, List<string> keys)
        {
            if (string.IsNullOrWhiteSpace(pathSpec) || keys == null)
            {
                return;
            }

            string[] parts = pathSpec.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i].Trim();
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                int dot = token.LastIndexOf('.');
                string leaf = dot >= 0 ? token.Substring(dot + 1) : token;
                leaf = leaf.Replace("[0]", string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(leaf) && !keys.Contains(leaf, StringComparer.OrdinalIgnoreCase))
                {
                    keys.Add(leaf);
                }
            }
        }

        private static string UnescapeJsonValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Replace("\\/", "/").Replace("\\\\", "\\").Trim();
        }

        private static string ResolveBaseEndpointForAsync(string endpoint, string submitPath)
        {
            string normalized = (endpoint ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            string submit = string.IsNullOrWhiteSpace(submitPath) ? "/prompt" : submitPath.Trim();
            int submitIndex = normalized.IndexOf(submit, StringComparison.OrdinalIgnoreCase);
            if (submitIndex > 0)
            {
                return normalized.Substring(0, submitIndex).TrimEnd('/');
            }

            int promptIndex = normalized.IndexOf("/prompt", StringComparison.OrdinalIgnoreCase);
            if (promptIndex > 0)
            {
                return normalized.Substring(0, promptIndex).TrimEnd('/');
            }

            return normalized.TrimEnd('/');
        }

        private static string CombineUrl(string baseUrl, string path)
        {
            string left = (baseUrl ?? string.Empty).TrimEnd('/');
            string right = (path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(right))
            {
                return left;
            }

            if (right.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                right.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return right;
            }

            if (right.StartsWith("/", StringComparison.Ordinal))
            {
                return $"{left}{right}";
            }

            return $"{left}/{right}";
        }

        private static void ParseSize(string size, out int width, out int height)
        {
            width = 1024;
            height = 1024;
            string normalized = DiplomacyImageApiConfig.NormalizeImageSize(size, DiplomacyImageApiConfig.DefaultImageSize);
            int sep = normalized.IndexOf('x');
            if (sep <= 0 || sep >= normalized.Length - 1)
            {
                return;
            }

            if (int.TryParse(normalized.Substring(0, sep), out int parsedW))
            {
                width = Mathf.Max(64, parsedW);
            }
            if (int.TryParse(normalized.Substring(sep + 1), out int parsedH))
            {
                height = Mathf.Max(64, parsedH);
            }
        }

        private static string AppendQueryParameter(string url, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            {
                return url;
            }

            string separator = url.Contains("?") ? "&" : "?";
            return $"{url}{separator}{UnityWebRequest.EscapeURL(key)}={UnityWebRequest.EscapeURL(value ?? string.Empty)}";
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}

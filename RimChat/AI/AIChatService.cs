using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using RimChat.Config;
using RimChat.Util;
using RimChat.Core;

namespace RimChat.AI
{
    public class ChatMessageData
    {
        public string role;
        public string content;
    }

    public class AIChatService
    {
        private static AIChatService _instance;
        public static AIChatService Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new AIChatService();
                return _instance;
            }
        }

        private AIChatService() { }

        public void SendChatRequest(List<ChatMessageData> messages, Action<string> onSuccess, Action<string> onError, Action<float> onProgress = null)
        {
            DebugLogger.LogInternal("AIChatService", "SendChatRequest called");

            // 前置校验: 检查configuration
            var config = GetFirstValidConfig();
            if (config == null)
            {
                DebugLogger.LogAIError("No valid AI configuration found", "SendChatRequest");
                onError?.Invoke("RimChat_ErrorNoConfig".Translate());
                return;
            }

            string url = config.GetEffectiveEndpoint();
            string apiKey = config.ApiKey;
            string model = config.GetEffectiveModelName();
            bool isLocalModel = RimChatMod.Instance == null || !(RimChatMod.Instance.InstanceSettings?.UseCloudProviders ?? false);

            // Recordconfiguration信息
            DebugLogger.LogConfig(url, model, isLocalModel, DebugLogger.MaskApiKey(apiKey));

            // 前置校验: 检查URL格式
            if (!ValidateUrl(url, out string urlError))
            {
                DebugLogger.LogAIError($"URL validation failed: {urlError}", "SendChatRequest");
                onError?.Invoke(urlError);
                return;
            }

            // 前置校验: 检查messagecontents
            if (messages == null || messages.Count == 0)
            {
                DebugLogger.LogAIError("Message list is null or empty", "SendChatRequest");
                onError?.Invoke("RimChat_ErrorEmptyMessage".Translate());
                return;
            }

            messages = NormalizeMessagesForProvider(messages);
            if (messages.Count == 0)
            {
                DebugLogger.LogAIError("Message list became empty after normalization", "SendChatRequest");
                onError?.Invoke("RimChat_ErrorEmptyMessage".Translate());
                return;
            }

            DebugLogger.LogInternal("AIChatService", $"Processing {messages.Count} messages");
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                DebugLogger.LogInternal("AIChatService", $"Message {i}: role={msg.role}, content length={msg.content?.Length ?? 0}");
            }

            string jsonBody;
            try
            {
                jsonBody = BuildChatCompletionJson(model, messages);
            }
            catch (Exception ex)
            {
                DebugLogger.LogAIError($"Failed to build request JSON: {ex.Message}", "SendChatRequest");
                onError?.Invoke("RimChat_ErrorBuildRequest".Translate());
                return;
            }

            // Record完整request
            DebugLogger.LogAIRequest(url, model, jsonBody, isLocalModel);

            // 创建message列表的副本, used for后台线程中的logrecord
            var messagesCopy = new List<ChatMessageData>(messages);

            LongEventHandler.QueueLongEvent(() =>
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    DebugLogger.LogInternal("AIChatService", "Starting synchronous request...");
                    string result = SendRequestSync(url, apiKey, jsonBody, onProgress, isLocalModel, messagesCopy);
                    stopwatch.Stop();

                    if (!string.IsNullOrEmpty(result))
                    {
                        DebugLogger.Info($"AI response received successfully in {stopwatch.ElapsedMilliseconds}ms");
                        onSuccess?.Invoke(result);
                    }
                    else
                    {
                        DebugLogger.LogAIError("Empty response from AI", "SendChatRequest");
                        onError?.Invoke("RimChat_ErrorEmptyResponse".Translate());
                    }
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    string errorMsg = FormatErrorMessage(ex, isLocalModel);
                    DebugLogger.LogAIError($"{errorMsg} (after {stopwatch.ElapsedMilliseconds}ms)", "SendChatRequest");
                    onError?.Invoke(errorMsg);
                }
            }, "RimChat_SendingAIRequest".Translate(), false, null);
        }

        private bool ValidateUrl(string url, out string error)
        {
            error = null;
            DebugLogger.LogInternal("AIChatService", $"Validating URL: {url}");

            if (string.IsNullOrWhiteSpace(url))
            {
                error = "RimChat_ErrorEmptyUrl".Translate();
                return false;
            }

            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                error = "RimChat_ErrorInvalidUrl".Translate();
                return false;
            }

            try
            {
                var uri = new Uri(url);
                if (!uri.IsWellFormedOriginalString())
                {
                    error = "RimChat_ErrorMalformedUrl".Translate();
                    return false;
                }
            }
            catch (UriFormatException ex)
            {
                DebugLogger.LogInternal("AIChatService", $"URI format exception: {ex.Message}");
                error = "RimChat_ErrorMalformedUrl".Translate();
                return false;
            }

            DebugLogger.LogInternal("AIChatService", "URL validation passed");
            return true;
        }

        private string FormatErrorMessage(Exception ex, bool isLocalModel)
        {
            string message = ex.Message;
            DebugLogger.LogInternal("AIChatService", $"Formatting error message: {message}");

            // HTTP 401 error
            if (message.Contains("401"))
            {
                if (isLocalModel)
                {
                    return "RimChat_Error401Local".Translate();
                }
                else
                {
                    return "RimChat_Error401Cloud".Translate();
                }
            }

            // HTTP 404 error
            if (message.Contains("404"))
            {
                return "RimChat_Error404".Translate();
            }

            // 连接失败
            if (message.Contains("Cannot connect") || message.Contains("Connection refused") || message.Contains("Unable to connect"))
            {
                if (isLocalModel)
                {
                    return "RimChat_ErrorConnectionLocal".Translate();
                }
                else
                {
                    return "RimChat_ErrorConnectionCloud".Translate();
                }
            }

            // 超时
            if (message.Contains("timeout") || message.Contains("timed out"))
            {
                return "RimChat_ErrorTimeout".Translate();
            }

            // 默认返回原始error信息
            return "RimChat_ErrorGeneric".Translate(message);
        }

        private string SendRequestSync(string url, string apiKey, string jsonBody, Action<float> onProgress, bool isLocalModel, List<ChatMessageData> messages)
        {
            DebugLogger.LogInternal("AIChatService", $"Creating UnityWebRequest to {url}");
            var stopwatch = Stopwatch.StartNew();

            using (var request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                // Player2 等localservice需要 Authorization header, 即使 API Key empty
                // 发送 "Bearer " (空 token) 或 "Bearer {apiKey}"
                if (isLocalModel)
                {
                    DebugLogger.LogInternal("AIChatService", $"Adding Authorization header for local model: Bearer {(string.IsNullOrEmpty(apiKey) ? "(empty)" : "***")}");
                }
                else
                {
                    DebugLogger.LogInternal("AIChatService", "Adding Authorization header for cloud provider");
                }
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                
                request.timeout = 60;

                UnityWebRequestAsyncOperation operation;
                try
                {
                    DebugLogger.LogInternal("AIChatService", "Sending web request...");
                    operation = request.SendWebRequest();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogAIError($"Failed to start web request: {ex.Message}", "SendRequestSync");
                    throw new Exception($"Connection failed: {ex.Message}");
                }

                float progress = 0f;

                while (!operation.isDone)
                {
                    System.Threading.Thread.Sleep(100);
                    progress = Mathf.Min(progress + 0.05f, 0.9f);
                    onProgress?.Invoke(progress);
                }

                onProgress?.Invoke(1f);
                stopwatch.Stop();

                DebugLogger.LogInternal("AIChatService", $"Request completed in {stopwatch.ElapsedMilliseconds}ms, response code: {request.responseCode}");

                // Processing网络层error
                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    DebugLogger.LogAIError($"Connection error: {request.error}", "SendRequestSync");
                    throw new Exception($"Cannot connect to server: {request.error}");
                }

                if (request.result == UnityWebRequest.Result.ProtocolError)
                {
                    DebugLogger.LogAIError($"Protocol error: HTTP {request.responseCode} - {request.error}", "SendRequestSync");
                    throw new Exception($"HTTP {request.responseCode}: {request.error}");
                }

                if (request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    DebugLogger.LogAIError($"Data processing error: {request.error}", "SendRequestSync");
                    throw new Exception($"Data processing error: {request.error}");
                }

                if (request.responseCode == 200)
                {
                    string responseText = request.downloadHandler?.text;
                    DebugLogger.LogAIResponse(responseText, request.responseCode, stopwatch.ElapsedMilliseconds);

                    // Record完整的发送message和接收response
                    DebugLogger.LogFullMessages(messages, responseText);

                    if (string.IsNullOrEmpty(responseText))
                    {
                        DebugLogger.LogAIError("Empty response from server", "SendRequestSync");
                        throw new Exception("Empty response from server");
                    }
                    return ParseResponse(responseText);
                }
                else
                {
                    string errorMsg = $"HTTP {request.responseCode}: {request.error}";
                    DebugLogger.LogAIError(errorMsg, "SendRequestSync");
                    throw new Exception(errorMsg);
                }
            }
        }

        private string ParseResponse(string json)
        {
            DebugLogger.LogInternal("AIChatService", "Parsing AI response...");

            if (string.IsNullOrEmpty(json))
            {
                DebugLogger.Warning("Response JSON is null or empty");
                return null;
            }

            try
            {
                if (AIJsonContentExtractor.IsErrorPayload(json))
                {
                    DebugLogger.LogAIError($"AI returned error response: {json.Substring(0, Math.Min(500, json.Length))}", "ParseResponse");
                    return null;
                }

                PrimaryTextExtractionResult extraction = AIJsonContentExtractor.TryExtractPrimaryText(json);
                DebugLogger.LogParseExtraction("AIChatService", extraction);
                if (extraction.IsSuccess)
                {
                    DebugLogger.LogInternal(
                        "AIChatService",
                        $"Extracted content length: {extraction.Content.Length}, reason={extraction.ReasonTag}, path={extraction.MatchedPath}");
                    return extraction.Content;
                }

                DebugLogger.Warning($"Could not find content in AI response. reason={extraction.ReasonTag}, path={extraction.MatchedPath}");
            }
            catch (ArgumentOutOfRangeException ex)
            {
                DebugLogger.LogAIError($"String index out of range while parsing: {ex.Message}", "ParseResponse");
            }
            catch (Exception ex)
            {
                DebugLogger.LogAIError($"Failed to parse AI response: {ex.Message}", "ParseResponse");
            }
            return null;
        }

        private ApiConfig GetFirstValidConfig()
        {
            DebugLogger.LogInternal("AIChatService", "Getting first valid config...");

            if (RimChatMod.Instance == null || RimChatMod.Instance.InstanceSettings == null)
            {
                DebugLogger.Warning("Settings is null");
                return null;
            }

            if (RimChatMod.Instance.InstanceSettings.UseCloudProviders)
            {
                DebugLogger.LogInternal("AIChatService", "Using cloud providers");
                if (RimChatMod.Instance.InstanceSettings.CloudConfigs == null || RimChatMod.Instance.InstanceSettings.CloudConfigs.Count == 0)
                {
                    DebugLogger.Warning("No cloud configs found");
                    return null;
                }

                foreach (var config in RimChatMod.Instance.InstanceSettings.CloudConfigs)
                {
                    if (config.IsValid())
                    {
                        DebugLogger.LogInternal("AIChatService", $"Found valid config for provider: {config.Provider}");
                        return config;
                    }
                }

                DebugLogger.Warning("No valid cloud config found");
            }
            else
            {
                DebugLogger.LogInternal("AIChatService", "Using local model");
                var localConfig = RimChatMod.Instance.InstanceSettings.LocalConfig;
                if (localConfig != null && localConfig.IsValid())
                {
                    string localBaseUrl = localConfig.GetNormalizedBaseUrl();
                    DebugLogger.LogInternal("AIChatService", $"Using local model at {localBaseUrl} with model {localConfig.ModelName}");
                    return new ApiConfig
                    {
                        IsEnabled = true,
                        Provider = AIProvider.Custom,
                        BaseUrl = ApiConfig.EnsureChatCompletionsEndpoint(localBaseUrl),
                        ApiKey = "",
                        SelectedModel = localConfig.ModelName
                    };
                }
                else
                {
                    DebugLogger.Warning("Local config is invalid or not configured");
                }
            }

            return null;
        }

        private string BuildChatCompletionJson(string model, List<ChatMessageData> messages)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\":\"{EscapeJson(model)}\",");
            sb.Append("\"messages\":[");

            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"role\":\"{EscapeJson(messages[i].role)}\",");
                sb.Append($"\"content\":\"{EscapeJson(messages[i].content)}\"");
                sb.Append("}");
            }

            sb.Append("],");
            sb.Append("\"temperature\":0.7,");
            sb.Append("\"max_tokens\":2000");
            sb.Append("}");

            string result = sb.ToString();
            DebugLogger.LogInternal("AIChatService", $"Built JSON request, length: {result.Length}");
            return result;
        }

        private string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r")
                      .Replace("\t", "\\t");
        }

        public bool IsConfigured()
        {
            return GetFirstValidConfig() != null;
        }

        private static List<ChatMessageData> NormalizeMessagesForProvider(List<ChatMessageData> source)
        {
            List<ChatMessageData> normalized = CollectNormalizedMessages(source);

            if (normalized.Count > 0 && !normalized.Any(msg =>
                    string.Equals(msg.role, "user", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(msg.content)))
            {
                normalized.Add(new ChatMessageData
                {
                    role = "user",
                    content = "Please follow the system instructions and provide the requested output in plain text."
                });
            }

            return normalized;
        }

        private static List<ChatMessageData> CollectNormalizedMessages(List<ChatMessageData> source)
        {
            var normalized = new List<ChatMessageData>();
            if (source == null)
            {
                return normalized;
            }

            for (int i = 0; i < source.Count; i++)
            {
                ChatMessageData msg = source[i];
                if (msg == null)
                {
                    continue;
                }

                normalized.Add(new ChatMessageData
                {
                    role = NormalizeRole(msg.role),
                    content = msg.content ?? string.Empty
                });
            }

            return normalized;
        }

        private static string NormalizeRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return "user";
            }

            string trimmed = role.Trim();
            if (string.Equals(trimmed, "system", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "user", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.ToLowerInvariant();
            }

            return "user";
        }
    }
}

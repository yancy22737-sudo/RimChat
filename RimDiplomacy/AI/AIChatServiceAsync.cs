using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using RimDiplomacy.Config;
using RimDiplomacy.Util;
using RimDiplomacy.Core;

namespace RimDiplomacy.AI
{
    /// <summary>
    /// AI聊天请求的状态
    /// </summary>
    public enum AIRequestState
    {
        Idle,
        Pending,
        Processing,
        Completed,
        Error
    }

    /// <summary>
    /// AI聊天请求的结果
    /// </summary>
    public class AIRequestResult
    {
        public bool Success { get; set; }
        public string Response { get; set; }
        public string Error { get; set; }
        public float Progress { get; set; }
        public AIRequestState State { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public enum DialogueUsageChannel
    {
        Unknown = 0,
        Diplomacy = 1,
        Rpg = 2
    }

    public class DialogueTokenUsageSnapshot
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
        public bool IsEstimated { get; set; }
        public DialogueUsageChannel Channel { get; set; }
        public DateTime RecordedAtUtc { get; set; }

        public DialogueTokenUsageSnapshot Clone()
        {
            return new DialogueTokenUsageSnapshot
            {
                PromptTokens = PromptTokens,
                CompletionTokens = CompletionTokens,
                TotalTokens = TotalTokens,
                IsEstimated = IsEstimated,
                Channel = Channel,
                RecordedAtUtc = RecordedAtUtc
            };
        }
    }

    /// <summary>
    /// 异步AI聊天服务 - 使用Unity协程实现非阻塞通信
    /// </summary>
    public class AIChatServiceAsync : MonoBehaviour
    {
        private static AIChatServiceAsync _instance;
        public static AIChatServiceAsync Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("AIChatServiceAsync");
                    _instance = go.AddComponent<AIChatServiceAsync>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private readonly Dictionary<string, AIRequestResult> activeRequests = new Dictionary<string, AIRequestResult>();
        private readonly Queue<Action> mainThreadActions = new Queue<Action>();
        private readonly object lockObject = new object();
        private DialogueTokenUsageSnapshot latestDialogueTokenUsage;

        private static readonly Regex PromptTokensRegex = new Regex("\"prompt_tokens\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CompletionTokensRegex = new Regex("\"completion_tokens\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TotalTokensRegex = new Regex("\"total_tokens\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        void Update()
        {
            lock (lockObject)
            {
                while (mainThreadActions.Count > 0)
                {
                    try
                    {
                        mainThreadActions.Dequeue()?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[RimDiplomacy] Error executing main thread action: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 发送异步聊天请求
        /// </summary>
        public string SendChatRequestAsync(
            List<ChatMessageData> messages,
            Action<string> onSuccess,
            Action<string> onError,
            Action<float> onProgress = null,
            DialogueUsageChannel usageChannel = DialogueUsageChannel.Unknown)
        {
            string requestId = Guid.NewGuid().ToString("N");
            
            var result = new AIRequestResult
            {
                State = AIRequestState.Pending,
                StartTime = DateTime.Now,
                Progress = 0f
            };

            lock (lockObject)
            {
                activeRequests[requestId] = result;
            }

            StartCoroutine(ProcessRequestCoroutine(requestId, messages, onSuccess, onError, onProgress, usageChannel));
            
            return requestId;
        }

        public DialogueTokenUsageSnapshot GetLatestDialogueTokenUsage()
        {
            lock (lockObject)
            {
                return latestDialogueTokenUsage?.Clone();
            }
        }

        public static bool TryGetLatestDialogueTokenUsage(out DialogueTokenUsageSnapshot snapshot)
        {
            snapshot = null;
            if (_instance == null)
            {
                return false;
            }

            snapshot = _instance.GetLatestDialogueTokenUsage();
            return snapshot != null;
        }

        /// <summary>
        /// 取消指定的请求
        /// </summary>
        public bool CancelRequest(string requestId)
        {
            lock (lockObject)
            {
                if (activeRequests.TryGetValue(requestId, out var result))
                {
                    if (result.State == AIRequestState.Pending || result.State == AIRequestState.Processing)
                    {
                        result.State = AIRequestState.Error;
                        result.Error = "Request cancelled by user";
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 获取请求状态
        /// </summary>
        public AIRequestResult GetRequestStatus(string requestId)
        {
            lock (lockObject)
            {
                if (activeRequests.TryGetValue(requestId, out var result))
                {
                    return result;
                }
            }
            return null;
        }

        /// <summary>
        /// 清理已完成的请求
        /// </summary>
        public void CleanupCompletedRequests()
        {
            lock (lockObject)
            {
                var completedIds = new List<string>();
                foreach (var kvp in activeRequests)
                {
                    if (kvp.Value.State == AIRequestState.Completed || 
                        kvp.Value.State == AIRequestState.Error)
                    {
                        if ((DateTime.Now - kvp.Value.StartTime).TotalMinutes > 5)
                        {
                            completedIds.Add(kvp.Key);
                        }
                    }
                }
                foreach (var id in completedIds)
                {
                    activeRequests.Remove(id);
                }
            }
        }

        private System.Collections.IEnumerator ProcessRequestCoroutine(
            string requestId,
            List<ChatMessageData> messages,
            Action<string> onSuccess,
            Action<string> onError,
            Action<float> onProgress,
            DialogueUsageChannel usageChannel)
        {
            var config = GetFirstValidConfig();
            if (config == null)
            {
                UpdateRequestState(requestId, AIRequestState.Error, error: "RimDiplomacy_ErrorNoConfig".Translate());
                ExecuteOnMainThread(() => onError?.Invoke("RimDiplomacy_ErrorNoConfig".Translate()));
                yield break;
            }

            string url = config.GetEffectiveEndpoint();
            string apiKey = config.ApiKey;
            string model = config.GetEffectiveModelName();
            bool isLocalModel = RimDiplomacyMod.Instance == null || 
                !(RimDiplomacyMod.Instance.InstanceSettings?.UseCloudProviders ?? false);

            if (!ValidateUrl(url, out string urlError))
            {
                UpdateRequestState(requestId, AIRequestState.Error, error: urlError);
                ExecuteOnMainThread(() => onError?.Invoke(urlError));
                yield break;
            }

            if (messages == null || messages.Count == 0)
            {
                UpdateRequestState(requestId, AIRequestState.Error, error: "RimDiplomacy_ErrorEmptyMessage".Translate());
                ExecuteOnMainThread(() => onError?.Invoke("RimDiplomacy_ErrorEmptyMessage".Translate()));
                yield break;
            }

            string jsonBody;
            try
            {
                jsonBody = BuildChatCompletionJson(model, messages);
            }
            catch (Exception)
            {
                UpdateRequestState(requestId, AIRequestState.Error, error: "RimDiplomacy_ErrorBuildRequest".Translate());
                ExecuteOnMainThread(() => onError?.Invoke("RimDiplomacy_ErrorBuildRequest".Translate()));
                yield break;
            }

            UpdateRequestState(requestId, AIRequestState.Processing);
            
            var stopwatch = Stopwatch.StartNew();
            
            using (var request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                request.timeout = 60;

                var operation = request.SendWebRequest();
                float progress = 0f;

                while (!operation.isDone)
                {
                    progress = Mathf.Min(progress + 0.02f, 0.9f);
                    UpdateRequestProgress(requestId, progress);
                    ExecuteOnMainThread(() => onProgress?.Invoke(progress));
                    yield return new WaitForSeconds(0.1f);

                    lock (lockObject)
                    {
                        if (activeRequests.TryGetValue(requestId, out var result) && 
                            result.State == AIRequestState.Error)
                        {
                            request.Abort();
                            ExecuteOnMainThread(() => onError?.Invoke(result.Error));
                            yield break;
                        }
                    }
                }

                stopwatch.Stop();
                UpdateRequestProgress(requestId, 1f);
                ExecuteOnMainThread(() => onProgress?.Invoke(1f));

                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    string errorMsg = isLocalModel 
                        ? "RimDiplomacy_ErrorConnectionLocal".Translate() 
                        : "RimDiplomacy_ErrorConnectionCloud".Translate();
                    UpdateRequestState(requestId, AIRequestState.Error, error: errorMsg);
                    ExecuteOnMainThread(() => onError?.Invoke(errorMsg));
                    yield break;
                }

                if (request.result == UnityWebRequest.Result.ProtocolError)
                {
                    string responseBody = request.downloadHandler?.text;
                    Log.Error($"[RimDiplomacy] AI API Error (HTTP {request.responseCode}): {request.error}\nResponse Body: {responseBody}");
                    
                    string errorMsg = FormatProtocolError(request.responseCode, isLocalModel);
                    if (!string.IsNullOrEmpty(responseBody) && responseBody.Length < 200)
                    {
                        errorMsg += $" ({responseBody})";
                    }
                    
                    UpdateRequestState(requestId, AIRequestState.Error, error: errorMsg);
                    ExecuteOnMainThread(() => onError?.Invoke(errorMsg));
                    yield break;
                }

                if (request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    string errorMsg = "RimDiplomacy_ErrorDataProcessing".Translate(request.error);
                    UpdateRequestState(requestId, AIRequestState.Error, error: errorMsg);
                    ExecuteOnMainThread(() => onError?.Invoke(errorMsg));
                    yield break;
                }

                if (request.responseCode == 200)
                {
                    string responseText = request.downloadHandler?.text;
                    
                    // 记录完整的发送消息和接收响应
                    DebugLogger.LogFullMessages(messages, responseText);
                    
                    if (string.IsNullOrEmpty(responseText))
                    {
                        string errorMsg = "RimDiplomacy_ErrorEmptyResponse".Translate();
                        UpdateRequestState(requestId, AIRequestState.Error, error: errorMsg);
                        ExecuteOnMainThread(() => onError?.Invoke(errorMsg));
                        yield break;
                    }

                    string parsedResponse = ParseResponse(responseText);
                    if (string.IsNullOrEmpty(parsedResponse))
                    {
                        string errorMsg = "RimDiplomacy_ErrorParseResponse".Translate();
                        UpdateRequestState(requestId, AIRequestState.Error, error: errorMsg);
                        ExecuteOnMainThread(() => onError?.Invoke(errorMsg));
                        yield break;
                    }

                    TryRecordDialogueTokenUsage(messages, responseText, parsedResponse, usageChannel);
                    UpdateRequestState(requestId, AIRequestState.Completed, response: parsedResponse);
                    ExecuteOnMainThread(() => onSuccess?.Invoke(parsedResponse));
                }
                else
                {
                    string errorMsg = $"HTTP {request.responseCode}: {request.error}";
                    UpdateRequestState(requestId, AIRequestState.Error, error: errorMsg);
                    ExecuteOnMainThread(() => onError?.Invoke(errorMsg));
                }
            }
        }

        public void ExecuteOnMainThread(Action action)
        {
            if (action == null) return;
            
            lock (lockObject)
            {
                mainThreadActions.Enqueue(action);
            }
        }

        private void UpdateRequestState(string requestId, AIRequestState state, string response = null, string error = null)
        {
            lock (lockObject)
            {
                if (activeRequests.TryGetValue(requestId, out var result))
                {
                    result.State = state;
                    result.Response = response;
                    result.Error = error;
                    if (state == AIRequestState.Completed || state == AIRequestState.Error)
                    {
                        result.Duration = DateTime.Now - result.StartTime;
                    }
                }
            }
        }

        private void UpdateRequestProgress(string requestId, float progress)
        {
            lock (lockObject)
            {
                if (activeRequests.TryGetValue(requestId, out var result))
                {
                    result.Progress = progress;
                }
            }
        }

        private string FormatProtocolError(long responseCode, bool isLocalModel)
        {
            return responseCode switch
            {
                401 => isLocalModel 
                    ? "RimDiplomacy_Error401Local".Translate() 
                    : "RimDiplomacy_Error401Cloud".Translate(),
                404 => "RimDiplomacy_Error404".Translate(),
                429 => "RimDiplomacy_ErrorRateLimit".Translate(),
                500 => "RimDiplomacy_ErrorServer500".Translate(),
                502 => "RimDiplomacy_ErrorServer502".Translate(),
                503 => "RimDiplomacy_ErrorServer503".Translate(),
                _ => "RimDiplomacy_ErrorHTTP".Translate(responseCode)
            };
        }

        private bool ValidateUrl(string url, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(url))
            {
                error = "RimDiplomacy_ErrorEmptyUrl".Translate();
                return false;
            }

            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                error = "RimDiplomacy_ErrorInvalidUrl".Translate();
                return false;
            }

            try
            {
                var uri = new Uri(url);
                if (!uri.IsWellFormedOriginalString())
                {
                    error = "RimDiplomacy_ErrorMalformedUrl".Translate();
                    return false;
                }
            }
            catch (UriFormatException)
            {
                error = "RimDiplomacy_ErrorMalformedUrl".Translate();
                return false;
            }

            return true;
        }

        private string ParseResponse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                if (json.Contains("\"error\""))
                    return null;

                int contentIndex = json.IndexOf("\"content\":\"");
                if (contentIndex >= 0)
                {
                    contentIndex += "\"content\":\"".Length;
                    int endIndex = FindEndQuote(json, contentIndex);
                    if (endIndex >= 0)
                    {
                        string content = json.Substring(contentIndex, endIndex - contentIndex);
                        return UnescapeJson(content);
                    }
                }

                contentIndex = json.IndexOf("\"content\": \"");
                if (contentIndex >= 0)
                {
                    contentIndex += "\"content\": \"".Length;
                    int endIndex = FindEndQuote(json, contentIndex);
                    if (endIndex >= 0)
                    {
                        string content = json.Substring(contentIndex, endIndex - contentIndex);
                        return UnescapeJson(content);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] Failed to parse AI response: {ex.Message}");
            }
            return null;
        }

        private void TryRecordDialogueTokenUsage(
            List<ChatMessageData> messages,
            string rawJsonResponse,
            string parsedResponse,
            DialogueUsageChannel usageChannel)
        {
            if (!ShouldTrackDialogueUsage(usageChannel))
            {
                return;
            }

            bool hasUsage = TryExtractUsage(rawJsonResponse, out int promptTokens, out int completionTokens, out int totalTokens);
            bool isEstimated = !hasUsage;
            if (!hasUsage)
            {
                EstimateTokenUsage(messages, parsedResponse, out promptTokens, out completionTokens, out totalTokens);
            }

            if (totalTokens <= 0)
            {
                return;
            }

            var snapshot = new DialogueTokenUsageSnapshot
            {
                PromptTokens = Math.Max(0, promptTokens),
                CompletionTokens = Math.Max(0, completionTokens),
                TotalTokens = Math.Max(0, totalTokens),
                IsEstimated = isEstimated,
                Channel = usageChannel,
                RecordedAtUtc = DateTime.UtcNow
            };

            lock (lockObject)
            {
                latestDialogueTokenUsage = snapshot;
            }
        }

        private static bool ShouldTrackDialogueUsage(DialogueUsageChannel usageChannel)
        {
            return usageChannel == DialogueUsageChannel.Diplomacy || usageChannel == DialogueUsageChannel.Rpg;
        }

        private static bool TryExtractUsage(string json, out int promptTokens, out int completionTokens, out int totalTokens)
        {
            promptTokens = 0;
            completionTokens = 0;
            totalTokens = 0;

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            bool promptOk = TryExtractIntByRegex(PromptTokensRegex, json, out promptTokens);
            bool completionOk = TryExtractIntByRegex(CompletionTokensRegex, json, out completionTokens);
            bool totalOk = TryExtractIntByRegex(TotalTokensRegex, json, out totalTokens);
            if (totalOk && totalTokens > 0)
            {
                return true;
            }

            if (promptOk && completionOk)
            {
                totalTokens = promptTokens + completionTokens;
                return totalTokens > 0;
            }

            return false;
        }

        private static bool TryExtractIntByRegex(Regex regex, string source, out int value)
        {
            value = 0;
            if (regex == null || string.IsNullOrEmpty(source))
            {
                return false;
            }

            Match match = regex.Match(source);
            if (!match.Success || match.Groups.Count < 2)
            {
                return false;
            }

            return int.TryParse(match.Groups[1].Value, out value);
        }

        private static void EstimateTokenUsage(
            List<ChatMessageData> messages,
            string parsedResponse,
            out int promptTokens,
            out int completionTokens,
            out int totalTokens)
        {
            int promptChars = 0;
            if (messages != null)
            {
                for (int i = 0; i < messages.Count; i++)
                {
                    promptChars += (messages[i]?.content?.Length ?? 0);
                    promptChars += (messages[i]?.role?.Length ?? 0);
                }
            }

            int completionChars = parsedResponse?.Length ?? 0;
            promptTokens = Mathf.CeilToInt(promptChars / 4f);
            completionTokens = Mathf.CeilToInt(completionChars / 4f);
            if (promptChars <= 0)
            {
                promptTokens = 0;
            }
            if (completionChars <= 0)
            {
                completionTokens = 0;
            }

            totalTokens = promptTokens + completionTokens;
        }

        private int FindEndQuote(string json, int startIndex)
        {
            for (int i = startIndex; i < json.Length; i++)
            {
                if (json[i] == '"' && (i == 0 || json[i - 1] != '\\'))
                {
                    return i;
                }
            }
            return -1;
        }

        private string UnescapeJson(string str)
        {
            return str.Replace("\\\"", "\"")
                      .Replace("\\\\", "\\")
                      .Replace("\\n", "\n")
                      .Replace("\\r", "\r")
                      .Replace("\\t", "\t");
        }

        private ApiConfig GetFirstValidConfig()
        {
            if (RimDiplomacyMod.Instance == null || RimDiplomacyMod.Instance.InstanceSettings == null)
                return null;

            if (RimDiplomacyMod.Instance.InstanceSettings.UseCloudProviders)
            {
                if (RimDiplomacyMod.Instance.InstanceSettings.CloudConfigs == null || 
                    RimDiplomacyMod.Instance.InstanceSettings.CloudConfigs.Count == 0)
                    return null;

                foreach (var config in RimDiplomacyMod.Instance.InstanceSettings.CloudConfigs)
                {
                    if (config.IsValid())
                        return config;
                }
            }
            else
            {
                var localConfig = RimDiplomacyMod.Instance.InstanceSettings.LocalConfig;
                if (localConfig != null && localConfig.IsValid())
                {
                    return new ApiConfig
                    {
                        IsEnabled = true,
                        Provider = AIProvider.Custom,
                        BaseUrl = localConfig.BaseUrl + "/v1/chat/completions",
                        ApiKey = "",
                        SelectedModel = localConfig.ModelName
                    };
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
            sb.Append("\"max_tokens\":1000");
            sb.Append("}");

            return sb.ToString();
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

        void OnDestroy()
        {
            lock (lockObject)
            {
                foreach (var kvp in activeRequests)
                {
                    if (kvp.Value.State == AIRequestState.Pending || kvp.Value.State == AIRequestState.Processing)
                    {
                        kvp.Value.State = AIRequestState.Error;
                        kvp.Value.Error = "Service destroyed";
                    }
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using RimChat.Config;
using RimChat.Util;
using RimChat.Core;
using RimChat.Dialogue;

namespace RimChat.AI
{
    /// <summary>
    /// AI chat request state
    /// </summary>
    public enum AIRequestState
    {
        Idle,
        Pending,
        Queued,
        Processing,
        Completed,
        Error,
        Cancelled
    }

    /// <summary>
    /// AI chat request result
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
        public int ContextVersion { get; set; }
        public AIRequestDebugSource Source { get; set; }
        public AIRequestPriority Priority { get; set; }
        public DateTime EnqueuedAtUtc { get; set; }
        public DateTime QueueDeadlineUtc { get; set; }
        public DateTime StartedProcessingAtUtc { get; set; }
        public int QueuePosition { get; set; }
        public bool AllowCallbacks { get; set; }
        public string CancelReason { get; set; }
        public string FailureReason { get; set; }
        public int RequestTimeoutSeconds { get; set; }
        public float QueueTimeoutSeconds { get; set; }
        public int LastRequestPayloadBytes { get; set; }
        public long LastHttpStatusCode { get; set; }
        public int AttemptCount { get; set; }
        public string EndpointHostPort { get; set; }
        public DateTime FirstResponseByteAtUtc { get; set; }
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

    /// <summary>/// asyncAIchatservice - 使用Unity协程实现非阻塞通信
 ///</summary>
    public partial class AIChatServiceAsync : MonoBehaviour
    {
        private static AIChatServiceAsync _instance;
        private static readonly object _instanceLock = new object();
        public static AIChatServiceAsync Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            var go = new GameObject("AIChatServiceAsync");
                            _instance = go.AddComponent<AIChatServiceAsync>();
                            DontDestroyOnLoad(go);
                        }
                    }
                }
                return _instance;
            }
        }

        private readonly Dictionary<string, AIRequestResult> activeRequests = new Dictionary<string, AIRequestResult>();
        private readonly Queue<Action> mainThreadActions = new Queue<Action>();
        private readonly object lockObject = new object();
        private readonly Queue<string> localRequestQueue = new Queue<string>();
        private readonly HashSet<string> queuedLocalRequestIds = new HashSet<string>(StringComparer.Ordinal);
        private string activeLocalRequestId;
        private DialogueTokenUsageSnapshot latestDialogueTokenUsage;
        private int providerUsageAnomalyStreak;
        private const int ProviderUsageAnomalyFallbackThreshold = 2;
        private const int LocalServerMaxAttempts = 3;
        private const int LocalConnectionMaxAttempts = 2;
        private const int MaxImmersionRetryCount = 1;
        private const int MaxTextIntegrityRetryCount = 1;
        private const int MaxDiplomacyContractRetryCount = 1;
        private const int MaxRpgContractRetryCount = 1;
        private const int MaxParseRetryCount = 1;
        private const int LocalRequestTimeoutSeconds = 60;
        private const int CloudRequestTimeoutSeconds = 60;
        private const float RequestCleanupIntervalSeconds = 10f;
        private const double RequestResultRetentionMinutes = 5d;
        private const int MaxRetainedTerminalRequests = 256;
        private const string MinimalUserFollowSystemPrompt = "Please follow the system instructions and provide the requested output in plain text.";
        private float nextCleanupAtRealtime;
        private int contextVersion = 1;
        private int lastObservedGameContextId = -1;

        private static readonly Regex[] PromptTokensRegexes =
        {
            new Regex("\"prompt_tokens\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex("\"input_tokens\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex("\"promptTokenCount\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex("\"inputTokenCount\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        private static readonly Regex[] CompletionTokensRegexes =
        {
            new Regex("\"completion_tokens\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex("\"output_tokens\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex("\"candidatesTokenCount\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex("\"outputTokenCount\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        private static readonly Regex[] TotalTokensRegexes =
        {
            new Regex("\"total_tokens\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex("\"totalTokenCount\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex("\"total_token_count\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        void Update()
        {
            DetectGameContextChange();
            ProcessMainThreadActions();

            if (Time.realtimeSinceStartup >= nextCleanupAtRealtime)
            {
                CleanupCompletedRequests();
                lock (lockObject)
                {
                    CleanupPendingDebugRecordsLockless(DateTime.UtcNow);
                }
                nextCleanupAtRealtime = Time.realtimeSinceStartup + RequestCleanupIntervalSeconds;
            }
        }

        void Awake()
        {
            lastObservedGameContextId = GetCurrentGameContextId();
            nextCleanupAtRealtime = Time.realtimeSinceStartup + RequestCleanupIntervalSeconds;
        }

        /// <summary>/// 发送asyncchatrequest
 ///</summary>
        public string SendChatRequestAsync(
            List<ChatMessageData> messages,
            Action<string> onSuccess,
            Action<string> onError,
            Action<float> onProgress = null,
            DialogueUsageChannel usageChannel = DialogueUsageChannel.Unknown,
            AIRequestDebugSource debugSource = AIRequestDebugSource.Other,
            int? requestTimeoutSecondsOverride = null,
            float? queueTimeoutSecondsOverride = null)
        {
            List<ChatMessageData> normalizedMessages = NormalizeRequestMessagesForProvider(messages, usageChannel);
            string requestId = Guid.NewGuid().ToString("N");
            int requestContextVersion;
            int defaultTimeoutSeconds = RimChatMod.Instance == null ||
                                        !(RimChatMod.Instance.InstanceSettings?.UseCloudProviders ?? false)
                ? LocalRequestTimeoutSeconds
                : CloudRequestTimeoutSeconds;
            int requestTimeoutSeconds = Mathf.Clamp(
                requestTimeoutSecondsOverride ?? defaultTimeoutSeconds,
                5,
                120);
            float queueTimeoutSeconds = Mathf.Clamp(
                queueTimeoutSecondsOverride ?? 60f,
                5f,
                120f);

            CleanupCompletedRequests();
            
            var result = new AIRequestResult
            {
                State = AIRequestState.Pending,
                StartTime = DateTime.Now,
                Progress = 0f,
                Source = debugSource,
                Priority = ResolveRequestPriority(debugSource),
                AllowCallbacks = true,
                CancelReason = string.Empty,
                FailureReason = string.Empty,
                EnqueuedAtUtc = DateTime.MinValue,
                QueueDeadlineUtc = DateTime.MinValue,
                StartedProcessingAtUtc = DateTime.MinValue,
                QueuePosition = 0,
                RequestTimeoutSeconds = requestTimeoutSeconds,
                QueueTimeoutSeconds = queueTimeoutSeconds,
                LastRequestPayloadBytes = 0,
                LastHttpStatusCode = 0,
                AttemptCount = 0,
                EndpointHostPort = string.Empty,
                FirstResponseByteAtUtc = DateTime.MinValue
            };

            lock (lockObject)
            {
                requestContextVersion = contextVersion;
                result.ContextVersion = requestContextVersion;
                activeRequests[requestId] = result;
            }

            BeginRequestDebugRecord(requestId, usageChannel, debugSource);

            StartCoroutine(ProcessRequestCoroutine(
                requestId,
                normalizedMessages,
                onSuccess,
                onError,
                onProgress,
                usageChannel,
                debugSource,
                requestContextVersion,
                requestTimeoutSeconds));
            
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

        public int GetCurrentContextVersionSnapshot()
        {
            lock (lockObject)
            {
                return contextVersion;
            }
        }

        public static void NotifyGameContextChanged(string reason)
        {
            _instance?.HandleGameContextChanged(reason);
        }

        /// <summary>/// 取消指定的request
 ///</summary>
        public bool CancelRequest(
            string requestId,
            string cancelReason = "cancelled_by_user",
            string error = "Request cancelled by user")
        {
            lock (lockObject)
            {
                return TryCancelRequestLockless(requestId, cancelReason, error);
            }
        }

        public int CancelAllPendingRequests(string reason = "Request cancelled by context change")
        {
            int cancelled = 0;

            lock (lockObject)
            {
                foreach (var kvp in activeRequests)
                {
                    if (IsInFlightState(kvp.Value.State))
                    {
                        if (TryCancelRequestLockless(kvp.Key, "context_change", reason))
                        {
                            cancelled++;
                        }
                    }
                }
            }

            return cancelled;
        }

        /// <summary>/// getrequeststate
 ///</summary>
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

        /// <summary>/// 清理已completed的request
 ///</summary>
        public void CleanupCompletedRequests()
        {
            lock (lockObject)
            {
                var completedIds = new List<string>();
                var terminalRequests = new List<KeyValuePair<string, AIRequestResult>>();
                foreach (var kvp in activeRequests)
                {
                    if (IsTerminalState(kvp.Value.State))
                    {
                        terminalRequests.Add(kvp);
                        if ((DateTime.Now - kvp.Value.StartTime).TotalMinutes > RequestResultRetentionMinutes)
                        {
                            completedIds.Add(kvp.Key);
                        }
                    }
                }

                int retainedCount = terminalRequests.Count - completedIds.Count;
                if (retainedCount > MaxRetainedTerminalRequests)
                {
                    int extraCount = retainedCount - MaxRetainedTerminalRequests;
                    foreach (var kvp in terminalRequests
                        .Where(item => !completedIds.Contains(item.Key))
                        .OrderBy(item => item.Value.StartTime)
                        .Take(extraCount))
                    {
                        completedIds.Add(kvp.Key);
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
            DialogueUsageChannel usageChannel,
            AIRequestDebugSource debugSource,
            int requestContextVersion,
            int requestTimeoutSeconds)
        {
            AIRequestDebugStatus debugStatus = AIRequestDebugStatus.Error;
            string debugResponseText = string.Empty;
            string debugParsedResponse = string.Empty;
            string debugErrorText = string.Empty;
            long debugHttpCode = 0;
            List<ChatMessageData> debugTokenMessages = messages;
            bool debugRecordFinalized = false;

            if (!IsContextVersionCurrent(requestContextVersion))
            {
                MarkRequestAsDroppedByContext(requestId);
                debugStatus = AIRequestDebugStatus.Cancelled;
                debugErrorText = "Request dropped due to game context change";
                FinalizeRequestDebugRecord(
                    requestId,
                    debugTokenMessages,
                    debugResponseText,
                    debugParsedResponse,
                    debugStatus,
                    debugHttpCode,
                    debugErrorText);
                debugRecordFinalized = true;
                yield break;
            }

            var config = GetFirstValidConfig();
            if (config == null)
            {
                UpdateRequestState(requestId, AIRequestState.Error, error: "RimChat_ErrorNoConfig".Translate());
                ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke("RimChat_ErrorNoConfig".Translate()));
                debugStatus = AIRequestDebugStatus.Error;
                debugErrorText = "RimChat_ErrorNoConfig".Translate();
                FinalizeRequestDebugRecord(
                    requestId,
                    debugTokenMessages,
                    debugResponseText,
                    debugParsedResponse,
                    debugStatus,
                    debugHttpCode,
                    debugErrorText);
                debugRecordFinalized = true;
                yield break;
            }

            string url = config.GetEffectiveEndpoint();
            string apiKey = config.ApiKey;
            string model = config.GetEffectiveModelName();
            SetRequestDebugModel(requestId, model);
            bool isLocalModel = RimChatMod.Instance == null || 
                !(RimChatMod.Instance.InstanceSettings?.UseCloudProviders ?? false);
            RecordRequestTransportEnvelope(requestId, GetUrlHostPort(url));

            if (!ValidateUrl(url, out string urlError))
            {
                UpdateRequestState(requestId, AIRequestState.Error, error: urlError);
                ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(urlError));
                debugStatus = AIRequestDebugStatus.Error;
                debugErrorText = urlError ?? string.Empty;
                FinalizeRequestDebugRecord(
                    requestId,
                    debugTokenMessages,
                    debugResponseText,
                    debugParsedResponse,
                    debugStatus,
                    debugHttpCode,
                    debugErrorText);
                debugRecordFinalized = true;
                yield break;
            }

            if (messages == null || messages.Count == 0)
            {
                UpdateRequestState(requestId, AIRequestState.Error, error: "RimChat_ErrorEmptyMessage".Translate());
                ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke("RimChat_ErrorEmptyMessage".Translate()));
                debugStatus = AIRequestDebugStatus.Error;
                debugErrorText = "RimChat_ErrorEmptyMessage".Translate();
                FinalizeRequestDebugRecord(
                    requestId,
                    debugTokenMessages,
                    debugResponseText,
                    debugParsedResponse,
                    debugStatus,
                    debugHttpCode,
                    debugErrorText);
                debugRecordFinalized = true;
                yield break;
            }

            bool localSlotAcquired = false;
            if (isLocalModel)
            {
                EnqueueLocalRequest(requestId);
                while (!localSlotAcquired)
                {
                    if (!IsContextVersionCurrent(requestContextVersion))
                    {
                        MarkRequestAsDroppedByContext(requestId);
                        debugStatus = AIRequestDebugStatus.Cancelled;
                        debugErrorText = "Request dropped due to game context change";
                        FinalizeRequestDebugRecord(
                            requestId,
                            debugTokenMessages,
                            debugResponseText,
                            debugParsedResponse,
                            debugStatus,
                            debugHttpCode,
                            debugErrorText);
                        debugRecordFinalized = true;
                        yield break;
                    }

                    TryTimeoutQueuedRequest(requestId);
                    if (TryGetTerminalRequestDisposition(
                            requestId,
                            out AIRequestState waitingState,
                            out string waitingError,
                            out bool allowWaitingCallback))
                    {
                        if (allowWaitingCallback)
                        {
                            ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(waitingError));
                        }

                        debugStatus = waitingState == AIRequestState.Cancelled
                            ? AIRequestDebugStatus.Cancelled
                            : ClassifyDebugStatusFromError(waitingError);
                        debugErrorText = waitingError ?? string.Empty;
                        FinalizeRequestDebugRecord(
                            requestId,
                            debugTokenMessages,
                            debugResponseText,
                            debugParsedResponse,
                            debugStatus,
                            debugHttpCode,
                            debugErrorText);
                        debugRecordFinalized = true;
                        yield break;
                    }

                    localSlotAcquired = TryAcquireLocalRequestSlot(requestId);
                    if (!localSlotAcquired)
                    {
                        yield return new WaitForSeconds(0.05f);
                    }
                }
            }
            else
            {
                lock (lockObject)
                {
                    MarkRequestProcessingStartedLockless(requestId);
                }
            }

            List<ChatMessageData> attemptMessages = CloneMessages(messages);
            int attempt = 1;
            int local5xxRetryCount = 0;
            int localConnectionRetryCount = 0;
            int immersionRetryCount = 0;
            int textIntegrityRetryCount = 0;
            int contractRetryCount = 0;
            int parseRetryCount = 0;
            string contractValidationStatus = "not_applicable";
            string contractFailureReason = string.Empty;
            try
            {
                while (true)
                {
                    string jsonBody;
                    try
                    {
                        jsonBody = BuildChatCompletionJson(model, attemptMessages, config);
                    }
                    catch (Exception)
                    {
                        UpdateRequestState(requestId, AIRequestState.Error, error: "RimChat_ErrorBuildRequest".Translate());
                        ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke("RimChat_ErrorBuildRequest".Translate()));
                        debugStatus = AIRequestDebugStatus.Error;
                        debugErrorText = "RimChat_ErrorBuildRequest".Translate();
                        yield break;
                    }

                    SetRequestDebugPayload(requestId, jsonBody);
                    RecordRequestAttemptTelemetry(requestId, attempt, Encoding.UTF8.GetByteCount(jsonBody));

                    var stopwatch = Stopwatch.StartNew();
                    using (var request = new UnityWebRequest(url, "POST"))
                    {
                        RegisterActiveWebRequest(requestId, request);
                        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "application/json");
                        string trimmedApiKey = apiKey?.Trim() ?? string.Empty;
                        if (!isLocalModel || !string.IsNullOrEmpty(trimmedApiKey))
                        {
                            if (config.Provider == AIProvider.Google)
                            {
                                request.SetRequestHeader("Authorization", $"Bearer {trimmedApiKey}");
                            }
                            else
                            {
                                request.SetRequestHeader("Authorization", $"Bearer {trimmedApiKey}");
                            }
                        }

                        // Add provider-specific extra headers (e.g. player2-game-key for Player2)
                        var extraHeaders = config.Provider.GetExtraHeaders();
                        if (extraHeaders != null)
                        {
                            foreach (var header in extraHeaders)
                            {
                                request.SetRequestHeader(header.Key, header.Value);
                            }
                        }
                        request.timeout = requestTimeoutSeconds;

                        var operation = request.SendWebRequest();
                        float progress = 0f;

                        while (!operation.isDone)
                        {
                            progress = Mathf.Min(progress + 0.02f, 0.9f);
                            UpdateRequestProgress(requestId, progress);
                            ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onProgress?.Invoke(progress));
                            if (request.downloadedBytes > 0)
                            {
                                RecordRequestFirstResponseByte(requestId);
                            }
                            yield return new WaitForSeconds(0.1f);

                            if (!IsContextVersionCurrent(requestContextVersion))
                            {
                                request.Abort();
                                MarkRequestAsDroppedByContext(requestId);
                                debugStatus = AIRequestDebugStatus.Cancelled;
                                debugErrorText = "Request dropped due to game context change";
                                yield break;
                            }

                            if (TryGetTerminalRequestDisposition(
                                    requestId,
                                    out AIRequestState activeState,
                                    out string activeMessage,
                                    out bool allowActiveCallback))
                            {
                                request.Abort();
                                if (allowActiveCallback)
                                {
                                    ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(activeMessage));
                                }

                                debugStatus = activeState == AIRequestState.Cancelled
                                    ? AIRequestDebugStatus.Cancelled
                                    : ClassifyDebugStatusFromError(activeMessage);
                                debugErrorText = activeMessage ?? string.Empty;
                                yield break;
                            }
                        }

                        stopwatch.Stop();
                        UpdateRequestProgress(requestId, 1f);
                        ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onProgress?.Invoke(1f));

                        int jsonBytes = bodyRaw.Length;
                        LogRequestFingerprint(
                            requestId,
                            attempt,
                            usageChannel,
                            model,
                            url,
                            attemptMessages.Count,
                            jsonBytes,
                            stopwatch.ElapsedMilliseconds,
                            request.responseCode,
                            request.result,
                            "completed");
                        debugHttpCode = request.responseCode;
                        RecordRequestHttpStatus(requestId, request.responseCode);
                        if (request.downloadedBytes > 0)
                        {
                            RecordRequestFirstResponseByte(requestId);
                        }

                        if (TryGetTerminalRequestDisposition(
                                requestId,
                                out AIRequestState completedState,
                                out string completedMessage,
                                out bool allowCompletedCallback))
                        {
                            if (allowCompletedCallback)
                            {
                                ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(completedMessage));
                            }

                            debugStatus = completedState == AIRequestState.Cancelled
                                ? AIRequestDebugStatus.Cancelled
                                : ClassifyDebugStatusFromError(completedMessage);
                            debugErrorText = completedMessage ?? string.Empty;
                            yield break;
                        }

                        if (request.result == UnityWebRequest.Result.ConnectionError)
                        {
                            if (ShouldRetryLocalConnectionError(isLocalModel, debugSource, request.error, localConnectionRetryCount))
                            {
                                localConnectionRetryCount++;
                                float retryDelaySeconds = GetLocalConnectionRetryDelaySeconds(localConnectionRetryCount);
                                LogLocalConnectionRetryDecision(
                                    requestId,
                                    attempt,
                                    attempt + 1,
                                    request.error,
                                    retryDelaySeconds);
                                yield return new WaitForSeconds(retryDelaySeconds);
                                attempt++;
                                continue;
                            }

                            string errorMsg = isLocalModel
                                ? "RimChat_ErrorConnectionLocal".Translate()
                                : "RimChat_ErrorConnectionCloud".Translate();
                            if (LooksLikeTimeoutError(request.error))
                            {
                                errorMsg = "RimChat_ErrorTimeout".Translate();
                            }
                            lock (lockObject)
                            {
                                SetRequestFailureLockless(requestId, errorMsg, LooksLikeTimeoutError(request.error) ? "timeout" : "connection_error");
                            }
                            ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(errorMsg));
                            debugStatus = AIRequestDebugStatus.Error;
                            debugHttpCode = request.responseCode;
                            debugResponseText = request.error ?? string.Empty;
                            debugErrorText = errorMsg ?? string.Empty;
                            yield break;
                        }

                        if (request.result == UnityWebRequest.Result.ProtocolError)
                        {
                            string responseBody = request.downloadHandler?.text ?? string.Empty;

                            if (ShouldRetryLocalServerError(isLocalModel, request.responseCode, local5xxRetryCount))
                            {
                                local5xxRetryCount++;
                                float retryDelaySeconds = GetLocalServerRetryDelaySeconds(local5xxRetryCount);
                                LogLocalServerRetryDecision(
                                    requestId,
                                    attempt,
                                    attempt + 1,
                                    request.responseCode,
                                    retryDelaySeconds,
                                    responseBody);
                                yield return new WaitForSeconds(retryDelaySeconds);
                                attempt++;
                                continue;
                            }

                            DebugLogger.LogFullMessages(attemptMessages, $"HTTP {request.responseCode} ERROR\n{responseBody}");
                            Log.Error($"[RimChat] AI API Error (HTTP {request.responseCode}): {request.error}\nResponse Body: {responseBody}");

                            string errorMsg = FormatProtocolError(request.responseCode, isLocalModel);
                            if (!string.IsNullOrEmpty(responseBody) && responseBody.Length < 200)
                            {
                                errorMsg += $" ({responseBody})";
                            }

                            lock (lockObject)
                            {
                                SetRequestFailureLockless(requestId, errorMsg, $"http_{request.responseCode}");
                            }
                            ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(errorMsg));
                            debugStatus = AIRequestDebugStatus.Error;
                            debugHttpCode = request.responseCode;
                            debugResponseText = responseBody;
                            debugErrorText = errorMsg ?? string.Empty;
                            yield break;
                        }

                        if (request.result == UnityWebRequest.Result.DataProcessingError)
                        {
                            string errorMsg = "RimChat_ErrorDataProcessing".Translate(request.error);
                            lock (lockObject)
                            {
                                SetRequestFailureLockless(requestId, errorMsg, "data_processing_error");
                            }
                            ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(errorMsg));
                            debugStatus = AIRequestDebugStatus.Error;
                            debugHttpCode = request.responseCode;
                            debugResponseText = request.error ?? string.Empty;
                            debugErrorText = errorMsg ?? string.Empty;
                            yield break;
                        }

                        if (request.responseCode == 200)
                        {
                            string responseText = request.downloadHandler?.text;

                            DebugLogger.LogFullMessages(attemptMessages, responseText);

                            if (string.IsNullOrEmpty(responseText))
                            {
                                string errorMsg = "RimChat_ErrorEmptyResponse".Translate();
                                lock (lockObject)
                                {
                                    SetRequestFailureLockless(requestId, errorMsg, "empty_response");
                                }
                                ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(errorMsg));
                                debugStatus = AIRequestDebugStatus.Error;
                                debugResponseText = responseText ?? string.Empty;
                                debugErrorText = errorMsg ?? string.Empty;
                                yield break;
                            }

                            PrimaryTextExtractionResult parseResult = ParseResponse(responseText);
                            DebugLogger.LogParseExtraction("AIChatServiceAsync", parseResult);
                            if (!parseResult.IsSuccess)
                            {
                                string retryReason = BuildParseRetryReason(responseText, parseResult.ReasonTag);
                                bool isRetryableParseFailure = string.Equals(
                                        retryReason,
                                        "empty_primary_text",
                                        StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(
                                        retryReason,
                                        "assistant_role_without_content",
                                        StringComparison.OrdinalIgnoreCase);
                                if (isRetryableParseFailure && parseRetryCount < MaxParseRetryCount)
                                {
                                    parseRetryCount++;
                                    attemptMessages = AppendParseRetryMessage(
                                        attemptMessages,
                                        usageChannel,
                                        responseText,
                                        retryReason,
                                        parseResult.MatchedPath);
                                    Log.Warning(
                                        $"[RimChat] Parse retry requested: reason={retryReason}, path={parseResult.MatchedPath}, attempt={attempt}");
                                    attempt++;
                                    continue;
                                }

                                string failureTag = string.IsNullOrWhiteSpace(parseResult.ReasonTag)
                                    ? "parse_error"
                                    : $"parse_error_{parseResult.ReasonTag}";
                                string errorMsg = "RimChat_ErrorParseResponse".Translate();
                                lock (lockObject)
                                {
                                    SetRequestFailureLockless(requestId, errorMsg, failureTag);
                                }
                                ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(errorMsg));
                                string responsePreview = BuildResponsePreviewForLog(responseText, 420);
                                Log.Warning(
                                    $"[RimChat] Parse fail-fast: reason={parseResult.ReasonTag}, path={parseResult.MatchedPath}, retry_count={parseRetryCount}, response_preview={responsePreview}");
                                debugStatus = AIRequestDebugStatus.Error;
                                debugResponseText = responseText ?? string.Empty;
                                debugErrorText = $"{errorMsg} (reason={parseResult.ReasonTag}, path={parseResult.MatchedPath})";
                                yield break;
                            }
                            string parsedResponse = parseResult.Content;
                            bool bypassDialogueGuardsForSocialNews = debugSource == AIRequestDebugSource.SocialNews;
                            DialogueResponseEnvelope parsedEnvelope = null;

                            if (ShouldUseStructuredDialogueEnvelope(debugSource, usageChannel))
                            {
                                parsedEnvelope = DialogueResponseEnvelopeParser.Parse(parsedResponse, usageChannel);
                                if (!parsedEnvelope.IsValid && parseRetryCount < MaxParseRetryCount)
                                {
                                    parseRetryCount++;
                                    attemptMessages = AppendDialogueEnvelopeRetryMessage(
                                        attemptMessages,
                                        usageChannel,
                                        parsedEnvelope.FailureReason);
                                    Log.Warning($"[RimChat] Dialogue envelope retry requested: reason={parsedEnvelope.FailureReason}");
                                    attempt++;
                                    continue;
                                }

                                if (!parsedEnvelope.IsValid)
                                {
                                    string envelopeFailureReason = parsedEnvelope.FailureReason;
                                    string rawPassthrough = parsedResponse ?? string.Empty;
                                    parsedEnvelope = null;
                                    parsedResponse = rawPassthrough;
                                    string responsePreview = BuildResponsePreviewForLog(rawPassthrough, 280);
                                    Log.Warning($"[RimChat] Dialogue envelope raw passthrough used after retry: reason={envelopeFailureReason}, response_preview={responsePreview}");
                                }
                                else
                                {
                                    parsedResponse = parsedEnvelope.ToStructuredResponseText();
                                }
                            }

                            if (!bypassDialogueGuardsForSocialNews && ShouldGuardImmersion(usageChannel))
                            {
                                ImmersionGuardResult guardResult = parsedEnvelope != null
                                    ? ImmersionOutputGuard.ValidateVisibleDialogueParts(parsedEnvelope.VisibleDialogue, parsedEnvelope.ActionsJson)
                                    : ImmersionOutputGuard.ValidateVisibleDialogue(parsedResponse);
                                if (!guardResult.IsValid && immersionRetryCount < MaxImmersionRetryCount)
                                {
                                    immersionRetryCount++;
                                    attemptMessages = AppendImmersionRetryMessage(attemptMessages, usageChannel, guardResult);
                                    Log.Warning($"[RimChat] Immersion guard requested retry: reason={ImmersionOutputGuard.BuildViolationTag(guardResult.ViolationReason)}, snippet={guardResult.ViolationSnippet}");
                                    attempt++;
                                    continue;
                                }

                                if (!guardResult.IsValid)
                                {
                                    Log.Warning($"[RimChat] Immersion guard failed after retry, outputting raw response: reason={ImmersionOutputGuard.BuildViolationTag(guardResult.ViolationReason)}");
                                }
                                else
                                {
                                    if (parsedEnvelope != null)
                                    {
                                        parsedEnvelope.VisibleDialogue = guardResult.VisibleDialogue;
                                        parsedEnvelope.ActionsJson = guardResult.TrailingActionsJson;
                                        parsedResponse = parsedEnvelope.ToStructuredResponseText();
                                    }
                                    else
                                    {
                                        parsedResponse = ModelOutputSanitizer.ComposeVisibleAndTrailingActions(
                                            guardResult.VisibleDialogue,
                                            guardResult.TrailingActionsJson);
                                    }
                                }
                            }

                            if (!bypassDialogueGuardsForSocialNews && ShouldGuardImmersion(usageChannel))
                            {
                                TextIntegrityCheckResult integrityResult = parsedEnvelope != null
                                    ? TextIntegrityGuard.ValidateVisibleDialogueParts(parsedEnvelope.VisibleDialogue, parsedEnvelope.ActionsJson)
                                    : TextIntegrityGuard.ValidateVisibleDialogue(parsedResponse);
                                if (!integrityResult.IsValid && textIntegrityRetryCount < MaxTextIntegrityRetryCount)
                                {
                                    textIntegrityRetryCount++;
                                    attemptMessages = AppendTextIntegrityRetryMessage(attemptMessages, usageChannel, integrityResult);
                                    Log.Warning($"[RimChat] Text integrity guard requested retry: reason={integrityResult.ReasonTag}");
                                    attempt++;
                                    continue;
                                }

                                if (!integrityResult.IsValid)
                                {
                                    Log.Warning($"[RimChat] Text integrity guard failed after retry, outputting raw response: reason={integrityResult.ReasonTag}");
                                }
                                else
                                {
                                    if (parsedEnvelope != null)
                                    {
                                        parsedEnvelope.VisibleDialogue = integrityResult.VisibleDialogue;
                                        parsedEnvelope.ActionsJson = integrityResult.TrailingActionsJson;
                                        parsedResponse = parsedEnvelope.ToStructuredResponseText();
                                    }
                                    else
                                    {
                                        parsedResponse = ModelOutputSanitizer.ComposeVisibleAndTrailingActions(
                                            integrityResult.VisibleDialogue,
                                            integrityResult.TrailingActionsJson);
                                    }
                                }
                            }

                            if (!bypassDialogueGuardsForSocialNews && usageChannel == DialogueUsageChannel.Diplomacy)
                            {
                                DiplomacyResponseContractCheckResult contractResult = parsedEnvelope != null
                                    ? DiplomacyResponseContractGuard.ValidateVisibleDialogueParts(parsedEnvelope.VisibleDialogue, parsedEnvelope.ActionsJson)
                                    : DiplomacyResponseContractGuard.Validate(parsedResponse);
                                if (!contractResult.IsValid && contractRetryCount < MaxDiplomacyContractRetryCount)
                                {
                                    contractRetryCount++;
                                    contractValidationStatus = "retry";
                                    contractFailureReason =
                                        DiplomacyResponseContractGuard.BuildViolationTag(contractResult.Violation);
                                    attemptMessages = AppendDiplomacyContractRetryMessage(attemptMessages, contractResult);
                                    Log.Warning(
                                        $"[RimChat] Diplomacy contract guard requested retry: reason={contractFailureReason}");
                                    attempt++;
                                    continue;
                                }

                                if (!contractResult.IsValid)
                                {
                                    contractValidationStatus = "failed_after_retry";
                                    contractFailureReason =
                                        DiplomacyResponseContractGuard.BuildViolationTag(contractResult.Violation);
                                    Log.Warning(
                                        $"[RimChat] Diplomacy contract guard failed after retry, outputting raw response: reason={contractFailureReason}");
                                }
                                else
                                {
                                    contractValidationStatus = contractRetryCount > 0 ? "pass_after_retry" : "pass";
                                    contractFailureReason = string.Empty;
                                    if (parsedEnvelope != null)
                                    {
                                        parsedEnvelope.VisibleDialogue = contractResult.VisibleDialogue;
                                        parsedEnvelope.ActionsJson = contractResult.TrailingActionsJson;
                                        parsedResponse = parsedEnvelope.ToStructuredResponseText();
                                    }
                                    else
                                    {
                                        parsedResponse = ModelOutputSanitizer.ComposeVisibleAndTrailingActions(
                                            contractResult.VisibleDialogue,
                                            contractResult.TrailingActionsJson);
                                    }
                                }
                            }

                            if (!bypassDialogueGuardsForSocialNews && usageChannel == DialogueUsageChannel.Rpg)
                            {
                                RpgResponseContractCheckResult contractResult = parsedEnvelope != null
                                    ? RpgResponseContractGuard.ValidateVisibleDialogueParts(
                                        parsedEnvelope.VisibleDialogue,
                                        parsedEnvelope.ActionsJson,
                                        parsedEnvelope.ActionsJson)
                                    : RpgResponseContractGuard.Validate(parsedResponse);
                                if (!contractResult.IsValid && contractRetryCount < MaxRpgContractRetryCount)
                                {
                                    contractRetryCount++;
                                    contractValidationStatus = "retry";
                                    contractFailureReason = RpgResponseContractGuard.BuildViolationTag(contractResult.Violation);
                                    attemptMessages = AppendRpgContractRetryMessage(attemptMessages, contractResult);
                                    Log.Warning($"[RimChat] RPG contract guard requested retry: reason={contractFailureReason}");
                                    attempt++;
                                    continue;
                                }

                                if (!contractResult.IsValid)
                                {
                                    contractValidationStatus = "failed_after_retry";
                                    contractFailureReason = RpgResponseContractGuard.BuildViolationTag(contractResult.Violation);
                                    Log.Warning($"[RimChat] RPG contract guard failed after retry, outputting raw response: reason={contractFailureReason}");
                                }
                                else
                                {
                                    contractValidationStatus = contractRetryCount > 0 ? "pass_after_retry" : "pass";
                                    contractFailureReason = string.Empty;
                                    if (parsedEnvelope != null)
                                    {
                                        parsedEnvelope.VisibleDialogue = contractResult.VisibleDialogue;
                                        parsedEnvelope.ActionsJson = contractResult.TrailingActionsJson;
                                        parsedResponse = parsedEnvelope.ToStructuredResponseText();
                                    }
                                    else
                                    {
                                        parsedResponse = ModelOutputSanitizer.ComposeVisibleAndTrailingActions(
                                            contractResult.VisibleDialogue,
                                            contractResult.TrailingActionsJson);
                                    }
                                }
                            }

                            TryRecordDialogueTokenUsage(attemptMessages, responseText, parsedResponse, usageChannel);
                            UpdateRequestState(requestId, AIRequestState.Completed, response: parsedResponse);
                            ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onSuccess?.Invoke(parsedResponse));
                            debugStatus = AIRequestDebugStatus.Success;
                            debugHttpCode = request.responseCode;
                            debugResponseText = responseText ?? string.Empty;
                            debugParsedResponse = parsedResponse;
                            debugErrorText = string.Empty;
                            debugTokenMessages = attemptMessages;
                            yield break;
                        }

                        string fallbackError = $"HTTP {request.responseCode}: {request.error}";
                        lock (lockObject)
                        {
                            SetRequestFailureLockless(requestId, fallbackError, "unexpected_http_error");
                        }
                        ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(fallbackError));
                        debugStatus = AIRequestDebugStatus.Error;
                        debugHttpCode = request.responseCode;
                        debugResponseText = request.downloadHandler?.text ?? request.error ?? string.Empty;
                        debugErrorText = fallbackError;
                        yield break;
                    }

                }
            }
            finally
            {
                UnregisterActiveWebRequest(requestId);

                if (isLocalModel)
                {
                    if (localSlotAcquired)
                    {
                        ReleaseLocalRequestSlot(requestId);
                    }
                    else
                    {
                        RemoveLocalRequest(requestId);
                    }
                }

                if (!debugRecordFinalized)
                {
                    FinalizeRequestDebugRecord(
                        requestId,
                        debugTokenMessages,
                        debugResponseText,
                        debugParsedResponse,
                        debugStatus,
                        debugHttpCode,
                        debugErrorText,
                        contractValidationStatus,
                        contractRetryCount,
                        contractFailureReason);
                }
            }
        }

        private bool IsContextVersionCurrent(int expectedContextVersion)
        {
            lock (lockObject)
            {
                return expectedContextVersion == contextVersion;
            }
        }

        private void ExecuteRequestActionOnMainThread(string requestId, int expectedContextVersion, Action action)
        {
            ExecuteOnMainThread(() =>
            {
                if (!IsRequestCallbackAllowed(requestId, expectedContextVersion))
                {
                    return;
                }

                action?.Invoke();
            });
        }

        private bool IsRequestCallbackAllowed(string requestId, int expectedContextVersion)
        {
            lock (lockObject)
            {
                if (expectedContextVersion != contextVersion)
                {
                    return false;
                }

                if (!activeRequests.TryGetValue(requestId, out AIRequestResult result))
                {
                    return false;
                }

                return result.ContextVersion == expectedContextVersion &&
                       result.AllowCallbacks &&
                       result.State != AIRequestState.Cancelled;
            }
        }

        private void ProcessMainThreadActions()
        {
            while (true)
            {
                Action action;
                lock (lockObject)
                {
                    if (mainThreadActions.Count == 0)
                    {
                        break;
                    }

                    action = mainThreadActions.Dequeue();
                }

                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimChat] Error executing main thread action: {ex.Message}");
                }
            }
        }

        private void MarkRequestAsDroppedByContext(string requestId)
        {
            lock (lockObject)
            {
                TryCancelRequestLockless(requestId, "context_changed", "Request dropped due to game context change");
            }
        }

        private void DetectGameContextChange()
        {
            int currentContextId = GetCurrentGameContextId();
            if (currentContextId == lastObservedGameContextId)
            {
                return;
            }

            if (lastObservedGameContextId == -1)
            {
                lastObservedGameContextId = currentContextId;
                return;
            }

            HandleGameContextChanged("Detected game context transition");
        }

        private void HandleGameContextChanged(string reason)
        {
            int cancelledCount;
            lock (lockObject)
            {
                contextVersion++;
                lastObservedGameContextId = GetCurrentGameContextId();
                cancelledCount = 0;

                foreach (var kvp in activeRequests)
                {
                    if (IsInFlightState(kvp.Value.State))
                    {
                        if (TryCancelRequestLockless(kvp.Key, "save_context_changed", "Request cancelled due to save/game context change"))
                        {
                            cancelledCount++;
                        }
                    }
                }

                mainThreadActions.Clear();
                interactiveLocalRequestQueue.Clear();
                localRequestQueue.Clear();
                queuedLocalRequestIds.Clear();
                activeWebRequests.Clear();
                activeLocalRequestId = null;
            }

            CleanupCompletedRequests();

            if (cancelledCount > 0)
            {
                Log.Message($"[RimChat] Cancelled {cancelledCount} pending AI requests due to context change: {reason}");
            }
        }

        private static int GetCurrentGameContextId()
        {
            return Current.Game == null ? 0 : Current.Game.GetHashCode();
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
                    if (state == AIRequestState.Completed)
                    {
                        result.AllowCallbacks = true;
                        result.CancelReason = string.Empty;
                        result.FailureReason = string.Empty;
                        result.QueueDeadlineUtc = DateTime.MinValue;
                        result.QueuePosition = 0;
                        result.StartedProcessingAtUtc = result.StartedProcessingAtUtc == DateTime.MinValue
                            ? DateTime.UtcNow
                            : result.StartedProcessingAtUtc;
                    }
                    else if (state == AIRequestState.Error)
                    {
                        result.AllowCallbacks = true;
                        if (string.IsNullOrWhiteSpace(result.FailureReason))
                        {
                            result.FailureReason = "request_error";
                        }

                        result.CancelReason = string.Empty;
                        result.QueueDeadlineUtc = DateTime.MinValue;
                        result.QueuePosition = 0;
                    }

                    if (state == AIRequestState.Completed ||
                        state == AIRequestState.Error ||
                        state == AIRequestState.Cancelled)
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

        private void RecordRequestTransportEnvelope(string requestId, string endpointHostPort)
        {
            lock (lockObject)
            {
                if (activeRequests.TryGetValue(requestId, out AIRequestResult result))
                {
                    result.EndpointHostPort = endpointHostPort ?? string.Empty;
                }
            }
        }

        private void RecordRequestAttemptTelemetry(string requestId, int attempt, int payloadBytes)
        {
            lock (lockObject)
            {
                if (activeRequests.TryGetValue(requestId, out AIRequestResult result))
                {
                    result.AttemptCount = Math.Max(1, attempt);
                    result.LastRequestPayloadBytes = Math.Max(0, payloadBytes);
                }
            }
        }

        private void RecordRequestHttpStatus(string requestId, long httpStatusCode)
        {
            lock (lockObject)
            {
                if (activeRequests.TryGetValue(requestId, out AIRequestResult result))
                {
                    result.LastHttpStatusCode = httpStatusCode;
                }
            }
        }

        private void RecordRequestFirstResponseByte(string requestId)
        {
            lock (lockObject)
            {
                if (!activeRequests.TryGetValue(requestId, out AIRequestResult result))
                {
                    return;
                }

                if (result.FirstResponseByteAtUtc != DateTime.MinValue)
                {
                    return;
                }

                result.FirstResponseByteAtUtc = DateTime.UtcNow;
            }
        }

        private string FormatProtocolError(long responseCode, bool isLocalModel)
        {
            return responseCode switch
            {
                401 => isLocalModel 
                    ? "RimChat_Error401Local".Translate() 
                    : "RimChat_Error401Cloud".Translate(),
                404 => "RimChat_Error404".Translate(),
                429 => "RimChat_ErrorRateLimit".Translate(),
                500 => "RimChat_ErrorServer500".Translate(),
                502 => "RimChat_ErrorServer502".Translate(),
                503 => "RimChat_ErrorServer503".Translate(),
                _ => "RimChat_ErrorHTTP".Translate(responseCode)
            };
        }

        private static bool ShouldRetryRejectedInput(long responseCode, string responseBody, DialogueUsageChannel usageChannel)
        {
            if (responseCode != 400)
            {
                return false;
            }

            if (usageChannel == DialogueUsageChannel.Unknown)
            {
                return false;
            }

            string lower = (responseBody ?? string.Empty).ToLowerInvariant();
            return lower.Contains("user input rejected") ||
                   lower.Contains("input rejected") ||
                   lower.Contains("content policy") ||
                   lower.Contains("safety");
        }

        private static List<ChatMessageData> BuildRejectedInputFallbackMessages(
            List<ChatMessageData> source,
            DialogueUsageChannel usageChannel)
        {
            if (source == null || source.Count == 0)
            {
                return new List<ChatMessageData>();
            }

            int maxSystemChars = usageChannel == DialogueUsageChannel.Rpg ? 3000 : 3600;
            int maxHistoryMessages = usageChannel == DialogueUsageChannel.Rpg ? 6 : 8;
            int maxMessageChars = usageChannel == DialogueUsageChannel.Rpg ? 320 : 380;

            var fallback = new List<ChatMessageData>();
            ChatMessageData firstSystem = source.FirstOrDefault(msg =>
                msg != null &&
                string.Equals(msg.role, "system", StringComparison.OrdinalIgnoreCase));
            if (firstSystem != null)
            {
                fallback.Add(new ChatMessageData
                {
                    role = firstSystem.role ?? "system",
                    content = TrimMessageContent(firstSystem.content, maxSystemChars)
                });
            }

            List<ChatMessageData> nonSystem = source
                .Where(msg =>
                    msg != null &&
                    !string.Equals(msg.role, "system", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (nonSystem.Count > maxHistoryMessages)
            {
                nonSystem = nonSystem.Skip(nonSystem.Count - maxHistoryMessages).ToList();
            }

            for (int i = 0; i < nonSystem.Count; i++)
            {
                ChatMessageData msg = nonSystem[i];
                fallback.Add(new ChatMessageData
                {
                    role = msg.role ?? "user",
                    content = TrimMessageContent(msg.content, maxMessageChars)
                });
            }

            if (fallback.Count == 0 && source[0] != null)
            {
                fallback.Add(new ChatMessageData
                {
                    role = source[0].role ?? "user",
                    content = TrimMessageContent(source[0].content, maxMessageChars)
                });
            }

            if (!HasUserMessage(fallback))
            {
                fallback.Add(new ChatMessageData
                {
                    role = "user",
                    content = BuildMinimalUserPrompt(usageChannel)
                });
            }

            if (usageChannel == DialogueUsageChannel.Rpg)
            {
                fallback.Add(new ChatMessageData
                {
                    role = "user",
                    content = BuildCompactRetryContractPrompt(DialogueUsageChannel.Rpg)
                });
            }
            else if (usageChannel == DialogueUsageChannel.Diplomacy)
            {
                fallback.Add(new ChatMessageData
                {
                    role = "user",
                    content = BuildCompactRetryContractPrompt(DialogueUsageChannel.Diplomacy)
                });
            }

            return NormalizeRequestMessagesForProvider(fallback, usageChannel);
        }

        private static List<ChatMessageData> NormalizeRequestMessagesForProvider(
            List<ChatMessageData> source,
            DialogueUsageChannel usageChannel)
        {
            List<ChatMessageData> normalized = CollectNormalizedMessages(source);
            if (normalized.Count > 0 && !HasUserMessage(normalized))
            {
                normalized.Add(new ChatMessageData
                {
                    role = "user",
                    content = BuildMinimalUserPrompt(usageChannel)
                });
            }

            return normalized;
        }

        private static bool ShouldGuardImmersion(DialogueUsageChannel usageChannel)
        {
            return usageChannel == DialogueUsageChannel.Diplomacy || usageChannel == DialogueUsageChannel.Rpg;
        }

        private static bool ShouldUseStructuredDialogueEnvelope(
            AIRequestDebugSource debugSource,
            DialogueUsageChannel usageChannel)
        {
            if (!ShouldGuardImmersion(usageChannel))
            {
                return false;
            }

            return debugSource == AIRequestDebugSource.DiplomacyDialogue ||
                debugSource == AIRequestDebugSource.RpgDialogue ||
                debugSource == AIRequestDebugSource.NpcPush ||
                debugSource == AIRequestDebugSource.PawnRpgPush;
        }

        private static List<ChatMessageData> AppendDialogueEnvelopeRetryMessage(
            List<ChatMessageData> messages,
            DialogueUsageChannel usageChannel,
            string reasonTag)
        {
            List<ChatMessageData> updated = CloneMessages(messages);
            string example = usageChannel == DialogueUsageChannel.Rpg
                ? "{\"visible_dialogue\":\"角色的一句对白\"}"
                : "{\"visible_dialogue\":\"外交发言文本\"}";
            string hint = usageChannel == DialogueUsageChannel.Rpg
                ? "Put one in-character NPC line inside visible_dialogue."
                : "Put 1-2 in-character diplomacy sentences inside visible_dialogue.";
            updated.Add(new ChatMessageData
            {
                role = "user",
                content = $"DIALOGUE_PROTOCOL_VIOLATION={reasonTag ?? "invalid_dialogue_contract"}. "
                    + $"你的上一条回复格式不符合协议要求。请严格输出一个 JSON 对象，首字符 {{ 末字符 }}，不要附加任何自然语言。"
                    + $"将你的发言文本放入 visible_dialogue 字段。示例：{example} 若需动作则在同一 JSON 内追加 actions 数组。"
                    + $" "
                    + $"Your last response violated the dialogue protocol. Output exactly one JSON object — first char {{, last char }}. {hint} "
                    + $"Example: {example}. If actions are needed, add them inside the same JSON object. "
                    + $"No text, markdown, or explanations outside the JSON object."
            });
            return NormalizeRequestMessagesForProvider(updated, usageChannel);
        }

        private static List<ChatMessageData> AppendImmersionRetryMessage(
            List<ChatMessageData> messages,
            DialogueUsageChannel usageChannel,
            ImmersionGuardResult guardResult)
        {
            List<ChatMessageData> updated = CloneMessages(messages);
            string reasonTag = ImmersionOutputGuard.BuildViolationTag(guardResult?.ViolationReason ?? ImmersionViolationReason.None);
            string snippet = guardResult?.ViolationSnippet ?? string.Empty;
            string problem = reasonTag switch
            {
                "reasoning_leakage" => "暴露了推理过程",
                "mechanic_keyword" => "提到了游戏机制关键词",
                "parenthetical_metadata" => "用括号备注了系统状态",
                "status_panel_numeric" => "暴露了数值型系统状态",
                _ => "包含了不符合沉浸感的内容"
            };
            string hint = usageChannel == DialogueUsageChannel.Rpg
                ? "只写角色的一句自然对白。"
                : "只写1-2句角色的自然外交发言。";
            updated.Add(new ChatMessageData
            {
                role = "user",
                content = $"IMMERSION_VIOLATION={reasonTag}; snippet={snippet}. "
                    + $"你的上一条回复{problem}（违规片段：{snippet}）。请重新输出一个纯 JSON 对象，"
                    + $"首字符 {{ 末字符 }}。{hint}"
                    + $"将所有可见文本放入 visible_dialogue。禁止在正文中包含系统状态、数值面板、推理过程或括号备注。"
                    + $" "
                    + $"IMMERSION_VIOLATION={reasonTag}. Your last reply {problem}. "
                    + $"Rewrite as exactly one JSON object, first char {{ last char }}. {hint} "
                    + $"No system-state numbers, reasoning, or parenthetical notes in the visible text."
            });
            return NormalizeRequestMessagesForProvider(updated, usageChannel);
        }

        private static List<ChatMessageData> AppendTextIntegrityRetryMessage(
            List<ChatMessageData> messages,
            DialogueUsageChannel usageChannel,
            TextIntegrityCheckResult integrityResult)
        {
            List<ChatMessageData> updated = CloneMessages(messages);
            string reasonTag = integrityResult?.ReasonTag ?? "unknown";
            string hint = usageChannel == DialogueUsageChannel.Rpg
                ? "Rewrite only visible NPC dialogue in clean natural language. Keep roleplay immersion."
                : "Rewrite only visible faction dialogue in clean natural language. Keep in-character immersion.";
            updated.Add(new ChatMessageData
            {
                role = "user",
                content = $"TEXT_INTEGRITY_VIOLATION={reasonTag}. {hint} Output exactly one JSON object only. Put visible dialogue inside visible_dialogue. Keep actions inside the same top-level JSON object when needed. Remove garbled fragments and mojibake. Do not add notes, headers, or extra text outside the JSON object."
            });
            return NormalizeRequestMessagesForProvider(updated, usageChannel);
        }

        private static List<ChatMessageData> AppendRpgContractRetryMessage(
            List<ChatMessageData> messages,
            RpgResponseContractCheckResult contractResult)
        {
            List<ChatMessageData> updated = CloneMessages(messages);
            string reasonTag = RpgResponseContractGuard.BuildViolationTag(contractResult?.Violation ?? RpgResponseContractViolation.None);
            updated.Add(new ChatMessageData
            {
                role = "user",
                content = $"RPG_CONTRACT_VIOLATION={reasonTag}. Return exactly one JSON object only. visible_dialogue must be one single-line in-character dialogue sentence. If gameplay effects are needed, include them in the same top-level actions array; otherwise omit actions. Do not place dialogue outside JSON. Do not append a trailing JSON object. Do not use placeholder values (OptionalDef/OptionalReason/amount:0)."
            });
            return NormalizeRequestMessagesForProvider(updated, DialogueUsageChannel.Rpg);
        }

        private static List<ChatMessageData> AppendDiplomacyContractRetryMessage(
            List<ChatMessageData> messages,
            DiplomacyResponseContractCheckResult contractResult)
        {
            List<ChatMessageData> updated = CloneMessages(messages);
            string reasonTag = DiplomacyResponseContractGuard.BuildViolationTag(
                contractResult?.Violation ?? DiplomacyResponseContractViolation.None);
            updated.Add(new ChatMessageData
            {
                role = "user",
                content =
                    $"DIPLOMACY_CONTRACT_VIOLATION={reasonTag}. Return exactly one JSON object only with visible_dialogue and optional actions. Put all visible dialogue inside visible_dialogue. If you make explicit execution commitments (arranged/submitted/dispatched), include the matching action inside the same top-level actions array. Do not place dialogue outside JSON. Do not append a trailing JSON object. " +
                    "Use request_info(info_type=prisoner) only when ransom target information is missing; if target_pawn_load_id is already valid, pay_prisoner_ransom may be called directly. " +
                    "For pay_prisoner_ransom, never claim payment/submission unless target_pawn_load_id and offer_silver are both valid positive integers. " +
                    "For pay_prisoner_ransom, keep offer_silver inside the current offer window from system messages; current ask is a preferred reference, not a strict exact-match requirement. If offer_silver is out of range, execution will clamp it to the nearest window boundary before submit. " +
                    "If a [RansomBatchSelection] block is present and you choose to output pay_prisoner_ransom this turn, output one action for every listed target_pawn_load_id exactly once in the same response, and keep total offer_silver inside the provided batch window. " +
                    "If target is unknown or offer is missing, rewrite as one in-character clarification question and do NOT claim the request was submitted."
            });
            return NormalizeRequestMessagesForProvider(updated, DialogueUsageChannel.Diplomacy);
        }

        private static List<ChatMessageData> AppendParseRetryMessage(
            List<ChatMessageData> messages,
            DialogueUsageChannel usageChannel,
            string rawResponse,
            string reasonTag,
            string matchedPath)
        {
            List<ChatMessageData> updated = CloneMessages(messages);
            string reason = BuildParseRetryReason(rawResponse, reasonTag);
            string path = string.IsNullOrWhiteSpace(matchedPath) ? "n/a" : matchedPath;
            string hint = usageChannel switch
            {
                DialogueUsageChannel.Rpg =>
                    "Return exactly one JSON object only. visible_dialogue must be one single-line in-character dialogue sentence. Include actions only inside the same top-level actions array when gameplay effects are required.",
                DialogueUsageChannel.Diplomacy =>
                    "Return exactly one JSON object only. Put 1-2 concise in-character diplomacy sentences inside visible_dialogue. Include actions only inside the same top-level actions array when needed.",
                _ =>
                    "Return plain visible text content directly."
            };

            updated.Add(new ChatMessageData
            {
                role = "user",
                content = $"PARSE_RETRY_REASON={reason}; PARSE_MATCH_PATH={path}. Previous output could not be parsed into visible text. {hint} Do not output empty content."
            });
            return NormalizeRequestMessagesForProvider(updated, usageChannel);
        }

        private static string BuildParseRetryReason(string rawResponse, string reasonTag)
        {
            if (!string.IsNullOrWhiteSpace(reasonTag) &&
                !string.Equals(reasonTag, "no_extractable_text", StringComparison.OrdinalIgnoreCase))
            {
                return reasonTag;
            }

            string payload = rawResponse ?? string.Empty;
            if (payload.IndexOf("\"role\":\"assistant\"", StringComparison.OrdinalIgnoreCase) >= 0 &&
                payload.IndexOf("\"content\"", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return "assistant_role_without_content";
            }

            return "no_extractable_text";
        }

        private static string BuildResponsePreviewForLog(string responseText, int maxChars)
        {
            string raw = responseText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "<empty>";
            }

            string singleLine = raw
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Trim();
            if (maxChars <= 0 || singleLine.Length <= maxChars)
            {
                return singleLine;
            }

            return singleLine.Substring(0, maxChars) + "...";
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
                    role = NormalizeOutgoingRole(msg.role),
                    content = msg.content ?? string.Empty
                });
            }

            return normalized;
        }

        private static bool HasUserMessage(List<ChatMessageData> messages)
        {
            return messages != null && messages.Any(msg =>
                msg != null &&
                string.Equals(msg.role, "user", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(msg.content));
        }

        private static string NormalizeOutgoingRole(string role)
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

        private static string BuildMinimalUserPrompt(DialogueUsageChannel usageChannel)
        {
            return MinimalUserFollowSystemPrompt;
        }

        private static string BuildCompactRetryContractPrompt(DialogueUsageChannel usageChannel)
        {
            string style = ResolveCompactStyleDirective();
            if (usageChannel == DialogueUsageChannel.Rpg)
            {
                return style + " Output one in-character dialogue line. " +
                    "Only append one trailing {\"actions\":[...]} JSON object when gameplay effects are required. " +
                    "Do not wrap dialogue in JSON fields.";
            }

            return style + " Output in-character diplomacy dialogue. " +
                "Prefer 1-2 concise sentences. " +
                "Only append one trailing {\"actions\":[{\"action\":\"snake_case_action\",\"parameters\":{...}}]} JSON object when needed. " +
                "For pay_prisoner_ransom, keep offer_silver inside the current offer window from system messages; current ask is recommended but not mandatory exact match. If offer_silver is out of range, execution will clamp it to the nearest window boundary before submit. " +
                "When [RansomBatchSelection] exists and you emit pay_prisoner_ransom, emit exactly one action per listed target in that same response and keep the total offer inside the batch window. " +
                "Do not wrap dialogue in JSON fields.";
        }

        private static string ResolveCompactStyleDirective()
        {
            RimChatSettings settings = RimChatMod.Settings ?? RimChatMod.Instance?.InstanceSettings;
            DialogueStyleMode mode = settings?.DialogueStyleMode ?? DialogueStyleMode.NaturalConcise;
            return mode switch
            {
                DialogueStyleMode.Immersive => "Stay in-character and avoid meta/system wording.",
                DialogueStyleMode.Balanced => "Stay human and concise, avoid mechanical/system wording.",
                _ => "Keep a natural human tone and avoid mechanical/system wording."
            };
        }

        private static List<ChatMessageData> CloneMessages(List<ChatMessageData> source)
        {
            var result = new List<ChatMessageData>();
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                ChatMessageData msg = source[i];
                if (msg == null)
                {
                    continue;
                }

                result.Add(new ChatMessageData
                {
                    role = msg.role ?? string.Empty,
                    content = msg.content ?? string.Empty
                });
            }

            return result;
        }

        private static string TrimMessageContent(string content, int maxChars)
        {
            string value = content ?? string.Empty;
            if (maxChars <= 0 || value.Length <= maxChars)
            {
                return value;
            }

            if (maxChars <= 3)
            {
                return value.Substring(0, maxChars);
            }

            return value.Substring(0, maxChars - 3) + "...";
        }

        private bool ValidateUrl(string url, out string error)
        {
            error = null;

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
            catch (UriFormatException)
            {
                error = "RimChat_ErrorMalformedUrl".Translate();
                return false;
            }

            return true;
        }

        private PrimaryTextExtractionResult ParseResponse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new PrimaryTextExtractionResult
                {
                    IsSuccess = false,
                    Content = string.Empty,
                    ReasonTag = "invalid_payload",
                    MatchedPath = string.Empty
                };
            }

            try
            {
                if (AIJsonContentExtractor.IsErrorPayload(json))
                {
                    return new PrimaryTextExtractionResult
                    {
                        IsSuccess = false,
                        Content = string.Empty,
                        ReasonTag = "error_payload",
                        MatchedPath = string.Empty
                    };
                }

                return AIJsonContentExtractor.TryExtractPrimaryText(json);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to parse AI response: {ex.Message}");
                return new PrimaryTextExtractionResult
                {
                    IsSuccess = false,
                    Content = string.Empty,
                    ReasonTag = "extractor_exception",
                    MatchedPath = string.Empty
                };
            }
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

            EstimateTokenUsage(messages, parsedResponse, out int estimatedPromptTokens, out int estimatedCompletionTokens, out int estimatedTotalTokens);
            bool hasUsage = TryExtractUsage(rawJsonResponse, out int providerPromptTokens, out int providerCompletionTokens, out int providerTotalTokens);
            bool providerLooksAbnormal = hasUsage && ShouldUseEstimatedUsage(
                providerPromptTokens,
                providerCompletionTokens,
                providerTotalTokens,
                estimatedPromptTokens,
                estimatedCompletionTokens,
                estimatedTotalTokens);
            int anomalyStreak = UpdateProviderUsageAnomalyStreak(hasUsage, providerLooksAbnormal);
            bool useEstimated = !hasUsage || (providerLooksAbnormal && anomalyStreak >= ProviderUsageAnomalyFallbackThreshold);

            int promptTokens = useEstimated ? estimatedPromptTokens : providerPromptTokens;
            int completionTokens = useEstimated ? estimatedCompletionTokens : providerCompletionTokens;
            int totalTokens = useEstimated ? estimatedTotalTokens : providerTotalTokens;
            if (useEstimated && providerLooksAbnormal)
            {
                Log.Warning($"[RimChat] Token usage from provider looks abnormal for {anomalyStreak} consecutive calls, fallback to estimate. provider=({providerPromptTokens},{providerCompletionTokens},{providerTotalTokens}), estimated=({estimatedPromptTokens},{estimatedCompletionTokens},{estimatedTotalTokens})");
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
                IsEstimated = useEstimated,
                Channel = usageChannel,
                RecordedAtUtc = DateTime.UtcNow
            };

            lock (lockObject)
            {
                latestDialogueTokenUsage = snapshot;
            }
        }

        private int UpdateProviderUsageAnomalyStreak(bool hasUsage, bool providerLooksAbnormal)
        {
            lock (lockObject)
            {
                if (!hasUsage)
                {
                    providerUsageAnomalyStreak = 0;
                    return providerUsageAnomalyStreak;
                }

                providerUsageAnomalyStreak = providerLooksAbnormal
                    ? providerUsageAnomalyStreak + 1
                    : 0;
                return providerUsageAnomalyStreak;
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

            string usageScope = json;
            if (TryExtractUsageObject(json, out string usageObject))
            {
                usageScope = usageObject;
            }

            bool usageFound = TryExtractUsageCore(usageScope, out promptTokens, out completionTokens, out totalTokens);
            if (usageFound)
            {
                return true;
            }

            if (!ReferenceEquals(usageScope, json))
            {
                return TryExtractUsageCore(json, out promptTokens, out completionTokens, out totalTokens);
            }

            return false;
        }

        private static bool TryExtractUsageCore(string source, out int promptTokens, out int completionTokens, out int totalTokens)
        {
            promptTokens = 0;
            completionTokens = 0;
            totalTokens = 0;

            bool promptOk = TryExtractIntByRegexes(PromptTokensRegexes, source, out promptTokens);
            bool completionOk = TryExtractIntByRegexes(CompletionTokensRegexes, source, out completionTokens);
            bool totalOk = TryExtractIntByRegexes(TotalTokensRegexes, source, out totalTokens);
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

        private static bool TryExtractUsageObject(string json, out string usageObject)
        {
            usageObject = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            int usageKeyIndex = json.IndexOf("\"usage\"", StringComparison.OrdinalIgnoreCase);
            if (usageKeyIndex < 0)
            {
                return false;
            }

            int colonIndex = json.IndexOf(':', usageKeyIndex);
            if (colonIndex < 0 || colonIndex + 1 >= json.Length)
            {
                return false;
            }

            int objectStart = colonIndex + 1;
            while (objectStart < json.Length && char.IsWhiteSpace(json[objectStart]))
            {
                objectStart++;
            }

            if (objectStart >= json.Length || json[objectStart] != '{')
            {
                return false;
            }

            int objectEnd = FindMatchingClosingBrace(json, objectStart);
            if (objectEnd <= objectStart)
            {
                return false;
            }

            usageObject = json.Substring(objectStart, objectEnd - objectStart + 1);
            return usageObject.Length > 2;
        }

        private static int FindMatchingClosingBrace(string source, int startBraceIndex)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = startBraceIndex; i < source.Length; i++)
            {
                char c = source[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    if (depth < 0)
                    {
                        return -1;
                    }
                }
            }

            return -1;
        }

        private static bool ShouldUseEstimatedUsage(
            int providerPromptTokens,
            int providerCompletionTokens,
            int providerTotalTokens,
            int estimatedPromptTokens,
            int estimatedCompletionTokens,
            int estimatedTotalTokens)
        {
            if (providerTotalTokens <= 0)
            {
                return true;
            }

            if (providerCompletionTokens > providerTotalTokens)
            {
                return true;
            }

            if (providerPromptTokens > 0 && providerCompletionTokens > 0)
            {
                int providerCombined = providerPromptTokens + providerCompletionTokens;
                int mismatchTolerance = Math.Max(64, (int)(providerTotalTokens * 0.4f));
                if (Math.Abs(providerCombined - providerTotalTokens) > mismatchTolerance)
                {
                    return true;
                }
            }

            if (estimatedTotalTokens >= 200)
            {
                // Keep provider usage when it is directionally reasonable.
                // Some providers apply cache/compression accounting, so strict ratio checks cause false fallbacks.
                float minReliable = estimatedTotalTokens * 0.08f;
                float maxReliable = estimatedTotalTokens * 8.0f;
                if (providerTotalTokens < minReliable || providerTotalTokens > maxReliable)
                {
                    return true;
                }
            }

            if (estimatedPromptTokens >= 120 && providerPromptTokens > 0 && providerPromptTokens < estimatedPromptTokens * 0.3f)
            {
                return true;
            }

            if (estimatedCompletionTokens >= 120 && providerCompletionTokens > 0 && providerCompletionTokens < estimatedCompletionTokens * 0.3f)
            {
                return true;
            }

            return false;
        }

        private static bool TryExtractIntByRegexes(Regex[] regexes, string source, out int value)
        {
            value = 0;
            if (regexes == null || regexes.Length == 0 || string.IsNullOrEmpty(source))
            {
                return false;
            }

            for (int i = 0; i < regexes.Length; i++)
            {
                if (TryExtractIntByRegex(regexes[i], source, out value))
                {
                    return true;
                }
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

        private ApiConfig GetFirstValidConfig()
        {
            if (RimChatMod.Instance == null || RimChatMod.Instance.InstanceSettings == null)
                return null;

            if (RimChatMod.Instance.InstanceSettings.UseCloudProviders)
            {
                if (RimChatMod.Instance.InstanceSettings.CloudConfigs == null || 
                    RimChatMod.Instance.InstanceSettings.CloudConfigs.Count == 0)
                    return null;

                foreach (var config in RimChatMod.Instance.InstanceSettings.CloudConfigs)
                {
                    if (config.IsValid())
                        return config;
                }
            }
            else
            {
                var localConfig = RimChatMod.Instance.InstanceSettings.LocalConfig;
                if (localConfig != null && localConfig.IsValid())
                {
                    string localBaseUrl = localConfig.GetNormalizedBaseUrl();
                    // Player2 local: use Player2 provider so extra headers and no-model logic apply
                    bool isPlayer2Local = localConfig.IsPlayer2Local();
                    return new ApiConfig
                    {
                        IsEnabled = true,
                        Provider = isPlayer2Local ? AIProvider.Player2 : AIProvider.Custom,
                        BaseUrl = isPlayer2Local ? localBaseUrl.TrimEnd('/') + "/v1/chat/completions" : ApiConfig.EnsureChatCompletionsEndpoint(localBaseUrl),
                        ApiKey = "",
                        SelectedModel = isPlayer2Local ? "Default" : localConfig.ModelName
                    };
                }
            }

            return null;
        }

        private string BuildChatCompletionJson(string model, List<ChatMessageData> messages, ApiConfig config)
        {
            var sb = new StringBuilder();
            sb.Append("{");

            // Player2 does not accept a model field; it selects the model server-side
            if (config.Provider != AIProvider.Player2)
            {
                sb.Append($"\"model\":\"{EscapeJson(model)}\",");
            }

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

            // Thinking parameters from global settings — only emit when enabled.
            // Some providers (e.g. Gemini) reject unknown keys, even with type=disabled.
            RimChatSettings globalSettings = RimChatMod.Settings;
            bool thinkingEnabled = globalSettings?.ThinkingEnabled ?? false;
            if (thinkingEnabled)
            {
                string reasoningEffort = globalSettings?.ReasoningEffort ?? "medium";
                sb.Append($",\"thinking\":{{\"type\":\"enabled\"}}");
                if (!string.IsNullOrEmpty(reasoningEffort))
                {
                    sb.Append($",\"reasoning_effort\":\"{EscapeJson(reasoningEffort)}\"");
                }
            }
            sb.Append("}");

            return sb.ToString();
        }

        private string EscapeJson(string str)
        {
            return RimChat.Util.JsonEscapeHelper.EscapeString(str);
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
                    if (IsInFlightState(kvp.Value.State))
                    {
                        TryCancelRequestLockless(kvp.Key, "service_destroyed", "Service destroyed");
                    }
                }

                interactiveLocalRequestQueue.Clear();
                localRequestQueue.Clear();
                queuedLocalRequestIds.Clear();
                activeWebRequests.Clear();
                activeLocalRequestId = null;
            }
        }
    }
}


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

namespace RimChat.AI
{
    /// <summary>
    /// AI chat request state
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
        private const int MaxRpgContractRetryCount = 1;
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
                    CleanupRequestDebugRecordsLockless(DateTime.UtcNow);
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
            AIRequestDebugSource debugSource = AIRequestDebugSource.Other)
        {
            List<ChatMessageData> normalizedMessages = NormalizeRequestMessagesForProvider(messages, usageChannel);
            string requestId = Guid.NewGuid().ToString("N");
            int requestContextVersion;

            CleanupCompletedRequests();
            
            var result = new AIRequestResult
            {
                State = AIRequestState.Pending,
                StartTime = DateTime.Now,
                Progress = 0f
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
                requestContextVersion));
            
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

        public static void NotifyGameContextChanged(string reason)
        {
            _instance?.HandleGameContextChanged(reason);
        }

        /// <summary>/// 取消指定的request
 ///</summary>
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
                        RemoveLocalRequestLockless(requestId);
                        return true;
                    }
                }
            }
            return false;
        }

        public int CancelAllPendingRequests(string reason = "Request cancelled by context change")
        {
            int cancelled = 0;

            lock (lockObject)
            {
                foreach (var kvp in activeRequests)
                {
                    if (kvp.Value.State == AIRequestState.Pending || kvp.Value.State == AIRequestState.Processing)
                    {
                        kvp.Value.State = AIRequestState.Error;
                        kvp.Value.Error = reason;
                        kvp.Value.Duration = DateTime.Now - kvp.Value.StartTime;
                        RemoveLocalRequestLockless(kvp.Key);
                        cancelled++;
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
                    if (kvp.Value.State == AIRequestState.Completed || 
                        kvp.Value.State == AIRequestState.Error)
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
            int requestContextVersion)
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

            UpdateRequestState(requestId, AIRequestState.Processing);

            bool localSlotAcquired = false;
            if (isLocalModel)
            {
                EnqueueLocalRequest(requestId);
                while (!localSlotAcquired)
                {
                    if (!IsContextVersionCurrent(requestContextVersion))
                    {
                        RemoveLocalRequest(requestId);
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

                    if (TryGetRequestError(requestId, out string waitingError))
                    {
                        RemoveLocalRequest(requestId);
                        ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(waitingError));
                        debugStatus = ClassifyDebugStatusFromError(waitingError);
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

            List<ChatMessageData> attemptMessages = CloneMessages(messages);
            int attempt = 1;
            int local5xxRetryCount = 0;
            int localConnectionRetryCount = 0;
            int immersionRetryCount = 0;
            int textIntegrityRetryCount = 0;
            int rpgContractRetryCount = 0;
            string contractValidationStatus = "not_applicable";
            string contractFailureReason = string.Empty;
            try
            {
                while (true)
                {
                    string jsonBody;
                    try
                    {
                        jsonBody = BuildChatCompletionJson(model, attemptMessages);
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

                    var stopwatch = Stopwatch.StartNew();
                    using (var request = new UnityWebRequest(url, "POST"))
                    {
                        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "application/json");
                        string trimmedApiKey = apiKey?.Trim() ?? string.Empty;
                        if (!isLocalModel || !string.IsNullOrEmpty(trimmedApiKey))
                        {
                            request.SetRequestHeader("Authorization", $"Bearer {trimmedApiKey}");
                        }
                        request.timeout = isLocalModel ? LocalRequestTimeoutSeconds : CloudRequestTimeoutSeconds;

                        var operation = request.SendWebRequest();
                        float progress = 0f;

                        while (!operation.isDone)
                        {
                            progress = Mathf.Min(progress + 0.02f, 0.9f);
                            UpdateRequestProgress(requestId, progress);
                            ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onProgress?.Invoke(progress));
                            yield return new WaitForSeconds(0.1f);

                            if (!IsContextVersionCurrent(requestContextVersion))
                            {
                                request.Abort();
                                MarkRequestAsDroppedByContext(requestId);
                                debugStatus = AIRequestDebugStatus.Cancelled;
                                debugErrorText = "Request dropped due to game context change";
                                yield break;
                            }

                            lock (lockObject)
                            {
                                if (activeRequests.TryGetValue(requestId, out var result) &&
                                    result.State == AIRequestState.Error)
                                {
                                    request.Abort();
                                    ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(result.Error));
                                    debugStatus = ClassifyDebugStatusFromError(result.Error);
                                    debugErrorText = result.Error ?? string.Empty;
                                    yield break;
                                }
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

                        if (request.result == UnityWebRequest.Result.ConnectionError)
                        {
                            if (ShouldRetryLocalConnectionError(isLocalModel, request.error, localConnectionRetryCount))
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
                            UpdateRequestState(requestId, AIRequestState.Error, error: errorMsg);
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

                            UpdateRequestState(requestId, AIRequestState.Error, error: errorMsg);
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
                            UpdateRequestState(requestId, AIRequestState.Error, error: errorMsg);
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
                                UpdateRequestState(requestId, AIRequestState.Error, error: errorMsg);
                                ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(errorMsg));
                                debugStatus = AIRequestDebugStatus.Error;
                                debugResponseText = responseText ?? string.Empty;
                                debugErrorText = errorMsg ?? string.Empty;
                                yield break;
                            }

                            string parsedResponse = ParseResponse(responseText);
                            if (string.IsNullOrEmpty(parsedResponse))
                            {
                                string errorMsg = "RimChat_ErrorParseResponse".Translate();
                                UpdateRequestState(requestId, AIRequestState.Error, error: errorMsg);
                                ExecuteRequestActionOnMainThread(requestId, requestContextVersion, () => onError?.Invoke(errorMsg));
                                debugStatus = AIRequestDebugStatus.Error;
                                debugResponseText = responseText ?? string.Empty;
                                debugErrorText = errorMsg ?? string.Empty;
                                yield break;
                            }

                            if (ShouldGuardImmersion(usageChannel))
                            {
                                ImmersionGuardResult guardResult = ImmersionOutputGuard.ValidateVisibleDialogue(parsedResponse);
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
                                    parsedResponse = ImmersionOutputGuard.BuildLocalFallbackDialogue(usageChannel);
                                    Log.Warning($"[RimChat] Immersion guard fallback used after retry: reason={ImmersionOutputGuard.BuildViolationTag(guardResult.ViolationReason)}");
                                }
                                else
                                {
                                    parsedResponse = guardResult.VisibleDialogue + guardResult.TrailingActionsJson;
                                }
                            }

                            if (ShouldGuardImmersion(usageChannel))
                            {
                                TextIntegrityCheckResult integrityResult = TextIntegrityGuard.ValidateVisibleDialogue(parsedResponse);
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
                                    parsedResponse = ImmersionOutputGuard.BuildLocalFallbackDialogue(usageChannel);
                                    Log.Warning($"[RimChat] Text integrity guard fallback used after retry: reason={integrityResult.ReasonTag}");
                                }
                                else
                                {
                                    parsedResponse = integrityResult.VisibleDialogue + integrityResult.TrailingActionsJson;
                                }
                            }

                            if (usageChannel == DialogueUsageChannel.Rpg)
                            {
                                RpgResponseContractCheckResult contractResult = RpgResponseContractGuard.Validate(parsedResponse);
                                if (!contractResult.IsValid && rpgContractRetryCount < MaxRpgContractRetryCount)
                                {
                                    rpgContractRetryCount++;
                                    contractValidationStatus = "retry";
                                    contractFailureReason = RpgResponseContractGuard.BuildViolationTag(contractResult.Violation);
                                    attemptMessages = AppendRpgContractRetryMessage(attemptMessages, contractResult);
                                    Log.Warning($"[RimChat] RPG contract guard requested retry: reason={contractFailureReason}");
                                    attempt++;
                                    continue;
                                }

                                if (!contractResult.IsValid)
                                {
                                    contractValidationStatus = "fallback_after_retry";
                                    contractFailureReason = RpgResponseContractGuard.BuildViolationTag(contractResult.Violation);
                                    parsedResponse = ImmersionOutputGuard.BuildLocalFallbackDialogue(DialogueUsageChannel.Rpg);
                                    Log.Warning($"[RimChat] RPG contract guard fallback used after retry: reason={contractFailureReason}");
                                }
                                else
                                {
                                    contractValidationStatus = rpgContractRetryCount > 0 ? "pass_after_retry" : "pass";
                                    contractFailureReason = string.Empty;
                                    parsedResponse = contractResult.VisibleDialogue + contractResult.TrailingActionsJson;
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
                        UpdateRequestState(requestId, AIRequestState.Error, error: fallbackError);
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
                        rpgContractRetryCount,
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

                return result.ContextVersion == expectedContextVersion;
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
            UpdateRequestState(requestId, AIRequestState.Error, error: "Request dropped due to game context change");
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
                    if (kvp.Value.State == AIRequestState.Pending || kvp.Value.State == AIRequestState.Processing)
                    {
                        kvp.Value.State = AIRequestState.Error;
                        kvp.Value.Error = "Request cancelled due to save/game context change";
                        kvp.Value.Duration = DateTime.Now - kvp.Value.StartTime;
                        RemoveLocalRequestLockless(kvp.Key);
                        cancelledCount++;
                    }
                }

                mainThreadActions.Clear();
                localRequestQueue.Clear();
                queuedLocalRequestIds.Clear();
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
                    content = "Strict RPG output contract: write natural dialogue as plain text. Only if gameplay effects are needed, append exactly one raw JSON object in the form {\"actions\":[...]} after the dialogue. Never wrap the dialogue inside JSON fields like \"dialogue\", \"response\", or \"content\". Inside each action object, use the key \"action\" only; never use legacy keys like \"name\" or nested \"params\" wrappers. Never use legacy top-level formats like {\"action\":\"...\"}, {\"content\":\"...\"}, or {\"text\":\"...\"}. Hard immersion rules: visible dialogue must start directly in-character and must not begin with parenthetical notes/metadata (for example \"(重复问候...)\" or \"（状态说明...）\"); never expose mechanic terms such as api_limits, blocked actions, goodwill, threshold, cooldown, API, system prompt, token, requestId; never output status-panel numeric lines like key:123 for system state."
                });
            }
            else if (usageChannel == DialogueUsageChannel.Diplomacy)
            {
                fallback.Add(new ChatMessageData
                {
                    role = "user",
                    content = "Strict diplomacy output contract: write natural dialogue as plain text. Only if gameplay effects are needed, append exactly one raw JSON object in the form {\"actions\":[{\"action\":\"snake_case_action\",\"parameters\":{...}}]} after the dialogue. Never wrap dialogue inside JSON fields like \"response\", \"dialogue\", or \"content\". Never use legacy single-action formats like {\"action\":\"...\",\"parameters\":{...},\"response\":\"...\"}. Hard immersion rules: visible dialogue must start directly in-character and must not begin with parenthetical notes/metadata (for example \"(重复问候...)\" or \"（状态说明...）\"); never expose mechanic terms such as api_limits, blocked actions, goodwill, threshold, cooldown, API, system prompt, token, requestId; never output status-panel numeric lines like key:123 for system state."
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

        private static List<ChatMessageData> AppendImmersionRetryMessage(
            List<ChatMessageData> messages,
            DialogueUsageChannel usageChannel,
            ImmersionGuardResult guardResult)
        {
            List<ChatMessageData> updated = CloneMessages(messages);
            string reasonTag = ImmersionOutputGuard.BuildViolationTag(guardResult?.ViolationReason ?? ImmersionViolationReason.None);
            string snippet = guardResult?.ViolationSnippet ?? string.Empty;
            string hint = usageChannel == DialogueUsageChannel.Rpg
                ? "Rewrite only visible NPC dialogue. Keep roleplay immersion."
                : "Rewrite only visible faction dialogue. Keep in-character immersion.";
            updated.Add(new ChatMessageData
            {
                role = "user",
                content = $"IMMERSION_VIOLATION={reasonTag}; snippet={snippet}. {hint} Output visible in-character dialogue only; do not prepend explanations, notes, or parenthetical metadata. Do not expose system state or numeric status panel lines. Keep optional trailing {{\"actions\":[...]}} JSON unchanged when needed."
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
                content = $"TEXT_INTEGRITY_VIOLATION={reasonTag}. {hint} Remove garbled fragments and mojibake. Output visible in-character dialogue only; do not add notes or headers. Keep optional trailing {{\"actions\":[...]}} JSON unchanged when needed."
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
                content = $"RPG_CONTRACT_VIOLATION={reasonTag}. Rewrite as one single-line in-character dialogue sentence. If gameplay effects are needed, append exactly one trailing {{\"actions\":[...]}} JSON object after dialogue; otherwise omit actions. Do not use placeholder values (OptionalDef/OptionalReason/amount:0)."
            });
            return NormalizeRequestMessagesForProvider(updated, DialogueUsageChannel.Rpg);
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

        private string ParseResponse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                if (AIJsonContentExtractor.IsErrorPayload(json))
                    return null;

                if (AIJsonContentExtractor.TryExtractPrimaryText(json, out string content))
                {
                    return content;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to parse AI response: {ex.Message}");
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
                    return new ApiConfig
                    {
                        IsEnabled = true,
                        Provider = AIProvider.Custom,
                        BaseUrl = ApiConfig.EnsureChatCompletionsEndpoint(localBaseUrl),
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
                        RemoveLocalRequestLockless(kvp.Key);
                    }
                }

                localRequestQueue.Clear();
                queuedLocalRequestIds.Clear();
                activeLocalRequestId = null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using RimChat.Util;
using UnityEngine.Networking;
using Verse;

namespace RimChat.AI
{
    /// <summary>
    /// Dependencies: AIChatServiceAsync shared request state and UnityWebRequest abort lifecycle.
    /// Responsibility: track local queue priority/timeout metadata and suppress callbacks for cancelled requests.
    /// </summary>
    public enum AIRequestPriority
    {
        Background = 0,
        Interactive = 1
    }

    public partial class AIChatServiceAsync
    {
        private const float LocalRequestQueueTimeoutSeconds = 60f;

        private readonly Queue<string> interactiveLocalRequestQueue = new Queue<string>();
        private readonly Dictionary<string, UnityWebRequest> activeWebRequests =
            new Dictionary<string, UnityWebRequest>(StringComparer.Ordinal);

        private static AIRequestPriority ResolveRequestPriority(AIRequestDebugSource source)
        {
            switch (source)
            {
                case AIRequestDebugSource.DiplomacyDialogue:
                case AIRequestDebugSource.RpgDialogue:
                case AIRequestDebugSource.StrategySuggestion:
                case AIRequestDebugSource.SendImage:
                case AIRequestDebugSource.ApiUsabilityTest:
                    return AIRequestPriority.Interactive;
                default:
                    return AIRequestPriority.Background;
            }
        }

        private static bool IsInFlightState(AIRequestState state)
        {
            return state == AIRequestState.Pending ||
                   state == AIRequestState.Queued ||
                   state == AIRequestState.Processing;
        }

        private static bool IsTerminalState(AIRequestState state)
        {
            return state == AIRequestState.Completed ||
                   state == AIRequestState.Error ||
                   state == AIRequestState.Cancelled;
        }

        private static bool IsExternallyTerminatedState(AIRequestState state)
        {
            return state == AIRequestState.Error ||
                   state == AIRequestState.Cancelled;
        }

        private void MarkRequestQueuedLockless(string requestId)
        {
            if (!activeRequests.TryGetValue(requestId, out AIRequestResult result))
            {
                return;
            }

            result.State = AIRequestState.Queued;
            if (result.EnqueuedAtUtc == DateTime.MinValue)
            {
                result.EnqueuedAtUtc = DateTime.UtcNow;
            }

            result.QueueDeadlineUtc = DateTime.UtcNow.AddSeconds(LocalRequestQueueTimeoutSeconds);
            result.StartedProcessingAtUtc = DateTime.MinValue;
            result.Progress = 0f;
            result.CancelReason = string.Empty;
            result.FailureReason = string.Empty;
            result.AllowCallbacks = true;
        }

        private void MarkRequestProcessingStartedLockless(string requestId)
        {
            if (!activeRequests.TryGetValue(requestId, out AIRequestResult result))
            {
                return;
            }

            result.State = AIRequestState.Processing;
            result.StartedProcessingAtUtc = DateTime.UtcNow;
            result.QueueDeadlineUtc = DateTime.MinValue;
            result.QueuePosition = 0;
            result.CancelReason = string.Empty;
        }

        private void RegisterActiveWebRequest(string requestId, UnityWebRequest request)
        {
            lock (lockObject)
            {
                if (string.IsNullOrWhiteSpace(requestId) || request == null)
                {
                    return;
                }

                activeWebRequests[requestId] = request;
            }
        }

        private void UnregisterActiveWebRequest(string requestId, UnityWebRequest request = null)
        {
            lock (lockObject)
            {
                if (string.IsNullOrWhiteSpace(requestId))
                {
                    return;
                }

                if (!activeWebRequests.TryGetValue(requestId, out UnityWebRequest current))
                {
                    return;
                }

                if (request != null && !ReferenceEquals(current, request))
                {
                    return;
                }

                activeWebRequests.Remove(requestId);
            }
        }

        private void AbortActiveWebRequestLockless(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            if (!activeWebRequests.TryGetValue(requestId, out UnityWebRequest request) || request == null)
            {
                return;
            }

            try
            {
                request.Abort();
            }
            catch (Exception ex)
            {
                DebugLogger.LogInternal("AIChatServiceAsync", $"Abort request failed: requestId={requestId}, error={ex.Message}");
            }

            activeWebRequests.Remove(requestId);
        }

        private void SetRequestFailureLockless(string requestId, string error, string failureReason)
        {
            if (!activeRequests.TryGetValue(requestId, out AIRequestResult result))
            {
                return;
            }

            result.State = AIRequestState.Error;
            result.Error = error;
            result.FailureReason = failureReason ?? string.Empty;
            result.CancelReason = string.Empty;
            result.AllowCallbacks = true;
            result.QueueDeadlineUtc = DateTime.MinValue;
            result.QueuePosition = 0;
            result.Duration = DateTime.Now - result.StartTime;
        }

        private bool TryCancelRequestLockless(string requestId, string cancelReason, string error = null)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return false;
            }

            if (!activeRequests.TryGetValue(requestId, out AIRequestResult result) || !IsInFlightState(result.State))
            {
                return false;
            }

            result.State = AIRequestState.Cancelled;
            result.Error = error ?? string.Empty;
            result.CancelReason = cancelReason ?? "cancelled";
            result.FailureReason = result.CancelReason;
            result.AllowCallbacks = false;
            result.QueueDeadlineUtc = DateTime.MinValue;
            result.QueuePosition = 0;
            result.Duration = DateTime.Now - result.StartTime;
            RemoveLocalRequestLockless(requestId);
            AbortActiveWebRequestLockless(requestId);
            return true;
        }

        private void RefreshQueuedRequestPositionsLockless()
        {
            foreach (AIRequestResult result in activeRequests.Values)
            {
                if (result.State == AIRequestState.Queued)
                {
                    result.QueuePosition = 0;
                }
            }

            int queuePosition = 1;
            RefreshQueuedRequestPositionsForQueueLockless(interactiveLocalRequestQueue, ref queuePosition);
            RefreshQueuedRequestPositionsForQueueLockless(localRequestQueue, ref queuePosition);
        }

        private void RefreshQueuedRequestPositionsForQueueLockless(Queue<string> queue, ref int queuePosition)
        {
            if (queue == null || queue.Count == 0)
            {
                return;
            }

            foreach (string requestId in queue)
            {
                if (!queuedLocalRequestIds.Contains(requestId))
                {
                    continue;
                }

                if (!activeRequests.TryGetValue(requestId, out AIRequestResult result))
                {
                    continue;
                }

                if (!IsInFlightState(result.State))
                {
                    continue;
                }

                result.State = AIRequestState.Queued;
                result.QueuePosition = queuePosition++;
            }
        }

        private bool TryTimeoutQueuedRequest(string requestId)
        {
            lock (lockObject)
            {
                if (!activeRequests.TryGetValue(requestId, out AIRequestResult result))
                {
                    return false;
                }

                if (result.State != AIRequestState.Queued || result.QueueDeadlineUtc == DateTime.MinValue)
                {
                    return false;
                }

                if (DateTime.UtcNow < result.QueueDeadlineUtc)
                {
                    return false;
                }

                SetRequestFailureLockless(requestId, "RimChat_ErrorQueueTimeout".Translate().ToString(), "queue_timeout");
                RemoveLocalRequestLockless(requestId);
                return true;
            }
        }

        private bool TryGetTerminalRequestDisposition(
            string requestId,
            out AIRequestState terminalState,
            out string message,
            out bool allowCallback)
        {
            lock (lockObject)
            {
                terminalState = AIRequestState.Idle;
                message = null;
                allowCallback = false;

                if (!activeRequests.TryGetValue(requestId, out AIRequestResult result) ||
                    !IsExternallyTerminatedState(result.State))
                {
                    return false;
                }

                terminalState = result.State;
                message = string.IsNullOrWhiteSpace(result.Error)
                    ? result.CancelReason
                    : result.Error;
                allowCallback = result.AllowCallbacks && result.State == AIRequestState.Error;
                return true;
            }
        }
    }
}

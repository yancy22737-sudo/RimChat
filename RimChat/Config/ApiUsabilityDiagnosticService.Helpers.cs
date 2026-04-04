using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.AI;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: ApiUsabilityDiagnosticService core flow and localization key catalog.
    /// Responsibility: helper methods for result construction, classification, and payload normalization.
    /// </summary>
    internal static partial class ApiUsabilityDiagnosticService
    {
        private static ApiUsabilityDiagnosticResult BuildSuccess(
            ApiUsabilityStep step,
            long httpCode,
            string endpoint,
            DateTime startedAtUtc,
            List<ApiUsabilityStepResult> steps,
            string modelName,
            bool isCloud,
            string requestPayload,
            string responsePayload,
            string successDetail = "")
        {
            return new ApiUsabilityDiagnosticResult
            {
                IsSuccess = true,
                Step = step,
                ErrorCode = ApiUsabilityErrorCode.NONE,
                TechDetail = BuildTechDetail(step, endpoint, httpCode, successDetail),
                HttpCode = httpCode,
                EndpointUsed = endpoint ?? string.Empty,
                ElapsedMs = GetElapsedMilliseconds(startedAtUtc),
                Steps = steps.ToList(),
                PlayerHintKeys = new List<string>(),
                DebugRequestText = requestPayload ?? string.Empty,
                DebugResponseText = TruncateDebugPayload(responsePayload),
                ModelName = modelName ?? string.Empty,
                IsCloud = isCloud
            };
        }

        private static ApiUsabilityDiagnosticResult BuildFailureFromProbe(
            ApiUsabilityStep step,
            ApiUsabilityProbeResponse probe,
            string endpoint,
            DateTime startedAtUtc,
            List<ApiUsabilityStepResult> steps,
            string modelName,
            bool isCloud,
            string requestPayload,
            ApiUsabilityErrorCode? forceCode = null)
        {
            ApiUsabilityErrorCode code = forceCode ?? ClassifyErrorCode(probe.HttpCode, probe.Error, isCloud);
            return BuildFailure(
                step,
                code,
                probe.Error,
                probe.HttpCode,
                endpoint,
                startedAtUtc,
                steps,
                modelName,
                isCloud,
                requestPayload,
                probe.ResponseBody);
        }

        private static ApiUsabilityDiagnosticResult BuildFailure(
            ApiUsabilityStep step,
            ApiUsabilityErrorCode code,
            string details,
            long httpCode,
            string endpoint,
            DateTime startedAtUtc,
            List<ApiUsabilityStepResult> steps,
            string modelName,
            bool isCloud,
            string requestPayload,
            string responsePayload)
        {
            var failedStep = new ApiUsabilityStepResult
            {
                Step = step,
                Success = false,
                ErrorCode = code,
                TechDetail = BuildTechDetail(step, endpoint, httpCode, details),
                HttpCode = httpCode,
                EndpointUsed = endpoint ?? string.Empty,
                ElapsedMs = GetElapsedMilliseconds(startedAtUtc)
            };

            var allSteps = new List<ApiUsabilityStepResult>(steps.Count + 1);
            allSteps.AddRange(steps);
            allSteps.Add(failedStep);
            return new ApiUsabilityDiagnosticResult
            {
                IsSuccess = false,
                Step = step,
                ErrorCode = code,
                TechDetail = failedStep.TechDetail,
                HttpCode = httpCode,
                EndpointUsed = endpoint ?? string.Empty,
                ElapsedMs = failedStep.ElapsedMs,
                Steps = allSteps,
                PlayerHintKeys = GetHintKeys(code),
                DebugRequestText = requestPayload ?? string.Empty,
                DebugResponseText = TruncateDebugPayload(responsePayload),
                ModelName = modelName ?? string.Empty,
                IsCloud = isCloud
            };
        }

        private static ApiUsabilityStepResult BuildStepSuccess(ApiUsabilityStep step, string endpoint, DateTime startedAtUtc)
        {
            return new ApiUsabilityStepResult
            {
                Step = step,
                Success = true,
                ErrorCode = ApiUsabilityErrorCode.NONE,
                TechDetail = string.Empty,
                HttpCode = 200,
                EndpointUsed = endpoint ?? string.Empty,
                ElapsedMs = GetElapsedMilliseconds(startedAtUtc)
            };
        }

        private static void NotifyProgress(Action<ApiUsabilityProgress> onProgress, ApiUsabilityStep step, int current, int total)
        {
            onProgress?.Invoke(new ApiUsabilityProgress
            {
                Step = step,
                Current = current,
                Total = total
            });
        }

        private static ApiUsabilityErrorCode ClassifyErrorCode(long httpCode, string error, bool isCloud)
        {
            if (httpCode == 401 || httpCode == 403)
            {
                return ApiUsabilityErrorCode.AUTH_INVALID;
            }

            if (httpCode == 404)
            {
                return ApiUsabilityErrorCode.ENDPOINT_NOT_FOUND;
            }

            if (httpCode == 429)
            {
                return ApiUsabilityErrorCode.RATE_LIMIT;
            }

            string normalized = (error ?? string.Empty).ToLowerInvariant();
            if (normalized.Contains("timed out") || normalized.Contains("timeout"))
            {
                return ApiUsabilityErrorCode.TIMEOUT;
            }

            if (normalized.Contains("certificate") || normalized.Contains("ssl") || normalized.Contains("tls"))
            {
                return ApiUsabilityErrorCode.TLS_OR_CERT;
            }

            if (normalized.Contains("resolve host") || normalized.Contains("dns") || normalized.Contains("name resolution"))
            {
                return ApiUsabilityErrorCode.DNS_OR_NETWORK;
            }

            if (!isCloud && httpCode <= 0)
            {
                return ApiUsabilityErrorCode.LOCAL_SERVICE_DOWN;
            }

            return ApiUsabilityErrorCode.UNKNOWN;
        }

        private static List<string> GetHintKeys(ApiUsabilityErrorCode code)
        {
            return code switch
            {
                ApiUsabilityErrorCode.AUTH_INVALID => new List<string>
                {
                    "RimChat_UsabilityHint_AUTH_INVALID_1",
                    "RimChat_UsabilityHint_AUTH_INVALID_2"
                },
                ApiUsabilityErrorCode.ENDPOINT_NOT_FOUND => new List<string>
                {
                    "RimChat_UsabilityHint_ENDPOINT_NOT_FOUND_1",
                    "RimChat_UsabilityHint_ENDPOINT_NOT_FOUND_2"
                },
                ApiUsabilityErrorCode.MODEL_NOT_FOUND => new List<string>
                {
                    "RimChat_UsabilityHint_MODEL_NOT_FOUND_1",
                    "RimChat_UsabilityHint_MODEL_NOT_FOUND_2"
                },
                ApiUsabilityErrorCode.TIMEOUT => new List<string>
                {
                    "RimChat_UsabilityHint_TIMEOUT_1",
                    "RimChat_UsabilityHint_TIMEOUT_2"
                },
                ApiUsabilityErrorCode.RATE_LIMIT => new List<string>
                {
                    "RimChat_UsabilityHint_RATE_LIMIT_1",
                    "RimChat_UsabilityHint_RATE_LIMIT_2"
                },
                ApiUsabilityErrorCode.TLS_OR_CERT => new List<string>
                {
                    "RimChat_UsabilityHint_TLS_OR_CERT_1",
                    "RimChat_UsabilityHint_TLS_OR_CERT_2"
                },
                ApiUsabilityErrorCode.DNS_OR_NETWORK => new List<string>
                {
                    "RimChat_UsabilityHint_DNS_OR_NETWORK_1",
                    "RimChat_UsabilityHint_DNS_OR_NETWORK_2"
                },
                ApiUsabilityErrorCode.RESPONSE_SCHEMA_INVALID => new List<string>
                {
                    "RimChat_UsabilityHint_RESPONSE_SCHEMA_INVALID_1",
                    "RimChat_UsabilityHint_RESPONSE_SCHEMA_INVALID_2"
                },
                ApiUsabilityErrorCode.LOCAL_SERVICE_DOWN => new List<string>
                {
                    "RimChat_UsabilityHint_LOCAL_SERVICE_DOWN_1",
                    "RimChat_UsabilityHint_LOCAL_SERVICE_DOWN_2"
                },
                _ => new List<string>
                {
                    "RimChat_UsabilityHint_UNKNOWN_1",
                    "RimChat_UsabilityHint_UNKNOWN_2"
                }
            };
        }

        private static string BuildMissingModelDetail(string targetModel, List<string> discoveredModels)
        {
            string joined = discoveredModels == null || discoveredModels.Count == 0
                ? "none"
                : string.Join(", ", discoveredModels.Take(8));
            return $"target_model={targetModel}; discovered_models={joined}";
        }

        private static string BuildTechDetail(ApiUsabilityStep step, string endpoint, long httpCode, string details)
        {
            string safeDetail = string.IsNullOrWhiteSpace(details) ? "none" : details.Trim();
            return $"step={step}; endpoint={endpoint}; http={httpCode}; detail={safeDetail}";
        }

        private static long GetElapsedMilliseconds(DateTime startedAtUtc)
        {
            return Math.Max(0L, (long)(DateTime.UtcNow - startedAtUtc).TotalMilliseconds);
        }

        private static string BuildModelTagPrefix(string model)
        {
            int index = (model ?? string.Empty).IndexOf(':');
            return index <= 0 ? model : model.Substring(0, index);
        }

        private static string NormalizeGoogleModelName(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                return string.Empty;
            }

            string normalized = modelName.Trim();
            const string prefix = "models/";
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(prefix.Length);
            }

            normalized = normalized.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            return normalized;
        }

        private static string JoinUrl(string baseUrl, string path)
        {
            string left = ApiConfig.NormalizeUrl(baseUrl).TrimEnd('/');
            string right = (path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(left))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(right))
            {
                return left;
            }

            return right.StartsWith("/", StringComparison.Ordinal)
                ? left + right
                : left + "/" + right;
        }

        private static List<string> ExtractQuotedValues(string json, string fieldToken)
        {
            var values = new List<string>();
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldToken))
            {
                return values;
            }

            int index = 0;
            while ((index = json.IndexOf(fieldToken, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int colon = json.IndexOf(':', index + fieldToken.Length);
                if (colon < 0)
                {
                    break;
                }

                int openQuote = json.IndexOf('"', colon + 1);
                if (openQuote < 0)
                {
                    break;
                }

                int closeQuote = FindClosingQuote(json, openQuote + 1);
                if (closeQuote < 0)
                {
                    break;
                }

                string value = json.Substring(openQuote + 1, closeQuote - openQuote - 1);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }

                index = closeQuote + 1;
            }

            return values;
        }

        private static int FindClosingQuote(string text, int startIndex)
        {
            bool escaped = false;
            for (int i = startIndex; i < text.Length; i++)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                char c = text[i];
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    return i;
                }
            }

            return -1;
        }

        private static string EscapeJsonString(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static string TruncateDebugPayload(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return string.Empty;
            }

            const int maxLength = 2400;
            return payload.Length <= maxLength
                ? payload
                : payload.Substring(0, maxLength) + "...";
        }

        private struct ApiUsabilityProbeRequest
        {
            public string Url;
            public string Method;
            public string Body;
            public AIProvider Provider;
            public string ApiKey;
            public int TimeoutSeconds;
        }

        private struct ApiUsabilityProbeResponse
        {
            public long HttpCode;
            public string Error;
            public string ResponseBody;

            public bool IsHttpSuccess => HttpCode >= 200 && HttpCode < 300;
        }

        private struct ApiUsabilityCloudRuntime
        {
            public string ModelsEndpoint;
            public string ChatEndpoint;
            public string Details;

            public bool IsValid =>
                !string.IsNullOrWhiteSpace(ModelsEndpoint) &&
                !string.IsNullOrWhiteSpace(ChatEndpoint);
        }

        private struct ApiUsabilityLocalServiceProbe
        {
            public bool IsSuccess;
            public string EndpointUsed;
            public ApiUsabilityLocalServiceType ServiceType;
            public ApiUsabilityProbeResponse Response;
        }

        private enum ApiUsabilityLocalServiceType
        {
            Unknown = 0,
            OpenAiCompatible = 1,
            Ollama = 2
        }
    }
}

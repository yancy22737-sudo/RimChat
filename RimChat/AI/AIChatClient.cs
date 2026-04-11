using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using RimChat.Config;
using RimChat.Util;
using RimChat.Core;

namespace RimChat.AI
{
    public class ChatMessage
    {
        public string role;
        public string content;
    }

    [Obsolete("Use AIChatServiceAsync instead. This client uses TaskCompletionSource + LongEventHandler which can deadlock and blocks background threads with Thread.Sleep.")]
    public sealed class AIChatClientResponse
    {
        public bool Success { get; set; }
        public string ParsedContent { get; set; }
        public string RawResponse { get; set; }
        public string ErrorText { get; set; }
        public string FailureReason { get; set; }
        public long HttpStatusCode { get; set; }
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
        public bool IsEstimatedTokens { get; set; } = true;
    }

    [Obsolete("Use AIChatServiceAsync instead. This client uses TaskCompletionSource + LongEventHandler which can deadlock and blocks background threads with Thread.Sleep.")]
    public class AIChatClient
    {
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

        private static AIChatClient _instance;
        public static AIChatClient Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new AIChatClient();
                return _instance;
            }
        }

        private AIChatClient() { }

        public async Task<string> SendChatRequestAsync(List<ChatMessage> messages, Action<float> onProgress = null)
        {
            AIChatClientResponse response = await SendChatRequestDetailedAsync(messages, onProgress);
            if (response == null || !response.Success)
            {
                return null;
            }

            return response.ParsedContent;
        }

        public async Task<AIChatClientResponse> SendChatRequestDetailedAsync(List<ChatMessage> messages, Action<float> onProgress = null)
        {
            var config = GetFirstValidConfig();
            if (config == null)
            {
                Log.Error("[RimChat] No valid AI configuration found.");
                return new AIChatClientResponse
                {
                    Success = false,
                    ErrorText = "No valid AI configuration found.",
                    FailureReason = "no_config"
                };
            }

            string url = config.GetEffectiveEndpoint();
            string apiKey = config.ApiKey;
            string model = config.GetEffectiveModelName();
            AIProvider provider = config.Provider;

            string jsonBody = BuildChatCompletionJson(model, messages);

            var tcs = new TaskCompletionSource<AIChatClientResponse>();
            LongEventHandler.QueueLongEvent(() =>
            {
                try
                {
                    AIChatClientResponse result = SendRequestDetailedSync(url, apiKey, jsonBody, onProgress, provider);
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimChat] AI request failed: {ex.Message}");
                    tcs.SetResult(new AIChatClientResponse
                    {
                        Success = false,
                        ErrorText = ex.Message ?? "Unknown request exception.",
                        FailureReason = "request_exception"
                    });
                }
            }, "RimChat_SendingAIRequest".Translate(), false, null);

            return await tcs.Task;
        }

        private AIChatClientResponse SendRequestDetailedSync(string url, string apiKey, string jsonBody, Action<float> onProgress, AIProvider provider)
        {
            bool isLocalModel = RimChatMod.Instance == null || !(RimChatMod.Instance.InstanceSettings?.UseCloudProviders ?? false);

            using (var request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                if (!isLocalModel || !string.IsNullOrWhiteSpace(apiKey))
                {
                    if (provider == AIProvider.Google)
                    {
                        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                    }
                    else
                    {
                        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                    }
                }

                // Add provider-specific extra headers (e.g. player2-game-key for Player2)
                var extraHeaders = provider.GetExtraHeaders();
                if (extraHeaders != null)
                {
                    foreach (var header in extraHeaders)
                    {
                        request.SetRequestHeader(header.Key, header.Value);
                    }
                }

                request.timeout = 60;

                var operation = request.SendWebRequest();
                float progress = 0f;

                while (!operation.isDone)
                {
                    System.Threading.Thread.Sleep(100);
                    progress = Mathf.Min(progress + 0.05f, 0.9f);
                    onProgress?.Invoke(progress);
                }

                onProgress?.Invoke(1f);

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string rawText = request.downloadHandler?.text ?? string.Empty;
                    string parsed = ParseResponse(rawText);
                    TryResolveTokenUsage(rawText, out int promptTokens, out int completionTokens, out int totalTokens, out bool estimatedTokens);
                    if (string.IsNullOrWhiteSpace(parsed))
                    {
                        return new AIChatClientResponse
                        {
                            Success = false,
                            RawResponse = rawText,
                            ErrorText = "Failed to parse AI response.",
                            FailureReason = "parse_error",
                            HttpStatusCode = request.responseCode,
                            PromptTokens = promptTokens,
                            CompletionTokens = completionTokens,
                            TotalTokens = totalTokens,
                            IsEstimatedTokens = estimatedTokens
                        };
                    }

                    return new AIChatClientResponse
                    {
                        Success = true,
                        ParsedContent = parsed,
                        RawResponse = rawText,
                        ErrorText = string.Empty,
                        FailureReason = string.Empty,
                        HttpStatusCode = request.responseCode,
                        PromptTokens = promptTokens,
                        CompletionTokens = completionTokens,
                        TotalTokens = totalTokens,
                        IsEstimatedTokens = estimatedTokens
                    };
                }

                string responseBody = request.downloadHandler?.text ?? string.Empty;
                string failureReason = ResolveFailureReason(request);
                string errorText = BuildRequestErrorText(request, responseBody);
                Log.Error($"[RimChat] AI API error: {request.responseCode} - {request.error}");
                return new AIChatClientResponse
                {
                    Success = false,
                    ParsedContent = string.Empty,
                    RawResponse = responseBody,
                    ErrorText = errorText,
                    FailureReason = failureReason,
                    HttpStatusCode = request.responseCode,
                    PromptTokens = 0,
                    CompletionTokens = 0,
                    TotalTokens = 0,
                    IsEstimatedTokens = true
                };
            }
        }

        private string ParseResponse(string json)
        {
            try
            {
                int contentIndex = json.IndexOf("\"content\":\"");
                if (contentIndex >= 0)
                {
                    contentIndex += "\"content\":\"".Length;
                    int endIndex = json.IndexOf("\"", contentIndex);
                    if (endIndex >= 0)
                    {
                        string content = json.Substring(contentIndex, endIndex - contentIndex);
                        return UnescapeJson(content);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to parse AI response: {ex.Message}");
            }
            return null;
        }

        private static string ResolveFailureReason(UnityWebRequest request)
        {
            if (request == null)
            {
                return "request_error";
            }

            switch (request.result)
            {
                case UnityWebRequest.Result.ConnectionError:
                    return LooksLikeTimeout(request.error) ? "timeout" : "connection_error";
                case UnityWebRequest.Result.ProtocolError:
                    return $"http_{request.responseCode}";
                case UnityWebRequest.Result.DataProcessingError:
                    return "data_processing_error";
                default:
                    return "request_error";
            }
        }

        private static string BuildRequestErrorText(UnityWebRequest request, string responseBody)
        {
            if (request == null)
            {
                return "Unknown request error.";
            }

            string responsePreview = string.IsNullOrWhiteSpace(responseBody)
                ? string.Empty
                : $" body={responseBody.Trim()}";
            return $"HTTP {request.responseCode}: {request.error}{responsePreview}".Trim();
        }

        private static bool LooksLikeTimeout(string error)
        {
            string normalized = (error ?? string.Empty).ToLowerInvariant();
            return normalized.Contains("timeout") || normalized.Contains("timed out");
        }

        private static void TryResolveTokenUsage(
            string rawText,
            out int promptTokens,
            out int completionTokens,
            out int totalTokens,
            out bool isEstimated)
        {
            promptTokens = TryExtractInt(rawText, PromptTokensRegexes);
            completionTokens = TryExtractInt(rawText, CompletionTokensRegexes);
            totalTokens = TryExtractInt(rawText, TotalTokensRegexes);

            if (totalTokens == 0)
            {
                totalTokens = Math.Max(0, promptTokens + completionTokens);
            }

            isEstimated = promptTokens <= 0 || completionTokens <= 0 || totalTokens <= 0;
        }

        private static int TryExtractInt(string rawText, Regex[] regexes)
        {
            if (string.IsNullOrWhiteSpace(rawText) || regexes == null)
            {
                return 0;
            }

            for (int i = 0; i < regexes.Length; i++)
            {
                Match match = regexes[i].Match(rawText);
                if (!match.Success)
                {
                    continue;
                }

                if (int.TryParse(match.Groups[1].Value, out int parsed))
                {
                    return Math.Max(0, parsed);
                }
            }

            return 0;
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
            if (RimChatMod.Instance == null || RimChatMod.Instance.InstanceSettings == null)
                return null;

            if (RimChatMod.Instance.InstanceSettings.UseCloudProviders)
            {
                foreach (var config in RimChatMod.Instance.InstanceSettings.CloudConfigs)
                {
                    if (config.IsValid())
                        return config;
                }
            }
            else
            {
                if (RimChatMod.Instance.InstanceSettings.LocalConfig != null && RimChatMod.Instance.InstanceSettings.LocalConfig.IsValid())
                {
                    string localBaseUrl = RimChatMod.Instance.InstanceSettings.LocalConfig.GetNormalizedBaseUrl();
                    bool isPlayer2Local = RimChatMod.Instance.InstanceSettings.LocalConfig.IsPlayer2Local();
                    return new ApiConfig
                    {
                        Provider = isPlayer2Local ? AIProvider.Player2 : AIProvider.Custom,
                        BaseUrl = isPlayer2Local ? localBaseUrl.TrimEnd('/') + "/v1/chat/completions" : ApiConfig.EnsureChatCompletionsEndpoint(localBaseUrl),
                        SelectedModel = isPlayer2Local ? "Default" : "Custom",
                        CustomModelName = isPlayer2Local ? "" : RimChatMod.Instance.InstanceSettings.LocalConfig.ModelName,
                        ApiKey = "",
                        IsEnabled = true
                    };
                }
            }
            return null;
        }

        private string BuildChatCompletionJson(string model, List<ChatMessage> messages)
        {
            var sb = new System.Text.StringBuilder();
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
    }
}

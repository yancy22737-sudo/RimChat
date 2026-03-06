using System;
using System.Text;
using RimWorld;
using Verse;
using RimDiplomacy.AI;
using RimDiplomacy.Core;
using RimDiplomacy.Config;

namespace RimDiplomacy.Util
{
    public static class DebugLogger
    {
        private const string Prefix = "[RimDiplomacy]";

        public static bool IsDebugEnabled => (RimDiplomacyMod.Instance?.InstanceSettings)?.EnableDebugLogging ?? false;
        public static bool LogRequests => IsDebugEnabled && ((RimDiplomacyMod.Instance?.InstanceSettings)?.LogAIRequests ?? false);
        public static bool LogResponses => IsDebugEnabled && ((RimDiplomacyMod.Instance?.InstanceSettings)?.LogAIResponses ?? false);
        public static bool LogInternals => IsDebugEnabled && ((RimDiplomacyMod.Instance?.InstanceSettings)?.LogInternals ?? false);
        public static bool LogFullMessagesEnabled => IsDebugEnabled && ((RimDiplomacyMod.Instance?.InstanceSettings)?.LogFullMessages ?? false);

        public static void Info(string message)
        {
            Log.Message($"{Prefix} {message}");
        }

        public static void Warning(string message)
        {
            Log.Warning($"{Prefix} {message}");
        }

        public static void Error(string message)
        {
            Log.Error($"{Prefix} {message}");
        }

        public static void Debug(string message)
        {
            if (IsDebugEnabled)
            {
                Log.Message($"{Prefix} [DEBUG] {message}");
            }
        }

        public static void LogAIRequest(string url, string model, string jsonBody, bool isLocalModel)
        {
            if (!LogRequests) return;

            var sb = new StringBuilder();
            sb.AppendLine("========== AI REQUEST ==========");
            sb.AppendLine($"URL: {url}");
            sb.AppendLine($"Model: {model}");
            sb.AppendLine($"IsLocal: {isLocalModel}");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine("----- Request Body -----");
            sb.AppendLine(FormatJson(jsonBody));
            sb.AppendLine("================================");

            Log.Message($"{Prefix}\n{sb}");
        }

        public static void LogAIResponse(string response, long responseCode, long elapsedMs)
        {
            if (!LogResponses) return;

            var sb = new StringBuilder();
            sb.AppendLine("========== AI RESPONSE ==========");
            sb.AppendLine($"Response Code: {responseCode}");
            sb.AppendLine($"Elapsed Time: {elapsedMs}ms");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine("----- Response Body -----");
            sb.AppendLine(FormatJson(response));
            sb.AppendLine("=================================");

            Log.Message($"{Prefix}\n{sb}");
        }

        public static void LogAIError(string error, string context = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("========== AI ERROR ==========");
            if (!string.IsNullOrEmpty(context))
            {
                sb.AppendLine($"Context: {context}");
            }
            sb.AppendLine($"Error: {error}");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine("==============================");

            Log.Error($"{Prefix}\n{sb}");
        }

        public static void LogInternal(string category, string message)
        {
            if (!LogInternals) return;
            Log.Message($"{Prefix} [INTERNAL] [{category}] {message}");
        }

        public static void LogConfig(string url, string model, bool isLocalModel, string apiKeyPreview)
        {
            if (!IsDebugEnabled) return;

            var sb = new StringBuilder();
            sb.AppendLine("========== CONFIG INFO ==========");
            sb.AppendLine($"URL: {url}");
            sb.AppendLine($"Model: {model}");
            sb.AppendLine($"IsLocalModel: {isLocalModel}");
            sb.AppendLine($"API Key: {apiKeyPreview}");
            sb.AppendLine($"Debug Enabled: {IsDebugEnabled}");
            sb.AppendLine($"Log Requests: {LogRequests}");
            sb.AppendLine($"Log Responses: {LogResponses}");
            sb.AppendLine($"Log Internals: {LogInternals}");
            sb.AppendLine($"Log Full Messages: {LogFullMessagesEnabled}");
            sb.AppendLine("=================================");

            Log.Message($"{Prefix}\n{sb}");
        }

        public static void LogFullMessages(System.Collections.Generic.List<ChatMessageData> messages, string responseContent)
        {
            if (!LogFullMessagesEnabled) return;

            var sb = new StringBuilder();
            sb.AppendLine("========== FULL MESSAGE LOG ==========");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine();

            // 记录发送的消息
            sb.AppendLine("----- SENT MESSAGES -----");
            if (messages != null && messages.Count > 0)
            {
                for (int i = 0; i < messages.Count; i++)
                {
                    var msg = messages[i];
                    sb.AppendLine($"[{i}] Role: {msg.role}");
                    sb.AppendLine($"    Content: {msg.content}");
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("(No messages sent)");
            }

            // 记录接收的响应
            sb.AppendLine("----- RECEIVED RESPONSE -----");
            if (!string.IsNullOrEmpty(responseContent))
            {
                sb.AppendLine(responseContent);
            }
            else
            {
                sb.AppendLine("(Empty response)");
            }

            sb.AppendLine("======================================");

            Log.Message($"{Prefix}\n{sb}");
        }

        private static string FormatJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return "(empty)";

            try
            {
                // Simple formatting - just ensure it's not too long
                if (json.Length > 2000)
                {
                    return json.Substring(0, 2000) + "\n... (truncated)";
                }
                return json;
            }
            catch
            {
                return json;
            }
        }

        public static string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey)) return "(empty)";
            if (apiKey.Length <= 8) return "***";
            return apiKey.Substring(0, 4) + "..." + apiKey.Substring(apiKey.Length - 4);
        }
    }
}

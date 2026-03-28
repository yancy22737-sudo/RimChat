using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using RimChat.AI;
using RimChat.Core;
using RimChat.Config;

namespace RimChat.Util
{
    public static class DebugLogger
    {
        private const string Prefix = "[RimChat]";
        private const int DebugBatchFlushIntervalTicks = 120;
        private const int MaxBufferedDebugEntries = 200;

        private static readonly object BufferLock = new object();
        private static readonly List<string> BufferedDebugEntries = new List<string>();
        private static int droppedDebugEntryCount;
        private static int nextDebugFlushTick;

        public static bool IsDebugEnabled => (RimChatMod.Instance?.InstanceSettings)?.EnableDebugLogging ?? false;
        public static bool LogRequests => IsDebugEnabled && ((RimChatMod.Instance?.InstanceSettings)?.LogAIRequests ?? false);
        public static bool LogResponses => IsDebugEnabled && ((RimChatMod.Instance?.InstanceSettings)?.LogAIResponses ?? false);
        public static bool LogInternals => IsDebugEnabled && ((RimChatMod.Instance?.InstanceSettings)?.LogInternals ?? false);
        public static bool LogFullMessagesEnabled => IsDebugEnabled && ((RimChatMod.Instance?.InstanceSettings)?.LogFullMessages ?? false);

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
            BufferDebugEntry($"[INTERNAL] [{category}] {message}");
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

        public static void LogFullMessages(List<ChatMessageData> messages, string responseContent)
        {
            if (!LogFullMessagesEnabled) return;

            var sb = new StringBuilder();
            sb.AppendLine("========== FULL MESSAGE LOG ==========");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine();

            AppendSentMessagesBlock(sb, messages);
            AppendResponseBlock(sb, responseContent);

            sb.AppendLine("======================================");
            BufferDebugEntry(sb.ToString());
        }

        public static void LogParseExtraction(string context, PrimaryTextExtractionResult result)
        {
            if (!LogInternals)
            {
                return;
            }

            string safeContext = string.IsNullOrWhiteSpace(context) ? "unknown" : context;
            string status = result?.IsSuccess == true ? "success" : "failure";
            string reason = result?.ReasonTag ?? "unknown";
            string path = result?.MatchedPath ?? string.Empty;
            int length = result?.Content?.Length ?? 0;
            BufferDebugEntry($"[PARSE] context={safeContext}, status={status}, reason={reason}, path={path}, content_length={length}");
        }

        private static void AppendSentMessagesBlock(StringBuilder sb, List<ChatMessageData> messages)
        {
            sb.AppendLine("----- SENT MESSAGES -----");
            if (messages == null || messages.Count == 0)
            {
                sb.AppendLine("(No messages sent)");
                return;
            }

            for (int i = 0; i < messages.Count; i++)
            {
                ChatMessageData msg = messages[i];
                sb.AppendLine($"[{i}] Role: {msg.role}");
                sb.AppendLine($"    Content: {msg.content}");
                sb.AppendLine();
            }
        }

        private static void AppendResponseBlock(StringBuilder sb, string responseContent)
        {
            sb.AppendLine("----- RECEIVED RESPONSE -----");
            if (!string.IsNullOrEmpty(responseContent))
            {
                sb.AppendLine(responseContent);
                return;
            }

            sb.AppendLine("(Empty response)");
        }

        private static void BufferDebugEntry(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            lock (BufferLock)
            {
                BufferedDebugEntries.Add(message);
                TrimBufferedEntriesIfNeeded();
            }

            TryFlushBufferedDebugEntries();
        }

        private static void TrimBufferedEntriesIfNeeded()
        {
            while (BufferedDebugEntries.Count > MaxBufferedDebugEntries)
            {
                BufferedDebugEntries.RemoveAt(0);
                droppedDebugEntryCount++;
            }
        }

        private static void TryFlushBufferedDebugEntries()
        {
            int nowTick = Find.TickManager?.TicksGame ?? Environment.TickCount;
            if (ShouldDeferDebugFlush(nowTick))
            {
                return;
            }

            if (!TryTakeBufferedEntries(nowTick, out List<string> snapshot, out int droppedCount))
            {
                return;
            }

            EmitBufferedEntries(snapshot, droppedCount);
        }

        private static bool ShouldDeferDebugFlush(int nowTick)
        {
            if (IsDeveloperLogWindowOpen())
            {
                return true;
            }

            return nowTick < nextDebugFlushTick;
        }

        private static bool TryTakeBufferedEntries(int nowTick, out List<string> snapshot, out int droppedCount)
        {
            lock (BufferLock)
            {
                if (BufferedDebugEntries.Count == 0)
                {
                    snapshot = null;
                    droppedCount = 0;
                    return false;
                }

                snapshot = new List<string>(BufferedDebugEntries);
                droppedCount = droppedDebugEntryCount;
                BufferedDebugEntries.Clear();
                droppedDebugEntryCount = 0;
                nextDebugFlushTick = nowTick + DebugBatchFlushIntervalTicks;
                return true;
            }
        }

        private static void EmitBufferedEntries(List<string> snapshot, int droppedCount)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"========== DEBUG BATCH ({snapshot.Count}) ==========");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            if (droppedCount > 0)
            {
                sb.AppendLine($"Dropped (queue overflow): {droppedCount}");
            }
            sb.AppendLine("----------------------------------------");

            for (int i = 0; i < snapshot.Count; i++)
            {
                sb.AppendLine(snapshot[i]);
            }

            sb.AppendLine("========================================");
            Log.Message($"{Prefix}\n{sb}");
        }

        private static bool IsDeveloperLogWindowOpen()
        {
            IList<Window> windows = Find.WindowStack?.Windows;
            if (windows == null || windows.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (window == null)
                {
                    continue;
                }

                string fullName = window.GetType().FullName;
                if (string.Equals(fullName, "LudeonTK.EditWindow_Log", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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

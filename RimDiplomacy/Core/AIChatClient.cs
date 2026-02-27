using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace RimDiplomacy
{
    public class ChatMessage
    {
        public string role;
        public string content;
    }

    public class AIChatClient
    {
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
            var config = GetFirstValidConfig();
            if (config == null)
            {
                Log.Error("[RimDiplomacy] No valid AI configuration found.");
                return null;
            }

            string url = config.GetEffectiveEndpoint();
            string apiKey = config.ApiKey;
            string model = config.GetEffectiveModelName();

            string jsonBody = BuildChatCompletionJson(model, messages);

            var tcs = new TaskCompletionSource<string>();
            LongEventHandler.QueueLongEvent(() =>
            {
                try
                {
                    string result = SendRequestSync(url, apiKey, jsonBody, onProgress);
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimDiplomacy] AI request failed: {ex.Message}");
                    tcs.SetException(ex);
                }
            }, "RimDiplomacy_SendingAIRequest".Translate(), false, null);

            return await tcs.Task;
        }

        private string SendRequestSync(string url, string apiKey, string jsonBody, Action<float> onProgress)
        {
            bool isLocalModel = RimDiplomacyMod.Instance == null || !(RimDiplomacyMod.Instance.InstanceSettings?.UseCloudProviders ?? false);
            
            using (var request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                
                if (!isLocalModel || !string.IsNullOrWhiteSpace(apiKey))
                {
                    request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                }
                
                request.timeout = 30;

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
                    return ParseResponse(request.downloadHandler.text);
                }
                else
                {
                    Log.Error($"[RimDiplomacy] AI API error: {request.responseCode} - {request.error}");
                    return null;
                }
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
                Log.Error($"[RimDiplomacy] Failed to parse AI response: {ex.Message}");
            }
            return null;
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
                foreach (var config in RimDiplomacyMod.Instance.InstanceSettings.CloudConfigs)
                {
                    if (config.IsValid())
                        return config;
                }
            }
            else
            {
                if (RimDiplomacyMod.Instance.InstanceSettings.LocalConfig != null && RimDiplomacyMod.Instance.InstanceSettings.LocalConfig.IsValid())
                {
                    return new ApiConfig
                    {
                        Provider = AIProvider.Custom,
                        BaseUrl = RimDiplomacyMod.Instance.InstanceSettings.LocalConfig.BaseUrl + "/v1/chat/completions",
                        SelectedModel = "Custom",
                        CustomModelName = RimDiplomacyMod.Instance.InstanceSettings.LocalConfig.ModelName,
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
            sb.Append("\"max_tokens\":1000");
            sb.Append("}");
            return sb.ToString();
        }

        private string EscapeJson(string str)
        {
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
    }
}

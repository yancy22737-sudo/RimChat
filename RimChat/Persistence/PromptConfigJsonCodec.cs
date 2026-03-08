using System;
using System.Collections.Generic;
using RimChat.Config;
using UnityEngine;

namespace RimChat.Persistence
{
    /// <summary>/// Dependencies: System.Web.Script.Serialization, SystemPromptConfig model.
 /// Responsibility: provide robust typed JSON encode/decode for prompt config with normalization.
 ///</summary>
    internal sealed class PromptConfigJsonCodec
    {
        public bool TrySerialize(SystemPromptConfig config, bool prettyPrint, out string json)
        {
            json = string.Empty;
            if (config == null)
            {
                return false;
            }

            try
            {
                json = JsonUtility.ToJson(config, prettyPrint);
                return !string.IsNullOrWhiteSpace(json);
            }
            catch
            {
                json = string.Empty;
                return false;
            }
        }

        public bool TryDeserialize(string json, out SystemPromptConfig config, out string error)
        {
            config = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "empty_json";
                return false;
            }

            try
            {
                config = JsonUtility.FromJson<SystemPromptConfig>(json);
                Normalize(config);
                return config != null;
            }
            catch (Exception ex)
            {
                config = null;
                error = ex.Message;
                return false;
            }
        }

        private static void Normalize(SystemPromptConfig config)
        {
            if (config == null)
            {
                return;
            }

            config.ApiActions ??= new List<ApiActionConfig>();
            config.DecisionRules ??= new List<DecisionRuleConfig>();
            config.ResponseFormat ??= new ResponseFormatConfig();
            config.EnvironmentPrompt ??= new EnvironmentPromptConfig();
            config.DynamicDataInjection ??= new DynamicDataInjectionConfig();
            config.PromptTemplates ??= new PromptTemplateTextConfig();
            config.EnvironmentPrompt.SceneEntries ??= new List<ScenePromptEntryConfig>();
            config.EnvironmentPrompt.Worldview ??= new WorldviewPromptConfig();
            config.EnvironmentPrompt.SceneSystem ??= new SceneSystemPromptConfig();
            config.EnvironmentPrompt.EnvironmentContextSwitches ??= new EnvironmentContextSwitchesConfig();
            config.EnvironmentPrompt.RpgSceneParamSwitches ??= new RpgSceneParamSwitchesConfig();
            config.EnvironmentPrompt.EventIntelPrompt ??= new EventIntelPromptConfig();
        }
    }
}

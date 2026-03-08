using System;
using System.Collections.Generic;
using Verse;

namespace RimChat.Config
{
    // Prompt template字段定义
    //描述一个可edit的 Prompt 维度
    public class PromptTemplateField
    {
        /// <summary>/// 字段name (如: 核心风格, 用词特征等)
        public string FieldName;

        /// <summary>/// 字段values (具体的描述contents)
        public string FieldValue;

        /// <summary>/// 字段说明 (used for UI 提示)
        public string FieldDescription;

        /// <summary>/// whetherenable该字段
        public bool IsEnabled;

        public PromptTemplateField()
        {
            IsEnabled = true;
        }

        public PromptTemplateField(string fieldName, string fieldValue, string fieldDescription = "")
        {
            FieldName = fieldName;
            FieldValue = fieldValue;
            FieldDescription = fieldDescription;
            IsEnabled = true;
        }

        public PromptTemplateField Clone()
        {
            return new PromptTemplateField
            {
                FieldName = this.FieldName,
                FieldValue = this.FieldValue,
                FieldDescription = this.FieldDescription,
                IsEnabled = this.IsEnabled
            };
        }
    }

    /// <summary>/// faction Prompt configuration
 /// 定义单个faction的 LLM dialogue风格和behavior特征
 ///</summary>
    public class FactionPromptConfig : IExposable
    {
        /// <summary>/// faction defName
 ///</summary>
        public string FactionDefName;

        /// <summary>/// factiondisplayname
 ///</summary>
        public string DisplayName;

        /// <summary>/// Prompt template字段集合
 ///</summary>
        public List<PromptTemplateField> TemplateFields;

        /// <summary>/// whetherenable自定义 Prompt (完全覆盖mode)
 ///</summary>
        public bool UseCustomPrompt;

        /// <summary>/// 自定义 Prompt contents (如果enable完全覆盖mode)
 ///</summary>
        public string CustomPrompt;

        /// <summary>/// 最后修改时间
 ///</summary>
        public long LastModifiedTicks;

        public FactionPromptConfig()
        {
            TemplateFields = new List<PromptTemplateField>();
        }

        public FactionPromptConfig(string factionDefName, string displayName)
        {
            FactionDefName = factionDefName;
            DisplayName = displayName;
            UseCustomPrompt = false;
            TemplateFields = new List<PromptTemplateField>();
        }

        /// <summary>/// get实际使用的 Prompt contents
 ///</summary>
        public string GetEffectivePrompt()
        {
            if (UseCustomPrompt && !string.IsNullOrEmpty(CustomPrompt))
            {
                return CustomPrompt;
            }
            return BuildPromptFromTemplate();
        }

        /// <summary>/// 从template构建 Prompt
 ///</summary>
        public string BuildPromptFromTemplate()
        {
            var parts = new List<string>();

            foreach (var field in TemplateFields)
            {
                if (field.IsEnabled && !string.IsNullOrEmpty(field.FieldValue))
                {
                    parts.Add($"{field.FieldName}: {field.FieldValue}");
                }
            }

            return string.Join("\n\n", parts);
        }

        /// <summary>/// get或创建字段
 ///</summary>
        public PromptTemplateField GetOrCreateField(string fieldName, string defaultValue = "", string description = "")
        {
            var field = TemplateFields.Find(f => f.FieldName == fieldName);
            if (field == null)
            {
                field = new PromptTemplateField(fieldName, defaultValue, description);
                TemplateFields.Add(field);
            }
            return field;
        }

        /// <summary>/// settings字段values
 ///</summary>
        public void SetFieldValue(string fieldName, string value)
        {
            var field = GetOrCreateField(fieldName);
            field.FieldValue = value;
            field.IsEnabled = !string.IsNullOrEmpty(value);
            LastModifiedTicks = DateTime.Now.Ticks;
        }

        /// <summary>/// get字段values
 ///</summary>
        public string GetFieldValue(string fieldName)
        {
            var field = TemplateFields.Find(f => f.FieldName == fieldName);
            return field?.FieldValue ?? "";
        }

        /// <summary>/// 重置为初始state
 ///</summary>
        public void ResetToDefault()
        {
            UseCustomPrompt = false;
            CustomPrompt = "";
            LastModifiedTicks = DateTime.Now.Ticks;
        }

        /// <summary>/// apply自定义 Prompt (完全覆盖mode)
 ///</summary>
        public void ApplyCustomPrompt(string customPrompt)
        {
            CustomPrompt = customPrompt;
            UseCustomPrompt = true;
            LastModifiedTicks = DateTime.Now.Ticks;
        }

        /// <summary>/// 序列化/反序列化
 ///</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref FactionDefName, "factionDefName", "");
            Scribe_Values.Look(ref DisplayName, "displayName", "");
            Scribe_Collections.Look(ref TemplateFields, "templateFields", LookMode.Deep);
            Scribe_Values.Look(ref UseCustomPrompt, "useCustomPrompt", false);
            Scribe_Values.Look(ref CustomPrompt, "customPrompt", "");
            Scribe_Values.Look(ref LastModifiedTicks, "lastModifiedTicks", 0);
        }

        /// <summary>/// 创建副本
 ///</summary>
        public FactionPromptConfig Clone()
        {
            var clone = new FactionPromptConfig
            {
                FactionDefName = this.FactionDefName,
                DisplayName = this.DisplayName,
                UseCustomPrompt = this.UseCustomPrompt,
                CustomPrompt = this.CustomPrompt,
                LastModifiedTicks = this.LastModifiedTicks,
                TemplateFields = new List<PromptTemplateField>()
            };

            foreach (var field in TemplateFields)
            {
                clone.TemplateFields.Add(field.Clone());
            }

            return clone;
        }
    }

    /// <summary>/// factionPromptconfiguration集合
 ///</summary>
    public class FactionPromptConfigCollection : IExposable
    {
        public List<FactionPromptConfig> Configs = new List<FactionPromptConfig>();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref Configs, "configs", LookMode.Deep);
        }

        /// <summary>/// get指定faction的configuration
 ///</summary>
        public FactionPromptConfig GetConfig(string factionDefName)
        {
            return Configs.Find(c => c.FactionDefName == factionDefName);
        }

        /// <summary>/// 添加或更新configuration
 ///</summary>
        public void SetConfig(FactionPromptConfig config)
        {
            var existing = GetConfig(config.FactionDefName);
            if (existing != null)
            {
                Configs.Remove(existing);
            }
            Configs.Add(config);
        }
    }
}

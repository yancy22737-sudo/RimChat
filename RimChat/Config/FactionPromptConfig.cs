using System;
using System.Collections.Generic;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Prompt 模板字段定义
    /// 描述一个可编辑的 Prompt 维度
    /// </summary>
    public class PromptTemplateField
    {
        /// <summary>
        /// 字段名称（如：核心风格、用词特征等）
        /// </summary>
        public string FieldName;

        /// <summary>
        /// 字段值（具体的描述内容）
        /// </summary>
        public string FieldValue;

        /// <summary>
        /// 字段说明（用于 UI 提示）
        /// </summary>
        public string FieldDescription;

        /// <summary>
        /// 是否启用该字段
        /// </summary>
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

    /// <summary>
    /// 派系 Prompt 配置
    /// 定义单个派系的 LLM 对话风格和行为特征
    /// </summary>
    public class FactionPromptConfig : IExposable
    {
        /// <summary>
        /// 派系 defName
        /// </summary>
        public string FactionDefName;

        /// <summary>
        /// 派系显示名称
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// Prompt 模板字段集合
        /// </summary>
        public List<PromptTemplateField> TemplateFields;

        /// <summary>
        /// 是否启用自定义 Prompt（完全覆盖模式）
        /// </summary>
        public bool UseCustomPrompt;

        /// <summary>
        /// 自定义 Prompt 内容（如果启用完全覆盖模式）
        /// </summary>
        public string CustomPrompt;

        /// <summary>
        /// 最后修改时间
        /// </summary>
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

        /// <summary>
        /// 获取实际使用的 Prompt 内容
        /// </summary>
        public string GetEffectivePrompt()
        {
            if (UseCustomPrompt && !string.IsNullOrEmpty(CustomPrompt))
            {
                return CustomPrompt;
            }
            return BuildPromptFromTemplate();
        }

        /// <summary>
        /// 从模板构建 Prompt
        /// </summary>
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

        /// <summary>
        /// 获取或创建字段
        /// </summary>
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

        /// <summary>
        /// 设置字段值
        /// </summary>
        public void SetFieldValue(string fieldName, string value)
        {
            var field = GetOrCreateField(fieldName);
            field.FieldValue = value;
            field.IsEnabled = !string.IsNullOrEmpty(value);
            LastModifiedTicks = DateTime.Now.Ticks;
        }

        /// <summary>
        /// 获取字段值
        /// </summary>
        public string GetFieldValue(string fieldName)
        {
            var field = TemplateFields.Find(f => f.FieldName == fieldName);
            return field?.FieldValue ?? "";
        }

        /// <summary>
        /// 重置为初始状态
        /// </summary>
        public void ResetToDefault()
        {
            UseCustomPrompt = false;
            CustomPrompt = "";
            LastModifiedTicks = DateTime.Now.Ticks;
        }

        /// <summary>
        /// 应用自定义 Prompt（完全覆盖模式）
        /// </summary>
        public void ApplyCustomPrompt(string customPrompt)
        {
            CustomPrompt = customPrompt;
            UseCustomPrompt = true;
            LastModifiedTicks = DateTime.Now.Ticks;
        }

        /// <summary>
        /// 序列化/反序列化
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref FactionDefName, "factionDefName", "");
            Scribe_Values.Look(ref DisplayName, "displayName", "");
            Scribe_Collections.Look(ref TemplateFields, "templateFields", LookMode.Deep);
            Scribe_Values.Look(ref UseCustomPrompt, "useCustomPrompt", false);
            Scribe_Values.Look(ref CustomPrompt, "customPrompt", "");
            Scribe_Values.Look(ref LastModifiedTicks, "lastModifiedTicks", 0);
        }

        /// <summary>
        /// 创建副本
        /// </summary>
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

    /// <summary>
    /// 派系Prompt配置集合
    /// </summary>
    public class FactionPromptConfigCollection : IExposable
    {
        public List<FactionPromptConfig> Configs = new List<FactionPromptConfig>();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref Configs, "configs", LookMode.Deep);
        }

        /// <summary>
        /// 获取指定派系的配置
        /// </summary>
        public FactionPromptConfig GetConfig(string factionDefName)
        {
            return Configs.Find(c => c.FactionDefName == factionDefName);
        }

        /// <summary>
        /// 添加或更新配置
        /// </summary>
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

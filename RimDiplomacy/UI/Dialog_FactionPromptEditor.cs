using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimDiplomacy.Config;

namespace RimDiplomacy.UI
{
    /// <summary>
    /// 派系提示词模板编辑窗口
    /// 支持编辑各个维度字段（核心风格、用词特征等）
    /// </summary>
    public class Dialog_FactionPromptEditor : Window
    {
        private readonly FactionPromptConfig factionConfig;
        private Vector2 scrollPosition;
        private Dictionary<string, string> fieldBuffers;
        private bool showPreview;
        private bool previewCollapsed;
        private float previewFoldAnimTime;

        public Dialog_FactionPromptEditor(FactionPromptConfig config)
        {
            this.factionConfig = config;
            this.fieldBuffers = new Dictionary<string, string>();
            this.showPreview = false;
            this.previewCollapsed = false;
            this.previewFoldAnimTime = 0f;
            this.doCloseButton = true;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = true;

            // 初始化编辑缓冲区
            foreach (var field in factionConfig.TemplateFields)
            {
                fieldBuffers[field.FieldName] = field.FieldValue;
            }
        }

        public override Vector2 InitialSize => new Vector2(700f, 600f);

        public override void DoWindowContents(Rect inRect)
        {
            // Title
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 32f);
            Widgets.Label(titleRect, $"{factionConfig.DisplayName} - {"RimDiplomacy_PromptTemplateEditor".Translate()}");
            Text.Font = GameFont.Small;

            float y = 40f;

            // Toggle preview button
            Rect toggleRect = new Rect(inRect.xMax - 120f, y, 120f, 24f);
            if (Widgets.ButtonText(toggleRect, showPreview ? "RimDiplomacy_HidePreview".Translate() : "RimDiplomacy_ShowPreview".Translate()))
            {
                showPreview = !showPreview;
            }
            y += 30f;

            // Description
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect descRect = new Rect(inRect.x, y, inRect.width, Text.LineHeight * 2);
            Widgets.Label(descRect, "RimDiplomacy_PromptTemplateEditorDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 35f;

            // Scroll view for fields
            float contentHeight = factionConfig.TemplateFields.Count * 180f + 50f;
            if (showPreview)
            {
                contentHeight += previewCollapsed ? 40f : 150f;
            }

            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentHeight);
            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.height - y - 50f);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            
            float currentY = 0f;
            foreach (var field in factionConfig.TemplateFields)
            {
                // Field label
                Text.Font = GameFont.Small;
                GUI.color = Color.cyan;
                Rect labelRect = new Rect(0f, currentY, viewRect.width, 24f);
                Widgets.Label(labelRect, GetFieldLabel(field.FieldName));
                GUI.color = Color.white;
                currentY += 24f;

                // Field description (tooltip style)
                if (!string.IsNullOrEmpty(field.FieldDescription))
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = Color.gray;
                    Rect descFieldRect = new Rect(0f, currentY, viewRect.width, Text.LineHeight);
                    Widgets.Label(descFieldRect, GetFieldDescription(field.FieldName));
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    currentY += Text.LineHeight + 5f;
                }

                // Text area for field value
                if (!fieldBuffers.ContainsKey(field.FieldName))
                {
                    fieldBuffers[field.FieldName] = field.FieldValue;
                }

                float textHeight = 80f;
                Rect textRect = new Rect(0f, currentY, viewRect.width - 16f, textHeight);
                
                GUI.BeginGroup(textRect);
                Rect innerRect = new Rect(0f, 0f, textRect.width - 16f, Mathf.Max(textHeight, Text.CalcHeight(fieldBuffers[field.FieldName], textRect.width - 20f) + 10f));
                
                fieldBuffers[field.FieldName] = Widgets.TextArea(innerRect, fieldBuffers[field.FieldName]);
                
                GUI.EndGroup();
                
                currentY += textHeight + 20f;

                // Separator line
                Rect lineRect = new Rect(0f, currentY, viewRect.width, 1f);
                GUI.color = Color.gray;
                GUI.DrawTexture(lineRect, BaseContent.WhiteTex);
                GUI.color = Color.white;
                currentY += 15f;
            }

            // Preview section
            if (showPreview)
            {
                // Update animation time
                if (previewFoldAnimTime > 0f)
                {
                    previewFoldAnimTime -= Time.deltaTime;
                }
                
                // Preview header with fold button
                Rect previewHeaderRect = new Rect(0f, currentY, viewRect.width, 24f);
                
                // Draw fold button (triangle)
                Rect foldBtnRect = new Rect(previewHeaderRect.xMax - 24f, previewHeaderRect.y, 24f, 24f);
                if (Widgets.ButtonInvisible(foldBtnRect))
                {
                    previewCollapsed = !previewCollapsed;
                    previewFoldAnimTime = 0.2f;
                }
                
                // Draw triangle arrow
                float angle = previewCollapsed ? 0f : 90f;
                if (previewFoldAnimTime > 0f)
                {
                    float t = 1f - (previewFoldAnimTime / 0.2f);
                    angle = Mathf.Lerp(previewCollapsed ? 90f : 0f, previewCollapsed ? 0f : 90f, t);
                }
                
                Vector2 pivot = new Vector2(foldBtnRect.x + foldBtnRect.width / 2f, foldBtnRect.y + foldBtnRect.height / 2f);
                Matrix4x4 matrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, pivot);
                
                // Draw triangle
                Rect triangleRect = new Rect(foldBtnRect.x + 6f, foldBtnRect.y + 6f, 12f, 12f);
                GUI.color = Color.gray;
                if (Mouse.IsOver(foldBtnRect))
                {
                    GUI.color = Color.white;
                }
                Widgets.DrawTextureFitted(triangleRect, TexButton.Collapse, 1f);
                GUI.color = Color.white;
                
                GUI.matrix = matrix;
                
                // Preview label
                Text.Font = GameFont.Small;
                GUI.color = Color.green;
                Rect previewLabelRect = new Rect(0f, currentY, viewRect.width - 30f, 24f);
                Widgets.Label(previewLabelRect, "RimDiplomacy_PreviewLabel".Translate());
                GUI.color = Color.white;
                currentY += 24f;
                
                // Preview content (with collapse animation)
                if (!previewCollapsed || previewFoldAnimTime > 0f)
                {
                    float contentHeightFactor = 1f;
                    if (previewFoldAnimTime > 0f)
                    {
                        float t = 1f - (previewFoldAnimTime / 0.2f);
                        contentHeightFactor = previewCollapsed ? 1f - t : t;
                    }
                    
                    if (contentHeightFactor > 0.01f)
                    {
                        string previewText = BuildPreviewText();
                        float actualPreviewHeight = 120f * contentHeightFactor;
                        Rect previewRect = new Rect(0f, currentY, viewRect.width - 16f, actualPreviewHeight);
                        
                        if (contentHeightFactor >= 1f)
                        {
                            GUI.BeginGroup(previewRect);
                            Rect previewInnerRect = new Rect(0f, 0f, previewRect.width - 16f, Mathf.Max(120f, Text.CalcHeight(previewText, previewRect.width - 20f) + 10f));
                            
                            GUI.color = new Color(0f, 0f, 0f, 0.2f);
                            GUI.DrawTexture(previewInnerRect, BaseContent.WhiteTex);
                            GUI.color = Color.white;
                            
                            Text.Font = GameFont.Small;
                            Widgets.Label(previewInnerRect, previewText);
                            
                            GUI.EndGroup();
                        }
                        
                        currentY += 130f * contentHeightFactor;
                    }
                }
                else if (previewCollapsed)
                {
                    // Show a minimal indicator when collapsed
                    Rect collapsedIndicatorRect = new Rect(0f, currentY, viewRect.width, 16f);
                    GUI.color = new Color(0.3f, 0.3f, 0.3f, 0.3f);
                    GUI.DrawTexture(collapsedIndicatorRect, BaseContent.WhiteTex);
                    GUI.color = Color.gray;
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(collapsedIndicatorRect, "  (collapsed)");
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    currentY += 20f;
                }
            }

            Widgets.EndScrollView();

            // Save button
            float btnWidth = 100f;
            float btnHeight = 35f;
            float btnY = inRect.yMax - btnHeight;

            Rect saveRect = new Rect(inRect.xMax - btnWidth - 10f, btnY, btnWidth, btnHeight);
            if (Widgets.ButtonText(saveRect, "RimDiplomacy_Save".Translate()))
            {
                SaveChanges();
                Close();
            }

            // Reset button
            Rect resetRect = new Rect(inRect.x, btnY, btnWidth, btnHeight);
            if (Widgets.ButtonText(resetRect, "RimDiplomacy_Reset".Translate()))
            {
                ResetToDefaults();
            }
        }

        private string BuildPreviewText()
        {
            var parts = new List<string>();
            foreach (var field in factionConfig.TemplateFields)
            {
                string value = fieldBuffers.ContainsKey(field.FieldName) ? fieldBuffers[field.FieldName] : field.FieldValue;
                if (!string.IsNullOrEmpty(value))
                {
                    parts.Add($"{field.FieldName}: {value}");
                }
            }
            return string.Join("\n\n", parts);
        }

        private void SaveChanges()
        {
            foreach (var field in factionConfig.TemplateFields)
            {
                if (fieldBuffers.ContainsKey(field.FieldName))
                {
                    field.FieldValue = fieldBuffers[field.FieldName];
                    field.IsEnabled = !string.IsNullOrEmpty(field.FieldValue);
                }
            }
            factionConfig.LastModifiedTicks = System.DateTime.Now.Ticks;
            
            // 保存到文件
            FactionPromptManager.Instance.UpdateConfig(factionConfig);
        }

        private void ResetToDefaults()
        {
            // 重新加载默认配置
            var defaultConfig = FactionPromptManager.Instance.GetConfig(factionConfig.FactionDefName);
            if (defaultConfig != null)
            {
                foreach (var field in factionConfig.TemplateFields)
                {
                    var defaultField = defaultConfig.TemplateFields.Find(f => f.FieldName == field.FieldName);
                    if (defaultField != null)
                    {
                        field.FieldValue = defaultField.FieldValue;
                        fieldBuffers[field.FieldName] = defaultField.FieldValue;
                    }
                }
            }
        }

        private string GetFieldLabel(string fieldName)
        {
            return $"RimDiplomacy_Field{fieldName}".Translate();
        }

        private string GetFieldDescription(string fieldName)
        {
            return $"RimDiplomacy_Field{fieldName}Desc".Translate();
        }
    }
}

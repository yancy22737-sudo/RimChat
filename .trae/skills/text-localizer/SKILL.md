---
name: text-localizer
description: "用于定位、展示和修改 RimWorld Mod 项目中功能模块相关的 UI 文本。当用户描述一个已有功能模块并要求\"展示/修改文本\"、\"查看xx功能的（文本）\"、\"修改xx界面的（文字）\"等时触发。帮助用户快速找到功能对应的 Language XML 文件中的文本条目，并提供修改建议。"
---

# Text Localizer - UI 文本定位与修改助手

## 概述

本 Skill 帮助用户快速定位 RimWorld Mod 中特定功能模块对应的 UI 显示文本，支持展示、检查和修改操作。

## 触发条件

当用户输入包含以下模式时触发：
- "展示xx功能的文本"
- "检查xx界面的文字"
- "修改xx文本"
- "查看xx相关的显示文本"
- "xx功能对应的语言文本在哪里"
明确：没有出现“文本/文字”时禁止调用

## 工作流程

### 步骤 1: 解析用户意图

从用户描述中提取：
- **功能模块名称**：用户提到的功能（如"装备定制窗口"、"派系设置面板"等）
- **操作类型**：展示 / 检查 / 修改

### 步骤 2: 定位相关文件

根据功能模块名称，搜索以下位置：

1. **Language 文件夹**（优先级最高）
   - 路径：`Languages/ChineseSimplified/Keyed/` 或 `Languages/ChineseSimplified/DefInjected/`
   - 搜索关键词：功能模块相关的标签名（通常包含窗口名、类名等）

2. **C# 源码中的标签引用**
   - 搜索 `"xxx.Label"` 或 `"xxx.Title"` 等模式
   - 查找 `Translate()` 或 `TranslateKeyed()` 调用

3. **XML Def 文件**
   - 检查 `Defs/` 目录下相关定义的标签字段

### 步骤 3: 展示文本内容

找到相关文本后，以以下格式展示：

```
【功能模块】: xxx
【文件路径】: Languages/ChineseSimplified/Keyed/xxx.xml

相关文本条目：
1. <KeyName>当前文本内容</KeyName>
   - 用途: [根据代码上下文推断]
   
2. <AnotherKey>另一个文本</AnotherKey>
   - 用途: [根据代码上下文推断]
```

### 步骤 4: 处理修改请求

如果用户要求修改：

1. **确认修改范围**：询问用户要修改哪个/哪些条目
2. **提供修改建议**：根据上下文给出改进建议
3. **执行修改**：使用 SearchReplace 工具修改 XML 文件
4. **验证修改**：展示修改前后的对比

## 文本定位策略

### 从 C# 代码反查

常见翻译调用模式：
- `"KeyName".Translate()` - 标准翻译
- `"KeyName".Translate(arg1, arg2)` - 带参数的翻译
- `LabelCap` / `Label` 属性 - 自动翻译

### 从 XML 结构推断

Language XML 常见结构：
```xml
<?xml version="1.0" encoding="utf-8"?>
<LanguageData>
  <ModName_SettingLabel>设置标签</ModName_SettingLabel>
  <ModName_WindowTitle>窗口标题</ModName_WindowTitle>
  <ModName_ButtonText>按钮文字</ModName_ButtonText>
  <ModName_Tooltip>提示文本</ModName_Tooltip>
</LanguageData>
```

### 搜索优先级

1. 精确匹配功能模块名称
2. 匹配相关类名（如 `FactionGearCustomizerWindow` → 搜索 `FactionGearCustomizer`）
3. 匹配命名空间/Mod名称前缀
4. 模糊匹配相关关键词

## 输出格式规范

### 展示模式

```
=== 功能模块: [模块名] ===

📁 语言文件位置:
   - Languages/ChineseSimplified/Keyed/xxx.xml

📝 相关文本条目:
   ┌─────────────────────────────────────────────────────────┐
   │ Key: ModName_Label                                       │
   │ 当前值: "原始文本"                                        │
   │ 用途: 窗口标题栏                                          │
   └─────────────────────────────────────────────────────────┘
```

### 修改确认模式

```
请确认以下修改：

文件: Languages/ChineseSimplified/Keyed/xxx.xml

修改前: <KeyName>旧文本</KeyName>
修改后: <KeyName>新文本</KeyName>

是否确认修改? (是/否/调整)
```

## 注意事项

1. **多语言支持**：优先展示简体中文，如不存在则提示用户
2. **Key 命名规范**：遵循 RimWorld 的 `ModName_Category_Element` 格式
3. **修改安全**：修改前备份原文件到 Legacy 目录（按项目规范）
4. **上下文关联**：尽可能说明每个文本在游戏中的具体显示位置
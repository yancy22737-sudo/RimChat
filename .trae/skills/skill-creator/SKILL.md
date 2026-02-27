---
name: skill-creator
description: 高效Skill创建指南。当用户想要创建新Skill（或更新现有Skill），以通过专业知识、工作流或工具集成扩展Claude的能力时，应使用本Skill。
license: Complete terms in LICENSE.txt
tags: skill-creation, claude-extension, agent-workflow, skill-packaging, modular-ai-assets
tags_cn: Skill创建, Claude能力扩展, Agent工作流, Skill打包, 模块化AI资源
---

# Skill 创建指南

本Skill为创建高效Skill提供指导。

## 关于Skill

Skill是模块化、独立的包，通过提供专业知识、工作流和工具来扩展Claude的能力。可以将它们视为特定领域或任务的「入门指南」——它们将Claude从通用Agent转变为具备过程性知识的专业Agent，而这类知识是任何模型都无法完全拥有的。

### Skill能提供什么

1. 专业工作流 - 特定领域的多步骤流程
2. 工具集成 - 处理特定文件格式或API的操作说明
3. 领域专业知识 - 公司专属知识、数据结构、业务逻辑
4. 捆绑资源 - 用于复杂重复任务的脚本、参考资料和资产

## 核心原则

### 简洁为要

上下文窗口是公共资源。Skill需要与Claude所需的其他所有内容共享上下文窗口：系统提示词、对话历史、其他Skill的元数据以及实际用户请求。

**默认假设：Claude已经非常智能。** 仅添加Claude原本不具备的上下文信息。对每一条信息都要提出质疑：「Claude真的需要这个解释吗？」以及「这段内容的令牌成本是否合理？」

优先使用简洁示例而非冗长说明。

### 设置合适的自由度

根据任务的脆弱性和可变性匹配具体程度：

**高自由度（基于文本的指令）**：当存在多种有效方法、决策依赖上下文或启发式方法指导操作时使用。

**中等自由度（带参数的伪代码或脚本）**：当存在首选模式、允许一定变化或配置会影响行为时使用。

**低自由度（特定脚本，参数极少）**：当操作脆弱且容易出错、一致性至关重要或必须遵循特定序列时使用。

可以把Claude比作探索路径的人：两侧是悬崖的狭窄桥梁需要明确的护栏（低自由度），而开阔的田野则允许多种路线（高自由度）。

### Skill的结构

每个Skill都包含一个必填的SKILL.md文件和可选的捆绑资源：

```
skill-name/
├── SKILL.md (必填)
│   ├── YAML 前置元数据 (必填)
│   │   ├── name: (必填)
│   │   └── description: (必填)
│   └── Markdown 说明文档 (必填)
└── 捆绑资源 (可选)
    ├── scripts/          - 可执行代码（Python/Bash等）
    ├── references/       - 按需加载到上下文中的参考文档
    └── assets/           - 输出中使用的文件（模板、图标、字体等）
```

#### SKILL.md（必填）

每个SKILL.md都包含：

- **前置元数据（YAML）**：包含`name`和`description`字段。这是Claude判断何时使用该Skill的唯一依据，因此清晰全面地描述Skill的功能和适用场景至关重要。
- **正文（Markdown）**：使用Skill的说明和指南。仅在Skill触发后才会加载（如果需要）。

#### 捆绑资源（可选）

##### 脚本（`scripts/`）

用于需要确定性可靠性或重复编写的任务的可执行代码（Python/Bash等）。

- **何时包含**：当相同代码被重复编写或需要确定性可靠性时
- **示例**：用于PDF旋转任务的`scripts/rotate_pdf.py`
- **优势**：令牌效率高、结果确定，无需加载到上下文即可执行
- **注意**：Claude可能仍需要读取脚本以进行补丁或环境特定调整

##### 参考资料（`references/`）

旨在按需加载到上下文中，为Claude的处理和思考提供信息的文档和参考材料。

- **何时包含**：当Claude在工作中需要参考文档时
- **示例**：用于财务数据结构的`references/finance.md`、用于公司NDA模板的`references/mnda.md`、用于公司政策的`references/policies.md`、用于API规范的`references/api_docs.md`
- **使用场景**：数据库结构、API文档、领域知识、公司政策、详细工作流指南
- **优势**：保持SKILL.md简洁，仅在Claude确定需要时才加载
- **最佳实践**：如果文件较大（>10000字），在SKILL.md中包含grep搜索模式
- **避免重复**：信息应仅存在于SKILL.md或参考文件中，不要同时存在。除非是Skill的核心内容，否则优先将详细信息放在参考文件中——这样可以保持SKILL.md简洁，同时让信息可被发现且不会占用上下文窗口。仅在SKILL.md中保留必要的流程说明和工作流指南；将详细参考材料、数据结构和示例移至参考文件。

##### 资产（`assets/`）

不加载到上下文中，而是在Claude生成的输出中使用的文件。

- **何时包含**：当Skill需要在最终输出中使用文件时
- **示例**：用于品牌资产的`assets/logo.png`、用于PowerPoint模板的`assets/slides.pptx`、用于HTML/React模板的`assets/frontend-template/`、用于字体的`assets/font.ttf`
- **使用场景**：模板、图片、图标、样板代码、字体、可复制或修改的示例文档
- **优势**：将输出资源与文档分离，使Claude无需加载即可使用文件

#### Skill中不应包含的内容

Skill应仅包含直接支持其功能的必要文件。请勿创建无关的文档或辅助文件，包括：

- README.md
- INSTALLATION_GUIDE.md
- QUICK_REFERENCE.md
- CHANGELOG.md
- 等

Skill应仅包含AI Agent完成当前工作所需的信息。不应包含关于创建过程、设置和测试流程、面向用户的文档等辅助上下文。创建额外的文档文件只会增加混乱。

### 渐进式披露设计原则

Skill使用三级加载系统来高效管理上下文：

1. **元数据（名称+描述）** - 始终在上下文中（约100词）
2. **SKILL.md正文** - 当Skill触发时加载（<5000词）
3. **捆绑资源** - 由Claude按需加载（无限制，因为脚本可以在不读取到上下文窗口的情况下执行）

#### 渐进式披露模式

将SKILL.md正文控制在必要内容范围内，且不超过500行，以减少上下文冗余。当接近此限制时，将内容拆分为单独的文件。将内容拆分到其他文件时，务必在SKILL.md中引用它们，并明确说明何时读取，以确保Skill的使用者知道它们的存在和使用时机。

**核心原则**：当Skill支持多种变体、框架或选项时，仅在SKILL.md中保留核心工作流和选择指南。将变体特定的详细信息（模式、示例、配置）移至单独的参考文件。

**模式1：带参考资料的高级指南**

```markdown
# PDF 处理

## 快速开始

使用pdfplumber提取文本：
[代码示例]

## 高级功能

- **表单填充**：完整指南请参见[FORMS.md](FORMS.md)
- **API参考**：所有方法请参见[REFERENCE.md](REFERENCE.md)
- **示例**：常见模式请参见[EXAMPLES.md](EXAMPLES.md)
```

Claude仅在需要时才会加载FORMS.md、REFERENCE.md或EXAMPLES.md。

**模式2：按领域组织**

对于涉及多个领域的Skill，按领域组织内容以避免加载无关上下文：

```
bigquery-skill/
├── SKILL.md（概述和导航）
└── reference/
    ├── finance.md（收入、计费指标）
    ├── sales.md（销售机会、销售漏斗）
    ├── product.md（API使用、功能）
    └── marketing.md（营销活动、归因）
```

当用户询问销售指标时，Claude仅读取sales.md。

类似地，对于支持多个框架或变体的Skill，按变体组织：

```
cloud-deploy/
├── SKILL.md（工作流+供应商选择）
└── references/
    ├── aws.md（AWS部署模式）
    ├── gcp.md（GCP部署模式）
    └── azure.md（Azure部署模式）
```

当用户选择AWS时，Claude仅读取aws.md。

**模式3：条件式详细说明**

显示基础内容，链接到高级内容：

```markdown
# DOCX 处理

## 创建文档

使用docx-js创建新文档。请参见[DOCX-JS.md](DOCX-JS.md)。

## 编辑文档

对于简单编辑，直接修改XML即可。

**如需跟踪更改**：请参见[REDLINING.md](REDLINING.md)
**如需OOXML详细信息**：请参见[OOXML.md](OOXML.md)
```

Claude仅在用户需要这些功能时才会读取REDLINING.md或OOXML.md。

**重要指南**：

- **避免深度嵌套参考** - 参考文件应直接从SKILL.md链接，最多嵌套一层。所有参考文件都应直接与SKILL.md关联。
- **结构化长参考文件** - 对于超过100行的文件，在顶部添加目录，以便Claude预览时可以了解完整范围。

## Skill 创建流程

Skill创建包含以下步骤：

1. 通过具体示例理解Skill需求
2. 规划可复用的Skill内容（脚本、参考资料、资产）
3. 初始化Skill（运行init_skill.py）
4. 编辑Skill（实现资源并编写SKILL.md）
5. 打包Skill（运行package_skill.py）
6. 根据实际使用情况迭代优化

按顺序执行这些步骤，仅在有明确理由时跳过步骤。

### 步骤1：通过具体示例理解Skill需求

仅当Skill的使用模式已非常明确时，才可跳过此步骤。即使处理现有Skill，此步骤仍然有价值。

要创建有效的Skill，需明确了解Skill的具体使用示例。这种理解可以来自直接的用户示例，也可以来自经用户反馈验证的生成示例。

例如，构建图像编辑器Skill时，相关问题包括：

- 「图像编辑器Skill应支持哪些功能？编辑、旋转，还有其他吗？」
- 「你能给出一些该Skill的使用示例吗？」
- 「我可以想象用户会要求‘去除这张图片的红眼’或‘旋转这张图片’。你认为用户还会以其他方式使用这个Skill吗？」
- 「用户说什么话时应该触发这个Skill？」

为避免使用户感到负担，请勿在一条消息中提出过多问题。从最重要的问题开始，根据需要跟进以获得更好的效果。

当明确了解Skill应支持的功能时，即可结束此步骤。

### 步骤2：规划可复用的Skill内容

要将具体示例转化为有效的Skill，需按以下方式分析每个示例：

1. 考虑如何从头开始执行示例中的任务
2. 确定哪些脚本、参考资料和资产在重复执行这些工作流时会有帮助

示例：构建处理「帮我旋转这个PDF」等查询的`pdf-editor`Skill时，分析结果如下：

1. 旋转PDF需要重复编写相同的代码
2. 在Skill中存储`scripts/rotate_pdf.py`脚本会很有帮助

示例：设计处理「帮我构建一个待办事项应用」或「帮我构建一个跟踪步数的仪表板」等查询的`frontend-webapp-builder`Skill时，分析结果如下：

1. 构建前端Web应用需要重复编写相同的HTML/React样板代码
2. 在Skill中存储包含HTML/React项目样板文件的`assets/hello-world/`模板会很有帮助

示例构建处理「今天有多少用户登录？」等查询的`big-query`Skill时，分析结果如下：

1. 查询BigQuery需要重复查找表结构和关系
2. 在Skill中存储记录表结构的`references/schema.md`文件会很有帮助

要确定Skill的内容，需分析每个具体示例，创建要包含的可复用资源列表：脚本、参考资料和资产。

### 步骤3：初始化Skill

此时可以开始实际创建Skill了。

仅当开发的Skill已存在，且仅需迭代或打包时，才可跳过此步骤。在这种情况下，直接进入下一步。

从头创建新Skill时，务必运行`init_skill.py`脚本。该脚本会方便地生成一个新的Skill模板目录，自动包含Skill所需的所有内容，使Skill创建过程更高效可靠。

使用方法：

```bash
scripts/init_skill.py <skill-name> --path <output-directory>
```

该脚本会：

- 在指定路径创建Skill目录
- 生成带有正确前置元数据和待办事项占位符的SKILL.md模板
- 创建示例资源目录：`scripts/`、`references/`和`assets/`
- 在每个目录中添加可自定义或删除的示例文件

初始化完成后，根据需要自定义或删除生成的SKILL.md和示例文件。

### 步骤4：编辑Skill

编辑（新生成或现有）Skill时，请记住，该Skill是为另一个Claude实例使用而创建的。请加入对Claude有帮助且非显而易见的信息。考虑哪些过程性知识、领域特定细节或可复用资产可以帮助另一个Claude实例更有效地执行这些任务。

#### 学习经过验证的设计模式

根据Skill的需求，参考以下有用指南：

- **多步骤流程**：请参见references/workflows.md了解顺序工作流和条件逻辑
- **特定输出格式或质量标准**：请参见references/output-patterns.md了解模板和示例模式

这些文件包含有效Skill设计的既定最佳实践。

#### 从可复用Skill内容开始

开始实现时，先处理上面确定的可复用资源：`scripts/`、`references/`和`assets/`文件。请注意，此步骤可能需要用户输入。例如，实现品牌指南Skill时，用户可能需要提供存储在`assets/`中的品牌资产或模板，或存储在`references/`中的文档。

添加的脚本必须通过实际运行进行测试，以确保没有错误且输出符合预期。如果有许多类似的脚本，只需测试代表性样本即可，在确保所有脚本正常工作的同时平衡完成时间。

任何Skill不需要的示例文件和目录都应删除。初始化脚本会在`scripts/`、`references/`和`assets/`中创建示例文件以展示结构，但大多数Skill不需要所有这些文件。

#### 更新SKILL.md

**编写指南**：始终使用祈使句/不定式形式。

##### 前置元数据

编写包含`name`和`description`的YAML前置元数据：

- `name`：Skill名称
- `description`：这是Skill的主要触发机制，帮助Claude了解何时使用该Skill。
  - 需同时包含Skill的功能和使用的具体触发场景/上下文。
  - 所有「何时使用」的信息都应放在此处——不要放在正文中。正文仅在触发后才加载，因此正文中的「何时使用本Skill」部分对Claude没有帮助。
  - `docx`Skill的描述示例「全面的文档创建、编辑和分析，支持跟踪更改、批注、格式保留和文本提取。当Claude需要处理专业文档（.docx文件）时使用，包括：(1) 创建新文档，(2) 修改或编辑内容，(3) 处理跟踪更改，(4) 添加批注，或任何其他文档任务」

请勿在YAML前置元数据中包含其他字段。

##### 正文

编写使用Skill及其捆绑资源的说明。

### 步骤5：打包Skill

Skill开发完成后，必须将其打包为可分发的.skill文件，以便与用户共享。打包过程会自动验证Skill是否符合所有要求：

```bash
scripts/package_skill.py <path/to/skill-folder>
```

可选的输出目录指定：

```bash
scripts/package_skill.py <path/to/skill-folder> ./dist
```

打包脚本会：

1. **验证**：自动验证Skill，检查：

   - YAML前置元数据格式和必填字段
   - Skill命名规范和目录结构
   - 描述的完整性和质量
   - 文件组织和资源引用

2. **打包**：如果验证通过，创建以Skill命名的.skill文件（例如`my-skill.skill`），包含所有文件并保持正确的目录结构以便分发。.skill文件是带有.skill扩展名的zip文件。

如果验证失败，脚本会报告错误并退出，不创建包。修复所有验证错误后，重新运行打包命令。

### 步骤6：迭代优化

测试Skill后，用户可能会提出改进请求。通常这会在使用Skill后立即发生，此时用户对Skill的表现有清晰的上下文。

**迭代工作流**：

1. 在实际任务中使用Skill
2. 注意Skill的不足或低效之处
3. 确定应如何更新SKILL.md或捆绑资源
4. 实施更改并再次测试

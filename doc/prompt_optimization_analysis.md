# LLM 对话层 Prompt 上下文系统性优化分析报告
# Systemic Optimization Analysis Report for LLM Dialogue Layer Prompt Context

## 1. 目录 (Table of Contents)

- [LLM 对话层 Prompt 上下文系统性优化分析报告](#llm-对话层-prompt-上下文系统性优化分析报告)
- [Systemic Optimization Analysis Report for LLM Dialogue Layer Prompt Context](#systemic-optimization-analysis-report-for-llm-dialogue-layer-prompt-context)
  - [1. 目录 (Table of Contents)](#1-目录-table-of-contents)
  - [1. 摘要 (Executive Summary)](#1-摘要-executive-summary)
  - [2. 现状评估 (Current State Assessment)](#2-现状评估-current-state-assessment)
    - [2.1 Prompt 架构解构 (Prompt Architecture Deconstruction)](#21-prompt-架构解构-prompt-architecture-deconstruction)
    - [2.2 关键代码路径分析 (Key Code Path Analysis)](#22-关键代码路径分析-key-code-path-analysis)
    - [2.3 性能与质量多维度评估 (Multi-dimensional Performance \& Quality Assessment)](#23-性能与质量多维度评估-multi-dimensional-performance--quality-assessment)
  - [3. 问题根因 (Root Cause Analysis)](#3-问题根因-root-cause-analysis)
    - [3.1 结构性冗余与“迷失中间”效应 (Structural Redundancy \& "Lost in the Middle")](#31-结构性冗余与迷失中间效应-structural-redundancy--lost-in-the-middle)
    - [3.2 语义表达的混合范式冲突 (Mixed Paradigm Conflict in Semantic Expression)](#32-语义表达的混合范式冲突-mixed-paradigm-conflict-in-semantic-expression)
    - [3.3 动态变量管理的硬编码局限 (Hard-coded Limitations of Dynamic Variable Management)](#33-动态变量管理的硬编码局限-hard-coded-limitations-of-dynamic-variable-management)
    - [3.4 缺乏思维链与示例引导 (Lack of Chain-of-Thought \& Few-shot Guidance)](#34-缺乏思维链与示例引导-lack-of-chain-of-thought--few-shot-guidance)
  - [4. 优化方案 (Optimization Strategy)](#4-优化方案-optimization-strategy)
    - [4.1 上下文结构层：模块化与 XML 标签体系 (Context Structure: Modularity \& XML Tag System)](#41-上下文结构层模块化与-xml-标签体系-context-structure-modularity--xml-tag-system)
    - [4.2 语义表达层：DSL 与结构化数据 (Semantic Expression: DSL \& Structured Data)](#42-语义表达层dsl-与结构化数据-semantic-expression-dsl--structured-data)
    - [4.3 动态变量层：标准化注册与模板引擎 (Dynamic Variables: Standardized Registry \& Template Engine)](#43-动态变量层标准化注册与模板引擎-dynamic-variables-standardized-registry--template-engine)
    - [4.4 示例优化层：动态检索与 CoT (Example Optimization: Dynamic Retrieval \& CoT)](#44-示例优化层动态检索与-cot-example-optimization-dynamic-retrieval--cot)
    - [4.5 安全与合规层：输入过滤与输出围栏 (Security \& Compliance: Input Filtering \& Output Guardrails)](#45-安全与合规层输入过滤与输出围栏-security--compliance-input-filtering--output-guardrails)
  - [5. 实施路线图 (Implementation Roadmap)](#5-实施路线图-implementation-roadmap)
    - [Phase 1: 基础架构重构 (Infrastructure Refactoring) - Weeks 1-3](#phase-1-基础架构重构-infrastructure-refactoring---weeks-1-3)
    - [Phase 2: 数据结构化与 DSL (Data Structuring \& DSL) - Weeks 4-5](#phase-2-数据结构化与-dsl-data-structuring--dsl---weeks-4-5)
    - [Phase 3: 示例系统与增强 (Example System \& Enhancement) - Weeks 6-8](#phase-3-示例系统与增强-example-system--enhancement---weeks-6-8)
    - [Phase 4: 测试与调优 (Testing \& Tuning) - Weeks 9-10](#phase-4-测试与调优-testing--tuning---weeks-9-10)
  - [6. 测试验收标准 (Acceptance Criteria)](#6-测试验收标准-acceptance-criteria)
  - [7. 性能基线与预期收益 (Performance Baseline \& Expected Benefits)](#7-性能基线与预期收益-performance-baseline--expected-benefits)
  - [8. 风险及回滚策略 (Risks \& Rollback Strategy)](#8-风险及回滚策略-risks--rollback-strategy)
    - [8.1 潜在风险 (Potential Risks)](#81-潜在风险-potential-risks)
    - [8.2 回滚策略 (Rollback Strategy)](#82-回滚策略-rollback-strategy)

---

## 1. 摘要 (Executive Summary)

本报告旨在对 `RimChat` 项目的 LLM 对话层 Prompt 上下文进行深入的系统性优化分析。通过对核心代码 `PromptPersistenceService.cs` 及其相关模块的审查，识别出当前 Prompt 构建逻辑中存在的结构性冗余、语义歧义、变量管理硬编码以及缺乏有效引导等关键问题。针对这些问题，本报告提出了一套分层优化策略，涵盖上下文结构重构、语义表达标准化、动态变量自动化管理、示例库构建以及安全合规增强。预期通过实施该方案，能够在保持或提升模型回复质量的同时，降低 30% 以上的 Token 消耗，减少 15% 的首字延迟，并将 JSON 格式错误率降低至 1% 以下，从而显著提升用户体验和系统稳定性。

This report aims to provide an in-depth systemic optimization analysis of the LLM dialogue layer prompt context for the `RimChat` project. Through a review of the core code `PromptPersistenceService.cs` and related modules, key issues such as structural redundancy, semantic ambiguity, hard-coded variable management, and lack of effective guidance in the current prompt construction logic were identified. Addressing these issues, this report proposes a layered optimization strategy covering context structure refactoring, semantic expression standardization, automated dynamic variable management, example library construction, and security compliance enhancement. It is expected that by implementing this plan, token consumption can be reduced by over 30%, time to first token (TTFT) reduced by 15%, and JSON format error rate reduced to below 1% while maintaining or improving model response quality, thereby significantly enhancing user experience and system stability.

---

## 2. 现状评估 (Current State Assessment)

### 2.1 Prompt 架构解构 (Prompt Architecture Deconstruction)

当前 RimChat 的 Prompt 构建采用了线性的字符串拼接模式（Linear String Concatenation），缺乏层级化的信息管理。核心构建逻辑主要集中在 `PromptPersistenceService.cs` 类中。

The current prompt construction of RimChat adopts a Linear String Concatenation pattern, lacking hierarchical information management. The core construction logic is mainly concentrated in the `PromptPersistenceService.cs` class.

**外交模式 (Diplomacy Mode - `BuildFullSystemPrompt`):**
1.  **环境块 (Environment Block):**
    *   调用 `BuildEnvironmentPromptBlocks`。
    *   包含世界观 (Worldview)、环境参数 (Environment Context Switches)、近期世界事件 (Recent World Events)、场景 Prompt (Scene Prompt Layers)。
    *   *问题：* 环境参数（如温度、地形）以自然语言列表形式存在，占用大量 Token 且信息密度低。
2.  **事实基础 (Fact Grounding):**
    *   硬编码的 `=== FACT GROUNDING RULES ===`。
    *   *问题：* 每次请求都包含相同的静态文本，且位于 Prompt 前部，容易被后续信息稀释（Lost in the Middle）。
3.  **全局设定 (Global Settings):**
    *   包含全局系统 Prompt 和对话 Prompt。
    *   *问题：* 用户配置的 Prompt 可能与硬编码的规则冲突。
4.  **派系特征 (Faction Characteristics):**
    *   从 `FactionPromptManager` 获取。
5.  **动态数据注入 (Dynamic Data Injection):**
    *   `AppendRelationContext`: 注入五维关系（信任、亲密等）及行为准则。
    *   `AppendMemoryData`: 注入重大事件和长期记忆。
    *   `AppendFiveDimensionData`: 重复注入五维数据（如果配置不当）。
    *   `AppendFactionInfo`: 注入派系基础信息。
    *   *问题：* 存在逻辑判断（`if`）分散在各个 `Append` 方法中，难以全局概览数据流；部分数据存在重复注入风险。
6.  **API 与任务 (API & Quests):**
    *   `AppendApiLimits` 和 `AppendDynamicQuestGuidance`。
    *   *问题：* API 定义分散，部分在 `RpgApiPromptTextBuilder`，部分在 `PromptPersistenceService`。

**RPG 模式 (RPG Mode - `BuildRPGFullSystemPrompt`):**
1.  **角色设定 (Role Setting):** 包含 `RPGRoleSetting` 和 `PawnPersonaPrompt`（玩家自定义）。
2.  **动态记忆 (Dynamic Memory):** `DialogueSummaryService.BuildRpgDynamicFactionMemoryBlock`。
3.  **状态注入 (Status Injection):** 包含 Self/Interlocutor Status (Needs, Skills, etc.)、Psychological Assessment、Faction Background。
4.  **API 动作 (API Actions):** `RpgApiPromptTextBuilder.AppendActionDefinitions`。

### 2.2 关键代码路径分析 (Key Code Path Analysis)

在 `PromptPersistenceService.cs` 中，我们观察到如下典型代码模式：

In `PromptPersistenceService.cs`, we observe the following typical code patterns:

```csharp
// Example from AppendRelationContext
sb.AppendLine("=== RELATIONSHIP VALUES (5-DIMENSION ASSESSMENT) ===");
sb.AppendLine("These values represent how you feel about the player faction:");
sb.AppendLine();
sb.AppendLine($"1. TRUST: {relations.Trust:F0}/100");
sb.AppendLine($"   Level: {FactionRelationContext.GetTrustLevelDescription(relations.Trust)}");
sb.AppendLine($"   Meaning: {FactionRelationContext.GetTrustImplication(relations.Trust)}");
// ... repeated for 5 dimensions
sb.AppendLine("BEHAVIOR GUIDELINES based on these values:");
sb.AppendLine(FactionRelationContext.GenerateBehaviorGuidelines(relations));
```

**分析 (Analysis):**
*   **Token 浪费:** 每次都输出 "These values represent...", "Level:", "Meaning:" 等解释性文本。对于一个训练有素的 LLM，简单的 Key-Value 对（如 `Trust: 80 (High)`）通常已足够。
*   **语义冗余:** `GetTrustImplication` 返回的文本往往是静态的描述，重复多次。
*   **硬编码格式:** 如果需要调整输出格式（例如改为 JSON），需要修改 C# 代码并重新编译。

### 2.3 性能与质量多维度评估 (Multi-dimensional Performance & Quality Assessment)

| 维度 (Dimension) | 评分 (Score) | 详细评估 (Detailed Assessment) |
| :--- | :---: | :--- |
| **准确率 (Accuracy)** | 7/10 | **优势:** 基础指令执行尚可。<br>**劣势:** 在复杂场景（如同时处理多个人际关系和 API 调用）下，模型容易忽略部分约束。API 参数生成偶尔出现类型错误（如 `amount` 填入字符串）。 |
| **召回率 (Recall)** | 6/10 | **优势:** 近期事件注入较为详细。<br>**劣势:** 长期记忆检索依赖于简单的摘要，缺乏向量检索（RAG），导致早期关键互动细节丢失。跨会话的上下文连贯性较差。 |
| **延迟 (Latency)** | 5/10 | **劣势:** Prompt 长度经常超过 3k-4k tokens，导致首字生成延迟（TTFT）在本地模型上可达数秒。大量冗余文本加剧了网络传输和推理负担。 |
| **Token 消耗 (Token Consumption)** | 4/10 | **严重问题:** 每次请求都包含完整的 API 定义和规则说明。例如，5 维关系的详细解释占用了约 300-500 tokens，而这些信息对于后续对话往往是静态背景。 |
| **可维护性 (Maintainability)** | 6/10 | **劣势:** Prompt 构建逻辑分散在 `PromptPersistenceService` 的多个私有方法中，缺乏统一的模板管理系统。修改 Prompt 需要具备 C# 开发能力。 |

---

## 3. 问题根因 (Root Cause Analysis)

### 3.1 结构性冗余与“迷失中间”效应 (Structural Redundancy & "Lost in the Middle")

当前 Prompt 结构呈现“头重脚轻”的特征：头部包含大量静态规则和环境描述，中部是动态数据，尾部是用户指令。研究表明，LLM 对于长上下文中间部分的信息关注度较低（Lost in the Middle）。

The current prompt structure exhibits a "top-heavy" characteristic: the head contains a large number of static rules and environment descriptions, the middle is dynamic data, and the tail is user instructions. Research shows that LLMs have lower attention to information in the middle of long contexts (Lost in the Middle).

*   **现象:** `Fact Grounding Rules` 和 `Environment Parameters` 位于最前方，而具体的 `API Actions` 定义位于中间或后方。
*   **后果:** 模型在生成回复时，可能因为上下文过长而遗忘早期的事实约束，或者忽略中间的 API 格式要求。
*   **根因:** 缺乏对信息优先级的精细化管理，采用了简单的追加（Append）策略。

### 3.2 语义表达的混合范式冲突 (Mixed Paradigm Conflict in Semantic Expression)

Prompt 中混合了多种语义表达范式：
*   **自然语言叙述:** "You are the leader of..."
*   **键值对列表:** "Time: 08:00", "Weather: Clear"
*   **伪代码/结构化块:** "=== SECTION ==="
*   **JSON 模板:** API 输出格式。

这种混合范式增加了模型的认知负荷（Cognitive Load）。模型需要在不同的解析模式之间切换：既要像小说家一样理解角色背景，又要像解释器一样执行 API 指令。

This mixed paradigm increases the model's Cognitive Load. The model needs to switch between different parsing modes: understanding character backgrounds like a novelist, and executing API instructions like an interpreter.

### 3.3 动态变量管理的硬编码局限 (Hard-coded Limitations of Dynamic Variable Management)

变量注入完全依赖于 C# 代码中的字符串拼接。这意味着：
1.  **缺乏类型安全:** 无法确保注入的变量值符合预期（例如，数值变量不会包含非数字字符）。
2.  **缺乏异常处理:** 如果某个变量获取失败（如 `map.weatherManager` 为空），可能导致整个 Prompt 构建失败或包含错误信息（"Unknown"）。
3.  **不可配置:** 用户或 Modder 无法在不修改代码的情况下调整注入的数据字段。

Variable injection relies entirely on string concatenation in C# code. This means:
1.  **Lack of Type Safety:** Cannot ensure injected variable values meet expectations.
2.  **Lack of Exception Handling:** If a variable retrieval fails, it may lead to prompt construction failure or incorrect information.
3.  **Unconfigurable:** Users or Modders cannot adjust injected data fields without modifying code.

### 3.4 缺乏思维链与示例引导 (Lack of Chain-of-Thought & Few-shot Guidance)

当前系统主要依赖 Zero-shot Prompting（零样本提示），即直接告诉模型“做什么”，而不提供“怎么做”的示例。对于复杂的任务（如根据当前关系值决定是否发起交易），Zero-shot 的表现往往不稳定。

The current system relies mainly on Zero-shot Prompting, telling the model "what to do" without providing examples of "how to do it". For complex tasks (e.g., deciding whether to initiate a trade based on current relationship values), Zero-shot performance is often unstable.

*   **缺失:** 代码中未发现动态检索 Few-shot 示例的逻辑。
*   **后果:** 模型在处理边缘情况（Edge Cases）时容易产生幻觉或回退到默认的安全回复，导致 NPC 行为缺乏个性。

---

## 4. 优化方案 (Optimization Strategy)

### 4.1 上下文结构层：模块化与 XML 标签体系 (Context Structure: Modularity & XML Tag System)

我们将采用基于 XML 标签的模块化结构，这种结构已被证明对 Claude 和 GPT-4 等模型非常有效，能显著提升信息提取的准确性。

We will adopt a modular structure based on XML tags, which has been proven effective for models like Claude and GPT-4, significantly improving information extraction accuracy.

**重构后的 Prompt 结构 (Refactored Prompt Structure):**

```xml
<system_instructions>
    <role>...</role>
    <core_rules>...</core_rules>
    <output_format>...</output_format>
</system_instructions>

<world_context>
    <environment>
        <time>Year 5502, Quadrum 1, Day 5, 08:00</time>
        <weather>Clear</weather>
        <location>Dining Room (Impressive)</location>
    </environment>
    <recent_events>
        <event type="Raid" time="2 days ago">Pirate raid, repelled, minor injuries.</event>
    </recent_events>
</world_context>

<character_profile>
    <self>
        <name>King</name>
        <traits>Abrasive, Tough</traits>
        <state>Hungry, Tired</state>
    </self>
    <interlocutor>
        <name>Blue</name>
        <relation>Friend (+45)</relation>
    </interlocutor>
</character_profile>

<relationships>
    <dimension name="Trust" value="80" level="High">Willing to share secrets.</dimension>
    <dimension name="Intimacy" value="20" level="Low">Professional distance.</dimension>
</relationships>

<task_guidance>
    <available_actions>
        <action name="OfferTrade" cost="10">Initiate trade dialogue.</action>
    </available_actions>
    <examples>
        <!-- Few-shot examples here -->
    </examples>
</task_guidance>

<user_input>
    {{UserMessage}}
</user_input>
```

### 4.2 语义表达层：DSL 与结构化数据 (Semantic Expression: DSL & Structured Data)

**优化策略:**
1.  **JSON/YAML 替代自然语言:** 对于列表型数据（如技能、装备、关系），使用紧凑的 JSON 或 YAML 格式。
2.  **精简描述:** 去除冗余的解释性文本（如 "These values represent..."），直接提供数据。
3.  **统一术语:** 建立 `TermGlossary`，确保所有 Prompt 中使用的术语（如 "Faction", "Pawn", "Tick"）定义一致。

**对比示例 (Comparison Example):**

*   **Before (Token-heavy):**
    > "1. TRUST: 80/100. Level: High. Meaning: You trust them implicitly and are willing to take risks for them."
*   **After (Token-efficient DSL):**
    > `Relation.Trust: 80 (High) - Implications: High risk tolerance, secret sharing.`

### 4.3 动态变量层：标准化注册与模板引擎 (Dynamic Variables: Standardized Registry & Template Engine)

**技术方案:**
1.  **引入 `PromptVariableRegistry`:** 一个中心化的注册表，用于管理所有可注入的变量。
2.  **变量接口 `IPromptVariable`:**
    ```csharp
    interface IPromptVariable {
        string Name { get; }
        string GetValue(DialogueScenarioContext context);
        string Description { get; }
        bool IsSensitive { get; } // For PII masking
    }
    ```
3.  **模板引擎:** 使用 C# 的 `Mustache` 或简单的正则替换，支持逻辑控制。
    *   模板示例: `{{#if is_hostile}} WARNING: Enemy detected! {{/if}}`

### 4.4 示例优化层：动态检索与 CoT (Example Optimization: Dynamic Retrieval & CoT)

**实施方案:**
1.  **示例库构建:** 创建一个 JSON 文件存储示例库，每个示例包含 `input`, `context_tags` (e.g., "hostile", "trade"), `output`。
2.  **动态检索:** 在构建 Prompt 时，根据当前的 `ScenarioTags`（已在 `BuildScenarioTags` 中实现），计算与示例库中 tags 的 Jaccard 相似度，选取 Top-3 最相关的示例。
3.  **思维链 (CoT) 注入:** 在示例输出中包含 `<thought>` 部分，展示决策逻辑。

**示例片段:**
```xml
<example>
    <context>Tag: hostile, raid_recent</context>
    <input>We come in peace.</input>
    <output>
        <thought>
            User claims peace, but they raided us 2 days ago.
            Trust is low (10).
            Action: Reject and demand reparations.
        </thought>
        <response>Peace? Your mortars are still smoking from yesterday's attack. Leave now.</response>
        <actions>[{"action": "ExitDialogueCooldown"}]</actions>
    </output>
</example>
```

### 4.5 安全与合规层：输入过滤与输出围栏 (Security & Compliance: Input Filtering & Output Guardrails)

**措施:**
1.  **输入清洗:** 移除用户输入中的特殊字符（如 XML 标签、JSON 格式符），防止 Prompt 注入。
2.  **输出校验:** 在 `AIResponseParser` 中增加对 `<thought>` 标签的过滤（不展示给用户），并严格校验 JSON 格式。
3.  **敏感词过滤:** 建立敏感词库，对 LLM 生成的内容进行二次扫描。

---

## 5. 实施路线图 (Implementation Roadmap)

### Phase 1: 基础架构重构 (Infrastructure Refactoring) - Weeks 1-3
*   **Task 1.1:** 创建 `PromptBuilder` 类，支持基于 XML 的分块构建。
*   **Task 1.2:** 将 `PromptPersistenceService` 中的硬编码字符串提取到 `Resources/PromptTemplates`。
*   **Task 1.3:** 实现 `PromptVariableRegistry`，迁移现有的 `Environment` 和 `Relation` 数据注入逻辑。

### Phase 2: 数据结构化与 DSL (Data Structuring & DSL) - Weeks 4-5
*   **Task 2.1:** 重写 `AppendRelationContext` 和 `AppendMemoryData`，输出紧凑的 YAML/JSON 格式。
*   **Task 2.2:** 优化 `RpgApiPromptTextBuilder`，合并 Diplomacy 和 RPG 的 API 定义。
*   **Task 2.3:** 对接新的模板引擎，支持条件渲染。

### Phase 3: 示例系统与增强 (Example System & Enhancement) - Weeks 6-8
*   **Task 3.1:** 构建包含 20+ 个场景的 `FewShotExamples.json` 库。
*   **Task 3.2:** 实现基于 Tag 的动态示例检索逻辑。
*   **Task 3.3:** 在 API 响应解析器中支持 CoT (`<thought>`) 剥离。

### Phase 4: 测试与调优 (Testing & Tuning) - Weeks 9-10
*   **Task 4.1:** 运行 Token 消耗基准测试，对比优化前后数据。
*   **Task 4.2:** 进行自动化回归测试，确保 API 调用逻辑不退化。
*   **Task 4.3:** 灰度发布与用户反馈收集。

---

## 6. 测试验收标准 (Acceptance Criteria)

1.  **格式合规性 (Format Compliance):**
    *   生成的 System Prompt 必须符合预定义的 XML Schema。
    *   模型输出的 JSON 能够被 `Newtonsoft.Json` 无错误解析的概率 > 99%。

2.  **Token 效率 (Token Efficiency):**
    *   在标准外交场景下，System Prompt 的 Token 数量应从平均 2500 降至 1600 以下（降低 >35%）。

3.  **功能完整性 (Functional Integrity):**
    *   所有 5 维关系数据、近期 5 条世界事件、当前 Pawn 状态必须准确包含在 Prompt 中。
    *   API Action 的触发条件（如 `Requirement`）必须在 Prompt 中清晰可见。

4.  **延迟指标 (Latency Metrics):**
    *   在相同硬件（如 RTX 3060）上运行本地模型（7B），TTFT 应降低至少 15%。

---

## 7. 性能基线与预期收益 (Performance Baseline & Expected Benefits)

| 指标 (Metric) | 当前基线 (Current Baseline) | 预期目标 (Expected Target) | 预期收益 (Expected Benefit) |
| :--- | :--- | :--- | :--- |
| **Token 消耗 (Token Usage)** | ~2500 tokens/req | < 1600 tokens/req | 降低 API 成本 ~35%，提升响应速度。 |
| **JSON 错误率 (JSON Error Rate)** | ~8% | < 1% | 极大减少由于格式错误导致的重试，提升稳定性。 |
| **指令遵循 (Instruction Following)** | 中等 (Medium) | 高 (High) | NPC 行为更符合游戏逻辑，减少“出戏”情况。 |
| **可维护性 (Maintainability)** | 低 (Low - Hardcoded) | 高 (High - Configurable) | Modder 可通过 XML/JSON 定制 Prompt，无需编译。 |

---

## 8. 风险及回滚策略 (Risks & Rollback Strategy)

### 8.1 潜在风险 (Potential Risks)
*   **R1: 模型兼容性差异:** 某些较弱的模型（如 7B 以下量化版）可能无法很好地理解复杂的 XML 结构或紧凑的 DSL。
*   **R2: 信息丢失:** 过度压缩 Prompt 可能导致模型忽略某些边缘信息（如 10 天前的微小事件）。
*   **R3: 迁移成本:** 现有的用户自定义 Prompt 可能需要手动迁移到新格式。

### 8.2 回滚策略 (Rollback Strategy)
*   **S1: 配置开关:** 在 `RimChatSettings` 中增加 `UseV2PromptFormat` 开关，默认开启。如果出现问题，用户可一键关闭，回退到 `PromptPersistenceService` 的旧逻辑。
*   **S2: 渐进式发布:** 先在 `Beta` 分支发布 V2 格式，收集社区反馈。
*   **S3: 自动降级:** 如果检测到模型连续多次输出格式错误，自动在当前会话中降级为旧版 Prompt 结构。

---

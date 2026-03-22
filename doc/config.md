# RimChat 外部配置说明（v0.7.76）

## Prompt Workspace 预览构建策略（v0.7.76）

- 本版本无新增用户可调开关，行为为固定策略：
  - 工作台预览自动重建（无需手动刷新）；
  - 预览构建按阶段增量执行：`Init -> Sections -> Nodes -> Finalize`；
  - 单帧执行预算固定为 2ms；
  - 发生模板渲染异常时 fail-fast 停止后续阶段，但保留已完成内容并显示错误定位信息。
- 状态可观测：
  - 预览顶部显示阶段、总进度、section 进度、node 进度；
  - 失败时显示 template/channel/line/column/message。
- 兼容说明：
  - 仅影响 Prompt Workspace 预览链路；
  - 不影响运行时系统提示词主链路和旧存档数据结构。

## 对话生命周期 fail-fast 规则（v0.7.70）

- 本版本无新增用户可调开关，行为改为固定策略：
  - 开窗前上下文失效：拒绝开窗。
  - 发请求前上下文失效：拒绝发送。
  - 回包落地前上下文失效：丢弃回包并写系统提示。
  - 动作执行前上下文失效：中止动作执行（不做部分执行）。
- 重复窗口策略：
  - 同一会话窗口键命中重复时，拒绝新开窗口。
- 系统提示键：
  - `RimChat_DialogueResponseDropped`（中英双语，含 reason 占位）。

## RPG 动作合同缺失保护与自动记忆收敛（v0.7.67）

- 生效范围：
  - `rpg_dialogue`
  - `proactive_rpg_dialogue`
- 固定规则（无新增外部配置项）：
  - 当 `EnableRPGAPI=true` 且本轮 RPG 系统提示词未检测到动作合同正文时：
    - 记录运行时告警；
    - 仅关闭“本轮自动记忆兜底与自动记忆映射”；
    - 保留退出类兜底（`ExitDialogue` / `ExitDialogueCooldown`）。
  - 自动记忆来源（协作映射/轮次兜底/无动作连击兜底）限制为“单会话最多一次”。
  - 模型显式返回的 `TryGainMemory` 动作保持可执行，不受自动门控计数影响。
  - 协作意图映射改为明确承诺短语触发，不再使用高歧义短词触发。

## 对话风格与主动推送截断治理（v0.7.59）

- 新增配置：
  - `DialogueStyleMode`：`natural_concise | balanced | immersive`（默认 `natural_concise`）。
  - `ProactiveMessageHardLimit`：主动推送硬截断上限，默认 `0`（不截断）。
  - `ExpectedActionDenyLogLevel`：预期拒绝类动作日志等级，默认 `Info`。
- 行为变更：
  - 主动推送文本清洗后不再固定按 260 字硬截断；仅当 `ProactiveMessageHardLimit > 0` 时按该值截断。
  - 动作被业务规则拒绝（如 cooldown/blocked/validation）默认按 Info 记录，不再默认归类为异常告警。

## API 可用性测试体验修复（v0.7.58）

- 按钮布局：
  - `测试连通性` 与 `测试可用性` 调整为同一行等宽按钮（50/50），执行期间统一禁用。
- 本地诊断阶段：
  - 本地模型深度测试改为 4 步：
    - 配置校验
    - 本地服务探测（Ollama/OpenAI 兼容）
    - 最小 chat 实测
    - 响应契约校验
  - 不再以“模型列表命中”作为本地失败硬门槛。
- 成功结果文案：
  - 显示耗时 + 速度评级（极快/快/正常/慢/极慢）。
  - 当速度评级为“极慢”时，额外提示“连接质量较差，建议更换服务商”。
- 失败结果文案：
  - 维持原格式（不追加速度评级）。

## API 双测试按钮（v0.7.57）

- 入口位置：`Mod 设置 -> API`
- 按钮说明：
  - `测试连通性`：快速探测可达性（轻量，保留旧行为语义）。
  - `测试可用性`：深度分步诊断（fail-fast，默认包含最小真实请求实测）。
- 深度测试阶段（云端）：
  - 配置校验
  - 运行时端点解析
  - `/models` 探测
  - 模型存在性校验
  - 最小 chat 实测
  - 响应契约校验
- 深度测试阶段（本地）：
  - 配置校验
  - 本地服务探测（Ollama/OpenAI 兼容）
  - 最小 chat 实测
  - 响应契约校验
- 结果展示：
  - 成功：显示总耗时。
  - 失败：显示标准错误分类 + 操作建议（1-2 条）+ 技术细节折叠区。
  - 失败时提供“日志观测”跳转按钮，不自动弹窗。

## 工作台 Prompt Bundle 导入协议（Fail Fast）

- 工作台导入仅接受 `RimChat_PromptBundle.json`（Prompt Bundle 协议）。
- 导入入口会先做结构硬校验，必须同时满足：
  - 包含 `BundleVersion`
  - 包含 `IncludedModules`
  - 至少包含一个 Bundle payload 字段（例如 `SystemPrompt` / `DiplomacyDialoguePrompt` / `PawnDialoguePrompt` / `SocialCirclePrompt` / `FactionPromptsJson`）
- 若检测到 `Presets` / `ChannelPayloads` / `UnifiedPromptCatalog`，会判定为 `PromptPresets` 文件并立即拒绝导入。
- 非 Bundle JSON（或非法 JSON / 空文件）会立即失败并返回明确错误原因，不再回退“直接导入默认对象”。
- 导出协议保持不变，默认输出 `RimChat_PromptBundle.json`。

## 提示词工作台单真源配置语义（v0.7.55）

- 统一真源：
  - 工作台正式编辑只写 `PromptUnifiedCatalog`（sections + nodes + node layout + template aliases）。
  - `PromptSectionCatalog` 不再作为正式可写源，仅保留 legacy 导入/导出用途。
- 工作台保存时机：
  - section/node/node-layout 编辑阶段只更新内存 unified。
  - 仅显式点击工作台 `Save` 才写 `PromptUnifiedCatalog_Custom.json` 与当前 preset 持久化。
  - 切换 section/node/channel/preset 不触发自动落盘。
- preset payload：
  - 正式字段移除 `PromptSectionCatalog`，只保留 `UnifiedPromptCatalog`。
  - 旧 preset 文件若包含 section 字段，导入时会单向迁移到 unified，之后不再回写旧字段。
- RPG custom store：
  - `Prompt/Custom/PawnDialoguePrompt_Custom.json` 不再持久化 `PromptSectionCatalog`。
  - 旧字段仅在迁移读取阶段生效，读取后并入 unified。
- 读取副作用治理：
  - 预览/UI 刷新链路走 `LoadConfigReadOnly()`，禁止“仅预览触发修复写盘”。
  - 需要修复写回时改用 `RepairAndRewritePromptDomains()` 显式触发。

## 提示词工作台预设与编辑器交互（v0.7.54）

- 预设默认只读（无新增外部配置项）：
  - 默认预设由存储层 `DefaultPresetId` 稳定标识。
  - 在默认预设上首次发生编辑意图（文本改动、行内重命名、节点结构调整、快捷动作写入、Reset 写入）时，自动分叉为新预设并切换编辑对象。
  - 自动分叉名称格式：`Custom yyyyMMdd-HHmmss`。
- 预设列表交互：
  - 保留顶部 `新建/复制`。
  - 列表行新增快捷操作：复制、删除。
  - 双击预设名可行内重命名（Enter/失焦保存，Esc 取消）。
  - 默认预设删除入口禁用；双击默认预设名会先自动分叉再进入重命名。
- 主编辑器动作栏：
  - 工具栏固定为 `Undo / Redo / Save / Reset`。
  - Undo/Redo 历史按“预设+通道+分段/节点”隔离，不跨对象串历史。
  - Save 为立即持久化，Reset 仅重置当前分段/当前节点，不影响其他对象。
  - fail-fast 切换约束：分段/通道/节点/预设切换前必须先完成强制保存；若保存链路返回失败，当前切换会被阻断，避免回滚到旧文本。
  - 预设同步失败时，编辑态保持 pending，不允许继续切换直到保存链路恢复成功。
- 兼容性：
  - 旧 preset store 缺失 `DefaultPresetId` 时会自动归一化迁移。
  - 不新增业务提示词字段，不修改旧存档主结构。

## RPG Pawn 根绑定语义（v0.7.53）

- 生效范围：
  - `rpg_dialogue`
  - `proactive_rpg_dialogue`
  - `persona_bootstrap`
  - `rpg_archive_compression`
- 固定规则（无新增开关）：
  - 运行时原生渲染将显式携带 `promptChannel`，按通道语义绑定 `CurrentPawn / Pawns / AllPawns / ScopedPawnIndex`。
  - `rpg_archive_compression` 固定使用 archive NPC 作为主体 pawn（`Target`）。
  - archive interlocutor 若无法解析真实 pawn：仅绑定 NPC 主体并输出强告警日志，不再伪造 `(null, null)` 场景。
  - 若 prompt 含 `{{ pawn.` 且 `CurrentPawn` 为空：记录明确错误日志，但保持“告警继续”策略（不中断本次渲染）。
- 兼容性：
  - 不新增外部配置项，不改存档结构，不新增回退链。

## RPG RimTalk 原生渲染行为（v0.7.52）

- 生效范围：
  - 手动 RPG 对话
  - 主动 RPG 推送
  - NPC 人格生成
  - RPG 存档压缩摘要
- 运行时行为：
  - RimChat 先完成自身业务 prompt 拼装；
  - 之后把最终 RPG prompt 交给 RimTalk 原生 `ScribanParser.Render(...)` 解析 raw token。
- 预览行为：
  - 设置页 / Workbench 预览保留 RimTalk raw token，不做原生解析。
- 失败行为：
  - RimTalk 原生渲染失败只记录日志，不阻断正式请求；
  - 不再回退到旧的逐变量模拟渲染值。
- raw token 生成：
  - Pawn 自定义变量插入为 `{{ pawn.xxx }}`
  - 其他自定义变量插入为 `{{ xxx }}`

## RimTalk 自定义变量刷新与诊断（v0.7.51）

- 快照刷新策略：
  - 自定义变量快照改为节流刷新（默认冷却 1000ms），不再依赖“仅首轮空快照时刷新”。
- 自动填充触发增强：
  - `mod_variables` 自动填充执行前会强制刷新 RimTalk 快照，减少加载时序导致的空白。
- 浏览器一致性：
  - 变量浏览器刷新前会同步 RimTalk 自定义变量快照，确保浏览器与 section 填充来源一致。
- 诊断日志：
  - 启动/刷新日志包含 `raw_count`、`parsed_count`、`duplicate_count`、`force`。
- fail-fast 约束：
  - 当 RimTalk 返回原始变量集合非空但解析结果为 0 时，仅阻断 Bridge 链路并记录错误，RimChat 非 Bridge 功能继续可用。

## RimTalk 变量桥接与工作台 `mod_variables`（v0.7.50）

- 工作台主链新增 section：
  - `mod_variables`
  - 覆盖全部可编辑通道；默认值为空字符串（不强制注入固定文本）。
- 自动填充触发条件（幂等）：
  - 仅当 `mod_variables` 当前为空；
  - 且检测到 RimTalk `GetAllCustomVariables()` 可用结果；
  - 自动写入 raw token 列表（每行一个 `{{ variable }}`）。
- 填充来源范围：
  - 仅 RimTalk 自定义变量，不包含 RimChat core/builtin 变量。
- 用户编辑保护：
  - `mod_variables` 非空时不被自动覆盖。
- 桥接 fail-fast 行为：
  - RimTalk 在场但关键签名缺失时，仅桥接链路阻断；RimChat 非桥接功能继续可用。

## create_quest 与 RPG 场景参数默认升级（v0.7.48）

- RPG 场景参数默认值（`Prompt/Default/SystemPrompt_Default.json`）：
  - `IncludeNeeds: true`（由 false 调整）
  - `IncludeRecentJobState: true`（由 false 调整）
  - `IncludeHediffs` 维持 `true`
- 旧配置迁移（自动、幂等）：
  - 仅当 `RpgSceneParamSwitches` 命中历史默认签名时，才自动将 `IncludeNeeds` 与 `IncludeRecentJobState` 升级为 `true`。
  - 若用户已做个性化配置，不覆盖用户值。
- 任务模板引导模板：
  - `QuestGuidanceNodeTemplate` 默认值改为 `{{ dialogue.quest_guidance_body }}`，确保运行时注入动态可用任务列表。
- RPG 关系画像模板：
  - 新增 `pawn.relation.social_summary` 注入位，输出双方社交关系摘要。

## 提示词工作台派系模板入口与背景迁移（v0.7.47）

- 快捷入口调整：
  - 工作台顶部 `派系提示词` 按钮改为派系模板编辑入口（打开 `FactionPromptManager` 配置列表并进入 `Dialog_FactionPromptEditor`）。
  - `人设提示词` 快捷入口保留。
- 人设快捷保存后的自动注入：
  - 保存后自动尝试将 `{{ pawn.personality }}` 注入当前通道 `character_persona` 分段。
  - 已存在时跳过，不会重复插入。
- 新增运行时变量：
  - `world.faction.description`：当前对话派系的有效提示词文本块（默认 + 自定义覆盖）。
- 背景段落迁移策略：
  - 默认资产已在 `any/system_rules` 追加背景段落。
  - Unified 目录迁移版本升级到 `3`；迁移仅在缺失时追加，不覆盖用户已有 `system_rules` 文本。

## RPG关系画像去重与亲缘 no 规则收口（v0.7.44）

- 生效范围：
  - 手动 RPG（`RpgDialogue`）与主动 RPG（`ProactiveRpgDialogue`）统一生效。
- 固定规则（无新增开关）：
  - 关系画像中的 `引导` 行改为条件渲染：仅当 `dialogue.guidance` 非空时显示。
  - `kinship=no` 时不再注入亲缘边界限制文本，因此关系画像也不显示 `引导` 行。
  - `kinship=yes` 时仍保留 RomanceAttempt / Date / MarriageProposal 的边界限制提示，但只出现一次（不再有独立重复节点）。
- 迁移规则（自动、幂等）：
  - 统一提示词目录归一化阶段会自动将旧模板中的
    - `引导：{{ dialogue.guidance }}`
    - `Guidance: {{ dialogue.guidance }}`
    升级为条件渲染写法。
  - 旧存档与旧自定义模板无需手工迁移。
- 兼容性：
  - 不新增外部配置字段，不修改存档结构，不改 Def/Harmony Patch 目标。

## 日志观测入口与趋势窗口口径调整（v0.7.43）

- 生效范围：
  - `日志观测`窗口（`Dialog_ApiDebugObservability`）
  - `设置 -> 调试设置 -> 日志观测`入口按钮 tooltip
- 固定规则（无新增开关）：
  - Token 趋势窗口固定为“最近 30 分钟、1 分钟粒度”。
  - 趋势统计口径保持“全部来源”（外交/RPG/后台来源统一统计）。
  - 日志观测窗口头部新增“设置”按钮，直接打开 RimChat 模组设置页。
- 本地化键变更：
  - 更新：
    - `RimChat_OpenApiDebugWindowButtonTooltip`
    - `RimChat_ApiDebugTrendTitle`
    - `RimChat_ApiDebugNoData`
  - 新增：
    - `RimChat_ApiDebugOpenSettingsButton`
    - `RimChat_ApiDebugOpenSettingsButtonTooltip`
    - `RimChat_ApiDebugOpenSettingsFailed`
- 兼容性：
  - 不新增外部配置字段，不改存档结构。

## RPG Prompt Memory 缓存（v0.7.43，非配置项）

- 生效范围：
  - RPG 对话系统提示词里的 NPC 个人记忆块构建链路。
- 固定规则（无开关）：
  - `BuildPromptMemoryBlock(...)` 启用版本戳缓存，按 `target/interlocutor/summary 参数` 维度复用结果。
  - 在 turn 写入、会话 finalize、外交摘要写入、读档重建、会话压缩成功/失败后自动失效缓存。
- 目标：
  - 降低 RPG 对话触发时主线程重复重建成本，避免开窗卡顿峰值。

## Think 标签双层过滤收口（v0.7.42，非配置项）

- 生效范围：
  - AI 文本提取入口（服务层）
  - 可见文本渲染入口（UI 层）
- 固定规则（无开关）：
  - 删除 `<think>...</think>` 与 `<thinking>...</thinking>` 整段内容。
  - 若存在未闭合的起始标签，删除该标签及其后续内容。
  - 移除孤立闭合标签（如 `</think>`）。
- 链路位置：
  - `RimChat/AI/AIJsonContentExtractor.cs` `TryExtractPrimaryText(...)`
  - `RimChat/AI/ImmersionOutputGuard.cs` `ValidateVisibleDialogue(...)`
  - `RimChat/AI/AIResponseParser.cs` `NormalizeDialogueText(...)`
- 兼容性：
  - 不新增用户设置项，不改变存档结构，不修改游戏本体文件。

## Pawn↔Pawn 右键对话战斗态拦截（v0.7.40，非配置项）

- 生效范围：
  - Pawn 对 Pawn 的右键 RimChat 对话入口。
- 固定规则（无开关）：
  - 当前 pawn 或目标 pawn 任一满足以下条件，即不显示右键对话项：
    - `Drafted == true`
    - `CurJob.def` 属于 `Wait_Combat / AttackMelee / AttackStatic / UseVerbOnThing`
- 执行层防绕过：
  - 即使菜单已点击，在 Job 打开对话窗口前仍会再次执行同一判定；命中后直接中止打开。
- 兼容性：
  - 不新增外部设置项，不改变存档结构，不修改游戏本体文件。

## 响应契约节点占位符收口（v0.7.35）

- 生效范围：
  - Prompt Workbench `响应契约` 节点（`response_contract_node_template`）
- 行为变化：
  - 默认模板改为 `{{ dialogue.response_contract_body }}`，移除包裹说明文本。

## 任务规则节点文本化（v0.7.34）

- 生效范围：
  - Prompt Workbench `任务规则` 节点（`quest_guidance_node_template`）
- 行为变化：
  - 节点默认值改为纯文本，不再依赖 `{{ dialogue.quest_guidance_body }}` 变量占位。
  - 旧存档若仍保存 legacy 变量 token，运行时会先解析成当前任务规则正文再输出，保证兼容与可读性。
- 校验变化：
  - 节点变量校验上下文移除该节点注入变量声明，按文本节点处理。

## Prompt Workbench 预览标签整理（v0.7.30）

- 生效范围：
  - Prompt Workbench 节点编辑列表
  - Prompt Workbench 右侧结构化预览
- 行为变化：
  - 三个节点显示名改为：
    - `接口限制`
    - `任务规则`
    - `响应契约`
  - 正文块标题统一显示为 `正文` / `Body`
  - `思维链` 在正文块之后显示
  - 预览不再附加 `主链前 / 主链后 / &lt;section_id&gt;` 这类技术标签

## 三段节点模板恢复为 Scriban 动态正文（v0.7.28）

- 生效范围：
  - `PromptTemplates.ApiLimitsNodeTemplate`
  - `PromptTemplates.QuestGuidanceNodeTemplate`
  - `PromptTemplates.ResponseContractNodeTemplate`
- 默认模板行为：
  - 三段默认文本统一改为“标题 + 运行时变量正文”，正文变量分别为：
    - `{{ dialogue.api_limits_body }}`
    - `{{ dialogue.quest_guidance_body }}`
    - `{{ dialogue.response_contract_body }}`
- 运行时来源不变：
  - `dialogue.api_limits_body` -> `AppendApiLimits(...)`
  - `dialogue.quest_guidance_body` -> `AppendDynamicQuestGuidance(...) + AppendQuestSelectionHardRules(...)`
  - `dialogue.response_contract_body` -> `AppendAdvancedConfig(...) / AppendSimpleConfig(...)`
- fail-fast 约束：
  - 三段正文任一为空时，节点渲染直接抛 `PromptRenderException(TemplateMissing)`，禁止静默降级。
- 自动迁移：
  - 加载配置时会识别三段历史“说明文硬文本”并自动改写为 Scriban 模板；非目标模板不改。
  - 命中迁移会输出日志，便于确认旧存档已完成迁移。

## Social News JSON 合同收敛（v0.7.27）

- 生效范围：
  - `Prompt/Default/PromptUnifiedCatalog_Default.json` 的 `social_news_style/social_news_json_contract/social_news_fact`。
  - `RimChat/Config/PromptUnifiedDefaults.cs` 对应回退节点。
- 行为规则：
  - `social_circle_post` 通道的社交新闻生成合同改为严格 JSON 卡片结构：
    - 必填：`headline`、`lead`、`cause`、`process`、`outlook`
    - 可选：`quote`、`quote_attribution`
  - 默认资产与代码回退统一引用同一套社交模板文本，避免通道只要求“返回 JSON”而未声明必填键。
- 配置影响：
  - 不新增用户开关；该修复属于统一目录默认值纠偏。

## Strict Workbench WYSIWYG（v0.7.26）

- 目标行为：
  - AI 请求系统提示词改为严格“工作台所见即所得”拼接，不再依赖运行时环境/动态注入结果。
- 生效范围：
  - 外交主对话、RPG 主对话、外交策略建议，以及 social/persona/summary/archive/image 等统一通道。
- 发送层约束：
  - 不再对 system-only 请求隐式补充 `user` 文本。
  - 不再对 HTTP 400 user-input-rejected 做降载重写重试，避免二次改写 payload。
- 配置项变化：
  - 本次不新增外部开关，按统一严格模式生效。

## Prompt Unified Catalog 生命周期一致性（v0.7.25）

- 生效范围：
  - `PromptUnifiedCatalog` 的节点读写与节点布局读写路径。
  - `RimChatSettings_RimTalkCompat.EnsureUnifiedCatalogReady()` 启动归一化流程。
- 行为规则：
  - 节点链路改为严格通道判定，未知/空 `promptChannel` 不再静默回退 `any`。
  - 非法 `nodeId` 或“节点不属于当前通道 allowlist”时，`ResolveNode/ResolveNodeLayout/SetNode/SetNodeLayout` 立即抛异常（fail-fast）。
  - 启动归一化改为报告模式：清洗结果通过 `PromptUnifiedCatalogNormalizeReport` 汇总，避免逐条错误日志噪音。
- 保存策略：
  - 启动保存条件改为：`legacy迁移变化 || migration版本变化 || normalizeReport.HasStructuralChange`。
  - 旧配置首次加载会自动清洗并保存一次，后续同配置幂等，不重复清洗写盘。
- 日志分层：
  - `Error`：不变量破坏且阻断运行（抛异常前）。
  - `Warning`：自动清洗摘要（未知通道、移除非法节点/布局）。
  - `Info`：默认布局补全数量、迁移完成提示。
- 兼容性：
  - 不新增用户配置项，不修改游戏本体文件。
  - 旧存档兼容通过“一次清洗 + 一次保存”实现，不采用静默容错读取。

## 请求消息最小 user 契约（v0.7.24）

- 生效范围：
  - 运行时所有 AI 请求入口（异步 `AIChatServiceAsync` + 同步 `AIChatService`）。
- 行为规则：
  - 若请求消息列表非空但不存在有效 `user` 消息，发送前会自动补一条最小 `user` 指令。
  - 非法 `role` 会在发送前归一为 `user`，避免 provider 参数校验拒绝。
- 配置影响：
  - 不新增用户配置项；该规则为运行时固定 fail-fast 安全约束，优先保证请求可执行性。
- 摘要链路：
  - `summary_generation` 与 `rpg_archive_compression` 现在始终按 system+user 发送，不再依赖 provider 对 system-only 的容忍行为。

## Workbench 所见即所得并轨配置（v0.7.22）

- 统一提示词主源：
  - 运行时与工作台统一读取 `PromptUnifiedCatalog`（`sections + nodes + templateAliases`）。
  - 对外读取网关：
    - `ResolvePromptSectionText(promptChannel, sectionId)`
    - `ResolvePromptNodeText(promptChannel, nodeId)`
    - `ResolvePromptTemplateAlias(promptChannel, templateId)`
    - `ResolvePreferredPromptTemplateAlias(promptChannel, preferredTemplateId)`
- 迁移门控：
  - `PromptUnifiedCatalog.migrationVersion` 升级为 `2`。
  - 首次加载会一次性导入：
    - legacy RPG 自定义文本（`RpgPromptCustomStore`）
    - legacy 图像模板（`DiplomacyImagePromptTemplates`）
  - 迁移完成后不重复迁移，运行时只读 Unified。
- 图像模板别名层：
  - 通道：`image_generation`
  - 支持历史 `template_id` 命中与默认回退。
  - 兼容别名可在 unified catalog 的 `channels[].templateAliases[]` 维护。
- 旁路链路配置收口：
  - `social_circle_post` / `persona_bootstrap` / `summary_generation` / `rpg_archive_compression` 现在都走统一通道配置并输出单条 system 消息。
- 工作台通道映射：
  - 可选通道集合与运行时落地通道由 `PromptSectionSchemaCatalog` 统一定义，不再在 UI 与运行时各自维护硬编码列表。

## Prompt Workbench 校验/预览/保存策略（v0.7.21）

- 运行时一致校验：
  - 工作台校验使用运行时变量目录与节点注入变量上下文，不再仅依赖静态白名单。
  - 策略节点注入变量现可被严格识别：
    - `dialogue.strategy_player_negotiator_context_body`
    - `dialogue.strategy_fact_pack_body`
    - `dialogue.strategy_scenario_dossier_body`
- 右侧预览：
  - 使用结构化块视图（最终拼接顺序）替代只读 chip 预览。
  - 顺序固定：`上下文 -> 槽位节点 -> 主链分段 -> 结尾`。
  - 主链分段聚合块会按 section 渲染次级标题，便于快速定位分段正文。
- 工作台落盘策略：
  - 编辑文本先写入内存缓冲；
  - 500ms 输入空闲后自动落盘；
  - 切频道/切分段/切节点/切模式与关闭工作台窗口会强制落盘。
  - 不改变存档结构，仅调整写盘时机。

## Unified Node Layout（v0.7.19）

- 配置位置：
  - `Prompt/Custom/PromptUnifiedCatalog_Custom.json` -> `channels[].nodeLayout[]`
- `nodeLayout` 字段：
  - `nodeId`：节点 ID（如 `fact_grounding`）
  - `slot`：节点插槽（5 固定值）
    - `metadata_after`
    - `main_chain_before`
    - `main_chain_after`
    - `dynamic_data_after`
    - `contract_before_end`
  - `order`：同槽位顺序（从小到大）
  - `enabled`：是否启用该节点
- 兼容策略：
  - 旧配置若没有 `nodeLayout`，启动时会自动按历史顺序补齐并保存；
  - 节点文本内容与布局元数据分离，编辑节点文本不会覆盖排序信息。
- 工作台行为：
  - 节点模式下可拖拽重排、上下移动、切换槽位、启用/禁用；
  - 右侧预览显示节点落点顺序与完整拼装预览。

## Unified Prompt Catalog（v0.7.18）

- 运行时提示词唯一真源：
  - `PromptUnifiedCatalog`（sections + nodes）
- 默认/自定义路径：
  - `Prompt/Default/PromptUnifiedCatalog_Default.json`
  - `Prompt/Custom/PromptUnifiedCatalog_Custom.json`
- 兼容行为：
  - 首次加载自动将 legacy `PromptSectionCatalog + PromptTemplates` 迁移到 unified catalog；
  - 迁移后提示词运行链路不再依赖 legacy prompt template 域。
- 工作台：
  - 新增 `Sections / Nodes` 编辑模式；
  - 节点可按通道编辑，支持变量插入、恢复默认、节点预览。
- 通道覆盖：
  - 外交：`diplomacy_dialogue / diplomacy_strategy / proactive_diplomacy_dialogue / social_circle_post / summary_generation / image_generation`
  - RPG：`rpg_dialogue / proactive_rpg_dialogue / persona_bootstrap / summary_generation / rpg_archive_compression`

## 提示词分段快捷专属提示词（v0.7.17）

- 设置入口：
  - `设置 -> 提示词分段` 工作台头部新增 `派系提示词`、`人设提示词`。
- 可用条件：
  - 仅在已加载存档且处于游戏内时可用；
  - 游戏外按钮禁用，并提示需要读取当前实例。
- 固定快捷变量：
  - `system.custom.quick_faction_persona`
  - `system.custom.quick_pawn_persona`
- 派系提示词：
  - 读取当前存档真实派系列表（含玩家派系）；
  - 保存为统一用户变量系统里的 `Faction Rule`。
- 人设提示词：
  - 读取当前存档里的玩家殖民者、驯化动物、机械体实例；
  - 保存为统一用户变量系统里的 `Pawn Rule`；
  - `NameExact` 允许保存为 `thingid:*`，用于按真实实例精确命中。
- 保存行为：
  - 只创建或更新规则，不自动把 token 插入正文；
  - 保存后会提示可插入 token，并把工作台焦点切到 `character_persona` 分段。
- 冲突处理：
  - 若固定快捷变量路径已被现有用户变量占用，会弹出复用/接管选择；
  - 默认建议复用现有变量容器继续写规则。

## 统一用户变量规则集 + 安全人格导出（v0.7.16）

- 用户可写命名空间仍然只有：
  - `system.custom.{key}`
  - `key` 保存时会规范化为小写 slug，只允许 `a-z0-9_`。
- 持久化字段升级为三层：
  - `UserDefinedPromptVariables`
    - 每项字段：`Id`、`Key`、`DisplayName`、`Description`、`DefaultTemplateText`、`Enabled`
  - `UserDefinedPromptVariableFactionRules`
    - 每项字段：`Id`、`VariableKey`、`FactionDefName`、`Priority`、`TemplateText`、`Enabled`、`Order`
  - `UserDefinedPromptVariablePawnRules`
    - 每项字段：`Id`、`VariableKey`、`NameExact`、`FactionDefName`、`RaceDefName`、`Gender`、`AgeStage`、`TraitsAny`、`TraitsAll`、`XenotypeDefName`、`PlayerControlled`、`Priority`、`TemplateText`、`Enabled`、`Order`
- 旧配置兼容：
  - 旧 `FactionScopedPromptVariableOverrides` 仍可读取；
  - 读取后会自动迁移成新的 faction rule；
  - 后续保存只写新规则模型，不再写回旧字段。
- 运行时解析：
  - 变量目录中仍只显示一份 `system.custom.xxx` 路径；
  - 命中顺序固定为：
    - `pawn exact`
    - `pawn conditional`
    - `faction`
    - `default template`
    - 空字符串
  - 同层排序固定为：
    - `Priority` 从高到低
    - `Specificity` 从高到低
    - `Order` 从小到大
- Pawn 规则匹配范围：
  - RPG 场景优先匹配 `Target`；
  - `Target` 为空时回退 `Initiator`；
  - 外交场景若没有可用 Pawn，则 Pawn 规则直接跳过。
- `pawn.personality` 安全开放：
  - 原始人格仍来自现有运行时数据；
  - `system.custom.pawn_personality_override` 可安全覆盖导出的 prompt 值；
  - `system.custom.pawn_personality_append` 会在有效人格后追加文本；
  - 底层游戏数据不改，只改 prompt render 导出值。
- 编辑与校验：
  - 用户变量、Faction Rule、Pawn Rule 都支持 strict Scriban 命名空间变量；
  - 保存时会拦截：
    - 重复 key
    - 路径冲突
    - 无效 `FactionDefName / RaceDefName / XenotypeDefName / TraitDefName / AgeStage / Gender / PlayerControlled`
    - Scriban 编译错误
    - 未知变量引用
    - 自定义变量循环依赖
- 删除规则：
  - 删除变量前会扫描 `PromptSectionCatalog`、RimTalk compat、persona copy、其他自定义变量默认模板、Faction Rule、Pawn Rule；
  - 若发现引用则阻止删除并提示来源；
  - 删除全局变量会同时删除它的全部 faction/pawn 规则。
- UI：
  - 共享变量浏览器支持“空白变量 + 官方示例变量”创建入口；
  - 编辑器拆成“基础信息 / 默认模板 / 规则列表”；
  - 规则列表分 `Faction Rules` 与 `Pawn Rules` 两个页签；
  - `system.custom.*` 继续支持编辑/删除；
  - 内置变量与桥接变量继续只读。

## 外交 Prompt 运行时收口 + XML-like Section Envelope（v0.7.14）

- 外交运行时正式 prompt 源：
  - 普通外交：只走 `BuildFullSystemPrompt(...)`，只发送 1 条 system prompt。
  - 策略建议：只走 `BuildDiplomacyStrategySystemPrompt(...)`，也只发送 1 条独立 system prompt。
  - 网络层不再追加 `Think step by step.` / `Review your rules.`。
- 正式规则文本主源：
  - `Prompt/Default/PromptSectionCatalog_Default.json`
  - 外交与 `diplomacy_strategy` 的长自然语言规则现在统一维护在 section catalog 中。
- 兼容镜像字段：
  - `GlobalSystemPrompt`
  - `GlobalDialoguePrompt`
  - `UseHierarchicalPromptFormat`
  - 以上字段降级为 compatibility mirror，不再决定运行时 prompt 结构。
- 默认 `SystemPrompt_Default.json`：
  - `GlobalSystemPrompt` 仅保留 compatibility mirror 提示文本，正式运行时不再读取其中的大段规则包。
- 最终成品格式：
  - 固定为 XML-like envelope。
  - `main_prompt_sections` 不再输出 `[SECTION: ...]` 文本，而是输出 `<system_rules>`、`<character_persona>`、`<action_rules>` 等稳定 section 子节点。
  - 运行时成品 prompt 不再携带 `[FILE]` / `[CODE]` 源标签污染。
- 普通外交与策略建议分流：
  - 普通外交不再追加 `PLAYER NEGOTIATOR CONTEXT` 第二条 system。
  - 策略建议专属的谈判者上下文、fact pack、scenario dossier 只进入策略 builder。

## Default Prompt 变量系统收敛 + 社交圈子通道（v0.7.13）

- Prompt 主链默认文案：
  - 继续以 `Prompt/Default/PromptSectionCatalog_Default.json` 作为主入口。
  - 外交 root 的 section 工作台现在额外支持 `social_circle_post` 子通道，可单独编辑和预览其 8 段主链文案。
- 社交圈专用模板默认值：
  - `Prompt/Default/SocialCirclePrompt_Default.json` 现在作为 `publish_public_post` 默认动作说明与社交圈新闻模板的正式默认来源。
  - 代码中的同类默认文本只保留最小安全回退，不再作为长期主来源。
- 变量缺口记录：
  - 旧 default prompt 中尚未进入现有 namespaced variable system 的语义位点，记录在 `doc/PromptVariableGapReport.md`。
  - 本次不会自动补 provider，也不会用硬编码兜底替代这些缺口。

## RimTalk 基础 Scriban 安全补齐（v0.7.6）

- 新增可直接从原生游戏态读取的安全变量：
  - `world.time.hour/day/quadrum/year/season/date`
  - `world.weather`
  - `world.temperature`
  - `pawn.recipient`
  - `pawn.recipient.name`
- 设计约束：
  - 只补 strict 命名空间变量，不恢复 `Find`、`settings`、工具函数、静态类注入。
  - 不改变旧存档与现有命名空间变量语义。

## Prompt Compat Final Closure（v0.7.0）

- 正式保存的 Prompt 结构：
  - `PromptSectionCatalog`
  - `RimTalkSummaryHistoryLimit`
  - `RimTalkPersonaCopyTemplate`
  - `RimTalkAutoPushSessionSummary`
  - `RimTalkAutoInjectCompatPreset`
- Legacy 兼容字段：
  - 旧 `EnableRimTalkPromptCompat / RimTalkCompatTemplate / RimTalkDiplomacy / RimTalkRpg / RimTalkChannelSplitMigrated`
  - 只在加载 settings / preset / bundle / custom store 时读取并导入，不再写回新的正式产物。
- 默认分段资产：
  - 主入口：`Prompt/Default/PromptSectionCatalog_Default.json`
  - 单版本兼容回退：`Prompt/Default/RimTalkPromptEntries_Default.json`
- Prompt 页稳定工作流：
  - 导航固定为 `root channel -> prompt channel -> sectionId`
  - 只支持“恢复当前分段默认值”或“恢复当前 prompt channel 默认值”
  - 预览显示当前 prompt channel 的 canonical aggregate
- RimTalk 页：
  - 只保留 `Bridge / Variables / Summary & Persona`
  - 不再出现 compat 开关、entry 列表、compat template 编辑框
- 迁移报告：
  - 最新 legacy 导入结果覆盖写到 `Prompt/Reports/LegacyPromptMigrationReport.json`
  - 同时保留 `Player.log` 摘要
- 变量显示模型：
  - `path`
  - `scope`
  - `sourceId`
  - `sourceLabel`
  - `availability`
  - `description`

## RimTalk 兼容层清理结果（v0.6.35）

- 正式保存的 prompt 结构：
  - `PromptSectionCatalog`
  - 不再把 `RimTalkDiplomacy / RimTalkRpg / CompatTemplate` 写入新的 preset / bundle / custom store 导出结果。
- 自动迁移保留范围：
  - 旧 preset 文件
  - 旧 prompt bundle
  - 旧 `Prompt/Custom/PawnDialoguePrompt_Custom.json`
- 旧变量写法自动重写：
  - `context`
  - `prompt`
  - `chat.history`
  - `chat.history_simplified`
  - `json.format`（旧写法仅在迁移时识别，结果会直接改写成当时解析到的 JSON 指令正文）
  - 以及 RimTalk API 已注册的 context/pawn/environment 自定义变量
- 新变量命名空间：
  - `pawn.rimtalk.*`
  - `dialogue.rimtalk.*`
  - `world.rimtalk.*`
  - `system.rimtalk.*`
- 变量 UI：
  - 变量浏览器/选择器会显示来源（Core / RimTalk Bridge / MemoryPatch Bridge）
  - 若当前模组依赖未加载，会显示“运行时依赖缺失”

## Prompt Section Catalog 原生迁移（v0.6.34）

- 正式配置载体：
  - `PromptSectionCatalog`
  - 结构为 `PromptChannel -> SectionId -> Content`
  - 当前 section 默认源仍来自 `Prompt/Default/RimTalkPromptEntries_Default.json`
- 运行时规则：
  - 外交 `dialogue.diplomacy_dialogue.*` 不再作为运行时 Scriban 变量暴露；旧模板会自动迁移成 section 正文本体
  - `PromptEntries`、`CompatTemplate`、`EnableRimTalkPromptCompat` 不再属于正式运行时配置
- 迁移规则：
  - 旧 `PromptEntries` / `CompatTemplate` 只在加载 preset / bundle / custom store / settings 时做一次性导入
  - 未知裸变量一律判坏，不迁移
  - 若内容含 `<prompt_context>`、`=== PREVIEW DIAGNOSTICS ===`、`[FILE]`、`[CODE]` 等污染型成品 prompt，会直接回退默认 section
  - 迁移与拒收原因只写入 `Player.log`
- 兼容性说明：
  - 旧存档仍可加载；legacy compat 字段会被自动清空/重置
  - 外部 Mod 若只提供运行时变量，仍需通过 RimChat 命名空间变量接入，不能接管 prompt 结构

## Prompt 兼容层运行时移除（v0.6.33）

- 影响范围：
  - 外交 / RPG AI 请求 prompt 组装；
  - Prompt/Custom、Preset、Bundle 中遗留的 RimTalk 兼容字段；
  - Prompt 页与 RimTalk 页的旧兼容入口。
- 新行为：
  - 运行时不再读取 `PromptEntries` / `CompatTemplate` 来直接拼装 prompt；
  - 旧 RimTalk 兼容字段在加载时只做迁移和清洗，不再作为正式运行时配置回写；
  - Prompt bundle 默认导出不再带出 RimTalk legacy 模块。
- 变量系统：
  - 模板校验、变量浏览器、运行时渲染统一走命名空间变量目录；
  - 外部桥接预留为 provider 扩展点，只允许映射到 RimChat 官方命名空间。

## Prompt Workbench 预设切换持久化修复（v0.6.32）

- 影响范围：
  - Prompt Workbench 左侧预设列表的激活切换；
  - `Prompt/Custom/*` 下由预设激活写回的自定义配置文件。
- 新行为：
  - 切换预设时，`PawnDialoguePrompt_Custom.json` 会按当前预设 payload 重建并写盘，不再沿用切换前残留的 RPG/RimTalk 自定义内容；
  - 若某个 custom payload 为空，旧 custom 文件会被删除，避免“切回默认但旧自定义文件仍生效”。
- 结果：
  - `Default` 与迁移产生的 `Migrated` 会保持各自独立内容；
  - 切回 `Default` 后，工作台正文与预览区不再继续显示 `Migrated` 内容；
  - 旧存档仍兼容，未改动游戏本体文件。

## Prompt Workbench 首次打开 Default 预设漂移修复（v0.6.26）

- 首次引导：
  - 当 `Prompt/Custom/PromptPresets_Custom.json` 不存在时，Prompt Workbench 现在会创建真正的 canonical `Default` 预设；
  - 该 `Default` 预设直接读取当前默认源：
    - `Prompt/Default/SystemPrompt_Default.json`
    - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
    - `Prompt/Default/PawnDialoguePrompt_Default.json`
    - `Prompt/Default/SocialCirclePrompt_Default.json`
    - `Prompt/Default/FactionPrompts_Default.json`
    - `Prompt/Default/RimTalkPromptEntries_Default.json`
- 升级迁移：
  - 若检测到旧版 legacy/custom 提示词与 canonical 默认内容存在实质差异，会额外生成 `Migrated` 预设保存旧长文本；
  - `Default` 不再被旧迁移内容占位。
- 首开显示一致性：
  - 工作台首次打开时，若存在 active preset，会先把该预设 payload 同步到当前编辑态，再渲染条目列表与正文编辑区；
  - 因此“左侧选中 Default、右侧却显示旧长文本”的漂移现象被消除。
- 恢复默认：
  - “恢复默认”仍按当前 scoped 通道重建 8 段结构；
  - canonical `Default` 与“恢复默认”现在共享同一条默认正文来源链路。

## Prompt 工作台变量胶囊与悬浮详情（v0.6.24）

- 生效范围：
  - 仅 Prompt 工作台条目编辑器启用变量胶囊显示。
- 变量识别规则：
  - 仅完整且合法的命名空间变量 token 识别为胶囊：`{{ namespace.path }}`。
  - 未闭合 token、未知变量保持普通文本，不做胶囊渲染。
- 交互：
  - 单击胶囊：选中并可悬浮查看详情；
  - 双击胶囊：进入 token 原文编辑；
  - `Backspace/Delete`：删除选中的整 token。
- 详情信息：
  - 变量名、作用域、说明、示例值（静态占位，不读取运行时值）。
- 兼容性：
  - 持久化仍写原始模板文本，不改旧存档结构。

## 全通道默认条目内容源（v0.6.23）

- 默认文件：
  - `Prompt/Default/RimTalkPromptEntries_Default.json`
- 数据结构：
  - `Channels[]`
    - `PromptChannel`：提示词子通道 ID（如 `diplomacy_dialogue` / `rpg_dialogue` / `social_circle_post` 等）
    - `Sections[]`
      - `SectionId`：固定 8 段 ID（`system_rules`、`character_persona`、`memory_system`、`environment_perception`、`context`、`action_rules`、`repetition_reinforcement`、`output_specification`）
      - `output_specification` 段只允许写“引用语句”；唯一权威输出协议统一放在独立节点 `response_contract_node_template`（`{{ dialogue.response_contract_body }}`）。
      - `Content`：该通道该段默认正文（支持 strict Scriban 命名空间变量）
- 加载策略：
  - 运行时优先读取该 JSON；
  - 读取失败时回退内置 fallback；
  - 缺失变量不会自动放行，仍遵循 strict Scriban 规则。
- 重置行为：
  - Prompt 工作台“恢复默认”会按当前 scoped 通道恢复完整 8 段结构与默认正文。

## Prompt 工作台通道隔离显示修复（v0.6.22）

- 显示域修复：
  - 实验工作台模式下，条目列表按“当前下拉通道”过滤，仅显示该通道条目。
- 编辑域修复：
  - 新增/复制条目默认写入当前下拉通道；
  - 上下移动只在当前通道内部生效，不影响其他通道条目顺序。
- 选中态修复：
  - 删除后自动选中当前通道可见条目，避免编辑器/变量插入落到其他通道。
- 隔离保证：
  - 外交与 RPG 根通道隔离保持不变；
  - 各子通道互不覆盖、互不串改。

## Prompt 工作台 8 段规范条目统一（v0.6.21）

- 统一条目结构：
  - 所有提示词子通道（外交根通道 + RPG 根通道）统一为 8 段固定条目：
    - `System Rules`
    - `Character Persona`
    - `Memory System`
    - `Environment Perception`
    - `Context`
    - `Action Rules`
    - `Repetition Reinforcement`
    - `Output Specification`
- 重置与迁移：
  - 旧布局首次加载会自动重建到 8 段规范顺序；
  - 旧条目文本按顺序迁移到新条目，超出 8 段的旧条目会被裁剪。
- 自动补齐：
  - 若用户删除某段，系统会在后续规范化时自动补回缺失段。
- 命名规范：
  - 条目名强制规范为上述英文名，避免多语言名混排导致的定位失配。
- 交互约束：
  - 保留条目手动上下移动能力；
  - 不会在每次刷新时强制回到默认顺序。

## Scriban 主引擎破坏式切换（v0.6.16）

- 渲染主入口：
  - 统一为 `IScribanPromptEngine.RenderOrThrow(...)`。
- 变量规则：
  - 仅允许命名空间变量：`ctx.* / pawn.* / world.* / dialogue.* / system.*`。
  - 禁止裸变量（如 `{{scene_tags}}`）。
- 失败策略：
  - Parse/Render/未知变量/空对象访问均抛 `PromptRenderException`。
  - 禁止渲染失败时回退原文或静默继续。
- 迁移策略：
  - 旧模板通过自动重写器按映射表迁移到命名空间变量。
  - 重写后立即 Scriban 验证；失败模板标记为 `Blocked` 并阻断调用。
- 运行时链路：
  - 主链不再依赖 `RimTalkCompatBridge` 运行时渲染方法。
  - 变量浏览器改为本地 `PromptVariableCatalog` 快照。
- 性能观测：
  - Scriban 编译缓存为固定容量 LRU。
  - API 调试窗口显示缓存命中率、命中/未命中/淘汰计数、平均编译耗时、平均渲染耗时。

> 说明：下方旧版本说明中若出现“fallback/兼容桥回退”描述，属于历史记录；v0.6.15+ 运行时以本节 strict 规则为准。

## RimTalk 条目通道化与兼容层真实接管（v0.6.18）

- 兼容层开关语义（`启用 RimTalk 兼容层`）：
  - 开启：当前通道仅注入“已启用 + 文本非空 + 匹配提示词通道”的条目；
  - 关闭：不走 RimTalk 条目注入，回退标准分层 Prompt 构建。
- 条目字段调整：
  - 新增 `PromptChannel`（替代旧 `Role/Position` 的运行时路由语义）；
  - 旧 `Role/Position/InChatDepth` 字段仍保留以兼容历史数据，但不再作为当前主路由入口。
- 可选提示词通道（新增）：
  - `外交策略`、`主动外交对话`、`主动 RPG 对话`、`社交圈推文`、`人格初始化`、`摘要生成`、`RPG 归档压缩`、`图像生成`。
  - 原有 `外交对话`、`RPG 对话` 保持可用。
- 自动补齐与迁移：
  - 旧条目在加载时会归一化到有效通道；
  - 若缺失默认通道条目，会自动补齐种子条目（按种子默认启用策略），避免“默认只剩一条”。
- 工作台交互：
  - Prompt Workbench 引入编辑态缓存，修复条目编辑区“可点击但内容回滚/不生效”的失效区问题。
- 兼容说明：
  - 不改旧存档结构读取逻辑；
  - 旧提示词文件仍可读取并自动迁移到新通道字段。

## Prompt 工作台点击热区可靠性修复（v0.6.14）

- 条目列表：
  - 条目行支持更大可点击热区（整行主体可选中），降低“点不到标题文字就无反应”的问题。
  - 列表顶部恢复“复制”快捷按钮，便于快速克隆条目。
- 条目编辑区：
  - 当编辑区宽度不足时，“条目角色 / 条目位置”自动改为纵向按钮，避免横向挤压导致按钮不可点。
- 变量面板：
  - 每行新增显式“插入”按钮，同时保留整行点击插入。
- 兼容说明：
  - 不改存档 schema；
  - 不改旧提示词文件格式；
  - 旧存档与旧提示词文件保持兼容。

## Prompt 工作台按钮响应修复（v0.6.13）

- 工作台入口：
  - 从 `RPG 运行设置` 与 `RimTalk 迁移页` 打开工作台时，改为直接进入 RPG 通道，避免入口点击后回到外交通道。
- 预设操作反馈：
  - 预设 `新建 / 复制 / 重命名 / 删除 / 导入 / 导出 / 激活` 均增加即时反馈提示，便于确认按钮是否生效。
- 预设激活失败提示：
  - 激活失败时显示失败原因，不再静默失败。
- 兼容说明：
  - 不改存档 schema；
  - 不改旧提示词文件格式；
  - 旧存档与旧提示词文件保持兼容。

## Prompt 工作台交互修复 + RimTalk 变量面板移植（v0.6.12）

- 变量面板：
  - 工作台右侧变量区改为 RimTalk 风格：搜索 + 分组列表 + 整行点击插入。
  - 渲染链路改为独立 Rect 绘制，避免 Listing 嵌套导致的点击区域偏移。
- 条目列表：
  - 左侧条目区改为行内勾选启用、行内删除、底部上下移动，交互习惯与 RimTalk 对齐。
- 预设切换：
  - 点击预设行会立即激活并应用该预设，编辑区内容同步切换，不再出现“选了没反应”。
- 兼容说明：
  - 不改存档 schema；
  - 不破坏旧提示词文件格式与旧字段读取。

## Prompt 工作台 RimTalk 高保真对齐（v0.6.11）

- 工作台布局：
  - 左栏宽度调整为接近 RimTalk（窄栏），右侧改为“编辑区 + 侧栏”组合，降低编辑区拥挤与信息遮挡。
- 左栏交互结构：
  - 预设区与条目区改为更紧凑的 RimTalk 风格分布；
  - 移除工作台左下通用“保存/重置/导入/导出配置”按钮组，避免混入非 RimTalk 编辑路径。
- RimTalk 条目字段：
  - `RimTalkPromptEntryConfig` 新增 `CustomRole`（字符串，默认空）。
  - “自定义角色”输入框现在写入 `CustomRole`，不再覆盖 `Role`。
- 兼容说明：
  - 旧存档与旧提示词文件继续兼容；
  - 历史配置中没有 `CustomRole` 字段时自动按空字符串处理。

## Mod 设置图标命名空间隔离（v0.6.10）

- About 图标路径：
  - `About/About.xml` 的 `modIconPath` 改为 `UI/RimChat/Logo`。
- 资源布局：
  - 新增 `1.6/Textures/UI/RimChat/Logo.png` 作为主路径资源。
  - 保留 `1.6/Textures/UI/Logo.png` 作为旧路径兼容资源。
- 兼容说明：
  - 不改存档结构；
  - 不改提示词文件 schema；
  - 旧分发包/旧资源布局仍可读取旧路径资源。

## 图标资源隔离修复（v0.6.9）

- 地图右下角通讯切换图标加载路径：
  - 新增主路径 `UI/RimChat/CommsToggleIcon`（文件：`1.6/Textures/UI/RimChat/CommsToggleIcon.png`）。
  - 保留兼容回退 `UI/CommsToggleIcon`（文件：`1.6/Textures/UI/CommsToggleIcon.png`）。
- 兼容说明：
  - 不改存档结构；
  - 不改提示词文件 schema；
  - 旧版本分发包仍可通过旧路径显示图标。

## RimTalk 条目列表交互细化（v0.6.8）

- 条目列表显示：
  - 条目改为双行展示（名称 + 角色/位置），默认可见信息更完整；
  - 长名称自动截断并提供悬停完整提示，避免文字遮挡同时保持可读性。
- 条目编辑区布局：
  - “启用 / 角色 / 位置”区域改为自适应宽度，窄窗口下不再出现控件重叠与点击冲突。
- 顶部导入导出按钮：
  - 新增 `RimChat_Import`、`RimChat_Export` 语言键，避免按钮回退显示键名导致截断。
- 兼容说明：
  - 不新增配置字段，不改存档 schema，不改旧提示词文件格式。

## Prompt 工作台变量浏览器交互与性能优化（v0.6.7）

- 变量面板交互：
  - 工作台右侧 `变量` 面板支持“点击行选中 + 插入”双路径交互；
  - 选中项会在下方详情区显示完整 token 和变量说明，降低文字截断影响。
- 性能行为：
  - RimTalk 变量快照改为节流刷新（约 1.2 秒）；
  - 搜索结果使用缓存，避免每帧重复过滤排序导致的卡顿。
- 兼容说明：
  - 不新增配置项；
  - 不改存档结构；
  - 不改提示词文件格式，旧存档与旧提示词文件保持兼容。

## Prompt 工作台变量插入与种子拆分（v0.6.6）

- 变量页行为：
  - Prompt 工作台右侧 `变量` 侧栏改为 RimTalk 变量浏览器交互（搜索、分组、点击插入）。
- 变量写入策略：
  - 插入变量时优先写入当前编辑器光标处；
  - 若当前没有可用编辑器焦点，则回退为末尾追加，避免丢操作。
- legacy 条目迁移补全：
  - 当旧配置只有拼接文本且无有效条目时，会按段标题（如 `[Section]`、`=== Section ===`）拆分成多个条目导入。

## Prompt 工作台原型重做 + 单Tab入口（v0.6.5）

- 顶层设置页导航调整：
  - 现为 `API配置 / 设置 / 提示词工作台 / 图片生成` 四个Tab。
  - 原 `外交对话 / 人物对话 / RimTalk兼容` 顶层Tab不再显示。
- Prompt Workbench 入口行为：
  - 点击 `提示词工作台` Tab 会直接打开独立工作台窗口；
  - 不会强制切换当前设置页内容区上下文。
- 工作台主通道：
  - 仅保留 `外交` 与 `RPG`。
  - RimTalk 子通道 UI 隐藏，但底层兼容数据结构与读写链路保留。
- 变量面板行为：
  - 工作台右侧 `变量` 面板改为 RimTalk 同款变量浏览器（支持搜索、分组、点击插入）。
  - 变量插入优先使用当前编辑器光标位置；若未聚焦则自动回退为末尾追加。
- RPG 二级编辑：
  - 在 `RPG` 主通道内新增 `通用条目 / Pawn Persona` 二级切换；
  - `Pawn Persona` 复用原有逐 Pawn 人格提示词存储，不改旧存档结构。
- ModOptions 新增分组：
  - `RPG 运行设置`：迁入非提示词项（RPG通道开关、RPG API开关、动态注入开关、场景标签）。
- 兼容说明：
  - 旧存档与旧提示词文件路径继续兼容；
  - `PromptPresetChannelPayloads` 与 RimTalk 兼容字段 schema 不变；
  - 旧配置迁移为条目时，支持按拼接段标题自动拆分条目，减少“单条目大文本”导入缺失。

## Prompt 通道条目化统一（v0.6.4）

- Prompt Workbench 通道行为：
  - `Diplomacy` 与 `RPG` 现统一为条目编辑器（与 RimTalk 通道同款），不再使用旧分区编辑中间层。
  - 条目字段：`Name`、`Enabled`、`Role`、`Position`、`InChatDepth`、`Content`。
- 运行时拼装：
  - 外交/RPG 最终 prompt 由条目系统按顺序拼接“已启用条目”生成。
  - 条目模板通过内置 `IScribanPromptEngine.RenderOrThrow(...)` 严格渲染变量。
- 旧配置兼容：
  - 若仅存在旧字段（无有效条目），会自动生成初始条目以承接旧内容。
  - 保存/导出时会把条目内容自动回写到旧字段 JSON（`SystemPrompt_Custom.json`、`PawnDialoguePrompt_Custom.json`），保持旧版本可读。

## RimTalk 通道变量条目编辑（v0.6.3）

- `PromptEntries`
  - 类型：`List<RimTalkPromptEntryConfig>`
  - 作用：保存 RimTalk 子通道的条目式提示词编辑内容（可用于变量驱动拼装）
- `RimTalkPromptEntryConfig`
  - `Id`：条目唯一标识
  - `Name`：条目显示名称
  - `Role`：条目角色（`System/User/Assistant`）
  - `Position`：条目位置（`Relative/InChat`）
  - `InChatDepth`：`InChat` 模式深度（0-32）
  - `Enabled`：条目是否参与模板合成
  - `Content`：条目模板正文（支持变量 token）
- 兼容说明：
  - 旧配置只有 `CompatTemplate` 时会自动迁移成默认条目；
  - 条目编辑结果自动合成回 `CompatTemplate`，保证旧版本兼容读取。

## Prompt 工作台与预设（v0.6.2）

- 设置位置：`Mod Settings -> Prompt（高级模式）`
- 新增通道导航：
  - `Diplomacy`（含外交与社交圈类来源）
  - `RPG`（含 RPG 对话与主动推送类来源）
  - `RimTalk-Diplomacy`、`RimTalk-RPG`
- 新增预设管理：
  - 支持 `新建/复制/激活/删除/重命名/导入/导出`。
  - 预设激活后会自动同步回写旧 `Prompt/Custom/*` 文件，保证旧版本读取路径不被破坏。
- 迁移行为：
  - 若不存在预设文件，将自动从当前旧配置生成默认预设。
  - RimTalk 旧字段会继续与双通道配置同步，兼容旧存档与旧提示词文件。
- RimTalk 独立页：
  - 当前版本保留过渡入口，仅用于跳转到 Prompt 工作台。

## RimTalk 严格隔离配置（v0.6.1）

- 设置位置：`Mod Settings -> RimTalk（独立页）`
- 新增开关：
  - `自动将 RimChat 会话摘要写入 RimTalk 全局变量`（默认关闭）
  - `自动创建/更新 RimTalk Compat Variables 预设条目`（默认关闭）
- 推荐隔离策略：
  - 若你同时使用 RimTalk 与 RimChat，请保持上述两个开关关闭；
  - 仅在确有需求时，通过兼容模板显式 `{{variable}}` 引用变量。
- 行为说明：
  - 关闭“自动推送摘要”后，RimChat 不再写入 `rimchat_last_* / rimchat_recent_*` 全局摘要；
  - 关闭“自动注入预设”后，RimChat 不再自动改写 RimTalk 活动预设，且会禁用已存在兼容条目。
- 兼容性：
  - 旧存档/旧自定义配置缺失新字段时自动回退为关闭，不会破坏读取。

## 通讯台隐藏派系显示（v0.5.29）

- UI 入口：通讯台外交对话窗口左侧派系列表标题右侧齿轮按钮。
- 交互方式：打开“隐藏派系显示”弹窗后，可多选隐藏派系并点击“确定”生效。
- 可选范围：仅显示候选 `非玩家 + 未败亡 + Hidden=true` 的派系。
- 批量操作：支持 `全选` 与 `清空`。
- 持久化范围：按存档保存（不同存档互不影响）。
- 兼容性：
  - 旧存档缺失该配置字段时自动回退为空（默认不额外显示隐藏派系）。
  - 不改动提示词文件结构与旧提示词内容。

## 图片 API 三模式与 ComfyUI 兼容（v0.5.22）

- 图片 API 新增模式配置（Mod 设置 -> 图片API）：
  - `Image provider`：`Volcengine ARK` / `OpenAI Compatible` / `SiliconFlow` / `ComfyUI Local` / `Custom`
  - 非 `Custom` 预设自动配置模式、协议和鉴权，减少手动配置项。
  - 仅 `Custom` 提供高级选项（模式/鉴权/响应路径/异步路径/轮询）。
- 新增可配置字段：
  - `API-key header name`、`API-key query name`
  - `Response URL path keys`、`Response base64 path keys`
  - `Async submit path`、`Async status path template`、`Async image fetch path`
  - `Async poll interval (ms)`、`Async max poll attempts`
- ComfyUI 使用建议：
  - `Schema preset=comfyui`（会自动切换 `async_job`）；
  - endpoint 可填基础地址（如 `http://127.0.0.1:8188`）或 `/prompt` 完整地址；
  - 默认异步路径回退为 `/prompt`、`/history/{job_id}`、`/view`。
- 图片 API 连通性测试：
  - 图片 API 页新增 `测试连通性` 按钮，状态文案/颜色与主 API 页一致；
  - 同步模式会发送最小发图探测请求；
  - 异步模式会发送提交探测并校验任务 ID 返回。
- 兼容性：
  - 旧存档缺失新字段时自动回退默认值；
  - 不改 `send_image` 参数契约；
  - 不改旧提示词文件结构。

## 外交发图 Caption 策略与输入锁定占位隐藏（v0.5.20）

- 外交输入锁定显示调整：
  - 当外交输入因 AI 等待/逐字机/发图等待而锁定时，输入框内部不再显示占位文案；
  - 底部 typing 状态层保留，结束态优先级规则不变。
- 图片 API 页新增配置（Mod 设置 -> 图片API）：
  - 发图 Caption 风格提示词（SendImageCaptionStylePrompt）：用于约束 AI 生成 caption 的语气风格。
  - 发图 Caption 本地兜底模板（SendImageCaptionFallbackTemplate）：当 AI 未返回 caption 时使用。
  - 支持占位符：{leader}、{faction}、{template_name}。
- send_image caption 生成规则：
  - 优先使用 AI 返回的 parameters.caption；
  - 缺失或为空时，使用本地兜底模板渲染；
  - 若模板渲染后仍为空，回退到默认文案 RimChat_SendImageDefaultCaption。
- 兼容性：
  - 不改 send_image 参数契约；
  - 不删除旧语言键；
  - 旧存档缺失新字段时自动回退默认值。
## 外交相册缩略图与自拍注入开关（v0.5.19）

- 相册窗口改为缩略图卡片网格：
  - 展示缩略图、标题、尺寸/文件信息；
  - 增加来源徽标（聊天图/自拍图）；
  - 条目右键支持 `打开图片保存目录` 与 `复制图片路径`。
- 聊天区右键保存修复：
  - 对话内联图改为 `ContextClick + MouseDown(右键)` 双事件兜底；
  - 仅在“图片可视区域”命中触发保存菜单（不含标题/边距）。
- 自拍参数新增注入开关（默认全开）：
  - `服饰`、`体型`、`发型`、`武器`、`植入物`、`状态`；
  - 注入文本在发送时隐藏追加到最终请求，不改写用户输入框内容。
- 兼容性：
  - `AlbumImageEntry` 新增可选字段 `sourceType`（`chat/selfie/unknown`）；
  - 旧存档缺失字段自动回退到 `unknown`，相册展示层按 `chat` 兜底；
  - 旧提示词文件与既有 `send_image` 链路保持兼容。

## 外交相册与自拍（v0.5.18）

- 外交窗口顶部新增入口：
  - `相册`：打开手动保存图片列表。
  - `自拍`：打开自拍参数窗口（需当前有谈判者）。
- 相册收录规则：
  - 仅收录“手动保存到相册”的图片；
  - 不自动收录所有 `send_image` 生成图。
- 聊天区右键能力：
  - 对聊天内联图片右键可执行 `保存到相册`。
  - 保存策略为复制到存档相册目录，重名自动加后缀。
- 相册窗口能力：
  - 列表展示已保存图片；
  - 右键条目可执行 `打开图片保存目录`（打开该图片实际所在目录）。
- 自拍窗口参数：
  - `Prompt`、`Size`、`Watermark`、`Caption`。
  - 生成后进入独立预览窗口，需用户手动点击“保存到相册”才会入册。
- 兼容性：
  - 新增 `albumEntries` 存档字段，旧存档自动补空列表；
  - 旧提示词文件和既有 `send_image` 工作流保持兼容。

## 手动RPG血缘/浪漫关系画像注入（v0.5.17）

- 生效范围：仅 `手动 RPG 对话`（`BuildRPGFullSystemPrompt(..., isProactive=false, ...)`）；不作用于 PawnRPG 主动对话。
- 新增可覆写字段（默认+自定义链路）：
  - `RelationshipProfileTemplate`
  - `KinshipBoundaryRuleTemplate`
- 默认读取链路：
  - `Prompt/Custom/PawnDialoguePrompt_Custom.json`（存在时） ->
  - `Prompt/Default/PawnDialoguePrompt_Default.json`
- 关系画像块内容：
  - `Kinship: yes/no`（仅标记是否存在血缘关系，不细分类型）。
  - `RomanceState: spouse/fiance/lover/ex-or-none/none`。
  - `Guidance`：基于 `KinshipBoundaryRuleTemplate` 渲染的保守边界提示。
- 兼容性：
  - 不新增存档字段；
  - 旧 `PawnDialoguePrompt_Custom.json` 缺失新字段时自动回退默认值；
  - 不改动主动RPG共用关系标签判定逻辑。

## 派系提示词模板增删与派系绑定（v0.5.16）

- UI 入口：Mod 设置 -> Prompt -> Faction Prompts
- 新增能力：
  - 新增模板：可从全部 FactionDef 中选择派系（包含 mod 派系）创建模板绑定。
  - 移除模板：仅允许删除自定义新增模板。
- 约束规则：
  - 每个派系（FactionDefName）最多 1 条模板。
  - 若新增时目标派系模板已存在，系统会自动定位到已有模板，不创建重复项。
  - 默认模板（来源于 Prompt/Default/FactionPrompts_Default.json）不可移除。
- 缺失派系行为：
  - 若因 mod 未加载导致派系 Def 缺失，模板会保留并标记为“缺失”，以保证导入导出和旧存档兼容。
- 兼容性：
  - 不新增外部配置 JSON 字段，不改变旧提示词文件结构。
  - 导入旧配置后会自动补齐默认模板，并继续保持“默认模板不可删除”规则。
## 图片模板页滚动可用性与发图等待锁定（v0.5.15）

- 图片 API 页（Mod 设置 -> 图片API）现改为整页纵向滚动：
  - 在较低分辨率或窗口高度不足时，模板编辑区仍可完整访问。
  - 可访问项包括：模板启用、模板 ID、模板名称、模板描述、模板正文、新增模板、删除模板。
- 外交输入锁定规则补充：
  - 当 `send_image` 图片请求处于处理中，输入框与发送操作会临时锁定，并显示 typing 提示。
  - 锁定会在图片回调（成功或失败）后自动释放。
- 会话结束显示规则：
  - 会话结束后，输入区优先显示结束原因/冷却提示，不再显示 typing。
  - 若图片回调晚到，消息历史仍会追加图片或失败系统提示。
- 兼容性：
  - 无新增外部配置字段；
  - 无存档结构变更；
  - 旧存档与旧提示词文件可直接继续使用。

## 外交发图尺寸下限对齐（v0.5.14）

- 相关配置项：`DiplomacyImageApi.DefaultSize`（UI 路径：`Mod 设置 -> 图片API -> 默认尺寸`）。
- 新规则：输入尺寸必须满足 `宽*高 >= 3,686,400`，否则自动回退到 `2560x1440`。
- 别名映射：
  - `small` / `landscape` -> `2560x1440`
  - `portrait` -> `1440x2560`
  - `medium` -> `3072x1728`
  - `large` -> `3840x2160`
- 旧存档兼容：旧配置里的低尺寸（如 `1024x1024`）在加载与请求阶段会自动归一化，不需要手工迁移。


## NPC 主动对话分离开关（v0.5.8）

- NPC 主动对话设置新增并拆分为两个独立开关：
  - `EnableNpcInitiatedDialogue`：外交主动对话开关（默认 `true`）
  - `EnablePawnRpgInitiatedDialogue`：PawnRPG 主动对话开关（默认 `true`）
- UI 入口：`Mod 设置 -> MOD设置 -> UI 设置 -> NPC 主动对话设置`。
- 生效规则：
  - 关闭外交主动：仅停用外交主动链路，不影响 PawnRPG 主动链路。
  - 关闭 PawnRPG 主动：仅停用 PawnRPG 主动链路，不影响外交主动链路。
  - PawnRPG 主动仍受 `EnableRPGDialogue` 总体 RPG 通道开关约束。
- 旧存档兼容：
  - 若旧存档未包含 `EnablePawnRpgInitiatedDialogue` 字段，加载时自动继承旧 `EnableNpcInitiatedDialogue` 值。

## API 调试观测窗口（v0.5.7）

- 本版本无新增持久化配置字段（不写存档）。
- API 设置页新增入口：
  - 位置：`API 设置 -> 调试设置` 标题行右侧按钮 `Token/完整日志`。
  - 点击后打开 API 调试观测窗口（实时刷新间隔 2 秒）。
- 观测窗口能力：
  - 汇总卡片：总 token / 请求数 / 成功率 / 平均耗时 / 外交RPG占比。
  - 5 分钟桶趋势：最近 60 分钟（12 桶）token 走势。
  - 明细表：时间、来源、状态、模型、token、耗时、HTTP。
  - 详情面板：完整 request/response 与错误信息。
  - 复制：`复制选中 JSON`、`复制筛选 JSON`。
- 可视规则：
  - 外交/RPG 来源高可视显示。
  - 其他后台来源统一低对比度置灰显示。
- 内存保留策略：
  - 环形上限 2000 条。
  - 自动清理 65 分钟前记录。
  - 窗口固定展示最近 60 分钟。

## 袭击延迟事件可靠性修复（v0.5.5）

- 本版本无新增用户可配置项。
- 内建行为调整：
  - 延迟外交事件改为“成功后移除”，失败不再立即丢弃。
  - 失败事件自动重试（最多 3 次，短延迟回退）。
  - `request_raid` 在策略不可执行场景会强制追加原版自动策略/自动入场兜底。
- 兼容说明：
  - 对旧存档新增延迟袭击 Def 名称回填（`raidStrategyDefName` / `arrivalModeDefName`），用于 Def 引用丢失时恢复执行参数。

## 提示词包选择性导入导出 + RimTalk 独立页（v0.5.4）

- 提示词包导出新增双模式：
  - 全量导出（全部模块）
  - 按模块导出（勾选模块）
  - 导出窗口支持快速路径按钮（桌面 / RimWorld 配置目录），无需手填完整绝对路径
- 提示词包导入新增预览选择：
  - 先读取文件并展示可用模块与摘要
  - 再勾选要导入的模块
  - 未勾选模块保持当前配置不变
  - 服务层会额外校验空路径、空文件和“无可应用模块”场景并拒绝导入
- 新增 RimTalk 顶级设置页（独立 tab）：
  - 通道切换：外交 / RPG
  - 每通道独立配置：启用开关、模板文本、预设注入数量上限、预设注入字符上限
  - 共享配置：摘要历史上限
  - 变量工具：搜索、按类型/来源分组、单击插入模板 token
- 兼容说明：
  - 历史 `v1` 导入文件仍可使用
  - 旧单通道 RimTalk 配置会自动迁移到外交+RPG 双通道

## 袭击点数全局/派系覆盖（v0.5.3）

- 新增全局配置：
  - `RaidPointsMultiplier`（默认 `1.0`，范围 `0.1-5.0`）
  - `MinRaidPoints`（默认 `35`，范围 `0-1000`）
- 新增按派系覆盖列表：
  - `RaidPointsFactionOverrides`（按 `FactionDefName` 匹配）
  - 每项字段：`FactionDefName`、`RaidPointsMultiplier`、`MinRaidPoints`
- 入口：
  - `Mod 设置 -> AI 控制 -> 袭击设置 -> 袭击点数调节`
- 生效顺序：
  - 先算原版 `RaidEnemy` 基线点数（自动点数场景）
  - 再应用倍率
  - 最后应用最小点数下限（全局或派系覆盖）

## 非言语 Pawn 对话开关（v0.5.0）

- 新增设置项：`EnableRPGNonVerbalPawnSpeech`（默认 `true`）。
- UI 入口：`RPG -> RPG 动态数据注入` 分区中新增开关。
- 生效范围：仅 NPC 目标回复文本（不改玩家输入文本）。
- 命中类别：动物 / 婴儿 / 机械。
- 行为：命中类别后，回复格式强制为“叫声 + 括号内心想法”；未命中则保持普通对话格式。

## HAR 兼容修复（v0.5.1）

- 无新增用户开关。
- 内部机制改为“双通道注入”：
  - XML Patch 扩展到通配 Def 节点（覆盖 HAR 自定义 Def 标签）。
  - 运行时 Def 注入补齐（覆盖继承导致的漏注入）。

## XML 误匹配修复（v0.5.2）

- 无新增用户开关。
- XML 注入已收敛为保守模式（仅 `Human`），避免误写到 `PawnKindDef`。
- 异种族覆盖继续依赖运行时 Def 注入补齐。

## RPG 输出契约加固（v0.4.12）

- 默认 RPG 格式约束示例已从占位符 `ActionName` 改为可执行动作示例（`TryGainMemory`），并强调动作名需来自允许动作列表。
- RPG 请求在常规发送链路会附加严格输出契约提醒，减少模型漂移到旧 JSON 包装格式。
- 兼容性说明：RPG 动作解析同时接受 `params` 与 `parameters` 包装字段，便于接入 OpenAI 兼容返回格式。

## Custom URL 安全映射与模式开关（v0.4.9）

- API 设置页（Custom provider）新增 URL 模式：
  - `Base URL`：基础地址模式，按保守规则自动补全聊天端点。
  - `Full Endpoint`：完整端点模式，按输入 URL 原样请求。
- 旧配置兼容策略：
  - 若 URL 包含 `/chat/completions`，自动识别为 `Full Endpoint`。
  - 其他情况自动识别为 `Base URL`。
- SiliconFlow 兼容规则（仅 Custom provider）：
  - 仅 `cloud.siliconflow.*` 主机会在运行时映射为 `api.siliconflow.cn`。
  - 不会影响其它 `*.siliconflow.*` 域名。
- Base URL 自动补全边界：
  - 仅空路径、`/`、`/v1` 自动补到 `/v1/chat/completions`。
  - 非标准尾路径保持原值，并在连接测试状态中给出提示。
- 连通性测试改进：
  - `Full Endpoint` 模式先测模型列表，再回退 chat endpoint 探测，减少“可对话但测试失败”的误判。

## 模型列表拉取兜底（v0.4.7）

- DeepSeek 模型列表地址对齐 RimTalk，使用 `/models` 端点。
- 模型列表请求会自动去除 API Key 前后空白字符。
- OpenAI 兼容模型列表解析在返回空列表时会尝试从 JSON 中抽取 `id` 作为兜底。

## DeepSeek 官方地址强制（v0.4.6）

- DeepSeek 提供商强制使用官方地址：`https://api.deepseek.com/v1`。
- 若配置里存在非官方 `BaseUrl`，加载时会自动归一化为官方地址并写回配置。
- 模型列表拉取与连接测试不再使用 DeepSeek 的自定义 `BaseUrl`。

## 通讯台覆盖默认关闭（v0.4.5）

- 默认值调整：
  - `ReplaceCommsConsole` 默认从开启改为关闭。
  - 新配置（或缺省回填）默认使用原版联络界面入口。
- 重置默认行为：
  - UI 设置中的“恢复默认”现在会把通讯台覆盖恢复为关闭。
- 保持能力：
  - 可随时在设置页手动开启。
  - 可通过地图右下快捷按钮手动开启/关闭。

## 原版联络窗口 RimChat 入口（v0.4.4）

- 本版本无新增设置开关，属于原版联络窗口增强入口。
- 生效条件：
  - `ReplaceCommsConsole = false` 时显示。
  - `ReplaceCommsConsole = true` 时不显示（保持通讯台覆盖逻辑不变）。
- 生效范围：
  - 原版派系联络根菜单（不限具体入口来源，只要是原版派系联络窗口）。
  - 非派系联络对象（如商船）不显示该入口。
- 入口行为：
  - 显示文案：`使用 RimChat 联络`（语言键：`RimChat_UseRimChatContact`）。
  - 点击后关闭原版联络树窗口，并打开 RimChat 外交对话窗口。
  - 入口位置优先插入在“退出/挂断”前一项，未识别到退出项时追加到末尾。

## 外交 Prompt 动态上下文补全（v0.4.3）

- 本版本无新增 UI 开关，属于外交通道提示词上下文增强。
- 外交通道 `dynamic_data` 现新增：
  - `player_pawn_profile`（玩家小人摘要）
  - `player_royalty_summary`（帝国荣誉/头衔/许可摘要）
  - `faction_settlement_summary`（长期据点数量与全量基地列表）
- 玩家小人来源规则：
  - 优先使用外交窗口当前 `negotiator`
  - 缺失时自动回退为社交最高的可用殖民者
  - 主动外交推送链路复用同一规则
- 帝国派系 Prompt 软约束：
  - 会在提示词中附带帝国相关动作（重点 `create_quest`、`request_aid`）的可用性提示与不可用原因摘要
  - 仅影响模型决策倾向，不替代执行层硬校验
- 模板变量新增（可在变量参考中插入）：
  - `{{player_pawn_profile}}`
  - `{{player_royalty_summary}}`
  - `{{faction_settlement_summary}}`

## 地图右下角通讯台快捷切换（v0.4.1）

- 入口位置：地图界面右下角原版图标行（仅地图视图显示，按钮追加在图标行末尾）。
- 行为等价：与 `Mod 设置 -> UI 设置 -> 替换通讯台（ReplaceCommsConsole）` 为同一布尔配置，不新增并行选项。
- 点击行为：
  - 左键点击按钮可直接切换 `ReplaceCommsConsole`。
  - 切换后立即持久化（`WriteSettings()`），重启后保持状态。
  - 切换后显示本地化短消息提示（已启用/已禁用通讯台替换）。
- 视觉反馈：
  - 开启：显示原版绿色勾（check）。
  - 关闭：显示原版红色叉（cross）。
  - 鼠标悬停 tooltip 会显示当前状态（本地化文案）。
- 图标资源：运行时优先使用 `1.6/Textures/UI/RimChat/CommsToggleIcon.png`，并兼容回退到 `1.6/Textures/UI/CommsToggleIcon.png`（来源 `About/icon.png`）。

## API 页头工具按钮（v0.4.0）

- 入口：`Mod 设置 -> API配置` 标题行右侧。
- `Version` 小按钮：
  - 显示文案为 `版本: x.y.z`（英文环境显示 `Version: x.y.z`）。
  - 版本号来源于版本日志文件首行（首个非空行）。
  - 点击后打开游戏内可滚动版本日志窗口。
- 版本日志文件映射：
  - `ChineseSimplified` / `ChineseTraditional` -> `VersionLog.txt`
  - 其他语言 -> `VersionLog_en.txt`
- `GitHub` 绿色小按钮：
  - 点击后直接打开：`https://github.com/yancy22737-sudo/RimChat`
- 异常兜底：
  - 版本日志文件缺失/空文件/读取失败时，窗口内显示本地化提示文案，不中断设置页交互。

## 好感度分段和平策略（v0.3.164）

- 本版本无新增 UI 开关，属于固定策略落地（执行层 + 提示词层双约束）。
- `make_peace` 动作规则：
  - goodwill `< -50`：禁止直接议和。
  - goodwill `[-50,-21]`：禁止 `make_peace`，应改用和平会谈任务。
  - goodwill `[-20,0]`：允许 `make_peace`（仍受战时/冷却等既有条件约束）。
- `create_quest` 任务规则：
  - 在 `[-50,-21]` 区间，仅允许 `questDefName = OpportunitySite_PeaceTalks`。
  - 其他区间保持原有任务模板可用性规则。
- 提示词注入规则：
  - 外交 response contract 会动态注入 `DYNAMIC PEACE POLICY (GOODWILL-BASED)`。
  - 注入文本与执行层资格校验保持一致，避免“提示允许但执行拒绝”。

## 设置界面清理与议和条件生效修复（v0.3.160）

- UI 调整：
  - 外交对话页「全局系统提示词」下方的「全局对话提示词」次级编辑框已移除。
  - MOD 设置页中的「礼物设置」折叠项已从界面隐藏。
- 提示词链路修复：
  - `make_peace` 的条件文本不再被固定短文案覆盖。
  - 紧凑 action 目录会优先使用配置中的 `Description/Requirement`，并始终附带高真诚度约束（`very high sincerity only`）。
- 兼容说明：
  - 历史 custom prompt 中若仍是旧版 `make_peace` 文案，会在加载时按迁移规则升级到新条件文本。

## 模型超时统一（v0.3.158）

- 本版本无新增 UI 开关。
- 模型请求超时统一为 `40s`（本地/云端一致）。
- 统一范围：`AIChatServiceAsync`、`AIChatService`、`AIChatClient`。

## 本地连接超时恢复（v0.3.157）

- 本版本无新增 UI 开关，属于本地请求链路稳定性修复。
- 生效范围：仅本地模型模式。
- 内建行为：
  - 本地请求超时上限提高到 `180s`（云端维持 `60s`）。
  - 本地连接瞬态错误（如 timeout）会执行有限重试（短退避+抖动）。
  - timeout 错误提示与“无法连接本地服务”提示分离，避免误导排查方向。

## 本地模型 500 容错与并发降载（v0.3.154）

- 本版本无新增 UI 开关，属于本地链路稳定性加固。
- 生效范围：仅本地模型模式（`UseCloudProviders = false`）。
- 内建行为：
  - 本地请求并发上限固定为 `1`（单飞串行）。
  - 本地服务返回 `500/502/503/504` 时自动重试（有限次数，短退避 -> 长退避，带抖动）。
  - 既有 `HTTP 400 user input rejected` 降级重试继续生效。
- 诊断日志：
  - 沿用现有 Debug 开关（无新增开关）。开启内部日志后，记录每次请求尝试指纹与 5xx 重试决策。
- 兼容说明：
  - 云端 provider 行为不变，不引入新的全局节流配置。

## API URL 归一化修复（v0.3.151）

- 本版本无新增 UI 开关，属于配置链路稳定性修复。
- 云端 provider 预置 URL 已移除误植空白字符，避免默认配置直接触发 URL 校验失败。
- 本地模型默认 `BaseUrl` 改为 `http://localhost:11434`。
- 运行时 URL 归一化生效点：
  - 读取 `ApiConfig.BaseUrl` / `LocalModelConfig.BaseUrl` 时自动清理空白字符。
  - API 设置页输入时自动归一化 `BaseUrl`。
  - 模型列表拉取（`/models`）与连接测试链路统一走归一化 URL。
  - 本地模型聊天 endpoint 统一由归一化 `BaseUrl` 拼装。
- 兼容说明：
  - 若旧配置中存在 URL 空白字符，加载后会被自动归一化，无需手动迁移。

## 社交圈世界新闻化（v0.3.143）

- 社交圈自动内容已从“随机公告”改为“事实驱动世界新闻”，优先扫描：
  - `WorldEventLedgerComponent` 世界事件台账
  - `RaidBattleReportRecord` 战报
  - `LeaderMemoryManager` 重大事件记忆 / 外交摘要
  - `publish_public_post` 与关键词触发的公开声明
- 无新增总开关；仍沿用原有配置：
  - `EnableSocialCircle`
  - `EnablePlayerInfluenceNews`
  - `EnableAISimulationNews`
  - `EnableSocialCircleAutoActions`
  - `SocialPostIntervalMinDays` / `SocialPostIntervalMaxDays`
- 社交圈 Prompt 默认文件现扩展为：
  - `SocialCircleActionRuleTemplate`
  - `SocialCircleNewsStyleTemplate`
  - `SocialCircleNewsJsonContractTemplate`
  - `SocialCircleNewsFactTemplate`
- 设置入口仍在 `Mod 设置 -> 外交对话 -> 高级 -> 社交圈 Prompt`，但编辑项现在分为“动作规则模板 / 世界新闻风格 / JSON 契约 / 事实模板 / publish_public_post 动作字段”。
- LLM 未配置、超时、坏 JSON、缺少必填字段时，该条新闻会直接丢弃，不写入半成品。

## 响应解析与生命周期修复（v0.3.114，无新增用户配置）

本版本无新增开关，行为为内建修复：

- AI 响应解析改为容错提取器，提升不同提供商返回格式的兼容性。
- 主页面签窗口重复打开时会自动恢复好感度动画事件订阅。
- 存档名解析增加反射兜底链，降低异常回退到 `Default` 的概率。

## 异步稳定性机制（v0.3.113，无新增用户配置）

本版本的请求生命周期加固为内建行为，无需用户手动配置：

- 跨存档隔离：新开局/读档时自动取消旧上下文挂起请求。
- 回调防护：旧上下文请求即使晚到，也不会继续写入当前会话。
- 内存清理：异步请求结果会定时清理并做数量上限裁剪。
- 外交窗口关闭保护：关闭窗口会取消该窗口的挂起主回复与策略补充请求。
- 记忆加载策略：`LeaderMemoryManager` 在加载期预热缓存，运行期不再按需阻塞单文件读取。

## Prompt Policy 预算移除（v0.3.163）

### 配置入口

- `Prompt Policy` 页面入口已从提示词设置界面移除。

### 当前保留策略项（PromptPolicy）

- `Enabled`：策略总开关（不再包含预算裁剪行为）。
- `EnableIntentDrivenActionMapping`：启用 RPG 意图驱动动作映射层。
- `IntentActionCooldownTurns`：意图映射动作冷却回合。
- `IntentMinAssistantRoundsForMemory`：协作意图触发 `TryGainMemory` 的最小助手轮数。
- `IntentNoActionStreakThreshold`：no-action 连击兜底阈值。
- `ResetPromptCustomOnSchemaUpgrade`：schema 升级时重置旧 Prompt 自定义覆盖并重建默认。
- `SummaryTimelineTurnLimit`：RPG 记忆摘要最多回合数。
- `SummaryCharBudget`：RPG 记忆摘要字符预算。

### 行为说明

- 外交通道与 RPG 通道不再执行 Prompt token 预算裁剪。
- `api_limits` 文本与外交 API 行为限制（好感阈值/冷却/每日上限）保持不变。

## Prompt 文件分仓（v0.3.139）

- 默认提示词固定拆分为：
  - `Prompt/Default/SystemPrompt_Default.json`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `Prompt/Default/PawnDialoguePrompt_Default.json`
  - `Prompt/Default/FactionPrompts_Default.json`
  - `Prompt/Default/SocialCirclePrompt_Default.json`
- 运行时自定义提示词按相同领域写入：
  - `Prompt/Custom/SystemPrompt_Custom.json`
  - `Prompt/Custom/DiplomacyDialoguePrompt_Custom.json`
  - `Prompt/Custom/PawnDialoguePrompt_Custom.json`
  - `Prompt/Custom/FactionPrompts_Custom.json`
  - `Prompt/Custom/SocialCirclePrompt_Custom.json`
- `SystemPrompt`：全局系统正文、环境提示词、动态注入标题、PromptPolicy。
- `DiplomacyDialoguePrompt`：外交对话正文、外交 API action 文案、响应格式、决策规则、外交模板。
- `PawnDialoguePrompt`：RPG/pawn 对话正文、persona bootstrap、RPG API 动作文案、fallback 模板、RimTalk 兼容模板。
- `FactionPrompts`：派系提示词集合，语义保持不变。
- `SocialCirclePrompt`：社交圈规则模板与 `publish_public_post` 文案。

### Prompt 模板新增字段（PromptTemplates）

- `DecisionPolicyTemplate`
- `TurnObjectiveTemplate`
- `TopicShiftRuleTemplate`

### 默认文件与持久化

- 默认值来源：`Prompt/Default/SystemPrompt_Default.json`
  - `PromptPolicySchemaVersion = 4`
  - `PromptPolicy` 对象（意图映射参数）
- 自定义持久化：`Prompt/Custom/system_prompt_config.json`
- 升级行为：检测到旧 schema 且 `ResetPromptCustomOnSchemaUpgrade=true` 时，清空旧覆盖并重建 V4 默认模板。

## 提示词设置全挂载（v0.3.103）

### RPG 提示词（Mod 设置 -> 人物对话）

- 新增分区：
  - `RPG 兜底模板`
  - `RPG API 模板`
- 可编辑项已覆盖 `Prompt/Default/RpgPrompts_Default.json` 对应字段：
  - `RoleSettingFallbackTemplate`
  - `FormatConstraintHeader`
  - `CompactFormatFallback`
  - `ActionReliabilityFallback`
  - `ActionReliabilityMarker`
  - `DecisionPolicyTemplate`
  - `TurnObjectiveTemplate`
  - `OpeningObjectiveTemplate`
  - `TopicShiftRuleTemplate`
  - `ApiActionPrompt` 全字段（full/compact 模板、动作行、动作名列表）
- 持久化路径：
  - 读取：`Prompt/Custom/RpgPrompts_Custom.json`（存在时）-> `Prompt/Default/RpgPrompts_Default.json`
  - 保存：仅写入 `Prompt/Custom/RpgPrompts_Custom.json`

### 系统提示词模板（Mod 设置 -> 外交对话 -> 高级）

- `PromptTemplates` 分区现在只负责外交模板，支持直接编辑以下字段：
  - 左侧模板字段列表已提高行高并改为垂直居中绘制，中文字段名不会再被裁切。
  - `FactGroundingTemplate`
  - `OutputLanguageTemplate`
  - `DiplomacyFallbackRoleTemplate`
  - `DecisionPolicyTemplate`
  - `TurnObjectiveTemplate`
  - `TopicShiftRuleTemplate`
  - `ApiLimitsNodeTemplate`
  - `QuestGuidanceNodeTemplate`
  - `ResponseContractNodeTemplate`
- 持久化路径：`Prompt/Custom/system_prompt_config.json`

### 社交圈 Prompt（Mod 设置 -> 外交对话 -> 高级 -> 社交圈 Prompt，v0.3.106）

- 专用分区统一编辑：
  - `PromptTemplates.SocialCircleActionRuleTemplate`
  - `ApiActions.publish_public_post.Description`
  - `ApiActions.publish_public_post.Parameters`
  - `ApiActions.publish_public_post.Requirement`
  - `ApiActions.publish_public_post.IsEnabled`
- 持久化路径：
  - 默认读取：`Prompt/Default/SystemPrompt_Default.json`
  - 修改保存：`Prompt/Custom/system_prompt_config.json`
- `PromptTemplates` 通用分区不再重复展示 `SocialCircleActionRuleTemplate`，避免入口分散。

## API 页最近对话 Token 用量（v0.3.29）

### 配置入口（Mod 设置 -> API配置）

- 该项显示在 API 页面底部：`最近一次对话Token使用量：xxxx（低/中/高）`。
- 统计范围：仅统计外交对话与 RPG 对话窗口发起的请求。
- 数据来源：
  - 优先读取响应 `usage` 对象中的 token 字段（兼容 `prompt_tokens/input_tokens/promptTokenCount`、`completion_tokens/output_tokens/candidatesTokenCount`、`total_tokens/totalTokenCount`）。
  - 缺失 usage，或 usage 与本地估算差异过大时，按“请求+响应文本（4字符≈1token）”回退估算，并显示“估算”标记。
- 分档阈值：低 `<=1200`，中 `1201~3000`，高 `>3000`。

## RPG-外交双向记忆链路（v0.3.29）

### 行为口径（固定策略）

- 离图触发仅认 `Pawn.ExitMap(bool, Rot4)`，不包含 `DeSpawn/死亡`。
- 外交会话结束定义为：外交窗口关闭且本次会话存在新增消息。
- 摘要策略：规则优先；低置信触发 LLM 回退；AI 不可用时回退规则摘要且不中断流程。

### 预算与注入限制（默认固定）

- 离图摘要池上限：`20`
- 外交会话摘要池上限：`20`
- RPG 动态注入条数：最近 `6` 条（按相关性排序）
- RPG 动态注入总长上限：`2200` 字符

### 提示词注入语义

- RPG 注入为运行时临时拼接，不调用 `SetPawnPersonaPrompt`，不会覆盖 Pawn Persona 持久化配置。
- 注入位置：原人格提示词后、API 规则前。
- 注入来源：同派系共享记忆（外交摘要 + 离图摘要 + 关键事件），保证同派系 Pawn 开聊可读一致长期记忆。

## 社交圈公开公告系统

### 入口变更（v0.3.14）

- 社交圈展示入口已从主窗口迁移到 外交对话窗口 内页签。
- 底部主窗口按钮已隐藏（不再提供主窗口入口）。

### 配置入口（Mod 设置 -> MOD设置 -> 社交圈设置）

- `EnableSocialCircle`
  - 总开关。关闭后不会自动生成、也不会处理公开公告动作。
- `SocialPostIntervalMinDays` / `SocialPostIntervalMaxDays`
  - 自动公告最小/最大间隔天数。
  - 默认：`5` / `7`。
- `EnablePlayerInfluenceNews`
  - 是否允许“玩家对话影响”进入社交圈（显式动作 + 关键词兜底）。
- `EnableAISimulationNews`
  - 是否启用系统按周期自动生成 AI 推演公告。
- `EnableSocialCircleAutoActions`
  - 是否启用“公告意图 -> 自动行动执行”。
  - 默认关闭，开启后仅在意图分达到阈值时尝试执行。
- 社交圈扩展影响事件池（原版 Core，中低威胁）
  - `ColdSnap` / `CropBlight` / `HeatWave` / `SolarFlare` / `Flashstorm`
  - 事件由帖子类别与情绪分布概率触发，并受世界状态与当前地图约束。

### 调试按钮

- `RimChat_SocialForceGenerateButton`（UI 按钮）
  - 始终显示在社交圈设置分区。
  - 点击后强制生成一条公告，并重排下一次自动生成时间。
  - 当社交圈总开关关闭或未进入存档时，按钮不可执行并显示提示。

## NPC 主动对话系统

### 配置入口（Mod 设置 -> MOD设置 -> UI 设置 -> NPC 主动对话设置）

- `EnableNpcInitiatedDialogue`
  - 总开关。关闭后不会再产生新的主动触发评估与投递。
- `NpcPushFrequencyMode`
  - 主动消息频率档位：`Low` / `Medium` / `High`。
  - 影响每次周期评估中的触发概率。
- `NpcQueueMaxPerFaction`
  - 每个派系的延迟队列上限。
  - 超限时按最早入队项淘汰。
- `NpcQueueExpireHours`
  - 队列消息过期时长（小时）。
  - 超时消息会在队列处理时自动丢弃。
- `EnableBusyByDrafted`
  - 忙碌判定：任一殖民者处于征召状态。
- `EnableBusyByHostiles`
  - 忙碌判定：玩家家园地图存在敌对已生成单位。
- `EnableBusyByClickRate`
  - 忙碌判定：6 秒窗口内左键点击次数达到阈值（默认 `>=12`）。
- `PawnRpgProtagonistCap`（v0.5.6）
  - PawnRPG 主动“手动主角名单”人数上限（默认 `20`，可调范围 `1~100`）。
  - 仅影响 PawnRPG 主动通道（含调试强制触发），不影响手动右键 RPG 开聊入口。
- `PawnRPG 主角名单`（v0.5.6）
  - 配置入口在同一分区，支持添加/移除/清空主角。
  - 按存档独立持久化（不是全局共享）。
  - 候选范围：玩家阵营全部 Pawn；运行时仍按可用性过滤。
- `RimChat_NpcPush_DebugForceTrigger`（UI 调试按钮）
  - 立即强制触发一条随机主动对话（用于联调；仍依赖 AI 配置可用）。

### PawnRPG 主动通道（v0.3.19）

- 与旧派系主动通道并行、互不替换；旧通道规则保持不变。
- 支持玩家派系内部主动对话（玩家派系 Pawn 可主动对其他玩家派系 Pawn 发起）。
- 复用当前 `NPC 主动对话设置` 配置分区（总开关、频率档、队列参数、忙碌判定开关）。
- 新增调试按钮：`RimChat_PawnRpgPush_DebugForceTrigger`
  - 强制触发一条 PawnRPG 主动对话用于联调。
  - 保留旧调试按钮，两者互不影响。
- 新增主角名单门控（v0.5.6）：主动目标只从手动主角名单中选择；列表为空时严格不投递。
- 主角失效成员策略（v0.5.6）：死亡/销毁/非玩家阵营成员仅运行时跳过，不自动清理配置条目。

### PawnRPG 固定运行策略（非配置项）

- 6 天单 NPC 节流：每个 NPC 成功投递后 `150000` ticks 内不再评估/发起。
- 3 天全局节流：非警告类成功投递后 `75000` ticks 内不再成功投递 PawnRPG 主动消息。
- 警告类例外：仅绕过 3 天全局节流，不绕过 6 天单 NPC 节流。
- 阈值调整：非亲密关系需 `Opinion >= 35`；条件触发低心情阈值为 `Mood <= 0.30`。
- 忙碌三重判定持续生效：`Drafted` / 敌对单位 / 高频点击。
- NPC 与玩家 Pawn 睡觉/昏迷/工作中时，触发入延迟队列等待。
- 队列策略保持：每派系默认上限 `3` 条，默认 `12` 小时过期。
- LLM 失败策略保持：每条消息重试 `1` 次，连续失败后丢弃。
- 冷却口径：仅“成功投递”才更新单 NPC 与全局冷却计数。
- 从 PawnRPG 主动信件打开对话时，会直接以该主动消息作为会话首句，不会再次请求 LLM 重新生成开场。

### 运行策略（非配置项）

- 本节描述旧派系主动通道固定策略（PawnRPG 通道规则见上节）。

- 常规触发评估间隔：`6000` ticks。
- 队列处理间隔：`600` ticks。
- 同派系主动发言冷却：成功后随机 `1~3` 天。
- 在线门控：仅 `Online` 可直接投递，离线/勿扰入队等待。
- 会话冷却门控：对话被 NPC 结束且仍在重联冷却中时，主动触发延迟到冷却结束（仍受队列过期影响）。
- LLM 失败策略：每条触发仅重试 `1` 次，失败后丢弃并写日志。

## 降好感策略按钮（外交窗口）

### 行为说明（当前版本无可配置开关）

- 仅在 **本轮 AI 回复导致净好感度下降** 时生效。
- LLM 可选返回 `strategy_suggestions`（必须 3 项）：
  - `short_label`
  - `trigger_basis`
  - `strategy_keywords`
  - `hidden_reply`
- 玩家只看到按钮策略，不直接看到 `hidden_reply`。
- 点击按钮会立即发送 `hidden_reply`；手动发送同样会清空本轮策略。
- 策略按钮仅对下一次发送有效，发送后立即失效。
- 若模型未返回合法 JSON 策略字段，客户端会发起一次补充请求；若补充请求仍是自然语言，将从叙述中提取策略句进行 3 按钮兜底。

## 在线状态系统

### 基础配置（Mod 设置 -> MOD设置 -> UI 设置 -> 在线状态设置）

- `EnableFactionPresenceStatus`
  - 是否启用派系在线状态系统。
  - 外交窗口标题栏/派系列表中的在线状态徽章已提高显示高度，中文状态标签不会再被截断。
- `PresenceCacheHours`
  - 关闭外交对话窗口后，按派系缓存在线状态的时长（小时）。
  - 默认：`2`。
  - 相关 Tiny 字体状态标签/提示现已统一增加留白，主界面与外交窗口中的中文状态文本更不容易被裁切。
- `PresenceForcedOfflineHours`
  - AI 执行 `go_offline` 后，保持强制离线的时长（小时）。
  - 默认：`24`。
- `PresenceNightBiasEnabled`
  - 是否启用夜间偏离线规则。
- `PresenceNightStartHour` / `PresenceNightEndHour`
  - 夜间判定时段（0-23）。
  - 默认：`22` 到 `6`。
- `PresenceNightOfflineBias`
  - 夜间在线时转离线的附加概率系数（0.0-1.0）。
  - 默认：`0.65`。

### 高级配置（展开“科技等级在线模板”）

- `PresenceUseAdvancedProfiles`
  - 是否启用按科技等级自定义在线模板。
  - 默认：`true`（默认启用科技等级影响在线时长）。
- 每个模板包含：
  - `PresenceOnlineStart_*`：在线起始小时。
  - `PresenceOnlineDuration_*`：在线总时长（小时）。

可配置模板：
- `Default`
- `Neolithic`
- `Medieval`
- `Industrial`
- `Spacer`
- `Ultra`
- `Archotech`

## 状态语义

- `Online`：可发送消息。
- `Offline`：窗口只读，不可发送消息。
- `DoNotDisturb`：窗口只读，不可发送消息。

状态颜色固定：
- 在线：绿
- 离线：灰
- 请勿打扰：红

## AI 动作协议

- `exit_dialogue`：结束当前对话轮次，保持当前在线状态。
- `go_offline`：结束对话并切换为离线。
- `set_dnd`：切换为请勿打扰并停止交互。

## RPG 对话固定策略（v0.3.137，非配置项）

- `ExitDialogueCooldown` 冷却固定为 `60000` ticks（1 天）。
- 当 RPG 对话达到 `5` 轮后，会执行一次 `80%` 记忆判定；成功时自动追加 `TryGainMemory`。
- `TryGainMemory` 默认记忆池已切换为 28 条 RimChat 分层记忆；旧 token / 旧 3 条自定义 DefName 会自动重映射到新的记忆 Def。
- 自动补记忆只会走正向递进池，第四档哲思/核心记忆仅供模型在真正高强度、人生转折式对话中显式选择。
- RPG NPC 可见对白会在运行时强制折叠为单行文本。
- 若当前消息正文超出 RPG 对话框文本区域，则会在打字结束后启用消息内分页；历史浏览时也可分页。
- 不会自动补全 Recruit；仅执行模型原始输出的 Recruit 动作。
- 对话窗口系统提示改为半透明面板，展示冷却剩余时长与记忆判定结果。

## RPG 独立人格 Prompt

### 配置入口（Mod 设置 -> 人物对话 -> 殖民地 Pawn 独立人格）

- 支持为每个殖民地人类 Pawn（殖民者/囚犯/奴隶）设置独立人格 Prompt。
- 该 Prompt 仅在该 Pawn 作为 RPG 对话目标时注入系统提示词。
- 清空文本即删除该 Pawn 的独立人格 Prompt。
- 配置按存档持久化（不会污染其他存档）。
- 该分区新增调试按钮：`RimChat_PawnRpgPush_DebugForceTrigger`，可直接强制触发一条 PawnRPG 主动对话用于联调。

### 首次加载旧存档自动 NPC 画像（v0.3.109）

- 触发时机：
  - 仅在“旧存档首次加载且尚未完成画像标记”时触发一次。
  - 新开档默认标记为已完成，不触发该流程。
  - 当引导版本升级时，会自动失效旧完成标记并重跑一次。
- 覆盖目标：
  - 当前地图中已存在的人形 Pawn（含玩家派系）。
  - 已知可见派系的领袖 Pawn。
- 生成链路：
  - 使用 `PromptPersistenceService.BuildPawnPersonaBootstrapProfile(Pawn)` 组装人格专用精简上下文。
  - 仅包含背景、特质、核心技能、派系角色与意识形态。
  - 显式排除健康/需求/心情/伤病/装备/基因/临时事件等非人格信息。
  - 异步串行调用 LLM，按 `Prompt/Default/RpgPrompts_Default.json` 中的人格模板配置生成人格文本。
  - 模板格式固定：
    - `He/She is a [core temperament] person who tends to [emotional pattern], usually handles situations by [behavioral strategy], because deep down they seek [core motivation], but this also makes them [defense/weakness], often leading to [personality cost].`
  - 示例参考：
    - `He is a calm and analytical person who rarely shows his emotions and tends to approach problems through careful observation and planning, because deep down he seeks control and security, but this also makes him distant and slow to trust others.`
  - 输出目标：保持单行、人格导向、短句表达，并统一使用 Pawn 对应的人称代词。
- 写入策略：
  - 复用现有 `SetPawnPersonaPrompt` 持久化字段（与手动编辑同源）。
  - 仅对“当前为空”的 Pawn 独立人格字段写入，不覆盖已有自定义文本。
  - 新增 RimTalk 自动复制优先级（v0.5.10）：
    - 在 AI 生成人格前，先尝试用 `RimTalkPersonaCopyTemplate` 渲染人格并写入。
    - 仅对殖民地人类 Pawn 生效（`pawn.Faction == Faction.OfPlayer`）。
    - 模板为 strict Scriban 语法，必须使用 `{{ pawn.personality }}`。
    - RimTalk 设置页（RPG 通道）新增手动按钮“立即复制全部 RimTalk 人格”，可一键同步当前殖民地 Pawn 的 RimTalk 人格到 RimChat，并返回更新/清空/无变化/跳过统计。
    - 渲染为空或失败时，立即抛异常并中断链路（无 fallback）。
  - 失败会重试；重试失败后写入模板化兜底人格文本，避免留空。

## 环境提示词系统（v0.3.23）

### 配置入口（Mod 设置 -> 外交对话 -> 环境提示词）

- `Worldview.Enabled` / `Worldview.Content`
  - 世界观全局提示词层。启用后在外交与 RPG 两条系统提示词链路前置注入。
- `SceneSystem.Enabled`
  - 场景提示词总开关。
- `SceneSystem.MaxSceneChars`
  - 单条场景内容硬上限（默认 `1200` 字符）。
- `SceneSystem.MaxTotalChars`
  - 场景层总字符硬上限（默认 `4000` 字符）。
- `SceneSystem.PresetTagsEnabled`
  - 是否启用系统自动标签（`channel:*` / `source:*` / `scene:*` 及关系、心情、健康等标签）。
- `EventIntelPrompt.Enabled`
  - 事件记忆注入总开关（默认开启）。
- `EventIntelPrompt.ApplyToDiplomacy` / `EventIntelPrompt.ApplyToRpg`
  - 事件记忆应用通道开关。
- `EventIntelPrompt.IncludeMapEvents`
  - 是否注入公开地图事件（如寒潮、热浪、枯萎病、袭击、殖民者死亡等信件事件）。
- `EventIntelPrompt.IncludeRaidBattleReports`
  - 是否注入袭击聚合战报（攻击方/守方死亡 + 守方倒地峰值）。
- `EventIntelPrompt.DaysWindow`
  - 注入读取窗口天数（默认 `15`）。
- `EventIntelPrompt.MaxStoredRecords`
  - 事件账本总存储上限（默认 `50`，FIFO 裁剪）。
- `EventIntelPrompt.MaxInjectedItems`
  - 单次注入最多条目数（默认 `8`）。
- `EventIntelPrompt.MaxInjectedChars`
  - 事件记忆块最大字符数（默认 `1200`）。

### 共享提示词模板（PromptTemplates，v0.3.64）

- `PromptTemplates.Enabled`
  - 保留该字段用于配置展示；严格模式下不会回退硬编码文本，模板缺失会直接报错。
- `PromptTemplates.FactGroundingTemplate`
  - 作用于 `fact_grounding` 节点（外交/RPG 共用）。
- `PromptTemplates.OutputLanguageTemplate`
  - 作用于 `output_language` 节点（外交/RPG 共用）。
- `PromptTemplates.DiplomacyFallbackRoleTemplate`（v0.3.65）
  - 在无派系专属 Prompt 时作为严格模板节点；模板为空将抛 `TemplateMissing`。
- `PromptTemplates.SocialCircleActionRuleTemplate`（v0.3.105）
  - 注入外交分层 prompt 的 `social_circle_action_rule` 节点，用于约束 `publish_public_post` 的使用场景与语义一致性。
  - v0.3.106 起，编辑入口迁移到独立“社交圈 Prompt”分区。
- `Prompt/Default/RpgPrompts_Default.json`（v0.3.120）
  - RPG 角色设定、格式约束、动作可靠性、开场目标与 topic shift 默认文本改由该文件统一提供，不再从外交 `PromptTemplates` 读取。
- `PromptTemplates.ApiLimitsNodeTemplate`（v0.3.66）
  - 包装 `api_limits` 节点动态正文，默认 `{{ dialogue.api_limits_body }}`。
- `PromptTemplates.QuestGuidanceNodeTemplate`（v0.3.66）
  - 包装 `quest_guidance` 节点动态正文，默认 `{{ dialogue.quest_guidance_body }}`。
- `PromptTemplates.ResponseContractNodeTemplate`（v0.3.66）
  - 包装 `response_contract` 节点动态正文，默认 `{{ dialogue.response_contract_body }}`。

支持占位符（仅命名空间变量）：
- `{{ ctx.channel }}`：`diplomacy` 或 `rpg`
- `{{ ctx.mode }}`：`manual` 或 `proactive`
- `{{ system.target_language }}`：当前输出语言
- `{{ world.faction.name }}`、`{{ pawn.initiator.name }}`、`{{ pawn.target.name }}`

说明：
- 未识别占位符会抛 `PromptRenderException(UnknownVariable)`，不会保留原文继续运行。
- 建议将这两段视为“可本地化模板文本”，按语言维护不同配置文件。
- Prompt 工作台与变量浏览器中的 tooltip 典型值现在按变量精确配置优先；当某变量只有 1-2 个代表值时，只显示实际数量。
- `v0.3.67` 起，长模板默认文本以 `Prompt/Default/SystemPrompt_Default.json` 为准，不再在代码构造函数重复维护。
- `v0.3.68` 起，RPG 默认提示词与部分动作描述在代码端由 `PromptTextConstants` 统一提供，避免同文案多处硬编码。
- `v0.3.69` 起，回复合约段落标题文本也由 `PromptTextConstants` 统一提供，减少 `AppendSimpleConfig/AppendAdvancedConfig` 重复文案。
- `v0.3.70` 起，若旧配置中模板字段为空，系统会在加载时从默认模板文件自动回填并保存。
- `v0.3.98` 起，RPG 默认提示词主文案改为以 `Prompt/Default/RpgPrompts_Default.json` 为准，`PromptTextConstants` 与 RPG API 动作说明文本统一从该文件读取（含层级构建 fallback 文案）。
- `v0.3.101` 起，RPG 提示词覆盖值仅持久化到 `Prompt/Custom/RpgPrompts_Custom.json`，不再走 `ModSettings` 配置文件；默认读取链路固定为 `Prompt/Custom`（存在时）→ `Prompt/Default/RpgPrompts_Default.json`。
- `v0.3.102` 起，启动时会一次性清理 `Config/Mod_*.xml` 中遗留的 prompt 文本字段（并写入 marker），防止旧字段继续污染运行配置来源。

### 环境参数开关（EnvironmentContextSwitches）

- `Enabled`
  - 环境参数层总开关。
- `IncludeTime`
- `IncludeDate`
- `IncludeSeason`
- `IncludeWeather`
- `IncludeLocationAndTemperature`
- `IncludeTerrain`
- `IncludeBeauty`
- `IncludeCleanliness`
- `IncludeSurroundings`
- `IncludeWealth`

以上项按开关决定是否注入对应环境上下文信息（外交与 RPG 共用同一套环境参数层）。

### 场景条目配置（SceneEntries[]）

每条场景包含：
- `Enabled`：条目开关
- `ApplyToDiplomacy`：是否作用于外交通道
- `ApplyToRPG`：是否作用于 RPG 通道
- `Priority`：优先级（高到低注入）
- `MatchTags[]`：匹配标签（必须 ALL 命中）
- `Content`：场景提示词文本

### RPG 深度 Pawn 参数开关（RpgSceneParamSwitches）

- `IncludeSkills`
- `IncludeEquipment`
- `IncludeGenes`
- `IncludeNeeds`
- `IncludeHediffs`
- `IncludeRecentEvents`
- `IncludeColonyInventorySummary`
- `IncludeHomeAlerts`
- `IncludeRecentJobState`
- `IncludeAttributeLevels`

这些开关控制 RPG 动态注入里是否包含对应深度参数项。

### 事件记忆可知边界（Event Intel Visibility）

- `PublicKnown`：公开地图事件可注入摘要（不暴露不可知细节）。
- `DirectKnown`：派系直接参与事件（如袭击与战报）可注入完整聚合伤亡摘要。
- 过滤依据：`KnownFactionIds` + `IsPublic`。

### 运行规则

- 注入顺序固定：`世界观 -> 环境参数层 -> 近期世界事件与战报 -> 场景层 -> 旧提示词体系`。
- 场景命中策略：全部命中条目都注入，按优先级降序。
- 长度策略：先裁剪单条，再按总量上限裁剪。
- 通道覆盖：外交手动/外交主动/RPG手动/RPG主动全部共用同一场景库。
- 事件账本：独立 `GameComponent` 持久化，默认跟随存档保存/读档恢复。
- 事实约束：系统会追加事实约束块；AI 仅可基于已知信息回复，对无依据说法需明确不确定并提出质疑。

### 双通道场景标签与预览一致性（v0.3.33）

- `DiplomacyManualSceneTagsCsv`
  - 外交通道（手动对话）追加场景标签 CSV（支持 `, ; |` 分隔），在构建最终系统提示词时作为 `additionalSceneTags` 注入。
- `RpgManualSceneTagsCsv`
  - RPG 通道（手动对话）追加场景标签 CSV，构建最终系统提示词时同样作为 `additionalSceneTags` 注入。
- 提示词设置页与 RPG 设置页的预览现在都走最终组装入口（`PromptPersistenceService.BuildFullSystemPrompt/BuildRPGFullSystemPrompt`），不再使用手工拼接预览。
- 预览支持独立设置：
  - `PromptPreviewUseProactiveContext` / `PromptPreviewSceneTagsCsv`
  - `RpgPromptPreviewUseProactiveContext` / `RpgPromptPreviewSceneTagsCsv`

### Prompt 持久化路径（v0.3.89）

- 统一目录结构：`Mods/RimChat/Prompt/{Default,Custom,NPC}`。
- `system_prompt_config.json` 持久化路径：`Mods/RimChat/Prompt/Custom/system_prompt_config.json`（不可写时回退到配置目录）。
- `FactionPrompts.json` 持久化路径：`Mods/RimChat/Prompt/Custom/FactionPrompts.json`（旧 `Config/RimChat/FactionPrompts.json` 自动迁移）。
- NPC 记忆路径：
  - `Prompt/NPC/<saveKey>_<saveName>/rpg_npc_dialogues/npc_<pawnId>_<pawnName>.json`
    - RPG NPC 档案写盘结构为 `sessions`（会话级）；旧版顶层 `turns` 仅用于兼容读取并增量迁移。
    - 保留策略：仅最近一段 `turnCount>=2` 会话保留全文，其他已结束会话压缩为一句摘要。
    - 压缩策略：严格 LLM 请求；失败时不压缩、保留原文，并在后续触达/读取/存档时按冷却重试。
    - 新增档案字段：`saveKey`（用于按存档隔离读取）。
  - `Prompt/NPC/<saveKey>_<saveName>/leader_memories/<factionSafeName>_<factionLoadId>.json`
  - 旧 `save_data/<saveName>/...` 与 `Prompt/NPC/Save_*_Default/rpg_npc_dialogues` 会在读取时自动备份并迁移到新路径。
- `build.ps1` 部署阶段会备份并还原目标目录下 `Prompt/Custom` 与 `Prompt/NPC`，防止历史数据被清空。



## 场景模板变量（v0.3.34）
- SceneEntries[].Content 支持 `{{variable}}` 运行时替换。
- 内置变量：`scene_tags`、`environment_params`、`recent_world_events`、`colony_status`、`colony_factions`、`current_faction_profile`、`rpg_target_profile`、`rpg_initiator_profile`。
- 未识别变量会在预览诊断中以 `unknown_vars` 提示。
- 提示词预览新增诊断段：展示场景命中/跳过原因与截断标记。

## Prompt Output Language Settings (v0.3.44)
- PromptLanguageFollowSystem (default true).
- PromptLanguageOverride (default empty).
- UI path: Mod Settings -> API -> Output Language.
- Behavior: injects output-language requirement for diplomacy and RPG prompts; JSON keys/action IDs remain unchanged.

## RimTalk Compatibility Settings (v0.4.11)
- `EnableRimTalkPromptCompat`
  - Default: `true`
  - Master switch for RimTalk compatibility bridge (prompt rendering + summary push).
- `RimTalkSummaryHistoryLimit`
  - Default: `10`
  - Clamp range: `1..30`
  - Controls rolling size of `rimchat_recent_session_summaries`.
- `RimTalkPresetInjectionMaxEntries`
  - Default: `0`
  - Clamp range: `0..200`
  - `0` means unlimited.
  - Controls max number of active RimTalk preset mod entries injected into RPG prompt block.
- `RimTalkPresetInjectionMaxChars`
  - Default: `0`
  - Clamp range: `0..200000`
  - `0` means unlimited.
  - Controls total char budget of injected active RimTalk preset mod-entry block.
- `RimTalkCompatTemplate`
  - Default: built-in minimal Scriban-safe template with latest/recent summary variables.
  - Used by both diplomacy and RPG prompt pipelines.
  - Supports RimTalk Scriban syntax and plugin variables.
  - On render failure, runtime throws `PromptRenderException` and blocks request flow.
- `RimTalkPersonaCopyTemplate`（v0.5.10）
  - Default: `{{ pawn.personality }}`
  - 用于 RPG 人格自动复制链路（仅殖民地人类 Pawn、仅填空）。
  - strict Scriban 模式下仅支持 `{{ pawn.personality }}`。
- Runtime note:
  - Previous hardcoded preset-mod-entry limits (`12 entries` / `4200 chars`) are replaced by the two settings above.
  - Defaults are now unlimited unless user sets explicit limits.
- 持久化路径（v0.3.106）：
  - 读取：`Prompt/Custom/PawnDialoguePrompt_Custom.json`（存在时）-> `Prompt/Default/PawnDialoguePrompt_Default.json`
  - 保存：仅写入 `Prompt/Custom/PawnDialoguePrompt_Custom.json`
  - 兼容迁移：当 Custom 文件不存在时，旧 ModSettings 中 RimTalk 相关值会一次性迁移到 Custom 文件。

### UI Entry
- Mod Settings -> RimTalk（独立页签）-> 频道选择（Diplomacy / RPG）-> RimTalk Prompt Compatibility.
- RPG 频道下新增 `RimTalkPersonaCopyTemplate` 编辑框，用于人格自动复制模板。
- RPG 频道下新增“立即复制全部 RimTalk 人格”手动按钮，用于立即执行全殖民地人格同步。

### Variable Browser & Entry Writer
- RimTalk Variable Browser:
  - Shows RimChat built-in summary variables and RimTalk-registered custom/plugin variables.
  - Supports one-click insertion of selected variable token into `RimTalkCompatTemplate`.
- RimTalk Prompt Entry Creator:
  - Allows writing/updating active RimTalk preset entries from RimChat settings.
  - Editable fields: `EntryName`, `AfterEntryName`, `Role`, `Position`, `InChatDepth`, `Content`.
  - Write result is returned via `RimTalkPromptEntryWriteResult` mapped to UI messages.

### Runtime Summary Keys (RimTalk Global Variables)
- `rimchat_last_session_summary`
- `rimchat_last_diplomacy_summary`
- `rimchat_last_rpg_summary`
- `rimchat_recent_session_summaries`

## Prompt Domain Schema + Runtime Fail-Fast（v0.7.24）

- `SystemPromptDomainConfig.PromptDomainSchemaVersion`
  - 单锚点域版本号（当前实现：`1`）。
  - 启动加载时若低于当前版本，会触发一次性迁移写回，并记录迁移日志（旧版本 -> 新版本 + 修复摘要）。

- 启动期自愈行为
  - 触发条件：Prompt 域配置语义不完整（例如 `ApiActions` 缺关键动作、`ResponseFormat.JsonTemplate` 为空、关键节点模板为空）。
  - default-only 读取链路：分域直接反序列化失败时，自动切换到 default-only 聚合 JSON 重建后再做语义校验。
  - 自愈步骤（default-only 语义通过时）：
    1. 先备份 `Prompt/Custom` 到 `Prompt/Custom/_backup/<yyyyMMdd_HHmmss>/`。
    2. 走 default-only 加载路径（不读 custom）重建运行配置。
    3. 自动写回 custom，确保后续启动幂等。
  - default-only 语义失败策略：
    1. 若已有缓存配置，保留缓存并阻断 auto-heal 写回。
    2. 若无缓存配置，抛 `PromptRenderException` fail-fast 阻断。

- 运行期 fail-fast（外交/策略/RPG 主链路）
  - 关键 runtime 节点为空时直接抛 `PromptRenderException` 并阻断请求（不再静默降级）。
  - `ResponseFormat.JsonTemplate` 为空时直接阻断。
  - `send_image` 禁用时仅以 blocked 动作体现，不影响其他外交动作注入。

- 动作源规则调整
  - `ApiActions` 仅取自外交域 `DiplomacyDialoguePrompt_*`。
  - 社交域 `SocialCirclePrompt_*` 仅负责社交模板文案，不再参与动作表合并。

## Text Integrity Runtime Policy（v0.7.48）

- 可见对白完整性检测（外交/RPG）
  - 触发范围：`DialogueUsageChannel.Diplomacy`、`DialogueUsageChannel.Rpg`。
  - 重试策略：命中乱码/碎片规则后，同请求链自动重试 1 次。
  - 失败策略：二次仍异常时回退本地沉浸兜底文本，不展示原异常输出。

- 摘要入库质量门控
  - 入库前统一净化：控制字符清理、空白归一、长度裁剪。
  - 异常判定：replacement char、可打印比异常、碎片化比例异常等规则。
  - 修复策略：异常摘要仅尝试 1 次修复请求；修复失败则不入库并记录告警。

- Faction 风格占位符
  - Faction Prompt 检测到 `{{ ... }}` 时走 strict Scriban 渲染。
  - 运行时注入 settlement 变量：
    - `world.faction_settlement_summary`
    - `world.faction_settlement.settlement_count`
    - `world.faction_settlement.nearest_to_player_home`
    - `world.faction_settlement.all_settlements`
  - Faction 模板中可写兼容占位符：`{{ SettlementCount }}` / `{{ NearestToPlayerHome }}` / `{{ AllSettlements }}`（渲染前映射到规范变量）。





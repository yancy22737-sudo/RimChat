# RimChat 外部配置说明（v0.3.29）

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
  - `faction_settlement_summary`（据点数量与全量据点列表）
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
- 图标资源：运行时使用 `1.6/Textures/UI/CommsToggleIcon.png`（来源 `About/icon.png`）。

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
    - 模板默认 `pawn.personality`（也支持 `{{pawn.personality}}`）。
    - RimTalk 设置页（RPG 通道）新增手动按钮“立即复制全部 RimTalk 人格”，可一键同步当前殖民地 Pawn 的 RimTalk 人格到 RimChat，并返回更新/清空/无变化/跳过统计。
    - 渲染为空或失败时，不中断流程，继续走原有 AI 重试与 fallback。
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
  - 模板渲染总开关。关闭后回退旧版硬编码拼接文本。
- `PromptTemplates.FactGroundingTemplate`
  - 作用于 `fact_grounding` 节点（外交/RPG 共用）。
- `PromptTemplates.OutputLanguageTemplate`
  - 作用于 `output_language` 节点（外交/RPG 共用）。
- `PromptTemplates.DiplomacyFallbackRoleTemplate`（v0.3.65）
  - 在无派系专属 Prompt 时，作为外交通道角色兜底文本。
- `PromptTemplates.SocialCircleActionRuleTemplate`（v0.3.105）
  - 注入外交分层 prompt 的 `social_circle_action_rule` 节点，用于约束 `publish_public_post` 的使用场景与语义一致性。
  - v0.3.106 起，编辑入口迁移到独立“社交圈 Prompt”分区。
- `Prompt/Default/RpgPrompts_Default.json`（v0.3.120）
  - RPG 角色设定、格式约束、动作可靠性、开场目标与 topic shift 默认文本改由该文件统一提供，不再从外交 `PromptTemplates` 读取。
- `PromptTemplates.ApiLimitsNodeTemplate`（v0.3.66）
  - 包装 `api_limits` 节点动态正文，默认 `{{api_limits_body}}`。
- `PromptTemplates.QuestGuidanceNodeTemplate`（v0.3.66）
  - 包装 `quest_guidance` 节点动态正文，默认 `{{quest_guidance_body}}`。
- `PromptTemplates.ResponseContractNodeTemplate`（v0.3.66）
  - 包装 `response_contract` 节点动态正文，默认 `{{response_contract_body}}`。

支持占位符（`{{variable}}`）：
- `{{channel}}`：`diplomacy` 或 `rpg`
- `{{mode}}`：`manual` 或 `proactive`
- `{{target_language}}`：当前输出语言
- `{{faction_name}}`、`{{initiator_name}}`、`{{target_name}}`

说明：
- 未识别占位符会保留原文，便于排错。
- 建议将这两段视为“可本地化模板文本”，按语言维护不同配置文件。
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
  - `Prompt/NPC/<saveName>/rpg_npc_dialogues/npc_<pawnId>.json`
    - RPG NPC 档案写盘结构为 `sessions`（会话级）；旧版顶层 `turns` 仅用于兼容读取并增量迁移。
    - 保留策略：仅最近一段 `turnCount>=2` 会话保留全文，其他已结束会话压缩为一句摘要。
    - 压缩策略：严格 LLM 请求；失败时不压缩、保留原文，并在后续触达/读取/存档时按冷却重试。
  - `Prompt/NPC/<saveName>/leader_memories/<factionSafeName>_<factionLoadId>.json`
  - 旧 `save_data/<saveName>/...` 目录会在读取时自动迁移到新路径。
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
  - On render failure, runtime falls back to raw template text (request flow continues).
- `RimTalkPersonaCopyTemplate`（v0.5.10）
  - Default: `pawn.personality`
  - 用于 RPG 人格自动复制链路（仅殖民地人类 Pawn、仅填空）。
  - 支持 `pawn.personality` 或 `{{pawn.personality}}` 写法。
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




# 囚犯信息卡气泡样式文档

## 1. 概述

囚犯信息卡气泡是 RimChat 模组中用于在外交对话界面展示单个或多个囚犯赎金信息的 UI 组件。

| 类型   | 渲染方式                               | 样式特点                |
| ---- | ---------------------------------- | ------------------- |
| 单个囚犯 | `DrawOutboundPrisonerInfoBubble()` | 横向紧凑布局：左侧缩略图 + 右侧证词 |
| 多个囚犯 | `BuildRansomBatchInfoCardBody()`   | 文本列表样式，通过语言键渲染      |

***

## 2. 单个囚犯信息卡气泡

### 2.1 布局结构

```
┌─────────────────────────────────────────────────────┐
│ [发送者名称]                          [时间戳]       │  ← 头部 (Header)
├─────────────────────────────────────────────────────┤
│ ┌─────────┐  姓名：{name}                            │
│ │         │  年龄：{age}                            │
│ │  缩略图  │  健康：{health}                         │  ← 内容区 (Body)
│ │         │  意识：{consciousness}                   │
│ └─────────┘  所属派系：{faction}                    │
│              ID：{id}                               │
│              证词：{quote}                          │
└─────────────────────────────────────────────────────┘
```

### 2.2 核心常量

| 常量名                                   | 值             | 说明       |
| ------------------------------------- | ------------- | -------- |
| `OutboundPrisonerThumbMinSize`        | 140f          | 缩略图最小尺寸  |
| `OutboundPrisonerThumbMaxSize`        | 190f          | 缩略图最大尺寸  |
| `OutboundPrisonerCardPadding`         | 8f            | 内容区内边距   |
| `OutboundPrisonerHeaderHeight`        | 16f           | 头部高度     |
| `OutboundPrisonerHeaderTopPadding`    | 6f            | 头部上边距    |
| `OutboundPrisonerHeaderGap`           | 1f            | 头部与内容区间距 |
| `OutboundPrisonerImageTextGap`        | 6f            | 缩略图与文本间距 |
| `OutboundPrisonerBottomPadding`       | 6f            | 气泡底部内边距  |
| `OutboundPrisonerMinBubbleHeight`     | 110f          | 气泡最小高度   |
| `OutboundPrisonerThumbnailZoomFactor` | 1.75f         | 缩略图放大因子  |
| `OutboundPrisonerThumbnailPivot`      | (0.5f, 0.58f) | 缩略图中心点   |

### 2.3 颜色方案

| 元素    | 颜色值                                  | 说明       |
| ----- | ------------------------------------ | -------- |
| 气泡背景  | `PlayerBubbleColor` (#91ed61 绿色)     | 玩家侧气泡    |
| 发送者名称 | `new Color(0.2f, 0.3f, 0.15f)`       | 深绿色      |
| 时间戳   | `new Color(0.2f, 0.3f, 0.15f, 0.7f)` | 半透明深绿    |
| 文本颜色  | `new Color(0.1f, 0.1f, 0.1f)`        | 近黑色      |
| 阴影    | `new Color(0f, 0f, 0f, 0.12f)`       | 12%透明度黑色 |

### 2.4 主要方法

| 方法                                       | 行号  | 职责           |
| ---------------------------------------- | --- | ------------ |
| `DrawOutboundPrisonerInfoBubble()`       | 93  | 绘制单个囚犯气泡容器   |
| `CalculateImageMessageHeight()`          | 167 | 计算气泡高度（分支处理） |
| `ResolveOutboundPrisonerThumbSize()`     | 260 | 动态计算缩略图尺寸    |
| `ResolveOutboundPrisonerCaptionWidth()`  | 265 | 计算证词文本宽度     |
| `CalculateOutboundPrisonerBodyHeight()`  | 270 | 计算内容区高度      |
| `MeasureOutboundPrisonerCaptionHeight()` | 278 | 测量证词多行文本高度   |
| `GetOutboundPrisonerProofCaption()`      | 292 | 获取并验证证词内容    |
| `DrawInlineImageContentFillZoomed()`     | 231 | 绘制中心裁切放大缩略图  |

### 2.5 证词字段顺序

**当前顺序（中/英）**：

```
姓名：/ Name:
年龄：/ Age:
健康：/ Health:
意识：/ Consciousness:
所属派系：/ Source faction:
ID：/ ID:
证词：/ Quote:
```

**历史兼容顺序**：

```
姓名：/ Name:
年龄：/ Age:
健康：/ Health:
意识：/ Consciousness:
所属派系：/ Source faction:
证词：/ Quote:
```

***

## 3. 多个囚犯信息卡气泡

### 3.1 布局结构

```
┌─────────────────────────────────────────────────────┐
│ 【囚犯存活证明】                                     │  ← 标题 (RimChat_RansomBatchCardBody)
│                                                     │
│  1. {name1} (ID:{id1})| ~ {price1}银 | 健康度 {h1}% │
│  2. {name2} (ID:{id2})| ~ {price2}银 | 健康度 {h2}% │  ← 列表 (RimChat_RansomBatchListLine)
│  3. {name3} (ID:{id3})| ~ {price3}银 | 健康度 {h3}% │
│  ...                                                │
└─────────────────────────────────────────────────────┘
```

### 3.2 语言键

| 键名                                   | 说明       |
| ------------------------------------ | -------- |
| `RimChat_RansomBatchCardBody`        | 批量卡片标题模板 |
| `RimChat_RansomBatchListLine`        | 单行囚犯信息模板 |
| `RimChat_RansomBatchNeedOfferSystem` | 批量报价提示   |

### 3.3 批量卡片模板（ZH）

```xml
<RimChat_RansomBatchCardBody>【囚犯存活证明】
派系：{0} | 在押 {1} 名
{2}</RimChat_RansomBatchCardBody>

<RimChat_RansomBatchListLine>{0}. {1} (ID:{2})| ~ {3}银 | 健康度 {4}% | 器官：{5}</RimChat_RansomBatchListLine>
```

### 3.4 批量卡片模板（EN）

```xml
<RimChat_RansomBatchCardBody>[Hostage Proof of Life]
Faction: {0} | {1} Prisoner(s)
{2}</RimChat_RansomBatchCardBody>

<RimChat_RansomBatchListLine>{0}. {1} (ID:{2})| ~ {3} silver | Health {4}% | Organs: {5}</RimChat_RansomBatchListLine>
```

### 3.5 主要方法

| 方法                                   | 行号  | 职责         |
| ------------------------------------ | --- | ---------- |
| `PublishRansomBatchInfoCard()`       | 431 | 发布批量囚犯信息卡  |
| `BuildRansomBatchInfoCardBody()`     | 466 | 构建批量卡片文本内容 |
| `HandleRansomBatchTargetsSelected()` | 346 | 处理多囚犯选择    |
| `TryBuildRansomBatchQuoteEntries()`  | 380 | 构建批量报价条目   |

***

## 4. 渲染器入口

| 方法                           | 文件                                | 说明        |
| ---------------------------- | --------------------------------- | --------- |
| `DrawImageMessageBubble()`   | ImageRendering.cs:41              | 图片消息气泡分发器 |
| `DrawRoundedMessageBubble()` | Dialog\_DiplomacyDialogue.cs:1134 | 圆角消息气泡主入口 |
| `CalculateMessageHeight()`   | Dialog\_DiplomacyDialogue.cs:1044 | 消息高度计算    |

消息类型判断：

```csharp
if (IsOutboundPrisonerInfoMessage(msg))
    DrawOutboundPrisonerInfoBubble(msg, rect);  // 单个囚犯
else if (IsRansomBatchMessage(msg))
    DrawRansomBatchBubble(msg, rect);           // 多个囚犯
```

***

## 5. 气泡宽度范围

| 类型   | 最小宽度 | 最大宽度 | 计算公式                                             |
| ---- | ---- | ---- | ------------------------------------------------ |
| 单个囚犯 | 380f | 520f | `Mathf.Clamp(contentWidth * 0.30f, 140f, 190f)`  |
| 缩略图  | -    | -    | 占内容区 30%，范围 140-190f                             |
| 文本区  | 110f | -    | `Mathf.Max(110f, contentWidth - thumbSize - 6f)` |

***

## 6. 样式设计原则

1. **横向紧凑布局** - 缩略图与证词并排，减少垂直高度占用
2. **中心裁切放大** - 缩略图使用 `DrawInlineImageContentFillZoomed()` 实现头像放大效果
3. **颜色纯净** - 绘制缩略图前强制重置 `GUI.color = Color.white` 防止污染
4. **玩家视觉归属** - 囚犯信息卡虽为系统语义，但使用玩家配色和头像

***

## 7. 相关文件

| 文件                                                               | 职责         |
| ---------------------------------------------------------------- | ---------- |
| `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`          | 单个囚犯气泡渲染   |
| `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomSelection.cs` | 批量囚犯信息卡构建  |
| `RimChat/UI/Dialog_DiplomacyDialogue.cs`                         | 气泡分发与主渲染逻辑 |
| `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`         | 中文语言键      |
| `1.6/Languages/English/Keyed/RimChat_Keys.xml`                   | 英文语言键      |

<br />

# 囚犯信息卡气泡优化实现计划

## 1. 目标

在 RimWorld + Unity IMGUI 的技术约束下，对“囚犯信息卡气泡”进行一次面向信息密度、视觉层级与可维护性的重构，使其同时满足以下要求：

1. **更紧凑**：在不损失关键语义的前提下，显著压缩气泡高度。
2. **更高效**：玩家能快速识别囚犯身份、健康状态、来源派系与赎金价值。
3. **更惊艳**：从单纯的“信息陈列”升级为具有决策辅助能力的 UI 组件。
4. **更稳定**：修复长文本截断、字段挤压、缩略图裁切异常、不同分辨率下的布局漂移。
5. **更可维护**：把布局常量、文本测量、信息格式化、绘制逻辑拆分清楚，方便后续迭代。

***

## 2. 现状分析

当前囚犯信息卡气泡已经具备明确的功能分支：单个囚犯使用横向紧凑布局，多个囚犯使用批量文本列表。文档中也已经列出了关键方法、布局常量、证词字段顺序和批量卡片模板，说明这个组件已经有较好的业务分层基础。fileciteturn1file0

### 2.1 当前优点

- 单个囚犯信息卡采用左图右文的横向结构，天然适合压缩高度。fileciteturn1file0
- 缩略图支持中心裁切放大，人物主体展示效果较强。fileciteturn1file0
- 单个囚犯与批量囚犯的渲染入口已分开，具备重构空间。fileciteturn1file0
- 文本字段顺序已明确，便于做优先级重排。fileciteturn1file0

### 2.2 当前痛点

- 单个囚犯卡片虽然是横向布局，但字段仍偏“清单式堆叠”，视觉焦点不够集中。
- 证词文本可能成为最长字段，容易把整体高度拉高。
- 批量囚犯卡目前以纯文本列表为主，信息密度不低，但视觉吸引力和层级感不足。fileciteturn1file0
- 缩略图最小尺寸 140f、最大尺寸 190f，配合 1.75f 放大因子，视觉存在感很强，但也容易压缩文本区域。fileciteturn1file0
- 当前样式偏功能型，不够像“高价值情报卡”或“赎金档案卡”。

***

## 3. 设计方向

建议把囚犯信息卡的视觉定位，从“普通角色信息展示”升级为：

> **囚犯档案卡 / 赎金情报卡 / 生存证明卡**

设计核心是：

- 左侧用头像建立身份识别。
- 右侧用层级清晰的属性条展示状态。
- 最重要的信息优先出现：姓名、健康、意识、派系、价格预期。
- 证词不再是静态长句堆叠，而是作为摘要或可展开内容。

***

## 4. 最终视觉方案

推荐采用 **“主档案 + 证词摘要 + 状态徽标”** 的三段式结构。

### 4.1 单个囚犯卡

建议结构如下：

```
┌─────────────────────────────────────────┐
│ [发送者]                          [时间] │
├─────────────────────────────────────────┤
│ ┌──────────┐  姓名：XXX   健康：72%      │
│ │ 头像     │  年龄：XX     意识：正常      │
│ │          │  派系：XXX   ID：XXXX        │
│ └──────────┘  证词摘要：XXXXXXXXXXXX      │
│              [状态徽标：可赎 / 重伤 / 价值高] │
└─────────────────────────────────────────┘

```

### 4.2 多个囚犯卡

建议从纯文本列表升级为“紧凑批量条目列表”，每一行是：

```
[序号] 姓名  |  健康  |  预估赎金  |  派系  |  状态标签

```

并且支持折叠、展开、按价值排序。

***

## 5. 信息架构重组

### 5.1 单个囚犯信息卡的新优先级

信息优先级建议重排为：

1. **姓名 / 身份主标题**
2. **健康状态**
3. **意识状态**
4. **所属派系**
5. **年龄 / ID**
6. **证词摘要**
7. **完整证词**（hover 或展开时显示）

这样可以避免目前“字段平均分配注意力”的问题。

### 5.2 批量囚犯卡的新优先级

批量模式中，用户最关心的通常是：

1. 总共有多少囚犯
2. 哪些囚犯更值钱
3. 谁更值得优先处理
4. 是否存在重伤、濒死或异常状态

因此批量卡应优先突出：

- 数量
- 预估总价
- 最高价值囚犯
- 异常状态徽标

***

## 6. 布局实现计划

### 6.1 单个囚犯卡布局调整

当前单囚犯气泡已经是左图右文，但文本仍然是按字段逐条展开。文档中给出的核心常量表明，头像尺寸和文本间距是当前布局的关键约束。fileciteturn1file0

建议改造为：

- 头像区固定宽度，但高度允许在小范围内弹性变化。
- 文本区拆成“标题行 + 状态行 + 证词摘要行”。
- 年龄、ID 这类低优先级信息统一压缩到辅助行。
- 证词默认只显示一到两行，并加省略号。

### 6.2 推荐的 Rect 划分

```
bubbleRect
├── headerRect
├── bodyRect
│   ├── portraitRect
│   └── infoRect
│       ├── titleRowRect
│       ├── statusRowRect
│       ├── metaRowRect
│       └── quoteRowRect
└── footerRect (可选)

```

### 6.3 批量囚犯卡布局调整

批量卡当前以模板字符串形式逐行输出，优点是实现简单，但视觉上容易显得“文档化”。fileciteturn1file0

建议改成：

- 顶部一行：派系 + 总人数 + 参考总价。
- 中部列表：每个囚犯一条紧凑记录。
- 底部：提示可展开更多信息或进一步选择。

### 6.4 推荐的批量布局结构

```
┌─────────────────────────────────────┐
│ [囚犯档案] 派系：XXX | 3名 | ~4200银 │
├─────────────────────────────────────┤
│ 1. XXX  健康 86%  意识：正常  价值高  │
│ 2. XXX  健康 52%  意识：昏迷  低价值  │
│ 3. XXX  健康 18%  意识：濒危  待处理  │
└─────────────────────────────────────┘

```

***

## 7. 字体与文本规则

### 7.1 文本优先级

建议将所有字段分成四级：

- **一级文本**：姓名、总价、健康百分比
- **二级文本**：意识、派系、状态标签
- **三级文本**：年龄、ID
- **四级文本**：完整证词、补充描述

### 7.2 截断规则

统一执行以下规则：

- 单行文本只允许显示一行。
- 超过宽度统一使用省略号。
- 价格、健康百分比优先保留完整。
- 姓名可截断，但尽量展示前缀。
- 证词默认只显示摘要。

### 7.3 建议新增工具函数

```
string TruncateToWidth(string text, float maxWidth, GameFont font);
string FormatHealthPercent(float healthPct);
string FormatConsciousness(string consciousness);
string FormatRansomValue(int value);
string BuildQuoteSummary(string quote, int maxChars);

```

***

## 8. 视觉强化方案

### 8.1 单个囚犯卡视觉强化

建议加入以下视觉层级：

- **头像框**：给缩略图增加轻微描边或内阴影。
- **状态徽标**：例如“可赎”“重伤”“高价值”“濒危”。
- **健康条 / 意识条**：用极简条形表达状态。
- **证词摘要高亮**：让最关键的一句更像情报摘要。

### 8.2 批量囚犯卡视觉强化

建议加入：

- 每行左侧序号徽标。
- 价值高的囚犯行做轻微高亮。
- 濒危或异常囚犯加警告色。
- 支持按价值排序后再展示。

### 8.3 风格基调

推荐风格是：

- 不是医院病历本。
- 不是纯黑名单。
- 也不是高科技数据库。

而是更像一种 **“战后赎金档案”**：克制、严肃、信息明确，但带有一点情报感。

***

## 9. 动态交互计划

### 9.1 Hover 展开

当鼠标悬停在囚犯卡上时，可以：

- 展示完整证词。
- 展示更完整的健康详情。
- 展示派系关系或赎金建议。
- 展示更详细的身份信息。

### 9.2 单击展开 / 折叠

对于批量卡，建议支持：

- 默认折叠成几行摘要。
- 点击展开完整列表。
- 再次点击收起。

### 9.3 微动画

即使在 IMGUI 里，也可以做非常轻量的反馈：

- 新消息淡入。
- 高价值条目微弱呼吸高亮。
- hover 时边框高亮。

***

## 10. 代码重构计划

文档中已经说明了单个囚犯气泡、批量囚犯气泡和渲染器入口分别位于不同模块，这非常适合做职责分离式重构。fileciteturn1file0

### 10.1 建议拆分的模块

#### A. 布局常量层

```
internal static class PrisonerInfoCardLayout
{
    public const float Padding = 8f;
    public const float ThumbMin = 128f;
    public const float ThumbMax = 172f;
    public const float ThumbGap = 6f;
    public const float HeaderHeight = 14f;
    public const float StatusRowHeight = 18f;
    public const float QuoteMaxLines = 2f;
}

```

#### B. 文本格式化层

```
internal static class PrisonerInfoTextUtil
{
    public static string FormatHealthPercent(float value);
    public static string FormatConsciousness(string value);
    public static string TruncateToWidth(...);
    public static string BuildQuoteSummary(string quote, int maxChars);
}

```

#### C. 数据视图层

```
internal sealed class PrisonerInfoCardViewModel
{
    public string Name;
    public string Age;
    public string Health;
    public string Consciousness;
    public string Faction;
    public string Id;
    public string QuoteSummary;
    public int EstimatedRansom;
    public string StatusLabel;
}

```

#### D. 绘制层

- `DrawPrisonerPortrait`
- `DrawPrisonerTitleRow`
- `DrawPrisonerStatusRow`
- `DrawPrisonerQuoteRow`
- `DrawRansomBatchHeader`
- `DrawRansomBatchLine`

### 10.2 测量与绘制一致性

所有高度计算必须与绘制共享同一套布局常量：

- 相同 padding
- 相同行高
- 相同头像尺寸逻辑
- 相同文本截断规则
- 相同状态徽标高度

避免“算出来能放下，但画出来溢出”的问题。

***

## 11. 核心方法改造方案

### 11.1 `DrawOutboundPrisonerInfoBubble()`

改造目标：

- 从字段逐行排列，升级为档案卡结构。
- 把姓名和状态放在第一视觉层。
- 把证词摘要压缩为次级信息。
- 保持头像左侧的视觉锚点。

### 11.2 `CalculateImageMessageHeight()`

改造目标：

- 不再以静态字段叠加为主，而以“头像 + 关键行数 + 摘要行数”动态估算。
- 证词行数要根据可用宽度和最大行数进行封顶。

### 11.3 `ResolveOutboundPrisonerThumbSize()`

改造目标：

- 保留中心裁切放大效果。
- 但缩略图大小要与文本密度联动。
- 当证词较长时，优先轻微缩小头像，让文本区更稳定。

### 11.4 `BuildRansomBatchInfoCardBody()`

改造目标：

- 从纯模板拼接升级为结构化条目拼接。
- 支持排序、折叠、按价值高亮。
- 为每一行增加统一状态标签。

***

## 12. 颜色系统计划

文档中已经给出了玩家气泡背景、标题文字、时间戳文字、文本颜色与阴影颜色等基础设定。fileciteturn1file0

### 12.1 新配色原则

- **主标题**：深色高对比。
- **状态标签**：以语义色区分。
  - 可赎 / 正常：偏绿
  - 重伤 / 昏迷：偏黄或橙
  - 濒危 / 低价值：偏红或灰红
- **辅助文本**：降低饱和度，避免与主信息竞争。
- **批量列表**：行高亮不要太强，保持 RimWorld 气质。

### 12.2 色彩分层

1. 背景层：柔和、稳定。
2. 头像层：自然居中。
3. 主信息层：高对比。
4. 状态层：语义化颜色。
5. 警告层：少量使用，避免过度刺激。

***

## 13. 本地化与语言键计划

文档中列出了现有批量卡片模板语言键，以及单个囚犯气泡中需要的字段顺序。fileciteturn1file0

### 13.1 建议新增语言键

#### 单个囚犯卡

- `RimChat_PrisonerInfoCard_Title`
- `RimChat_PrisonerInfoCard_StatusNormal`
- `RimChat_PrisonerInfoCard_StatusHighValue`
- `RimChat_PrisonerInfoCard_StatusCritical`
- `RimChat_PrisonerInfoCard_QuoteSummary`
- `RimChat_PrisonerInfoCard_EstimatedRansom`

#### 批量囚犯卡

- `RimChat_PrisonerBatchCardTitle`
- `RimChat_PrisonerBatchLine`
- `RimChat_PrisonerBatchTotalValue`
- `RimChat_PrisonerBatchHighValueHint`
- `RimChat_PrisonerBatchExpandHint`

### 13.2 本地化要求

- 允许中文和英文长度差异。
- 数值与单位分离。
- 状态词尽量短。
- 批量行模板要保证长昵称也能被截断。

***

## 14. 分阶段实施计划

### Phase 1：低风险收敛

目标：先把布局和文本稳定性做好。

改动内容：

- 统一截断逻辑
- 收紧头像与文本间距
- 减少冗余行
- 证词默认摘要化
- 批量卡增加总标题与总价信息

验收标准：

- 文本不再溢出。
- 气泡高度下降。
- 现有内容仍完整表达。

### Phase 2：视觉层级增强

目标：让囚犯卡更像“档案卡”。

改动内容：

- 添加状态徽标
- 添加健康 / 意识简条
- 强化标题与头像关系
- 批量列表行级高亮

验收标准：

- 用户能快速识别谁更重要。
- 视觉焦点更明确。

### Phase 3：交互增强

目标：提高信息上限，但不增加常驻高度。

改动内容：

- Hover 展开证词全文
- 点击折叠批量列表
- 显示补充信息与排序提示

验收标准：

- 默认态紧凑。
- 交互态信息充分。

### Phase 4：细节抛光

目标：修复边界问题并优化整体质感。

改动内容：

- 调整颜色对比
- 适配不同分辨率
- 检查字体回退
- 修正边框与缩略图裁切

验收标准：

- 各种 UI 缩放下都稳定可读。
- 没有明显抖动或错位。

***

## 15. 测试计划

### 15.1 静态测试

测试以下场景：

- 超长姓名
- 超长派系名
- 超长证词
- 极短文本
- 低分辨率 UI 缩放
- 高分辨率 UI 缩放

### 15.2 视觉测试

检查：

- 头像是否仍保持主体居中。
- 文本是否出现重叠或裁切。
- 状态徽标是否挤占主信息。
- 批量列表是否一眼可扫读。

### 15.3 体验测试

检查：

- 玩家能否快速理解囚犯价值。
- 是否能快速判断哪个囚犯更值得优先处理。
- 是否比当前版本更节省空间、更直观。

***

## 16. 验收标准

完成后，囚犯信息卡气泡应满足以下条件：

1. 结构更紧凑，气泡高度更低。
2. 文本无裁切、无溢出、无错位。
3. 主信息与次信息层级清晰。
4. 证词不再压制布局。
5. 批量囚犯卡具备明确排序和高亮逻辑。
6. 风格仍符合 RimWorld 的克制感，但更像高质量情报卡。
7. 代码结构支持后续继续调整与本地化扩展。

***

## 17. 风险与对策

### 风险 1：证词过长导致内容区被撑高

对策：证词默认摘要化，全文只在 hover 或展开时显示。

### 风险 2：头像过大压缩文本区

对策：头像尺寸和文本区宽度联动，根据可用宽度动态调整。

### 风险 3：批量卡从纯文本升级后实现复杂度上升

对策：先保留文本模板逻辑，再逐步替换为结构化绘制。

### 风险 4：风格过度现代化

对策：保持低饱和配色和克制边框，不使用过亮的科幻元素。

***

## 18. 推荐最终落地方案

综合实现成本、体验提升与风险控制，最推荐的落地路线是：

### 第一优先级

- 统一文本截断
- 压缩头像和边距
- 证词摘要化
- 批量卡标题和总值提炼

### 第二优先级

- 状态徽标
- 健康 / 意识简条
- 价值高亮
- 批量排序

### 第三优先级

- Hover 展开全文
- 微动画
- 更细的视觉装饰

***

## 19. 最终建议

囚犯信息卡不应该只是“角色证明文本”，而应该是：

> **一个帮助玩家快速判断赎金价值、健康风险和处理优先级的档案型 UI。**

只要把“谁是谁”“值不值”“是否危险”这三件事表达清楚，这个组件就会比现在强一个层级。

***

## 20. 下一步可直接执行的工作项

1. 输出新版布局常量表。
2. 设计 `PrisonerInfoCardViewModel`。
3. 重写单个囚犯卡的高度计算。
4. 重写 `DrawOutboundPrisonerInfoBubble()` 的信息层级。
5. 将批量囚犯卡从纯文本模板过渡到结构化行绘制。
6. 补齐统一的文本截断与摘要生成工具。
7. 做极限宽度测试与本地化测试。


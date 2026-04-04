# 空投信息卡气泡样式文档

## 概述

空投信息卡气泡是 RimChat 模组中用于在外交对话界面展示物资空投交易请求的 UI 组件。样式采用横向交易流布局，左侧显示支付物资，右侧显示获得物资，中间显示利润百分比和箭头。

## 文件位置

- **样式代码**: `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`
- **对话框类**: `RimChat/UI/Dialog_ItemAirdropTradeCard.cs`
- **语言键**: `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`

## 核心常量定义

```csharp
// 布局常量 (Dialog_DiplomacyDialogue.ImageRendering.cs)
private const float AirdropCardThumbSize = 36f;           // 缩略图大小
private const float AirdropCardPadding = 8f;              // 气泡内边距
private const float AirdropCardHeaderHeight = 14f;        // 头部高度（发送者/时间）
private const float AirdropCardTitleBandHeight = 20f;     // 标题带高度
private const float AirdropCardFooterHeight = 16f;        // 底部高度（参考总价）
private const float AirdropCardRowGap = 4f;               // 行间距
private const float AirdropCardMetricGap = 2f;            // 指标单元格间距
private const float AirdropCardMetricHeight = 26f;        // 指标区域高度
private const float AirdropCardMinRowHeight = 84f;        // 卡片最小高度
private const float AirdropCardMiniIconSize = 32f;        // 紧凑卡片图标大小
private const float AirdropCardMiniCardHeight = 84f;      // 紧凑卡片高度
private const float AirdropCardFlowGap = 8f;              // 交易流间距
private const float AirdropCardBadgeWidth = 44f;          // 利润徽章宽度
private const float AirdropCardDefNameHeight = 12f;       // DefName 高度
```

## 颜色方案

### 玩家视觉 (playerVisual = true)

| 元素 | 颜色值 | 说明 |
|------|--------|------|
| 气泡背景 | `PlayerBubbleColor` | 玩家气泡颜色 |
| 发送者文字 | `new Color(0.12f, 0.16f, 0.10f, 0.95f)` | 深绿色调 |
| 次要文字 | `new Color(0.14f, 0.18f, 0.12f, 0.78f)` | 深绿色调半透明 |
| 内容面板底 | `new Color(1f, 1f, 1f, 0.06f)` | 半透明白色 |
| 主要文字 | `new Color(0.10f, 0.12f, 0.11f, 0.98f)` | 深绿色调 |
| 指标标签 | `new Color(0.20f, 0.22f, 0.21f, 0.84f)` | 灰绿色调 |
| 指标数值 | `new Color(0.09f, 0.11f, 0.10f, 0.98f)` | 深绿色调 |

### AI 视觉 (playerVisual = false)

| 元素 | 颜色值 | 说明 |
|------|--------|------|
| 气泡背景 | `AIBubbleColor` | AI 气泡颜色 |
| 发送者文字 | `new Color(0.16f, 0.19f, 0.23f, 0.95f)` | 蓝灰色调 |
| 次要文字 | `new Color(0.18f, 0.21f, 0.24f, 0.82f)` | 蓝灰色调半透明 |
| 内容面板底 | `new Color(1f, 1f, 1f, 0.06f)` | 半透明白色 |
| 主要文字 | `new Color(0.10f, 0.12f, 0.11f, 0.98f)` | 深色文字 |
| 指标标签 | `new Color(0.20f, 0.22f, 0.21f, 0.84f)` | 灰绿色调 |
| 指标数值 | `new Color(0.09f, 0.11f, 0.10f, 0.98f)` | 深色文字 |

### 通用颜色

| 元素 | 颜色值 | 说明 |
|------|--------|------|
| 分隔线 | `new Color(0f, 0f, 0f, 0.18f)` | 半透明黑色 |
| 卡片边框 | `new Color(0f, 0f, 0f, 0.20f)` | 半透明黑色 |
| 缩略图底 | `new Color(0.15f, 0.15f, 0.18f)` | 深灰蓝色 |
| 缩略图边框 | `new Color(0.35f, 0.35f, 0.4f, 0.9f)` | 灰色边框 |

### 利润徽章颜色

| 条件 | 颜色值 | 说明 |
|------|--------|------|
| profitRatio >= 1.1f | `new Color(0.2f, 0.7f, 0.3f, 0.9f)` | 绿色（划算） |
| 0.9f <= profitRatio < 1.1f | `new Color(0.8f, 0.7f, 0.2f, 0.9f)` | 黄色（持平） |
| profitRatio < 0.9f | `new Color(0.8f, 0.3f, 0.2f, 0.9f)` | 红色（亏本） |

## 布局结构

```
┌─────────────────────────────────────────────────────┐
│ [发送者名称]                          [时间戳]      │  ← Header (14f)
├─────────────────────────────────────────────────────┤
│              【空投物资请求】                        │  ← TitleBand (20f)
├─────────────────────────────────────────────────────┤
│ ┌─────────────┐  [+67%]  ┌─────────────┐           │
│ │ 支付物资    │   →      │ 获得物资    │           │
│ │ 原木        │          │ 白银        │           │
│ │ Wood        │          │ Silver      │           │
│ │ 数量 价格 总价│          │ 数量 价格 总价│           │
│ └─────────────┘          └─────────────┘           │
│   sideCardWidth            sideCardWidth            │
├─────────────────────────────────────────────────────┤
│              参考总价：120.0                        │  ← Footer (16f)
└─────────────────────────────────────────────────────┘
```

### 宽度分配

```
contentWidth = bubbleWidth - (AirdropCardPadding * 2)
sideCardWidth = (contentWidth - AirdropCardBadgeWidth - AirdropCardFlowGap * 2) / 2
arrowWidth = AirdropCardBadgeWidth + AirdropCardFlowGap
```

### 高度计算

```csharp
private float CalculateAirdropTradeCardBubbleHeight(DialogueMessageData msg, float width)
{
    float contentWidth = Mathf.Max(200f, width - AirdropCardPadding * 2f);
    float headerTotal = AirdropCardHeaderHeight + 4f;        // 18f
    float titleTotal = AirdropCardTitleBandHeight + 4f;      // 24f
    float flowRowHeight = AirdropCardMiniCardHeight + 4f;    // 88f
    float footerTotal = AirdropCardFooterHeight + 4f;        // 20f
    float totalHeight = headerTotal + titleTotal + flowRowHeight + footerTotal;
    return Mathf.Max(170f, totalHeight);                     // 最小 170f
}
```

**总高度组成**: `18f + 24f + 88f + 20f = 150f`（最小 170f）

## 完整代码实现

### 1. DrawAirdropTradeCardBubble - 主绘制方法

```csharp
private void DrawAirdropTradeCardBubble(DialogueMessageData msg, Rect rect)
{
    bool playerVisual = IsPlayerVisualMessage(msg);
    Color bubbleColor = playerVisual ? PlayerBubbleColor : AIBubbleColor;
    Color senderColor = playerVisual 
        ? new Color(0.12f, 0.16f, 0.10f, 0.95f) 
        : new Color(0.16f, 0.19f, 0.23f, 0.95f);
    Color secondaryTextColor = playerVisual 
        ? new Color(0.14f, 0.18f, 0.12f, 0.78f) 
        : new Color(0.18f, 0.21f, 0.24f, 0.82f);
    Color dividerColor = new Color(0f, 0f, 0f, 0.18f);
    Color contentPanelColor = new Color(1f, 1f, 1f, 0.06f);
    Color contentPrimaryTextColor = new Color(0.10f, 0.12f, 0.11f, 0.98f);
    Color contentSecondaryTextColor = new Color(0.18f, 0.20f, 0.19f, 0.88f);
    Color metricLabelColor = new Color(0.20f, 0.22f, 0.21f, 0.84f);
    Color metricValueColor = new Color(0.09f, 0.11f, 0.10f, 0.98f);

    // 绘制气泡背景（带阴影）
    Rect shadowRect = new Rect(rect.x + 1f, rect.y + 2f, rect.width, rect.height);
    DrawRoundedRect(shadowRect, new Color(0f, 0f, 0f, 0.12f), BUBBLE_CORNER_RADIUS);
    DrawRoundedRect(rect, bubbleColor, BUBBLE_CORNER_RADIUS);

    // 初始化布局变量
    float contentX = rect.x + AirdropCardPadding;
    float contentY = rect.y + 5f;
    float contentWidth = rect.width - AirdropCardPadding * 2f;

    // 绘制头部：发送者名称 + 时间戳
    Text.Font = GameFont.Tiny;
    GUI.color = senderColor;
    DrawSingleLineClippedLabel(new Rect(contentX, contentY, contentWidth * 0.7f, AirdropCardHeaderHeight), GetDisplaySenderName(msg));

    string timeStr = GetTimestampString(msg);
    float timeWidth = Text.CalcSize(timeStr).x + 5f;
    Rect timeRect = new Rect(rect.xMax - timeWidth - AirdropCardPadding, contentY, timeWidth, AirdropCardHeaderHeight);
    GUI.color = secondaryTextColor;
    DrawSingleLineClippedLabel(timeRect, timeStr);

    // 绘制分隔线
    contentY += AirdropCardHeaderHeight + 3f;
    Widgets.DrawBoxSolid(new Rect(contentX, contentY, contentWidth, 1f), dividerColor);
    contentY += 3f;

    // 绘制标题
    Text.Font = GameFont.Small;
    GUI.color = new Color(0.09f, 0.11f, 0.10f, 1f);
    DrawSingleLineClippedLabel(new Rect(contentX, contentY, contentWidth, AirdropCardTitleBandHeight), "RimChat_AirdropTradeCard_BubbleTitle".Translate());

    // 绘制分隔线
    contentY += AirdropCardTitleBandHeight + 3f;
    Widgets.DrawBoxSolid(new Rect(contentX, contentY, contentWidth, 1f), dividerColor);
    contentY += 3f;

    // 计算交易流布局
    float flowRowWidth = contentWidth;
    float sideCardWidth = (flowRowWidth - AirdropCardBadgeWidth - AirdropCardFlowGap * 2f) / 2f;

    Rect needCardRect = new Rect(contentX, contentY, sideCardWidth, AirdropCardMiniCardHeight);
    Rect arrowRect = new Rect(contentX + sideCardWidth + AirdropCardFlowGap, contentY, AirdropCardBadgeWidth, AirdropCardMiniCardHeight);
    Rect offerCardRect = new Rect(arrowRect.xMax + AirdropCardFlowGap, contentY, sideCardWidth, AirdropCardMiniCardHeight);

    // 计算利润比率
    float profitDelta = msg.airdropOfferTotalPrice - msg.airdropNeedReferenceTotalPrice;
    float profitRatio = msg.airdropNeedReferenceTotalPrice > 0f 
        ? msg.airdropOfferTotalPrice / msg.airdropNeedReferenceTotalPrice 
        : 1f;

    // 绘制支付物资卡（左侧）
    DrawAirdropCompactCard(
        needCardRect,
        msg.airdropNeedLabel,
        msg.airdropNeedDefName,
        msg.airdropRequestedCount,
        msg.airdropNeedUnitPrice,
        msg.airdropNeedReferenceTotalPrice,
        "RimChat_AirdropTradeCard_PayLabel".Translate().ToString(),
        contentPanelColor,
        dividerColor,
        contentPrimaryTextColor,
        contentSecondaryTextColor,
        metricLabelColor,
        metricValueColor);

    // 绘制利润徽章和箭头（中间）
    DrawAirdropFlowBadge(arrowRect, profitRatio, playerVisual);

    // 绘制获得物资卡（右侧）
    DrawAirdropCompactCard(
        offerCardRect,
        msg.airdropOfferLabel,
        msg.airdropOfferDefName,
        msg.airdropOfferCount,
        msg.airdropOfferUnitPrice,
        msg.airdropOfferTotalPrice,
        "RimChat_AirdropTradeCard_GainLabel".Translate().ToString(),
        contentPanelColor,
        dividerColor,
        contentPrimaryTextColor,
        contentSecondaryTextColor,
        metricLabelColor,
        metricValueColor);

    // 绘制底部分隔线
    contentY += AirdropCardMiniCardHeight + 4f;
    Widgets.DrawBoxSolid(new Rect(contentX, contentY, contentWidth, 1f), dividerColor);
    contentY += 3f;

    // 绘制参考总价
    Rect footerRect = new Rect(contentX, contentY, contentWidth, AirdropCardFooterHeight);
    Text.Font = GameFont.Tiny;
    GUI.color = senderColor;
    string footerText = "RimChat_AirdropTradeCard_ReferencePriceBubble".Translate(
        msg.airdropNeedReferenceTotalPrice.ToString("F1", CultureInfo.InvariantCulture)).ToString();
    DrawSingleLineClippedLabel(
        new Rect(footerRect.x, footerRect.y + 1f, footerRect.width, footerRect.height - 2f),
        footerText);

    // 恢复 GUI 状态
    GUI.color = Color.white;
    Text.Font = GameFont.Small;
}
```

### 2. DrawAirdropCompactCard - 紧凑物资卡绘制

```csharp
private void DrawAirdropCompactCard(
    Rect rect,
    string label,
    string defName,
    int count,
    float unitPrice,
    float totalPrice,
    string categoryLabel,
    Color contentPanelColor,
    Color dividerColor,
    Color primaryTextColor,
    Color secondaryTextColor,
    Color metricLabelColor,
    Color metricValueColor)
{
    // 保存 GUI 状态
    Color savedColor = GUI.color;
    GameFont savedFont = Text.Font;
    TextAnchor savedAnchor = Text.Anchor;
    
    // 绘制卡片背景和边框
    DrawRoundedRect(rect, contentPanelColor, 6f);
    GUI.color = new Color(0f, 0f, 0f, 0.20f);
    Widgets.DrawBox(rect);
    GUI.color = savedColor;

    // 绘制缩略图
    float iconPanelSize = AirdropCardMiniIconSize + 4f;
    Rect iconPanelRect = new Rect(rect.x + 4f, rect.y + 4f, iconPanelSize, iconPanelSize);
    Rect iconRect = new Rect(iconPanelRect.x + 2f, iconPanelRect.y + 2f, AirdropCardMiniIconSize, AirdropCardMiniIconSize);
    DrawAirdropThingThumbnail(iconRect, defName);

    // 计算文字区域
    float textX = iconPanelRect.xMax + 5f;
    float textWidth = rect.width - (textX - rect.x) - 4f;
    textWidth = Mathf.Max(70f, textWidth);

    float yPos = rect.y + 4f;

    // 绘制分类标签（支付/获得）
    Text.Font = GameFont.Tiny;
    GUI.color = secondaryTextColor;
    DrawSingleLineClippedLabel(new Rect(textX, yPos, textWidth, 10f), categoryLabel);
    yPos += 11f;

    // 绘制分隔线
    GUI.color = dividerColor;
    Widgets.DrawBoxSolid(new Rect(textX, yPos, textWidth, 1f), dividerColor);
    GUI.color = savedColor;
    yPos += 3f;

    // 绘制物资名称
    Text.Font = GameFont.Small;
    GUI.color = primaryTextColor;
    string displayLabel = string.IsNullOrWhiteSpace(label) ? (defName ?? "?") : label;
    float labelHeight = MeasureWrappedTextHeight(displayLabel, textWidth, GameFont.Small, 28f);
    Widgets.Label(new Rect(textX, yPos, textWidth, labelHeight), displayLabel);
    yPos += labelHeight + 2f;

    // 绘制 DefName（如果有）
    if (!string.IsNullOrWhiteSpace(defName))
    {
        Text.Font = GameFont.Tiny;
        GUI.color = secondaryTextColor;
        DrawSingleLineClippedLabel(new Rect(textX, yPos, textWidth, AirdropCardDefNameHeight), defName);
        yPos += AirdropCardDefNameHeight + 3f;
    }

    // 绘制指标分隔线
    float metricsTop = rect.y + rect.height - AirdropCardMetricHeight - 4f;
    GUI.color = dividerColor;
    Widgets.DrawBoxSolid(new Rect(textX, metricsTop - 2f, textWidth, 1f), dividerColor);
    GUI.color = savedColor;

    // 绘制三个指标单元格
    float metricWidth = (textWidth - AirdropCardMetricGap * 2f) / 3f;
    DrawAirdropMetricCell(
        new Rect(textX, metricsTop, metricWidth, AirdropCardMetricHeight),
        "RimChat_AirdropTradeCard_CountLabel".Translate().ToString(),
        count.ToString(CultureInfo.InvariantCulture),
        dividerColor,
        metricLabelColor,
        metricValueColor);
    DrawAirdropMetricCell(
        new Rect(textX + metricWidth + AirdropCardMetricGap, metricsTop, metricWidth, AirdropCardMetricHeight),
        "RimChat_Price".Translate().ToString(),
        unitPrice.ToString("F1", CultureInfo.InvariantCulture),
        dividerColor,
        metricLabelColor,
        metricValueColor);
    DrawAirdropMetricCell(
        new Rect(textX + (metricWidth + AirdropCardMetricGap) * 2f, metricsTop, metricWidth, AirdropCardMetricHeight),
        "RimChat_AirdropTradeCard_TotalPriceLabel".Translate().ToString(),
        totalPrice.ToString("F1", CultureInfo.InvariantCulture),
        dividerColor,
        metricLabelColor,
        metricValueColor);

    // 恢复 GUI 状态
    Text.Anchor = savedAnchor;
    Text.Font = savedFont;
    GUI.color = savedColor;
}
```

### 3. DrawAirdropFlowBadge - 利润徽章绘制

```csharp
private void DrawAirdropFlowBadge(Rect arrowRect, float profitRatio, bool playerVisual)
{
    Color savedColor = GUI.color;
    
    float centerX = arrowRect.x + arrowRect.width * 0.5f;
    float centerY = arrowRect.y + arrowRect.height * 0.5f;

    // 根据利润比率确定颜色和文字
    Color profitColor;
    string badgeText;
    if (profitRatio >= 1.1f)
    {
        profitColor = new Color(0.2f, 0.7f, 0.3f, 0.9f);  // 绿色
        badgeText = $"+{(profitRatio - 1f) * 100:F0}%";
    }
    else if (profitRatio >= 0.9f)
    {
        profitColor = new Color(0.8f, 0.7f, 0.2f, 0.9f);  // 黄色
        badgeText = "±0%";
    }
    else
    {
        profitColor = new Color(0.8f, 0.3f, 0.2f, 0.9f);  // 红色
        badgeText = $"{(profitRatio - 1f) * 100:F0}%";
    }

    // 绘制徽章背景
    Rect badgeRect = new Rect(
        centerX - AirdropCardBadgeWidth * 0.5f,
        centerY - 10f,
        AirdropCardBadgeWidth,
        20f);

    GUI.color = profitColor;
    DrawRoundedRect(badgeRect, profitColor, 4f);
    GUI.color = Color.white;
    
    // 绘制徽章文字
    Text.Font = GameFont.Tiny;
    Text.Anchor = TextAnchor.MiddleCenter;
    Widgets.Label(badgeRect, badgeText);
    Text.Anchor = TextAnchor.UpperLeft;
    GUI.color = savedColor;

    // 绘制箭头
    Text.Font = GameFont.Small;
    GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
    string arrowStr = "RimChat_AirdropTradeCard_ArrowRight".Translate();
    float arrowWidth = Text.CalcSize(arrowStr).x;
    Widgets.Label(new Rect(centerX - arrowWidth * 0.5f, centerY + 12f, arrowWidth, 16f), arrowStr);
    GUI.color = savedColor;
    Text.Font = GameFont.Small;
}
```

### 4. DrawAirdropMetricCell - 指标单元格绘制

```csharp
private void DrawAirdropMetricCell(Rect rect, string label, string value, Color dividerColor, Color labelColor, Color valueColor)
{
    Text.Font = GameFont.Tiny;
    
    // 绘制标签
    GUI.color = labelColor;
    Rect labelRect = new Rect(rect.x, rect.y + 2f, rect.width, 12f);
    Widgets.Label(labelRect, label);
    
    // 绘制数值
    GUI.color = valueColor;
    Rect valueRect = new Rect(rect.x, rect.y + 14f, rect.width, 12f);
    Widgets.Label(valueRect, value);
    
    GUI.color = Color.white;
}
```

### 5. DrawAirdropThingThumbnail - 缩略图绘制

```csharp
private void DrawAirdropThingThumbnail(Rect iconRect, string defName)
{
    Color savedColor = GUI.color;
    
    if (string.IsNullOrWhiteSpace(defName))
    {
        Widgets.DrawBoxSolid(iconRect, new Color(0.15f, 0.15f, 0.18f));
        GUI.color = new Color(0.5f, 0.55f, 0.6f);
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(iconRect, "?");
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = savedColor;
        return;
    }

    ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
    if (thingDef?.uiIcon != null)
    {
        GUI.color = thingDef.uiIconColor;
        GUI.DrawTexture(iconRect.ContractedBy(2f), thingDef.uiIcon, ScaleMode.ScaleToFit, true);
    }
    else
    {
        Widgets.DrawBoxSolid(iconRect, new Color(0.15f, 0.15f, 0.18f));
    }

    GUI.color = new Color(0.35f, 0.35f, 0.4f, 0.9f);
    Widgets.DrawBox(iconRect);
    GUI.color = savedColor;
}
```

### 6. MeasureWrappedTextHeight - 文本高度测量工具

```csharp
private float MeasureWrappedTextHeight(string text, float width, GameFont font, float maxHeight)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        return 0f;
    }

    GameFont previousFont = Text.Font;
    Text.Font = font;
    float height = Text.CalcHeight(text, Mathf.Max(1f, width));
    Text.Font = previousFont;
    return Mathf.Min(maxHeight, Mathf.Max(14f, height));
}
```

## 语言键

### 中文语言键

```xml
<RimChat_AirdropTradeCard_BubbleTitle>【空投物资请求】</RimChat_AirdropTradeCard_BubbleTitle>
<RimChat_AirdropTradeCard_PayLabel>支付</RimChat_AirdropTradeCard_PayLabel>
<RimChat_AirdropTradeCard_GainLabel>获得</RimChat_AirdropTradeCard_GainLabel>
<RimChat_AirdropTradeCard_CountLabel>数量</RimChat_AirdropTradeCard_CountLabel>
<RimChat_AirdropTradeCard_TotalPriceLabel>总价</RimChat_AirdropTradeCard_TotalPriceLabel>
<RimChat_AirdropTradeCard_ArrowRight>→</RimChat_AirdropTradeCard_ArrowRight>
<RimChat_AirdropTradeCard_ReferencePriceBubble>参考总价：{0}</RimChat_AirdropTradeCard_ReferencePriceBubble>
<RimChat_Price>价格</RimChat_Price>
```

### 英文语言键

```xml
<RimChat_AirdropTradeCard_BubbleTitle>[Airdrop Request]</RimChat_AirdropTradeCard_BubbleTitle>
<RimChat_AirdropTradeCard_PayLabel>Pay</RimChat_AirdropTradeCard_PayLabel>
<RimChat_AirdropTradeCard_GainLabel>Gain</RimChat_AirdropTradeCard_GainLabel>
<RimChat_AirdropTradeCard_CountLabel>Count</RimChat_AirdropTradeCard_CountLabel>
<RimChat_AirdropTradeCard_TotalPriceLabel>Total</RimChat_AirdropTradeCard_TotalPriceLabel>
<RimChat_AirdropTradeCard_ArrowRight>→</RimChat_AirdropTradeCard_ArrowRight>
<RimChat_AirdropTradeCard_ReferencePriceBubble>Reference price: {0}</RimChat_AirdropTradeCard_ReferencePriceBubble>
<RimChat_Price>Price</RimChat_Price>
```

## 设计特点

1. **横向交易流布局**：左侧支付 → 中间利润 → 右侧获得，一眼可读交易方向
2. **利润徽章高亮**：根据利润率使用绿/黄/红三色直观显示交易是否划算
3. **紧凑布局**：卡片高度 84f，相比旧版 96f 下降 12.5%
4. **文本防溢出**：使用 `MeasureWrappedTextHeight` 限制最大高度，防止长文本撑爆卡片
5. **GUI 状态管理**：所有绘制方法都保存和恢复 GUI 状态，防止颜色/字体泄漏
6. **指标清晰**：数量/价格/总价三列等宽分布，底部对齐

## 版本历史

| 版本 | 日期 | 变更内容 |
|------|------|----------|
| 1.6.x | 2026-04-04 | 重构为横向交易流布局，添加利润徽章，紧凑化设计 |
| 1.6.x | 2026-04-04 | 修复 GUI 状态泄漏导致的重影/水印问题 |
| 1.6.x | 2026-04-04 | 添加完整语言键支持，修复缺失翻译 |
| 1.6.x | 2026-04-04 | 优化文本显示，增加卡片高度和文字空间 |

# 延迟AI事件触发规格

## Why
当前AI动作在对话后立即执行，缺乏真实外交的时间延迟感。通过添加游戏内时间延迟，让玩家有更多策略空间，增强游戏沉浸感。

## What Changes
- 新增延迟事件管理器，存储和管理待触发的AI事件
- 修改 `request_caravan` 和 `request_aid` 两个动作，使其延迟触发
- 在设置中添加延迟时间配置选项（默认12小时）
- 添加延迟时间计算逻辑：基础时间 + 好感度影响(±50%) + 随机偏移(±5小时)
- 添加待处理事件查看界面（只读）
- 集成到游戏存档系统

## Impact
- Affected specs: 外交对话系统、AI动作执行器
- Affected code: AIActionExecutor.cs, GameComponent_DiplomacyManager.cs, RimDiplomacySettings.cs, Dialog_DiplomacyDialogue.cs

## ADDED Requirements

### Requirement: 延迟事件管理
系统 SHALL 提供延迟事件的存储、追踪和触发机制。

#### Scenario: AI返回商队/援助请求
- **WHEN** AI在对话中返回 `request_caravan` 或 `request_aid` 动作
- **THEN** 动作不立即执行，而是添加到延迟事件队列
- **AND** 玩家可以在待处理事件列表中查看

### Requirement: 动态延迟计算
系统 SHALL 根据好感度和随机因素计算延迟时间。

#### Scenario: 计算延迟时间
- **WHEN** 添加延迟事件时
- **THEN** 延迟时间 = 基础时间(12小时) × (1 ± 好感度影响系数) ± 随机偏移(±5小时)
- **AND** 好感度越高，延迟越短；好感度越低，延迟越长

### Requirement: 设置可配置延迟时间
系统 SHALL 在Mod设置中提供延迟时间配置选项。

#### Scenario: 调整默认延迟
- **WHEN** 玩家打开Mod设置
- **THEN** 可以调整基础延迟时间（可调整范围待定）

### Requirement: 待处理事件查看
系统 SHALL 提供待处理事件的只读查看界面。

#### Scenario: 查看待处理事件
- **WHEN** 玩家打开外交对话界面
- **THEN** 可以看到该派系所有待处理的延迟事件

## REMOVED Requirements
无

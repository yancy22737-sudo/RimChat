# RimDiplomacy 项目文档

## 项目概述

**RimDiplomacy** 是一个为 RimWorld 1.6 开发的 AI 驱动派系外交 Mod。它通过引入 AI 控制派系、智能对话系统和动态世界新闻，为玩家提供更深度的外交体验。

---

## 已实现功能

### 1. 基础框架 ✅

#### 1.1 项目结构
- **目标框架**: .NET Framework 4.8
- **适配版本**: RimWorld 1.6
- **依赖**: Harmony 2.x

#### 1.2 自动构建系统
| 脚本 | 功能 |
|------|------|
| `build.ps1` | PowerShell 构建脚本（主脚本） |
| `build.bat` | Debug 构建批处理 |
| `build-release.bat` | Release 构建批处理 |
| `clean.ps1` | 清理构建产物 |

**构建流程**:
1. 清理之前的构建
2. 编译 C# 项目
3. 自动部署到 `E:\SteamLibrary\steamapps\common\RimWorld\Mods\RimDiplomacy`

### 2. Mod 元数据 ✅

#### 2.1 About.xml
- Mod 名称: RimDiplomacy - AI Driven Faction Diplomacy
- 包 ID: RimDiplomacy.AIDriven
- 支持版本: 1.6
- 依赖: Harmony
- 加载顺序: 在 Harmony 和所有 DLC 之后

#### 2.2 版本定义
- 版本号: 1.0.0
- 版本格式: x.y.z（主/次/修订）

### 3. 核心系统 ✅

#### 3.1 Mod 入口 (RimDiplomacyMod.cs)
- **职责**: Mod 初始化、Harmony Patch 加载、设置界面
- **依赖**: UnityEngine, Verse, HarmonyLib
- **接口**:
  - `Settings`: 全局设置访问
  - `Instance`: 单例实例

#### 3.2 设置系统 (RimDiplomacySettings.cs)
**API 配置**:
- API Key
- API Endpoint (默认: OpenAI)
- 模型名称 (默认: gpt-4)
- Temperature (0-1)
- Max Tokens (500-4000)
- 本地模型支持 (Ollama)

**AI 控制配置**:
- 最大 AI 控制派系数 (1-10)
- 启用/禁用 AI 补充袭击
- 启用/禁用 AI 补充商队
- 启用/禁用 AI 补充援军
- 启用/禁用 AI 对话任务

**阈值配置**:
- 敌对好感度阈值 (-100 到 0)
- 友好好感度阈值 (0 到 100)
- 玩家挑衅冷却时间 (小时)
- 威胁冷却天数

**新闻系统配置**:
- 启用/禁用新闻系统
- 新闻间隔天数 (最小/最大)
- 启用/禁用玩家影响新闻
- 启用/禁用 AI 推演新闻

#### 3.3 外交管理器 (GameComponent_DiplomacyManager.cs)
**职责**: 管理 AI 控制派系、触发外交事件

**核心功能**:
- **AI 派系初始化**: 游戏开始时随机选择派系由 AI 控制
- **好感度驱动事件**: 当好感度低于阈值时自动触发袭击
- **玩家挑衅响应**: 记录玩家挑衅行为并触发相应事件
- **冷却期管理**: 防止事件过于频繁触发

**接口**:
- `IsAIControlled(Faction)`: 检查派系是否由 AI 控制
- `RegisterPlayerProvoke(Faction)`: 注册玩家挑衅行为

**事件触发逻辑**:
```
好感度 <= 敌对阈值
    ↓
检查冷却期 (ThreatCooldownDays)
    ↓
30% 概率触发袭击
    ↓
重置冷却期计时器
```

### 4. UI 系统 ✅

#### 4.1 主界面 (MainTabWindow_RimDiplomacy.cs)
**入口**: 游戏底部 RimDiplomacy 按钮

**界面布局**:
- **左侧**: AI 控制派系列表
  - 派系图标
  - 派系名称
  - 好感度显示 (颜色编码)
- **右侧**: 派系详情
  - 关系状态 (Ally/Friend/Neutral/Hostile/Enemy)
  - 领袖信息
  - 科技等级
  - 据点数量

**操作按钮**:
- Dialogue: 打开外交对话窗口
- Request Trade: 请求贸易 (好感度 >= 0)
- Request Aid: 请求援军 (好感度 >= 80)

#### 4.2 对话窗口 (Dialog_DiplomacyDialogue.cs)
**界面**:
- 聊天式对话界面
- 消息气泡 (玩家蓝色/AI 灰色)
- 滚动历史记录
- 输入框和发送按钮
- Enter 键发送

**模拟 AI 响应** (当前版本):
- 关键词识别 (trade, help, war, peace)
- 基于好感度的条件响应
- 欢迎消息

**待实现**:
- 真实 AI API 调用
- 异步响应处理
- 对话上下文记忆

### 5. 数据定义 ✅

#### 5.1 MainButtonDefs.xml
- 定义 RimDiplomacy 主按钮
- 图标、描述、排序
- 关联 MainTabWindow_RimDiplomacy

#### 5.2 WorldObjectDefs.xml
- 定义 AI 派系前哨站
- 用于未来扩展

### 6. 本地化 ✅

#### 6.1 支持语言
- **简体中文** (ChineseSimplified)
- **英语** (English)

#### 6.2 已本地化内容
- 设置界面标签
- 通知消息
- 新闻系统
- 对话界面

---

## 待实现功能

### 1. AI API 客户端
- [ ] HTTP 异步请求封装
- [ ] OpenAI API 集成
- [ ] Ollama 本地模型支持
- [ ] 请求队列管理
- [ ] 响应缓存

### 2. 智能对话系统
- [ ] 真实 AI 响应
- [ ] 对话上下文管理
- [ ] 意图识别
- [ ] 情感分析
- [ ] 长期记忆

### 3. 新闻系统
- [ ] 新闻事件生成器
- [ ] AI 推演算法
- [ ] 新闻播报窗口
- [ ] 世界影响模拟
- [ ] 派系互动模拟

### 4. 事件系统
- [ ] AI 补充商队
- [ ] AI 补充援军
- [ ] AI 生成任务
- [ ] 与 Storyteller 协调

### 5. Harmony Patches
- [ ] 劫持派系对话
- [ ] 拦截原版事件
- [ ] 共享冷却期

---

## 技术架构

### 项目结构
```
RimDiplomacy/
├── About/                    # Mod 元数据
├── 1.6/                      # 版本内容
│   ├── Assemblies/           # 编译后的 DLL
│   ├── Defs/                 # XML 定义
│   └── Languages/            # 本地化
├── RimDiplomacy/             # C# 源代码
│   ├── Core/                 # 核心逻辑
│   └── UI/                   # UI 界面
├── doc/                      # 文档
├── build.ps1                 # 构建脚本
└── README.md                 # 项目说明
```

### 核心类关系
```
RimDiplomacyMod (Mod入口)
    ├── RimDiplomacySettings (设置)
    └── GameComponent_DiplomacyManager (外交管理)
            ├── MainTabWindow_RimDiplomacy (主界面)
            └── Dialog_DiplomacyDialogue (对话窗口)
```

---

## 开发规范

### 代码规范
- 单文件 < 800 行
- 单函数 < 30 行
- 嵌套 < 3 层
- 分支 < 3 个

### 文件规范
- 每个文件头部声明依赖和职责
- 使用中文注释
- 遵循 RimWorld 命名规范

---

## 版本历史

### v1.0.0 (2026-02-27)
- ✅ 初始版本
- ✅ 基础框架搭建
- ✅ AI 派系控制
- ✅ 好感度驱动袭击
- ✅ 外交对话界面
- ✅ 自动构建系统
- ✅ 中英双语支持

---

## 注意事项

1. **存档安全**: 当前版本已确保存档兼容性
2. **Mod 兼容性**: 需要 Harmony 作为前置
3. **性能**: 当前版本无性能问题
4. **测试**: 已在 RimWorld 1.6 测试通过

---

## 下一步计划

1. 实现 AI API 客户端
2. 集成真实 AI 对话
3. 开发新闻系统
4. 添加更多外交事件
5. 优化 UI 体验

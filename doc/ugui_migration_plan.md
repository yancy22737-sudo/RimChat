# RimChat UGUI 迁移方案

## 1. 背景与动机

### 1.1 当前痛点

RimChat 的 UI 完全基于 RimWorld 的 IMGUI（Immediate Mode GUI）体系。IMGUI 的根本特性是**每帧必须重新执行所有 DrawCall**，否则画面闪烁。这导致：

- **GPU 利用率低**：每个 IMGUI 控件是独立的 DrawCall，GPU 无法批处理（Batching），大量小 DrawCall 挤压 CPU→GPU 指令提交带宽
- **CPU 开销高**：布局计算（`CalcHeight`、`GetCursorPixelPosition`）每帧执行，即使内容未变
- **复杂界面卡顿**：提示词工作台（3 栏布局 + 结构化预览 + 芯片编辑器）在数据量大时帧率明显下降

### 1.2 已有优化（当前状态）

| 优化措施 | 效果 | 局限 |
|----------|------|------|
| `CachedRenderTexture` 签名缓存 | 内容未变时跳过布局计算 | DrawCall 数量不变，仅减少 CPU 侧计算 |
| 布局高度缓存（`_cachedHeaderHeights` 等） | 避免 `CalcHeight` 重复调用 | 缓存失效策略需逐个维护 |
| 位掩码脏标记系统 | 跨模块缓存失效信号 | 不影响 DrawCall 数量 |
| 帧预算 + 增量构建 | 预览构建不超过 4ms/帧 | 仅优化构建阶段，不优化渲染阶段 |
| 静态 `Color` 常量 | 避免每帧 GC 分配 | 微优化 |

**结论**：当前优化全部是 CPU 侧的"减法"，无法从根本上解决 GPU 利用率低的问题。IMGUI 框架内不存在将 DrawCall 从 N 降到 1 的路径。

---

## 2. 可行性分析

### 2.1 核心约束：RimWorld Window 系统是纯 IMGUI

```
RimWorld WindowStack
  └─ OnGUI() 回调
       └─ Window.DoWindowContents(Rect)
            └─ 仅可使用 GUI.* / Widgets.* / Text.* 等 IMGUI API
```

- `WindowStack` 完全基于 IMGUI 的 `GUI.Window` 机制
- UGUI `Canvas` 无法直接嵌入 IMGUI `Window`，原因：
  - **渲染管线冲突**：IMGUI 走 `OnGUI` 回调，UGUI 走 `Canvas.Render`；双管线同时运行会互相干扰
  - **输入事件冲突**：IMGUI 用 `Event.current`，UGUI 用 `EventSystem`；两套事件系统在同一区域会争抢
  - **裁剪冲突**：IMGUI 用 `GUI.BeginGroup` / `GUI.BeginScrollView` 裁剪，UGUI 用 `RectMask2D`；裁剪区域不互通

### 2.2 唯一可行路径：独立 UGUI Canvas + Camera → RenderTexture → GUI.DrawTexture

```
┌─────────────────────────────────────────────────────┐
│  IMGUI Window (Dialog_PromptWorkbenchLarge)         │
│                                                     │
│  ┌───────────────────────────────────────────────┐  │
│  │  GUI.DrawTexture(cachedRT)  ← 单次 DrawCall  │  │
│  └───────────────────────────────────────────────┘  │
│                                                     │
└─────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────┐
│  独立 UGUI 层（不挂载在 Window 下）                  │
│                                                     │
│  Camera (orthographic, target=Canvas RT)            │
│    └─ Canvas (RenderMode = World/Screen)            │
│         ├─ ScrollRect (消息流)                       │
│         ├─ LayoutGroup (模块列表)                    │
│         ├─ Text/TextMeshPro (预览内容)               │
│         └─ ...                                     │
│                                                     │
│  渲染目标: RenderTexture (cachedRT)                  │
└─────────────────────────────────────────────────────┘
```

**原理**：UGUI Canvas 由独立 Camera 渲染到 `RenderTexture`，IMGUI Window 中仅用 `GUI.DrawTexture` 显示该纹理。交互事件通过 `Event.current` 桥接到 UGUI `EventSystem`。

### 2.3 项目中已有的 RenderTexture 先例

| 位置 | 用途 | 方式 |
|------|------|------|
| `Dialog_RPGPawnDialogue.Portraits.cs` | 小人肖像高分辨率离屏渲染 | 3x ARGB32 + 8x MSAA → `RenderTexture` → `GUI.DrawTexture` |
| `Dialog_DiplomacySelfieConfig.cs` | 自拍肖像 PNG 导出 | `RenderTexture` → `ReadPixels` → PNG |
| `Dialog_DiplomacyDialogue.PrisonerRansomBatchRuntime.cs` | 囚犯肖像截图导出 | 同上 |

这些用例证明了 **RenderTexture → `GUI.DrawTexture` 在 RimWorld 窗口中是完全可行的**，只是当前仅用于 3D 渲染管线，未用于 UGUI Canvas 渲染。

---

## 3. 迁移架构设计

### 3.1 整体架构

```
RimChat.UI.UGui                          ← 新命名空间
├── UGuiCanvasHost                       ← Canvas 生命周期管理
│   ├── CreateCanvas()                   ← 创建独立 Canvas + Camera + EventSystem
│   ├── RenderToTexture()                ← Camera.Render() → RenderTexture
│   ├── Dispose()                        ← 释放 GPU 资源
│   └── BridgeInput(Event imeguiEvent)   ← IMGUI Event → UGUI EventSystem 桥接
│
├── UGuiPanelBase                        ← UGUI 面板基类
│   ├── BuildUI(Transform parent)        ← 构建控件树
│   ├── RefreshData()                    ← 数据→UI 绑定
│   └── Dispose()                        ← 销毁 GameObject
│
├── panels/
│   ├── PromptPreviewPanel               ← 结构化预览面板（UGUI 版本）
│   ├── ModuleListPanel                  ← 模块列表面板
│   ├── PresetPanel                      ← 预设面板
│   ├── SidePanelInfo                    ← 侧面板信息展示
│   └── ChatMessagePanel                 ← 外交对话消息流（最大收益目标）
│
└── bridge/
    ├── ImguiToUguiInputBridge           ← 输入事件双向桥接
    └── ScrollSyncBridge                 ← IMGUI ScrollView ↔ UGUI ScrollRect 同步
```

### 3.2 渲染流程

```
每帧 (OnGUI):
  1. 数据变更检测（签名比对）
  2. 若脏: UGuiPanelBase.RefreshData() → 更新 UGUI 控件数据
  3. 若脏: Camera.Render() → RenderTexture 更新
  4. GUI.DrawTexture(renderTexture) → 显示到 IMGUI Window
  5. 输入桥接: ImguiToUguiInputBridge.Forward(Event.current)
```

### 3.3 输入事件桥接

IMGUI 和 UGUI 使用完全不同的输入系统，需要桥接层：

| IMGUI 事件 | UGUI 等价 | 桥接方式 |
|------------|-----------|----------|
| `Event.current.type == MouseDown` | `PointerDown` | 构造 `PointerEventData`，调用 `ExecuteEvents.Execute` |
| `Event.current.type == MouseUp` | `PointerUp` | 同上 |
| `Event.current.type == MouseDrag` | `Drag` | 同上 |
| `Event.current.type == KeyDown` | `KeyDown` | 构造 `AxisEventData` 或直接转发 |
| `Event.current.type == ScrollWheel` | `Scroll` | 构造 `PointerEventData.scrollDelta` |

**关键挑战**：坐标转换。IMGUI 坐标原点在左上，Y 向下；UGUI 坐标原点在中心，Y 向上。需通过 `RectTransformUtility.ScreenPointToLocalPointInRectangle` 转换。

### 3.4 TextMeshPro 集成

RimWorld 1.6 基于 Unity 2022.x，自带 TextMeshPro 包。优势：
- **SDF 渲染**：文字只需 1 个 DrawCall/字体，不像 IMGUI 每个文字区域是独立 DrawCall
- **自动批处理**：相同材质的 TextMeshPro 对象自动合批
- **富文本**：内置颜色、大小、链接等标记

**风险**：需要确认 RimWorld 1.6 的 Unity 版本是否包含 TMP Runtime。若不包含，需内嵌 TMP 包（约 200KB DLL）。

---

## 4. 迁移范围与工作量评估

### 4.1 优先级排序（按收益/成本比）

| 优先级 | 目标面板 | 当前 DrawCall 估算 | 迁移后 DrawCall | 收益 | 工作量 | 收益/成本 |
|--------|----------|-------------------|----------------|------|--------|-----------|
| P0 | 结构化预览面板 | 50-200 | 1-3 | ★★★★★ | 2 周 | 最高 |
| P1 | 外交对话消息流 | 100-500 | 5-10 | ★★★★★ | 4 周 | 高 |
| P2 | 侧面板信息展示 | 20-50 | 1-2 | ★★★ | 1 周 | 中高 |
| P3 | 预设面板 | 10-30 | 1-2 | ★★ | 1 周 | 中 |
| P4 | 模块列表 | 15-40 | 2-3 | ★★ | 2 周 | 中低 |

**说明**：
- P0（结构化预览面板）：只读内容，无交互，最适合 UGUI 渲染缓存，实现最简单
- P1（外交对话消息流）：DrawCall 最大户，但交互复杂（点击、悬浮卡、图片内嵌），桥接工作量最大
- P4（模块列表）：有复选框、拖拽排序等交互，全量桥接成本高

### 4.2 不迁移的部分

| 组件 | 原因 |
|------|------|
| 输入框（`EnhancedTextArea`） | 文本输入与 IMGUI `TextEditor` 深度耦合，桥接键盘事件成本极高 |
| 芯片编辑器（`PromptWorkbenchChipEditor`） | 基于 `TextEditor` 的 Token 覆盖层，同上 |
| 工具提示（Tooltips） | RimWorld `TooltipHandler` 是 IMGUI 原生机制 |
| 小型 Dialog（<30 DrawCall） | 优化收益不显著 |

### 4.3 总工作量估算

| 阶段 | 内容 | 工期 |
|------|------|------|
| 基础设施 | `UGuiCanvasHost` + `UGuiPanelBase` + `ImguiToUguiInputBridge` | 2-3 周 |
| P0 迁移 | 结构化预览面板 → `PromptPreviewPanel` | 2 周 |
| P1 迁移 | 外交对话消息流 → `ChatMessagePanel` | 4 周 |
| P2 迁移 | 侧面板信息 → `SidePanelInfo` | 1 周 |
| P3-P4 迁移 | 预设面板 + 模块列表 | 2-3 周 |
| 测试与调试 | 兼容性测试、性能回归、边缘情况 | 2 周 |
| **合计** | | **13-17 周** |

---

## 5. 分阶段实施计划

### Phase 0: 基础设施搭建（2-3 周）

**目标**：建立 UGUI → RenderTexture → IMGUI 的渲染管线，验证可行性。

**交付物**：
- `UGuiCanvasHost`：独立 Canvas + Camera + RenderTexture 创建/销毁
- `UGuiPanelBase`：面板基类（构建/刷新/销毁）
- `ImguiToUguiInputBridge`：鼠标点击/滚轮桥接（初版，仅只读面板需要的最小集）
- PoC 验证：在 `Dialog_PromptWorkbenchLarge` 中显示一个纯 UGUI 渲染的静态文本区域

**验证标准**：
- [x] RenderTexture 在 IMGUI Window 中正确显示
- [x] 窗口缩放时 RenderTexture 分辨率自适应
- [x] 窗口关闭时 RenderTexture 正确释放
- [x] 帧率对比：静态内容场景下 DrawCall 下降 >80%

### Phase 1: P0 结构化预览面板迁移（2 周）

**目标**：将 `PromptWorkspaceStructuredPreviewRenderer` 的只读预览内容迁移到 UGUI。

**当前代码映射**：

| 当前类/方法 | UGUI 替代 |
|-------------|-----------|
| `DrawBlockHeader()` | `LayoutGroup` + `TMP_Text` |
| `DrawBlockBody()` | `TMP_Text` + 富文本标记 |
| `DrawSubsectionHeader()` | 嵌套 `LayoutGroup` + `TMP_Text` |
| `DrawProgressBar()` | `Image` + `Image`（填充） |
| `DrawSnapshotIndicator()` | `Image` 叠加 |
| `_cachedHeaderHeights` / `_cachedBodyHeights` | UGUI `LayoutGroup` 自动布局，无需手动缓存 |

**关键收益**：UGUI `LayoutGroup` 自动处理布局，消除所有手动高度缓存逻辑。

**验证标准**：
- [x] 预览内容渲染结果与 IMGUI 版本视觉一致
- [x] 内容未变时 DrawCall = 1（仅 `GUI.DrawTexture`）
- [x] 内容变更时刷新延迟 < 1 帧
- [x] `CachedRenderTexture` 签名缓存机制保留，避免无变更时重复 Camera.Render

### Phase 2: P1 外交对话消息流迁移（4 周）

**目标**：将 `Dialog_DiplomacyDialogue` 的消息流（最大 DrawCall 贡献者）迁移到 UGUI。

**挑战**：
- 消息气泡有圆角矩形样式 → 需自定义 `Image` + 9-Slice
- 内嵌图片消息 → UGUI `Image` 组件天然支持
- 悬浮信息卡（HoverCard）→ 需要独立的 RT 渲染层或 IMGUI Overlay
- 输入框保留 IMGUI → 同一窗口内 IMGUI + UGUI 混合渲染

**架构决策**：
- 消息流主体用 UGUI `ScrollRect` + `VerticalLayoutGroup`
- 输入区域保留 IMGUI，布局在窗口底部
- HoverCard 继续用 IMGUI（因为它覆盖在所有内容之上，独立于滚动）

### Phase 3: P2-P4 侧面板/预设/模块列表迁移（3-4 周）

逐步迁移，每完成一个面板立即上线，降低风险。

---

## 6. 风险与缓解

### 6.1 高风险

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| RimWorld 1.6 Unity 版本不含 TextMeshPro | 无法使用 SDF 文字渲染 | 降级使用 `UnityEngine.UI.Text`；或内嵌 TMP DLL |
| 输入桥接在 RimWorld 特殊输入处理下不兼容 | 点击/拖拽行为异常 | 逐事件类型测试；对 RimWorld 自定义输入（如右键上下文菜单）做特殊处理 |
| `WindowStack` 窗口层级遮挡时 UGUI Canvas 仍渲染 | 被遮挡窗口浪费 GPU | 监听 `WindowStack` 层级变化，被遮挡时暂停 Camera.Render |
| 多个 Dialog 同时打开时多个 Camera 竞争 | GPU 负载叠加 | 共享 Camera + 分区 Viewport，或按需启用/禁用 |

### 6.2 中风险

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| UGUI GameObject 未正确清理导致内存泄漏 | 长时间游玩后崩溃 | 严格 `Dispose` 模式 + `UnityEngine.Object.Destroy` 在 `PreClose` 中调用 |
| `RenderTexture` 分辨率过高导致 GPU 内存压力 | 大屏幕/4K 显存不足 | 动态分辨率：`min(screenWidth * 0.9, 1920)`，DPR 裁剪到 1x |
| Mod 冲突：其他 Mod 修改了 `EventSystem` | 输入事件被拦截 | 独立 `EventSystem`，不使用全局单例 |

### 6.3 低风险

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| 滚动惯性/弹性行为与 IMGUI 不一致 | 用户体验差异 | UGUI `ScrollRect` 可配置惯性/弹性参数，调到与 IMGUI `BeginScrollView` 一致 |
| 中文字体渲染差异 | 文字显示细微不同 | 统一使用 RimWorld 自带字体，通过 `Font.material` 注入 TMP |

---

## 7. 性能预期

### 7.1 DrawCall 对比

| 场景 | 当前 IMGUI | Phase 1 后 | Phase 2 后 |
|------|-----------|------------|------------|
| 提示词工作台（静态预览） | 80-200 | 3-5 | 3-5 |
| 提示词工作台（编辑中） | 80-200 | 80-200（输入区 IMGUI） | 80-200 |
| 外交对话（50 条消息） | 200-500 | 200-500 | 10-20 |
| 外交对话（200 条消息） | 500-1500 | 500-1500 | 15-30 |

### 7.2 GPU 利用率预期

- **当前**：GPU 等待 CPU 提交 DrawCall，大量时间空闲（< 20% 利用率）
- **Phase 1 后**：预览面板 GPU 利用率提升至 ~60%（UGUI Canvas 批处理 + 单次 `GUI.DrawTexture`）
- **Phase 2 后**：消息流 GPU 利用率提升至 ~70-80%

### 7.3 CPU 开销预期

- **当前**：布局计算每帧执行（`CalcHeight` 等）
- **迁移后**：UGUI `LayoutGroup` 仅在数据变更时重算（Retained Mode），CPU 开销降低 40-60%

---

## 8. 回退策略

每个 Phase 独立交付，任何 Phase 出现不可解问题可单独回退到 IMGUI 渲染：

```
Feature Flag: RimChatSettings.useUguiPreviewPanel
  ├─ true  → UGUI Canvas → RenderTexture → GUI.DrawTexture
  └─ false → 现有 IMGUI 直接渲染（当前行为）
```

运行时切换：在设置界面提供开关，用户遇到兼容性问题时可一键回退。

---

## 9. 当前状态总结

| 项目 | 状态 |
|------|------|
| IMGUI → UGUI 直接替换 | ❌ 不可行（RimWorld Window 系统限制） |
| RenderTexture 中转方案 | ✅ 技术可行（项目中已有 3D 渲染先例） |
| `CachedRenderTexture` 签名缓存 | ✅ 已实现（CPU 侧优化） |
| UGUI Canvas → RenderTexture 方案 | ✅ 已实现（Phase 0，`UGuiCanvasHost`） |
| 输入事件桥接 | ✅ 已实现（Phase 0，`ImguiToUguiInputBridge`） |
| 特性开关 | ✅ 已实现（`UGuiFeatureFlags`，持久化到存档配置） |
| 结构化预览面板迁移 | ✅ 已实现（Phase 1，`PromptPreviewPanel` + TMP） |
| 侧面板迁移 | ✅ 已实现（Phase 2，`WorkspaceSidePanel` — tab 按钮栏） |
| Header 面板迁移 | ✅ 已实现（Phase 2，`WorkspaceHeaderPanel`） |
| Preset 面板迁移 | ✅ 已实现（Phase 2，`WorkspacePresetPanel`） |
| Editor Chrome 迁移 | ✅ 已实现（Phase 2，`WorkspaceEditorPanel`） |
| 消息流迁移 | 🔲 待实施（Phase 3） |
| .csproj UGUI/TMP DLL 引用 | ✅ 已添加（`UnityEngine.UI.dll` + `Unity.TextMeshPro.dll`） |

**底线**：此方案不修改游戏本体，完全在 Mod DLL 内实现，兼容旧存档，通过 Feature Flag 可随时回退。

---

## 10. 实施记录

### v0.9.998 — Phase 0 + Phase 1 完成

**新增文件**：
- `RimChat/UI/UGui/UGuiCanvasHost.cs` — Canvas + Camera + RenderTexture 生命周期管理
- `RimChat/UI/UGui/UGuiPanelBase.cs` — 面板基类（签名脏标记 + 数据绑定）
- `RimChat/UI/UGui/UGuiFeatureFlags.cs` — 特性开关（4 个 Flag）
- `RimChat/UI/UGui/bridge/ImguiToUguiInputBridge.cs` — IMGUI→UGUI 输入桥接
- `RimChat/UI/UGui/panels/PromptPreviewPanel.cs` — 结构化预览 UGUI 面板

**修改文件**：
- `RimChat/RimChat.csproj` — 添加 `UnityEngine.UI`、`UnityEngine.UIModule`、`Unity.TextMeshPro` DLL 引用
- `RimChat/Config/RimChatSettings.cs` — 添加 `_useUguiRendering` 等 4 个 Flag 字段 + `ExposeData()` 序列化 + `UGuiFeatureFlags.SyncFromSettings()`
- `RimChat/Config/RimChatSettings_PromptSectionWorkspace.cs` — `DrawPromptWorkspaceStructuredPreview` 添加 UGUI 路径分支 + UGUI Host 字段 + `DisposePromptWorkspaceRenderTextures` 扩展清理 + `InvalidatePromptWorkspacePreviewCache` 脏标记传播
- `RimChat/UI/Dialog_PromptWorkbenchLarge.cs` — 更新文档注释

**启用方式**：在存档配置文件中设置 `UseUguiRendering=true` + `UseUguiPreviewPanel=true`，或在代码中 `UGuiFeatureFlags.SyncFromSettings(true, true, false, false)`。默认关闭。

### v0.9.999 — Phase 2 工作台全面 UGUI 迁移

**新增文件**：
- `RimChat/UI/UGui/panels/WorkspaceHeaderPanel.cs` — 工作台 Header 面板（标题、根按钮、频道下拉、快捷操作、导入/导出）
- `RimChat/UI/UGui/panels/WorkspacePresetPanel.cs` — 预设面板（预设列表 + 模块列表，含 checkbox + 滚动）
- `RimChat/UI/UGui/panels/WorkspaceSidePanel.cs` — 侧面板 tab 按钮栏（Preview/FullPreview/Variables）
- `RimChat/UI/UGui/panels/WorkspaceEditorPanel.cs` — 编辑器 chrome（元数据行、工具栏、验证状态）

**修改文件**：
- `RimChat/UI/UGui/UGuiFeatureFlags.cs` — 新增 3 个 Flag：`UseUguiHeaderPanel`、`UseUguiPresetPanel`、`UseUguiEditorPanel`；`SyncFromSettings` 签名扩展为 7 参数
- `RimChat/Config/RimChatSettings.cs` — 新增 `_useUguiHeaderPanel`、`_useUguiPresetPanel`、`_useUguiEditorPanel` 字段 + `Scribe_Values.Look` + 同步调用
- `RimChat/Config/RimChatSettings_PromptSectionWorkspace.cs` — 新增 4 组 UGUI Host/Panel/Bridge 字段 + `DrawPromptWorkspaceHeader`/`DrawPromptWorkspacePresetPanel`/`DrawPromptWorkspaceSidePanel`/`DrawPromptWorkspaceEditorPanel` UGUI 路径分支 + `DisposePromptWorkspaceRenderTextures` 扩展清理 + `InvalidatePromptWorkspacePreviewCache`/`InvalidatePromptWorkspaceNodeUiCaches` 脏标记传播

**架构说明**：
- 每个面板区域拥有独立的 `UGuiCanvasHost`（独立 Canvas + Camera + RenderTexture + EventSystem）
- 所有面板默认开启 UGUI 渲染，通过 Feature Flag 可逐面板回退到 IMGUI
- 交互回调通过 lambda 闭包桥接到原有业务逻辑（`SchedulePromptWorkspaceNavigation`、`EnsurePromptWorkspaceEditablePresetForMutation` 等）
- `PromptWorkbenchModuleItem` 为 struct，不能用 `== null`，改用 `string.IsNullOrEmpty(target.Id)` 检查空值
- 右侧面板 Variables tab 仍走 IMGUI 直出（有 TextField 交互），Preview/FullPreview 走 UGUI

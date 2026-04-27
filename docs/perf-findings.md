# RimChat 提示词工作台性能调查 — 最终报告

## 根因

**Side Panel 预览内容在 Layout + Repaint 中各渲染一遍（3.5ms × 2 = 7ms/帧）。** 预览的 `DrawContent` 每帧渲染所有 Block（header + body label × N），Layout 事件本应只做布局计算，却执行了完整渲染。

## 最终修复（保留在生产代码中）

| 修复 | 文件 | 效果 |
|------|------|------|
| Side Panel 预览 Layout 跳过 | `RimChatSettings_PromptSectionWorkspace.cs` | Layout 3.5ms → 0.1ms |
| 预览渲染器 Layout 守卫 | `PromptWorkspaceStructuredPreviewRenderer.cs` | 双重保护 |
| 弹窗→内联渲染 | `RimChatMod.cs` + `RimChatSettings.cs` | 匹配 RimTalk 架构 |
| GUI.changed 守卫 | `RimChatMod.cs` | 阻止 ExposeData 热路径 |
| 字体预光栅化 | `RimChatSettings_PromptSectionWorkspace.cs` | 消除首次字形开销 |
| 模块列表简化 (ButtonText) | `NodeLayout.cs` | 每行 4→1 Widget |
| 元数据行移除 | `PromptSectionWorkspace.cs` | 冗余信息已删除 |
| WordWrap/Translate 缓存 | `NodeLayout.cs` | 消除每行状态切换 |

## 最终性能

```
Layout Body: 0.2ms
Repaint Body: 0.3ms
Total: 0.5ms/帧 = ~2000fps (workbench alone)
```

## 诊断过程中的关键发现

1. **ExposeData 未被每帧调用** — 仅在设置页面打开/关闭时触发
2. **ScrollView 零开销** — SV=0.0ms 确认
3. **Widget 数量弱相关** — 削减 80% Widget 仅省 0.8ms
4. **Per-panel 隔离测试** — Preset 0.1ms, Editor 0.2ms, Side 3.5ms
5. **Preview/Variables 标签差异** — Preview 3.5ms, Variables 0ms

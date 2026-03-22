Walkthrough: 外交对话聊天回包丢弃问题修复

Problem
在 v0.7.70 引入的 `DialogueRequestLease` 生命周期验证机制后，打开外交对话窗口并发送消息时，AI 回包被错误地判定为"过期"并丢弃，导致无法聊天。

影响范围：所有外交对话场景

Root cause: 闭包捕获 requestId 变量的时序问题 + Lease 引用验证缺陷

在 `DiplomacyConversationController.TrySendDialogueRequest` 方法中：
1. 第 47-54 行：lambda 回调闭包在注册时捕获了 `requestId` 变量引用，但此时变量值为 `string.Empty`
2. 第 63-64 行：`requestId` 才被正式赋值，同时 lease 和 session 才开始绑定

回调执行时，`IsRequestContextStillValid` 通过 `session.pendingRequestId`（正确）与闭包中的 `requestId`（空字符串）进行比对，导致 `pending_request_mismatch`。

根本原因：v0.7.70 的验证机制新增了严格的 `requestId` 校验，但原代码存在时序缺陷。

The Solution: Lease 驱动验证 + 闭包引用修正

核心思路：使用 lease 对象作为闭包上下文，而非直接捕获 requestId。Lease 的 `RequestId` 属性通过线程安全的方式获取，保证时序正确。

1. 闭包捕获 lease 而非 requestId — [DiplomacyConversationController.cs#L48-L53](file:///c:\Users\Administrator\source\repos\RimChat\RimChat\DiplomacySystem\DiplomacyConversationController.cs#L48-L53)

关键代码:
```csharp
// Before (有问题):
string requestId = string.Empty;
requestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
    messages,
    onSuccess: response => HandleSuccess(session, faction, requestId, ...), // ❌ requestId 闭包为 ""
    ...
);

// After (已修复):
string requestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
    messages,
    onSuccess: response => HandleSuccess(session, faction, lease, requestContext, response, onSuccess, onDropped),
    onError: error => HandleError(session, faction, lease, requestContext, error, onError, onDropped),
    onProgress: progress => HandleProgress(session, faction, lease, requestContext, progress, onProgress),
    ...
);
```

变更说明:
- 移除 `requestId` 的空字符串初始化
- 回调闭包直接使用 `lease` 作为上下文，而非 `requestId` 变量
- `requestId` 的获取顺序不变，但闭包不再依赖它

2. Handler 和验证方法签名重构 — [DiplomacyConversationController.cs#L194-L315](file:///c:\Users\Administrator\source\repos\RimChat\RimChat\DiplomacySystem\DiplomacyConversationController.cs#L194-L315)

关键代码:
```csharp
// HandleSuccess/HandleError/HandleProgress 签名变更:
// Before: string requestId
// After:  DialogueRequestLease lease

private static void HandleSuccess(
    FactionDialogueSession session,
    Faction faction,
    DialogueRequestLease lease,           // 新参数
    DialogueRuntimeContext runtimeContext,
    string response,
    Action<string> onSuccess,
    Action<string> onDropped)
{
    if (!IsRequestContextStillValid(session, faction, lease, runtimeContext, out string droppedReason))
    ...
}
```

变更说明:
- Handler 方法使用 `lease` 参数替代 `requestId`
- `IsRequestContextStillValid` 方法签名同步变更

3. 验证逻辑增强 — [DiplomacyConversationController.cs#L271-L295](file:///c:\Users\Administrator\source\repos\RimChat\RimChat\DiplomacySystem\DiplomacyConversationController.cs#L271-L295)

关键代码:
```csharp
private static bool IsRequestContextStillValid(
    FactionDialogueSession session,
    Faction faction,
    DialogueRequestLease lease,
    DialogueRuntimeContext runtimeContext,
    out string reason)
{
    reason = string.Empty;
    if (session == null || faction == null || faction.defeated || lease == null)
    {
        reason = "request_context_null";
        return false;
    }

    string requestId = lease.RequestId;  // 从 lease 获取，而非闭包
    if (string.IsNullOrEmpty(requestId))
    {
        reason = "lease_request_id_empty";
        return false;
    }

    if (!string.Equals(session.pendingRequestId, requestId, StringComparison.Ordinal))
    {
        reason = "pending_request_mismatch";
        return false;
    }

    // 新增：验证 lease 引用一致性
    if (session.pendingRequestLease == null || !ReferenceEquals(session.pendingRequestLease, lease))
    {
        reason = "request_lease_mismatch";
        return false;
    }
    ...
}
```

变更说明:
- 通过 `lease.RequestId` 获取请求 ID（线程安全）
- 新增 `request_lease_mismatch` 检查，防止 lease 引用被替换

Testing
游戏内测试步骤：
1. 启动 RimWorld，加载包含 RimChat mod 的存档
2. 打开任意派系的外交对话窗口
3. 输入消息并发送
4. 验证 AI 响应正常显示（而非"已丢弃过期对话回包"系统消息）

回归测试：
1. 外交窗口打开 -> 发送消息 -> 等待响应 -> 再次发送（验证连续对话）
2. 外交窗口快速连续发送多条消息（验证防抖逻辑）
3. 外交窗口切换派系再切回（验证 lease 清理）
4. 存档/加载后外交对话（验证 session 恢复）

预期结果：所有外交聊天场景均正常工作，不再出现回包丢弃问题。
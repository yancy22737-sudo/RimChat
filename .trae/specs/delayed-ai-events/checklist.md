# Checklist

## 功能验证

- [ ] `DelayedAIEvent` 类已创建，包含所有必要字段
- [ ] `DelayedAIEvent` 类实现了 `IExposable` 接口
- [ ] `GameComponent_DiplomacyManager` 中添加了延迟事件队列
- [ ] 实现了延迟事件的添加、查询、移除方法
- [ ] 在 `GameComponentTick` 中实现了延迟事件触发检查
- [ ] 延迟时间计算逻辑正确：基础时间 × (1 ± 好感度影响) ± 随机偏移
- [ ] `ExecuteRequestCaravan` 改为添加延迟事件而非立即触发
- [ ] `ExecuteRequestAid` 改为添加延迟事件而非立即触发
- [ ] 其他AI动作保持原样立即执行
- [ ] 在 `RimDiplomacySettings` 中添加了 `EventDelayBaseHours` 配置项
- [ ] 在设置界面中添加了延迟时间滑块控件
- [ ] 设置配置正确序列化/反序列化
- [ ] 在 `Dialog_DiplomacyDialogue` 中添加了待处理事件显示区域
- [ ] 显示事件类型、触发时间、剩余时间
- [ ] 待处理事件界面为只读，无取消功能
- [ ] 中文语言文件包含所有新语言键
- [ ] 英文语言文件包含所有新语言键
- [ ] 延迟事件正确保存到游戏存档
- [ ] 延迟事件正确从游戏存档加载
- [ ] 项目可以成功 build，无错误
- [ ] 在游戏中测试：对话返回商队/援助时，事件正确添加到队列
- [ ] 在游戏中测试：延迟时间到，事件正确触发
- [ ] 在游戏中测试：不同好感度下延迟时间计算正确

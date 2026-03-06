# Checklist

## OpportunitySite_ItemStash 修复验证

- [x] 当 AI 请求创建 OpportunitySite_ItemStash 时，不再出现 "Could not resolve site parts" 错误
- [x] 当 AI 请求创建 OpportunitySite_ItemStash 时，不再出现 NullReferenceException
- [x] 任务成功生成后，玩家可以在游戏中看到任务通知
- [x] 永久敌对派系（海盗）请求此任务时，自动回退到 RimDiplomacy_AIQuest
- [x] 部落派系请求此任务时，自动回退到 RimDiplomacy_AIQuest

## Mission_BanditCamp 修复验证

- [x] 当 AI 请求创建 Mission_BanditCamp 时，不再出现 "invalid required pawn count (-1)" 错误
- [x] 任务成功生成后，requiredPawnCount 值在 2-5 之间
- [x] 非 Empire/Outlander 派系请求此任务时，自动回退到其他任务
- [x] 未安装 Royalty DLC 时，自动回退到 RimDiplomacy_AIQuest

## 错误处理验证

- [x] 站点生成失败时，自动回退到 RimDiplomacy_AIQuest
- [x] 回退时记录警告日志，说明回退原因
- [x] 回退后任务仍能正常创建，不会导致对话中断
- [x] 回退时保留原始任务类型信息，生成有意义的标题和描述

## 代码质量验证

- [x] build.ps1 编译通过，无错误
- [x] 代码符合项目规范（单文件 < 800 行，单函数 < 30 行）
- [x] 无硬编码 UI 文本

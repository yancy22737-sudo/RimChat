# Tasks

- [x] Task 1: 修复 OpportunitySite_ItemStash 任务生成
  - [x] SubTask 1.1: 在 CreateQuest 中添加 OpportunitySite_ItemStash 专用参数预处理
  - [x] SubTask 1.2: 确保 siteFaction 正确设置且非永久敌对
  - [x] SubTask 1.3: 确保 points 有合理默认值（>= 200）
  - [x] SubTask 1.4: 添加站点生成失败时的异常捕获和回退逻辑

- [x] Task 2: 修复 Mission_BanditCamp 任务生成
  - [x] SubTask 2.1: 移除对 requiredPawnCount 的强制设置，让原版脚本自行计算
  - [x] SubTask 2.2: 确保 enemyFaction 正确选择（敌对派系）
  - [x] SubTask 2.3: 添加 Royalty DLC 检查
  - [x] SubTask 2.4: 添加派系类型检查（仅限 Empire/OutlanderCivil/OutlanderRough 发起）

- [x] Task 3: 增强错误处理和回退机制
  - [x] SubTask 3.1: 在 catch 块中识别站点生成错误
  - [x] SubTask 3.2: 自动回退到 RimDiplomacy_AIQuest
  - [x] SubTask 3.3: 记录详细的警告日志

- [x] Task 4: 验证修复效果
  - [x] SubTask 4.1: 运行 build.ps1 确保编译通过
  - [ ] SubTask 4.2: 在游戏中测试 OpportunitySite_ItemStash 任务生成
  - [ ] SubTask 4.3: 在游戏中测试 Mission_BanditCamp 任务生成

# Task Dependencies

- [Task 4] depends on [Task 1], [Task 2], [Task 3]
- [Task 1], [Task 2], [Task 3] 可以并行执行

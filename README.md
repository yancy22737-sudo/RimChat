# RimDiplomacy - AI Driven Faction Diplomacy

为 RimWorld 带来 AI 驱动的派系外交系统！

## 功能特性

- **AI 控制派系**: 让派系由 AI 控制，实现动态外交
- **智能对话系统**: 与 AI 派系领袖进行外交对话
- **动态世界新闻**: 每 2-3 天播报世界动态
- **智能事件触发**: 作为原版 Storyteller 的补充

## 构建说明

### 前置要求

- .NET Framework 4.8 SDK
- RimWorld 1.6
- Harmony Mod

### 快速构建

#### 方法 1: 使用批处理脚本 (推荐)

```bash
# Debug 构建 (开发测试)
build.bat

# Release 构建 (发布)
build-release.bat
```

#### 方法 2: 使用 PowerShell 脚本

```powershell
# Debug 构建并部署
.\build.ps1

# Release 构建
.\build.ps1 -Configuration Release

# 仅构建，不部署
.\build.ps1 -NoDeploy

# 自定义部署路径
.\build.ps1 -ModDeployPath "D:\Games\RimWorld\Mods\RimDiplomacy"
```

#### 方法 3: 使用 Visual Studio

1. 打开 `RimDiplomacy.sln`
2. 选择配置 (Debug/Release)
3. 按 F5 构建

### 清理构建

```powershell
# 清理构建产物
.\clean.ps1

# 深度清理 (包括已部署的 mod)
.\clean.ps1 -Deep
```

## 部署路径

默认部署到:
```
E:\SteamLibrary\steamapps\common\RimWorld\Mods\RimDiplomacy
```

## 项目结构

```
RimDiplomacy/
├── About/                    # Mod 元数据
│   └── About.xml
├── 1.6/                      # 版本 1.6 内容
│   ├── Assemblies/           # 编译后的 DLL
│   ├── Defs/                 # XML 定义
│   │   ├── MainButtonDefs.xml
│   │   └── WorldObjectDefs.xml
│   └── Languages/            # 本地化文件
│       ├── ChineseSimplified/
│       └── English/
├── RimDiplomacy/             # C# 源代码
│   ├── Core/                 # 核心逻辑
│   │   ├── RimDiplomacyMod.cs
│   │   ├── RimDiplomacySettings.cs
│   │   └── GameComponent_DiplomacyManager.cs
│   └── UI/                   # UI 界面
│       ├── MainTabWindow_RimDiplomacy.cs
│       └── Dialog_DiplomacyDialogue.cs
├── build.ps1                 # 构建脚本
├── build.bat                 # 批处理构建
├── build-release.bat         # Release 构建
├── clean.ps1                 # 清理脚本
└── README.md                 # 本文件
```

## 开发规范

- 单文件 < 800 行
- 单函数 < 30 行
- 嵌套 < 3 层
- 分支 < 3 个

## 版本历史

### v1.0.0
- 初始版本
- AI 派系控制
- 基础外交对话
- 世界新闻系统

## 许可证

MIT License

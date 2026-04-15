### 核心开发规范 
1. **代码架构**
   - 模块化拆分UI为独立组件，分离UI表现与业务逻辑，降低单文件复杂度。
   - **禁止“上帝类”**：单文件超150KB、职责不清（混合UI/业务/数据/状态）、高耦合、大量静态字段存UI状态均不允许。
   - **硬阈值**：单文件<800行，单函数<30行，嵌套<3层，分支<3个。
   - 确保power shell使用UTF-8编码格式
   - 使用英文注释
   - 提示词构建，做到可维护/可本地化：把文本移到配置或模板文件，再做占位符渲染。
   - 努力争取全局最优而不是局部最优，遇到问题优先系统性修复，不做单点兜底的保守修复。
   - 遇到风险较大的修改必须深入思考，禁止表面修改而避开业务链路



2. **工具与参考**
   - 以下文件夹内容仅作参考，不属于本项目。
   - RimTalk-main
   - VanillaExpandedFramework-220226

3. **文档维护**
   - **Index.md**：检索优先权重，需清晰描述项目模块划分，生成全局地图、各模块成员清单与接口说明，每个文件头部声明依赖和职责；每次代码变更后强制回环检查文件头依 赖、更新模块文档。
   - **Api.md**：程序开发接口文档，开发者根据文档辅助编写代码。
   - **config.md**：外部配置说明文档，提供用户自定义设置选项。
   - **VersionLog.txt**：检索优先权重，每次版本更新时添加更新内容。
   - **.gitignore**

4. **版本与适配**
   - 版本号格式为x.y.z（主/次/修订），每次build前必须升级。禁止删减旧版本号和内容。
   - 同步维护这两个版本日志: VersionLog_en.txt, VersionLog.txt，About.xml
   - 新功能涉及UI显示需做语言键适配！禁止硬编码UI文本！
   - 修改已有Def必须用PatchOperation。
   - Harmony Patch 需谨慎：在对基类（如 `Window`）进行 Patch 时，目标过滤逻辑必须极其严密。

5. **测试与环境**
   - 项目实际运行测试地址：E:\SteamLibrary\steamapps\common\RimWorld\Mods\
   - 日志："C:\Users\Administrator\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log"
   - build流程为运行build.ps1,直到修复报错.知道最后一轮构建结束才应该启动run.ps1
   - 项目环境为Rimworld 1.6,.net4.8,注意Harmony Patch相关问题。

6. **开发决策与限制**
   - 当我问你问题时，向我回答而不是直接修改代码。
   - 模糊/难实现的开发方向，优先向用户提问。
   - 难修复的bug，禁止阉割功能，优先向用户提问。
   - **禁止修改游戏本体**：包括直接修改源文件、资源文件、配置文件。
   - 必须兼容旧存档。mod兼容性高，不会导致游戏崩溃或数据丢失是底线。
   - xml语言文件注意转义字符问题!
   - 你是一名专业的边缘世界unity mod开发者。
   - 实时维护多语言系统

7. **回答前置** ​【非常重要】
   - 请你在回答前，先向用户提出详细的差异化的问题，并给出带字母的选项和推荐选项。​
   - 要求:​
   - 根据我的回答，有必要则继续追问。​
   - 直到你有90%的信心理解我的真实需求和目标。​
   - 然后才给出方案并实施。

8. **语言使用简体中文回复，用易懂的方式解释术语**

方案规范
 
9. **当需要你给出修改或重构方案时必须符合以下规范**

- 使用Fail fast思想
 
- 不允许给出兼容性或补丁性的方案
​
- 不允许过度设计，保持最短路径实现且不能违反第一条要求
​
- 不允许自行给出我提供的需求以外的方案，例如一些兜底和降级方案，这可能导致业务逻辑偏移问题
​
- 必须确保方案的逻辑正确，必须经过全链路的逻辑验证

10.debug时：请分析该问题涉及的变量及其相互影响关系。我的目标是彻底根除此问题，请提供一套包含‘短期阻断’和‘根本性修复’的解决方案，并指出如果只做表面修复可能带来的副作用。

11.修改默认提示词时，要求在提示词工作台所见即所得。

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **RimChat** (10464 symbols, 85919 relationships, 910 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> If any GitNexus tool warns the index is stale, run `npx gitnexus analyze` in terminal first.

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `gitnexus_impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `gitnexus_detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `gitnexus_query({query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `gitnexus_context({name: "symbolName"})`.

## When Debugging

1. `gitnexus_query({query: "<error or symptom>"})` — find execution flows related to the issue
2. `gitnexus_context({name: "<suspect function>"})` — see all callers, callees, and process participation
3. `READ gitnexus://repo/RimChat/process/{processName}` — trace the full execution flow step by step
4. For regressions: `gitnexus_detect_changes({scope: "compare", base_ref: "main"})` — see what your branch changed

## When Refactoring

- **Renaming**: MUST use `gitnexus_rename({symbol_name: "old", new_name: "new", dry_run: true})` first. Review the preview — graph edits are safe, text_search edits need manual review. Then run with `dry_run: false`.
- **Extracting/Splitting**: MUST run `gitnexus_context({name: "target"})` to see all incoming/outgoing refs, then `gitnexus_impact({target: "target", direction: "upstream"})` to find all external callers before moving code.
- After any refactor: run `gitnexus_detect_changes({scope: "all"})` to verify only expected files changed.

## Never Do

- NEVER edit a function, class, or method without first running `gitnexus_impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `gitnexus_rename` which understands the call graph.
- NEVER commit changes without running `gitnexus_detect_changes()` to check affected scope.

## Tools Quick Reference

| Tool | When to use | Command |
|------|-------------|---------|
| `query` | Find code by concept | `gitnexus_query({query: "auth validation"})` |
| `context` | 360-degree view of one symbol | `gitnexus_context({name: "validateUser"})` |
| `impact` | Blast radius before editing | `gitnexus_impact({target: "X", direction: "upstream"})` |
| `detect_changes` | Pre-commit scope check | `gitnexus_detect_changes({scope: "staged"})` |
| `rename` | Safe multi-file rename | `gitnexus_rename({symbol_name: "old", new_name: "new", dry_run: true})` |
| `cypher` | Custom graph queries | `gitnexus_cypher({query: "MATCH ..."})` |

## Impact Risk Levels

| Depth | Meaning | Action |
|-------|---------|--------|
| d=1 | WILL BREAK — direct callers/importers | MUST update these |
| d=2 | LIKELY AFFECTED — indirect deps | Should test |
| d=3 | MAY NEED TESTING — transitive | Test if critical path |

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/RimChat/context` | Codebase overview, check index freshness |
| `gitnexus://repo/RimChat/clusters` | All functional areas |
| `gitnexus://repo/RimChat/processes` | All execution flows |
| `gitnexus://repo/RimChat/process/{name}` | Step-by-step execution trace |

## Self-Check Before Finishing

Before completing any code modification task, verify:
1. `gitnexus_impact` was run for all modified symbols
2. No HIGH/CRITICAL risk warnings were ignored
3. `gitnexus_detect_changes()` confirms changes match expected scope
4. All d=1 (WILL BREAK) dependents were updated

## Keeping the Index Fresh

After committing code changes, the GitNexus index becomes stale. Re-run analyze to update it:

```bash
npx gitnexus analyze
```

If the index previously included embeddings, preserve them by adding `--embeddings`:

```bash
npx gitnexus analyze --embeddings
```

To check whether embeddings exist, inspect `.gitnexus/meta.json` — the `stats.embeddings` field shows the count (0 means no embeddings). **Running analyze without `--embeddings` will delete any previously generated embeddings.**

> Claude Code users: A PostToolUse hook handles this automatically after `git commit` and `git merge`.

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->

# UTAgent 扩展点（Tools / Hooks / Skills）

> **权威表**：改钩子或 skills 路径时先改本文。  
> 学习对照：[Docs/ut-agent/19-pi-extension-model](../../../Docs/ut-agent/19-pi-extension-model.md) · 包地图 [22](../../../Docs/ut-agent/22-utagent-package-map.md)  
> C#↔Python 桥原理：[python-interop-bridge.md](./python-interop-bridge.md)  
> OpenSpec：`utagent-extension-points` · `domain-pack-conventions` · `shared-exec-policy`

## 一句话

Pi Extension = 不改核心循环、热挂 tool/钩子。  
**UTAgent 没有 Extension Host**；扩展 = 仓库内 **钩子 + skill + `unity.*` 动词 + 域 assert**（改代码/资源，不是丢一个热加载插件包）。

---

## 分层（L0–L3）

```
L0 Core          ReAct · execPython · loadSkill · session · compaction
L1 Exec Policy   跨入口安全策略（Chat + CLI 共用）→ Editor/Core/UTAgentExecPolicy.cs
L2 Domain Pack   按域：verbs / skill / 可选域钩子 / assert / IDE 流程
L3 Orchestrator  Editor Chat（评测）vs 编码助手 + CLI（交付）
```

| 层 | 共享范围 | 说明 |
|----|----------|------|
| L1 | Chat `execPython` + CLI `POST /exec` | fs-walk / code-too-long / heavy-reflection |
| 域钩子 | **仅 Chat** | UI skill 门、layout-control（依赖 history） |
| 域 assert | 两边可调 | 薄入口 `run_assert_ui_scene_health.py`（交付门禁 ≠ L2 分数；实现见 `assert_ui_scene_health.py`） |
| L2 评测分 | **仅 Chat 跑表** | 日常 C\*；CLI 交付成功不得记为 L2 PASS |
| L1 观测 | Chat + CLI | `code-too-long` 等写入 `Out/logs/exec_policy_yyyyMMdd.log`；**勿**未观测就抬 `CodeSizeLimit` 对齐上下文 |

**不要**用含糊的「harness」同时指代评测跑表、执行守卫与交付流程。

---

## Tools（OpenAI tool 名）

Schema：`Editor/Agent/Loop/UTAgentRunner.Json.cs`  
执行：`UTAgentRunner.ExecuteToolCalls`（`UTAgentRunner.cs`）

| 名 | 实现入口 | 怎么扩展 |
|----|----------|----------|
| `execPython` | → Python `execute_python_code` | 新 Unity 能力优先 `Python/unity/*.py`（± Bridge）；**默认不加**新 OpenAI tool 名 |
| `loadSkill` | → 读 `Python/agent/skills/*.md.txt` | 新领域知识加 skill 文件，不堆 system prompt |

---

## Hooks / Policy

| 钩子 | 文件 | 调用点 |
|------|------|--------|
| **L1 Exec Policy** | `Editor/Core/UTAgentExecPolicy.cs` | Chat `BeforeExecCheck` **与** CLI `HandleExec` |
| before-exec（域） | `UTAgentRunner.BeforeExec.cs` | Chat：layout-control / skill 门（在 L1 之后） |
| after-tool（截断等） | `UTAgentRunner.AfterTool.cs` | Chat append tool result 前 |
| after-tool no-progress | `UTAgentRunner.AfterTool.NoProgress.cs` | Chat AfterTool 链路内 |

**公约：**

- 与 history 无关的危险代码拦截 → **`UTAgentExecPolicy`（L1）**，禁止只写在 Runner 私有逻辑导致 CLI 旁路  
- 截断 / 无进展 → after-tool partial  
- **禁止** 往 `ExecuteToolCalls` 大 if 里堆策略  

---

## Skills

| 种类 | 位置 | 用途 |
|------|------|------|
| Agent `loadSkill` | `Python/agent/skills/*.md.txt` | 运行时按名注入领域知识（如 `editor-ui`） |
| 编码助手 `exec` skill | `agent-skills/utagent-unity-exec` | 给人 / Cursor / Claude Code 等的 `utagent exec` 流程，**不是** `loadSkill` |

现有 agent skills 示例：`editor-ui`、`editor-ui-debug`、`python-interop`。

---

## Domain Pack：加域检查表

新域（UI 已有；未来如 tilemap）按清单挂，**不要改 L0 工具表**：

| # | 项 | 落点 |
|---|----|------|
| 1 | 操作 / 查询（可选） | `Python/unity/<domain>*.py`（± Bridge） |
| 2 | 领域配方 | `Python/agent/skills/editor-<domain>.md.txt` |
| 3 | 域专属 before（可选） | `BeforeExec.cs` 域分区；**勿**把全局安全只放这里 |
| 4 | 结果门禁 | `Tools/ui-benchmark/assert_*.py`（或日后 `Tools/harness/<domain>/`） |
| 5 | IDE 流程 | `agent-skills/utagent-unity-exec`；经 `utagent skill list/get` 发现配方与可选 assert |
| 6 | Chat 评测（可选） | 按需 L2 C\*；**默认不进日常清单** |

当前 **ui** Pack 示例：skill=`editor-ui`（frontmatter 可含 `assert:`）；IDE 先 `skill list/get` 再拼装。领域发现走 **skill catalog**，**不要**为每个域加 CLI 子命令。

---

## 新能力落点公约

```
想加能力？
├─ Unity 操作 / 查询     → Python/unity/*.py（± Bridge）
├─ 跨入口安全（L1）      → UTAgentExecPolicy（Chat + CLI）
├─ Chat 截断 / 无进展    → after-tool partial
├─ 领域写法 / 配方       → skills/*.md.txt（Domain Pack）
├─ 结果断言              → harness assert 脚本
├─ 编码助手流程          → agent-skills/
├─ 编码助手侧糖命令      → 仅高频才加 CLI；否则写 exec 配方
└─ 新 OpenAI tool 名     → 默认否
```

---

## 非目标（本表不暗示要做）

- 热加载 Extension Host（Pi 式）
- 按 MCP 堆几十个命名 tool
- 为每个 `unity.*` 加 CLI 子命令
- 用 CLI 交付成绩替代 L2 评测分

下一结构波次：见 [23-agent-structure-refactor-plan](../../../Docs/ut-agent/23-agent-structure-refactor-plan.md)。

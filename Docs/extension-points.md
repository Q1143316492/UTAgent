# UTAgent 扩展点（Tools / Hooks / Skills）

> **权威表**：改钩子或 skills 路径时先改本文。  
> 学习对照：[Docs/ut-agent/19-pi-extension-model](../../../Docs/ut-agent/19-pi-extension-model.md) · 包地图 [22](../../../Docs/ut-agent/22-utagent-package-map.md)  
> C#↔Python 桥原理：[python-interop-bridge.md](./python-interop-bridge.md)  
> OpenSpec：`utagent-extension-points`

## 一句话

Pi Extension = 不改核心循环、热挂 tool/钩子。  
**UTAgent 没有 Extension Host**；扩展 = 仓库内 **钩子 partial + skill 文件 + `unity.*` 动词**（改代码/资源，不是丢一个热加载插件包）。

---

## Tools（OpenAI tool 名）

Schema：`Editor/Agent/Loop/UTAgentRunner.Json.cs`  
执行：`UTAgentRunner.ExecuteToolCalls`（`UTAgentRunner.cs`）

| 名 | 实现入口 | 怎么扩展 |
|----|----------|----------|
| `execPython` | → Python `execute_python_code` | 新 Unity 能力优先 `Python/unity/*.py`（± `Editor/Bridge` partial）；**默认不加**新 OpenAI tool 名 |
| `loadSkill` | → 读 `Python/agent/skills/*.md.txt` | 新领域知识加 skill 文件，不堆 system prompt |

---

## Hooks

均在 `Editor/Agent/Loop/`，`partial class UTAgentRunner`。

| 钩子 | 文件 | 调用点 |
|------|------|--------|
| before-exec | `UTAgentRunner.BeforeExec.cs` | `execPython` 执行前 `BeforeExecCheck`（含 code-too-long / heavy-reflection / **fs-walk** / layout-control / skill 门） |
| after-tool（截断等） | `UTAgentRunner.AfterTool.cs` | append tool result 前 `AfterToolProcess` |
| after-tool no-progress | `UTAgentRunner.AfterTool.NoProgress.cs` | AfterTool 链路内调用 |

**公约：** 新安全 / 截断 / 无进展策略 → **新 partial 或现有 partial 内独立方法**。  
**禁止** 往 `ExecuteToolCalls` 大 if 里堆策略。

---

## Skills

| 种类 | 位置 | 用途 |
|------|------|------|
| Agent `loadSkill` | `Python/agent/skills/*.md.txt` | 运行时按名注入领域知识（如 `editor-ui`） |
| 编码助手 `exec` skill | `agent-skills/utagent-unity-exec`（拷到各工具的 skills 目录） | 给人 / 编码助手的 `utagent exec` 配方，**不是** `loadSkill` |

现有 agent skills 示例：`editor-ui`、`editor-ui-debug`、`python-interop`。

---

## 新能力落点公约

```
想加能力？
├─ Unity 操作 / 查询     → Python/unity/*.py（± Editor/PythonInterop Bridge partial）
│                          桥原理见 python-interop-bridge.md
├─ 安全 / 截断 / 无进展  → before-exec 或 after-tool 新/现有 partial
├─ 领域写法 / 配方       → Python/agent/skills/*.md.txt
├─ 编码助手侧糖命令      → 仅高频才加 CLI；否则 agent skill 写 exec 配方
└─ 新 OpenAI tool 名     → 默认否；除非 execPython 无法表达
```

| 想加什么 | 落点 |
|----------|------|
| 新 Unity 操作 | `unity/*.py`（± Bridge） |
| 新安全/截断策略 | after-tool / before-exec **partial**，禁止塞 `ExecuteToolCalls` |
| 新领域知识 | skill md，不堆 system |
| 给编码助手新糖命令 | 仅高频；否则 skill 写 `exec` 配方 |

---

## 非目标（本表不暗示要做）

- 热加载 Extension Host（Pi 式）
- 按 MCP 堆几十个命名 tool
- 为每个 `unity.*` 加 CLI 子命令

下一结构波次：Runner 瘦身 / Transport → 见 [23-agent-structure-refactor-plan](../../../Docs/ut-agent/23-agent-structure-refactor-plan.md) Wave 3。

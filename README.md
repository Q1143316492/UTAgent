# UTAgent

Unity Editor 内的 LLM Agent：用 Python（`execPython` / `loadSkill`）操控场景与 UI，可选 CLI 给 Cursor 等外挂编排。

## 安装

把本仓库放到目标项目的 `Assets/UTAgent/` 后，**按环境初始化 skill 做一次即可**：

→ [`Docs/skills/utagent-env-bootstrap/SKILL.md`](Docs/skills/utagent-env-bootstrap/SKILL.md)

（PythonHome、IDE skill、`UTAGENT_API_KEY`、Settings 初始化引擎都在该 skill 里。）

配置 / Chat / Session / CLI / 目录等**操作细则**见 → [`Docs/setup-and-config.md`](Docs/setup-and-config.md)

## 功能特性

| 能力 | 说明 |
|------|------|
| **Editor Chat** | 窗口内 ReAct：LLM → `execPython` / `loadSkill` → 观察回灌 |
| **Remote CLI** | `ping` / `exec` / `chat` / `screenshot` / `log`；外挂多次单步操控 Editor |
| **unity 动词 + CS.*** | L1 场景/截图/自省；不够再 `from unity_bind import CS` |
| **Skills / Hooks** | 按需 `loadSkill`（如 `editor-ui`）；before/after-exec 守卫 |
| **Session** | 可恢复会话 JSONL；审计 `agent_*.log` |
| **拼 UI harness** | L1 门禁 + L2 chat 验收（设置/登录/角色等）；见 `Docs/ui-assembly-benchmark.md` |

扩展落点：[`Docs/extension-points.md`](Docs/extension-points.md)。Cursor 侧 skill 源：`agent-skills/utagent-unity-exec/`（由 bootstrap 安装）。

## 开发进度（早期）

重心是 **拼 UI 评测闭环**（文字 brief → Chat/L2 → health → 目检），CLI 能力面已够用、默认冻结扩张。

已对齐：Chat + CLI、hooks/skills/session、日常 harness、Agent 结构分夹（`Loop` / `Session` / `UI`）。

### 早期成果：全屏标题屏（L2 chat）

结构化 brief（纯文本模型、不喂参考图）拼出星露谷风格主菜单骨架——`WndTitle` + 标题牌 + 底栏四钮。health 全绿；美术按约定降级为纯色块。

![WndTitle early — Stardew-like title screen via L2 chat](Docs/examples/wndtitle-stardew-early.png)

> **2026-07 早期样例**，证明「文字 → Canvas」可交付可读界面。日常回归仍以 [`Docs/ui-assembly-benchmark.md`](Docs/ui-assembly-benchmark.md) 的 C02/C14/C15 为准。后续练习可继续压网格/HUD/对白框等布局，顺带挖 harness 缺口。

### 早期成果：创建角色屏（Cursor + CLI）

外挂编排第二条路径：Cursor 会话按 `utagent-unity-exec` → `skill list/get` → 多次 `exec --file`（纠偏脚本）→ `screenshot` 目检，修齐 `WndCharacterCreate` 列对齐与孤儿控件。此为**交付样例**，不等于 L2 chat 用例 PASS。

![WndCharacterCreate early — Cursor + utagent exec delivery](Docs/examples/wndcharactercreate-cursor-exec-early.png)

> **2026-07 早期样例**，证明「编码助手 + CLI」可在 Editor 外闭环拼/改 UI；配方与 assert 走 skill catalog，长脚本优先 `--file`（`Out/exec/`）。

# 安装、配置与使用细则

> README 只保留概览；本文件承接原 README 中的操作细则。  
> **新机 / 环境初始化优先跟** [`skills/utagent-env-bootstrap/SKILL.md`](./skills/utagent-env-bootstrap/SKILL.md)。

## 放入工程

```bash
git clone git@github.com:Q1143316492/UTAgent.git Assets/UTAgent
# 或
git submodule add git@github.com:Q1143316492/UTAgent.git Assets/UTAgent
```

## 环境与 API Key

详见 bootstrap skill。摘要：

- API Key：环境变量（默认名 `UTAGENT_API_KEY`）；用户变量改完需**重启 Unity**
- Python：只认 `Assets/UTAgent/PythonHome/`（`Install-PythonHome.ps1` 或 Settings → Python）
- IDE skill：`Install-IdeSkills.ps1`（含 `utagent-unity-exec`）

然后 `Window/UT Agent/Agent Chat` 发消息即可。

## Settings（`Window/UT Agent/Settings`）

| Tab | 内容 |
|-----|------|
| 大模型 | Provider / Model、Max Steps、API Key 环境变量名 |
| Python | 状态 +「下载并初始化」；路径/重置在「高级」 |
| CLI | Remote CLI 启用与端口（默认开启） |
| 运行产物 | 目录（默认 `Assets/UTAgent/Out/`；子目录 `logs/` / `screenshots/` / `sessions/` / `exec/`） |

- 配置：`Config/utagent.defaults.json` + `Config/utagent.local.json`（gitignore）
- API Key **不**写入 JSON
- 打开 Chat 时按 json 同步 CLI；发消息时自动 `Initialize` + `ConfigureFromConfig`

## Agent 编排（仅 JSON）

改 `llm` 段后需 `UTAgentConfig.Reload()`（或重开 Settings / 重启 Editor）：

| 字段 | 默认 | 说明 |
|------|------|------|
| `afterToolTruncateChars` | `8000` | tool stdout 截断；`0`=关 |
| `noProgressEnabled` | `false` | 纯侦察空转注入；日常建议关 |
| `noProgressStreak` | `3` | 触发阈值 |

```json
{
  "llm": {
    "afterToolTruncateChars": 8000,
    "noProgressEnabled": false,
    "noProgressStreak": 3
  }
}
```

回归：截断 L2 C12；无进展 L2 C13（测时临时开 `noProgressEnabled`）。

## Chat 交互要点

- 运行中改指令：有内容 Enter →「待发送」；点发送或空 Enter → Abort 后续跑；Stop 急停并清队列
- **AGENTS.md**：项目根优先，否则包内 `AGENTS.md`；不自动加载 `CLAUDE.md`；进 system「Project Instructions」
- **Session**：`{Out}/sessions/{id}.jsonl`；Chat 可新建 / 打开 / 删除；默认续最近非空会话；审计为 `{Out}/logs/agent_yyyyMMdd.log`

## CLI

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "from unity.scene_view import get_hierarchy; print(get_hierarchy('Canvas', echo=False))"
```

详见 `Tools/utagent-cli/README.md`。

## 目录结构

```
Assets/UTAgent/
├── Editor/          C# Editor（Agent / Core / PythonInterop / RemoteCli / Config）
├── Runtime/         C# Runtime（Engine / Play）
├── Python/          agent / unity / unity_bind
├── Scripts/         业务 UI 面板 .py
├── Tools/           utagent-cli、ui-benchmark、bootstrap
├── Docs/            包内文档、skills、基准、examples
├── Config/          defaults + local（用户覆盖）
├── PythonHome/      嵌入式 CPython（gitignore）
├── Out/             运行产物（gitignore）：logs / screenshots / sessions / exec
└── agent-skills/    编码助手 skill 源
```

## 相关文档

- [`extension-points.md`](./extension-points.md) — Tools / Hooks / Skills 落点
- [`ui-assembly-benchmark.md`](./ui-assembly-benchmark.md) — 拼 UI 验收真源
- [`python-interop-bridge.md`](./python-interop-bridge.md) — Bridge / 互操作
- 仓库外路线图：`Docs/ut-agent/`（父工程）

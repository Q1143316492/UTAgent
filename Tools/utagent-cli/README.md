# UTAgent CLI

通过 localhost HTTP 调用运行中的 Unity Editor（UTAgent Remote CLI），供 Cursor 终端自主验证 Unity 改动。

CLI 随 **UTAgent 插件**分发，路径固定为 `Assets/UTAgent/Tools/utagent-cli/`。拷贝整个 `Assets/UTAgent` 到其他 Unity 项目即可使用。

## 前提

- Unity Editor 已打开本项目
- `Window/UT Agent/Settings` → **③ CLI** Tab：`bridge.enabled` 为 true（**defaults 默认开启**）；首次打开 **Agent Chat** 或点「保存 CLI 设置」后监听生效
- 环境变量 `UTAGENT_API_KEY` 已设置（`utagent chat` 需要；`exec` 不需要）
- 本机已安装 **Python 3**（或在 Settings → ① Python 选择目录）；`utagent init` 或 Chat 发消息可拉起引擎

## 快速开始

```powershell
# 项目根目录（相对路径）
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 init
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "import unity; unity.log('ok')"
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 log tail -n 40
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 scene find StartGameButton
```

## 命令

| 命令 | 说明 |
|------|------|
| `ping` | Editor / 引擎 / 域重载状态 |
| `init` | 初始化或恢复 Python 引擎 |
| `exec --code '...'` | 执行 Python（同 Chat execPython 底层） |
| `exec --file script.py` | 从文件执行 |
| `log tail [-n 80]` | 最新 agent 日志末尾 |
| `log errors [-n 200]` | 筛 Traceback / HTTP 400 / step |
| `scene find <name>` | 场景内同名对象计数 |
| `screenshot [--view scene\|game]` | PNG 落盘，打印 path（Cursor Read 看图） |
| `chat "..."` | 自然语言 ReAct（等同 Chat），默认阻塞到结束 |
| `chat --no-wait "..."` | 仅提交，返回 turn_id（选项写在消息前） |
| `chat wait <turn_id>` | 等待已有 turn |
| `chat --compact` | 等待时单行刷新进度 |
| `chat --timeout 600` | 阻塞超时秒数（默认 600） |

`chat` 须设置环境变量 `UTAGENT_API_KEY`；Bridge 侧会在 turn 开始时自动配置 Agent（与 Chat 发消息相同）。详见 `Docs/ut-agent/14-utagent-cli.md`。

## 环境变量

- `UTAGENT_PORT` — 覆盖默认端口 `17861`（与 `utagent.local.json` 中 `bridge.port` 一致）

## 退出码

| 码 | 含义 |
|----|------|
| 0 | 成功 |
| 1 | 连接 / HTTP 错误 |
| 2 | 引擎不可用（常因域重载，先 `init`） |
| 3 | Python 执行有 error |
| 4 | Chat 失败 / 超时 / 409 |

## 域重载恢复

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping    # invalidated: true
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 init
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping    # engine_available: true
```

域重载会停止 HTTP 监听；重新打开 **Agent Chat** 或 Settings → ③ CLI 点「保存 CLI 设置」可恢复监听。

## Cursor Skill

将 `Assets/UTAgent/ide-skills/utagent-unity-verify/` 复制到目标项目 `.cursor/skills/`。

详见 `Docs/ut-agent/14-utagent-cli.md`。

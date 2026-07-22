# UTAgent CLI

通过 localhost HTTP 调用运行中的 Unity Editor（UTAgent Remote CLI）。主能力是 **`exec`：在 Editor 内跑 Python** 操控场景/UI；Cursor 用多次 `exec` 编排。

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
# 短探测可用 --code；多行/纠偏请用 --file（推荐 Out/exec/）
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "import unity; unity.log('ok')"
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 log tail -n 40
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 scene find StartGameButton
```

### `exec`：优先 `--file`

| 场景 | 推荐 |
|------|------|
| 多行、含引号/`$`、UI/Layout 纠偏 | `exec --file Assets/UTAgent/Out/exec/….py` |
| 极短单行探测 | `exec --code '…'` |

临时脚本落点：`Assets/UTAgent/Out/exec/`（gitignore；首次可先建目录，或打开 Chat/Settings 触发 Ensure）。不要把该目录当正式库代码。

## 命令

| 命令 | 说明 |
|------|------|
| `ping` | Editor / 引擎 / 域重载状态 |
| `init` | 初始化或恢复 Python 引擎 |
| `exec --code '...'` | 极短单行探测（PowerShell 下易吃引号，慎用） |
| `exec --file script.py` | **推荐**：多行/纠偏从文件执行（落 `Out/exec/`） |
| `log tail [-n 80]` | 最新 agent 日志末尾（`Out/logs/`） |
| `log errors [-n 200]` | 筛 Traceback / HTTP 400 / step |
| `scene find <name>` | 场景内同名对象计数 |
| `screenshot [--view scene\|game] [--name UI名] [--padding N]` | PNG 落盘（默认 `Out/screenshots/`）；`--name` 按节点裁切 |
| `skill list [--json]` | 领域 skill 目录（仅 frontmatter + 绝对路径；离线；对标 Chat Available Skills） |
| `skill get <id> [--json]` | 输出 skill 全文（对标 `loadSkill`） |
| `chat "..."` | 自然语言 ReAct（等同 Chat），默认阻塞到结束 |
| `chat --no-wait "..."` | 仅提交，返回 turn_id（选项写在消息前） |
| `chat wait <turn_id>` | 等待已有 turn |
| `chat --compact` | 等待时单行刷新进度 |
| `chat --timeout 600` | 阻塞超时秒数（默认 600） |

`chat` 须设置环境变量 `UTAGENT_API_KEY`；Bridge 侧会在 turn 开始时自动配置 Agent（与 Chat 发消息相同）。详见 `Docs/ut-agent/14-utagent-cli.md`。

## skill 目录（离线，对标 Chat）

Chat：tool description 里 **Available Skills** = 扫 `Python/agent/skills/*.md.txt` 的 frontmatter `description` → 再 `loadSkill` 取全文。  
CLI：同真源、同渐进方式：

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 skill list --json
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 skill get editor-ui
```

- `list`：只读 frontmatter（`name` / `description` / 可选 `assert`），每项含 skill 文件**绝对路径**；可选 `assert` 亦为绝对路径（相对包根写在 skill 内）。
- `get`：全文；未知 id 非 0。
- **不需要** Editor 在线；路径相对 CLI 所在 UTAgent 包解析。
- 不要为每个域加 CLI 子命令；加域 = 加 `*.md.txt`。

## 环境变量

- `UTAGENT_PORT` — 覆盖默认端口 `17861`（与 `utagent.local.json` 中 `bridge.port` 一致）

## 执行策略（L1，与 Chat 共用）

`POST /exec` 在跑 Python **之前**调用 `UTAgentExecPolicy`（与 Chat before-exec 同源）：禁止 `os.walk` / `.rglob` / 递归 `glob`；单步过长、全量 `GetComponents(Component)` 亦拒。查找资源用 `AssetDatabase.FindAssets` / `LoadAssetAtPath`。

拒绝时响应示例字段：

| 字段 | 含义 |
|------|------|
| `ok` | `false` |
| `error` | 形如 `[exec-policy:fs-walk] …` 的说明 |
| `policy` | 策略域名：`fs-walk` / `code-too-long` / `heavy-reflection` |
| `engine_available` | `true`（引擎可用，是策略拒绝而非未 init） |

CLI 将此类失败按 **退出码 3** 处理（与 Python `error` 非空相同）。UI skill 门、layout-control **仅 Chat**，CLI 不因「未 loadSkill」拒绝。

**观测（不调 `CodeSizeLimit` 前）：** L1 拒绝写入 `Assets/UTAgent/Out/logs/exec_policy_yyyyMMdd.log`（含 `domain` / `chars` / `source=cli|chat`）。约一日后可汇总：

```powershell
Select-String -Path Assets/UTAgent/Out/logs/exec_policy_*.log -Pattern "domain=code-too-long" | Measure-Object
```

**勿**把 4000 直接抬到模型上下文量级；有数据再另开 change 评估（建议下一档仍远低于 100k）。

**UI health 交付：** `utagent exec --file Assets/UTAgent/Tools/ui-benchmark/run_assert_ui_scene_health.py`（薄入口）。

**交付 vs 评测：** 编码助手用 `exec` + 域 assert（如 UI health）做交付底线；日常 L2 chat 跑表才是 Agent 评测分。二者勿混。

## 退出码

| 码 | 含义 |
|----|------|
| 0 | 成功 |
| 1 | 连接 / HTTP 错误 |
| 2 | 引擎不可用（常因域重载，先 `init`） |
| 3 | Python 执行有 error，或 **L1 策略拒绝** |
| 4 | Chat 失败 / 超时 / 409 |

## 域重载恢复

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping    # invalidated: true
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 init
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping    # engine_available: true
```

域重载后执行 `init` 即可恢复（同 `PythonHome` / dll，**无需**重启 Unity Editor）。Settings → ① Python「域重载前关闭 Python」**默认关**（避免拖慢编译）；勾选后走轻量 Finalize。若仍报 Runtime.PythonDLL 锁定，请**重启 Unity Editor** 再 `init`。

域重载会停止 HTTP 监听；重新打开 **Agent Chat** 或 Settings → ③ CLI 点「保存 CLI 设置」可恢复监听。

## Cursor Skill

将 `Assets/UTAgent/agent-skills/utagent-unity-exec/` 复制到当前编码助手的 skills 目录（例如 Cursor：`.cursor/skills/`）。

详见 `Docs/ut-agent/14-utagent-cli.md`。

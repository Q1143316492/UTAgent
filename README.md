# UTAgent

Unity Editor 内 LLM Agent 插件（Python + `execPython` / `loadSkill`）。

## 安装

将本仓库放到目标 Unity 项目的 `Assets/UTAgent/`。

```bash
git clone git@github.com:Q1143316492/UTAgent.git Assets/UTAgent
```

或在父项目中使用 submodule：

```bash
git submodule add git@github.com:Q1143316492/UTAgent.git Assets/UTAgent
```

### 快速上手

1. 设置 API Key 环境变量（默认名 `UTAGENT_API_KEY`）：

```powershell
$env:UTAGENT_API_KEY = "sk-..."
```

若在 Windows「用户变量」中新增，需**完全退出并重启 Unity Editor** 后进程才会继承。

2. `Window/UT Agent/Settings` 按顶部三步引导配置（见下表）。
3. `Window/UT Agent/Agent Chat` 发消息；运行时自动初始化 Python 并配置 Agent，无需单独「应用」按钮。

### 配置（`Window/UT Agent/Settings`）

| Tab | 内容 |
|-----|------|
| ① Python | 选择一次 CPython 目录、「保存并初始化」或「重置引擎」 |
| ② 大模型 | Provider / Model（预制 DeepSeek V4 Flash / Pro）、Max Steps、API Key **环境变量名**（状态自动显示） |
| ③ CLI | Remote CLI 启用与端口（**默认开启**） |
| 日志 | 目录（默认 `Assets/UTAgent/LOG/`） |

- **配置文件**：`Config/utagent.defaults.json`（跟踪版本）+ `Config/utagent.local.json`（gitignore，用户覆盖）
- **API Key**：仅存环境变量，不写入 JSON；② 大模型 Tab 显示「已设置 / 未设置」
- **Python 路径解析**：json 已保存目录 → `PYTHONHOME` → `Assets/UTAgent/PythonHome/` → 平台探测
- **Editor 启动**：不自动迁移配置、不自动启 CLI；打开 Chat 时按 json 同步 CLI 监听
- **发消息**：自动 `Initialize` + `ConfigureFromConfig`（前提：API Key 与 Python 路径可用）

## CLI

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "from unity.scene_view import get_hierarchy; print(get_hierarchy('Canvas', echo=False))"
```

详见 `Tools/utagent-cli/README.md`。

## unity 模块

`unity` 按域拆 4 子模块：`unity.scene_view` / `unity.screenshot` / `unity.inspect` / `unity.console`。`unity.<verb>` 顶层路径仍可用（兼容层）。详见 `Python/agent/skills/python-interop.md.txt`。

## 目录结构

```
Assets/UTAgent/
├── Editor/          C# Editor 程序集（Agent / Core / PythonInterop / RemoteCli / PlayBinding / Config）
├── Runtime/         C# Runtime 程序集（Engine / Play）
├── Python/          Python 资源（agent / unity / unity_bind）
├── Scripts/         业务 UI 面板 .py
├── Tools/           utagent-cli、ui-benchmark
├── Docs/            包内文档与基准
├── Config/          utagent.defaults.json + utagent.local.json（用户覆盖）
├── PythonHome/      本机 CPython 嵌入（gitignore，可选）
├── LOG/             Agent 会话日志（gitignore，运行时生成）
└── ide-skills/      Cursor IDE skill（非 LLM loadSkill）
```

## Cursor Skill

复制 `ide-skills/utagent-unity-verify/` 到项目 `.cursor/skills/`。

## Docs / Benchmark

- `Docs/ui-assembly-benchmark.md` — UI 拼装验收基准（L0/L1/L2 用例表 + 结果列，唯一真源）
- `Tools/ui-benchmark/` — benchmark 脚本（`golden_path_*.py` L1、`parse_agent_log.py` log 解析、`run_benchmark.ps1` 一键回归）

改 UI 相关代码后，跑 `Tools/ui-benchmark/run_benchmark.ps1` 回归 UI 拼装能力。

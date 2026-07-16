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

日常只需配置 **API Key**（默认环境变量名 `UTAGENT_API_KEY`）：

```powershell
$env:UTAGENT_API_KEY = "sk-..."
```

若在 Windows「用户变量」中新增，需**完全退出并重启 Unity Editor** 后进程才会继承。

新机可选：

```powershell
./Assets/UTAgent/Tools/bootstrap/Install-PythonHome.ps1
./Assets/UTAgent/Tools/bootstrap/Install-IdeSkills.ps1
```

或在 `Window/UT Agent/Settings` → **Python** 点「下载并初始化」。详见 `Docs/skills/utagent-env-bootstrap/SKILL.md`。

然后 `Window/UT Agent/Agent Chat` 发消息即可（Python / CLI 按需自动就绪）。

### 配置（`Window/UT Agent/Settings`）

| Tab | 内容 |
|-----|------|
| 大模型 | **主要配置**：Provider / Model、Max Steps、API Key 环境变量名 |
| Python | 一行状态 + 主按钮（缺则下载并初始化）；路径/重置在「高级」 |
| CLI | Remote CLI 启用与端口（**默认开启**） |
| 日志 | 目录（默认 `Assets/UTAgent/LOG/`） |

- **配置文件**：`Config/utagent.defaults.json`（跟踪版本）+ `Config/utagent.local.json`（gitignore，用户覆盖）
- **API Key**：仅存环境变量，不写入 JSON
- **Python**：只认 `Assets/UTAgent/PythonHome/`（含 `python312.dll`）；忽略 json/`PYTHONHOME`/本机探测
- **Editor 启动**：不自动迁移配置、不自动启 CLI；打开 Chat 时按 json 同步 CLI 监听
- **发消息**：自动 `Initialize` + `ConfigureFromConfig`（前提：API Key 与 PythonHome 就绪）

> 注：曾规划的「更换/清除外部 Python 路径」UI（`fix-python-config-ux`）已被本方案取代，不再支持选本机 Programs\Python。

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
├── Tools/           utagent-cli、ui-benchmark、bootstrap
├── Docs/            包内文档、skills、基准
├── Config/          utagent.defaults.json + utagent.local.json（用户覆盖）
├── PythonHome/      嵌入式 CPython（gitignore；仅认此目录）
├── LOG/             Agent 会话日志（gitignore，运行时生成）
└── ide-skills/      Cursor IDE skill 源（用 Install-IdeSkills.ps1 复制）
```

## Cursor Skill

```powershell
./Assets/UTAgent/Tools/bootstrap/Install-IdeSkills.ps1
```

或手动复制 `ide-skills/utagent-unity-verify/` 到项目 `.cursor/skills/`。

环境初始化手册：`Docs/skills/utagent-env-bootstrap/SKILL.md`。

## Docs / Benchmark

- `Docs/ui-assembly-benchmark.md` — UI 拼装验收基准（L0/L1/L2 用例表 + 结果列，唯一真源）
- `Tools/ui-benchmark/` — benchmark 脚本（`golden_path_*.py` L1、`parse_agent_log.py` log 解析、`run_benchmark.ps1` 一键回归）

改 UI 相关代码后，跑 `Tools/ui-benchmark/run_benchmark.ps1` 回归 UI 拼装能力。

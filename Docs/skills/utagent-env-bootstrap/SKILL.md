---
name: utagent-env-bootstrap
description: >-
  初始化 UTAgent 本机环境：下载 embeddable CPython 到 PythonHome、
  复制 agent-skills 到当前编码助手的 skills 目录、API Key 与 Settings 初始化引擎。
  用于新克隆项目、新电脑、PythonHome 缺失、缺少 utagent-unity-exec 等场景。
---

# UTAgent 环境初始化

新电脑 / 新克隆后按本 skill 做一次即可。运行时 **只认** `Assets/UTAgent/PythonHome/`（不再用系统 AppData Python、也不再读 json 自定义 home）。

## 前置

- 工作区为含 `Assets/UTAgent` 的 Unity 项目根（如 `EqZeroUT2`）
- Windows + PowerShell（下载脚本）
- 网络可访问 `python.org`（或手动下 zip）

## 一键脚本（推荐）

在**项目根**执行：

```powershell
# 1) 下载官方 embeddable 3.12 → Assets/UTAgent/PythonHome/
./Assets/UTAgent/Tools/bootstrap/Install-PythonHome.ps1

# 2) 复制编码助手 skill（默认 → 工作区 .cursor/skills/）
./Assets/UTAgent/Tools/bootstrap/Install-IdeSkills.ps1
```

覆盖已有 skill：

```powershell
./Assets/UTAgent/Tools/bootstrap/Install-IdeSkills.ps1 -Force
```

其它工具可改目标目录，例如：

```powershell
./Assets/UTAgent/Tools/bootstrap/Install-IdeSkills.ps1 -DestRel ".claude/skills" -Force
```

## 手动：PythonHome

1. 打开：https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip  
2. 解压到 `Assets/UTAgent/PythonHome/`（目录内应有 `python312.dll`）  
3. 勿拷完整 Anaconda / 带大量 site-packages 的安装树  

## 手动：编码助手 skills

把 `Assets/UTAgent/agent-skills/` 下每个子目录（如 `utagent-unity-exec`）复制到当前工具约定的 skills 目录，例如：

```
<工作区根>/.cursor/skills/<skill名>/     # Cursor
# Claude Code / 其它：按其文档放置
```

## API Key

```powershell
$env:UTAGENT_API_KEY = "sk-..."
```

若写入 Windows「用户环境变量」，需**完全退出并重启 Unity**。

## Unity Settings

日常只需 **大模型** Tab 配 API Key。Python / CLI 默认可用。

- `Window/UT Agent/Settings` 默认打开 **大模型**
- **Python**：一行状态 +「下载并初始化」/「重新初始化」；路径在「高级」
- **CLI**：默认开启

## 验收

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping
# 若 engine_available=false：
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 init
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping
```

期望：`engine_available: True`。

## 相关路径

| 路径 | 用途 |
|------|------|
| `Assets/UTAgent/PythonHome/` | 嵌入式 CPython（gitignore） |
| `Assets/UTAgent/agent-skills/` | 编码助手 skill 源（非 Editor loadSkill） |
| `Assets/UTAgent/Docs/skills/utagent-env-bootstrap/` | 本 skill |
| `Assets/UTAgent/Tools/bootstrap/` | 安装脚本 |

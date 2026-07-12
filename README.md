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

## CLI

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping
```

详见 `Tools/utagent-cli/README.md`。

## Cursor Skill

复制 `cursor-skills/utagent-unity-verify/` 到项目 `.cursor/skills/`。

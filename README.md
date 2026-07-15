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
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "from unity.scene_view import get_hierarchy; print(get_hierarchy('Canvas', echo=False))"
```

详见 `Tools/utagent-cli/README.md`。

## unity 模块

`unity` 按域拆 4 子模块：`unity.scene_view` / `unity.screenshot` / `unity.inspect` / `unity.console`。`unity.<verb>` 顶层路径仍可用（兼容层）。详见 `Runtime/agent/skills/python-interop.md.txt`。

## Cursor Skill

复制 `cursor-skills/utagent-unity-verify/` 到项目 `.cursor/skills/`。

## Docs / Benchmark

- `Docs/ui-assembly-benchmark.md` — UI 拼装验收基准（L0/L1/L2 用例表 + 结果列，唯一真源）
- `Tools/ui-benchmark/` — benchmark 脚本（`golden_path_*.py` L1、`parse_agent_log.py` log 解析、`run_benchmark.ps1` 一键回归）

改 UI 相关代码后，跑 `Tools/ui-benchmark/run_benchmark.ps1` 回归 UI 拼装能力。

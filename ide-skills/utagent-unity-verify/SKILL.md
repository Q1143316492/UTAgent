---
name: utagent-unity-verify
description: >-
  通过 utagent CLI 自主验证 Unity Editor 改动（ping、init、exec、screenshot、log），无需用户手测。
  用于 Unity 验证、utagent、execPython 验收、场景/UI 改动、按钮 Canvas 预制体等任务。
---

# UTAgent Unity 自主验证

改完 Unity / UTAgent 相关代码后，**必须**在终端用 CLI 闭环验收，**不要**让用户「去 Unity 试一下」。

对标 Puerts MCP：Cursor 本会话做编排；CLI 只做**单次**控 Unity（多次 `exec` 组合），**不要**靠 `utagent chat` 在 Unity 里跑长 ReAct。

## CLI 路径

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 <子命令>
```

拷贝 `Assets/UTAgent` 到其他项目时，路径不变。

## 标准流程（主路径）

```
1. utagent ping
2. 若 engine_available=false → utagent init → 再 ping
3. 用一次或多次单步调用验收（Cursor 自己编排）：
   - utagent exec --code '...'   # = Chat 内 execPython
   - utagent screenshot          # PNG 落盘 → Cursor Read 看图
   - utagent log tail / log errors
4. 失败则自行修复，最多重试 3 轮
```

## 命令用途

| 命令 | 用途 |
|------|------|
| `exec --code` / `--file` | **主路径**：单次 Python，无 LLM |
| `screenshot` | 目检：PNG 落盘；**Cursor Read 看图** |
| `log tail` / `errors` | 诊断 |
| `scene find` | 按名查对象（糖） |
| `chat "..."` | **旁路**：Editor DeepSeek ReAct；**不是** Cursor 验收默认步骤 |

## 高频 exec 配方

```powershell
# 按名计数
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "import unity; r=unity.find_objects('StartGameButton', echo=False); print(r)"

# 打日志确认引擎
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "import unity; unity.log('ok')"

# 截图后 Read path
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 screenshot --view scene
```

需要更复杂的场景/组件操作时，在一段 `exec` 里组合 `unity.*` / `CS.*`（见 `python-interop` skill），**不必**为每个动词加 CLI 子命令。

## 看图

- Editor Chat / `utagent chat`（DeepSeek 等）→ **不能看图**
- Cursor 本会话 → `screenshot` 得 path → Read

## 域重载 / 连接

- `invalidated: true` → `utagent init`
- Editor 已开；Settings → ③ CLI 或打开 Agent Chat；端口默认 17861

## 禁止

- ❌ 「请你在 Unity 里点一下」
- ❌ 让用户贴 Console（用 log）
- ❌ 跳过 ping 直接 exec
- ❌ 用 `chat` 当 Cursor 验收主路径，或对 DeepSeek 问「截图里有什么」
- ❌ 因缺少 chat abort 而阻塞验收

## 示例

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 init
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "import unity; r=unity.find_objects('StartGameButton', echo=False); print(r['count'])"
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 screenshot --view scene
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 scene find StartGameButton
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 log errors
```

参考：`Docs/ut-agent/14-utagent-cli.md`、`Docs/ut-agent/18-next-plan-cli-and-surface.md`

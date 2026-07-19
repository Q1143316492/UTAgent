---
name: utagent-unity-exec
description: >-
  用 utagent CLI 的 exec 在 Unity Editor 里跑 Python（unity.* / CS.*），由本会话编排多次单步调用。
  用于操控场景与 UI、查对象、截图、读日志；触发词：utagent、exec、execPython、unity 操作、CLI。
---

# UTAgent：用 exec 操控 Unity

核心能力：**`utagent exec` = Editor 内单次 Python**（等价 Agent Chat 的 `execPython`），无 LLM。  
**本会话（任意编码助手）负责编排**；需要多步时就多次 `exec`，不要用 `utagent chat` 当默认路径。

对标 Puerts MCP 的 `evalJsCode`：一个「跑一段代码」入口 + 包内 `unity`/`CS` 动词。

适用于能跑终端命令的编码助手（Cursor、Copilot、Claude Code、Codex 等）。

## CLI 路径

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 <子命令>
# 或
python ./Assets/UTAgent/Tools/utagent-cli/utagent.py <子命令>
```

拷贝 `Assets/UTAgent` 到其他项目时，路径不变。

## 标准流程

```
1. utagent ping
2. 若 engine_available=false → utagent init → 再 ping
3. 一次或多次：
   - utagent exec --code '...'   # 主路径：跑 Python 操控 Unity
   - utagent exec --file path.py
   - utagent screenshot          # 可选：PNG 落盘 → 本会话读图
   - utagent log tail / errors   # 可选：诊断
4. 失败则修代码/再 exec，最多重试 3 轮；不要让用户去 Editor 里点试
```

## 命令

| 命令 | 用途 |
|------|------|
| `exec --code` / `--file` | **主路径**：单次 Python，操作/查询场景与 UI |
| `screenshot` | 目检辅助：PNG 落盘后由本会话读图 |
| `log tail` / `errors` | 诊断 |
| `scene find` | 按名查对象（糖，多数情况 `exec` + `find_objects` 即可） |
| `chat "..."` | **旁路**：Editor 内 DeepSeek ReAct；不是本会话默认编排手段 |

## 高频 exec 配方

```powershell
# 按名计数
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "import unity; r=unity.find_objects('StartGameButton', echo=False); print(r)"

# 确认引擎
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "import unity; unity.log('ok')"

# 截图后根据输出 path 读图
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 screenshot --view scene
```

更复杂的创建/改组件：在一段 `exec` 里组合 `unity.*` / `CS.*`（Editor Agent 侧可 `loadSkill(python-interop)` / `editor-ui`），**不必**为每个动词加 CLI 子命令。

## 看图

- Editor Chat / `utagent chat` → **不能**替本会话看图  
- 本会话：`screenshot` → path → 读图文件

## 连接

- `invalidated: true` → `utagent init`
- Editor 已开；Settings → ③ CLI 或打开 Agent Chat；端口默认 17861

## 禁止

- ❌ 默认让用户「去 Unity 点一下」代替 `exec`
- ❌ 让用户贴 Console（用 `log`）
- ❌ 跳过 ping 直接 exec
- ❌ 用 `chat` 当本会话主编排，或对 Editor LLM 问「截图里有什么」

## 示例

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "import unity; r=unity.find_objects('BtnStart', echo=False); print(r['count'])"
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 screenshot --view scene
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 log errors
```

包内相关：`Tools/utagent-cli/`、`Docs/extension-points.md`（编码助手 skill vs Editor `loadSkill`）。

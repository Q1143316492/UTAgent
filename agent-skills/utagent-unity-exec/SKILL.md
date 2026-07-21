---
name: utagent-unity-exec
description: >-
  用 utagent CLI 的 exec 在 Unity Editor 里跑 Python（unity.* / CS.*），由本会话编排多次单步调用。
  用于操控场景与 UI、查对象、截图、读日志；触发词：utagent、exec、execPython、unity 操作、CLI。
---

# UTAgent：用 exec 操控 Unity

核心能力：**`utagent exec` = Editor 内单次 Python**（等价 Agent Chat 的 `execPython` 底层），无本会话 LLM。  
**本会话（任意编码助手：Cursor / Claude Code / Copilot / Codex 等）负责编排**；需要多步时就多次 `exec`，不要用 `utagent chat` 当默认路径。

对标 Puerts MCP 的 `evalJsCode`：一个「跑一段代码」入口 + 包内 `unity`/`CS` 动词。

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
   - utagent exec --file Out/exec/….py   # 推荐：多行/纠偏（PowerShell 勿用长 --code）
   - utagent exec --code '...'           # 仅极短单行探测
   - utagent screenshot                  # 可选：PNG → Out/screenshots/ → 本会话读图
   - utagent log tail / errors           # 可选：诊断（Out/logs/）
4. 失败则修脚本/再 exec，最多重试 3 轮；不要让用户去 Editor 里点试
```

临时脚本推荐目录：`Assets/UTAgent/Out/exec/`（gitignore）。不存在时先建目录，或触发一次 Chat/Settings Ensure。

### 拼 / 改 Canvas UI（Domain Pack: ui）

任务含新建或显著改动 `Wnd*` / Canvas 布局时，在标准流程上 **必须**：

```
1. 发现配方（对标 Chat Available Skills → loadSkill）：
   utagent skill list --json
   → 按 description 选 editor-ui（或已知 id）
   utagent skill get editor-ui
   → 或 Read list 返回的绝对 path（同一文件；勿抄整页用例答案进会话常驻）
2. 多次 exec --file 拼装（照 skill 规则写 Python，脚本放 Out/exec/）
3. 交付门禁：若 list/get 含 assert 绝对路径，则
   utagent exec --file <assert 绝对路径>
   （无 assert 字段时回退：Tools/ui-benchmark/assert_ui_scene_health.py）
4. health FAIL → 再 exec --file 纠偏，最多再 3 轮；仍失败则如实报告
5. 按需 screenshot → 本会话识图做审美微调（仍用 exec，不为此改走 chat）
```

- **交付过 health ≠ L2 用例 PASS**（L2 只评 Editor Chat 跑表）
- **禁止** 为拼 UI 交付默认调用 `utagent chat`

## 命令

| 命令 | 用途 |
|------|------|
| `exec --file` / `--code` | **主路径**：单次 Python；多步纠偏用 `--file`（`Out/exec/`） |
| `skill list` / `skill get` | 领域配方目录与全文（绝对路径；拼 UI 前先用） |
| `screenshot` | 目检辅助：PNG 落盘后由本会话读图 |
| `log tail` / `errors` | 诊断 |
| `scene find` | 按名查对象（糖，多数情况 `exec` + `find_objects` 即可） |
| `chat "..."` | **旁路**：Editor 内 DeepSeek ReAct；仅测 Agent / 跑 benchmark 时用 |

## 执行策略（L1，Editor 硬门禁）

CLI `exec` 与 Chat 共用策略：禁止 `os.walk` / `.rglob` / 递归 `glob`；过长单步与全量 `GetComponents(Component)` 也会被拒。查找资源用 `AssetDatabase.FindAssets` / `LoadAssetAtPath`。拒绝时 JSON 含 `ok:false`、`error`（前缀 `[exec-policy:…]`），可能含 `policy` 字段；CLI 退出码 **3**。

## 高频 exec 配方

```powershell
# 按名计数
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "import unity; r=unity.find_objects('StartGameButton', echo=False); print(r)"

# 确认引擎
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "import unity; unity.log('ok')"

# 截图后根据输出 path 读图
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 screenshot --view scene
```

更复杂的创建/改组件：在一段 `exec` 里组合 `unity.*` / `CS.*`，**不必**为每个动词加 CLI 子命令。

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
- ❌ 拼 UI 交付跳过 health，或把 CLI 成功写成 L2 PASS

## 示例

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "import unity; r=unity.find_objects('BtnStart', echo=False); print(r['count'])"
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 screenshot --view scene
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 log errors
```

包内相关：`Tools/utagent-cli/`、`Docs/extension-points.md`（L0–L3 / Domain Pack）。

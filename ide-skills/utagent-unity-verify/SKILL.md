---
name: utagent-unity-verify
description: >-
  通过 utagent CLI 自主验证 Unity Editor 改动（ping、init、exec、chat、log），无需用户手测。
  用于 Unity 验证、utagent、execPython 验收、自然语言调试、场景/UI 改动、按钮 Canvas 预制体等任务。
---

# UTAgent Unity 自主验证

改完 Unity / UTAgent 相关代码后，**必须**在终端用 CLI 闭环验收，**不要**让用户「去 Unity 试一下」。

## CLI 路径

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 <子命令>
```

拷贝 `Assets/UTAgent` 到其他项目时，路径不变。

## 标准流程

```
1. utagent ping
2. 若 engine_available=false → utagent init → 再 ping
3. 验收方式二选一：
   - utagent exec --code '...'   # 单步 Python，参考 golden-paths
   - utagent chat "自然语言任务"  # 完整 ReAct（等同 Chat 发话），默认阻塞到结束
4. utagent log tail            # 或 log errors
5. 失败则自行修复，最多重试 3 轮
```

## chat vs exec

| 命令 | 用途 |
|------|------|
| `chat "..."` | 调试：走 LLM ReAct 多步，等同 UT Agent Chat |
| `exec --code` | 验收：单步 Python，无 LLM |

`chat` 须设置环境变量 `UTAGENT_API_KEY`（与 Chat 相同）。同时只能有一个 running turn（409）。

## 域重载

Unity 脚本编译会触发域重载，`ping` 会显示 `invalidated: true`。运行 `utagent init` 恢复，**不要**假设一次 ping 永久有效。

## 连接失败

`ping` 连接被拒绝时：

- 确认 Unity Editor 已打开本项目
- `Window/UT Agent/Settings` → **③ CLI** 已启用；或打开 **Agent Chat** 触发按 json 同步监听
- 不要转而请用户手测；先排查 Editor / 端口（默认 17861）

## exec 注意

- CLI `exec` 与 Chat `execPython` 共用 `UTAgentBootstrap.Exec`，但**无** LLM 跨步 `_exec_globals`
- 多步逻辑写在一个 `--code` 或 `--file` 里
- 验收优先 `unity.find_objects(name)["count"]`、 `unity.log()`，DeepSeek 等纯文本模型**不要**依赖截图

## 禁止

- ❌ 「请你在 Unity 里点一下看看」作为首选验收
- ❌ 让用户复制 Console 贴回来（用 `log tail` / `log errors`）
- ❌ 跳过 ping 直接 exec（可能撞上域重载后的 503）

## 示例

```powershell
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 ping
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 init
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 exec --code "import unity; r=unity.find_objects('StartGameButton', echo=False); print(r['count'])"
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 chat "场景里查找 StartGameButton" --compact
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 scene find StartGameButton
./Assets/UTAgent/Tools/utagent-cli/utagent.ps1 log errors
```

参考：`Docs/ut-agent/12-unity-bind-golden-paths.md`、`Docs/ut-agent/14-utagent-cli.md`

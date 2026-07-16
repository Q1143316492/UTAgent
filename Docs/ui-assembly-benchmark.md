# UI 拼装验收基准

> **本文件是 UI 拼装 L0/L1/L2 验收用例表的唯一真源。**
> 各 change 的 `verification.md` MUST NOT 重新定义用例 ID，仅引用本表表项。
> 改 UI 相关代码后，跑 `Tools/ui-benchmark/run_benchmark.ps1` 回归。
> 项目级路线图见 `Docs/ut-agent/16-scene-inspection-and-cursor-workflow.md`（§七为索引，结果不重复）。

## 验收三层

```
L0 契约    skill 体积、模板可解析、scripts 与 skill 同步
    ↓
L1 结构    utagent exec 断言场景 JSON（不经过 LLM）
    ↓
L2 行为    utagent chat + parse_agent_log.py 断言 tool 序列（经过 LLM）
```

**禁止**仅用 `utagent exec --file` 宣称「skill 加固完成」。归档 skill/编排类 change **至少** L1 + L2 过表内对应用例。

---

## L0 — Skill 契约

| ID | 检查 | 结果 | 日期 | change |
|----|------|------|------|--------|
| S0 | `editor-ui`：rules ≤4k；rules+recipes ≤20k（200k 模型）；整页组合不得内嵌，放 doc 15 | ✅ 13835 字符 | 2026-07-17 | editor-ui-layout-hardening |
| S1 | `create_*` / `describe_go` 与 `Tools/ui-benchmark/golden_path_*.py` 同步 | ✅ golden_path_settings_form 同步 | 2026-07-15 | editor-ui-layout-primitives |
| S2 | frontmatter `name` / `description` 可被目录列出 | ✅ | 2026-07-14 | editor-ui-slim-templates |

---

## L1 — 结构（`utagent exec --file`，固定断言脚本）

脚本存放于 `Assets/UTAgent/Tools/ui-benchmark/`，末行 `print(json.dumps(...))` 含可断言字段。

| ID | 断言要点 | 脚本 | 结果 | 日期 | change |
|----|----------|------|------|------|--------|
| E01 | TMP 按钮：`Btn*/TxtLabel` + Button/TMP 分层 | golden_path_tmp_button | ✅ | 2026-07-14 | editor-ui-slim-templates |
| E02 | 面板：`Wnd*/GrpBody` + VLG + `TxtTitle` + `BtnSubmit`；`colors` 三档 | golden_path_layout_panel | ✅ | 2026-07-14 | editor-ui-slim-templates |
| E03 | 幂等：同脚本 exec 两次 `count==1` | （同 E02） | ✅ | 2026-07-14 | editor-ui-slim-templates |
| E04 | 命名：无 `*Go`、无裸 `Button` | （E01/E02 断言） | ✅ | 2026-07-14 | editor-ui-slim-templates |
| E05 | debug：`describe_go` JSON 含 `rect` 或 `interactable` 字段 | golden_path_describe_go | ✅ | 2026-07-13 | align-puerts-interop-editor |
| E06 | `add_to_layout`：挂子后 `parent==GrpBody` 且子 `anchorMin` 未被原语改 | （inline 测试） | ✅ pre=post=[0.5,0.5] | 2026-07-15 | editor-ui-layout-primitives |
| E07 | `add_free_child`：`anchorMin==(1,1)` 且 `offsetMin==(-120,-48)` | （inline 测试） | ✅ | 2026-07-15 | editor-ui-layout-primitives |
| E08 | `golden_path_settings_form.py`：`row_count>=2`、`button_count==2`、`has_vlg==True`；幂等 `count==1` | golden_path_settings_form | ✅ row=2 btn=2 vlg=true | 2026-07-15 | editor-ui-layout-primitives |
| E09 | `convert_to_llm`：3 条 `reminder` 输入 → 输出仅 1 条（最近） | assert_convert_to_llm_e09 | ✅ | 2026-07-16 | agent-message-layering |
| E10 | `loadSkill` / `emit_progress` 不进 `_history` | assert_history_no_progress_e10 | ✅ | 2026-07-16 | agent-message-layering |
| E11 | `apply_compaction_summary` → `kind=compaction`；`convert_to_llm` 保留；超预算 `needs_compaction` + 摘要 prompt 含任务锚点 | assert_compaction_e11 | ✅ | 2026-07-17 | agent-llm-compaction |
| E12 | Layout 零宽：`Wnd*`+`Input*` 后 `Input*`/`Btn*` 的 `rect.w/h` 均 >1 | assert_layout_zero_width_e12 | ✅ | 2026-07-17 | editor-ui-layout-hardening |

---

## L2 — 行为（`utagent chat` + `parse_agent_log.py` 断言）

每条含 prompt、必达 log 断言（`loadSkill`/`execSteps`/`beforeExecDecisions`）、步数预算。步数给区间（L2 chat 非确定）。

| ID | Prompt | 必达（log） | 步数预算 | 结果 | 日期 | change |
|----|--------|-------------|----------|------|------|--------|
| C01 | Canvas 下 TMP 按钮 Start | `loadSkill: editor-ui ok`；exec ≤3 步 | ≤3 | ✅ | 2026-07-15 | agent-loadskill-hardening |
| C02 | WndSettings 标题+音乐/音效 row+保存/取消 | loadSkill ok；布局未炸；count=1 | ≤12（守卫拆步） | ✅ 12 步；体积守卫触发拆步 | 2026-07-15 | editor-ui-layout-primitives |
| C03 | 某按钮点不了 | `loadSkill: editor-ui-debug ok`；含 `describe_go` | ≤6 | ✅ | 2026-07-15 | agent-skill-before-exec |
| C04 | 创建一个 Cube（对照） | **不得** load editor-ui | ≤3 | ✅ | 2026-07-15 | agent-loadskill-hardening |
| C05 | 与 Puerts 同 prompt（可选） | 手填步数；UT ≤ Puerts+1 | — | ⏳ 未跑 | — | — |
| C06 | 诱导跳过 loadSkill 直接拼 UI（`AddComponent Image`+`Canvas`+`Btn*`） | before-exec `skill=missing → inject reminder`；下一轮 `skill=loaded → allow` | ≤8 | ✅ | 2026-07-15 | agent-skill-before-exec |
| C07 | 改已有 UI（把保存按钮颜色改红） | 不 `prepare_scene_object` 删根；外科式 `GetComponent(Image).color`；子树完整 | ≤3 | ✅ 2 步 | 2026-07-15 | editor-ui-layout-primitives |
| C08 | 诱导长脚本（一次性写超长脚本检查所有组件所有属性） | before-exec `code-too-long → inject reminder`（>4000 字符）；拆小步 | — | ✅ 由 C02 触发验证（8688 chars） | 2026-07-15 | editor-ui-layout-primitives |
| C09 | 诱导全量反射（`GetComponents(CS.UnityEngine.Component)`） | before-exec `heavy-reflection → inject reminder`；改用 describe_go/指定类型 | — | ✅ | 2026-07-15 | editor-ui-layout-primitives |
| C10 | 同 C02 复杂 UI（反复守卫拆步） | `llm-prepare` 各行 `reminder_in_llm ≤ 1`（可观测时） | ≤12 | ✅ max=1；history 最高 6 | 2026-07-16 | agent-message-layering |

**模板贴合**（L2 加分，不作唯一标准）：exec 代码含 `create_tmp_button` / `create_layout_panel` / `add_to_layout` / `add_free_child` / `describe_go`。

---

## log 格式契约（解析锚定）

`parse_agent_log.py` 锚定以下 ASCII 事件 token（MUST 不本地化，改动 MUST 同步解析器）：

| 事件 | 行格式 | 解析产出 |
|------|--------|----------|
| 回合开始 | `[HH:mm:ss] TURN BEGIN [<id>]` | `turns[]` |
| 步分隔 | `--- step <N> ---` | `exec_steps` 计数 |
| before-exec | `[HH:mm:ss] before-exec` + 缩行 `<domain>, skill=<state> → <action>` | `before_exec_decisions[]` |
| execPython | `[HH:mm:ss] tool_call` + code 块 | `exec_steps` |
| loadSkill | `[HH:mm:ss] status: loadSkill: <name> <ok\|fail>` | `loadSkill_calls[]` |
| llm-prepare | `[HH:mm:ss] llm-prepare reminder_in_history=N reminder_in_llm=M` | `llm_prepare_stats[]` |
| 回合结束 | `[HH:mm:ss] TURN END <outcome>` | `turns[]` 闭合 |

domain ∈ {non-ui, ui-domain, debug-domain, code-too-long, heavy-reflection}。

格式不符时解析器填 `parse_warnings` 不崩溃（见 `python-llm-agent` spec「log 可观测事件格式契约」）。

---

## 验收通则

```text
1. utagent ping → invalidated → utagent init
2. 改 C# 后 Unity 编译 → init
3. 归档 skill change：L0 + 对应 L1 + 至少 1 条 L2 通过
4. 回归：align CS.* 子集 + E05
5. 改 log 格式 MUST 重跑 parse_agent_log.py，parse_warnings 须空
```

## 已归档 change 索引

| change | 归档 | 贡献用例 |
|--------|------|----------|
| `align-puerts-interop-editor` | `archive/2026-07-13-*` | L3 CS + debug exec（E05） |
| `python-interop-edit-mode` | `archive/2026-07-14-*` | L1 销毁/inactive |
| `editor-ui-slim-templates` | `archive/2026-07-14-*` | L0 S0/S1 + L1 E01–E03 |
| `agent-loadskill-hardening` | `archive/2026-07-15-*` | L0 + L2 C01/C02/C04 |
| `agent-skill-before-exec` | `archive/2026-07-15-*` | L0 + L2 C01/C03/C04/C06 |
| `editor-ui-layout-primitives` | `archive/2026-07-14-*` | L0 9274 + L1 E06–E08 + L2 C02/C07/C08/C09 |
| `agent-message-layering` | `archive/2026-07-16-*` | L1 E09/E10 + L2 C02/C07/C10 + `messages.py` / `convert_to_llm` |

# UI 拼装验收基准

> **本文件是 UI 拼装 L0/L1/L2 验收用例表与结果列的真源。**  
> Agent 评测导读：`Docs/ut-agent/21-agent-core-loop-and-eval.md`。  
> 跑表入口与加测约定：`Assets/UTAgent/Tools/ui-benchmark/README.md` + `suite_map.json`。  
> **没有全量流程。** `-FullDev` 已废除。

## 怎么跑（两档）

| 档 | 命令 | 内容 |
|----|------|------|
| **日常 L1** | `./run_benchmark.ps1` | 门禁冒烟：**E16+E17**（无面板 golden） |
| **日常 L2** | `./run_benchmark.ps1 -L2Only` | **C02+C14+C15**（chat → health；FAIL 则打回 AI 再扫 → export） |
| **日常合计** | `./run_benchmark.ps1 -L2` | 上述 L1 + L2 |
| **按需** | `-Cases …` | 显式 ID（如钩子 C11） |

**正式拼 UI = L2 chat。** 预写整页 golden（E08/E14/E15）已归档，禁止加回日常。  
**审阅预制体：** `Assets/UTAgent/TestFixtures/UIPanels/`（WndSettings / WndLogin / WndCharacter）；由 L2 **最终** health PASS 后 export（非 L1 golden）。

**加测：** 整页拼 UI 只加 L2；门禁/钩子可加 L1。临时验证放 `opt-in/`，测通且不再需要 → **删脚本**，本表标 **已删**。

**门禁：** `assert_ui_scene_health`（近零 rect；Layout 下须 childControl + preferred）。health FAIL 时 harness **打回 Agent 限次纠偏**（见 `format_health_remediation_prompt.py` / `-RemediationMax`），不得停在「无 export」。  
**Fixtures：** `Assets/UTAgent/TestFixtures/UIPanels/`；导出请求在 `.tmp/`。

## 验收三层

```
L0 契约 → L1 门禁/钩子 → L2 chat → health →（FAIL→打回AI）→ export
```

---

## L0 — Skill 契约

| ID | 档 | 检查 | 结果 | 日期 | change |
|----|----|------|------|------|--------|
| S0 | 按需 | `editor-ui` 体积/不内嵌整页 | ✅ | 2026-07-17 | editor-ui-layout-hardening |
| S1 | 按需 | create_* 与 golden 同步 | ✅ | 2026-07-15 | editor-ui-layout-primitives |
| S2 | 按需 | frontmatter 可列出 | ✅ | 2026-07-14 | editor-ui-slim-templates |

---

## L1 — 门禁 / 钩子（`utagent exec`；非整页拼 UI 答案）

路径相对 `Tools/ui-benchmark/`（含 `opt-in/`、`archive/`）。

| ID | 档 | 断言要点 | 脚本 | 结果 | 日期 | change |
|----|----|----------|------|------|------|--------|
| E16 | **日常** | 非 ASCII 名 FAIL；文案中文 OK | `assert_ui_naming_smoke.py` | ✅ | 2026-07-20 | ui-l2-chat-only-acceptance |
| E17 | **日常** | 缺 preferred FAIL；声明 preferred 的小树 PASS | `assert_ui_layout_size_smoke.py` | ✅ | 2026-07-20 | ui-l2-chat-only-acceptance |
| E01 | 按需 | TMP 按钮原语 | `opt-in/golden_path_tmp_button.py` | ✅ | 2026-07-14 | — |
| E02 | 按需 | 布局面板原语 | `opt-in/golden_path_layout_panel.py` | ✅ | 2026-07-14 | — |
| E09 | 按需 | convert_to_llm reminder | `opt-in/assert_convert_to_llm_e09.py` | ✅ | 2026-07-16 | — |
| E10 | 按需 | progress 不进 history | `opt-in/assert_history_no_progress_e10.py` | ✅ | 2026-07-16 | — |
| E11 | 按需 | compaction | `opt-in/assert_compaction_e11.py` | ✅ | 2026-07-17 | — |
| E12 | 按需 | Layout 零宽 | `opt-in/assert_layout_zero_width_e12.py` | ✅ | 2026-07-17 | — |
| E08 | **已归档** | 设置面板预写拼装（作弊，勿回日常） | `archive/golden_path_settings_form.py` | ✅ 归档 | 2026-07-20 | ui-l2-chat-only-acceptance |
| E14 | **已归档** | 登录面板预写拼装（作弊，勿回日常） | `archive/golden_path_login_form.py` | ✅ 归档 | 2026-07-20 | ui-l2-chat-only-acceptance |
| E15 | **已归档** | 角色面板预写拼装（作弊，勿回日常） | `archive/golden_path_character_panel.py` | ✅ 归档 | 2026-07-20 | ui-l2-chat-only-acceptance |
| E03 | 已删 | 幂等 count（并入面板脚本） | — | — | — | 流程瘦身 |
| E04 | 已删 | 命名断言（由 E16 覆盖） | — | — | — | 流程瘦身 |
| E05 | 已删 | describe_go（无脚本） | — | — | — | 流程瘦身 |
| E06 | 已删 | add_to_layout inline | — | — | — | 流程瘦身 |
| E07 | 已删 | add_free_child inline | — | — | — | 流程瘦身 |
| E13 | 按需 | health 全量扫描入口 | `assert_ui_scene_health.py` | ✅ | 2026-07-19 | 挂在 L2/导出 |

---

## L2 — 行为（`utagent chat`）

| ID | 档 | Prompt 摘要 | 必达 | 结果 | 日期 |
|----|----|-------------|------|------|------|
| C02 | **日常** | 设置面板 WndSettings | loadSkill；health；export | ✅ | 2026-07-20 |
| C14 | **日常** | 登录面板 WndLogin | loadSkill；health；export | ✅ | 2026-07-20 |
| C15 | **日常** | 角色面板 WndCharacter | loadSkill；health；export | ⚠️ chat ✅；health 有 Txt* 近零高（LLM 布局，非 golden） | 2026-07-20 |
| C01 | 按需 | TMP 按钮 | loadSkill | ✅ | 2026-07-19 |
| C03 | 按需 | debug | editor-ui-debug | ✅ | — |
| C04 | 按需 | Cube 对照 | 不得 load editor-ui | ✅ | — |
| C06 | 按需 | 跳过 loadSkill | before-exec inject | ✅ | — |
| C07 | 按需 | 改按钮颜色 | 外科式改 | ✅ | — |
| C08 | 按需 | 超长脚本 | code-too-long（可由 C02 触发） | ✅ | — |
| C09 | 按需 | 全量反射 | heavy-reflection | ✅ | — |
| C10 | 按需 | reminder≤1 | llm-prepare | ✅ | — |
| C11 | 按需 | layout-control | inject | ✅ | — |
| C12 | 按需 | truncate | after-tool | ✅ | — |
| C13 | 按需 | no-progress | after-tool | ✅ | — |
| C16 | 按需 | 诱导 os.walk 扫 Assets | before-exec `fs-walk` inject；离线 `opt-in/assert_fs_walk_regex.py` | ✅ 离线 | 2026-07-20 |
| C05 | 已删 | Puerts 对照（未跑） | — | — | — |

---

## log 格式契约

`parse_agent_log.py` 锚定 ASCII token（`before-exec` / `after-tool` / `loadSkill:` / `TURN BEGIN` 等）。改格式须重跑解析器。

## 已删 / 勿回潮

- `Tools/ui-benchmark/cases/case01–03`：早期 interop 探测，**已删文件**（不是经验归档）。
- 面板 `golden_path_*`（E08/E14/E15）：已移 `archive/`；**禁止**加回 `daily_l1` 当拼 UI 答案。
- 不要把 opt-in 当成永久博物馆；测完无用就删。

## 稳定性备注（2026-07-20）

- L2 C02 首轮曾触发 Editor **exec 期 SIGSEGV**（非关编辑器路径）。已单独立项：`openspec/changes/utagent-exec-native-crash-guard`（与 `utagent-python-shutdown-safe` 正交）。
- 断言曾与 prompt/skill 不对齐（设置强求 Toggle+Slider；Input 在 `forceExpandWidth` 下仍索 preferredWidth；登录强求 Row*；用例间未清其它 `Wnd*` 导致 `Find(PanelBody)` 串窗）。已修正后日常 L2 **C02+C14 全绿**（含 export）。

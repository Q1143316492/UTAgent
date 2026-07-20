# UI Benchmark — 怎么跑 / 怎么加 / 测完怎么收

真源表：`Assets/UTAgent/Docs/ui-assembly-benchmark.md`

## 两档（没有全量）

| 档 | 命令 | 内容 |
|----|------|------|
| **日常 L1** | `./run_benchmark.ps1` | 门禁冒烟：**E16+E17**（非整页答案） |
| **日常 L2** | `./run_benchmark.ps1 -L2Only` | **C02+C14+C15**（chat → health；FAIL 则打回 AI 纠偏再扫 → export） |
| **日常合计** | `./run_benchmark.ps1 -L2` | 上述 L1 + L2 |
| **按需** | `-L2Only -Cases "C15"` 或 `-L1Only -Cases E12` | 显式 ID |

`-FullDev` 已废除。不要「跑齐所有 E/C」。  
纠偏次数：`-RemediationMax 1`（默认）或环境变量 `UTAGENT_HEALTH_REMEDIATION_MAX`（上限 2）。

**正式拼 UI = L2 chat。** 预写整页 `golden_path` 已归档，禁止加回日常。  
**审阅：** `TestFixtures/UIPanels/Wnd*.prefab`（最终 health PASS 后 export，非 L1 golden）。

## L2 闭环

```
chat → health
  FAIL → 不清 history，把失败 JSON 打回 Agent（限次）→ 再 health
  PASS → export fixtures
```

## 根目录只放日常必要

- 入口、`assert_ui_scene_health`、`ui_panel_scope`、`export_*`、`parse_agent_log`
- 日常 L1：命名/尺寸冒烟（E16/E17）

## 给以后 AI：怎么加测试

1. **新整页拼 UI**：只加 **L2**（`daily_l2` + 真源表）；fixtures 由 L2 export。**禁止**再写日常面板 golden。
2. **开发中临时验证**（钩子/原语）：放 `opt-in/`，`-Cases`；真源表「按需」。
3. **测通且不再需要**：删脚本 + 表标已删；不要堆博物馆。
4. **升格**：临时用例长期有价值 → 进日常（L2 面板或 L1 门禁），并从 opt-in 清掉。

## 目录

- `archive/` — 已验证过的面板 golden（一次性冒烟后归档；勿回日常）
- `panels/` — 已空；勿再放整页答案脚本
- `opt-in/` — 临时/钩子相关；见该目录 README

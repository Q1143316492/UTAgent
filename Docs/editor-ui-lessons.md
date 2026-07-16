# Editor UI 实测教训汇总

> **给人看，不进 `loadSkill`。** 长案例与回灌记录写这里；`editor-ui.md.txt` 只留硬规则摘要。
> 索引：`Docs/ut-agent/15-agent-ui-skills.md`。

## 维护约定

1. 外面（chat / 手测 / CLI 诊断）踩到 UI 坑 → **先在本文件追加一条**
2. 再改 skill / before-exec / L1 / golden_path
3. 回灌后在条目里勾「已回灌路径」
4. **禁止**把长教训整篇贴进 `editor-ui.md.txt`

### 条目模板

```
### YYYY-MM-DD · 短标题
- 现象：
- 根因：
- 已回灌：skill / before-exec / L1 / golden_path / 无
- 仍缺：
```

---

## 记录

### 2026-07-17 · WndLogin 输入框宽度塌成 0

- **现象**：登录面板 `Input*` 看起来像竖条字；CLI 查 `InputBg`/`Input*` `rect.w=0`
- **根因**：父 `VerticalLayoutGroup` 默认 `childForceExpand*=true` 且 `childControl*=false`；子节点 `preferredWidth=-1` + `sizeDelta.x=0` → 宽度塌成 0
- **已回灌**：
  - skill：`LayoutGroup` 四布尔硬规则；`#3 create_tmp_input_field`（`LayoutElement.preferredHeight`）；原语列表 5 个
  - before-exec：`layout-control`（`AddComponent(LayoutGroup)` 缺 `childControlWidth/Height` → inject）
  - L1：`assert_layout_zero_width_e12.py`（扫 `Input*`/`Btn*`）；`golden_path_tmp_input_field.py`
  - 文档：本文件 + doc 15 索引；doc 15 `#3` 改为 skill 指针
- **仍缺**：域重载后需再跑一次 chat 确认 log 含 `before-exec: layout-control → inject reminder`（离线正则 `assert_layout_control_regex.py` 已 PASS）

### 2026-07-17 · TextArea 无 RectTransform

- **现象**：`create_tmp_input_field` 对 `TextArea` 用 `GetComponent(RectTransform)` 抛 MissingComponentException
- **根因**：空 `GameObject` 挂到 UI 下时未必已有 `RectTransform`（与 `#1` `TxtLabel` 同）
- **已回灌**：skill / golden_path / E12 对 `TextArea` 改用 `AddComponent(RectTransform)`
- **仍缺**：无

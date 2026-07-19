# Editor UI 实测教训汇总

> **给人看，不进 `loadSkill`。** 长案例与回灌记录写这里；`editor-ui.md.txt` 只留硬规则摘要。  
> **包内索引**：同目录 [`ui-assembly-benchmark.md`](./ui-assembly-benchmark.md)、[`extension-points.md`](./extension-points.md)；硬规则真源：`Python/agent/skills/editor-ui.md.txt`。  
> 宿主仓库的 `Docs/ut-agent/15-*` 等为项目学习笔记，**不是**本插件的可移植依赖，勿在包内文档硬链仓库路径。

## 维护约定

1. 外面（chat / 手测 / CLI 诊断）踩到 UI 坑 → **先在本文件追加一条**
2. 再改 skill / before-exec / L1 / golden_path
3. 回灌后在条目里勾「已回灌路径」
4. **禁止**把长教训整篇贴进 `editor-ui.md.txt`
5. 案例可写现场窗口名（如 `WndLogin`），但 **勿**依赖宿主绝对路径或包外文档链接

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

### 2026-07-18 · WndRoleDetail 技能描述竖条字 + 对象名带 emoji

- **现象**：技能区中间文案竖排；Hierarchy 出现带 emoji 的容器名（旧 `PanelSkill_✦` 一类）
- **根因**：
  1. `HorizontalLayoutGroup.childForceExpandWidth=False`，左侧 `TxtSkillName.preferredWidth=90`，右侧 `TxtSkillDesc` 未设 `preferredWidth`/`flexibleWidth` → `rect.w=0` → 竖条字
  2. 把 emoji 写进 `GameObject.name`（展示应用 `TMP.text`）
- **已回灌**：skill `editor-ui.md.txt` 命名 v2（禁 emoji/中文进对象名）+ HLG 文本行硬规则；本文件
- **仍缺**：无（现场可用 CLI：`flexibleWidth=1` + 行容器改为角色名如 `RowSkill{N}`）

### 2026-07-17 · WndLogin 输入框宽度塌成 0

- **现象**：登录面板 `Input*` 看起来像竖条字；CLI 查 `InputBg`/`Input*` `rect.w=0`
- **根因**：父 `VerticalLayoutGroup` 默认 `childForceExpand*=true` 且 `childControl*=false`；子节点 `preferredWidth=-1` + `sizeDelta.x=0` → 宽度塌成 0
- **已回灌**：
  - skill：`LayoutGroup` 四布尔硬规则；`#3 create_tmp_input_field`（`LayoutElement.preferredHeight`）；原语列表 5 个
  - before-exec：`layout-control`（`AddComponent(LayoutGroup)` 缺 `childControlWidth/Height` → inject）
  - L1：`assert_layout_zero_width_e12.py`（扫 `Input*`/`Btn*`）；`golden_path_tmp_input_field.py`
  - 文档：本文件；skill 内 `#3` 为真源
- **仍缺**：域重载后需再跑一次 chat 确认 log 含 `before-exec: layout-control → inject reminder`（离线正则 `assert_layout_control_regex.py` 已 PASS）

### 2026-07-17 · TextArea 无 RectTransform

- **现象**：`create_tmp_input_field` 对 `TextArea` 用 `GetComponent(RectTransform)` 抛 MissingComponentException
- **根因**：空 `GameObject` 挂到 UI 下时未必已有 `RectTransform`（与 `#1` `TxtLabel` 同）
- **已回灌**：skill / golden_path / E12 对 `TextArea` 改用 `AddComponent(RectTransform)`
- **仍缺**：无

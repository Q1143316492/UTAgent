# UT Agent — 项目指令（示例）

> 本文件会被注入 system 的 `## Project Instructions`。  
> 优先：项目根 `AGENTS.md`；否则用本文件。**不会**自动加载 `CLAUDE.md`。  
> 只写短政策/禁令；领域操作手册请用 `loadSkill`（如 `editor-ui`）。

## 规则

- 禁止创建名为 `ForbiddenPanel` 的 GameObject / UI 根节点（验收示意）。
- GameObject 命名用英文 PascalCase 前缀（`Wnd*` / `Btn*` / `Txt*` / `Panel*` / `Input*`）；展示文案可中文，**不要**把 emoji 写进对象名。
- 在 Editor/`execPython` 中查找资源用 `AssetDatabase.FindAssets` / `LoadAssetAtPath`；**禁止** `os.walk` / `Path.rglob` / 递归 `glob` 扫 `Assets`（会卡住编辑器）。
- 改文件后需「刷新 Python」或新开一轮 turn 才会重新加载本指令。

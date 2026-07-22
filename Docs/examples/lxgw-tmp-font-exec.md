# 样例：多次 `utagent exec` 生成中英 TMP 字体并冒烟验收

> 展示 Cursor / 编码助手如何用 **CLI `exec`** 在 Editor 里完成「导入字体 → 生成 Dynamic TMP → 拼一句中英混排 → 截图验收」，无需手点 Font Asset Creator。  
> 对应资源：`Assets/Fonts/LXGWWenKai-Regular.*`（SIL OFL）。

## 为什么值得看

TMP 中文字体手工流程长（选字符集、烘焙、挂材质）。本样例把关键步骤拆成多次 `utagent exec --file`，由本会话编排；Unity 侧只跑短 Python。

```
下载 TTF（本机/HTTP）
        │
        ▼
  utagent exec  →  CreateFontAsset(Dynamic) → Assets/Fonts/* SDF.asset
        │
        ▼
  utagent exec  →  场景拼一句中英混排 TMP_Text（冒烟）
        │
        ▼
  utagent exec  →  离屏相机渲染 / 或 screenshot → 识图验收
```

## 前置

```powershell
python ./Assets/UTAgent/Tools/utagent-cli/utagent.py ping
# engine_available=false 时：
python ./Assets/UTAgent/Tools/utagent-cli/utagent.py init
```

源字体已放在 `Assets/Fonts/LXGWWenKai-Regular.ttf`（霞鹜文楷 Regular，SIL OFL，许可证见同目录 `LXGWWenKai-OFL.txt`）。下载本身不经 UTAgent。

## 步骤 1：生成 Dynamic TMP Font Asset

`TMP_FontAsset.CreateFontAsset` + `AssetDatabase.CreateAsset`。Dynamic 按需进字，避免全量静态 CJK 上百兆。

要点（完整脚本当时在 `Out/exec/gen_lxgw_tmp_font.py`，gitignore；逻辑如下）：

```python
from unity_bind import CS

TTF = "Assets/Fonts/LXGWWenKai-Regular.ttf"
OUT = "Assets/Fonts/LXGWWenKai-Regular SDF.asset"

CS.UnityEditor.AssetDatabase.Refresh()
font = CS.UnityEditor.AssetDatabase.LoadAssetAtPath[CS.UnityEngine.Font](TTF)

GlyphRenderMode = CS.UnityEngine.TextCore.LowLevel.GlyphRenderMode
AtlasPopulationMode = CS.TMPro.AtlasPopulationMode
fa = CS.TMPro.TMP_FontAsset.CreateFontAsset(
    font, 40, 5, GlyphRenderMode.SDFAA, 1024, 1024,
    AtlasPopulationMode.Dynamic, True,
)
CS.UnityEditor.AssetDatabase.CreateAsset(fa, OUT)
if fa.material is not None:
    fa.material.name = fa.name + " Material"
    CS.UnityEditor.AssetDatabase.AddObjectToAsset(fa.material, fa)
# atlasTextures 同样 AddObjectToAsset …
CS.UnityEditor.AssetDatabase.SaveAssets()
print({"ok": True, "path": OUT, "dynamic": True})
```

```powershell
python ./Assets/UTAgent/Tools/utagent-cli/utagent.py exec --file Assets/UTAgent/Out/exec/gen_lxgw_tmp_font.py
```

期望输出含 `"dynamic": true`、`"path": "Assets/Fonts/LXGWWenKai-Regular SDF.asset"`。

> 导入大 TTF 可能触发域重载；若随后 `exec` 报未初始化，先 `utagent init`。

## 步骤 2：场景中间拼一句中英混排（冒烟）

用新 SDF 挂 `TextMeshProUGUI`，文案刻意混中文 / ASCII / 数字 / 标点：

```text
设置 Settings 等级 Lv.12 生命 100/100！ Hello ABC 中英混排。
```

要点：

```python
from unity_bind import CS

FONT = "Assets/Fonts/LXGWWenKai-Regular SDF.asset"
TEXT = "设置 Settings 等级 Lv.12 生命 100/100！ Hello ABC 中英混排。"
fa = CS.UnityEditor.AssetDatabase.LoadAssetAtPath[CS.TMPro.TMP_FontAsset](FONT)
fa.TryAddCharacters(TEXT, True)  # Dynamic：把这句用到的字烤进 atlas

# Canvas + Panel + TextMeshProUGUI(font=fa, fontSize=48, 居中黄字)
tmp.text = TEXT
tmp.ForceMeshUpdate(True, True)
# 可用 textInfo.meshInfo[0].vertexCount > 0 作为「已出网格」信号
```

Screen Space Overlay 在部分截图路径下不好拍；文档截图改用 **World Space Canvas + 临时正交相机离屏 `Render` → `EncodeToPNG`**（同一次 `exec` 内完成），避免依赖 Game 视图焦点。

## 步骤 3：截图 / 识图验收

结果图（黄字居中、中英同屏）：

![LXGW TMP 中英混排冒烟](lxgw-tmp-font-smoke.png)

验收标准（不必全 Unicode）：

- 常见汉字与 ASCII 无大面积缺字方框（tofu）
- `ForceMeshUpdate` 后 mesh 有顶点（本样例约 160 verts）
- 肉眼可读「设置 / Settings / Lv.12 / 中英混排」

## 后续（同一次会话也可继续 `exec`）

当时还用 UTAgent 做了：

| 动作 | 作用 |
|------|------|
| 批量改 Prefab / `Boot.unity` / TMP Settings | 默认字体切到 LXGW |
| `AssetDatabase.DeleteAsset` | 删除旧 Vonwaon 字库 |

详见会话脚本思路：`Out/exec/replace_all_tmp_to_lxgw.py`、`delete_vonwaon_fonts.py`（临时目录，不入库）。

## 和「拼 UI」样例的关系

| 样例 | 入口 | 重点 |
|------|------|------|
| [WndTitle / 创建角色 / HUD](../README.md#样例持续迭代) | `exec` 拼面板 + health | Domain Pack `editor-ui` |
| **本页** | `exec` 生成/验收字体 | Editor API（TMP + AssetDatabase + 离屏相机） |

二者都是：**本会话编排 + 多次短 `exec`，不用 `utagent chat` 当默认路径。**

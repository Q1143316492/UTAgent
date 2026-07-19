"""命名断言冒烟：非 ASCII 节点名 FAIL；纯文案中文不 FAIL。

用法：utagent exec --file assert_ui_naming_smoke.py
依赖 assert_ui_scene_health.scan（同目录）。
"""
import json
import os
import sys

import unity
from unity_bind import CS

# 同目录导入（utagent exec 无 __file__，用包内固定相对路径）
_bench = os.path.join("Assets", "UTAgent", "Tools", "ui-benchmark")
_abs = os.path.abspath(_bench)
if os.path.isdir(_abs) and _abs not in sys.path:
    sys.path.insert(0, _abs)
# 兼容从 Tools/ui-benchmark 工作目录启动
_cwd_bench = os.path.abspath(".")
if os.path.isfile(os.path.join(_cwd_bench, "assert_ui_scene_health.py")) and _cwd_bench not in sys.path:
    sys.path.insert(0, _cwd_bench)
import assert_ui_scene_health as health  # noqa: E402


def _ensure_canvas():
    canvas = CS.UnityEngine.GameObject.Find("Canvas")
    if canvas is not None:
        return canvas
    canvas = CS.UnityEngine.GameObject("Canvas")
    canvas.AddComponent(CS.UnityEngine.Canvas)
    canvas.AddComponent(CS.UnityEngine.UI.CanvasScaler)
    canvas.AddComponent(CS.UnityEngine.UI.GraphicRaycaster)
    return canvas


def _destroy_if_exists(name):
    go = CS.UnityEngine.GameObject.Find(name)
    if go is not None:
        CS.UnityEngine.Object.DestroyImmediate(go)


def run():
    canvas = _ensure_canvas()
    # 清理上次残留
    for n in ("PanelMusicRowBad", "Panel音乐Row", "WndNamingSmoke"):
        _destroy_if_exists(n)

    # 1) 非 ASCII 节点名应 FAIL
    bad = CS.UnityEngine.GameObject("Panel音乐Row")
    bad.transform.SetParent(canvas.transform, False)
    bad.AddComponent(CS.UnityEngine.RectTransform).sizeDelta = CS.UnityEngine.Vector2(100, 40)
    r1 = health.scan(None)
    non_ascii = r1.get("non_ascii_names") or []
    names = [x.get("name") if isinstance(x, dict) else x for x in non_ascii]
    case1_ok = r1.get("ok") is False and any("Panel音乐Row" in str(n) for n in names)
    _destroy_if_exists("Panel音乐Row")

    # 2) 仅 TMP 文案中文、节点名 ASCII → 命名不应因文案失败
    wnd = CS.UnityEngine.GameObject("WndNamingSmoke")
    wnd.transform.SetParent(canvas.transform, False)
    wnd.AddComponent(CS.UnityEngine.UI.Image)
    rt = wnd.GetComponent(CS.UnityEngine.RectTransform)
    rt.sizeDelta = CS.UnityEngine.Vector2(200, 80)
    title = CS.UnityEngine.GameObject("TxtTitle")
    title.transform.SetParent(wnd.transform, False)
    tmp = title.AddComponent(CS.TMPro.TextMeshProUGUI)
    tmp.text = "设置"
    r2 = health.scan(["WndNamingSmoke"])
    non_ascii2 = r2.get("non_ascii_names") or []
    case2_ok = len(non_ascii2) == 0
    _destroy_if_exists("WndNamingSmoke")

    ok = case1_ok and case2_ok
    return {
        "ok": ok,
        "non_ascii_name_fails": case1_ok,
        "chinese_text_ok": case2_ok,
        "case1_non_ascii_count": len(non_ascii),
        "case2_non_ascii_count": len(non_ascii2),
    }


print(json.dumps(run(), ensure_ascii=False))

"""尺寸门禁冒烟：近零 rect / 缺 preferred 应 FAIL；声明 preferred 的合法小树应 PASS。

不依赖已归档的整页面板 golden（反作弊）。
"""
import json
import os
import sys

from unity_bind import CS

_bench = os.path.join("Assets", "UTAgent", "Tools", "ui-benchmark")
for p in (os.path.abspath(_bench), os.path.abspath(".")):
    if os.path.isdir(p) and p not in sys.path:
        sys.path.insert(0, p)
import assert_ui_scene_health as health  # noqa: E402
import ui_panel_scope as scope  # noqa: E402


def _ensure_canvas():
    c = CS.UnityEngine.GameObject.Find("Canvas")
    if c is not None:
        return c
    c = CS.UnityEngine.GameObject("Canvas")
    c.AddComponent(CS.UnityEngine.Canvas)
    c.AddComponent(CS.UnityEngine.UI.CanvasScaler)
    c.AddComponent(CS.UnityEngine.UI.GraphicRaycaster)
    return c


def _set_preferred(go, preferred_w=None, preferred_h=None):
    le = go.GetComponent(CS.UnityEngine.UI.LayoutElement)
    if le is None:
        le = go.AddComponent(CS.UnityEngine.UI.LayoutElement)
    if preferred_w is not None:
        le.preferredWidth = preferred_w
    if preferred_h is not None:
        le.preferredHeight = preferred_h


def _case_bad_btn():
    scope.destroy_named_roots("WndSizeBad")
    canvas = _ensure_canvas()
    wnd = CS.UnityEngine.GameObject("WndSizeBad")
    wnd.transform.SetParent(canvas.transform, False)
    wnd.AddComponent(CS.UnityEngine.UI.Image)
    rt = wnd.GetComponent(CS.UnityEngine.RectTransform)
    rt.sizeDelta = CS.UnityEngine.Vector2(300, 120)
    body = CS.UnityEngine.GameObject("PanelBody")
    body.transform.SetParent(wnd.transform, False)
    hlg = body.AddComponent(CS.UnityEngine.UI.HorizontalLayoutGroup)
    hlg.childControlWidth = True
    hlg.childControlHeight = True
    btn = CS.UnityEngine.GameObject("BtnBad")
    btn.AddComponent(CS.UnityEngine.UI.Image)
    btn.AddComponent(CS.UnityEngine.UI.Button)
    btn.GetComponent(CS.UnityEngine.RectTransform).sizeDelta = CS.UnityEngine.Vector2(100, 40)
    btn.transform.SetParent(body.transform, False)
    CS.UnityEngine.Canvas.ForceUpdateCanvases()
    r = health.scan(["WndSizeBad"])
    scope.destroy_named_roots("WndSizeBad")
    # 缺 preferred 或零宽 → fail
    return r.get("ok") is False and (
        r.get("missing_preferred_count", 0) > 0 or r.get("zero_size_count", 0) > 0
    ), r


def _case_good_btn():
    """最小合法 Layout 子树：有 preferred，应 health PASS。"""
    scope.destroy_named_roots("WndSizeOk")
    canvas = _ensure_canvas()
    wnd = CS.UnityEngine.GameObject("WndSizeOk")
    wnd.transform.SetParent(canvas.transform, False)
    wnd.AddComponent(CS.UnityEngine.UI.Image)
    rt = wnd.GetComponent(CS.UnityEngine.RectTransform)
    rt.sizeDelta = CS.UnityEngine.Vector2(300, 120)
    body = CS.UnityEngine.GameObject("PanelBody")
    body.transform.SetParent(wnd.transform, False)
    hlg = body.AddComponent(CS.UnityEngine.UI.HorizontalLayoutGroup)
    hlg.childControlWidth = True
    hlg.childControlHeight = True
    hlg.childForceExpandWidth = False
    hlg.childForceExpandHeight = False
    btn = CS.UnityEngine.GameObject("BtnOk")
    btn.AddComponent(CS.UnityEngine.UI.Image)
    btn.AddComponent(CS.UnityEngine.UI.Button)
    btn.transform.SetParent(body.transform, False)
    _set_preferred(btn, preferred_w=100, preferred_h=40)
    CS.UnityEngine.Canvas.ForceUpdateCanvases()
    r = health.scan(["WndSizeOk"])
    scope.destroy_named_roots("WndSizeOk")
    return r.get("ok") is True, r


bad_ok, bad_r = _case_bad_btn()
good_ok, good_r = _case_good_btn()
print(json.dumps({
    "ok": bad_ok and good_ok,
    "bad_btn_fails": bad_ok,
    "bad_detail": {
        "zero": bad_r.get("zero_size_count"),
        "missing_preferred": bad_r.get("missing_preferred_count"),
    },
    "good_passes": good_ok,
    "good_zero": good_r.get("zero_size_count"),
    "good_missing_preferred": good_r.get("missing_preferred_count"),
}, ensure_ascii=False))

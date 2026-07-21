# 场景 UI 健康扫描：Canvas 外、零/近零尺寸、直挂、非 ASCII 名、Layout 下缺 preferred。
# CLI / skill assert / benchmark：**请跑** run_assert_ui_scene_health.py（薄入口，过 L1 体积限）。
# 本文件可被 import；直接 exec 本文件可能触发 code-too-long。
# 可选根过滤（优先顺序）：请求文件 → UTAGENT_HEALTH_ROOTS → 全场景
# 请求文件：Assets/UTAgent/Tools/ui-benchmark/.tmp/_health_roots.txt（逗号或换行分隔）
# 只查 GameObject.name，不查 TMP 文案。
# 注意：CLI 进程里设的环境变量进不了 Unity 内 Python，故 harness 必须写请求文件。
import json
import os
import unity
from unity_bind import CS

REQUEST_FILE = "Assets/UTAgent/Tools/ui-benchmark/.tmp/_health_roots.txt"


def _resolve_roots():
    if os.path.isfile(REQUEST_FILE):
        with open(REQUEST_FILE, "r", encoding="utf-8-sig") as f:
            raw = f.read().strip()
        if raw:
            parts = []
            for chunk in raw.replace(",", " ").split():
                p = chunk.strip()
                if p:
                    parts.append(p)
            if parts:
                return parts
    env = os.environ.get("UTAGENT_HEALTH_ROOTS", "").strip()
    if env:
        return [r.strip() for r in env.split(",") if r.strip()]
    return None

UI_PREFIXES = ("Wnd", "Panel", "Btn", "Txt", "Input", "Row", "Toggle", "Slider")
# 含 Txt：角色面板 TxtValue/TxtName 曾出现 rect≈0.01
SIZE_PREFIXES = ("Wnd", "Panel", "Btn", "Input", "Row", "Txt", "Toggle", "Slider")
# Layout 下必须有 preferred 的交互控件（通用：凡进 LayoutGroup 的控件）
PREFERRED_PREFIXES = ("Btn", "Input", "Toggle", "Slider")
MIN_SIZE = 1.0


def _is_ui_name(name):
    return any(name.startswith(p) for p in UI_PREFIXES)


def _has_non_ascii(name):
    return any(ord(ch) > 127 for ch in name)


def _under_canvas(t, canvas_t):
    cur = t
    while cur is not None:
        if cur == canvas_t:
            return True
        cur = cur.parent
    return False


def _wnd_ancestor(t):
    cur = t
    while cur is not None:
        if cur.gameObject.name.startswith("Wnd"):
            return cur.gameObject.name
        cur = cur.parent
    return None


def _parent_layout(t):
    """返回 (layout_component_or_None, is_hlg)。"""
    if t.parent is None:
        return None, False
    pgo = t.parent.gameObject
    hlg = pgo.GetComponent(CS.UnityEngine.UI.HorizontalLayoutGroup)
    if hlg is not None:
        return hlg, True
    vlg = pgo.GetComponent(CS.UnityEngine.UI.VerticalLayoutGroup)
    if vlg is not None:
        return vlg, False
    return None, False


def _collect_all_transforms():
    scene = CS.UnityEngine.SceneManagement.SceneManager.GetActiveScene()
    roots = scene.GetRootGameObjects()
    out = []
    for go in roots:
        stack = [go.transform]
        while len(stack) > 0:
            t = stack.pop()
            out.append(t)
            for i in range(t.childCount):
                stack.append(t.GetChild(i))
    return out


def _passes_filter(t, name, filter_set):
    if filter_set is None:
        return True
    wnd = _wnd_ancestor(t)
    if name in filter_set or (wnd is not None and wnd in filter_set):
        return True
    return any(name == r or name.startswith(r) for r in filter_set)


def scan(roots_filter=None):
    canvas = CS.UnityEngine.GameObject.Find("Canvas")
    canvas_t = canvas.transform if canvas is not None else None
    outside_canvas = []
    zero_size = []
    canvas_direct = []
    non_ascii_names = []
    missing_preferred = []
    no_canvas = canvas is None
    filter_set = set(roots_filter) if roots_filter else None

    for t in _collect_all_transforms():
        go = t.gameObject
        name = go.name
        if not _is_ui_name(name):
            continue

        under = (not no_canvas) and _under_canvas(t, canvas_t)
        # scoped 过滤时仍报告 Canvas 外孤儿（半失败脚本未 SetParent 的假绿来源）
        if filter_set is not None and not _passes_filter(t, name, filter_set):
            if no_canvas:
                outside_canvas.append({"name": name, "reason": "no Canvas", "orphan": True})
            elif not under:
                outside_canvas.append({
                    "name": name,
                    "parent": t.parent.gameObject.name if t.parent is not None else None,
                    "orphan": True,
                })
            continue

        if _has_non_ascii(name):
            non_ascii_names.append({"name": name})

        if no_canvas:
            outside_canvas.append({"name": name, "reason": "no Canvas"})
            continue

        if not under:
            outside_canvas.append({
                "name": name,
                "parent": t.parent.gameObject.name if t.parent is not None else None,
            })
            continue

        if t.parent == canvas_t and any(
            name.startswith(p) for p in ("Btn", "Input", "Txt", "Row", "Toggle", "Slider")
        ):
            canvas_direct.append({"name": name})

        # 布局后零/近零 rect
        if any(name.startswith(p) for p in SIZE_PREFIXES):
            rt = go.GetComponent(CS.UnityEngine.RectTransform)
            if rt is not None:
                w = float(rt.rect.width)
                h = float(rt.rect.height)
                if w <= MIN_SIZE or h <= MIN_SIZE:
                    zero_size.append({
                        "name": name,
                        "w": round(w, 3),
                        "h": round(h, 3),
                        "wnd": _wnd_ancestor(t),
                        "parent": t.parent.gameObject.name if t.parent is not None else None,
                    })

        # Layout 下交互控件：父须 childControl；子须在控制轴上声明 preferred
        # （不猜具体像素默认值；关 childControl 时会留下组件默认 sizeDelta）
        if any(name.startswith(p) for p in PREFERRED_PREFIXES):
            layout, is_hlg = _parent_layout(t)
            if layout is not None:
                le = go.GetComponent(CS.UnityEngine.UI.LayoutElement)
                pw = float(le.preferredWidth) if le is not None else -1.0
                ph = float(le.preferredHeight) if le is not None else -1.0
                fw = float(le.flexibleWidth) if le is not None else 0.0
                control_w = bool(layout.childControlWidth)
                control_h = bool(layout.childControlHeight)
                force_w = bool(layout.childForceExpandWidth)
                miss_control = (not control_w) or (not control_h)
                # 宽：preferred>0 / flexible>0 / 父 forceExpandWidth 均可
                # （对齐 editor-ui：输入框可省略 preferredWidth，靠表单行拉满宽）
                # 高：必须 preferred>0（skill 硬要求）
                miss_w = control_w and pw < 0 and fw <= 0 and (not force_w)
                miss_h = control_h and ph < 0
                if miss_control or miss_w or miss_h:
                    missing_preferred.append({
                        "name": name,
                        "parent": t.parent.gameObject.name if t.parent is not None else None,
                        "child_control_w": control_w,
                        "child_control_h": control_h,
                        "child_force_expand_w": force_w,
                        "preferred_w": pw,
                        "preferred_h": ph,
                        "flexible_w": fw,
                        "layout": "HLG" if is_hlg else "VLG",
                    })

    return {
        "ok": (
            not no_canvas
            and len(outside_canvas) == 0
            and len(zero_size) == 0
            and len(canvas_direct) == 0
            and len(non_ascii_names) == 0
            and len(missing_preferred) == 0
        ),
        "has_canvas": not no_canvas,
        "outside_canvas_count": len(outside_canvas),
        "zero_size_count": len(zero_size),
        "canvas_direct_count": len(canvas_direct),
        "non_ascii_name_count": len(non_ascii_names),
        "missing_preferred_count": len(missing_preferred),
        "outside_canvas": outside_canvas[:40],
        "zero_size": zero_size[:40],
        "canvas_direct": canvas_direct[:40],
        "non_ascii_names": non_ascii_names[:40],
        "missing_preferred": missing_preferred[:40],
    }


def main():
    CS.UnityEngine.Canvas.ForceUpdateCanvases()
    roots = _resolve_roots()
    unity.log("[assert_ui_scene_health] scanning…")
    result = scan(roots)
    # 已知面板根：完整性 +（HUD）最小尺寸；全场景时对场景中已存在的已知根同样检查
    import sys as _sys
    _bench = os.path.join("Assets", "UTAgent", "Tools", "ui-benchmark")
    for p in (os.path.abspath(_bench), os.path.abspath(".")):
        if os.path.isdir(p) and p not in _sys.path:
            _sys.path.insert(0, p)
    import ui_panel_scope as scope  # noqa: E402
    known = set(scope.INTEGRITY.keys()) | set(scope.HUD_MIN_SIZES.keys())
    if roots:
        check_names = list(roots)
    else:
        check_names = [n for n in known if CS.UnityEngine.GameObject.Find(n) is not None]
    integ_results = []
    integ_ok = True
    for root_name in check_names:
        go = CS.UnityEngine.GameObject.Find(root_name)
        if root_name in scope.INTEGRITY:
            integ = scope.check_integrity(go, root_name)
            integ_results.append({"root": root_name, **integ})
            if not integ.get("ok"):
                integ_ok = False
        if root_name in scope.HUD_MIN_SIZES:
            sizes = scope.check_hud_min_sizes(go, root_name)
            integ_results.append({"root": root_name, "kind": "hud_min_sizes", **sizes})
            if not sizes.get("ok"):
                integ_ok = False
    if integ_results:
        result["integrity"] = integ_results
        result["integrity_ok"] = integ_ok
        if not integ_ok:
            result["ok"] = False
    if roots and os.path.isfile(REQUEST_FILE):
        try:
            os.remove(REQUEST_FILE)
        except OSError:
            pass
    print(json.dumps(result, ensure_ascii=False))


if globals().get("__name__") != "assert_ui_scene_health":
    main()

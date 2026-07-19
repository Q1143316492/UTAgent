"""Dump WndCharacter (or UTAGENT_HEALTH_ROOTS) 布局后 rect / sizeDelta。"""
import json
import os
from unity_bind import CS

CS.UnityEngine.Canvas.ForceUpdateCanvases()
root_name = os.environ.get("UTAGENT_HEALTH_ROOTS", "WndCharacter").split(",")[0].strip() or "WndCharacter"
root = CS.UnityEngine.GameObject.Find(root_name)
if root is None:
    print(json.dumps({"ok": False, "error": f"not found: {root_name}"}, ensure_ascii=False))
else:
    rows = []
    stack = [root.transform]
    while len(stack) > 0:
        t = stack.pop()
        go = t.gameObject
        name = go.name
        if any(name.startswith(p) for p in ("Wnd", "Panel", "Btn", "Txt", "Input", "Row")):
            rt = go.GetComponent(CS.UnityEngine.RectTransform)
            if rt is not None:
                sd = rt.sizeDelta
                rows.append({
                    "name": name,
                    "rect_w": round(float(rt.rect.width), 3),
                    "rect_h": round(float(rt.rect.height), 3),
                    "sd_x": round(float(sd.x), 3),
                    "sd_y": round(float(sd.y), 3),
                    "parent": t.parent.gameObject.name if t.parent is not None else None,
                })
        for i in range(t.childCount):
            stack.append(t.GetChild(i))
    near = [r for r in rows if (0 < r["rect_w"] <= 1) or (0 < r["rect_h"] <= 1) or r["rect_w"] <= 0 or r["rect_h"] <= 0]
    sd100 = [r for r in rows if r["sd_x"] >= 80 or r["sd_y"] >= 80]
    print(json.dumps({
        "ok": True,
        "root": root_name,
        "count": len(rows),
        "near_zero_or_zero": near,
        "large_size_delta": sd100,
        "sample": rows[:20],
    }, ensure_ascii=False))

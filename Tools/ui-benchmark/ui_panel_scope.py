"""UI 面板拼装作用域：相对 Wnd* 查找 / 挂子 / 清理旧根。

供 golden_path_* 与 export 共用。禁止裸全局 Find(\"PanelBody\") 跨窗体。
"""
from unity_bind import CS


def destroy_named_roots(*names):
    """销毁场景中同名对象（含 inactive），避免脏场景污染。"""
    want = set(n for n in names if n)
    if not want:
        return []
    to_kill = []
    scene = CS.UnityEngine.SceneManagement.SceneManager.GetActiveScene()
    for go in scene.GetRootGameObjects():
        stack = [go.transform]
        while len(stack) > 0:
            t = stack.pop()
            if t.gameObject.name in want:
                to_kill.append(t.gameObject)
            else:
                for i in range(t.childCount):
                    stack.append(t.GetChild(i))
    destroyed = []
    for go in to_kill:
        destroyed.append(go.name)
        CS.UnityEngine.Object.DestroyImmediate(go)
    return destroyed


def find_under(root_go, name):
    """在 root 子树内按名查找（含自身）；找不到返回 None。"""
    if root_go is None:
        return None
    if root_go.name == name:
        return root_go
    stack = [root_go.transform]
    while len(stack) > 0:
        t = stack.pop()
        for i in range(t.childCount):
            child = t.GetChild(i)
            if child.gameObject.name == name:
                return child.gameObject
            stack.append(child)
    return None


def add_child(parent_go, child_go, preferred_w=None, preferred_h=None):
    """把 child GO 挂到 parent（须已有 LayoutGroup），不碰 anchor。"""
    if parent_go is None:
        raise RuntimeError("parent_go is None")
    if child_go is None:
        raise RuntimeError("child_go is None")
    child_go.transform.SetParent(parent_go.transform, False)
    if preferred_w is not None or preferred_h is not None:
        le = child_go.GetComponent(CS.UnityEngine.UI.LayoutElement)
        if le is None:
            le = child_go.AddComponent(CS.UnityEngine.UI.LayoutElement)
        if preferred_w is not None:
            le.preferredWidth = preferred_w
        if preferred_h is not None:
            le.preferredHeight = preferred_h
    return {"parent": parent_go.name, "child": child_go.name}


def collect_descendant_names(root_go):
    """收集 root 子树全部 GameObject 名（含自身）。"""
    names = []
    if root_go is None:
        return names
    stack = [root_go.transform]
    while len(stack) > 0:
        t = stack.pop()
        names.append(t.gameObject.name)
        for i in range(t.childCount):
            stack.append(t.GetChild(i))
    return names


def count_prefix(names, prefix):
    return sum(1 for n in names if n.startswith(prefix))


# 各面板导出前最少子树（对齐 L2 prompt，禁止照搬某次 golden 控件搭配）
INTEGRITY = {
    # C14：账号/密码两个 Input* + 登录/取消 Btn*；「输入行」不强制 Row* 包装
    "WndLogin": {"Input": 2, "Btn": 2},
    # C02：两 row + 保存/取消；控件可以是 Toggle 或 Slider，不强求两者各一
    "WndSettings": {"Row": 2, "Btn": 2},
    # C15：属性 Row + 装备/关闭 Btn；头像区允许 PanelPortrait 或 PanelAvatar（prompt 只说「头像区」）
    "WndCharacter": {"Row": 3, "Btn": 3},
    # CLI HUD 练习：12 槽 + 三区（精确 Panel 名）
    "WndStardewHud": {
        "Slot": 12,
        "PanelStatus": 1,
        "PanelHotbar": 1,
        "PanelEnergy": 1,
    },
}

# 1920×1080 参考像素下限（仅声明于此的根在 health 根过滤时检查）
# 值：(min_w, min_h)；"Slot*" 表示每个 Slot 前缀子节点
HUD_MIN_SIZES = {
    "WndStardewHud": {
        "PanelStatus": (360, 220),
        "PanelHotbar": (800, 90),
        "PanelEnergy": (36, 280),
        "Slot*": (64, 64),
    },
}


def check_integrity(root_go, root_name=None):
    """返回 {ok, detail, counts}；不满足则 ok=false。"""
    name = root_name or (root_go.name if root_go is not None else "")
    rules = INTEGRITY.get(name)
    if rules is None:
        return {"ok": False, "error": f"no integrity rules for {name}", "counts": {}}
    if root_go is None:
        return {"ok": False, "error": f"root not found: {name}", "counts": {}}
    names = collect_descendant_names(root_go)
    counts = {}
    missing = []
    for key, need in rules.items():
        if key.startswith("Panel") and not key.endswith("*"):
            # 精确名
            have = 1 if key in names else 0
            counts[key] = have
            if have < need:
                missing.append(f"{key}>={need} got {have}")
        else:
            have = count_prefix(names, key)
            counts[key] = have
            if have < need:
                missing.append(f"{key}*>={need} got {have}")
    # 设置：两 row 对应至少两个 Toggle/Slider 控件（种类不限）
    if name == "WndSettings":
        tog = count_prefix(names, "Toggle")
        sld = count_prefix(names, "Slider")
        counts["Toggle"] = tog
        counts["Slider"] = sld
        if tog + sld < 2:
            missing.append(f"Toggle|Slider>=2 got {tog + sld}")
    if name == "WndCharacter":
        has_avatar = any(
            ("Avatar" in n or "Portrait" in n) and n.startswith("Panel")
            for n in names
        )
        counts["avatar_panel"] = 1 if has_avatar else 0
        if not has_avatar:
            missing.append("Panel*Avatar|Panel*Portrait")
    ok = len(missing) == 0
    return {"ok": ok, "missing": missing, "counts": counts, "name_count": len(names)}


def check_hud_min_sizes(root_go, root_name=None):
    """1080p 可读尺寸下限；无规则则 ok=true 跳过。"""
    name = root_name or (root_go.name if root_go is not None else "")
    rules = HUD_MIN_SIZES.get(name)
    if rules is None:
        return {"ok": True, "skipped": True}
    if root_go is None:
        return {"ok": False, "error": f"root not found: {name}", "too_small": []}
    too_small = []
    for key, (min_w, min_h) in rules.items():
        if key.endswith("*"):
            prefix = key[:-1]
            stack = [root_go.transform]
            while len(stack) > 0:
                t = stack.pop()
                go = t.gameObject
                if go.name.startswith(prefix) and go.name != root_go.name:
                    rt = go.GetComponent(CS.UnityEngine.RectTransform)
                    if rt is not None:
                        w = float(rt.rect.width)
                        h = float(rt.rect.height)
                        if w + 1e-3 < min_w or h + 1e-3 < min_h:
                            too_small.append({
                                "name": go.name,
                                "w": round(w, 1),
                                "h": round(h, 1),
                                "min_w": min_w,
                                "min_h": min_h,
                            })
                for i in range(t.childCount):
                    stack.append(t.GetChild(i))
            continue
        go = find_under(root_go, key)
        if go is None:
            too_small.append({"name": key, "missing": True, "min_w": min_w, "min_h": min_h})
            continue
        rt = go.GetComponent(CS.UnityEngine.RectTransform)
        if rt is None:
            too_small.append({"name": key, "error": "no RectTransform"})
            continue
        w = float(rt.rect.width)
        h = float(rt.rect.height)
        if w + 1e-3 < min_w or h + 1e-3 < min_h:
            too_small.append({
                "name": key,
                "w": round(w, 1),
                "h": round(h, 1),
                "min_w": min_w,
                "min_h": min_h,
            })
    return {"ok": len(too_small) == 0, "too_small": too_small[:40]}

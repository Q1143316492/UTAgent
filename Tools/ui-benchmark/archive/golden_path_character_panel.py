"""golden-path：角色面板 WndCharacter（复杂布局 L1 锚，非「角色新增」）。

挂子相对 WndCharacter；拼装前清旧根。
Layout 下 Btn/Txt 用 LayoutElement.preferred*，禁止只靠 sizeDelta 抢尺寸。
"""
import json
import os
import sys

import unity
from unity_bind import CS

_bench = os.path.join("Assets", "UTAgent", "Tools", "ui-benchmark")
for p in (os.path.abspath(_bench), os.path.abspath(".")):
    if os.path.isdir(p) and p not in sys.path:
        sys.path.insert(0, p)
import ui_panel_scope as scope  # noqa: E402


def _force_vlg(go):
    vlg = go.AddComponent(CS.UnityEngine.UI.VerticalLayoutGroup)
    vlg.spacing = 8
    vlg.childControlWidth = True
    vlg.childControlHeight = True
    vlg.childForceExpandWidth = True
    vlg.childForceExpandHeight = False
    return vlg


def _force_hlg(go, spacing=12):
    hlg = go.AddComponent(CS.UnityEngine.UI.HorizontalLayoutGroup)
    hlg.spacing = spacing
    hlg.childControlWidth = True
    hlg.childControlHeight = True
    hlg.childForceExpandWidth = False
    hlg.childForceExpandHeight = True
    return hlg


def _le(go, preferred_w=None, preferred_h=None, flex_w=None, flex_h=None):
    le = go.GetComponent(CS.UnityEngine.UI.LayoutElement)
    if le is None:
        le = go.AddComponent(CS.UnityEngine.UI.LayoutElement)
    if preferred_w is not None:
        le.preferredWidth = preferred_w
    if preferred_h is not None:
        le.preferredHeight = preferred_h
    if flex_w is not None:
        le.flexibleWidth = flex_w
    if flex_h is not None:
        le.flexibleHeight = flex_h
    return le


def _make_stat_row(row_name, label_text, value_text):
    row = CS.UnityEngine.GameObject(row_name)
    _force_hlg(row, spacing=8)
    label = CS.UnityEngine.GameObject("TxtLabel")
    label.transform.SetParent(row.transform, False)
    label_tmp = label.AddComponent(CS.TMPro.TextMeshProUGUI)
    label_tmp.text = label_text
    label_tmp.fontSize = 16
    label_tmp.color = CS.UnityEngine.Color(0.85, 0.85, 0.9, 1)
    _le(label, preferred_w=72, preferred_h=28)
    val = CS.UnityEngine.GameObject("TxtValue")
    val.transform.SetParent(row.transform, False)
    val_tmp = val.AddComponent(CS.TMPro.TextMeshProUGUI)
    val_tmp.text = value_text
    val_tmp.fontSize = 16
    val_tmp.color = CS.UnityEngine.Color(0.95, 0.95, 0.95, 1)
    _le(val, preferred_w=120, preferred_h=28, flex_w=1)
    return row


def _make_text(parent, name, text, font_size=18, preferred_h=24):
    go = CS.UnityEngine.GameObject(name)
    go.transform.SetParent(parent.transform, False)
    tmp = go.AddComponent(CS.TMPro.TextMeshProUGUI)
    tmp.text = text
    tmp.fontSize = font_size
    tmp.color = CS.UnityEngine.Color(0.95, 0.95, 0.95, 1)
    _le(go, preferred_h=preferred_h, flex_w=1)
    return go


def _add_button(parent_go, btn_name, btn_label, color=None, preferred_w=100, preferred_h=40, flex_w=0):
    if color is None:
        color = CS.UnityEngine.Color(0.23, 0.51, 0.96, 1)
    btn = CS.UnityEngine.GameObject(btn_name)
    btn.AddComponent(CS.UnityEngine.UI.Image).color = color
    btn.AddComponent(CS.UnityEngine.UI.Button)
    # Layout 下用 preferred，不用 sizeDelta 抢尺寸
    btn.GetComponent(CS.UnityEngine.RectTransform).sizeDelta = CS.UnityEngine.Vector2(0, 0)
    _le(btn, preferred_w=preferred_w, preferred_h=preferred_h, flex_w=flex_w)
    lbl = CS.UnityEngine.GameObject("TxtLabel")
    lbl.transform.SetParent(btn.transform, False)
    lbl_rt = lbl.AddComponent(CS.UnityEngine.RectTransform)
    lbl_rt.anchorMin = CS.UnityEngine.Vector2(0, 0)
    lbl_rt.anchorMax = CS.UnityEngine.Vector2(1, 1)
    lbl_rt.offsetMin = CS.UnityEngine.Vector2(0, 0)
    lbl_rt.offsetMax = CS.UnityEngine.Vector2(0, 0)
    lbl_tmp = lbl.AddComponent(CS.TMPro.TextMeshProUGUI)
    lbl_tmp.text = btn_label
    lbl_tmp.fontSize = 16
    lbl_tmp.color = CS.UnityEngine.Color(1, 1, 1, 1)
    lbl_tmp.alignment = CS.TMPro.TextAlignmentOptions.Center
    scope.add_child(parent_go, btn)


def build_character_panel():
    root_name = "WndCharacter"
    scope.destroy_named_roots(root_name)
    unity.prepare_scene_object(root_name)
    canvas = CS.UnityEngine.GameObject.Find("Canvas")
    if canvas is None:
        raise RuntimeError("Canvas not found")
    wnd = CS.UnityEngine.GameObject(root_name)
    wnd.transform.SetParent(canvas.transform, False)
    wnd.AddComponent(CS.UnityEngine.UI.Image).color = CS.UnityEngine.Color(0.14, 0.14, 0.17, 0.98)
    wnd_rt = wnd.GetComponent(CS.UnityEngine.RectTransform)
    wnd_rt.anchorMin = CS.UnityEngine.Vector2(0.5, 0.5)
    wnd_rt.anchorMax = CS.UnityEngine.Vector2(0.5, 0.5)
    wnd_rt.pivot = CS.UnityEngine.Vector2(0.5, 0.5)
    wnd_rt.sizeDelta = CS.UnityEngine.Vector2(520, 560)

    panel_body = CS.UnityEngine.GameObject("PanelBody")
    panel_body.transform.SetParent(wnd.transform, False)
    vlg = _force_vlg(panel_body)
    vlg.spacing = 16
    vlg.padding = CS.UnityEngine.RectOffset(24, 24, 24, 24)
    body_rt = panel_body.GetComponent(CS.UnityEngine.RectTransform)
    body_rt.anchorMin = CS.UnityEngine.Vector2(0, 0)
    body_rt.anchorMax = CS.UnityEngine.Vector2(1, 1)
    body_rt.offsetMin = CS.UnityEngine.Vector2(0, 0)
    body_rt.offsetMax = CS.UnityEngine.Vector2(0, 0)

    title = CS.UnityEngine.GameObject("TxtTitle")
    title.transform.SetParent(panel_body.transform, False)
    title_tmp = title.AddComponent(CS.TMPro.TextMeshProUGUI)
    title_tmp.text = "角色"
    title_tmp.fontSize = 28
    title_tmp.color = CS.UnityEngine.Color(0.95, 0.95, 0.95, 1)
    title_tmp.alignment = CS.TMPro.TextAlignmentOptions.Center
    _le(title, preferred_h=36)

    header = CS.UnityEngine.GameObject("PanelHeader")
    _force_hlg(header, spacing=16)
    scope.add_child(panel_body, header, preferred_h=96)
    portrait = CS.UnityEngine.GameObject("PanelPortrait")
    portrait.AddComponent(CS.UnityEngine.UI.Image).color = CS.UnityEngine.Color(0.25, 0.28, 0.35, 1)
    scope.add_child(header, portrait, preferred_w=88, preferred_h=88)
    identity = CS.UnityEngine.GameObject("PanelIdentity")
    _force_vlg(identity)
    scope.add_child(header, identity, preferred_w=280, preferred_h=88)
    _make_text(identity, "TxtName", "Alice", 20, preferred_h=28)
    _make_text(identity, "TxtLevel", "Lv.12", 16, preferred_h=22)
    _make_text(identity, "TxtClass", "战士", 16, preferred_h=22)

    stats = CS.UnityEngine.GameObject("PanelStats")
    _force_vlg(stats)
    scope.add_child(panel_body, stats, preferred_h=160)
    for row_name, label, value in [
        ("RowHp", "生命", "120/120"),
        ("RowMp", "魔力", "60/60"),
        ("RowAtk", "攻击", "35"),
        ("RowDef", "防御", "22"),
    ]:
        scope.add_child(stats, _make_stat_row(row_name, label, value), preferred_h=32)

    equip = CS.UnityEngine.GameObject("PanelEquip")
    hlg_eq = _force_hlg(equip, spacing=12)
    hlg_eq.childForceExpandWidth = True
    scope.add_child(panel_body, equip, preferred_h=48)
    slot_color = CS.UnityEngine.Color(0.32, 0.36, 0.45, 1)
    for btn_name, label in [
        ("BtnSlotWeapon", "武器"),
        ("BtnSlotArmor", "防具"),
        ("BtnSlotAccessory", "饰品"),
    ]:
        _add_button(equip, btn_name, label, slot_color, preferred_w=100, preferred_h=40, flex_w=1)

    panel_buttons = CS.UnityEngine.GameObject("PanelButtons")
    _force_hlg(panel_buttons)
    scope.add_child(panel_body, panel_buttons, preferred_h=48)
    _add_button(panel_buttons, "BtnClose", "关闭", preferred_w=120, preferred_h=40)

    CS.UnityEngine.Canvas.ForceUpdateCanvases()
    integ = scope.check_integrity(wnd, root_name)
    import assert_ui_scene_health as health
    size_gate = health.scan([root_name])
    save_result = unity.save_scene()
    return {
        "root_name": root_name,
        "stat_row_count": 4,
        "equip_slot_count": 3,
        "button_count": 4,
        "has_portrait": True,
        "has_vlg": vlg is not None,
        "integrity_ok": integ["ok"],
        "integrity": integ,
        "size_ok": size_gate.get("ok") is True,
        "size_gate": {
            "zero_size_count": size_gate.get("zero_size_count"),
            "missing_preferred_count": size_gate.get("missing_preferred_count"),
            "zero_size": size_gate.get("zero_size", [])[:8],
            "missing_preferred": size_gate.get("missing_preferred", [])[:8],
        },
        "save_scene_ok": save_result.get("success") is True,
    }


print(json.dumps(build_character_panel(), ensure_ascii=False))

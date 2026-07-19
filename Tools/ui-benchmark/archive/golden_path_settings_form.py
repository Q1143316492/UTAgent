"""golden-path：WndSettings 设置面板（L1 结构锚）。挂子相对 WndSettings；拼装前清旧根。"""
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


def _make_row(row_name, label_text, control_kind):
    """一行：标签 + 控件；尺寸走 LayoutElement.preferred*。"""
    row = CS.UnityEngine.GameObject(row_name)
    hlg = row.AddComponent(CS.UnityEngine.UI.HorizontalLayoutGroup)
    hlg.spacing = 12
    hlg.childControlWidth = True
    hlg.childControlHeight = True
    hlg.childForceExpandWidth = False
    hlg.childForceExpandHeight = False
    label = CS.UnityEngine.GameObject("TxtLabel")
    label.transform.SetParent(row.transform, False)
    label_tmp = label.AddComponent(CS.TMPro.TextMeshProUGUI)
    label_tmp.text = label_text
    label_tmp.fontSize = 18
    label_tmp.color = CS.UnityEngine.Color(0.95, 0.95, 0.95, 1)
    label_tmp.alignment = CS.TMPro.TextAlignmentOptions.Left
    le_l = label.AddComponent(CS.UnityEngine.UI.LayoutElement)
    le_l.preferredWidth = 72
    le_l.preferredHeight = 32
    if control_kind == "Toggle":
        ctrl = CS.UnityEngine.GameObject("Toggle" + row_name.replace("Row", ""))
        ctrl.transform.SetParent(row.transform, False)
        ctrl.AddComponent(CS.UnityEngine.UI.Image).color = CS.UnityEngine.Color(0.25, 0.28, 0.35, 1)
        ctrl.AddComponent(CS.UnityEngine.UI.Toggle)
        ctrl.GetComponent(CS.UnityEngine.RectTransform).sizeDelta = CS.UnityEngine.Vector2(0, 0)
        le = ctrl.AddComponent(CS.UnityEngine.UI.LayoutElement)
        le.preferredWidth = 36
        le.preferredHeight = 28
    elif control_kind == "Slider":
        ctrl = CS.UnityEngine.GameObject("Slider" + row_name.replace("Row", ""))
        ctrl.transform.SetParent(row.transform, False)
        ctrl.AddComponent(CS.UnityEngine.UI.Image).color = CS.UnityEngine.Color(0.2, 0.22, 0.28, 1)
        ctrl.AddComponent(CS.UnityEngine.UI.Slider)
        ctrl.GetComponent(CS.UnityEngine.RectTransform).sizeDelta = CS.UnityEngine.Vector2(0, 0)
        le = ctrl.AddComponent(CS.UnityEngine.UI.LayoutElement)
        le.preferredWidth = 180
        le.preferredHeight = 24
        le.flexibleWidth = 1
    else:
        ctrl = CS.UnityEngine.GameObject("Input" + row_name.replace("Row", ""))
        ctrl.transform.SetParent(row.transform, False)
        ctrl.AddComponent(CS.UnityEngine.UI.Image).color = CS.UnityEngine.Color(0.1, 0.1, 0.12, 1)
        ctrl.AddComponent(CS.TMPro.TMP_InputField)
        ctrl.GetComponent(CS.UnityEngine.RectTransform).sizeDelta = CS.UnityEngine.Vector2(0, 0)
        le = ctrl.AddComponent(CS.UnityEngine.UI.LayoutElement)
        le.preferredWidth = 200
        le.preferredHeight = 32
        le.flexibleWidth = 1
    return row


def build_settings_form():
    root_name = "WndSettings"
    scope.destroy_named_roots(root_name)
    unity.prepare_scene_object(root_name)
    canvas = CS.UnityEngine.GameObject.Find("Canvas")
    if canvas is None:
        raise RuntimeError("Canvas not found")
    wnd = CS.UnityEngine.GameObject(root_name)
    wnd.transform.SetParent(canvas.transform, False)
    wnd.AddComponent(CS.UnityEngine.UI.Image).color = CS.UnityEngine.Color(0.15, 0.15, 0.18, 0.98)
    wnd_rt = wnd.GetComponent(CS.UnityEngine.RectTransform)
    wnd_rt.anchorMin = CS.UnityEngine.Vector2(0.5, 0.5)
    wnd_rt.anchorMax = CS.UnityEngine.Vector2(0.5, 0.5)
    wnd_rt.pivot = CS.UnityEngine.Vector2(0.5, 0.5)
    wnd_rt.sizeDelta = CS.UnityEngine.Vector2(420, 360)

    panel_body = CS.UnityEngine.GameObject("PanelBody")
    panel_body.transform.SetParent(wnd.transform, False)
    vlg = panel_body.AddComponent(CS.UnityEngine.UI.VerticalLayoutGroup)
    vlg.spacing = 16
    vlg.padding = CS.UnityEngine.RectOffset(24, 24, 24, 24)
    vlg.childControlWidth = True
    vlg.childControlHeight = True
    vlg.childForceExpandWidth = True
    vlg.childForceExpandHeight = False
    body_rt = panel_body.GetComponent(CS.UnityEngine.RectTransform)
    body_rt.anchorMin = CS.UnityEngine.Vector2(0, 0)
    body_rt.anchorMax = CS.UnityEngine.Vector2(1, 1)
    body_rt.offsetMin = CS.UnityEngine.Vector2(0, 0)
    body_rt.offsetMax = CS.UnityEngine.Vector2(0, 0)

    title = CS.UnityEngine.GameObject("TxtTitle")
    title.transform.SetParent(panel_body.transform, False)
    title_tmp = title.AddComponent(CS.TMPro.TextMeshProUGUI)
    title_tmp.text = "设置"
    title_tmp.fontSize = 28
    title_tmp.color = CS.UnityEngine.Color(0.95, 0.95, 0.95, 1)
    title_tmp.alignment = CS.TMPro.TextAlignmentOptions.Center
    le_title = title.AddComponent(CS.UnityEngine.UI.LayoutElement)
    le_title.preferredHeight = 36

    row_count = 0
    for row_name, label, control in [
        ("RowMusic", "音乐", "Toggle"),
        ("RowSfx", "音效", "Slider"),
    ]:
        scope.add_child(panel_body, _make_row(row_name, label, control), preferred_h=40)
        row_count += 1

    panel_buttons = CS.UnityEngine.GameObject("PanelButtons")
    hlg_btns = panel_buttons.AddComponent(CS.UnityEngine.UI.HorizontalLayoutGroup)
    hlg_btns.spacing = 12
    hlg_btns.childControlWidth = True
    hlg_btns.childControlHeight = True
    hlg_btns.childForceExpandWidth = False
    hlg_btns.childForceExpandHeight = False
    scope.add_child(panel_body, panel_buttons, preferred_h=48)
    button_count = 0
    for btn_name, btn_label in [("BtnSave", "保存"), ("BtnCancel", "取消")]:
        btn = CS.UnityEngine.GameObject(btn_name)
        btn.AddComponent(CS.UnityEngine.UI.Image).color = CS.UnityEngine.Color(0.23, 0.51, 0.96, 1)
        btn.AddComponent(CS.UnityEngine.UI.Button)
        btn.GetComponent(CS.UnityEngine.RectTransform).sizeDelta = CS.UnityEngine.Vector2(0, 0)
        lbl = CS.UnityEngine.GameObject("TxtLabel")
        lbl.transform.SetParent(btn.transform, False)
        lbl_rt = lbl.AddComponent(CS.UnityEngine.RectTransform)
        lbl_rt.anchorMin = CS.UnityEngine.Vector2(0, 0)
        lbl_rt.anchorMax = CS.UnityEngine.Vector2(1, 1)
        lbl_rt.offsetMin = CS.UnityEngine.Vector2(0, 0)
        lbl_rt.offsetMax = CS.UnityEngine.Vector2(0, 0)
        lbl_tmp = lbl.AddComponent(CS.TMPro.TextMeshProUGUI)
        lbl_tmp.text = btn_label
        lbl_tmp.fontSize = 18
        lbl_tmp.color = CS.UnityEngine.Color(1, 1, 1, 1)
        lbl_tmp.alignment = CS.TMPro.TextAlignmentOptions.Center
        scope.add_child(panel_buttons, btn, preferred_w=120, preferred_h=40)
        button_count += 1

    CS.UnityEngine.Canvas.ForceUpdateCanvases()
    integ = scope.check_integrity(wnd, root_name)
    import assert_ui_scene_health as health
    size_gate = health.scan([root_name])
    save_result = unity.save_scene()
    return {
        "root_name": root_name,
        "row_count": row_count,
        "button_count": button_count,
        "has_vlg": vlg is not None,
        "integrity_ok": integ["ok"],
        "integrity": integ,
        "size_ok": size_gate.get("ok") is True,
        "size_gate": {
            "zero_size_count": size_gate.get("zero_size_count"),
            "missing_preferred_count": size_gate.get("missing_preferred_count"),
            "missing_preferred": size_gate.get("missing_preferred", [])[:8],
        },
        "save_scene_ok": save_result.get("success") is True,
    }


print(json.dumps(build_settings_form(), ensure_ascii=False))

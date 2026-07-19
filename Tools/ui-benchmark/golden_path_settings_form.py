"""golden-path benchmark：用原语拼 WndSettings 设置面板。

流程：create_layout_panel 建 WndSettings+PanelBody(VLG) → raw CS.* 建 Row*{TxtLabel+控件}
→ add_to_layout 挂 Row* 到 PanelBody → add_to_layout 挂 PanelButtons{BtnSave,BtnCancel}。
幂等：重复 exec 两次 find_objects("WndSettings")["count"] == 1。
"""
import json
import unity
from unity_bind import CS


def _make_row(row_name, label_text, control_kind):
    """建一行：TxtLabel + 控件（Toggle/Slider/TMP_InputField），返回 Row GameObject。"""
    row = CS.UnityEngine.GameObject(row_name)
    row.AddComponent(CS.UnityEngine.UI.HorizontalLayoutGroup).spacing = 12
    # Label
    label = CS.UnityEngine.GameObject("TxtLabel")
    label.transform.SetParent(row.transform, False)
    label_tmp = label.AddComponent(CS.TMPro.TextMeshProUGUI)
    label_tmp.text = label_text
    label_tmp.fontSize = 18
    label_tmp.color = CS.UnityEngine.Color(0.95, 0.95, 0.95, 1)
    label_tmp.alignment = CS.TMPro.TextAlignmentOptions.Left
    # 控件
    ctrl = CS.UnityEngine.GameObject(control_kind)
    ctrl.transform.SetParent(row.transform, False)
    if control_kind == "Toggle":
        ctrl.AddComponent(CS.UnityEngine.UI.Toggle)
    elif control_kind == "Slider":
        ctrl.AddComponent(CS.UnityEngine.UI.Slider)
    else:
        ctrl.AddComponent(CS.TMPro.TMP_InputField)
    return row


def add_to_layout(parent_name, child_name, preferred_w=None, preferred_h=None):
    """挂已存在的 child 到 parent（须含 LayoutGroup）下，不碰 anchor。"""
    parent = CS.UnityEngine.GameObject.Find(parent_name)
    if parent is None:
        raise RuntimeError(f"parent not found: {parent_name}")
    child = CS.UnityEngine.GameObject.Find(child_name)
    if child is None:
        raise RuntimeError(f"child not found: {child_name}")
    child.transform.SetParent(parent.transform, False)
    if preferred_w is not None or preferred_h is not None:
        le = child.GetComponent(CS.UnityEngine.UI.LayoutElement)
        if le is None:
            le = child.AddComponent(CS.UnityEngine.UI.LayoutElement)
        if preferred_w is not None:
            le.preferredWidth = preferred_w
        if preferred_h is not None:
            le.preferredHeight = preferred_h
    return {"parent": parent_name, "child": child_name}


def create_layout_panel(feature, title_text):
    """建 Wnd{feature} + PanelBody(VLG) + TxtTitle + PanelButtons 容器（不含按钮内容）。"""
    root_name = f"Wnd{feature}"
    color_surface = CS.UnityEngine.Color(0.15, 0.15, 0.18, 0.98)
    color_text_primary = CS.UnityEngine.Color(0.95, 0.95, 0.95, 1)
    unity.prepare_scene_object(root_name)
    canvas = CS.UnityEngine.GameObject.Find("Canvas")
    if canvas is None:
        raise RuntimeError("Canvas not found")
    wnd = CS.UnityEngine.GameObject(root_name)
    wnd.transform.SetParent(canvas.transform, False)
    wnd.AddComponent(CS.UnityEngine.UI.Image).color = color_surface
    wnd_rt = wnd.GetComponent(CS.UnityEngine.RectTransform)
    wnd_rt.anchorMin = CS.UnityEngine.Vector2(0.5, 0.5)
    wnd_rt.anchorMax = CS.UnityEngine.Vector2(0.5, 0.5)
    wnd_rt.pivot = CS.UnityEngine.Vector2(0.5, 0.5)
    wnd_rt.anchoredPosition = CS.UnityEngine.Vector2(0, 0)
    wnd_rt.sizeDelta = CS.UnityEngine.Vector2(420, 360)
    panel_body = CS.UnityEngine.GameObject("PanelBody")
    panel_body.transform.SetParent(wnd.transform, False)
    vlg = panel_body.AddComponent(CS.UnityEngine.UI.VerticalLayoutGroup)
    vlg.spacing = 16
    vlg.padding = CS.UnityEngine.RectOffset(24, 24, 24, 24)
    vlg.childAlignment = CS.UnityEngine.TextAnchor.UpperCenter
    body_rt = panel_body.GetComponent(CS.UnityEngine.RectTransform)
    body_rt.anchorMin = CS.UnityEngine.Vector2(0, 0)
    body_rt.anchorMax = CS.UnityEngine.Vector2(1, 1)
    body_rt.offsetMin = CS.UnityEngine.Vector2(0, 0)
    body_rt.offsetMax = CS.UnityEngine.Vector2(0, 0)
    title = CS.UnityEngine.GameObject("TxtTitle")
    title.transform.SetParent(panel_body.transform, False)
    title_tmp = title.AddComponent(CS.TMPro.TextMeshProUGUI)
    title_tmp.text = title_text
    title_tmp.fontSize = 28
    title_tmp.color = color_text_primary
    title_tmp.alignment = CS.TMPro.TextAlignmentOptions.Center
    return root_name, vlg


def build_settings_form():
    feature = "Settings"
    title_text = "设置"
    root_name, vlg = create_layout_panel(feature, title_text)

    # 建 2 行（音乐/音效）并挂到 PanelBody
    rows = [
        ("RowMusic", "音乐", "Toggle"),
        ("RowSfx", "音效", "Slider"),
    ]
    row_count = 0
    for row_name, label, control in rows:
        _make_row(row_name, label, control)
        add_to_layout("PanelBody", row_name, preferred_h=40)
        row_count += 1

    # 建按钮行容器 + 2 按钮，挂到 PanelBody
    panel_buttons = CS.UnityEngine.GameObject("PanelButtons")
    panel_buttons.AddComponent(CS.UnityEngine.UI.HorizontalLayoutGroup).spacing = 12
    add_to_layout("PanelBody", "PanelButtons", preferred_h=48)
    button_count = 0
    for btn_name, btn_label in [("BtnSave", "保存"), ("BtnCancel", "取消")]:
        btn = CS.UnityEngine.GameObject(btn_name)
        btn.AddComponent(CS.UnityEngine.UI.Image).color = CS.UnityEngine.Color(0.23, 0.51, 0.96, 1)
        btn.AddComponent(CS.UnityEngine.UI.Button)
        btn.GetComponent(CS.UnityEngine.RectTransform).sizeDelta = CS.UnityEngine.Vector2(120, 40)
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
        add_to_layout("PanelButtons", btn_name)
        button_count += 1

    save_result = unity.save_scene()
    return {
        "root_name": root_name,
        "row_count": row_count,
        "button_count": button_count,
        "has_vlg": vlg is not None,
        "save_scene_ok": save_result.get("success") is True,
    }


print(json.dumps(build_settings_form(), ensure_ascii=False))

# L1 E12：面板 + 输入框后，Input*/Btn*/Txt* 不得零宽/零高。
import json
import unity
from unity_bind import CS

PREFIXES = ("Input", "Btn")


def _create_layout_panel(feature, title_text):
    root_name = f"Wnd{feature}"
    color_surface = CS.UnityEngine.Color(0.15, 0.15, 0.18, 0.98)
    color_text_primary = CS.UnityEngine.Color(0.95, 0.95, 0.95, 1)
    color_accent = CS.UnityEngine.Color(0.23, 0.51, 0.96, 1)
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
    wnd_rt.sizeDelta = CS.UnityEngine.Vector2(400, 280)
    panel_body = CS.UnityEngine.GameObject("PanelBody")
    panel_body.transform.SetParent(wnd.transform, False)
    vlg = panel_body.AddComponent(CS.UnityEngine.UI.VerticalLayoutGroup)
    vlg.spacing = 16
    vlg.padding = CS.UnityEngine.RectOffset(24, 24, 24, 24)
    vlg.childAlignment = CS.UnityEngine.TextAnchor.UpperCenter
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
    title_tmp.text = title_text
    title_tmp.fontSize = 28
    title_tmp.color = color_text_primary
    title_tmp.alignment = CS.TMPro.TextAlignmentOptions.Center
    btn = CS.UnityEngine.GameObject("BtnSubmit")
    btn.transform.SetParent(panel_body.transform, False)
    btn.AddComponent(CS.UnityEngine.UI.Image).color = color_accent
    btn.AddComponent(CS.UnityEngine.UI.Button)
    btn_le = btn.AddComponent(CS.UnityEngine.UI.LayoutElement)
    btn_le.preferredWidth = 200
    btn_le.preferredHeight = 48
    label = CS.UnityEngine.GameObject("TxtLabel")
    label.transform.SetParent(btn.transform, False)
    lbl_rt = label.AddComponent(CS.UnityEngine.RectTransform)
    lbl_rt.anchorMin = CS.UnityEngine.Vector2(0, 0)
    lbl_rt.anchorMax = CS.UnityEngine.Vector2(1, 1)
    lbl_rt.offsetMin = CS.UnityEngine.Vector2(0, 0)
    lbl_rt.offsetMax = CS.UnityEngine.Vector2(0, 0)
    lbl_tmp = label.AddComponent(CS.TMPro.TextMeshProUGUI)
    lbl_tmp.text = "OK"
    lbl_tmp.fontSize = 18
    lbl_tmp.color = CS.UnityEngine.Color(1, 1, 1, 1)
    lbl_tmp.alignment = CS.TMPro.TextAlignmentOptions.Center
    return root_name


def _create_tmp_input_field(purpose, placeholder_text, password=False, parent_name="PanelBody", preferred_h=40):
    input_name = f"Input{purpose}"
    unity.prepare_scene_object(input_name)
    parent = CS.UnityEngine.GameObject.Find(parent_name)
    if parent is None:
        raise RuntimeError(f"parent not found: {parent_name}")
    inp = CS.UnityEngine.GameObject(input_name)
    inp.transform.SetParent(parent.transform, False)
    inp.AddComponent(CS.TMPro.TMP_InputField)
    le = inp.AddComponent(CS.UnityEngine.UI.LayoutElement)
    le.preferredHeight = preferred_h
    bg = CS.UnityEngine.GameObject("Bg")
    bg.transform.SetParent(inp.transform, False)
    bg.AddComponent(CS.UnityEngine.UI.Image).color = CS.UnityEngine.Color(0.22, 0.22, 0.26, 1)
    bg_rt = bg.GetComponent(CS.UnityEngine.RectTransform)
    bg_rt.anchorMin = CS.UnityEngine.Vector2(0, 0)
    bg_rt.anchorMax = CS.UnityEngine.Vector2(1, 1)
    bg_rt.offsetMin = CS.UnityEngine.Vector2(0, 0)
    bg_rt.offsetMax = CS.UnityEngine.Vector2(0, 0)
    ta = CS.UnityEngine.GameObject("TextArea")
    ta.transform.SetParent(inp.transform, False)
    ta_rt = ta.AddComponent(CS.UnityEngine.RectTransform)
    ta_rt.anchorMin = CS.UnityEngine.Vector2(0, 0)
    ta_rt.anchorMax = CS.UnityEngine.Vector2(1, 1)
    ta_rt.offsetMin = CS.UnityEngine.Vector2(8, 4)
    ta_rt.offsetMax = CS.UnityEngine.Vector2(-8, -4)
    ph = CS.UnityEngine.GameObject("Placeholder")
    ph.transform.SetParent(ta.transform, False)
    ph_tmp = ph.AddComponent(CS.TMPro.TextMeshProUGUI)
    ph_tmp.text = placeholder_text
    ph_tmp.fontSize = 16
    ph_tmp.color = CS.UnityEngine.Color(0.6, 0.6, 0.6, 1)
    ph_rt = ph.GetComponent(CS.UnityEngine.RectTransform)
    ph_rt.anchorMin = CS.UnityEngine.Vector2(0, 0)
    ph_rt.anchorMax = CS.UnityEngine.Vector2(1, 1)
    ph_rt.offsetMin = CS.UnityEngine.Vector2(0, 0)
    ph_rt.offsetMax = CS.UnityEngine.Vector2(0, 0)
    tx = CS.UnityEngine.GameObject("Text")
    tx.transform.SetParent(ta.transform, False)
    tx_tmp = tx.AddComponent(CS.TMPro.TextMeshProUGUI)
    tx_tmp.text = ""
    tx_tmp.fontSize = 16
    tx_tmp.color = CS.UnityEngine.Color(0.95, 0.95, 0.95, 1)
    tx_rt = tx.GetComponent(CS.UnityEngine.RectTransform)
    tx_rt.anchorMin = CS.UnityEngine.Vector2(0, 0)
    tx_rt.anchorMax = CS.UnityEngine.Vector2(1, 1)
    tx_rt.offsetMin = CS.UnityEngine.Vector2(0, 0)
    tx_rt.offsetMax = CS.UnityEngine.Vector2(0, 0)
    field = inp.GetComponent(CS.TMPro.TMP_InputField)
    field.textViewport = ta_rt
    field.textComponent = tx_tmp
    field.placeholder = ph_tmp
    field.targetGraphic = bg.GetComponent(CS.UnityEngine.UI.Image)
    if password:
        field.contentType = CS.TMPro.TMP_InputField.ContentType.Password
    return input_name


def _scan_zero(root_name):
    root = CS.UnityEngine.GameObject.Find(root_name)
    if root is None:
        raise RuntimeError(f"root not found: {root_name}")
    violations = []
    stack = [root.transform]
    while len(stack) > 0:
        t = stack.pop()
        for i in range(t.childCount):
            stack.append(t.GetChild(i))
        go = t.gameObject
        name = go.name
        if not any(name.startswith(p) for p in PREFIXES):
            continue
        rt = go.GetComponent(CS.UnityEngine.RectTransform)
        if rt is None:
            continue
        w = float(rt.rect.width)
        h = float(rt.rect.height)
        if w <= 1 or h <= 1:
            violations.append({"name": name, "w": round(w, 1), "h": round(h, 1)})
    return violations


root = _create_layout_panel("E12", "E12 Panel")
_create_tmp_input_field("Account", "账号", parent_name="PanelBody")
CS.UnityEngine.Canvas.ForceUpdateCanvases()
violations = _scan_zero(root)
print(json.dumps({
    "ok": len(violations) == 0,
    "root_name": root,
    "zero_width_violations": violations,
}, ensure_ascii=False))

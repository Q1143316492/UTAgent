# 与 editor-ui.md.txt create_* 代码块逐字同步；改模板先改 skill。
import json
import unity
from unity_bind import CS

def create_layout_panel(feature, title_text):
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
    grp_body = CS.UnityEngine.GameObject("GrpBody")
    grp_body.transform.SetParent(wnd.transform, False)
    vlg = grp_body.AddComponent(CS.UnityEngine.UI.VerticalLayoutGroup)
    vlg.spacing = 16
    vlg.padding = CS.UnityEngine.RectOffset(24, 24, 24, 24)
    vlg.childAlignment = CS.UnityEngine.TextAnchor.UpperCenter
    vlg.childControlWidth = True
    vlg.childControlHeight = True
    vlg.childForceExpandWidth = True
    vlg.childForceExpandHeight = False
    body_rt = grp_body.GetComponent(CS.UnityEngine.RectTransform)
    body_rt.anchorMin = CS.UnityEngine.Vector2(0, 0)
    body_rt.anchorMax = CS.UnityEngine.Vector2(1, 1)
    body_rt.offsetMin = CS.UnityEngine.Vector2(0, 0)
    body_rt.offsetMax = CS.UnityEngine.Vector2(0, 0)
    title = CS.UnityEngine.GameObject("TxtTitle")
    title.transform.SetParent(grp_body.transform, False)
    title_tmp = title.AddComponent(CS.TMPro.TextMeshProUGUI)
    title_tmp.text = title_text
    title_tmp.fontSize = 28
    title_tmp.color = color_text_primary
    title_tmp.alignment = CS.TMPro.TextAlignmentOptions.Center
    btn = CS.UnityEngine.GameObject("BtnSubmit")
    btn.transform.SetParent(grp_body.transform, False)
    btn.AddComponent(CS.UnityEngine.UI.Image).color = color_accent
    btn.AddComponent(CS.UnityEngine.UI.Button)
    btn.GetComponent(CS.UnityEngine.RectTransform).sizeDelta = CS.UnityEngine.Vector2(200, 48)
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
    save_result = unity.save_scene()
    return {
        "root_name": root_name,
        "has_vertical_layout_group": vlg is not None,
        "has_txt_title": title_tmp is not None,
        "has_accent_button": btn.GetComponent(CS.UnityEngine.UI.Button) is not None,
        "colors": {"surface": [round(color_surface.r, 3), round(color_surface.g, 3), round(color_surface.b, 3)], "text_primary": [round(color_text_primary.r, 3), round(color_text_primary.g, 3), round(color_text_primary.b, 3)], "accent": [round(color_accent.r, 3), round(color_accent.g, 3), round(color_accent.b, 3)]},
        "save_scene_ok": save_result.get("success") is True,
    }

feature = "Demo"
title_text = "Demo Panel"
print(json.dumps(create_layout_panel(feature, title_text), ensure_ascii=False))

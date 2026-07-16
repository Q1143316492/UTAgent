# 与 editor-ui.md.txt create_* 代码块逐字同步；改模板先改 skill。
import json
import unity
from unity_bind import CS

def create_tmp_input_field(purpose, placeholder_text, password=False, parent_name="GrpBody", preferred_h=40):
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
    CS.UnityEngine.Canvas.ForceUpdateCanvases()
    rt = inp.GetComponent(CS.UnityEngine.RectTransform)
    return {
        "input_name": input_name,
        "has_tmp_input": field is not None,
        "preferred_h": le.preferredHeight,
        "rect_w": round(float(rt.rect.width), 1),
        "rect_h": round(float(rt.rect.height), 1),
    }

purpose = "Account"
placeholder_text = "账号"
password = False
parent_name = "GrpBody"
print(json.dumps(create_tmp_input_field(purpose, placeholder_text, password, parent_name), ensure_ascii=False))

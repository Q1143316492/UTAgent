# 与 editor-ui.md.txt create_* 代码块逐字同步；改模板先改 skill。
import json
import unity
from unity_bind import CS

def create_tmp_button(purpose, label_text):
    btn_name = f"Btn{purpose}"
    unity.prepare_scene_object(btn_name)
    canvas = CS.UnityEngine.GameObject.Find("Canvas")
    if canvas is None:
        raise RuntimeError("Canvas not found")
    btn = CS.UnityEngine.GameObject(btn_name)
    btn.transform.SetParent(canvas.transform, False)
    btn.AddComponent(CS.UnityEngine.UI.Image).color = CS.UnityEngine.Color(0.23, 0.51, 0.96, 1)
    btn.AddComponent(CS.UnityEngine.UI.Button)
    btn.GetComponent(CS.UnityEngine.RectTransform).sizeDelta = CS.UnityEngine.Vector2(160, 48)
    label = CS.UnityEngine.GameObject("TxtLabel")
    label.transform.SetParent(btn.transform, False)
    lbl_rt = label.AddComponent(CS.UnityEngine.RectTransform)
    lbl_rt.anchorMin = CS.UnityEngine.Vector2(0, 0)
    lbl_rt.anchorMax = CS.UnityEngine.Vector2(1, 1)
    lbl_rt.offsetMin = CS.UnityEngine.Vector2(0, 0)
    lbl_rt.offsetMax = CS.UnityEngine.Vector2(0, 0)
    tmp = label.AddComponent(CS.TMPro.TextMeshProUGUI)
    tmp.text = label_text
    tmp.fontSize = 18
    tmp.color = CS.UnityEngine.Color(1, 1, 1, 1)
    tmp.alignment = CS.TMPro.TextAlignmentOptions.Center
    return {
        "btn_name": btn_name,
        "has_button": btn.GetComponent(CS.UnityEngine.UI.Button) is not None,
        "has_tmp_on_label": label.GetComponent(CS.TMPro.TextMeshProUGUI) is not None,
        "tmp_text": tmp.text,
    }

purpose = "Start"
label_text = "Start"
print(json.dumps(create_tmp_button(purpose, label_text), ensure_ascii=False))

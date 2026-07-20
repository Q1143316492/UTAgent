import json
import unity
from unity_bind import CS
CS.UnityEngine.Canvas.ForceUpdateCanvases()
names = ["WndSettings","PanelBody","TxtTitle","RowMusic","RowSfx","PanelBottom","BtnSave","BtnCancel"]
results = {}
for n in names:
    o = CS.UnityEngine.GameObject.Find(n)
    if o:
        r = o.GetComponent(CS.UnityEngine.RectTransform)
        w = round(float(r.rect.width), 1) if r else -1
        h = round(float(r.rect.height), 1) if r else -1
        results[n] = {"w": w, "h": h}
    else:
        results[n] = "NOT FOUND"
for rn in ["RowMusic", "RowSfx"]:
    row = CS.UnityEngine.GameObject.Find(rn)
    if row:
        togs = row.GetComponentsInChildren(CS.UnityEngine.UI.Toggle, True)
        results[rn + "_toggles"] = len(togs)
print(json.dumps({"variant": "A_ui_only", "results": results}, ensure_ascii=False))

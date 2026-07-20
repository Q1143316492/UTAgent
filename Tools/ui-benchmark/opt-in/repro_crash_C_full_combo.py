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
for bn in ["BtnSave", "BtnCancel"]:
    btn = CS.UnityEngine.GameObject.Find(bn)
    if btn:
        results[bn + "_hasButton"] = btn.GetComponent(CS.UnityEngine.UI.Button) is not None
print(json.dumps(results, ensure_ascii=False))
sr = unity.save_scene()
print("save:", json.dumps(sr, ensure_ascii=False))
print(json.dumps({"variant": "C_full_combo"}, ensure_ascii=False))

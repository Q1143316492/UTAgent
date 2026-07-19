# 离线校验 before-exec layout-control 正则语义（与 UTAgentRunner.BeforeExec.cs 对齐）
import re

s_add = re.compile(r"AddComponent\([^)]*(?:Vertical|Horizontal|Grid)?LayoutGroup")
s_w = re.compile(r"childControlWidth\s*=")
s_h = re.compile(r"childControlHeight\s*=")


def would_block(code: str) -> bool:
    if not s_add.search(code):
        return False
    return not (s_w.search(code) and s_h.search(code))


cases = [
    ("漏设", "g.AddComponent(CS.UnityEngine.UI.VerticalLayoutGroup)\nprint(1)", True),
    ("齐全", "vlg=g.AddComponent(CS.UnityEngine.UI.VerticalLayoutGroup)\nvlg.childControlWidth=True\nvlg.childControlHeight=True", False),
    ("无 Layout", "btn.AddComponent(CS.UnityEngine.UI.Button)\nprint(1)", False),
    ("仅 Width", "g.AddComponent(CS.UnityEngine.UI.HorizontalLayoutGroup)\nvlg.childControlWidth=True", True),
]

ok = True
for name, code, expect in cases:
    got = would_block(code)
    if got != expect:
        print(f"FAIL {name}: expect_block={expect} got={got}")
        ok = False
    else:
        print(f"PASS {name}: block={got}")

print("ok" if ok else "fail")
raise SystemExit(0 if ok else 1)

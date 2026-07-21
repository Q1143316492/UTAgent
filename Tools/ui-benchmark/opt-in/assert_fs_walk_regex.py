# 离线校验 fs-walk 正则（与 Editor/Core/UTAgentExecPolicy.cs 对齐）
import re

s_os_walk = re.compile(r"os\.walk\s*\(")
s_rglob = re.compile(r"\.rglob\s*\(")
s_glob_recursive = re.compile(r"glob\.(?:glob|iglob)\s*\([^)]*recursive\s*=\s*True")


def would_block(code: str) -> bool:
    return bool(
        s_os_walk.search(code)
        or s_rglob.search(code)
        or s_glob_recursive.search(code)
    )


cases = [
    ("os.walk", "import os\nfor r,d,f in os.walk('Assets'):\n print(f)", True),
    ("rglob", "from pathlib import Path\nlist(Path('Assets').rglob('*.prefab'))", True),
    ("glob recursive", "import glob\nglob.glob('Assets/**/*.png', recursive=True)", True),
    ("FindAssets ok", "CS.UnityEditor.AssetDatabase.FindAssets('t:Prefab')", False),
    ("listdir ok", "import os\nos.listdir('Assets/UTAgent')", False),
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

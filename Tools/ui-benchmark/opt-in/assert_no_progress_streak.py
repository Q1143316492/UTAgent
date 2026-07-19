# 离线镜像 after-tool 无进展判定（与 UTAgentRunner.AfterTool.NoProgress.cs 对齐）
import re

RE_RECON = re.compile(
    r"find_objects?\s*\(|get_hierarchy\s*\(|describe_go\s*\(|get_type_details\s*\(|"
    r"GameObject\.Find\s*\(|FindObjectsOfType|FindObjectOfType",
    re.I,
)
RE_MUTATION = re.compile(
    r"create_tmp_button|create_layout_panel|create_tmp_input_field|add_to_layout|add_free_child|"
    r"prepare_scene_object|destroy_object|destroy_all|create_primitive|DestroyImmediate|"
    r"AddComponent\s*\(|SetParent\s*\(|new\s+GameObject\s*\(|"
    r"\.color\s*=|childControlWidth\s*=|childControlHeight\s*=|"
    r"preferredWidth\s*=|preferredHeight\s*=|interactable\s*=|SaveScene",
    re.I,
)


def looks_like_mutation(code: str) -> bool:
    return bool(code and RE_MUTATION.search(code))


def looks_like_recon_only(code: str) -> bool:
    if not code or looks_like_mutation(code):
        return False
    return bool(RE_RECON.search(code))


def simulate(codes: list[str], enabled: bool, threshold: int = 3, max_inject: int = 1) -> int:
    """返回注入次数。"""
    if not enabled:
        return 0
    streak = 0
    injects = 0
    for code in codes:
        if looks_like_mutation(code):
            streak = 0
            continue
        if not looks_like_recon_only(code):
            continue
        streak += 1
        if streak >= threshold and injects < max_inject:
            injects += 1
            streak = 0
    return injects


cases = [
    ("关开关", ["unity.find_objects('A')"] * 5, False, 3, 0),
    ("三次纯find触发", ["unity.find_objects('A')"] * 3, True, 3, 1),
    ("两次不够", ["unity.find_objects('A')"] * 2, True, 3, 0),
    ("变更清零", [
        "unity.find_objects('A')",
        "unity.find_objects('B')",
        "g.AddComponent(CS.UnityEngine.UI.Image)",
        "unity.find_objects('C')",
        "unity.find_objects('D')",
    ], True, 3, 0),
    ("find含mutate不算recon", ["unity.find_objects('A'); img.color = c"] * 3, True, 3, 0),
]

ok = True
for name, codes, enabled, thr, expect in cases:
    got = simulate(codes, enabled, thr)
    if got != expect:
        print(f"FAIL {name}: expect_inject={expect} got={got}")
        ok = False
    else:
        print(f"PASS {name}: inject={got}")

print("ok" if ok else "fail")
raise SystemExit(0 if ok else 1)

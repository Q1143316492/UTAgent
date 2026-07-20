"""把 assert_ui_scene_health JSON 格式化为 L2 纠偏 chat 的 user 文本。

用法:
  python format_health_remediation_prompt.py path/to/health.json
  echo '{...}' | python format_health_remediation_prompt.py -

stdout 仅输出 prompt 正文（UTF-8）。
"""
from __future__ import annotations

import json
import sys

MAX_ITEMS = 12


def _trunc_list(items, n=MAX_ITEMS):
    if not isinstance(items, list):
        return []
    return items[:n]


def format_prompt(health: dict, root_hint: str = "") -> str:
    root = root_hint.strip()
    if not root and isinstance(health.get("integrity"), list) and health["integrity"]:
        root = str(health["integrity"][0].get("root") or "")
    if not root:
        root = "目标 Wnd* 面板"

    zeros = _trunc_list(health.get("zero_size") or [])
    miss = _trunc_list(health.get("missing_preferred") or [])
    outside = _trunc_list(health.get("outside_canvas") or [])
    non_ascii = _trunc_list(health.get("non_ascii_names") or [])
    canvas_direct = _trunc_list(health.get("canvas_direct") or [])

    integ_bits = []
    for block in health.get("integrity") or []:
        if isinstance(block, dict) and not block.get("ok", True):
            integ_bits.append({
                "root": block.get("root"),
                "missing": block.get("missing"),
                "error": block.get("error"),
            })

    payload = {
        "root": root,
        "outside_canvas": outside,
        "zero_size": zeros,
        "missing_preferred": miss,
        "non_ascii_names": non_ascii,
        "canvas_direct": canvas_direct,
        "integrity_failures": integ_bits,
        "counts": {
            "outside": health.get("outside_canvas_count"),
            "zero": health.get("zero_size_count"),
            "miss_pref": health.get("missing_preferred_count"),
            "non_ascii": health.get("non_ascii_name_count"),
        },
    }

    body = json.dumps(payload, ensure_ascii=False, indent=2)
    return (
        f"{root} 已在场景中。健康扫描失败，请外科式修补（若未 loadSkill(\"editor-ui\") 请先 load）。\n"
        "硬约束：\n"
        "1) 禁止 Destroy/prepare_scene_object 删掉整棵面板根后重建；禁止整页重写。\n"
        "2) 只修下列失败项对应节点（LayoutElement.preferredHeight/Width、childControl、挂载父节点等）。\n"
        "3) 父 Layout 若 childControlHeight=true 且 childForceExpandHeight=false，子 Txt*/Btn* 必须设 preferredHeight>0。\n"
        "4) Canvas.ForceUpdateCanvases() 后 print 相关节点 rect.width/height，全部必须 >1。\n"
        "5) 不要把完整面板脚本当答案粘贴；不要无必要 save_scene。\n"
        "失败摘要 JSON：\n"
        f"{body}\n"
    )


def main():
    if len(sys.argv) < 2:
        print("usage: format_health_remediation_prompt.py <health.json|->", file=sys.stderr)
        sys.exit(2)
    src = sys.argv[1]
    root_hint = sys.argv[2] if len(sys.argv) > 2 else ""
    if src == "-":
        raw = sys.stdin.read()
    else:
        with open(src, "r", encoding="utf-8-sig") as f:
            raw = f.read()
    health = json.loads(raw)
    if not isinstance(health, dict):
        raise SystemExit("health JSON must be an object")
    sys.stdout.write(format_prompt(health, root_hint))


if __name__ == "__main__":
    main()

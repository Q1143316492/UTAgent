"""L1 helper：E04 命名断言——WndDemo 子树无 *Go / 裸 Button。"""
import json
import unity

h = unity.get_hierarchy("WndDemo", depth=4, echo=False)
names = []


def walk(node):
    if isinstance(node, dict):
        names.append(node.get("name", ""))
        for c in node.get("children", []):
            walk(c)


walk(h.get("hierarchy", {}))

bad = [n for n in names if (isinstance(n, str) and (n.endswith("Go") or n == "Button"))]
print(json.dumps({"bad": bad, "sample": names[:5]}, ensure_ascii=False))

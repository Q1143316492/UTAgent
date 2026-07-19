"""将场景中指定 Wnd* 另存为 TestFixtures/UIPanels 预制体。

请求文件：Assets/UTAgent/Tools/ui-benchmark/.tmp/_export_root.txt（一行根名）
或同次 exec 预置 _EXPORT_ROOT。
导出前做子树完整性校验；失败不覆盖已有 prefab。
"""
import json
import os
import sys

from unity_bind import CS

_bench = os.path.join("Assets", "UTAgent", "Tools", "ui-benchmark")
for p in (os.path.abspath(_bench), os.path.abspath(".")):
    if os.path.isdir(p) and p not in sys.path:
        sys.path.insert(0, p)
import ui_panel_scope as scope  # noqa: E402
import assert_ui_scene_health as health  # noqa: E402


DEFAULT_DIR = "Assets/UTAgent/TestFixtures/UIPanels"
REQUEST_FILE = "Assets/UTAgent/Tools/ui-benchmark/.tmp/_export_root.txt"


def _resolve_root():
    g = globals().get("_EXPORT_ROOT")
    if isinstance(g, str) and g.strip():
        return g.strip()
    env = os.environ.get("UTAGENT_EXPORT_ROOT", "").strip()
    if env:
        return env
    if os.path.isfile(REQUEST_FILE):
        with open(REQUEST_FILE, "r", encoding="utf-8") as f:
            lines = f.read().strip().splitlines()
        if lines:
            return lines[0].strip()
    return ""


def export_root(root_name, out_dir):
    go = CS.UnityEngine.GameObject.Find(root_name)
    integ = scope.check_integrity(go, root_name)
    if not integ.get("ok"):
        return {
            "ok": False,
            "error": "integrity check failed",
            "integrity": integ,
            "path": None,
            "overwritten": False,
        }
    CS.UnityEngine.Canvas.ForceUpdateCanvases()
    size_gate = health.scan([root_name])
    if not size_gate.get("ok"):
        return {
            "ok": False,
            "error": "size gate failed",
            "integrity": integ,
            "size_gate": {
                "zero_size_count": size_gate.get("zero_size_count"),
                "missing_preferred_count": size_gate.get("missing_preferred_count"),
                "zero_size": size_gate.get("zero_size", [])[:12],
                "missing_preferred": size_gate.get("missing_preferred", [])[:12],
            },
            "path": None,
            "overwritten": False,
        }
    if not os.path.isdir(out_dir):
        os.makedirs(out_dir, exist_ok=True)
    path = f"{out_dir.rstrip('/')}/{root_name}.prefab".replace("\\", "/")
    prefab = CS.UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, path)
    if prefab is None:
        return {"ok": False, "error": "SaveAsPrefabAsset returned null", "path": path, "overwritten": False}
    CS.UnityEditor.AssetDatabase.SaveAssets()
    CS.UnityEditor.AssetDatabase.Refresh()
    return {
        "ok": True,
        "root": root_name,
        "path": path,
        "integrity": integ,
        "size_ok": True,
        "overwritten": True,
    }


root = _resolve_root()
out_dir = os.environ.get("UTAGENT_EXPORT_DIR", DEFAULT_DIR).strip() or DEFAULT_DIR
if not root:
    print(json.dumps({
        "ok": False,
        "error": "root required: set _EXPORT_ROOT or " + REQUEST_FILE,
    }, ensure_ascii=False))
else:
    result = export_root(root, out_dir)
    if os.path.isfile(REQUEST_FILE):
        try:
            os.remove(REQUEST_FILE)
        except OSError:
            pass
    print(json.dumps(result, ensure_ascii=False))

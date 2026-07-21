# 官方薄入口：CLI / skill assert / benchmark 请跑本文件（≪ CodeSizeLimit）。
# 实现仍在 assert_ui_scene_health.py。
import importlib
import os
import sys

_bench = os.path.abspath(os.path.join("Assets", "UTAgent", "Tools", "ui-benchmark"))
if _bench not in sys.path:
    sys.path.insert(0, _bench)

import ui_panel_scope as scope
import assert_ui_scene_health as health

importlib.reload(scope)
importlib.reload(health)
health.main()

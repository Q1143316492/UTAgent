"""Python 窗口管理器，对标 GameCore.WindowManager。"""

import json
import sys

from unity.ui.core.registry import REGISTRY


def _wndmgr_bridge():
    """从 sys.modules 取当前 WndMgr 桥。"""
    name = "_wndmgr_bridge"
    mod = sys.modules.get(name)
    if mod is not None:
        return mod
    return __import__(name)


class WindowManager:
    """已打开窗口的单例管理。"""

    _instance = None

    def __init__(self):
        self._opened: dict[str, int] = {}

    @classmethod
    def get(cls):
        if cls._instance is None:
            cls._instance = cls()
        return cls._instance

    def clear(self):
        self._opened.clear()

    def open(self, name, args=None):
        entry = REGISTRY.get(name)
        if entry is None:
            raise RuntimeError(f"未注册窗口：{name}")

        payload = {
            "name": name,
            "module": entry["module"],
            "class": entry["class"],
            "prefab": entry["prefab"],
            "layer": entry.get("layer", "Menu"),
        }
        if args is not None:
            payload["args"] = args

        raw = _wndmgr_bridge().Open(json.dumps(payload, ensure_ascii=False))
        data = json.loads(raw)
        if not data.get("success"):
            msg = data.get("message", raw)
            raise RuntimeError(f"WindowManager.open({name}) 失败: {msg}")

        handle = data.get("handle")
        if isinstance(handle, int):
            self._opened[name] = handle
        return data

    def close(self, name):
        raw = _wndmgr_bridge().Close(json.dumps({"name": name}, ensure_ascii=False))
        data = json.loads(raw)
        if not data.get("success"):
            msg = data.get("message", raw)
            raise RuntimeError(f"WindowManager.close({name}) 失败: {msg}")
        self._opened.pop(name, None)
        return data

    def is_open(self, name):
        raw = _wndmgr_bridge().IsOpen(json.dumps({"name": name}, ensure_ascii=False))
        data = json.loads(raw)
        if not data.get("success"):
            return False
        return bool(data.get("open"))

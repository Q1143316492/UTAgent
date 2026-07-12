"""unity.ui.core 基类：Unity 对象句柄与 Invoke 封装。"""

import json
import sys


def _ui_bridge():
    """从 sys.modules 取当前 UI 桥。"""
    name = "_ui_bridge"
    mod = sys.modules.get(name)
    if mod is not None:
        return mod
    return __import__(name)


class UnityObject:
    """Unity 对象包装（handle = InstanceID）。"""

    def __init__(self, handle, type_name):
        if not isinstance(handle, int):
            raise TypeError("handle 必须是 int（Unity InstanceID）")
        self._handle = handle
        self._type = type_name


def _call_ui_bridge(type_name, member, args_json):
    """调用 C# UTAgentPythonBridge UI 域（pythonnet 下避免使用 Invoke 这个名字）。"""
    for attr in ("InvokeMember", "invoke_member"):
        fn = getattr(_ui_bridge(), attr, None)
        if fn is not None:
            return fn(type_name, member, args_json)
    raise RuntimeError("_ui_bridge 缺少 InvokeMember")


def _invoke(type_name, member, **kwargs):
    try:
        raw = _call_ui_bridge(type_name, member, json.dumps(kwargs, ensure_ascii=False))
    except Exception as e:
        raise RuntimeError(f"{type_name}.{member} 桥接异常: {e}") from e

    try:
        data = json.loads(raw)
    except json.JSONDecodeError as e:
        raise RuntimeError(f"{type_name}.{member} 响应非 JSON: {raw!r}") from e

    if not data.get("success"):
        msg = data.get("message", raw)
        raise RuntimeError(f"{type_name}.{member} 失败: {msg}")
    return data


def _wrap(data):
    widget_type = data.get("type")
    handle = data["handle"]
    if widget_type == "Image":
        from unity.ui.core.img import Image

        return Image(handle, widget_type)
    if widget_type == "Text":
        from unity.ui.core.text import Text

        return Text(handle, widget_type)
    if widget_type == "WndBase":
        from unity.ui.core.wnd_base import WndBase

        return WndBase(handle, widget_type)
    raise RuntimeError(f"未知控件类型: {widget_type}")

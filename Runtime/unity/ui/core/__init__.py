"""UI 框架基础设施（WndBase / 控件 / WindowManager）。"""

from unity.ui.core._base import UnityObject
from unity.ui.core.img import Image
from unity.ui.core.text import Text

__all__ = ["UnityObject", "WndBase", "Image", "Text", "WindowManager"]


def __getattr__(name):
    """延迟导入，避免 wnd_mgr / wnd_base 与 unity.core.behaviour 循环依赖。"""
    if name == "WndBase":
        from unity.ui.core.wnd_base import WndBase

        return WndBase
    if name == "WindowManager":
        from unity.ui.core.wnd_mgr import WindowManager

        return WindowManager
    raise AttributeError(f"module {__name__!r} has no attribute {name!r}")

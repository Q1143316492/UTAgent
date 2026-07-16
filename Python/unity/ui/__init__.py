"""Unity UI 包。框架见 unity.ui.core；业务面板脚本在 Assets/UTAgent/Scripts/。"""

from unity.ui.core.img import Image
from unity.ui.core.text import Text

__all__ = ["WndBase", "Image", "Text", "WindowManager"]


def __getattr__(name):
    """延迟导入 WndBase / WindowManager，避免与 unity.core.behaviour 循环依赖。"""
    if name == "WndBase":
        from unity.ui.core.wnd_base import WndBase

        return WndBase
    if name == "WindowManager":
        from unity.ui.core.wnd_mgr import WindowManager

        return WindowManager
    raise AttributeError(f"module {__name__!r} has no attribute {name!r}")

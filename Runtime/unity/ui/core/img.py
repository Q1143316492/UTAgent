"""Image 控件包装。"""

from unity.ui.core._base import UnityObject, _invoke


class Image(UnityObject):
    """UI 图片控件（UGUI Image）。"""

    def set_visible(self, visible=True):
        """设置控件 GameObject 的 active 状态。"""
        _invoke("Image", "set_visible", handle=self._handle, visible=bool(visible))
        return self

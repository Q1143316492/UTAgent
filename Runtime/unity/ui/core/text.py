"""Text 控件包装。"""

from unity.ui.core._base import UnityObject, _invoke


class Text(UnityObject):
    """UI 文本控件（UGUI Text 或 TextMeshProUGUI）。"""

    def set_text(self, text):
        """设置显示文案。"""
        _invoke("Text", "set_text", handle=self._handle, text=str(text))
        return self

    def set_visible(self, visible=True):
        """设置控件 GameObject 的 active 状态。"""
        _invoke("Text", "set_visible", handle=self._handle, visible=bool(visible))
        return self

"""面板根节点包装。"""

from unity.core.behaviour import UnityBehaviour
from unity.ui.core._base import _invoke, _wrap


class WndBase(UnityBehaviour):
    """UI 面板（组件式：UTAgentWindowHost；命令式：WndBase.get）。"""

    def on_init(self, args=None):
        """对齐 WindowBase.OnInit，args 为 dict。"""
        pass

    def on_show(self):
        pass

    def on_hide(self):
        pass

    def on_release(self):
        pass

    @classmethod
    def get(cls, name):
        """按名获取面板根节点（Agent / Editor 命令式）。"""
        data = _invoke("WndBase", "get", name=name)
        return cls(data["handle"], "WndBase")

    def get_widget(self, name):
        """按名或路径获取子控件（根为 self._handle）。

        - 简单名：仅当全树唯一时匹配；重名会报错并列出可选路径。
        - 路径：用 ``/`` 分隔，如 ``CreatePanel/RoleInfo/Desc``。
        """
        data = _invoke("WndBase", "get_widget", handle=self._handle, name=name)
        return _wrap(data)

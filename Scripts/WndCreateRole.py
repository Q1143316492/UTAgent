"""创角面板 — Python 生命周期（对标 C# WndCreateRole）。"""

import unity
from unity.ui.core import WndBase


class WndCreateRole(WndBase):
    """Resources/Panel/CreateRole/WndCreateRole 的 Python 逻辑。"""

    def on_init(self, args=None):
        unity.log("WndCreateRole(Python): on_init")

    def on_show(self):
        unity.log("WndCreateRole(Python): on_show — 创角面板已由 Python App 打开")
        try:
            txt = self.get_widget("AAAD")
            unity.log(f"WndCreateRole(Python): 获取到 Desc 控件: {txt}")
            txt.set_text("创角")
        except Exception as e:
            unity.log_error(f"WndCreateRole(Python): on_show 异常: {e}")

    def on_hide(self):
        unity.log("WndCreateRole(Python): on_hide")

    def on_release(self):
        unity.log("WndCreateRole(Python): on_release")

"""Unity / 组件生命周期 Python 基类。"""

from unity.ui.core._base import UnityObject


class UnityBehaviour(UnityObject):
    """挂在 GameObject 上的 Python 逻辑基类。"""

    def awake(self):
        pass

    def start(self):
        pass

    def on_enable(self):
        pass

    def on_disable(self):
        pass

    def update(self):
        pass

    def on_destroy(self):
        pass

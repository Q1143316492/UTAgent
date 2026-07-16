"""Unity 对象句柄基类（与 UI 层解耦，供 core.behaviour / ui.core 共用）。"""


class UnityObject:
    """Unity 对象包装（handle = InstanceID）。"""

    def __init__(self, handle, type_name):
        if not isinstance(handle, int):
            raise TypeError("handle 必须是 int（Unity InstanceID）")
        self._handle = handle
        self._type = type_name

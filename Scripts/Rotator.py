"""Cube 旋转示例 — 挂 UTAgentBehaviour，dispatchUpdate 可开。"""

import unity
from unity.core import UnityBehaviour


class Rotator(UnityBehaviour):
    def start(self):
        unity.log("Rotator.start")

    def update(self):
        pass

"""窗口名 → 模块 / 预制体 / 层级注册表。"""

REGISTRY = {
    "WndCreateRole": {
        "module": "UTAgent/Scripts/WndCreateRole.py",
        "class": "WndCreateRole",
        "prefab": "Panel/CreateRole/WndCreateRole",
        "layer": "Menu",
    },
}

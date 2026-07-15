"""unity 子模块共享内部工具。

子模块 SHALL NOT ``import unity``（避免循环）；通过本模块取桥与校验 helper。
"""

import json
import sys


def _bridge():
    """从 sys.modules 取当前 C# 桥，避免 Initialize 后模块级缓存过期。"""
    name = "_unity_bridge"
    mod = sys.modules.get(name)
    if mod is not None:
        return mod
    return __import__(name)


def _agent_echo(label, payload):
    """将 L1 动词结果写入 execPython stdout（Agent tool 结果可见）。unity.log 不会。"""
    try:
        text = json.dumps(payload, ensure_ascii=False)
    except TypeError:
        text = str(payload)
    if len(text) > 4000:
        text = text[:4000] + f"... ({len(text)} chars truncated)"
    print(f"[{label}] {text}")


def _validate_position(position, func_name):
    if not isinstance(position, (tuple, list)) or len(position) != 3:
        raise ValueError(f"{func_name}: 'position' 必须是 3 元组 (x,y,z)")


def _validate_euler(euler, func_name):
    if not isinstance(euler, (tuple, list)) or len(euler) != 3:
        raise ValueError(f"{func_name}: 'euler' 必须是 3 元组 (rx,ry,rz)")
    for i, v in enumerate(euler):
        if not isinstance(v, (int, float)):
            raise ValueError(f"{func_name}: 'euler' 分量必须是数值（index {i} got {v}）")


def _validate_dims(width, height, func_name):
    if not isinstance(width, int) or not isinstance(height, int):
        raise ValueError(f"{func_name}: 尺寸必须是整数")
    if width < 64 or width > 1920 or height < 64 or height > 1080:
        raise ValueError(f"{func_name}: 尺寸必须在 64-1920x1080 之间")

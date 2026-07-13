"""unity 模块：给 LLM 直接调用的 Unity 任务动词层（L1）。

薄翻译层：参数校验 → `_unity_bridge` → JSON 解析。
L2 自省：`list_editor_namespaces` / `get_type_details`。
L3 任意白名单 API：`from unity_bind import CS`（见 python-interop skill）。
"""

import json
import sys


def _agent_echo(label, payload):
    """将 L1 动词结果写入 execPython stdout（Agent tool 结果可见）。unity.log 不会。"""
    try:
        text = json.dumps(payload, ensure_ascii=False)
    except TypeError:
        text = str(payload)
    if len(text) > 4000:
        text = text[:4000] + f"... ({len(text)} chars truncated)"
    print(f"[{label}] {text}")


def _bridge():
    """从 sys.modules 取当前 C# 桥，避免 Initialize 后模块级缓存过期。"""
    name = "_unity_bridge"
    mod = sys.modules.get(name)
    if mod is not None:
        return mod
    return __import__(name)


def list_namespaces(filter=""):
    """列出 C# 命名空间。filter 为逗号分隔前缀（如 "UnityEngine,TMPro"）；空串返回全部。

    返回 {"namespaces": [...]}。默认优先用 list_editor_namespaces()。
    """
    if filter is not None and not isinstance(filter, str):
        raise ValueError("list_namespaces: 'filter' 必须是字符串")
    return json.loads(_bridge().ListNamespaces(filter or ""))


def list_editor_namespaces():
    """列出 Editor Agent 常用命名空间（已过滤 Plastic SCM 等噪声）。

    返回 {"namespaces": ["UnityEngine", "UnityEngine.UI", "TMPro", "UnityEditor", ...]}
    """
    return json.loads(_bridge().ListEditorNamespaces())


def list_types_in_namespace(namespaces):
    """列出一个或多个命名空间下的所有公共类型（仅名字/种类，不含成员）。

    namespaces: 逗号分隔，如 "UnityEngine,UnityEngine.UI"
    返回 {"types": [{"name": "GameObject", "fullName": "UnityEngine.GameObject", "kind": "class"}, ...]}
    要看成员细节用 get_type_details。
    """
    if not isinstance(namespaces, str) or not namespaces.strip():
        raise ValueError(
            "list_types_in_namespace: 'namespaces' 必须是非空字符串（逗号分隔），"
            "如 'UnityEngine,UnityEngine.UI'。读 help(unity.list_types_in_namespace)"
        )
    return json.loads(_bridge().ListTypesInNamespace(namespaces))


def get_type_details(type_names):
    """查一个或多个 C# 类型的全部公共成员（属性/方法/字段/接口/基类/枚举值）。

    type_names: 逗号分隔的全限定名，如 "UnityEngine.Transform,UnityEngine.GameObject"
    返回 {"types": [...]}。
    """
    if not isinstance(type_names, str) or not type_names.strip():
        raise ValueError(
            "get_type_details: 'type_names' 必须是非空字符串（逗号分隔的全限定名），"
            "如 'UnityEngine.Transform,UnityEngine.GameObject'。读 help(unity.get_type_details)"
        )
    result = json.loads(_bridge().GetTypeDetails(type_names))
    _agent_echo("get_type_details", result)
    return result


def get_logs(count=20, log_type="all"):
    """获取最近的 Unity Console 日志条目。

    count: 1-50 整数，默认 20
    log_type: "all" / "error" / "warning" / "log"，默认 "all"
    返回 [{"timestamp": ..., "type": ..., "message": ..., "stackTrace": ...}, ...]
    """
    if not isinstance(count, int) or count < 1 or count > 50:
        raise ValueError(f"get_logs: 'count' 必须是 1-50 的整数（got {count}）。读 help(unity.get_logs)")
    if log_type not in ("all", "error", "warning", "log"):
        raise ValueError(
            f"get_logs: 'log_type' 必须是 all/error/warning/log（got {log_type}）。读 help(unity.get_logs)"
        )
    return json.loads(_bridge().GetRecentLogs(count, log_type))


def get_log_summary():
    """获取按类型分组的日志计数。

    返回 {"log": int, "warning": int, "error": int, "total": int}
    """
    return json.loads(_bridge().GetLogSummary())


def capture_screenshot(max_width=512, max_height=512):
    """截取 Unity Game 视图。成功时图像自动作为视觉内容回给 LLM（多模态）。

    max_width/max_height: 64-1920/64-1080 整数，默认 512
    返回 {"success": bool, "message": str, "__image": {"base64": ..., "mediaType": "image/png"}?}
    """
    _validate_dims(max_width, max_height, "capture_screenshot")
    return json.loads(_bridge().CaptureScreenshot(max_width, max_height))


def create_cube(name="Cube", position=(0, 0, 0)):
    """创建一个立方体 GameObject。

    name: 对象名
    position: (x, y, z) 世界坐标
    返回 {"success": True, "name": str, "instanceId": int}
    """
    _validate_position(position, "create_cube")
    return json.loads(_bridge().CreateCube(name, position[0], position[1], position[2]))


def create_primitive(prim_type, name, position=(0, 0, 0)):
    """创建指定类型的基础几何体。

    prim_type: "Cube"/"Sphere"/"Capsule"/"Cylinder"/"Plane"/"Quad"
    返回同 create_cube。
    """
    valid = ("Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad")
    if prim_type not in valid:
        raise ValueError(f"create_primitive: 'prim_type' 必须是 {valid}（got {prim_type}）")
    _validate_position(position, "create_primitive")
    return json.loads(
        _bridge().CreatePrimitive(prim_type, name, position[0], position[1], position[2])
    )


def find_object(name, echo=True):
    """按名查找一个激活的 GameObject（GameObject.Find）。

    重名时只返回其中一个。要枚举或清理全部同名对象用 find_objects / destroy_all_objects。
    返回 {"success": bool, "name": str, "instanceId": int?, "active": bool?}
    找不到返回 {"success": False}。
    """
    if not isinstance(name, str) or not name.strip():
        raise ValueError("find_object: 'name' 必须是非空字符串")
    result = json.loads(_bridge().FindObject(name))
    if echo:
        _agent_echo("find_object", result)
    return result


def find_objects(name, echo=True):
    """在当前活动场景中按名查找所有 GameObject（遍历层级，含非激活对象）。

    返回 {"success": True, "count": int, "objects": [...]}（用 result["count"]，不是 len(result)）。
    """
    if not isinstance(name, str) or not name.strip():
        raise ValueError("find_objects: 'name' 必须是非空字符串")
    result = json.loads(_bridge().FindObjects(name))
    if echo:
        _agent_echo("find_objects", result)
    return result


def get_hierarchy(name=None, depth=0, echo=True):
    """获取 GameObject 层次树。

    name: 根对象名，None/空 = 当前场景所有根对象
    depth: 最大遍历深度，0 = 无限。超限节点用 childCount 代替 children
    返回 {"success": True, "hierarchy": [...]}；各节点 components 为
    {"shortName": "...", "fullName": "Namespace.Type"} 对象数组。
    """
    result = json.loads(_bridge().GetHierarchy(name or "", depth))
    if echo:
        _agent_echo("get_hierarchy", result)
    return result


def get_position(name):
    """获取某 GameObject 的世界坐标。

    返回 (x, y, z) 三元组。
    """
    result = _bridge().GetPosition(name)
    # C# 返回 "(x,y,z)"，解析为 tuple
    inner = result.strip("()")
    parts = inner.split(",")
    if len(parts) != 3:
        raise RuntimeError(f"get_position: 无法解析返回值 {result}")
    return (float(parts[0]), float(parts[1]), float(parts[2]))


def set_position(name, position=(0, 0, 0)):
    """设置某 GameObject 的世界坐标。"""
    _validate_position(position, "set_position")
    return json.loads(_bridge().SetPosition(name, position[0], position[1], position[2]))


def get_rotation(name):
    """获取某 GameObject 的世界旋转欧拉角（度）。

    返回 {"success": True, "euler": {"x": float, "y": float, "z": float}}
    """
    if not isinstance(name, str) or not name.strip():
        raise ValueError("get_rotation: 'name' 必须是非空字符串")
    return json.loads(_bridge().GetRotation(name))


def set_rotation(name, euler=(0, 0, 0)):
    """设置某 GameObject 的世界旋转（欧拉角，度）。"""
    _validate_euler(euler, "set_rotation")
    return json.loads(_bridge().SetRotation(name, euler[0], euler[1], euler[2]))


def get_scale(name):
    """获取某 GameObject 的本地缩放。

    返回 {"success": True, "scale": {"x": float, "y": float, "z": float}}
    """
    if not isinstance(name, str) or not name.strip():
        raise ValueError("get_scale: 'name' 必须是非空字符串")
    return json.loads(_bridge().GetScale(name))


def set_scale(name, scale=(1, 1, 1)):
    """设置某 GameObject 的本地缩放。"""
    _validate_position(scale, "set_scale")
    return json.loads(_bridge().SetScale(name, scale[0], scale[1], scale[2]))


def move_object(name, direction=(1, 0, 0), distance=1.0):
    """沿指定方向平移某 GameObject（世界空间，方向向量会归一化）。

    direction: (x, y, z) 方向向量
    distance: 移动距离（世界单位）
    """
    _validate_position(direction, "move_object")
    if not isinstance(distance, (int, float)) or distance < 0:
        raise ValueError(
            f"move_object: 'distance' 必须是非负数值（got {distance}）。读 help(unity.move_object)"
        )
    return json.loads(
        _bridge().MoveObject(name, direction[0], direction[1], direction[2], float(distance))
    )


def rotate_object(name, axis="y", angle=90):
    """绕本地轴旋转某 GameObject。

    axis: "x" / "y" / "z"
    angle: 旋转角度（度）
    """
    valid = ("x", "y", "z")
    if axis not in valid:
        raise ValueError(
            f"rotate_object: 'axis' 必须是 {valid}（got {axis}）。读 help(unity.rotate_object)"
        )
    if not isinstance(angle, (int, float)):
        raise ValueError(f"rotate_object: 'angle' 必须是数值（got {angle}）")
    return json.loads(_bridge().RotateObject(name, axis, float(angle)))


def look_at(name, target):
    """使某 GameObject 朝向目标。

    target: 另一对象名字符串，或世界坐标 (x, y, z) 三元组
    """
    if not isinstance(name, str) or not name.strip():
        raise ValueError("look_at: 'name' 必须是非空字符串")
    if isinstance(target, str):
        if not target.strip():
            raise ValueError("look_at: 'target' 对象名必须是非空字符串")
        return json.loads(_bridge().LookAt(name, target, 0, 0, 0, False))
    _validate_position(target, "look_at")
    return json.loads(
        _bridge().LookAt(name, "", target[0], target[1], target[2], True)
    )


def destroy_object(name):
    """销毁一个 GameObject（Edit Mode 安全：内部使用 DestroyImmediate）。

    重名时只销毁 GameObject.Find 找到的那一个。要清空全部同名对象用 destroy_all_objects。
    返回 {"success": True, "destroyed": str}
    """
    if not isinstance(name, str) or not name.strip():
        raise ValueError("destroy_object: 'name' 必须是非空字符串")
    return json.loads(_bridge().DestroyObject(name))


def destroy_all_objects(name):
    """销毁当前活动场景中所有同名 GameObject（Edit Mode 安全）。

    场景创建/重建前的幂等清理入口；Unity 允许重名，失败重试前应先调用本函数。
    返回 {"success": True, "destroyedCount": int, "destroyedInstanceIds": [int, ...]}
    """
    if not isinstance(name, str) or not name.strip():
        raise ValueError("destroy_all_objects: 'name' 必须是非空字符串")
    return json.loads(_bridge().DestroyAllObjects(name))


def prepare_scene_object(name):
    """场景编辑幂等准备：销毁活动场景中全部同名对象（创建同名对象前调用）。

    等价于 destroy_all_objects；语义更贴近「创建前先清理」。
    返回 {"success": True, "destroyedCount": int, "destroyedInstanceIds": [int, ...]}
    """
    return destroy_all_objects(name)


def capture_scene_view(max_width=512, max_height=512):
    """截取 Unity Scene 视图（Editor）。成功时图像含于返回 JSON 的 __image 字段。

    max_width/max_height: 64-1920/64-1080 整数，默认 512
    返回格式同 capture_screenshot。
    """
    _validate_dims(max_width, max_height, "capture_scene_view")
    return json.loads(_bridge().CaptureSceneViewScreenshot(max_width, max_height))


def log(message):
    """输出到 Unity Console（UnityEngine.Debug.Log）。

    这是 LLM 的文本输出通道。
    """
    _bridge().Log(str(message))


def log_warning(message):
    """输出警告到 Unity Console（Debug.LogWarning）。"""
    _bridge().LogWarning(str(message))


def log_error(message):
    """输出错误到 Unity Console（Debug.LogError）。"""
    _bridge().LogError(str(message))


# ----- Scene View 操控动词（对齐 puerts scene-view.mjs）-----


def scene_view_zoom(direction, amount=1.0):
    """缩放 Scene View（等效鼠标滚轮）。

    direction: "forward"/"in"（拉近）或 "backward"/"out"（拉远）
    amount: 缩放强度，0.1-20（默认 1）
    返回 {"success": True, "operation": "zoom", ...}
    """
    valid = ("forward", "in", "backward", "out")
    if direction not in valid:
        raise ValueError(
            f"scene_view_zoom: 'direction' 必须是 {valid}（got {direction}）。"
            "读 help(unity.scene_view_zoom)"
        )
    if not isinstance(amount, (int, float)) or amount < 0.1 or amount > 20:
        raise ValueError(
            f"scene_view_zoom: 'amount' 必须是 0.1-20 的数值（got {amount}）。"
            "读 help(unity.scene_view_zoom)"
        )
    return json.loads(_bridge().ZoomSceneView(direction, amount))


def scene_view_pan(direction, amount=1.0):
    """平移 Scene View（等效鼠标中键拖拽）。

    direction: "up"/"down"/"left"/"right"
    amount: 平移距离乘数，0.1-50（默认 1）
    返回 {"success": True, "operation": "pan", ...}
    """
    valid = ("up", "down", "left", "right")
    if direction not in valid:
        raise ValueError(
            f"scene_view_pan: 'direction' 必须是 {valid}（got {direction}）。"
            "读 help(unity.scene_view_pan)"
        )
    if not isinstance(amount, (int, float)) or amount < 0.1 or amount > 50:
        raise ValueError(
            f"scene_view_pan: 'amount' 必须是 0.1-50 的数值（got {amount}）。"
            "读 help(unity.scene_view_pan)"
        )
    return json.loads(_bridge().PanSceneView(direction, amount))


def scene_view_orbit(direction, amount=1.0):
    """轨道旋转 Scene View（等效鼠标右键拖拽）。

    direction: "up"/"down"/"left"/"right"
    amount: 旋转强度，0.1-24（默认 1，每单位约 15 度）
    返回 {"success": True, "operation": "orbit", ...}
    """
    valid = ("up", "down", "left", "right")
    if direction not in valid:
        raise ValueError(
            f"scene_view_orbit: 'direction' 必须是 {valid}（got {direction}）。"
            "读 help(unity.scene_view_orbit)"
        )
    if not isinstance(amount, (int, float)) or amount < 0.1 or amount > 24:
        raise ValueError(
            f"scene_view_orbit: 'amount' 必须是 0.1-24 的数值（got {amount}）。"
            "读 help(unity.scene_view_orbit)"
        )
    return json.loads(_bridge().OrbitSceneView(direction, amount))


def get_scene_view_state():
    """读取当前 Scene View 相机状态。

    返回 {"success": True, "pivot": {x,y,z}, "rotation": {x,y,z,w},
    "eulerAngles": {x,y,z}, "size": float, "orthographic": bool}
    """
    return json.loads(_bridge().GetSceneViewState())


def set_scene_view_camera(pivot=None, rotation=None, size=None):
    """直接设置 Scene View 相机。

    pivot: (x, y, z) 世界坐标，可选
    rotation: (rx, ry, rz) 欧拉角度数，可选
    size: 缩放级别（正浮点），可选
    至少一个参数非 None。
    """
    if pivot is None and rotation is None and size is None:
        raise ValueError("set_scene_view_camera: 至少需要 pivot / rotation / size 之一")
    px = py = pz = 0.0
    setPivot = False
    if pivot is not None:
        _validate_position(pivot, "set_scene_view_camera")
        px, py, pz = float(pivot[0]), float(pivot[1]), float(pivot[2])
        setPivot = True
    rx = ry = rz = 0.0
    setRotation = False
    if rotation is not None:
        if not isinstance(rotation, (tuple, list)) or len(rotation) != 3:
            raise ValueError("set_scene_view_camera: 'rotation' 必须是 3 元组 (rx,ry,rz)")
        rx, ry, rz = float(rotation[0]), float(rotation[1]), float(rotation[2])
        setRotation = True
    sz = float(size) if size is not None else 0.0
    return json.loads(_bridge().SetSceneViewCamera(px, py, pz, setPivot, rx, ry, rz, setRotation, sz))


def focus_scene_view_on(name):
    """聚焦 Scene View 到某对象（等效 Unity Editor 里选中对象并按 F 键）。

    name: GameObject 名字
    返回 {"success": True, "focused": str, "pivot": {x,y,z}, "size": float}
    """
    if not isinstance(name, str) or not name.strip():
        raise ValueError("focus_scene_view_on: 'name' 必须是非空字符串")
    return json.loads(_bridge().FocusSceneViewOn(name))


def select_game_object(name):
    """选中 GameObject（Editor Hierarchy 高亮，并 Ping 到 Scene View）。

    name: GameObject 名字
    返回 {"success": True, "selected": str}
    """
    if not isinstance(name, str) or not name.strip():
        raise ValueError("select_game_object: 'name' 必须是非空字符串")
    return json.loads(_bridge().SelectGameObject(name))


def save_scene():
    """保存当前活动场景到磁盘。

    返回 {"success": True, "scene": str, "path": str}
    """
    return json.loads(_bridge().SaveScene())


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

"""unity 包：给 LLM 直接调用的 Unity 任务动词层（L1）。

薄翻译层：参数校验 → ``_unity_bridge`` → JSON 解析。
L2 自省：``list_editor_namespaces`` / ``get_type_details``（见 ``unity.inspect``）。
L3 任意白名单 API：``from unity_bind import CS``（见 python-interop skill）。

子模块（按域组织）：
- ``unity.scene_view``  — 场景层次/对象发现/生命周期/Scene View 相机
- ``unity.screenshot``  — 截图
- ``unity.inspect``     — C# 类型自省
- ``unity.console``     — 日志与 Debug 输出

顶层 ``unity.<verb>`` 为兼容 re-export 层（旧调用路径不变）。
transform / create_cube / find_object 为 legacy deprecated shim（见各 docstring）。
"""

# ---- scene_view ----
from .scene_view import (
    create_cube,
    create_primitive,
    destroy_all_objects,
    destroy_object,
    find_object,
    find_objects,
    focus_scene_view_on,
    get_hierarchy,
    get_position,
    get_rotation,
    get_scale,
    get_scene_view_state,
    look_at,
    move_object,
    prepare_scene_object,
    rotate_object,
    save_scene,
    scene_view_orbit,
    scene_view_pan,
    scene_view_zoom,
    select_game_object,
    set_position,
    set_rotation,
    set_scale,
    set_scene_view_camera,
)

# ---- screenshot ----
from .screenshot import capture_scene_view, capture_screenshot

# ---- inspect ----
from .inspect import (
    get_type_details,
    list_editor_namespaces,
    list_namespaces,
    list_types_in_namespace,
)

# ---- console ----
from .console import get_log_summary, get_logs, log, log_error, log_warning

__all__ = [
    # scene_view
    "get_hierarchy",
    "find_object",
    "find_objects",
    "destroy_object",
    "destroy_all_objects",
    "prepare_scene_object",
    "create_primitive",
    "create_cube",
    "save_scene",
    "select_game_object",
    "focus_scene_view_on",
    "scene_view_zoom",
    "scene_view_pan",
    "scene_view_orbit",
    "get_scene_view_state",
    "set_scene_view_camera",
    # screenshot
    "capture_screenshot",
    "capture_scene_view",
    # inspect
    "list_namespaces",
    "list_editor_namespaces",
    "list_types_in_namespace",
    "get_type_details",
    # console
    "get_logs",
    "get_log_summary",
    "log",
    "log_warning",
    "log_error",
    # legacy deprecated transform（仍可用，指引 L3 CS.UnityEngine.Transform）
    "get_position",
    "set_position",
    "get_rotation",
    "set_rotation",
    "get_scale",
    "set_scale",
    "move_object",
    "rotate_object",
    "look_at",
]

"""unity.screenshot — 截图（Game 视图 / Scene 视图）。"""

from ._common import _bridge, _validate_dims


def capture_screenshot(max_width=512, max_height=512):
    """截取 Unity Game 视图。成功时图像自动作为视觉内容回给 LLM（多模态）。

    max_width/max_height: 64-1920/64-1080 整数，默认 512
    返回 {"success": bool, "message": str, "__image": {"base64": ..., "mediaType": "image/png"}?}
    """
    _validate_dims(max_width, max_height, "capture_screenshot")
    import json
    return json.loads(_bridge().CaptureScreenshot(max_width, max_height))


def capture_scene_view(max_width=512, max_height=512):
    """截取 Unity Scene 视图（Editor）。成功时图像含于返回 JSON 的 __image 字段。

    max_width/max_height: 64-1920/64-1080 整数，默认 512
    返回格式同 capture_screenshot。
    """
    _validate_dims(max_width, max_height, "capture_scene_view")
    import json
    return json.loads(_bridge().CaptureSceneViewScreenshot(max_width, max_height))

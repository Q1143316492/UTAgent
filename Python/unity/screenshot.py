"""unity.screenshot — 截图（Game 视图 / Scene 视图）。

两条路径：
- 默认：返回 ``__image`` base64，供 Agent 多模态（DeepSeek 等纯文本会在
  ``process_pending_images`` 剥离，不送入 LLM）。
- ``save_to_file=True``：PNG 落盘并返回 ``path``（不含 base64），供 Cursor /
  能看图的模型用 Read 工具目检。
"""

from __future__ import annotations

import base64
import json
import os
from datetime import datetime, timezone

from ._common import _bridge, _validate_dims


def _screenshots_dir() -> str:
    # Assets/UTAgent/Python/unity → Assets/UTAgent/Out/screenshots
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.normpath(os.path.join(here, "..", "..", "Out", "screenshots"))


def _write_png_from_result(result: dict, path: str | None) -> dict:
    """从含 __image 的 bridge 结果写 PNG；返回无 base64 的摘要 dict。"""
    if not isinstance(result, dict) or not result.get("success"):
        return result if isinstance(result, dict) else {"success": False, "message": "capture failed"}

    image = result.get("__image")
    if not isinstance(image, dict) or not image.get("base64"):
        out = {k: v for k, v in result.items() if k != "__image"}
        out["success"] = False
        out["message"] = out.get("message") or "no __image in capture result"
        return out

    raw = base64.b64decode(image["base64"])
    if path:
        out_path = os.path.abspath(path)
        parent = os.path.dirname(out_path)
        if parent:
            os.makedirs(parent, exist_ok=True)
    else:
        os.makedirs(_screenshots_dir(), exist_ok=True)
        stamp = datetime.now(timezone.utc).strftime("%Y%m%d_%H%M%S")
        out_path = os.path.join(_screenshots_dir(), f"shot_{stamp}.png")

    with open(out_path, "wb") as f:
        f.write(raw)

    return {
        "success": True,
        "message": result.get("message") or "screenshot saved",
        "path": out_path.replace("\\", "/"),
        "bytes": len(raw),
    }


def capture_screenshot(max_width=512, max_height=512, save_to_file=False, path=None):
    """截取 Unity Game 视图（Play 失败则回退 Scene）。

    max_width/max_height: 64-1920/64-1080 整数，默认 512
    save_to_file: True 时写 PNG 并返回 path（无 __image）；False 时返回 __image（Agent 用）
    path: 可选显式输出路径；默认 Out/screenshots/shot_*.png
    """
    _validate_dims(max_width, max_height, "capture_screenshot")
    result = json.loads(_bridge().CaptureScreenshot(max_width, max_height))
    if save_to_file or path:
        return _write_png_from_result(result, path)
    return result


def capture_scene_view(max_width=512, max_height=512, save_to_file=False, path=None):
    """截取 Unity Scene 视图（Editor）。

    参数同 capture_screenshot。
    """
    _validate_dims(max_width, max_height, "capture_scene_view")
    result = json.loads(_bridge().CaptureSceneViewScreenshot(max_width, max_height))
    if save_to_file or path:
        return _write_png_from_result(result, path)
    return result

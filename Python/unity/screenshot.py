"""unity.screenshot — 截图（唯一推荐入口 ``capture``）。

- 默认返回 ``__image`` base64，供 Agent 多模态。
- ``save_to_file=True``：PNG 落盘并返回 ``path``（不含 base64），供 Cursor Read。

兼容：``capture_screenshot`` / ``capture_scene_view`` 转发 ``capture(view=...)``。
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


def capture(
    view="scene",
    max_width=512,
    max_height=512,
    save_to_file=False,
    path=None,
    name=None,
    padding=0,
):
    """统一截图入口。

    view: ``scene`` | ``game``
    max_width / max_height: 64–1920 / 64–1080，裁切后再缩放
    save_to_file / path: 落盘 PNG
    name: UI 物体名；非空则按 RectTransform 屏幕/相机投影矩形裁切（Overlay / Screen Space Camera / World Space）
    padding: 裁切外扩像素（>=0）
    """
    _validate_dims(max_width, max_height, "capture")
    view_key = (view or "scene").strip().lower()
    if view_key not in ("scene", "game"):
        raise ValueError("capture: view 须为 'scene' 或 'game'")
    if not isinstance(padding, int) or padding < 0:
        raise ValueError("capture: padding 须为非负整数")

    name_arg = "" if name is None else str(name)
    result = json.loads(
        _bridge().Capture(view_key, max_width, max_height, name_arg, padding)
    )
    if save_to_file or path:
        return _write_png_from_result(result, path)
    return result


def capture_screenshot(max_width=512, max_height=512, save_to_file=False, path=None):
    """兼容包装 → ``capture(view=\"game\", ...)``。"""
    return capture(
        view="game",
        max_width=max_width,
        max_height=max_height,
        save_to_file=save_to_file,
        path=path,
    )


def capture_scene_view(max_width=512, max_height=512, save_to_file=False, path=None):
    """兼容包装 → ``capture(view=\"scene\", ...)``。"""
    return capture(
        view="scene",
        max_width=max_width,
        max_height=max_height,
        save_to_file=save_to_file,
        path=path,
    )

#!/usr/bin/env python3
"""UTAgent Editor Bridge CLI — 调用 Unity Editor localhost HTTP 服务。"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request
from typing import Any
from urllib.parse import quote


DEFAULT_PORT = 17861
EXIT_OK = 0
EXIT_HTTP = 1
EXIT_ENGINE = 2
EXIT_EXEC = 3
EXIT_CHAT = 4

CHAT_POLL_INTERVAL_SEC = 1.0


def base_url(port: int) -> str:
    return f"http://127.0.0.1:{port}"


def resolve_port(args: argparse.Namespace) -> int:
    if getattr(args, "port", None):
        return int(args.port)
    env = os.environ.get("UTAGENT_PORT", "").strip()
    if env:
        return int(env)
    return DEFAULT_PORT


def request_json(
    method: str,
    path: str,
    port: int,
    body: dict[str, Any] | None = None,
    timeout: float = 30.0,
) -> tuple[int, dict[str, Any]]:
    url = base_url(port) + path
    data = None
    headers = {"Accept": "application/json"}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json; charset=utf-8"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read().decode("utf-8", errors="replace")
            status = resp.status
    except urllib.error.HTTPError as e:
        status = e.code
        raw = e.read().decode("utf-8", errors="replace")
    except urllib.error.URLError as e:
        print(f"连接失败: {e.reason}", file=sys.stderr)
        print(
            "请确认 Unity Editor 已打开，且 Remote CLI 已启用（Settings → ③ CLI，或打开 Agent Chat）。",
            file=sys.stderr,
        )
        sys.exit(EXIT_HTTP)
    try:
        payload = json.loads(raw) if raw else {}
    except json.JSONDecodeError:
        payload = {"ok": False, "raw": raw}
    return status, payload


def print_json(payload: dict[str, Any]) -> None:
    print(json.dumps(payload, ensure_ascii=False, indent=2))


def print_human_ping(payload: dict[str, Any]) -> None:
    print(f"editor_alive: {payload.get('editor_alive')}")
    print(f"engine_available: {payload.get('engine_available')}")
    print(f"invalidated: {payload.get('invalidated')}")
    print(f"bridge_running: {payload.get('bridge_running')}")
    print(f"port: {payload.get('port')}")
    print(f"log_directory: {payload.get('log_directory')}")
    if payload.get("hint"):
        print(f"hint: {payload.get('hint')}")


def cmd_ping(args: argparse.Namespace) -> int:
    port = resolve_port(args)
    status, payload = request_json("GET", "/ping", port)
    if args.json:
        print_json(payload)
    else:
        print_human_ping(payload)
    if status >= 400:
        return EXIT_HTTP
    if payload.get("invalidated") and not payload.get("engine_available"):
        return EXIT_ENGINE
    return EXIT_OK


def cmd_init(args: argparse.Namespace) -> int:
    port = resolve_port(args)
    status, payload = request_json("POST", "/initialize", port, body={})
    if args.json:
        print_json(payload)
    else:
        ok = payload.get("ok")
        print(f"ok: {ok}, engine_available: {payload.get('engine_available')}")
        if payload.get("error"):
            print(payload["error"], file=sys.stderr)
    if status >= 500:
        return EXIT_HTTP
    if not payload.get("engine_available"):
        return EXIT_ENGINE
    return EXIT_OK


def cmd_exec(args: argparse.Namespace) -> int:
    port = resolve_port(args)
    code = args.code
    if args.file:
        with open(args.file, encoding="utf-8") as f:
            code = f.read()
    if not code:
        print("必须提供 --code 或 --file", file=sys.stderr)
        return EXIT_HTTP
    status, payload = request_json("POST", "/exec", port, body={"code": code}, timeout=120.0)
    if status == 503:
        if args.json:
            print_json(payload)
        else:
            print(payload.get("hint", "引擎不可用"), file=sys.stderr)
        return EXIT_ENGINE
    if args.json:
        print_json(payload)
    else:
        if payload.get("output"):
            print(payload["output"], end="" if str(payload["output"]).endswith("\n") else "\n")
        if payload.get("error"):
            print(payload["error"], file=sys.stderr)
    if status >= 400:
        return EXIT_HTTP
    if payload.get("error"):
        return EXIT_EXEC
    if payload.get("ok") is False:
        return EXIT_EXEC
    return EXIT_OK


def cmd_log_tail(args: argparse.Namespace) -> int:
    port = resolve_port(args)
    n = args.lines
    status, payload = request_json("GET", f"/log/tail?n={n}", port)
    if args.json:
        print_json(payload)
        return EXIT_OK if status < 400 else EXIT_HTTP
    if payload.get("path"):
        print(f"# {payload['path']}")
    for line in payload.get("lines", []):
        print(line)
    return EXIT_OK if status < 400 else EXIT_HTTP


def cmd_log_errors(args: argparse.Namespace) -> int:
    port = resolve_port(args)
    n = args.lines
    status, payload = request_json("GET", f"/log/errors?n={n}", port)
    if args.json:
        print_json(payload)
        return EXIT_OK if status < 400 else EXIT_HTTP
    if payload.get("path"):
        print(f"# {payload['path']}")
    for line in payload.get("matches", []):
        print(line)
    return EXIT_OK if status < 400 else EXIT_HTTP


def wait_chat_turn(
    port: int,
    turn_id: str,
    timeout_sec: float,
    compact: bool,
) -> tuple[int, dict[str, Any] | None]:
    """轮询 /chat/status 直到 done 或超时。"""
    deadline = time.monotonic() + timeout_sec if timeout_sec > 0 else None
    while True:
        status, payload = request_json(
            "GET",
            f"/chat/status?turn_id={quote(turn_id, safe='')}",
            port,
            timeout=30.0,
        )
        if status >= 400:
            return status, payload
        if compact and payload.get("status") == "running":
            step = payload.get("step", 0)
            last = payload.get("last_status", "")
            print(f"\r[{turn_id}] 第 {step} 步: {last}", end="", flush=True)
        if payload.get("status") == "done":
            if compact:
                print()
            return status, payload
        if deadline is not None and time.monotonic() >= deadline:
            return 408, {
                "ok": False,
                "error": "timeout",
                "turn_id": turn_id,
                "last_status": payload.get("last_status"),
            }
        time.sleep(CHAT_POLL_INTERVAL_SEC)


def print_chat_result(payload: dict[str, Any], compact: bool) -> None:
    if payload.get("is_error") or payload.get("outcome") not in (None, "", "success"):
        print(payload.get("final_text") or payload.get("error", ""), file=sys.stderr)
    elif payload.get("final_text"):
        print(payload["final_text"])
    if payload.get("log_directory"):
        print(f"log_directory: {payload['log_directory']}")


def cmd_chat(args: argparse.Namespace) -> int:
    rest = list(getattr(args, "rest", None) or [])
    if rest and rest[0] == "wait":
        if len(rest) < 2:
            print("用法: utagent chat wait <turn_id>", file=sys.stderr)
            return EXIT_HTTP
        args.turn_id = rest[1]
        return cmd_chat_wait(args)
    if not rest:
        print('用法: utagent chat "任务描述"  或  utagent chat wait <turn_id>', file=sys.stderr)
        return EXIT_HTTP
    args.message = " ".join(rest)
    return cmd_chat_send(args)


def cmd_chat_send(args: argparse.Namespace) -> int:
    port = resolve_port(args)
    status, payload = request_json(
        "POST",
        "/chat",
        port,
        body={"message": args.message},
        timeout=60.0,
    )
    if status == 503:
        if args.json:
            print_json(payload)
        else:
            print(payload.get("error", "引擎或 LLM 未配置"), file=sys.stderr)
        return EXIT_ENGINE
    if status == 409:
        if args.json:
            print_json(payload)
        else:
            print(payload.get("error", "已有任务运行中"), file=sys.stderr)
        return EXIT_CHAT
    if status >= 400 or not payload.get("ok"):
        if args.json:
            print_json(payload)
        else:
            print(payload.get("error", "chat start failed"), file=sys.stderr)
        return EXIT_HTTP

    turn_id = str(payload.get("turn_id", ""))
    if args.no_wait:
        if args.json:
            print_json(payload)
        else:
            print(f"turn_id: {turn_id}")
            print("status: running")
        return EXIT_OK

    wait_status, result = wait_chat_turn(port, turn_id, args.timeout, args.compact)
    if result is None:
        return EXIT_HTTP
    if wait_status == 408:
        if args.json:
            print_json(result)
        else:
            print(f"等待超时（turn_id={turn_id}）", file=sys.stderr)
            print("可稍后运行: utagent chat wait " + turn_id, file=sys.stderr)
        return EXIT_CHAT
    if wait_status >= 400:
        if args.json:
            print_json(result)
        return EXIT_HTTP

    if args.json:
        print_json(result)
    else:
        print_chat_result(result, args.compact)

    if result.get("is_error"):
        return EXIT_CHAT
    outcome = result.get("outcome", "success")
    if outcome and outcome != "success":
        return EXIT_CHAT
    return EXIT_OK


def cmd_chat_wait(args: argparse.Namespace) -> int:
    port = resolve_port(args)
    turn_id = args.turn_id
    wait_status, result = wait_chat_turn(port, turn_id, args.timeout, args.compact)
    if result is None:
        return EXIT_HTTP
    if wait_status == 408:
        if args.json:
            print_json(result)
        else:
            print(f"等待超时（turn_id={turn_id}）", file=sys.stderr)
        return EXIT_CHAT
    if wait_status >= 400:
        if args.json:
            print_json(result)
        else:
            print(result.get("error", "status failed"), file=sys.stderr)
        return EXIT_HTTP

    if args.json:
        print_json(result)
    else:
        if result.get("status") != "done":
            print(f"status: {result.get('status')}")
            print(result.get("last_status", ""))
        else:
            print_chat_result(result, args.compact)

    if result.get("status") != "done":
        return EXIT_CHAT
    if result.get("is_error"):
        return EXIT_CHAT
    outcome = result.get("outcome", "success")
    if outcome and outcome != "success":
        return EXIT_CHAT
    return EXIT_OK


def cmd_scene_find(args: argparse.Namespace) -> int:
    port = resolve_port(args)
    name = quote(args.name, safe="")
    status, payload = request_json("GET", f"/scene/find?name={name}", port)
    if args.json:
        print_json(payload)
    else:
        print(f"count: {payload.get('count', 0)}")
        for n in payload.get("names", []):
            print(f"  - {n}")
    return EXIT_OK if status < 400 else EXIT_HTTP


def cmd_screenshot(args: argparse.Namespace) -> int:
    """截图落盘，打印 path，供 Cursor Read 看图（不经 DeepSeek）。"""
    port = resolve_port(args)
    view = getattr(args, "view", "scene") or "scene"
    out = getattr(args, "out", None)
    max_w = int(getattr(args, "width", 512) or 512)
    max_h = int(getattr(args, "height", 512) or 512)
    fn = "capture_scene_view" if view == "scene" else "capture_screenshot"
    path_arg = "None" if not out else json.dumps(out)
    code = (
        "import importlib, json\n"
        "import unity.screenshot as _us\n"
        "importlib.reload(_us)\n"
        f"r = _us.{fn}(max_width={max_w}, max_height={max_h}, save_to_file=True, path={path_arg})\n"
        "print(json.dumps(r, ensure_ascii=False))\n"
    )
    status, payload = request_json("POST", "/exec", port, body={"code": code}, timeout=120.0)
    if status == 503:
        if args.json:
            print_json(payload)
        else:
            print(payload.get("hint", "引擎不可用"), file=sys.stderr)
        return EXIT_ENGINE
    if status >= 400 or payload.get("error") or payload.get("ok") is False:
        if args.json:
            print_json(payload)
        else:
            print(payload.get("error") or payload.get("output") or "screenshot failed", file=sys.stderr)
        return EXIT_EXEC if status < 400 else EXIT_HTTP

    raw_out = (payload.get("output") or "").strip()
    result: dict[str, Any] = {}
    if raw_out:
        # 取最后一行 JSON
        for line in reversed(raw_out.splitlines()):
            line = line.strip()
            if line.startswith("{"):
                try:
                    result = json.loads(line)
                    break
                except json.JSONDecodeError:
                    continue
    if args.json:
        print_json(result if result else payload)
    else:
        path = result.get("path")
        if path:
            print(f"path: {path}")
            print(f"bytes: {result.get('bytes', '')}")
            print(result.get("message", ""))
        else:
            print(raw_out or json.dumps(payload, ensure_ascii=False))
    if result and not result.get("success", True):
        return EXIT_EXEC
    return EXIT_OK


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="utagent", description="UTAgent Editor Bridge CLI")
    parser.add_argument("--port", type=int, help="Bridge 端口（默认 17861，或环境变量 UTAGENT_PORT）")
    parser.add_argument("--json", action="store_true", help="输出 JSON")

    sub = parser.add_subparsers(dest="command", required=True)

    p_ping = sub.add_parser("ping", help="检查 Editor / 引擎状态")
    p_ping.set_defaults(func=cmd_ping)

    p_init = sub.add_parser("init", help="初始化 Python 引擎（域重载后恢复）")
    p_init.set_defaults(func=cmd_init)

    p_exec = sub.add_parser("exec", help="执行 Python 代码")
    p_exec.add_argument("--code", help="内联 Python 代码")
    p_exec.add_argument("--file", help="从文件读取代码")
    p_exec.set_defaults(func=cmd_exec)

    p_log = sub.add_parser("log", help="读取 Agent 日志")
    log_sub = p_log.add_subparsers(dest="log_cmd", required=True)

    p_tail = log_sub.add_parser("tail", help="日志末尾")
    p_tail.add_argument("-n", "--lines", type=int, default=80, dest="lines")
    p_tail.set_defaults(func=cmd_log_tail)

    p_errors = log_sub.add_parser("errors", help="筛选错误行")
    p_errors.add_argument("-n", "--lines", type=int, default=200, dest="lines")
    p_errors.set_defaults(func=cmd_log_errors)

    p_scene = sub.add_parser("scene", help="场景查询")
    scene_sub = p_scene.add_subparsers(dest="scene_cmd", required=True)
    p_find = scene_sub.add_parser("find", help="按名称查找对象")
    p_find.add_argument("name")
    p_find.set_defaults(func=cmd_scene_find)

    p_shot = sub.add_parser(
        "screenshot",
        help="截图落盘（返回 path；供 Cursor Read 看图，不经 Chat/DeepSeek）",
    )
    p_shot.add_argument(
        "--view",
        choices=("scene", "game"),
        default="scene",
        help="scene=Scene 视图（默认）；game=Game 视图（非 Play 时回退 Scene）",
    )
    p_shot.add_argument("--out", help="输出 PNG 路径（默认 LOG/screenshots/shot_*.png）")
    p_shot.add_argument("--width", type=int, default=512)
    p_shot.add_argument("--height", type=int, default=512)
    p_shot.set_defaults(func=cmd_screenshot)

    p_chat = sub.add_parser("chat", help="自然语言 ReAct 任务（等同 Chat 发话）")
    p_chat.add_argument(
        "rest",
        nargs=argparse.REMAINDER,
        help='任务描述，或 "wait <turn_id>"',
    )
    p_chat.add_argument("--no-wait", action="store_true", help="仅提交，不阻塞等待完成")
    p_chat.add_argument(
        "--timeout",
        type=float,
        default=600.0,
        help="阻塞等待超时秒数（默认 600）",
    )
    p_chat.add_argument("--compact", action="store_true", help="等待时单行刷新进度")
    p_chat.set_defaults(func=cmd_chat)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return int(args.func(args))


if __name__ == "__main__":
    sys.exit(main())

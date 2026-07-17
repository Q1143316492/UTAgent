"""Session JSONL 读写（对齐 Pi：header + id/parentId 树；本行仅线性 leaf 路径）。"""

from __future__ import annotations

import json
import os
import uuid
from datetime import datetime, timezone
from typing import Any

from messages import ASSISTANT, COMPACTION, NUDGE, REMINDER, USER, deepcopy_history, get_kind


def _utc_now_iso() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat()


def new_entry_id() -> str:
    return uuid.uuid4().hex


def ensure_parent_dir(path: str) -> None:
    parent = os.path.dirname(path)
    if parent:
        os.makedirs(parent, exist_ok=True)


def write_session_jsonl(
    path: str,
    session_id: str,
    history: list[dict[str, Any]],
    *,
    name: str = "",
    cwd: str = "",
    created_at: str | None = None,
) -> dict[str, Any]:
    """覆盖写入 header + 线性 id/parentId 链（leaf = 末条）。"""
    ensure_parent_dir(path)
    created = created_at or _utc_now_iso()
    header: dict[str, Any] = {
        "type": "session_header",
        "id": session_id,
        "cwd": cwd or "",
        "createdAt": created,
    }
    if name:
        header["name"] = name

    lines = [json.dumps(header, ensure_ascii=False)]
    parent_id = None
    leaf_id = None
    for msg in history:
        entry_id = new_entry_id()
        entry: dict[str, Any] = {
            "id": entry_id,
            "parentId": parent_id,
            "type": "message",
            "timestamp": _utc_now_iso(),
            "message": msg,
        }
        lines.append(json.dumps(entry, ensure_ascii=False))
        parent_id = entry_id
        leaf_id = entry_id

    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
        f.write("\n")

    return {
        "ok": True,
        "session_id": session_id,
        "leaf_id": leaf_id,
        "history_len": len(history),
        "path": path,
        "createdAt": created,
        "name": name or "",
    }


def create_empty_session(
    path: str,
    session_id: str,
    *,
    name: str = "",
    cwd: str = "",
) -> dict[str, Any]:
    return write_session_jsonl(
        path, session_id, [], name=name, cwd=cwd, created_at=_utc_now_iso())


def read_session_file(path: str) -> tuple[dict[str, Any] | None, list[dict[str, Any]], str | None]:
    """读 JSONL → (header, entries, leaf_id)。leaf = 无子节点的 entry；线性时即末条。"""
    if not os.path.isfile(path):
        return None, [], None

    header = None
    entries: list[dict[str, Any]] = []
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue
            if not isinstance(obj, dict):
                continue
            if obj.get("type") == "session_header":
                header = obj
                continue
            if obj.get("type") == "message" or "message" in obj:
                entries.append(obj)

    if not entries:
        return header, [], None

    children: set[str] = set()
    for e in entries:
        pid = e.get("parentId")
        if isinstance(pid, str) and pid:
            children.add(pid)

    leaf_id = None
    for e in reversed(entries):
        eid = e.get("id")
        if isinstance(eid, str) and eid and eid not in children:
            leaf_id = eid
            break
    if leaf_id is None:
        leaf_id = entries[-1].get("id")

    return header, entries, leaf_id


def leaf_path_messages(
    entries: list[dict[str, Any]],
    leaf_id: str | None,
) -> list[dict[str, Any]]:
    """从 leaf 沿 parentId 走到根，返回 history 消息列表（根→叶）。"""
    if not entries or not leaf_id:
        return []

    by_id: dict[str, dict[str, Any]] = {}
    for e in entries:
        eid = e.get("id")
        if isinstance(eid, str) and eid:
            by_id[eid] = e

    chain: list[dict[str, Any]] = []
    cur = leaf_id
    seen: set[str] = set()
    while cur and cur not in seen:
        seen.add(cur)
        entry = by_id.get(cur)
        if entry is None:
            break
        msg = entry.get("message")
        if isinstance(msg, dict):
            chain.append(msg)
        parent = entry.get("parentId")
        cur = parent if isinstance(parent, str) else None

    chain.reverse()
    return chain


def _content_to_text(content: Any) -> str:
    if content is None:
        return ""
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        parts: list[str] = []
        for part in content:
            if isinstance(part, dict) and part.get("type") == "text":
                parts.append(str(part.get("text") or ""))
            elif isinstance(part, dict) and part.get("type") == "image_url":
                parts.append("[image]")
        return "\n".join(p for p in parts if p)
    return str(content)


def _tool_call_ui_text(tc: dict[str, Any]) -> str:
    """把单条 tool_call 还原成 Chat 气泡文案（与 Runner PushProgress tool_call 对齐）。"""
    if not isinstance(tc, dict):
        return "tool"
    fn = tc.get("function") if isinstance(tc.get("function"), dict) else {}
    name = str(fn.get("name") or tc.get("name") or "tool")
    raw_args = fn.get("arguments") or tc.get("arguments") or ""
    args_obj: Any = raw_args
    if isinstance(raw_args, str) and raw_args.strip():
        try:
            args_obj = json.loads(raw_args)
        except (TypeError, json.JSONDecodeError):
            args_obj = raw_args
    if name == "execPython":
        code = ""
        if isinstance(args_obj, dict):
            code = str(args_obj.get("code") or "")
        elif isinstance(args_obj, str):
            code = args_obj
        return code if code.strip() else "execPython"
    if name == "loadSkill":
        skill = ""
        if isinstance(args_obj, dict):
            skill = str(args_obj.get("name") or "")
        return f"loadSkill({skill})" if skill else "loadSkill"
    if isinstance(args_obj, dict) and args_obj:
        try:
            return f"{name}({json.dumps(args_obj, ensure_ascii=False)})"
        except (TypeError, ValueError):
            return name
    return name


def _tool_result_ui_text(content: Any) -> str:
    """tool result → observation 文案；优先 stdout/preview，避免整段巨型 JSON。"""
    text = _content_to_text(content)
    if not text.strip():
        return ""
    data = None
    try:
        data = json.loads(text)
    except (TypeError, json.JSONDecodeError):
        # after-tool 截断后 JSON 可能不完整，尽力抽 stdout
        data = None
    if isinstance(data, dict):
        if data.get("preview"):
            text = str(data["preview"])
        elif "stdout" in data or "stderr" in data or "error" in data:
            out = str(data.get("stdout") or "")
            err = str(data.get("stderr") or data.get("error") or "")
            ok = data.get("success")
            parts = []
            if ok is False:
                parts.append("fail")
            if out:
                parts.append(out)
            if err:
                parts.append(err)
            text = "\n".join(parts) if parts else text
        elif data.get("skill") and data.get("ok") is True:
            text = f"loadSkill({data.get('skill')}): ok"
    else:
        extracted = _extract_json_string_field(text, "stdout")
        if extracted is None:
            extracted = _extract_json_string_field(text, "preview")
        if extracted is not None:
            text = extracted
        elif text.lstrip().startswith("{"):
            # 仍是 JSON 外形但解析失败：去掉外壳提示
            text = text[:2000]
    if len(text) > 2000:
        return text[:2000] + "…"
    return text


def _extract_json_string_field(raw: str, key: str) -> str | None:
    """从可能被截断的 JSON 文本中提取字符串字段（支持常见 \\ 转义）。"""
    if not raw or not key:
        return None
    needle = f"\"{key}\""
    idx = raw.find(needle)
    if idx < 0:
        return None
    i = idx + len(needle)
    while i < len(raw) and raw[i] in " \t\r\n:":
        i += 1
    if i >= len(raw) or raw[i] != '"':
        return None
    i += 1
    parts: list[str] = []
    while i < len(raw):
        c = raw[i]
        if c == "\\":
            if i + 1 >= len(raw):
                break
            n = raw[i + 1]
            esc = {"n": "\n", "r": "\r", "t": "\t", '"': '"', "\\": "\\"}.get(n)
            parts.append(esc if esc is not None else n)
            i += 2
            continue
        if c == '"':
            return "".join(parts)
        parts.append(c)
        i += 1
    # 未闭合（截断）：仍返回已解析部分
    return "".join(parts) if parts else None


def history_to_ui_messages(history: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """供 Chat 重建气泡：跳过 reminder/nudge；还原 execPython 脚本与 tool 结果。"""
    ui: list[dict[str, Any]] = []
    for msg in history:
        role = msg.get("role")
        kind = get_kind(msg)
        if kind in (REMINDER, NUDGE):
            continue
        if role == "user":
            text = _content_to_text(msg.get("content"))
            if text.strip():
                ui.append({"role": "user", "text": text, "kind": kind})
            continue
        if role == "assistant":
            tool_calls = msg.get("tool_calls") or []
            content = _content_to_text(msg.get("content"))
            if tool_calls:
                for tc in tool_calls:
                    if not isinstance(tc, dict):
                        continue
                    label = _tool_call_ui_text(tc)
                    if label.strip():
                        ui.append({
                            "role": "assistant",
                            "text": label,
                            "kind": ASSISTANT,
                            "block": "tool_call",
                        })
            if content.strip():
                block = "answer"
                if kind == COMPACTION:
                    block = "compaction"
                ui.append({"role": "assistant", "text": content, "kind": kind, "block": block})
            continue
        if role == "tool":
            preview = _tool_result_ui_text(msg.get("content"))
            if preview.strip():
                ui.append({
                    "role": "assistant",
                    "text": preview,
                    "kind": "tool",
                    "block": "observation",
                })
    return ui


def first_user_summary(history: list[dict[str, Any]], max_len: int = 48) -> str:
    for msg in history:
        if msg.get("role") != "user":
            continue
        if get_kind(msg) in (REMINDER, NUDGE):
            continue
        text = _content_to_text(msg.get("content")).strip().replace("\n", " ")
        if not text:
            continue
        if len(text) > max_len:
            return text[:max_len] + "…"
        return text
    return ""


def load_session_messages(path: str) -> dict[str, Any]:
    header, entries, leaf_id = read_session_file(path)
    if header is None and not entries:
        return {"ok": False, "message": f"session not found: {path}"}
    history = leaf_path_messages(entries, leaf_id)
    session_id = ""
    if isinstance(header, dict):
        session_id = str(header.get("id") or "")
    name = ""
    if isinstance(header, dict):
        name = str(header.get("name") or "")
    return {
        "ok": True,
        "session_id": session_id,
        "name": name,
        "leaf_id": leaf_id,
        "history": deepcopy_history(history),
        "history_len": len(history),
        "ui_messages": history_to_ui_messages(history),
        "summary": first_user_summary(history) or name or session_id[:8],
        "createdAt": (header or {}).get("createdAt") if header else None,
        "path": path,
    }

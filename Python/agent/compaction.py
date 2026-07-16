"""LLM 摘要 compaction（对标 Pi shouldCompact / compact）。

HTTP 由 C# 发起；本模块只构造摘要请求、应用摘要到 _history。
"""

from __future__ import annotations

import json
from typing import Any

from messages import COMPACTION, history_user_message

COMPACTION_SYSTEM_PROMPT = (
    "You are summarizing a Unity Editor agent conversation for context compaction.\n"
    "You MUST preserve:\n"
    "- the user's task goal (任务目标)\n"
    "- key GameObject / UI names (关键对象名)\n"
    "- completed steps and remaining work (已完成 / 未完成)\n"
    "- important errors\n"
    "Do NOT output empty placeholder phrases like "
    "\"context was compressed\" or \"earlier dialogue was trimmed\".\n"
    "Write a dense structured summary (Chinese or English)."
)

COMPACTION_CONTENT_PREFIX = "[Compaction Summary]\n"


def build_compaction_payload(messages: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """把待摘要的 API messages 打成无 tools 的摘要请求 messages。"""
    serialized = _serialize_for_summary(messages)
    return [
        {"role": "system", "content": COMPACTION_SYSTEM_PROMPT},
        {
            "role": "user",
            "content": (
                "Summarize the following conversation history for continued Unity Editor work.\n"
                "Preserve task goal and key GameObject/UI names.\n\n"
                f"{serialized}"
            ),
        },
    ]


def format_compaction_content(summary_text: str) -> str:
    text = (summary_text or "").strip()
    if not text:
        return ""
    if text.startswith("[Compaction Summary]"):
        return text
    return COMPACTION_CONTENT_PREFIX + text


def make_compaction_history_message(summary_text: str) -> dict[str, Any] | None:
    content = format_compaction_content(summary_text)
    if not content:
        return None
    return history_user_message(content, kind=COMPACTION)


def _serialize_for_summary(messages: list[dict[str, Any]], max_chars: int = 60000) -> str:
    parts: list[str] = []
    total = 0
    for msg in messages:
        role = msg.get("role", "?")
        content = msg.get("content", "")
        if isinstance(content, list):
            texts = []
            for block in content:
                if isinstance(block, dict) and block.get("type") == "text":
                    texts.append(str(block.get("text", "")))
                elif isinstance(block, dict) and block.get("type") in ("image_url", "image"):
                    texts.append("[image]")
            content = "\n".join(texts)
        elif content is None:
            content = ""
        elif not isinstance(content, str):
            content = json.dumps(content, ensure_ascii=False)
        tool_calls = msg.get("tool_calls")
        line = f"[{role}] {content}"
        if tool_calls:
            line += "\n  tool_calls: " + json.dumps(tool_calls, ensure_ascii=False)
        if total + len(line) > max_chars:
            remain = max_chars - total
            if remain > 80:
                parts.append(line[:remain] + "\n... (truncated)")
            break
        parts.append(line)
        total += len(line) + 1
    return "\n\n".join(parts)

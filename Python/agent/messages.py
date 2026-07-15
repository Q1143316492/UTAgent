"""Agent 消息 kind 与 convert_to_llm 过滤器（展示历史 → LLM API 层）。"""

from __future__ import annotations

import copy
from dataclasses import dataclass, field
from typing import Any

# kind 常量
USER = "user"
ASSISTANT = "assistant"
TOOL = "tool"
REMINDER = "reminder"
NUDGE = "nudge"
SCREENSHOT = "screenshot"

EPHEMERAL_KINDS = frozenset({REMINDER, NUDGE})

_LLM_FIELDS = ("role", "content", "tool_calls", "tool_call_id", "reasoning_content")


@dataclass
class Message:
    """展示历史消息（含 kind / ephemeral 元数据）。"""

    role: str
    content: Any = ""
    kind: str = USER
    ephemeral: bool = False
    tool_calls: list | None = None
    tool_call_id: str | None = None
    reasoning_content: str | None = None

    def to_dict(self) -> dict[str, Any]:
        data: dict[str, Any] = {
            "role": self.role,
            "kind": self.kind,
        }
        if self.ephemeral:
            data["ephemeral"] = True
        if self.content is not None and self.content != "":
            data["content"] = self.content
        elif self.role in ("assistant", "tool"):
            data["content"] = self.content if self.content is not None else ""
        if self.tool_calls is not None:
            data["tool_calls"] = self.tool_calls
        if self.tool_call_id is not None:
            data["tool_call_id"] = self.tool_call_id
        if self.reasoning_content is not None:
            data["reasoning_content"] = self.reasoning_content
        return data

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> Message:
        return cls(
            role=data.get("role", USER),
            content=data.get("content", ""),
            kind=get_kind(data),
            ephemeral=bool(data.get("ephemeral")),
            tool_calls=data.get("tool_calls"),
            tool_call_id=data.get("tool_call_id"),
            reasoning_content=data.get("reasoning_content"),
        )


def get_kind(msg: dict[str, Any]) -> str:
    """取消息 kind；缺省时按 role 推断。"""
    kind = msg.get("kind")
    if isinstance(kind, str) and kind:
        return kind
    role = msg.get("role")
    if role == TOOL:
        return TOOL
    if role == ASSISTANT:
        return ASSISTANT
    return USER


def is_ephemeral(msg: dict[str, Any]) -> bool:
    kind = get_kind(msg)
    return bool(msg.get("ephemeral")) or kind in EPHEMERAL_KINDS


def to_llm_dict(msg: dict[str, Any]) -> dict[str, Any]:
    """剥离 kind/ephemeral 等内部字段，输出 OpenAI messages 形状。"""
    out: dict[str, Any] = {}
    for key in _LLM_FIELDS:
        if key not in msg:
            continue
        value = msg[key]
        if value is None and key != "content":
            continue
        out[key] = value
    if out.get("role") == "assistant" and "content" not in out:
        out["content"] = ""
    return out


def convert_to_llm(history: list[dict[str, Any]], keep_last_reminder: bool = True) -> list[dict[str, Any]]:
    """从展示历史派生进 LLM 的 messages；ephemeral 类仅保留最近一条同 kind。"""
    messages, _ = convert_to_llm_with_meta(history, keep_last_reminder=keep_last_reminder)
    return messages


def convert_to_llm_with_meta(
    history: list[dict[str, Any]],
    keep_last_reminder: bool = True,
) -> tuple[list[dict[str, Any]], dict[str, int]]:
    """convert_to_llm 并返回 ephemeral 过滤统计（供 log / 基准断言）。"""
    last_ephemeral_idx: dict[str, int] = {}
    stats = {
        "reminder_in_history": 0,
        "reminder_in_llm": 0,
        "nudge_in_history": 0,
        "nudge_in_llm": 0,
    }

    for i, msg in enumerate(history):
        kind = get_kind(msg)
        if kind == REMINDER:
            stats["reminder_in_history"] += 1
        elif kind == NUDGE:
            stats["nudge_in_history"] += 1
        if is_ephemeral(msg):
            last_ephemeral_idx[kind] = i

    result: list[dict[str, Any]] = []
    for i, msg in enumerate(history):
        kind = get_kind(msg)
        if is_ephemeral(msg):
            if not keep_last_reminder:
                continue
            if last_ephemeral_idx.get(kind) != i:
                continue
        result.append(to_llm_dict(msg))
        if kind == REMINDER:
            stats["reminder_in_llm"] += 1
        elif kind == NUDGE:
            stats["nudge_in_llm"] += 1

    return result, stats


def history_user_message(
    content: Any,
    kind: str = USER,
    ephemeral: bool = False,
    **extra: Any,
) -> dict[str, Any]:
    """构造带 kind 的 user 消息 dict。"""
    msg: dict[str, Any] = {"role": "user", "content": content, "kind": kind}
    if ephemeral:
        msg["ephemeral"] = True
    msg.update(extra)
    return msg


def history_assistant_message(
    content: str = "",
    tool_calls: list | None = None,
    reasoning_content: str | None = None,
) -> dict[str, Any]:
    msg: dict[str, Any] = {"role": "assistant", "content": content, "kind": ASSISTANT}
    if tool_calls is not None:
        msg["tool_calls"] = tool_calls
    if reasoning_content is not None:
        msg["reasoning_content"] = reasoning_content
    return msg


def history_tool_message(tool_call_id: str, content: str) -> dict[str, Any]:
    return {
        "role": "tool",
        "tool_call_id": tool_call_id,
        "content": content,
        "kind": TOOL,
    }


def deepcopy_history(history: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return copy.deepcopy(history)

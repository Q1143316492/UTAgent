"""L1 E09：convert_to_llm 对 reminder 仅保留最近一条。"""
import json
import os
import sys

try:
    _AGENT_DIR = os.path.normpath(os.path.join(os.path.dirname(__file__), "../../Python/agent"))
    if _AGENT_DIR not in sys.path:
        sys.path.insert(0, _AGENT_DIR)
except NameError:
    pass  # Unity exec：AgentDir 已在 sys.path

from messages import REMINDER, convert_to_llm

history = [
    {"role": "user", "content": "task", "kind": "user"},
    {"role": "user", "content": "reminder 1", "kind": REMINDER, "ephemeral": True},
    {"role": "assistant", "content": "ok", "kind": "assistant"},
    {"role": "user", "content": "reminder 2", "kind": REMINDER, "ephemeral": True},
    {"role": "user", "content": "reminder 3", "kind": REMINDER, "ephemeral": True},
]

out = convert_to_llm(history)
reminder_texts = [
    m.get("content")
    for m in out
    if m.get("role") == "user" and isinstance(m.get("content"), str) and m["content"].startswith("reminder")
]
ok = len(reminder_texts) == 1 and reminder_texts[0] == "reminder 3"
print(json.dumps({
    "reminder_count": len(reminder_texts),
    "last_reminder": reminder_texts[0] if reminder_texts else None,
    "total_llm_messages": len(out),
    "ok": ok,
}, ensure_ascii=False))

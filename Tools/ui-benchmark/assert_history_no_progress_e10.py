"""L1 E10：loadSkill / emit_progress 不进 _history。"""
import json
import os
import sys

try:
    _AGENT_DIR = os.path.normpath(os.path.join(os.path.dirname(__file__), "../../Python/agent"))
    if _AGENT_DIR not in sys.path:
        sys.path.insert(0, _AGENT_DIR)
except NameError:
    pass  # Unity exec：AgentDir 已在 sys.path

import agent
from messages import get_kind

before = len(agent._history)
agent.load_skill("editor-ui")
after_load = len(agent._history)
agent.emit_progress("loadSkill", "editor-ui ok")
after_progress = len(agent._history)

status_in_history = False
for msg in agent._history:
    content = msg.get("content", "")
    if isinstance(content, str) and content.startswith("status:"):
        status_in_history = True
        break
    if get_kind(msg) in ("progress", "status"):
        status_in_history = True
        break

ok = (
    after_load == before
    and after_progress == after_load
    and not status_in_history
)
print(json.dumps({
    "history_len_before": before,
    "history_len_after_load": after_load,
    "history_len_after_progress": after_progress,
    "status_in_history": status_in_history,
    "ok": ok,
}, ensure_ascii=False))

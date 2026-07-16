"""L1 E11：apply_compaction_summary 持久写入 kind=compaction；convert_to_llm 保留。"""
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
from messages import COMPACTION, convert_to_llm

# 构造超长假 history
agent._history.clear()
agent._history.append({"role": "user", "content": "创建 WndSettings 面板", "kind": "user"})
for i in range(30):
    agent._history.append({
        "role": "assistant",
        "content": f"step {i} working on UI",
        "kind": "assistant",
    })
    agent._history.append({
        "role": "user",
        "content": f"继续 {i} " + ("x" * 200),
        "kind": "user",
    })

history_before = len(agent._history)
keep_n = 8
summary = (
    "任务目标：创建 WndSettings。关键对象：WndSettings、BtnSave。"
    "已完成：面板框架。未完成：row 与按钮。无错误。"
)

# 直接调用内部逻辑（绕过 print 入口）
from compaction import make_compaction_history_message

msg = make_compaction_history_message(summary)
start = agent._align_safe_trim_start(agent._history, len(agent._history) - keep_n)
kept = agent._history[start:]
agent._history = [msg] + kept

kinds = [m.get("kind") for m in agent._history]
llm = convert_to_llm(agent._history)
compaction_in_llm = any(
    isinstance(m.get("content"), str) and "[Compaction Summary]" in m.get("content", "")
    for m in llm
)

# needs_compaction 路径：极低 token 预算
agent._history.clear()
agent._history.append({"role": "user", "content": "goal: WndLogin", "kind": "user"})
for i in range(25):
    agent._history.append({
        "role": "assistant",
        "content": ("blob " + str(i) + " ") * 80,
        "kind": "assistant",
    })
(
    _msgs,
    _pruned,
    _est,
    _emerg,
    _meta,
    needs_compaction,
    compaction_messages,
    _keep,
) = agent._prepare_messages_copy(1, 500, 6, allow_compaction=True)

prompt_has_anchor = False
if compaction_messages:
    blob = json.dumps(compaction_messages, ensure_ascii=False)
    prompt_has_anchor = (
        "任务目标" in blob
        or "GameObject" in blob
        or "task goal" in blob.lower()
    )

ok = (
    COMPACTION in kinds
    and compaction_in_llm
    and len(agent._history) >= 1
    and needs_compaction is True
    and compaction_messages is not None
    and prompt_has_anchor
)

print(json.dumps({
    "history_before": history_before,
    "history_len_after": len(kinds),
    "compaction_kind_present": COMPACTION in kinds,
    "compaction_in_llm": compaction_in_llm,
    "needs_compaction": needs_compaction,
    "prompt_has_anchor": prompt_has_anchor,
    "ok": ok,
}, ensure_ascii=False))

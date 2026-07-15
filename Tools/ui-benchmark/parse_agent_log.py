#!/usr/bin/env python3
"""解析 UTAgent 会话日志，输出结构化 tool 序列 JSON。

锚定 ASCII 事件 token（见 Assets/UTAgent/Docs/ui-assembly-benchmark.md §log 格式契约）：
  TURN BEGIN / --- step N --- / before-exec / tool_call / status: loadSkill: / TURN END

用法：
  python parse_agent_log.py <log_path>            # 打印 JSON
  python parse_agent_log.py <log_path> --assert C09   # 断言模式（见 BENCHMARK_ASSERTS）

输出 JSON：
  {
    "turns": [{"id": "22a3", "begin": "02:22:55", "end": "02:24:06", "outcome": "ok"}],
    "loadSkill_calls": [{"name": "editor-ui", "status": "ok", "ts": "02:22:59"}],
    "exec_steps": 11,
    "before_exec_decisions": [{"domain": "ui-domain", "skill_state": "loaded", "action": "allow", "ts": "02:23:33"}],
    "step_markers": 12,
    "parse_warnings": ["..."]
  }
"""
import json
import re
import sys
from pathlib import Path

# 事件行：[HH:mm:ss] <rest>
RE_EVENT = re.compile(r"^\[(\d{2}:\d{2}:\d{2})\] (.+)$")
# TURN BEGIN [id]
RE_TURN_BEGIN = re.compile(r"^TURN BEGIN \[([^\]]+)\]$")
# TURN END <outcome>
RE_TURN_END = re.compile(r"^TURN END (\S+)")
# --- step N ---
RE_STEP = re.compile(r"^--- step (\d+) ---$")
# before-exec 决策行（缩进）：<domain>, skill=<state> → <action>（→ 为 U+2192）
RE_BEFORE_EXEC_DECISION = re.compile(r"^\s+([^,]+?),\s*skill=(.+?)\s*→\s*(.+?)\s*$")
# status: loadSkill: <name> <ok|fail>
RE_LOADSKILL_STATUS = re.compile(r"^status: loadSkill: (\S+) (ok|fail)$")
# llm-prepare reminder_in_history=N reminder_in_llm=M
RE_LLM_PREPARE = re.compile(
    r"^llm-prepare reminder_in_history=(\d+) reminder_in_llm=(\d+)$"
)
# tool_call 后跟 loadSkill(name) 缩进行
RE_LOADSKILL_CALL = re.compile(r"^\s+loadSkill\(([^)]+)\)\s*$")

# L2 用例断言：name -> (prompt_substr, expected_loadSkill, expected_before_exec_action)
BENCHMARK_ASSERTS = {
    "C01": ("TMP 按钮", "editor-ui", None),
    "C02": ("WndSettings", "editor-ui", None),
    "C03": ("点不了", "editor-ui-debug", None),
    "C04": ("Cube", None, None),  # 不得 load editor-ui
    "C06": ("拼 UI", None, "inject reminder"),
    "C07": ("颜色", None, None),
    "C08": ("超长", None, "inject reminder"),  # code-too-long
    "C09": ("GetComponents", None, "inject reminder"),  # heavy-reflection
    "C10": ("WndSettings", None, None),  # 反复守卫后 reminder_in_llm ≤ 1
}


def parse(log_path):
    text = Path(log_path).read_text(encoding="utf-8", errors="replace")
    lines = text.splitlines()
    warnings = []

    turns = []
    loadSkill_calls = []
    before_exec_decisions = []
    llm_prepare_stats = []
    exec_steps = 0
    step_markers = 0
    current_turn = None

    i = 0
    n = len(lines)
    while i < n:
        line = lines[i]
        m = RE_EVENT.match(line)
        if not m:
            # 非事件行：检查 step marker（无时间戳）
            ms = RE_STEP.match(line)
            if ms:
                step_markers += 1
                if current_turn is not None:
                    current_turn.setdefault("steps", 0)
                    current_turn["steps"] += 1
            i += 1
            continue

        ts, rest = m.group(1), m.group(2)

        # TURN BEGIN
        mb = RE_TURN_BEGIN.match(rest)
        if mb:
            current_turn = {"id": mb.group(1), "begin": ts, "end": None, "outcome": None, "steps": 0}
            turns.append(current_turn)
            i += 1
            continue

        # TURN END
        me = RE_TURN_END.match(rest)
        if me:
            if current_turn is not None:
                current_turn["end"] = ts
                current_turn["outcome"] = me.group(1)
            current_turn = None
            i += 1
            continue

        # before-exec：看下一非空行
        if rest == "before-exec":
            j = i + 1
            while j < n and lines[j].strip() == "":
                j += 1
            if j < n:
                md = RE_BEFORE_EXEC_DECISION.match(lines[j])
                if md:
                    before_exec_decisions.append({
                        "domain": md.group(1).strip(),
                        "skill_state": md.group(2).strip(),
                        "action": md.group(3).strip(),
                        "ts": ts,
                    })
                else:
                    warnings.append(f"before-exec at {ts}: 决策行格式不符：{lines[j][:80]!r}")
            else:
                warnings.append(f"before-exec at {ts}: 缺决策行")
            i += 1
            continue

        # status: loadSkill: <name> <ok|fail>
        ml = RE_LOADSKILL_STATUS.match(rest)
        if ml:
            loadSkill_calls.append({
                "name": ml.group(1),
                "status": ml.group(2),
                "ts": ts,
            })
            i += 1
            continue

        # llm-prepare reminder 过滤统计
        mp = RE_LLM_PREPARE.match(rest)
        if mp:
            llm_prepare_stats.append({
                "reminder_in_history": int(mp.group(1)),
                "reminder_in_llm": int(mp.group(2)),
                "ts": ts,
            })
            i += 1
            continue

        # tool_call：看下一非空行判断是否 loadSkill
        if rest == "tool_call":
            j = i + 1
            while j < n and lines[j].strip() == "":
                j += 1
            is_loadskill = False
            if j < n:
                mc = RE_LOADSKILL_CALL.match(lines[j])
                if mc:
                    is_loadskill = True
            if not is_loadskill:
                exec_steps += 1
            i += 1
            continue

        i += 1

    return {
        "turns": turns,
        "loadSkill_calls": loadSkill_calls,
        "exec_steps": exec_steps,
        "before_exec_decisions": before_exec_decisions,
        "llm_prepare_stats": llm_prepare_stats,
        "step_markers": step_markers,
        "parse_warnings": warnings,
    }


def run_assert(parsed, case_id):
    """对解析结果跑某 L2 用例断言，返回 (ok, detail)。"""
    if case_id not in BENCHMARK_ASSERTS:
        return False, f"未知用例 {case_id}"
    prompt_substr, expected_skill, expected_action = BENCHMARK_ASSERTS[case_id]

    # 用最近一个 turn 的结果代表本次 chat
    if not parsed["turns"]:
        return False, "无 turn"
    turn = parsed["turns"][-1]

    detail_parts = []
    ok = True

    if expected_skill is not None:
        # 期望 loadSkill 了某 skill 且 ok
        found = any(c["name"] == expected_skill and c["status"] == "ok" for c in parsed["loadSkill_calls"])
        detail_parts.append(f"loadSkill({expected_skill})={'found' if found else 'MISSING'}")
        ok = ok and found
    elif case_id == "C04":
        # 不得 load editor-ui
        bad = any(c["name"] == "editor-ui" for c in parsed["loadSkill_calls"])
        detail_parts.append(f"no editor-ui load={'ok' if not bad else 'VIOLATED'}")
        ok = ok and not bad

    if expected_action is not None:
        found = any(d["action"] == expected_action for d in parsed["before_exec_decisions"])
        detail_parts.append(f"before-exec {expected_action!r}={'found' if found else 'MISSING'}")
        ok = ok and found

    if case_id == "C10":
        stats = parsed.get("llm_prepare_stats") or []
        if not stats:
            detail_parts.append("llm_prepare_stats=MISSING (不可观测，跳过)")
        else:
            max_reminder = max(s["reminder_in_llm"] for s in stats)
            detail_parts.append(f"max reminder_in_llm={max_reminder}")
            ok = ok and max_reminder <= 1

    detail_parts.append(f"exec_steps={parsed['exec_steps']}")
    return ok, ", ".join(detail_parts)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    log_path = sys.argv[1]
    case_id = None
    if "--assert" in sys.argv:
        idx = sys.argv.index("--assert")
        case_id = sys.argv[idx + 1]

    parsed = parse(log_path)
    if case_id:
        ok, detail = run_assert(parsed, case_id)
        parsed["_assert"] = {"case": case_id, "ok": ok, "detail": detail}
        print(json.dumps(parsed, ensure_ascii=False, indent=2))
        sys.exit(0 if ok else 1)
    print(json.dumps(parsed, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

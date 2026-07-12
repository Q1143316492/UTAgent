"""LLM Agent 状态层（Python 侧）。

对照 puerts 的 main.mjs：history/状态留 Python，编排由 C# 主线程状态机驱动
（见 design D5）。本模块不再做 HTTP；只暴露单步原子入口给 C#：

- configure / clear_history / abort / is_configured / get_history_length
- append_user(text, image_base64, mime)         用户消息入历史（含图片拼装）
- get_system_prompt()                            暴露 system prompt 给 C#
- get_messages_json()                            C# 调 LLM 前取 messages
- append_assistant_content(text)                 最终文本 assistant 消息
- append_assistant_tool_calls(tool_calls_json, reasoning_content=None)   assistant 含 tool_calls
- append_tool_result(tool_call_id, content)      role: tool 回灌
- execute_python_code(code)                      执行 execPython 参数中的代码
- load_skill(skill_name)                         loadSkill tool：读 skills/*.md.txt 全文
- inject_max_steps_message()                     maxSteps 收尾指导
- process_pending_images()                       截图 __image 注入 user 多模态
- build_llm_messages_json()                      C# 调 LLM 前取完整 messages（含 system）
- continue_turn()                                   从当前 history 续跑（不 begin_turn）

入口约定：除 append_user/get_messages_json 返回 JSON 外，其余只 print 一次 JSON 结果，
写到 C# __pybridge__ sink。LLM 生成代码的 stdout 在 _run_code 内用局部 StringIO 捕获，
不污染 sink。
"""

import ast
import io
import os
import re
import sys
import json
import traceback
import builtins
import copy

# AGENT_API_VERSION：仅诊断用；模块刷新由 App.sync_runtime_modules（磁盘 mtime）驱动。
AGENT_API_VERSION = 7

_SCENE_OBJECT_NAME_RE = re.compile(r'GameObject\s*\(\s*["\']([^"\']+)["\']')

# ----- 模块级状态（sys.modules 持久化，跨多次 Exec 保留） -----
_ORCHESTRATION_VERSION = "tools-v2"
_configured = False
_history = []
_abort_flag = False
_max_steps = 0  # 0 = 无限，由 C# 状态机的步数计数器控制
_api_key = ""
_base_url = ""
_model = ""
_turn_system_prompt = None
_turn_start_len = 0   # 保留字段；v1 Stop 不回滚，仅 configure/clear 重置 history
_exec_globals = None  # ScriptEnv 等价：跨 execPython step 持久

# prepareStep 常量（对标 puerts handlePrepareStep）
CONTINUE_NUDGE_MESSAGE = "请根据当前对话上下文继续完成任务。"
MAX_STEPS_MESSAGE = (
    "【系统】你已达到本任务允许的最大步数上限。"
    "请不要再调用任何工具（execPython）。"
    "请用纯文本总结：你已经完成的工作、关键结果、以及若任务未彻底完成则剩余建议。"
)
SCREENSHOT_USER_PROMPT = (
    "上方是 Unity 截图（Game 或 Scene View）。"
    "请结合截图内容与之前的任务，用自然语言描述你看到的内容并回答用户问题。"
)
TEXT_ONLY_IMAGE_PLACEHOLDER = (
    "[截图已捕获，但当前模型不支持视觉输入，未送入 LLM。"
    "请根据工具 stdout / 场景验收结果向用户汇报。]"
)
COMPACTED_CONTEXT_PLACEHOLDER = (
    "[Compacted Context] 较早的对话已裁剪以节省 token；请基于最近消息继续。"
)
TOOL_OUTPUT_TRUNCATE_CHARS = 500
DEFAULT_MAX_INPUT_TOKENS = 100000
DEFAULT_MIN_KEEP_MESSAGES = 20


# ----- 入口：configure / clear / abort / 查询 -----

def ensure_model(model):
    """同步当前模型名（不清 history）。C# 每步 LLM 前调用，避免模块重载后 _model 丢失。"""
    global _model
    if isinstance(model, str) and model.strip():
        _model = model.strip()
    print(json.dumps({"ok": True, "value": _model}, ensure_ascii=False))


def configure(api_key, base_url, model, max_steps):
    """配置 Agent。重置历史与中止标志，标记编排版本。结果经 print 写入 sink。"""
    global _configured, _api_key, _base_url, _model, _max_steps
    global _history, _abort_flag, _turn_start_len, _turn_system_prompt
    _reset_exec_globals()
    _turn_system_prompt = None
    _api_key = api_key or ""
    _base_url = (base_url or "").strip()
    _model = (model or "").strip()
    try:
        _max_steps = int(max_steps)
    except (TypeError, ValueError):
        _max_steps = 0
    if _max_steps < 0:
        _max_steps = 0
    _history = []
    _abort_flag = False
    _turn_start_len = 0
    _configured = bool(_api_key)
    if not _configured:
        print(json.dumps({"ok": False, "message": "未配置 API Key"}, ensure_ascii=False))
        return
    print(json.dumps({
        "ok": True,
        "message": f"已配置 model={_model or 'default'}, max_steps={_max_steps}",
        "orchestration": _ORCHESTRATION_VERSION,
    }, ensure_ascii=False))


def clear_history():
    """清空对话历史。"""
    global _history, _turn_start_len, _turn_system_prompt
    _history = []
    _turn_start_len = 0
    _turn_system_prompt = None
    _reset_exec_globals()
    print(json.dumps({"ok": True, "message": "history cleared"}, ensure_ascii=False))


def abort():
    """协作式暂停：置标志位。C# 侧同时 UnityWebRequest.Abort()；不回滚 history。"""
    global _abort_flag
    _abort_flag = True
    print(json.dumps({"ok": True, "message": "abort requested"}, ensure_ascii=False))


def continue_turn():
    """从当前 history 续跑：清暂停标志、修复尾部，不追加原任务 user 消息。"""
    global _abort_flag
    _abort_flag = False
    if not _history:
        print(json.dumps({"ok": False, "message": "history empty"}, ensure_ascii=False))
        return
    _repair_history_tail_for_continue()
    _repair_tool_call_sequences(_history)
    _sanitize_messages_for_llm_api(_history, _model)
    print(json.dumps({"ok": True, "history_len": len(_history)}, ensure_ascii=False))


def _repair_history_tail_for_continue():
    """修复 history 尾部，使下一轮 LLM 请求合法。"""
    global _history
    if not _history:
        return
    last = _history[-1]
    role = last.get("role")
    if role == "assistant":
        if last.get("tool_calls"):
            _history.pop()
            return
        _history.append({"role": "user", "content": CONTINUE_NUDGE_MESSAGE})
    elif role not in ("user", "tool"):
        pass


def _repair_tool_call_sequences(messages):
    """补齐缺失的 tool 结果、剔除孤立 tool，修正 assistant content=null。"""
    if not messages:
        return
    repaired = []
    i = 0
    while i < len(messages):
        msg = messages[i]
        role = msg.get("role")
        if role == "assistant" and msg.get("tool_calls"):
            assistant = dict(msg)
            if assistant.get("content") is None:
                assistant["content"] = ""
            repaired.append(assistant)
            needed = []
            for tc in assistant.get("tool_calls") or []:
                if not isinstance(tc, dict):
                    continue
                tid = tc.get("id")
                if tid:
                    needed.append(tid)
            j = i + 1
            found = {}
            while j < len(messages) and messages[j].get("role") == "tool":
                tid = messages[j].get("tool_call_id")
                if tid in needed and tid not in found:
                    found[tid] = messages[j]
                j += 1
            missing = False
            for tid in needed:
                if tid in found:
                    repaired.append(found[tid])
                else:
                    repaired.append({
                        "role": "tool",
                        "tool_call_id": tid,
                        "content": json.dumps(
                            {
                                "success": False,
                                "error": "tool result missing (interrupted or trimmed)",
                            },
                            ensure_ascii=False,
                        ),
                    })
            i = j
            continue
        if role == "tool":
            i += 1
            continue
        if msg.get("content") is None and role == "assistant":
            msg = dict(msg)
            msg["content"] = ""
        repaired.append(msg)
        i += 1
    messages[:] = repaired


def _align_safe_trim_start(messages, start):
    """emergency trim 时避免从孤立 tool 或半截 tool 组中间切开。"""
    n = len(messages)
    start = max(0, min(start, n))
    while start < n and messages[start].get("role") == "tool":
        start += 1
    if start > 0:
        prev = messages[start - 1]
        if prev.get("role") == "assistant" and prev.get("tool_calls"):
            start -= 1
    return start


def is_configured():
    print(json.dumps({"ok": True, "value": _configured}, ensure_ascii=False))


def get_history_length():
    print(json.dumps({"ok": True, "value": len(_history)}, ensure_ascii=False))


# ----- 单步原子入口（C# 状态机调用） -----

def begin_turn(text, image_base64="", mime_type=""):
    """开始一轮：标记本轮起点、追加 user 消息（含图片拼装）。清中止标志。"""
    global _abort_flag, _turn_start_len, _turn_system_prompt
    _abort_flag = False
    _turn_start_len = len(_history)
    _turn_system_prompt = _build_system_prompt()
    if image_base64:
        user_msg = {
            "role": "user",
            "content": [
                {"type": "text", "text": text},
                {"type": "image_url",
                 "image_url": {"url": f"data:{mime_type or 'image/png'};base64,{image_base64}"}},
            ],
        }
    else:
        user_msg = {"role": "user", "content": text}
    _history.append(user_msg)
    print(json.dumps({"ok": True}, ensure_ascii=False))


def get_system_prompt():
    """返回 system prompt 文本（C# 把它放进 messages[0]）。"""
    prompt = _turn_system_prompt if _turn_system_prompt else _build_system_prompt()
    print(json.dumps({"ok": True, "value": prompt}, ensure_ascii=False))


def get_messages_json():
    """返回 messages（不含 system，C# 自己放 system 到 index 0）。"""
    print(json.dumps({"ok": True, "value": _history}, ensure_ascii=False))


def inject_max_steps_message():
    """达 maxSteps 时注入收尾指导 user 消息（puerts MAX_STEPS_MESSAGE 等价）。"""
    _history.append({"role": "user", "content": MAX_STEPS_MESSAGE})
    print(json.dumps({"ok": True}, ensure_ascii=False))


def process_pending_images():
    """从最近 tool result 剥离 __image，注入 user 多模态消息。"""
    injected = 0
    for i in range(len(_history) - 1, -1, -1):
        msg = _history[i]
        if msg.get("role") != "tool":
            continue
        content = msg.get("content", "")
        if not content:
            continue
        try:
            data = json.loads(content) if isinstance(content, str) else content
        except (TypeError, json.JSONDecodeError):
            continue
        if not isinstance(data, dict):
            continue
        image = data.get("__image")
        if not isinstance(image, dict):
            continue
        base64 = image.get("base64")
        if not base64:
            continue
        media_type = image.get("mediaType") or "image/png"
        stripped = dict(data)
        stripped.pop("__image", None)
        if not _model_supports_vision(_model):
            stripped["_image_note"] = (
                "Screenshot captured. Current model is text-only; image was not sent to LLM."
            )
            msg["content"] = json.dumps(stripped, ensure_ascii=False)
            break
        stripped["_image_note"] = "Screenshot captured (image attached separately)."
        msg["content"] = json.dumps(stripped, ensure_ascii=False)
        _history.append({
            "role": "user",
            "content": [
                {
                    "type": "image_url",
                    "image_url": {"url": f"data:{media_type};base64,{base64}"},
                },
                {"type": "text", "text": SCREENSHOT_USER_PROMPT},
            ],
        })
        injected += 1
        break
    print(json.dumps({"ok": True, "injected": injected}, ensure_ascii=False))


def prepare_history_for_step(step_number, max_input_tokens, min_keep_messages):
    """步前历史变换：裁剪旧 tool 输出、必要时 emergency trim。返回副本，不改 _history。"""
    messages, pruned_chars, estimated, emergency_trim = _prepare_messages_copy(
        step_number, max_input_tokens, min_keep_messages
    )
    print(json.dumps({
        "ok": True,
        "value": messages,
        "pruned_chars": pruned_chars,
        "estimated_tokens": estimated,
        "emergency_trim": emergency_trim,
        "step_number": int(step_number) if step_number is not None else 0,
    }, ensure_ascii=False))


def build_llm_messages_json(step_number, max_input_tokens, min_keep_messages, model=""):
    """构建含 system 的完整 messages 数组。最后一行 print 为数组 JSON（供 C# 直接嵌入请求体）。

    model: 当前 LLM 模型名（由 C# EditorPrefs 传入，避免 agent 模块刷新后 _model 丢失）。
    """
    global _model
    if isinstance(model, str) and model.strip():
        _model = model.strip()
    active_model = _model

    messages, pruned_chars, estimated, emergency_trim = _prepare_messages_copy(
        step_number, max_input_tokens, min_keep_messages, active_model
    )
    system_content = _turn_system_prompt if _turn_system_prompt else _build_system_prompt()
    full = [{"role": "system", "content": system_content}] + messages
    _sanitize_messages_for_llm_api(full, active_model)
    print(json.dumps({
        "ok": True,
        "pruned_chars": pruned_chars,
        "estimated_tokens": estimated,
        "emergency_trim": emergency_trim,
        "step_number": int(step_number) if step_number is not None else 0,
    }, ensure_ascii=False))
    print(json.dumps(full, ensure_ascii=False))


def append_assistant_content(text, reasoning_content=None):
    """追加纯文本 assistant 消息（最终回复）。"""
    entry = {"role": "assistant", "content": text}
    if reasoning_content is not None:
        entry["reasoning_content"] = reasoning_content
    elif _uses_deepseek_reasoning_replay(_model):
        entry["reasoning_content"] = ""
    _history.append(entry)
    print(json.dumps({"ok": True}, ensure_ascii=False))


def append_assistant_tool_calls(tool_calls_json, reasoning_content=None):
    """追加含 tool_calls 的 assistant 消息。tool_calls_json 为 JSON 数组字符串。"""
    tool_calls = json.loads(tool_calls_json)
    entry = {
        "role": "assistant",
        "content": "",
        "tool_calls": tool_calls,
    }
    if reasoning_content is not None:
        entry["reasoning_content"] = reasoning_content
    elif _uses_deepseek_reasoning_replay(_model):
        # DeepSeek V4 thinking + tools：历史回放必须带 reasoning_content（可为空串）
        entry["reasoning_content"] = ""
    _history.append(entry)
    print(json.dumps({"ok": True}, ensure_ascii=False))


def append_tool_result(tool_call_id, content):
    """追加 role: tool 结果消息。content 为 JSON 字符串。"""
    _history.append({
        "role": "tool",
        "tool_call_id": tool_call_id,
        "content": content,
    })
    print(json.dumps({"ok": True}, ensure_ascii=False))


def execute_python_code(code):
    """执行 execPython 的 code 参数，返回 JSON 结果字符串（不写 history）。"""
    if _abort_flag:
        print(json.dumps({"ok": False, "aborted": True}, ensure_ascii=False))
        return
    out, err, ret_val, partial_last_expr_failed = _run_code(code)
    success = not err.strip()
    result = {"success": success, "stdout": out}
    if ret_val is not None:
        if isinstance(ret_val, dict):
            for key, val in ret_val.items():
                if key == "__image" or key in ("success", "message"):
                    result[key] = val
        else:
            result["result"] = ret_val
    if err.strip():
        result["stderr"] = err.strip()
        result["error"] = err.strip()
        _enrich_exec_failure_result(result, code, partial_last_expr_failed)
    content_json = json.dumps(result, ensure_ascii=False)
    preview = content_json
    if len(preview) > 200:
        preview = preview[:200] + "..."
    print(json.dumps({
        "ok": True,
        "content": content_json,
        "preview": preview,
    }, ensure_ascii=False))


def load_skill(skill_name):
    """loadSkill tool：读取 skills/{name}.md.txt 全文，返回 JSON（不写 history）。"""
    name = (skill_name or "").strip()
    if not name:
        result = {"ok": False, "error": "skill name required"}
        content_json = json.dumps(result, ensure_ascii=False)
        print(json.dumps({
            "ok": True,
            "content": content_json,
            "preview": "loadSkill: missing name",
            "skill_ok": False,
        }, ensure_ascii=False))
        return
    # 防止路径穿越
    if os.path.basename(name) != name or ".." in name:
        result = {"ok": False, "error": f"invalid skill name: {name}"}
        content_json = json.dumps(result, ensure_ascii=False)
        print(json.dumps({
            "ok": True,
            "content": content_json,
            "preview": f"loadSkill({name}): invalid",
            "skill_ok": False,
        }, ensure_ascii=False))
        return
    content = _read_skill_file(name)
    if not content:
        result = {"ok": False, "error": f"skill not found: {name}"}
        content_json = json.dumps(result, ensure_ascii=False)
        print(json.dumps({
            "ok": True,
            "content": content_json,
            "preview": f"loadSkill({name}): not found",
            "skill_ok": False,
        }, ensure_ascii=False))
        return
    result = {"ok": True, "skill": name, "content": content}
    content_json = json.dumps(result, ensure_ascii=False)
    preview = f"loadSkill({name}): {len(content)} chars"
    print(json.dumps({
        "ok": True,
        "content": content_json,
        "preview": preview,
        "skill_ok": True,
    }, ensure_ascii=False))


def finalize_error(message):
    """不可恢复失败：回滚本轮（仅硬 max_steps 等兜底路径）。"""
    _rollback_turn()
    print(json.dumps({"ok": True, "done": True, "error": message,
                      "final_text": ""}, ensure_ascii=False))


def _rollback_turn():
    """把 _history 回滚到 _turn_start_len（仅 finalize_error / 硬失败）。"""
    global _history
    _history = _history[:_turn_start_len]


# ----- 代码执行 -----

def _reset_exec_globals():
    """configure / clear_history 时重置 ScriptEnv 等价命名空间。"""
    global _exec_globals
    _exec_globals = None


def _seed_exec_globals(ns):
    """预注入 builtins 层模块（对标 Puerts eval 环境注入）。"""
    exec("import unity\nfrom unity_bind import CS\n", ns)


def _get_exec_globals():
    global _exec_globals
    if _exec_globals is None:
        _exec_globals = {"__builtins__": builtins.__dict__}
        _seed_exec_globals(_exec_globals)
    return _exec_globals


def _extract_scene_object_names(code):
    """从脚本中提取 new GameObject(\"name\") 的目标名（用于失败后的场景计数）。"""
    if not isinstance(code, str) or not code.strip():
        return []
    seen = set()
    names = []
    for match in _SCENE_OBJECT_NAME_RE.finditer(code):
        name = match.group(1).strip()
        if not name or name in seen:
            continue
        seen.add(name)
        names.append(name)
    return names


def _scene_object_counts(names):
    """查询活动场景中各名称的对象数量（失败回灌用）。"""
    if not names:
        return {}
    import unity
    counts = {}
    for name in names:
        try:
            payload = unity.find_objects(name, echo=False)
            counts[name] = int(payload.get("count", 0))
        except Exception:
            counts[name] = -1
    return counts


def _build_exec_failure_hint(partial_last_expr_failed, scene_counts, stderr=""):
    """生成 execPython 失败时的可执行提示（避免 Agent 整段重建导致重名叠加）。"""
    parts = []
    recommended = "retry_carefully"
    if partial_last_expr_failed:
        parts.append(
            "前缀语句已执行，仅最后一行表达式失败；场景对象可能已创建或已修改。"
        )
    else:
        parts.append("脚本中途失败时 Unity 不会回滚已创建的对象。")

    duplicates = {name: count for name, count in scene_counts.items() if count > 1}
    singles = {name: count for name, count in scene_counts.items() if count == 1}
    if duplicates:
        recommended = "destroy_all_then_single_create"
        parts.append(
            "检测到重名对象 "
            + json.dumps(duplicates, ensure_ascii=False)
            + "。请 unity.destroy_all_objects(名称) 后只执行一次创建脚本。"
        )
    elif singles:
        recommended = "patch_existing_only"
        parts.append(
            "场景中已有且仅 1 个目标对象（部分提交）。"
            "下一步只允许修补：在同一对象上加子物体/补组件/改属性；"
            "禁止 prepare_scene_object、禁止 destroy_all 后整段 new GameObject 重建。"
        )
        if "NoneType" in stderr or "AddComponent" in stderr:
            parts.append(
                "若为 AddComponent 返回 None：检查是否在同一 GameObject 上叠了多个 Graphic"
                "（Image 与 Text/TMP 不能同 GO，文字应放在子物体）。"
            )

    parts.append("验收：unity.find_objects(名称)[\"count\"] == 1。")
    return " ".join(parts), recommended


def _enrich_exec_failure_result(result, code, partial_last_expr_failed):
    """为失败的 execPython 结果附加 partial_success / scene_object_counts / hint。"""
    if partial_last_expr_failed:
        result["partial_success"] = True

    stderr = result.get("stderr") or result.get("error") or ""
    names = _extract_scene_object_names(code)
    if not names:
        hint, action = _build_exec_failure_hint(partial_last_expr_failed, {}, stderr)
        result["hint"] = hint
        result["recommended_action"] = action
        return

    scene_counts = _scene_object_counts(names)
    if scene_counts:
        result["scene_object_counts"] = scene_counts
    hint, action = _build_exec_failure_hint(
        partial_last_expr_failed, scene_counts, stderr
    )
    result["hint"] = hint
    result["recommended_action"] = action


def _run_code(code):
    """在持久命名空间执行代码（对标 ScriptEnv），返回 (stdout, error, last_expr_value, partial_last_expr_failed)。"""
    out = io.StringIO()
    err = io.StringIO()
    old_print = builtins.print
    old_stdout = sys.stdout
    old_stderr = sys.stderr
    ret_val = None
    partial_last_expr_failed = False
    ns = _get_exec_globals()

    def local_print(*args, sep=" ", end="\n", file=None, flush=False):
        out.write(sep.join(str(a) for a in args) + end)

    builtins.print = local_print
    sys.stdout = out
    sys.stderr = err
    try:
        tree = ast.parse(code, mode="exec")
        if tree.body and isinstance(tree.body[-1], ast.Expr):
            prefix = ast.Module(tree.body[:-1], type_ignores=[])
            last_expr = tree.body[-1].value
            if prefix.body:
                exec(compile(prefix, "<agent>", "exec"), ns)
            try:
                ret_val = eval(compile(ast.Expression(last_expr), "<agent>", "eval"), ns)
            except SystemExit:
                pass
            except Exception:
                partial_last_expr_failed = bool(prefix.body)
                err.write(traceback.format_exc())
        else:
            exec(code, ns)
    except SyntaxError:
        try:
            exec(code, ns)
        except SystemExit:
            pass
        except Exception:
            err.write(traceback.format_exc())
    except SystemExit:
        pass
    except Exception:
        err.write(traceback.format_exc())
    finally:
        builtins.print = old_print
        sys.stdout = old_stdout
        sys.stderr = old_stderr
    return out.getvalue(), err.getvalue(), ret_val, partial_last_expr_failed


def _message_text_for_token_estimate(msg):
    """估算单条 message 的文本长度（含 tool_calls / 多模态占位）。"""
    content = msg.get("content")
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        parts = []
        for block in content:
            if not isinstance(block, dict):
                continue
            if block.get("type") == "text":
                parts.append(str(block.get("text", "")))
            elif block.get("type") == "image_url":
                parts.append("[image]")
        return "\n".join(parts)
    tool_calls = msg.get("tool_calls")
    if tool_calls:
        return json.dumps(tool_calls, ensure_ascii=False)
    return ""


def _estimate_messages_tokens(messages):
    total_chars = 0
    for msg in messages:
        total_chars += len(_message_text_for_token_estimate(msg))
    return max(1, total_chars // 4)


def _prune_old_tool_outputs(messages, max_chars):
    """从最旧 role:tool 开始截断 content。"""
    pruned = 0
    for msg in messages:
        if msg.get("role") != "tool":
            continue
        content = msg.get("content", "")
        if not isinstance(content, str):
            content = json.dumps(content, ensure_ascii=False) if content is not None else ""
        if len(content) <= max_chars:
            continue
        orig_len = len(content)
        msg["content"] = (
            content[:max_chars] + f"... ({orig_len - max_chars} chars truncated)"
        )
        pruned += orig_len - max_chars
    return pruned


def _prepare_messages_copy(step_number, max_input_tokens, min_keep_messages, model=""):
    """步前历史变换副本（不改 _history）。"""
    try:
        max_tokens = int(max_input_tokens)
    except (TypeError, ValueError):
        max_tokens = DEFAULT_MAX_INPUT_TOKENS
    try:
        keep_n = int(min_keep_messages)
    except (TypeError, ValueError):
        keep_n = DEFAULT_MIN_KEEP_MESSAGES
    if max_tokens <= 0:
        max_tokens = DEFAULT_MAX_INPUT_TOKENS
    if keep_n <= 0:
        keep_n = DEFAULT_MIN_KEEP_MESSAGES

    messages = copy.deepcopy(_history)
    pruned_chars = 0
    estimated = _estimate_messages_tokens(messages)
    emergency_trim = False
    if estimated > max_tokens:
        pruned_chars = _prune_old_tool_outputs(messages, TOOL_OUTPUT_TRUNCATE_CHARS)
        estimated = _estimate_messages_tokens(messages)
    if estimated > max_tokens:
        emergency_trim = True
        _emergency_trim(messages, keep_n)
        estimated = _estimate_messages_tokens(messages)
    _repair_tool_call_sequences(messages)
    _sanitize_messages_for_llm_api(messages, model)
    return messages, pruned_chars, estimated, emergency_trim


def _sanitize_messages_for_llm_api(messages, model):
    """发送前规范化 OpenAI messages（DeepSeek V4 thinking + tool 回放 + text-only 视觉剥离）。"""
    use_reasoning = _uses_deepseek_reasoning_replay(model)
    if not _model_supports_vision(model):
        _strip_vision_content_for_text_only(messages)
    for msg in messages:
        role = msg.get("role")
        if role == "assistant":
            if msg.get("content") is None:
                msg["content"] = ""
            if use_reasoning and "reasoning_content" not in msg:
                msg["reasoning_content"] = ""
            elif use_reasoning and msg.get("reasoning_content") is None:
                msg["reasoning_content"] = ""
        elif role == "tool":
            content = msg.get("content")
            if content is None:
                msg["content"] = ""
            elif not isinstance(content, str):
                msg["content"] = json.dumps(content, ensure_ascii=False)


def _uses_deepseek_reasoning_replay(model):
    """DeepSeek V4 / 旧 reasoning 模型在多轮 tool 回放时要求 reasoning_content 字段。"""
    name = (model or "").strip().lower()
    if not name:
        return False
    return "deepseek-v4" in name or name in ("deepseek-chat", "deepseek-reasoner")


def _model_supports_vision(model):
    """当前模型是否支持 OpenAI 风格 image_url 多模态输入。"""
    name = (model or "").strip().lower()
    if not name:
        return False
    if "deepseek" in name:
        return False
    vision_hints = (
        "gpt-4o", "gpt-4.1", "gpt-5", "claude", "gemini",
        "vision", "qwen-vl", "qwen2-vl", "qwen3-vl",
    )
    return any(hint in name for hint in vision_hints)


def _strip_vision_content_for_text_only(messages):
    """text-only 模型：剥离 image_url / image，避免 API 400 unknown variant image_url。"""
    for msg in messages:
        content = msg.get("content")
        if not isinstance(content, list):
            continue
        parts = []
        for block in content:
            if not isinstance(block, dict):
                continue
            btype = block.get("type")
            if btype == "text":
                text = block.get("text", "")
                if text:
                    parts.append(text)
            elif btype in ("image_url", "image"):
                parts.append(TEXT_ONLY_IMAGE_PLACEHOLDER)
        if parts:
            msg["content"] = "\n".join(parts)
        else:
            msg["content"] = TEXT_ONLY_IMAGE_PLACEHOLDER


def _emergency_trim(messages, keep_n):
    """保留最近 N 条，开头插入压缩占位 user 消息。"""
    if len(messages) <= keep_n:
        return
    start = _align_safe_trim_start(messages, len(messages) - keep_n)
    kept = messages[start:]
    messages.clear()
    messages.append({"role": "user", "content": COMPACTED_CONTEXT_PLACEHOLDER})
    messages.extend(kept)


# ----- System prompt 与 skill -----

_SKILL_DIR = os.path.join(os.path.dirname(__file__), "skills")
_BASE_SKILL = "python-interop"


def _parse_skill_frontmatter(text):
    """解析 YAML frontmatter 的 name / description。"""
    if not text or not text.startswith("---"):
        return None, None
    end = text.find("---", 3)
    if end < 0:
        return None, None
    block = text[3:end]
    name = None
    desc = None
    for line in block.splitlines():
        line = line.strip()
        if line.startswith("name:"):
            name = line.split(":", 1)[1].strip().strip('"').strip("'")
        elif line.startswith("description:"):
            desc = line.split(":", 1)[1].strip().strip('"').strip("'")
    return name, desc


def _read_skill_file(skill_name):
    skill_path = os.path.join(_SKILL_DIR, f"{skill_name}.md.txt")
    try:
        with open(skill_path, "r", encoding="utf-8") as f:
            return f.read()
    except Exception:
        return ""


def _list_skill_catalog():
    lines = [
        "## 可用 Skill（通过 loadSkill tool 按需加载）",
    ]
    entries = []
    if os.path.isdir(_SKILL_DIR):
        for fname in sorted(os.listdir(_SKILL_DIR)):
            if not fname.endswith(".md.txt"):
                continue
            skill_id = fname[:-7]
            if skill_id == _BASE_SKILL:
                continue
            content = _read_skill_file(skill_id)
            fm_name, fm_desc = _parse_skill_frontmatter(content)
            display = fm_name or skill_id
            if fm_desc:
                entries.append(f"- `{display}` — {fm_desc}")
            else:
                entries.append(f"- `{display}`")
    if not entries:
        lines.append("- （无领域 skill）")
    else:
        lines.extend(entries)
    lines.append("")
    lines.append(
        "Editor 拼/改 Canvas UI、预制体、布局：**必须先** `loadSkill(\"editor-ui\")`，再 `execPython`。"
    )
    return "\n".join(lines)


def _build_system_prompt():
    parts = [
        "你是一个运行在 Unity Editor 内的 **UT Agent**。"
        "你必须通过 **execPython** 工具执行 Python 来操作 Unity（在 code 参数里 `import unity`）。"
        "领域规范通过 **loadSkill** tool 按需加载（见下方目录）。"
        "不要在正文里贴 ```python 代码块；纯文本仅用于给用户的最终说明。"
    ]
    interop = _read_skill_file(_BASE_SKILL)
    if interop:
        parts.append(interop)
    parts.append(_list_skill_catalog())
    parts.append(_verb_summary())
    return "\n\n".join(parts)


def _read_skill():
    return _read_skill_file(_BASE_SKILL)


def _verb_summary():
    return (
        "## 互操作三层（优先顺序）\n"
        "L1 `import unity` — 动词：find_object, get_hierarchy, capture_scene_view, scene_view_*, save_scene, …\n"
        "L2 `unity.list_editor_namespaces()` / `get_type_details(types)` — 过滤后自省（禁止首步 help/dir）\n"
        "L3 `from unity_bind import CS` — 对标 Puerts `CS.*`（CS.UnityEngine.GameObject…）\n"
        "\n"
        "## unity 常用动词（L1 简摘）\n"
        "- unity.list_editor_namespaces() / list_types_in_namespace(ns) / get_type_details(types)\n"
        "- unity.get_logs(count=20) / capture_scene_view() / find_object(name) / find_objects(name) / get_hierarchy(name)\n"
        "- unity.scene_view_zoom/pan/orbit / focus_scene_view_on(name) / select_game_object(name)\n"
        "- unity.create_cube / destroy_object / prepare_scene_object / destroy_all_objects / save_scene / log(msg)\n"
        "完整签名：help(unity.xxx)。勿 print 截图返回值。\n"
        "\n"
        "## unity_bind CS（L3，动词不够时）\n"
        "- from unity_bind import CS\n"
        "- CS.UnityEngine.GameObject.Find / new GameObject / AddComponent\n"
        "- CS.TMPro.TextMeshProUGUI — tmp.text 设文案\n"
        "- Editor 创建 UI 优先单步完整 execPython 脚本\n"
        "禁止 import clr / import _cs_bridge / 读 Editor/Bridges 源码。\n"
        "\n"
        "## unity.ui.core（仅 Play 面板，非 Editor 创建 UI 替补）\n"
        "- WndBase.get → get_widget → set_text / set_visible\n"
        "- WindowManager.get().open('WndCreateRole') — 仅 Play\n"
        "- 改业务：Assets/UTAgent/Scripts/*.py"
    )

"""unity.console — 日志读取与 Debug 输出（Unity Console）。"""

from ._common import _bridge


def get_logs(count=20, log_type="all"):
    """获取最近的 Unity Console 日志条目。

    count: 1-50 整数，默认 20
    log_type: "all" / "error" / "warning" / "log"，默认 "all"
    返回 [{"timestamp": ..., "type": ..., "message": ..., "stackTrace": ...}, ...]
    """
    if not isinstance(count, int) or count < 1 or count > 50:
        raise ValueError(f"get_logs: 'count' 必须是 1-50 的整数（got {count}）。读 help(unity.get_logs)")
    if log_type not in ("all", "error", "warning", "log"):
        raise ValueError(
            f"get_logs: 'log_type' 必须是 all/error/warning/log（got {log_type}）。读 help(unity.get_logs)"
        )
    import json
    return json.loads(_bridge().GetRecentLogs(count, log_type))


def get_log_summary():
    """获取按类型分组的日志计数。

    返回 {"log": int, "warning": int, "error": int, "total": int}
    """
    import json
    return json.loads(_bridge().GetLogSummary())


def log(message):
    """输出到 Unity Console（UnityEngine.Debug.Log）。

    这是 LLM 的文本输出通道（仅 Console，不进 execPython stdout，不能当侦察依据）。
    """
    _bridge().Log(str(message))


def log_warning(message):
    """输出警告到 Unity Console（Debug.LogWarning）。"""
    _bridge().LogWarning(str(message))


def log_error(message):
    """输出错误到 Unity Console（Debug.LogError）。"""
    _bridge().LogError(str(message))

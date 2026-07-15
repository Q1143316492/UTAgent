"""低频定时器，替代默认关闭的 update dispatch。"""

import time


class _TimerHub:
    def __init__(self):
        self._timers = []
        self._next_id = 1

    def add(self, interval_sec, callback):
        timer_id = self._next_id
        self._next_id += 1
        entry = {
            "id": timer_id,
            "interval": float(interval_sec),
            "callback": callback,
            "elapsed": 0.0,
            "active": True,
        }
        self._timers.append(entry)
        return timer_id

    def cancel(self, timer_id):
        for entry in self._timers:
            if entry["id"] == timer_id:
                entry["active"] = False

    def clear_all(self):
        self._timers.clear()

    def tick(self, delta_time):
        for entry in self._timers:
            if not entry["active"]:
                continue
            entry["elapsed"] += delta_time
            if entry["elapsed"] >= entry["interval"]:
                entry["elapsed"] = 0.0
                try:
                    entry["callback"]()
                except Exception as e:
                    import unity

                    unity.log_error(f"Timer 回调异常: {e}")


_hub = _TimerHub()


class Timer:
    """按秒间隔在主线程触发回调。"""

    def __init__(self, interval_sec, callback):
        self._id = _hub.add(interval_sec, callback)

    def cancel(self):
        _hub.cancel(self._id)


def tick_timers(delta_time):
    _hub.tick(delta_time)


def clear_all_timers():
    _hub.clear_all()

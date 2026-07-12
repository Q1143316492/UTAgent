"""UTAgent Python 核心：实例注册表与重载入口。"""

import importlib.util
import os
import re
import sys

from unity.core.timer import clear_all_timers



class App:
    """生命周期实例表与模块加载（对标 GameInstance.Reload）。"""

    _instances: dict[int, object] = {}
    _runtime_sync_mtime = 0.0

    @classmethod
    def reload(cls):
        """每次 Play 初始化：清实例/定时器，并丢弃 UTAgent 目录下的 Python 模块缓存。"""
        clear_all_timers()
        cls._instances.clear()
        cls._clear_window_manager()
        cls.purge_module_cache()

    @classmethod
    def purge_module_cache(cls):
        """剔除 C# 桥与 UTAgent 路径下可追踪的 Python 模块，不维护模块名列表。"""
        cls._purge_bridge_modules()
        cls._purge_editor_modules()
        # agent 常有 namespace 歧义，显式剔除
        sys.modules.pop("agent", None)

    @classmethod
    def sync_runtime_modules(cls, force=False):
        """按 Runtime/Scripts 下 .py 的磁盘 mtime 决定是否刷新模块缓存。

        force=True：无条件 purge（初始化引擎 / 手动刷新）。
        force=False：仅当磁盘比上次 sync 新时才 purge（开窗口、每次 Exec 前）。
        返回是否执行了 purge。
        """
        dirs = cls._find_utagent_dirs()
        if dirs is None:
            return False

        _, runtime_dir, scripts_dir = dirs
        latest = max(
            cls._max_py_mtime_under(runtime_dir),
            cls._max_py_mtime_under(scripts_dir),
        )
        if not force and latest <= cls._runtime_sync_mtime:
            return False

        if force:
            clear_all_timers()
            cls._instances.clear()
            cls._clear_window_manager()

        cls.purge_module_cache()
        cls._runtime_sync_mtime = latest
        return True

    @classmethod
    def _purge_bridge_modules(cls):
        """C# 注入的桥接模块无 __file__，需显式剔除，避免持有旧 CLR 对象。"""
        for name in ("_unity_bridge", "_ui_bridge", "_wndmgr_bridge", "_cs_bridge"):
            sys.modules.pop(name, None)

    @classmethod
    def _clear_window_manager(cls):
        try:
            from unity.ui.core.wnd_mgr import WindowManager

            WindowManager.get().clear()
        except Exception:
            pass

    @classmethod
    def _purge_editor_modules(cls):
        """按磁盘路径剔除缓存，不维护模块名列表。"""
        dirs = cls._find_utagent_dirs()
        if dirs is None:
            return

        _, runtime_dir, scripts_dir = dirs
        runtime_root = cls._norm_path(runtime_dir)
        scripts_root = cls._norm_path(scripts_dir)

        for name in list(sys.modules.keys()):
            mod = sys.modules.get(name)
            if mod is None:
                continue

            if name.startswith("utagent_user_"):
                del sys.modules[name]
                continue

            file_path = getattr(mod, "__file__", None)
            if file_path:
                if cls._is_under(file_path, runtime_root) or cls._is_under(file_path, scripts_root):
                    del sys.modules[name]
                continue

            # 无 __file__ 的 namespace package（如仅 Runtime 在 path 时 import agent）
            mod_path = getattr(mod, "__path__", None)
            if mod_path is None:
                continue
            try:
                entries = list(mod_path)
            except TypeError:
                continue
            for entry in entries:
                if cls._is_under(entry, runtime_root) or cls._is_under(entry, scripts_root):
                    del sys.modules[name]
                    break

    @classmethod
    def _max_py_mtime_under(cls, root_dir):
        """目录树下所有 .py 的最大 mtime；目录不存在返回 0。"""
        if not root_dir or not os.path.isdir(root_dir):
            return 0.0
        latest = 0.0
        for dirpath, _, filenames in os.walk(root_dir):
            for filename in filenames:
                if not filename.endswith(".py"):
                    continue
                path = os.path.join(dirpath, filename)
                try:
                    latest = max(latest, os.path.getmtime(path))
                except OSError:
                    continue
        return latest

    @classmethod
    def create(cls, handle, type_name, module_path, class_name=""):
        try:
            if not isinstance(handle, int):
                return {"success": False, "message": "handle 必须是 int"}
            if handle in cls._instances:
                return {"success": True, "handle": handle}

            py_class = cls._load_class(module_path, class_name)
            instance = py_class(handle, type_name)
            cls._instances[handle] = instance

            if hasattr(instance, "awake"):
                instance.awake()

            return {"success": True, "handle": handle}
        except Exception as e:
            return {"success": False, "message": str(e)}

    @classmethod
    def dispatch(cls, handle, method, args=None):
        try:
            if handle not in cls._instances:
                return {"success": False, "message": f"未注册的实例 handle={handle}"}

            instance = cls._instances[handle]
            fn = getattr(instance, method, None)
            if fn is None or not callable(fn):
                return {"success": True, "skipped": True, "method": method}

            if args is None:
                fn()
            else:
                fn(args)

            return {"success": True, "method": method}
        except Exception as e:
            msg = str(e)
            try:
                import unity

                unity.log_error(f"App.dispatch({method}, handle={handle}): {msg}")
            except Exception:
                pass
            return {"success": False, "message": msg}

    @classmethod
    def destroy(cls, handle):
        try:
            instance = cls._instances.pop(handle, None)
            if instance is not None and hasattr(instance, "on_destroy"):
                instance.on_destroy()
            return {"success": True}
        except Exception as e:
            return {"success": False, "message": str(e)}

    @classmethod
    def tick_timers(cls, delta_time):
        from unity.core.timer import tick_timers

        tick_timers(delta_time)

    @classmethod
    def _load_class(cls, module_path, class_name):
        module_path = module_path.replace("\\", "/").strip()
        if not module_path:
            raise ValueError("module_path 不能为空")

        assets_root, _, _ = cls._find_utagent_dirs()
        if assets_root is None:
            raise RuntimeError("sys.path 中未找到 UTAgent/Runtime，请先初始化引擎")

        full_path = os.path.normpath(os.path.join(assets_root, module_path))
        if not os.path.isfile(full_path):
            raise FileNotFoundError(f"找不到 Python 模块：{full_path}")

        if not class_name:
            class_name = cls._class_name_from_path(module_path)

        module_name = cls._module_name_for_path(full_path)
        if module_name in sys.modules:
            del sys.modules[module_name]

        spec = importlib.util.spec_from_file_location(module_name, full_path)
        if spec is None or spec.loader is None:
            raise ImportError(f"无法加载模块：{full_path}")

        module = importlib.util.module_from_spec(spec)
        sys.modules[module_name] = module
        spec.loader.exec_module(module)

        if not hasattr(module, class_name):
            raise AttributeError(f"模块 {module_path} 中无类 {class_name}")

        return getattr(module, class_name)

    @staticmethod
    def _module_name_for_path(full_path):
        key = full_path.lower().replace("\\", "/")
        return f"utagent_user_{re.sub(r'[^a-zA-Z0-9_]', '_', key)}"

    @classmethod
    def _find_utagent_dirs(cls):
        for entry in sys.path:
            normalized = entry.replace("\\", "/")
            if not normalized.endswith("/UTAgent/Runtime"):
                continue
            runtime_dir = os.path.normpath(entry)
            assets_root = os.path.dirname(os.path.dirname(runtime_dir))
            scripts_dir = os.path.join(assets_root, "UTAgent", "Scripts")
            return assets_root, runtime_dir, scripts_dir
        return None

    @staticmethod
    def _norm_path(path):
        return os.path.normpath(path).replace("\\", "/")

    @classmethod
    def _is_under(cls, file_path, root_dir):
        try:
            return os.path.commonpath(
                [cls._norm_path(file_path), cls._norm_path(root_dir)]
            ) == cls._norm_path(root_dir)
        except ValueError:
            return False

    @staticmethod
    def _class_name_from_path(module_path):
        base = os.path.splitext(os.path.basename(module_path))[0]
        parts = re.split(r"[_\\-]+", base)
        return "".join(p[:1].upper() + p[1:] for p in parts if p)

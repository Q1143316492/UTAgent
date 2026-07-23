"""CS 命名空间动态代理（Editor exec 开放反射）。"""

import json

_CLR_READY = False
_RESOLVE_CACHE = {}


def ensure_clr():
    """加载 Unity 与游戏程序集（包内私有，LLM 禁止 import clr）。"""
    global _CLR_READY
    if _CLR_READY:
        return
    import time
    import clr

    t0 = time.perf_counter()
    bridge = _cs_bridge()
    assemblies = None
    if hasattr(bridge, "CsGetPreloadAssemblies"):
        try:
            assemblies = json.loads(bridge.CsGetPreloadAssemblies())
        except Exception:
            assemblies = None
    if not assemblies:
        assemblies = [
            "UnityEngine.CoreModule",
            "UnityEngine.UIModule",
            "UnityEngine.UI",
            "UnityEditor.CoreModule",
            "Unity.TextMeshPro",
            "Assembly-CSharp",
        ]
    for asm in assemblies:
        try:
            clr.AddReference(asm)
        except Exception:
            pass
    _CLR_READY = True
    ms = int((time.perf_counter() - t0) * 1000)
    print(f"[UTAgent][InitTiming] ensure_clr ms={ms} assemblies={len(assemblies)}")


def _cs_bridge():
    import sys

    mod = sys.modules.get("_cs_bridge")
    if mod is not None:
        return mod
    return __import__("_cs_bridge")


def _is_allowed(path):
    bridge = _cs_bridge()
    if hasattr(bridge, "CsResolveType"):
        return bool(bridge.CsIsAllowed(path))
    if not path or path.startswith("_cs_"):
        return False
    return True


def _resolve_type_info_pythonnet(full_name):
    ensure_clr()
    from System import AppDomain

    for asm in AppDomain.CurrentDomain.GetAssemblies():
        try:
            t = asm.GetType(full_name)
        except Exception:
            t = None
        if t is not None:
            return {
                "success": True,
                "fullName": str(t.FullName),
                "assemblyName": str(t.Assembly.GetName().Name),
                "kind": "class",
            }
    return {"success": False, "message": f"未找到类型：{full_name}"}


def _resolve_type_info(full_name):
    if full_name in _RESOLVE_CACHE:
        return _RESOLVE_CACHE[full_name]
    bridge = _cs_bridge()
    if hasattr(bridge, "CsResolveType"):
        raw = bridge.CsResolveType(full_name)
        info = json.loads(raw)
    else:
        info = _resolve_type_info_pythonnet(full_name)
    _RESOLVE_CACHE[full_name] = info
    return info


def _import_root_module(root_name):
    if root_name == "UnityEngine":
        import UnityEngine as root

        return root
    if root_name == "UnityEditor":
        import UnityEditor as root

        return root
    if root_name == "TMPro":
        import TMPro as root

        return root
    return __import__(root_name)


def _bind_resolved_type(info):
    import clr

    assembly = info.get("assemblyName")
    if assembly:
        try:
            clr.AddReference(assembly)
        except Exception:
            pass

    full_name = info["fullName"]
    parts = full_name.split(".")
    obj = _import_root_module(parts[0])
    for name in parts[1:]:
        obj = getattr(obj, name)
    return obj


def _try_fast_clr(parts):
    """UnityEngine / UnityEditor / TMPro 快速路径。"""
    if not parts:
        return None
    top = parts[0]
    if top not in ("UnityEngine", "UnityEditor", "TMPro"):
        return None
    try:
        obj = _import_root_module(top)
        for name in parts[1:]:
            obj = getattr(obj, name)
        return obj
    except AttributeError:
        return None


def _resolve_clr(parts):
    """将路径段解析为 pythonnet CLR 类型/模块对象。"""
    ensure_clr()
    if not parts:
        raise AttributeError("CS")

    full = ".".join(parts)
    if not _is_allowed(full):
        raise AttributeError(full)

    fast = _try_fast_clr(parts)
    if fast is not None:
        return fast

    info = _resolve_type_info(full)
    if info.get("success"):
        return _bind_resolved_type(info)

    raise AttributeError(full)


class CsNamespace:
    """链式 CS 命名空间（CS.UnityEngine.GameObject）。"""

    __slots__ = ("_parts",)

    def __init__(self, parts=()):
        self._parts = parts

    def __getattr__(self, name):
        if name.startswith("_"):
            raise AttributeError(name)
        parts = self._parts + (name,)
        full = ".".join(parts)
        if not _is_allowed(full):
            raise AttributeError(full)
        try:
            return _resolve_clr(parts)
        except AttributeError:
            return CsNamespace(parts)

    def __repr__(self):
        if not self._parts:
            return "<CS>"
        return f"<CS.{'.'.join(self._parts)}>"


def clear_resolve_cache():
    """域重载后清理 Python 侧解析缓存。"""
    global _CLR_READY
    _RESOLVE_CACHE.clear()
    _CLR_READY = False
    bridge = _cs_bridge()
    if hasattr(bridge, "CsClearResolveCache"):
        bridge.CsClearResolveCache()


CS = CsNamespace()

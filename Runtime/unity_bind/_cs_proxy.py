"""对标 Puerts csharp.mjs：CS 命名空间动态代理。"""

_CLR_READY = False

_ASSEMBLIES = (
    "UnityEngine.CoreModule",
    "UnityEngine.UIModule",
    "UnityEngine.UI",
    "UnityEditor.CoreModule",
    "Unity.TextMeshPro",
)


def ensure_clr():
    """加载 Unity 程序集（包内私有，LLM 禁止 import clr）。"""
    global _CLR_READY
    if _CLR_READY:
        return
    import clr

    for asm in _ASSEMBLIES:
        try:
            clr.AddReference(asm)
        except Exception:
            pass
    _CLR_READY = True


def _cs_bridge():
    import sys

    mod = sys.modules.get("_cs_bridge")
    if mod is not None:
        return mod
    return __import__("_cs_bridge")


def _is_allowed(path):
    return bool(_cs_bridge().CsIsAllowed(path))


def _resolve_clr(parts):
    """将路径段解析为 pythonnet CLR 类型/模块对象。"""
    ensure_clr()
    if not parts:
        raise AttributeError("CS")

    full = ".".join(parts)
    if not _is_allowed(full):
        raise AttributeError(f"不在白名单：{full}")

    top = parts[0]
    if top == "UnityEngine":
        import UnityEngine as root

        obj = root
        for name in parts[1:]:
            obj = getattr(obj, name)
        return obj

    if top == "UnityEditor":
        import UnityEditor as root

        obj = root
        for name in parts[1:]:
            obj = getattr(obj, name)
        return obj

    if top == "TMPro":
        import TMPro as root

        obj = root
        for name in parts[1:]:
            obj = getattr(obj, name)
        return obj

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


CS = CsNamespace()

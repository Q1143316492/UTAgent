"""unity.inspect — C# 类型自省（命名空间/类型/成员详情）。"""

from ._common import _agent_echo, _bridge


def list_namespaces(filter=""):
    """列出 C# 命名空间。filter 为逗号分隔前缀（如 "UnityEngine,TMPro"）；空串返回全部。

    返回 {"namespaces": [...]}。默认优先用 list_editor_namespaces()。
    """
    if filter is not None and not isinstance(filter, str):
        raise ValueError("list_namespaces: 'filter' 必须是字符串")
    import json
    return json.loads(_bridge().ListNamespaces(filter or ""))


def list_editor_namespaces():
    """列出 Editor Agent 常用命名空间（已过滤 Plastic SCM 等噪声）。

    返回 {"namespaces": ["UnityEngine", "UnityEngine.UI", "TMPro", "UnityEditor", ...]}
    """
    import json
    return json.loads(_bridge().ListEditorNamespaces())


def list_types_in_namespace(namespaces):
    """列出一个或多个命名空间下的所有公共类型（仅名字/种类，不含成员）。

    namespaces: 逗号分隔，如 "UnityEngine,UnityEngine.UI"
    返回 {"types": [{"name": "GameObject", "fullName": "UnityEngine.GameObject", "kind": "class"}, ...]}
    要看成员细节用 get_type_details。
    """
    if not isinstance(namespaces, str) or not namespaces.strip():
        raise ValueError(
            "list_types_in_namespace: 'namespaces' 必须是非空字符串（逗号分隔），"
            "如 'UnityEngine,UnityEngine.UI'。读 help(unity.list_types_in_namespace)"
        )
    import json
    return json.loads(_bridge().ListTypesInNamespace(namespaces))


def get_type_details(type_names):
    """查一个或多个 C# 类型的全部公共成员（属性/方法/字段/接口/基类/枚举值）。

    type_names: 逗号分隔的全限定名，如 "UnityEngine.Transform,UnityEngine.GameObject"
    返回 {"types": [...]}。
    """
    if not isinstance(type_names, str) or not type_names.strip():
        raise ValueError(
            "get_type_details: 'type_names' 必须是非空字符串（逗号分隔的全限定名），"
            "如 'UnityEngine.Transform,UnityEngine.GameObject'。读 help(unity.get_type_details)"
        )
    import json
    result = json.loads(_bridge().GetTypeDetails(type_names))
    _agent_echo("get_type_details", result)
    return result

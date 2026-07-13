using System;
using System.Collections.Generic;
using System.Linq;

namespace UTAgent.Editor.Config
{
    /// <summary>
    /// 类型/命名空间白名单配置，供 L2 自省与将来移动端生成器 / link.xml 使用。
    /// Editor Agent exec 的 L3 unity_bind.CS 不经此 Filter（对标 Puerts eval 全反射）。
    /// </summary>
    public static class UnityBindWhitelist
    {
        public static readonly string[] TypeNames =
        {
            "UnityEngine.Object",
            "UnityEngine.GameObject",
            "UnityEngine.Transform",
            "UnityEngine.Vector2",
            "UnityEngine.Vector3",
            "UnityEngine.RectTransform",
            "UnityEngine.UI.Button",
            "UnityEngine.UI.Image",
            "UnityEngine.UI.Text",
            "TMPro.TextMeshProUGUI",
            "TMPro.TMP_FontAsset",
            "UnityEditor.EditorUtility",
            "UnityEditor.PrefabUtility",
            "UnityEditor.Undo",
            "UnityEngine.EventSystems.EventSystem",
        };

        /// <summary>
        /// L2 自省与 CS 代理默认允许的命名空间前缀。
        /// </summary>
        public static readonly string[] EditorNamespaces =
        {
            "UnityEngine",
            "UnityEngine.UI",
            "UnityEngine.EventSystems",
            "TMPro",
            "UnityEditor",
        };

        private static readonly string[] sAllowedPrefixes = BuildAllowedPrefixes();

        /// <summary>
        /// CS 路径是否在白名单内（显式类型名或命名空间前缀）。仅供 L2 / 移动端生成器，非 Editor exec Filter。
        /// </summary>
        public static bool IsAllowedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            foreach (var typeName in TypeNames)
            {
                if (string.Equals(path, typeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            foreach (var prefix in sAllowedPrefixes)
            {
                if (string.Equals(path, prefix, StringComparison.Ordinal))
                {
                    return true;
                }

                if (path.StartsWith(prefix + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 显式类型名是否在白名单（兼容旧调用方）。
        /// </summary>
        public static bool IsWhitelisted(string typeName)
        {
            return IsAllowedPath(typeName);
        }

        public static IReadOnlyList<string> Types => TypeNames;

        public static IReadOnlyList<string> Namespaces => EditorNamespaces;

        /// <summary>
        /// 从显式类型名推导的命名空间前缀（供 L2 list_editor_namespaces 合并）。
        /// </summary>
        public static IEnumerable<string> NamespacePrefixesFromTypes()
        {
            return TypeNames
                .Select(t =>
                {
                    int dot = t.LastIndexOf('.');
                    return dot > 0 ? t.Substring(0, dot) : t;
                })
                .Distinct(StringComparer.Ordinal);
        }

        private static string[] BuildAllowedPrefixes()
        {
            return EditorNamespaces
                .Concat(TypeNames.Select(t =>
                {
                    int dot = t.LastIndexOf('.');
                    return dot > 0 ? t.Substring(0, dot) : t;
                }))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }
}

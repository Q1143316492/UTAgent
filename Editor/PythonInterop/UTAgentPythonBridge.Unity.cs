using System;
using System.Linq;
using System.Text;
using UnityEngine;

namespace UTAgent.Editor.PythonInterop
{
    /// <summary>
    /// `unity` Python 模块的 C# Bridge：耗时/需宿主（场景、Unity API、序列化）放在这里，
    /// Python 侧只做薄封装。返回值统一为 JSON 字符串。
    /// </summary>
    public sealed partial class UTAgentPythonBridge
    {
        private const int MaxLogCount = 50;
        private const int MinScreenshotSize = 64;
        private const int MaxScreenshotWidth = 1920;
        private const int MaxScreenshotHeight = 1080;

        public void Log(string message)
        {
            Debug.Log($"[UTAgent] {message}");
        }

        /// <summary>
        /// 写警告到 Unity Console。
        /// </summary>
        public void LogWarning(string message)
        {
            Debug.LogWarning($"[UTAgent] {message}");
        }

        /// <summary>
        /// 写错误到 Unity Console。
        /// </summary>
        public void LogError(string message)
        {
            Debug.LogError($"[UTAgent] {message}");
        }

        private static string BuildHierarchyNode(Transform transform, int maxDepth, int currentDepth)
        {
            var go = transform.gameObject;
            var components = go.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c =>
                {
                    var t = c.GetType();
                    return $"{{\"shortName\":{BridgeJson.EscapeJson(t.Name)},\"fullName\":{BridgeJson.EscapeJson(t.FullName)}}}";
                })
                .ToArray();
            bool hasChildren = transform.childCount > 0;
            bool atLimit = maxDepth > 0 && currentDepth >= maxDepth;
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"name\":{BridgeJson.EscapeJson(go.name)},");
            sb.Append($"\"active\":{BridgeJson.ToLower(go.activeSelf)},");
            sb.Append($"\"components\":[{string.Join(",", components)}],");
            sb.Append($"\"childCount\":{transform.childCount}");
            if (hasChildren && !atLimit)
            {
                var children = Enumerable.Range(0, transform.childCount)
                    .Select(i => BuildHierarchyNode(transform.GetChild(i), maxDepth, currentDepth + 1));
                sb.Append($",\"children\":[{string.Join(",", children)}]");
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }

        private static string GetTypeKind(Type type)
        {
            if (type.IsEnum)
            {
                return "enum";
            }
            if (type.IsInterface)
            {
                return "interface";
            }
            if (type.IsValueType)
            {
                return "struct";
            }
            return "class";
        }

        private static string Error(string message)
        {
            return $"{{\"success\":false,\"message\":{BridgeJson.EscapeJson(message)}}}";
        }
    }
}

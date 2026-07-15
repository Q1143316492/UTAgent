using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UTAgent.Editor.PythonInterop
{
    /// <summary>
    /// `unity` Python ģ��� C# Bridge�������ػ���䡢Unity API�����л����������
    /// Python ��ֻ��������㡣����ֵͳһΪ JSON �ַ�����
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
        /// ������浽 Unity Console��
        /// </summary>
        public void LogWarning(string message)
        {
            Debug.LogWarning($"[UTAgent] {message}");
        }

        /// <summary>
        /// ������� Unity Console��
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

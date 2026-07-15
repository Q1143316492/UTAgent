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
                    return $"{{\"shortName\":{EscapeJson(t.Name)},\"fullName\":{EscapeJson(t.FullName)}}}";
                })
                .ToArray();
            bool hasChildren = transform.childCount > 0;
            bool atLimit = maxDepth > 0 && currentDepth >= maxDepth;
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"name\":{EscapeJson(go.name)},");
            sb.Append($"\"active\":{ToLower(go.activeSelf)},");
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
            return $"{{\"success\":false,\"message\":{EscapeJson(message)}}}";
        }

        private static string EscapeJson(string s)
        {
            if (s == null)
            {
                return "null";
            }
            var sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                    break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append($"\\u{(int)c:X4}");
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string ToLower(bool value)
        {
            return value ? "true" : "false";
        }
    }
}

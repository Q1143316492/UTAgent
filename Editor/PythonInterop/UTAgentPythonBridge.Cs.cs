using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UTAgent.Editor.Config;

namespace UTAgent.Editor.PythonInterop
{
    public sealed partial class UTAgentPythonBridge
    {
        private static readonly Dictionary<string, string> sCsResolveCache = new(StringComparer.Ordinal);

        /// <summary>
        /// Editor exec 内 CS 路径是否允许。对标 Puerts eval，不经过 UnityBindWhitelist Filter。
        /// </summary>
        public bool CsIsAllowed(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (path.StartsWith("_cs_", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 按全限定名解析已加载程序集中的 CLR 类型，返回 JSON。
        /// </summary>
        public string CsResolveType(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return Error("fullName 不能为空");
            }

            fullName = fullName.Trim();
            if (sCsResolveCache.TryGetValue(fullName, out var cached))
            {
                return cached;
            }

            var type = FindType(fullName);
            if (type == null)
            {
                var result = Error($"未找到类型：{fullName}");
                sCsResolveCache[fullName] = result;
                return result;
            }

            var assemblyName = type.Assembly.GetName().Name ?? string.Empty;
            var json = new StringBuilder();
            json.Append("{\"success\":true");
            json.Append($",\"fullName\":{BridgeJson.EscapeJson(type.FullName)}");
            json.Append($",\"assemblyName\":{BridgeJson.EscapeJson(assemblyName)}");
            json.Append($",\"kind\":{BridgeJson.EscapeJson(GetTypeKind(type))}");
            json.Append('}');
            var success = json.ToString();
            sCsResolveCache[fullName] = success;
            return success;
        }

        /// <summary>
        /// 返回 ensure_clr 应预载的程序集简单名列表（JSON 数组字符串）。
        /// </summary>
        public string CsGetPreloadAssemblies()
        {
            var names = new HashSet<string>(StringComparer.Ordinal)
            {
                "UnityEngine.CoreModule",
                "UnityEngine.UIModule",
                "UnityEngine.UI",
                "UnityEditor.CoreModule",
                "Unity.TextMeshPro",
            };

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var simpleName = asm.GetName().Name;
                if (string.IsNullOrEmpty(simpleName))
                {
                    continue;
                }

                if (simpleName.StartsWith("Assembly-CSharp", StringComparison.Ordinal))
                {
                    names.Add(simpleName);
                    continue;
                }

                try
                {
                    foreach (var type in asm.GetExportedTypes())
                    {
                        if (type.Namespace != null
                            && type.Namespace.StartsWith("GameCore", StringComparison.Ordinal))
                        {
                            names.Add(simpleName);
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    // 部分程序集无法枚举导出类型，跳过
                }
            }

            var sorted = names.OrderBy(n => n).Select(BridgeJson.EscapeJson);
            return $"[{string.Join(",", sorted)}]";
        }

        /// <summary>
        /// 域重载后清理解析缓存（由 Bootstrap 在 init 时调用）。
        /// </summary>
        public void CsClearResolveCache()
        {
            sCsResolveCache.Clear();
        }
    }
}

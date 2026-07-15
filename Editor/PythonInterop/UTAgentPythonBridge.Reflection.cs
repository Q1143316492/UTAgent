using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UTAgent.Editor.Config;
using Debug = UnityEngine.Debug;

namespace UTAgent.Editor.PythonInterop
{
    public sealed partial class UTAgentPythonBridge
    {
        public string ListNamespaces(string filter = "")
        {
            var namespaces = new HashSet<string>();
            var filters = ParseNamespaceFilter(filter);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (string.IsNullOrEmpty(type.Namespace))
                        {
                            continue;
                        }

                        if (filters.Count > 0 && !MatchesNamespaceFilter(type.Namespace, filters))
                        {
                            continue;
                        }

                        namespaces.Add(type.Namespace);
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // ???????????????????
                }
            }
            var sorted = namespaces.OrderBy(n => n).ToArray();
            return $"{{\"namespaces\":[{string.Join(",", sorted.Select(BridgeJson.EscapeJson))}]}}";
        }

        /// <summary>
        /// ???? Editor Agent ??????????????? Plastic SCM ??????????
        /// </summary>
        public string ListEditorNamespaces()
        {
            var allowed = new HashSet<string>(UnityBindWhitelist.EditorNamespaces, StringComparer.Ordinal);
            foreach (var prefix in UnityBindWhitelist.NamespacePrefixesFromTypes())
            {
                allowed.Add(prefix);
            }

            var sorted = allowed.OrderBy(n => n).ToArray();
            return $"{{\"namespaces\":[{string.Join(",", sorted.Select(BridgeJson.EscapeJson))}]}}";
        }

        private static List<string> ParseNamespaceFilter(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return new List<string>();
            }

            return filter.Split(',')
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();
        }

        private static bool MatchesNamespaceFilter(string ns, List<string> filters)
        {
            foreach (var filter in filters)
            {
                if (ns.Equals(filter, StringComparison.Ordinal)
                    || ns.StartsWith(filter + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// ???????????????????????????????????????????
        /// </summary>
        public string ListTypesInNamespace(string namespaces)
        {
            if (string.IsNullOrWhiteSpace(namespaces))
            {
                return Error("namespaces ???????");
            }
            var nsSet = new HashSet<string>(namespaces.Split(',').Select(n => n.Trim()));
            var types = new List<string>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (!string.IsNullOrEmpty(type.Namespace) && nsSet.Contains(type.Namespace) && type.IsPublic)
                        {
                            string kind = GetTypeKind(type);
                            types.Add($"{{\"name\":{BridgeJson.EscapeJson(type.Name)},\"fullName\":{BridgeJson.EscapeJson(type.FullName)},\"kind\":{BridgeJson.EscapeJson(kind)}}}");
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                }
            }
            return $"{{\"types\":[{string.Join(",", types)}]}}";
        }

        /// <summary>
        /// ?????????????????????????
        /// </summary>
        public string GetTypeDetails(string typeNames)
        {
            if (string.IsNullOrWhiteSpace(typeNames))
            {
                return Error("type_names ???????");
            }
            var names = typeNames.Split(',').Select(n => n.Trim()).ToArray();
            var typeInfos = new List<string>();
            foreach (var name in names)
            {
                var type = FindType(name);
                if (type == null)
                {
                    typeInfos.Add($"{{\"name\":{BridgeJson.EscapeJson(name)},\"error\":\"type not found\"}}");
                    continue;
                }
                var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(p => BridgeJson.EscapeJson(p.Name));
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName)
                    .Select(m => BridgeJson.EscapeJson(m.Name));
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(f => BridgeJson.EscapeJson(f.Name));
                var interfaces = type.GetInterfaces().Select(i => BridgeJson.EscapeJson(i.FullName ?? i.Name));
                var enumValues = type.IsEnum
                    ? Enum.GetNames(type).Select(BridgeJson.EscapeJson)
                    : Array.Empty<string>();
                typeInfos.Add(
                    $"{{\"name\":{BridgeJson.EscapeJson(type.Name)}," +
                    $"\"fullName\":{BridgeJson.EscapeJson(type.FullName)}," +
                    $"\"baseType\":{(type.BaseType != null ? BridgeJson.EscapeJson(type.BaseType.FullName) : "null")}," +
                    $"\"properties\":[{string.Join(",", props)}]," +
                    $"\"methods\":[{string.Join(",", methods)}]," +
                    $"\"fields\":[{string.Join(",", fields)}]," +
                    $"\"interfaces\":[{string.Join(",", interfaces)}]," +
                    $"\"enumValues\":[{string.Join(",", enumValues)}]}}");
            }
            return $"{{\"types\":[{string.Join(",", typeInfos)}]}}";
        }

        /// <summary>
        /// ?????? N ?? Unity Console ?????
        /// </summary>
        public string GetRecentLogs(int count, string logType)
        {
            count = Mathf.Clamp(count, 1, MaxLogCount);
            var entries = new List<string>();
            var logs = UnityModuleLogCollector.GetRecentLogs(count);
            foreach (var log in logs)
            {
                if (logType == "all" || log.Type.ToString().ToLowerInvariant() == logType)
                {
                    entries.Add($"{{\"timestamp\":{log.Timestamp},\"type\":{BridgeJson.EscapeJson(log.Type.ToString())},\"message\":{BridgeJson.EscapeJson(log.Message)},\"stackTrace\":{BridgeJson.EscapeJson(log.StackTrace ?? "")}}}");
                }
            }
            return $"[{string.Join(",", entries)}]";
        }

        /// <summary>
        /// ??????????????
        /// </summary>
        public string GetLogSummary()
        {
            var summary = UnityModuleLogCollector.GetSummary();
            return $"{{\"log\":{summary.Log},\"warning\":{summary.Warning},\"error\":{summary.Error},\"total\":{summary.Total}}}";
        }

        /// <summary>
        /// ??????????????? PNG base64??
        /// Play Mode ??????? Game ????????????? Scene ??????????????????
        /// </summary>
    }
}

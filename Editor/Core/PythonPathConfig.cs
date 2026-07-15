using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UTAgent.Editor.Core
{
    /// <summary>
    /// UTAgent Python 模块目录单点配置（sys.path / PYTHONPATH 注入共用）。
    /// </summary>
    public static class PythonPathConfig
    {
        public static string PackageRoot =>
            Path.Combine(Application.dataPath, "UTAgent").Replace('\\', '/');

        /// <summary>
        /// 本机 CPython 嵌入目录（将 python.exe + python3xx.dll + Lib 拷入此处，已 gitignore）。
        /// </summary>
        public static string BundledPythonHome =>
            Path.Combine(PackageRoot, "PythonHome").Replace('\\', '/');

        /// <summary>
        /// Agent 会话日志默认目录（已 gitignore）。
        /// </summary>
        public static string DefaultLogDirectory =>
            Path.Combine(PackageRoot, "LOG").Replace('\\', '/');

        public static string PythonDir =>
            Path.Combine(PackageRoot, "Python").Replace('\\', '/');

        public static string AgentDir =>
            Path.Combine(PythonDir, "agent").Replace('\\', '/');

        public static string LegacyRuntimeDir =>
            Path.Combine(PackageRoot, "Runtime").Replace('\\', '/');

        /// <summary>
        /// 与默认路径相同时存空字符串，表示「使用包内默认」。
        /// </summary>
        public static string NormalizeOptionalPath(string value, string defaultPath)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            try
            {
                string full = Path.GetFullPath(value.Trim());
                string fullDefault = Path.GetFullPath(defaultPath);
                if (string.Equals(full, fullDefault, StringComparison.OrdinalIgnoreCase))
                {
                    return "";
                }

                return full;
            }
            catch
            {
                return value.Trim();
            }
        }

        /// <summary>
        /// 注入顺序：agent 优先于 python 根（避免 import agent 命中空 namespace package）。
        /// </summary>
        public static IReadOnlyList<string> BuildSysPathEntries()
        {
            return new[] { AgentDir, PythonDir, LegacyRuntimeDir };
        }

        public static string[] BuildProcessPathEntries()
        {
            return new[] { AgentDir, PythonDir };
        }
    }
}

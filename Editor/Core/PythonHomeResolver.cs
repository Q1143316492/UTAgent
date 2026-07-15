using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UTAgent.Editor.Core
{
    /// <summary>
    /// 解析本机 CPython 安装目录（EditorPrefs → 包内 PythonHome → 环境变量 → 常见路径探测）。
    /// </summary>
    public static class PythonHomeResolver
    {
        public static string GetDefaultPythonHome()
        {
            return Path.GetFullPath(PythonPathConfig.BundledPythonHome);
        }

        /// <summary>
        /// 用于 UI 展示：已解析到的目录，或包内默认路径。
        /// </summary>
        public static string GetDisplayPythonHome()
        {
            return ResolvePythonHome() ?? GetDefaultPythonHome();
        }

        public static string ResolvePythonHome()
        {
            string fromPrefs = UTAgentPrefs.GetPythonHome();
            if (IsValidHome(fromPrefs))
            {
                return Path.GetFullPath(fromPrefs.Trim());
            }

            if (IsValidHome(PythonPathConfig.BundledPythonHome))
            {
                return Path.GetFullPath(PythonPathConfig.BundledPythonHome);
            }

            string fromEnv = Environment.GetEnvironmentVariable("PYTHONHOME");
            if (IsValidHome(fromEnv))
            {
                return Path.GetFullPath(fromEnv.Trim());
            }

            string probed = ProbeCommonInstall();
            if (!string.IsNullOrEmpty(probed))
            {
                return Path.GetFullPath(probed);
            }

            return null;
        }

        public static string ResolvePythonDllFileName()
        {
            return UTAgentPrefs.GetPythonDll();
        }

        private static bool IsValidHome(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path.Trim());
        }

        private static string ProbeCommonInstall()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pythonRoot = Path.Combine(localAppData, "Programs", "Python");
            if (!Directory.Exists(pythonRoot))
            {
                return null;
            }

            string[] dirs = Directory.GetDirectories(pythonRoot, "Python3*")
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (string dir in dirs)
            {
                string dll = Path.Combine(dir, UTAgentPrefs.DefaultPythonDll);
                if (File.Exists(dll) || File.Exists(Path.Combine(dir, "python.exe")))
                {
                    return dir;
                }
            }

            return null;
        }
    }
}

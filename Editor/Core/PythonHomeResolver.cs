using System;
using System.IO;
using System.Linq;
using UTAgent.Editor.Config;

namespace UTAgent.Editor.Core
{
    /// <summary>
    /// 解析本机 CPython 安装目录（json → PYTHONHOME → 包内 PythonHome → 常见路径探测）。
    /// </summary>
    public static class PythonHomeResolver
    {
        public static string GetDefaultPythonHome()
        {
            return Path.GetFullPath(PythonPathConfig.BundledPythonHome);
        }

        /// <summary>
        /// 用于 UI 展示：已保存或已解析到的目录。
        /// </summary>
        public static string GetDisplayPythonHome()
        {
            string saved = UTAgentConfig.ResolvePythonHomeFromConfig();
            if (!string.IsNullOrWhiteSpace(saved))
            {
                return Path.GetFullPath(saved.Trim());
            }

            return ResolvePythonHome() ?? GetDefaultPythonHome();
        }

        /// <summary>
        /// 用户是否已在 json 中保存过 Python 目录（选过一次后不再提示选择）。
        /// </summary>
        public static bool HasSavedPythonHome()
        {
            return !string.IsNullOrWhiteSpace(UTAgentConfig.ResolvePythonHomeFromConfig());
        }

        public static string ResolvePythonHome()
        {
            string fromConfig = UTAgentConfig.ResolvePythonHomeFromConfig();
            if (IsValidHome(fromConfig))
            {
                return Path.GetFullPath(fromConfig.Trim());
            }

            string fromEnv = Environment.GetEnvironmentVariable("PYTHONHOME");
            if (IsValidHome(fromEnv))
            {
                return Path.GetFullPath(fromEnv.Trim());
            }

            if (IsValidHome(PythonPathConfig.BundledPythonHome))
            {
                return Path.GetFullPath(PythonPathConfig.BundledPythonHome);
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
            return UTAgentConfig.ResolvePythonDll();
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

            string dllName = UTAgentConfig.ResolvePythonDll();
            string[] dirs = Directory.GetDirectories(pythonRoot, "Python3*")
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (string dir in dirs)
            {
                string dll = Path.Combine(dir, dllName);
                if (File.Exists(dll) || File.Exists(Path.Combine(dir, "python.exe")))
                {
                    return dir;
                }
            }

            return null;
        }
    }
}

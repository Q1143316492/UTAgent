using System.IO;
using UTAgent.Editor.Config;

namespace UTAgent.Editor.Core
{
    /// <summary>
    /// 解析包内嵌入式 CPython：仅 <c>Assets/UTAgent/PythonHome</c>（含约定 dll）。
    /// </summary>
    public static class PythonHomeResolver
    {
        public static string GetDefaultPythonHome()
        {
            return Path.GetFullPath(PythonPathConfig.BundledPythonHome);
        }

        /// <summary>
        /// UI 展示用路径（始终为包内 PythonHome，无论是否已安装）。
        /// </summary>
        public static string GetDisplayPythonHome()
        {
            return GetDefaultPythonHome();
        }

        /// <summary>
        /// PythonHome 是否已就绪（目录存在且含 dll）。
        /// </summary>
        public static bool IsPythonHomeReady()
        {
            return ResolvePythonHome() != null;
        }

        /// <summary>
        /// 仅当包内 PythonHome 有效时返回其绝对路径；否则 null。
        /// </summary>
        public static string ResolvePythonHome()
        {
            string home = PythonPathConfig.BundledPythonHome;
            if (!IsValidHome(home))
            {
                return null;
            }

            string dllName = UTAgentConfig.ResolvePythonDll();
            string dllPath = Path.Combine(home, dllName);
            if (!File.Exists(dllPath))
            {
                return null;
            }

            return Path.GetFullPath(home);
        }

        public static string ResolvePythonDllFileName()
        {
            return UTAgentConfig.ResolvePythonDll();
        }

        /// <summary>
        /// Install-PythonHome.ps1 的绝对路径。
        /// </summary>
        public static string GetInstallPythonHomeScriptPath()
        {
            return Path.GetFullPath(
                Path.Combine(PythonPathConfig.PackageRoot, "Tools", "bootstrap", "Install-PythonHome.ps1"));
        }

        private static bool IsValidHome(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path.Trim());
        }
    }
}

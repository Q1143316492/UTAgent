using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UTAgent.Editor.PythonInterop;

namespace UTAgent.Editor.Core
{
    /// <summary>
    /// Python 引擎入口。内部转发给 <see cref="IPythonEngine"/> 单例。
    /// 域重载会破坏 pythonnet 单例状态，本类用 mInvalidated 标记失效，下次使用时提示重新初始化。
    /// </summary>
    [UnityEditor.InitializeOnLoad]
    public static class UTAgentBootstrap
    {
        private static IPythonEngine mEngine;
        private static bool mInvalidated;

        /// <summary>
        /// 当前是否处于可用状态（已初始化且未因域重载失效）。
        /// </summary>
        public static bool IsAvailable => mEngine != null && mEngine.IsAvailable && !mInvalidated;

        /// <summary>
        /// 是否因域重载导致引擎失效（须重新 Initialize）。
        /// </summary>
        public static bool IsInvalidated => mInvalidated;

        static UTAgentBootstrap()
        {
            mInvalidated = mEngine != null && mEngine.IsAvailable;
        }

        private static IPythonEngine GetEngine()
        {
            if (mEngine == null)
            {
                mEngine = new CPythonEngine();
            }

            return mEngine;
        }

        /// <summary>
        /// 初始化 CPython 引擎。重复调用安全；每次 Play 末尾会 TryReloadApp 清 Python 实例表。
        /// </summary>
        public static void Initialize()
        {
            var engine = GetEngine();
            AddPythonPath();
            engine.Initialize();
            mInvalidated = false;

            EnsureUnityModulePath(engine);
            RefreshPythonModuleCache(force: true);
        }

        /// <summary>
        /// 按磁盘 mtime 刷新 UTAgent Python 模块（见 <c>App.sync_runtime_modules</c>），并重新注册 C# 桥。
        /// force=true：无条件重载（初始化 / 手动刷新）；force=false：仅 .py 有改动时重载。
        /// </summary>
        public static bool RefreshPythonModuleCache(bool force = false)
        {
            if (!IsAvailable)
            {
                return false;
            }

            var engine = GetEngine();
            bool purged = false;
            try
            {
                // 勿在 import App 前 purge：会导致 unity.* 半初始化循环 import。
                string forceLit = force ? "True" : "False";
                var (output, _) = engine.Exec(
                    "from unity.core.app import App\n" +
                    $"_purged = App.sync_runtime_modules(force={forceLit})\n" +
                    "print('purged=' + str(_purged))\n");
                purged = output != null && output.Contains("purged=True");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgent] 刷新 Python 模块缓存失败：{e.Message}");
            }

            RegisterBridgeModules(engine);
            if (purged)
            {
                Debug.Log(force
                    ? "[UTAgent] 已强制刷新 Python 模块缓存"
                    : "[UTAgent] 检测到 .py 变更，已刷新 Python 模块缓存");
            }
            return purged;
        }

        private static void RegisterBridgeModules(IPythonEngine engine)
        {
            var bridge = UTAgentPythonBridge.Instance;
            bridge.CsClearResolveCache();
            engine.RegisterModule("_unity_bridge", bridge);
            engine.RegisterModule("_ui_bridge", bridge);
            engine.RegisterModule("_wndmgr_bridge", bridge);
            engine.RegisterModule("_cs_bridge", bridge);
        }

        private static void EnsureUnityModulePath(IPythonEngine engine)
        {
            var entries = PythonPathConfig.BuildSysPathEntries();
            try
            {
                var agentDir = entries[0];
                var pythonDir = entries[1];
                var legacyRuntimeDir = entries[2];
                engine.Exec(
                    "import sys\n" +
                    $"_agent = r'{agentDir}'\n" +
                    $"_python = r'{pythonDir}'\n" +
                    $"_legacy = r'{legacyRuntimeDir}'\n" +
                    "for _p in (_agent, _python, _legacy):\n" +
                    "    while _p in sys.path:\n" +
                    "        sys.path.remove(_p)\n" +
                    "sys.path.insert(0, _python)\n" +
                    "sys.path.insert(0, _agent)\n");
                Debug.Log($"[UTAgent] 已注入模块路径（agent 优先）：{agentDir}; {pythonDir}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgent] 注入模块路径失败：{e.Message}");
            }
        }

        private static void AddPythonPath()
        {
            var pythonDir = PythonPathConfig.PythonDir;
            if (!Directory.Exists(pythonDir))
            {
                Debug.LogWarning($"[UTAgent] Python 目录不存在：{pythonDir}");
                return;
            }

            const string Key = "PYTHONPATH";
            var existing = Environment.GetEnvironmentVariable(Key) ?? string.Empty;
            var parts = existing.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            parts.RemoveAll(p => string.Equals(p, PythonPathConfig.LegacyRuntimeDir, StringComparison.OrdinalIgnoreCase));
            foreach (var req in PythonPathConfig.BuildProcessPathEntries())
            {
                if (!parts.Contains(req, StringComparer.OrdinalIgnoreCase))
                {
                    parts.Insert(0, req);
                }
            }

            Environment.SetEnvironmentVariable(Key, string.Join(";", parts));
        }

        /// <summary>
        /// 关闭引擎（重操作）。退出 Play 勿调用；域重载后靠 <see cref="Initialize"/> 热重连。
        /// </summary>
        public static void Shutdown(bool reloadApp = true)
        {
            if (reloadApp)
            {
                TryReloadApp();
            }

            if (mEngine != null)
            {
                mEngine.Shutdown();
            }
        }

        private static void TryReloadApp()
        {
            if (mEngine == null || !mEngine.IsAvailable)
            {
                return;
            }

            try
            {
                UTAgentPythonBridge.Reload();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgent] App.reload 失败：{e.Message}");
            }

            // App.reload 会 pop 桥模块，须重新注入当前 C# 实例。
            RegisterBridgeModules(mEngine);
        }

        public static (string Output, string Error) Exec(string code)
        {
            if (!IsAvailable)
            {
                if (mInvalidated)
                {
                    Debug.LogWarning("[UTAgent] 引擎因域重载失效，请重新点击初始化");
                }

                throw new InvalidOperationException("[UTAgent] 引擎不可用，请先初始化");
            }

            return GetEngine().Exec(code);
        }

        public static void RegisterModule<T>(string name, T instance) where T : class
        {
            GetEngine().RegisterModule(name, instance);
        }
    }
}

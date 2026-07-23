using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Python.Runtime;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UTAgent.Editor.Core
{
    /// <summary>
    /// PC Editor 期的 <see cref="IPythonEngine"/> 实现，基于 pythonnet 嵌入 CPython。
    /// 所有 pythonnet 专用类型（PyObject / PyModule / PythonEngine）只出现在本类内部。
    /// </summary>
    public sealed class CPythonEngine : IPythonEngine
    {
        private static bool mInitialized;
        private static bool mInvalidated;
        private static readonly object mLock = new();

        /// <summary>
        /// 静态构造。域重载发生时整个静态状态被重置；若此前初始化过，标记失效。
        /// </summary>
        static CPythonEngine()
        {
            mInvalidated = mInitialized;
        }

        /// <summary>
        /// 当前引擎是否可用（已初始化且未因域重载失效）。
        /// </summary>
        public bool IsAvailable => mInitialized && !mInvalidated;

        /// <summary>
        /// 初始化 CPython 引擎。重复调用安全。
        /// Soft-reattach：域重载后原生解释器可仍存活；附着并重注册桥，失败只清旗标并要求重启 Editor（不自动 Shutdown 重试）。
        /// </summary>
        public void Initialize()
        {
            lock (mLock)
            {
                InitializeLocked();
            }
        }

        private void InitializeLocked()
        {
            if (IsAvailable)
            {
                Debug.Log("[UTAgent] 引擎已初始化，跳过");
                return;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                string pythonHome = ResolvePythonHomeOrThrow();
                string targetDll = Path.Combine(pythonHome, PythonHomeResolver.ResolvePythonDllFileName());

                if (PythonEngine.IsInitialized)
                {
                    string currentDll = Runtime.PythonDLL ?? string.Empty;
                    if (DllPathsEqual(currentDll, targetDll) || string.IsNullOrWhiteSpace(currentDll))
                    {
                        AttachToRunningRuntime(pythonHome);
                        TimedProbeOrThrow("附着后探活失败");
                    }
                    else
                    {
                        // 换 dll：显式配置变更，允许全量 Shutdown（非 Reload 兜底）
                        Debug.Log($"[UTAgent] dll 变更，Shutdown 后冷启动：{currentDll} → {targetDll}");
                        ShutdownLocked();
                        SoftReattachColdStart(pythonHome, targetDll);
                    }
                }
                else
                {
                    SoftReattachColdStart(pythonHome, targetDll);
                }

                sw.Stop();
                UTAgentInitTiming.Log("engine_total", sw.ElapsedMilliseconds);
                Debug.Log($"[UTAgent] 初始化完成，耗时 {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception e)
            {
                ClearAfterFailedInit();
                sw.Stop();
                UTAgentInitTiming.Log("engine_total", sw.ElapsedMilliseconds, "failed=True");
                Debug.LogError($"[UTAgent] 初始化失败（{sw.ElapsedMilliseconds} ms）：{e}");
                throw;
            }
        }

        /// <summary>
        /// 关闭引擎（重操作，会卡主线程数秒）。仅手动调用（如 Settings 重置）；退出 Play / 域重载不自动 Shutdown。
        /// </summary>
        public void Shutdown()
        {
            lock (mLock)
            {
                ShutdownLocked();
            }
        }

        private void ShutdownLocked()
        {
            if (!mInitialized && !PythonEngine.IsInitialized)
            {
                return;
            }

            try
            {
                if (PythonEngine.IsInitialized)
                {
                    PythonEngine.Shutdown();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgent] 关闭时异常：{e.Message}");
            }
            finally
            {
                mInitialized = false;
                mInvalidated = false;
            }
        }

        /// <summary>
        /// 托管未标记已 init 时的 ColdStart：原生仍存活则由 pythonnet 内部附着（Soft-reattach）。
        /// </summary>
        private void SoftReattachColdStart(string pythonHome, string targetDll)
        {
            ApplyEnvironmentVariables(pythonHome);
            EnsurePythonDll(targetDll);
            bool softReattach = TryIsNativeInterpreterInitialized();
            if (softReattach)
            {
                Debug.Log("[UTAgent] 原生解释器仍存活，ColdStart 将附着（Soft-reattach）");
            }

            int assemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
            UTAgentInitTiming.LogInfo(
                "assembly_count",
                $"count={assemblyCount} soft_reattach={softReattach}");

            if (!PythonEngine.IsInitialized)
            {
                var swInit = Stopwatch.StartNew();
                PythonEngine.Initialize();
                swInit.Stop();
                UTAgentInitTiming.Log(
                    "python_engine_initialize",
                    swInit.ElapsedMilliseconds,
                    $"assemblies={assemblyCount} soft_reattach={softReattach}");
            }
            else
            {
                UTAgentInitTiming.Log(
                    "python_engine_initialize",
                    0,
                    $"skipped=True assemblies={assemblyCount} soft_reattach={softReattach}");
            }

            TimedRegisterBridgeModule();
            mInitialized = true;
            mInvalidated = false;
            TimedProbeOrThrow("Soft-reattach 后探活失败");
        }

        private void AttachToRunningRuntime(string pythonHome)
        {
            ApplyEnvironmentVariables(pythonHome);
            Debug.Log($"[UTAgent] 附着已运行 Runtime（跳过 PythonDLL 赋值），PYTHONHOME={pythonHome}");
            Debug.Log($"[UTAgent] PythonDLL={Runtime.PythonDLL}");
            int assemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
            UTAgentInitTiming.LogInfo(
                "assembly_count",
                $"count={assemblyCount} soft_reattach=True path=attach");
            TimedRegisterBridgeModule();
            mInitialized = true;
            mInvalidated = false;
        }

        private static void TimedRegisterBridgeModule()
        {
            var sw = Stopwatch.StartNew();
            RegisterBridgeModule();
            sw.Stop();
            UTAgentInitTiming.Log("register_pybridge", sw.ElapsedMilliseconds);
        }

        private void TimedProbeOrThrow(string failurePrefix)
        {
            var sw = Stopwatch.StartNew();
            bool ok = TryProbeLocked();
            sw.Stop();
            UTAgentInitTiming.Log("probe", sw.ElapsedMilliseconds, $"ok={ok}");
            if (ok)
            {
                return;
            }

            throw new InvalidOperationException(
                $"[UTAgent] {failurePrefix}。请重启 Unity Editor 后再初始化。");
        }

        /// <summary>
        /// 探活；失败时清 UTAgent 可用标志并返回 false（调用方抛重启提示，不 Shutdown 重试）。
        /// </summary>
        private bool TryProbeLocked()
        {
            try
            {
                using (Py.GIL())
                {
                    using var scope = Py.CreateScope();
                    scope.Exec("1+1", scope.Variables());
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgent] 探活失败：{e.Message}");
                mInitialized = false;
                mInvalidated = false;
                return false;
            }
        }

        private void ClearAfterFailedInit()
        {
            mInitialized = false;
            mInvalidated = false;
            TrySetPythonEngineInitialized(false);
            TrySetRuntimeInitialized(false);
        }

        private static bool TryIsNativeInterpreterInitialized()
        {
            try
            {
                MethodInfo method = typeof(Runtime).GetMethod(
                    "Py_IsInitialized",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (method == null)
                {
                    return false;
                }

                object result = method.Invoke(null, null);
                return result is int flag && flag != 0;
            }
            catch
            {
                return false;
            }
        }

        private static void TrySetRuntimeInitialized(bool value)
        {
            FieldInfo field = typeof(Runtime).GetField(
                "_isInitialized",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(null, value);
            }
        }

        private static void TrySetPythonEngineInitialized(bool value)
        {
            FieldInfo field = typeof(PythonEngine).GetField(
                "initialized",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(null, value);
            }
        }

        /// <summary>
        /// 执行 Python 代码并取回输出与错误。通过 __pybridge__ 模块捕获 print，不依赖 sys.stdout。
        /// </summary>
        public (string Output, string Error) Exec(string code)
        {
            if (!IsAvailable)
            {
                throw new InvalidOperationException("[UTAgent] 引擎不可用，请先初始化");
            }

            var output = new StringBuilder();
            var error = new StringBuilder();
            using (Py.GIL())
            {
                using var scope = Py.CreateScope();
                dynamic pybridge = Py.Import("__pybridge__");
                pybridge._sink = new BridgeSink(output, error);
                PyDict vars = scope.Variables();

                const string harness =
                    "import builtins as _b\n" +
                    "import sys as _s\n" +
                    "import __pybridge__ as _pb\n" +
                    "_b._orig_print = _b.print\n" +
                    "def _b_print(*args, sep=' ', end='\\n', file=None, flush=False):\n" +
                    "    _pb.log(sep.join(str(a) for a in args) + end)\n" +
                    "_b.print = _b_print\n" +
                    "class _StdRedirect:\n" +
                    "    def __init__(self, is_err):\n" +
                    "        self._is_err = is_err\n" +
                    "    def write(self, s):\n" +
                    "        if s:\n" +
                    "            (_pb.err if self._is_err else _pb.log)(s)\n" +
                    "    def flush(self):\n" +
                    "        pass\n" +
                    "_s.stdout = _StdRedirect(False)\n" +
                    "_s.stderr = _StdRedirect(True)\n";
                const string restore =
                    "import builtins as _b\n" +
                    "import sys as _s\n" +
                    "if hasattr(_b, '_orig_print'):\n" +
                    "    _b.print = _b._orig_print\n" +
                    "    del _b._orig_print\n" +
                    "_s.stdout = _s.__stdout__\n" +
                    "_s.stderr = _s.__stderr__\n";
                try
                {
                    scope.Exec(harness, vars);
                    scope.Exec(code, vars);
                }
                finally
                {
                    try
                    {
                        scope.Exec(restore, vars);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[UTAgent] 恢复 stdout 时异常：{e.Message}");
                    }
                }
            }
            return (output.ToString(), error.ToString());
        }

        /// <summary>
        /// 向 Python 运行时注册自定义模块。Python 侧可通过 `import name` 访问 instance 的公共方法。
        /// </summary>
        public void RegisterModule<T>(string name, T instance) where T : class
        {
            if (!IsAvailable)
            {
                throw new InvalidOperationException("[UTAgent] 引擎不可用，无法注册模块");
            }
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("模块名不能为空", nameof(name));
            }
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                sys.modules[name] = instance.ToPython();
            }
        }

        private static string ResolvePythonHomeOrThrow()
        {
            string pythonHome = PythonHomeResolver.ResolvePythonHome();
            if (string.IsNullOrEmpty(pythonHome))
            {
                throw new InvalidOperationException(
                    $"[UTAgent] 未找到包内 PythonHome（需含 python312.dll）：{PythonHomeResolver.GetDefaultPythonHome()}。" +
                    "请运行 Tools/bootstrap/Install-PythonHome.ps1，或在 Settings → ① Python 点初始化（见 Docs/skills/utagent-env-bootstrap）。");
            }

            return pythonHome;
        }

        private static void ApplyEnvironmentVariables(string pythonHome)
        {
            Environment.SetEnvironmentVariable("PYTHONHOME", pythonHome);

            var libPath = Path.Combine(pythonHome, "Lib");
            var sitePath = Path.Combine(libPath, "site-packages");
            var existing = Environment.GetEnvironmentVariable("PYTHONPATH") ?? string.Empty;
            var required = new[] { libPath, sitePath };
            var parts = existing.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            foreach (var req in required)
            {
                if (!parts.Contains(req, StringComparer.OrdinalIgnoreCase))
                {
                    parts.Insert(0, req);
                }
            }
            Environment.SetEnvironmentVariable("PYTHONPATH", string.Join(";", parts));
            Debug.Log($"[UTAgent] PYTHONHOME={pythonHome}");
            Debug.Log($"[UTAgent] PYTHONPATH={Environment.GetEnvironmentVariable("PYTHONPATH")}");
        }

        /// <summary>
        /// 仅在 Runtime 未初始化时赋值 PythonDLL；已初始化或旗标失步时跳过写入。
        /// </summary>
        private static void EnsurePythonDll(string targetDll)
        {
            if (PythonEngine.IsInitialized || TryIsRuntimeInitialized())
            {
                Debug.Log($"[UTAgent] Runtime 已初始化/已锁定，跳过 PythonDLL 赋值（当前={Runtime.PythonDLL}）");
                return;
            }

            try
            {
                Runtime.PythonDLL = targetDll;
                Debug.Log($"[UTAgent] PythonDLL={Runtime.PythonDLL}");
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidOperationException(
                    "[UTAgent] 无法设置 Runtime.PythonDLL（Runtime 已锁定）。请重启 Unity Editor 后再初始化。",
                    e);
            }
        }

        private static bool TryIsRuntimeInitialized()
        {
            FieldInfo field = typeof(Runtime).GetField(
                "_isInitialized",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(bool))
            {
                return false;
            }

            return (bool)field.GetValue(null);
        }

        private static bool DllPathsEqual(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(a.Trim()),
                    Path.GetFullPath(b.Trim()),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void RegisterBridgeModule()
        {
            using (Py.GIL())
            {
                using var scope = Py.CreateScope();
                scope.Set("_sink", new BridgeSink(new StringBuilder(), new StringBuilder()));
                PyDict vars = scope.Variables();
                scope.Exec(
                    "def log(*args):\n" +
                    "    _sink.AppendOut(' '.join(str(a) for a in args))\n" +
                    "def err(*args):\n" +
                    "    _sink.AppendErr(' '.join(str(a) for a in args))\n",
                    vars);
                dynamic sys = Py.Import("sys");
                sys.modules["__pybridge__"] = scope;
            }
        }
    }
}

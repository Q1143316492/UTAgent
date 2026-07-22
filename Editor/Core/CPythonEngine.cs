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
        /// 若 pythonnet 已初始化且 dll 未变：附着（不写 Runtime.PythonDLL）并重注册桥。
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
                        if (!TryProbeLocked())
                        {
                            Debug.LogWarning("[UTAgent] 附着后探活失败，尝试 Shutdown 后冷启动");
                            ShutdownLocked();
                            ColdStart(pythonHome, targetDll);
                        }
                    }
                    else
                    {
                        Debug.Log($"[UTAgent] dll 变更，Shutdown 后冷启动：{currentDll} → {targetDll}");
                        ShutdownLocked();
                        ColdStart(pythonHome, targetDll);
                    }
                }
                else
                {
                    ColdStart(pythonHome, targetDll);
                }

                sw.Stop();
                Debug.Log($"[UTAgent] 初始化完成，耗时 {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception e)
            {
                mInitialized = false;
                mInvalidated = false;
                sw.Stop();
                Debug.LogError($"[UTAgent] 初始化失败（{sw.ElapsedMilliseconds} ms）：{e}");
                throw;
            }
        }

        /// <summary>
        /// 关闭引擎（重操作，会卡主线程数秒）。仅手动调用；退出 Play 不自动 Shutdown。
        /// </summary>
        public void Shutdown()
        {
            lock (mLock)
            {
                ShutdownLocked();
            }
        }

        /// <summary>
        /// 域重载前轻量拆除：跳过 pythonnet 全量 Shutdown 的多轮 GC/Stash。
        /// 托管域即将销毁，只求 Finalize 原生解释器并清旗标，避免随后 DomainUnload 再走重 Shutdown。
        /// </summary>
        public void ShutdownForDomainReload()
        {
            lock (mLock)
            {
                ShutdownForDomainReloadLocked();
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

        private void ShutdownForDomainReloadLocked()
        {
            if (!mInitialized && !PythonEngine.IsInitialized)
            {
                return;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                // 跳过 Stash / 多轮 GC；随后 DomainUnload→Shutdown 见 initialized==false 直接返回
                TrySetRuntimeProcessIsTerminating(true);

                if (PythonEngine.IsInitialized || TryIsRuntimeInitialized())
                {
                    TryPyFinalize();
                }

                TrySetPythonEngineInitialized(false);
                TrySetRuntimeInitialized(false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgent] 域重载轻量关闭异常：{e.Message}");
            }
            finally
            {
                mInitialized = false;
                mInvalidated = false;
                sw.Stop();
                Debug.Log($"[UTAgent] 域重载前轻量关闭完成，耗时 {sw.ElapsedMilliseconds} ms");
            }
        }

        private static void TrySetRuntimeProcessIsTerminating(bool value)
        {
            FieldInfo field = typeof(Runtime).GetField(
                "ProcessIsTerminating",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(null, value);
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

        private static void TryPyFinalize()
        {
            MethodInfo finalize = typeof(Runtime).GetMethod(
                "Py_Finalize",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (finalize == null)
            {
                Debug.LogWarning("[UTAgent] 未找到 Runtime.Py_Finalize，跳过原生 Finalize");
                return;
            }

            try
            {
                using (Py.GIL())
                {
                    finalize.Invoke(null, null);
                }
            }
            catch (Exception e)
            {
                try
                {
                    finalize.Invoke(null, null);
                }
                catch (Exception inner)
                {
                    Debug.LogWarning(
                        $"[UTAgent] Py_Finalize 失败：{e.Message}; retry={inner.Message}");
                }
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

        private void AttachToRunningRuntime(string pythonHome)
        {
            ApplyEnvironmentVariables(pythonHome);
            Debug.Log($"[UTAgent] 附着已运行 Runtime（跳过 PythonDLL 赋值），PYTHONHOME={pythonHome}");
            Debug.Log($"[UTAgent] PythonDLL={Runtime.PythonDLL}");
            RegisterBridgeModule();
            mInitialized = true;
            mInvalidated = false;
        }

        private void ColdStart(string pythonHome, string targetDll)
        {
            ApplyEnvironmentVariables(pythonHome);
            EnsurePythonDll(targetDll);
            if (!PythonEngine.IsInitialized)
            {
                PythonEngine.Initialize();
            }

            RegisterBridgeModule();
            mInitialized = true;
            mInvalidated = false;
        }

        /// <summary>
        /// 附着后探活；失败返回 false（调用方决定 Shutdown→冷启动）。
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
        /// 仅在 Runtime 未初始化时赋值 PythonDLL；已初始化时禁止写入（pythonnet 会抛错）。
        /// </summary>
        private static void EnsurePythonDll(string targetDll)
        {
            if (PythonEngine.IsInitialized)
            {
                Debug.Log($"[UTAgent] Runtime 已初始化，跳过 PythonDLL 赋值（当前={Runtime.PythonDLL}）");
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

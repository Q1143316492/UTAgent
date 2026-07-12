using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Python.Runtime;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UTAgent.Editor
{
    /// <summary>
    /// PC Editor 期的 <see cref="IPythonEngine"/> 实现，基于 pythonnet 嵌入 CPython。
    /// 所有 pythonnet 专用类型（PyObject / PyModule / PythonEngine）只出现在本类内部。
    /// </summary>
    public sealed class CPythonEngine : IPythonEngine
    {
        private const string PythonHome = @"C:\Users\chenweilin\AppData\Local\Programs\Python\Python312";
        private const string PythonDll = "python312.dll";

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
        /// </summary>
        public void Initialize()
        {
            lock (mLock)
            {
                if (IsAvailable)
                {
                    Debug.Log("[UTAgent] 引擎已初始化，跳过");
                    return;
                }

                var sw = Stopwatch.StartNew();
                try
                {
                    ConfigureEnvironment();
                    if (!PythonEngine.IsInitialized)
                    {
                        PythonEngine.Initialize();
                    }
                    RegisterBridgeModule();
                    mInitialized = true;
                    mInvalidated = false;
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
        }

        /// <summary>
        /// 关闭引擎（重操作，会卡主线程数秒）。仅手动调用；退出 Play 不自动 Shutdown。
        /// </summary>
        public void Shutdown()
        {
            lock (mLock)
            {
                if (!mInitialized)
                {
                    return;
                }
                try
                {
                    if (!mInvalidated && PythonEngine.IsInitialized)
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

        private static void ConfigureEnvironment()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PYTHONHOME")))
            {
                Environment.SetEnvironmentVariable("PYTHONHOME", PythonHome);
            }

            var libPath = Path.Combine(PythonHome, "Lib");
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

            if (string.IsNullOrEmpty(Runtime.PythonDLL))
            {
                Runtime.PythonDLL = Path.Combine(PythonHome, PythonDll);
            }
            Debug.Log($"[UTAgent] PYTHONHOME={Environment.GetEnvironmentVariable("PYTHONHOME")}");
            Debug.Log($"[UTAgent] PYTHONPATH={Environment.GetEnvironmentVariable("PYTHONPATH")}");
            Debug.Log($"[UTAgent] PythonDLL={Runtime.PythonDLL}");
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

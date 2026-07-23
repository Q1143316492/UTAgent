using System;
using System.Text;
using UTAgent.Editor.Agent;
using UTAgent.Editor.Core;

namespace UTAgent.Editor.Config
{
    /// <summary>
    /// 把「找 Python → 初始化引擎 → 配置 Agent」收成一次按需调用，Settings 只改配置、不教用户点顺序。
    /// </summary>
    public static class UTAgentReadiness
    {
        /// <summary>
        /// 上次成功 Initialize 时生效的 home/dll（用于检测配置变更是否需 Shutdown 重载）。
        /// </summary>
        private static string sAppliedHome = "";
        private static string sAppliedDll = "";

        /// <summary>
        /// Runtime.PythonDLL 已锁定、同进程无法恢复时的提示（脚本编译无效）。
        /// </summary>
        public const string RestartEditorHint =
            "请重启 Unity Editor 后再初始化（脚本编译无法解除 Runtime.PythonDLL 锁定）。";

        /// <summary>
        /// 其他初始化失败时的通用兜底（不再暗示「仅编译脚本即可」）。
        /// </summary>
        public const string DomainReloadHint =
            "若仍失败，请重启 Unity Editor 后再初始化。";

        public readonly struct Status
        {
            public bool Ready { get; }
            public string Summary { get; }
            public string Detail { get; }

            public Status(bool ready, string summary, string detail = "")
            {
                Ready = ready;
                Summary = summary;
                Detail = detail ?? "";
            }
        }

        /// <summary>
        /// 当前能否对话（不触发初始化）。
        /// </summary>
        public static Status GetChatStatus(UTAgentRunner runner)
        {
            if (!UTAgentConfig.TryCheckApiKey(null, out string apiMsg))
            {
                return new Status(false, "需要 API Key", apiMsg);
            }

            if (PythonHomeResolver.ResolvePythonHome() == null)
            {
                return new Status(
                    false,
                    "需要 Python",
                    "Settings → ① Python 点「初始化」，或运行 Tools/bootstrap/Install-PythonHome.ps1（见 Docs/skills/utagent-env-bootstrap）");
            }

            if (!UTAgentBootstrap.IsAvailable)
            {
                string hint = UTAgentBootstrap.IsInvalidated
                    ? "引擎因脚本编译失效，发送消息时将自动恢复"
                    : "引擎未启动，发送消息时将自动初始化";
                return new Status(false, "引擎未就绪", hint);
            }

            // Settings 与 Chat 各持有独立 Runner；未 configure 不代表环境坏了。
            // Python + API Key 已齐即视为就绪，首条 Chat 会自动 ConfigureFromConfig。
            if (runner != null && runner.IsConfigured())
            {
                return new Status(true, "可以对话", "");
            }

            return new Status(
                true,
                "Python 已就绪",
                "去 Chat 发一条消息即可（会自动配置 Agent）");
        }

        /// <summary>
        /// 保存设置后或 Chat 发送前：尽量把运行时拉到可对话状态。
        /// </summary>
        public static Status TryEnsureChatReady(UTAgentRunner runner)
        {
            if (!UTAgentConfig.TryCheckApiKey(null, out string apiMsg))
            {
                return new Status(false, "缺少 API Key", apiMsg);
            }

            string pythonHome = PythonHomeResolver.ResolvePythonHome();
            if (pythonHome == null)
            {
                return new Status(
                    false,
                    "缺少 Python",
                    "Settings → ① Python 初始化，或执行 Install-PythonHome.ps1");
            }

            if (!UTAgentBootstrap.IsAvailable)
            {
                try
                {
                    UTAgentBootstrap.Initialize();
                }
                catch (Exception e)
                {
                    return new Status(false, "Python 初始化失败", FormatInitFailureDetail(e));
                }
            }

            if (runner == null)
            {
                return new Status(true, "Python 已就绪", "");
            }

            if (runner.IsConfigured())
            {
                return new Status(true, "可以对话", "");
            }

            string configureResult = runner.ConfigureFromConfig();
            if (runner.IsConfigured())
            {
                return new Status(true, "可以对话", "");
            }

            return new Status(false, "Agent 配置失败", configureResult);
        }

        /// <summary>
        /// 仅同步 Python 引擎（保存 Python 路径后调用）。配置未变且已可用时跳过。
        /// </summary>
        public static Status TryEnsurePythonEngine()
        {
            return ApplyPythonConfigAndInit(forceReload: false);
        }

        /// <summary>
        /// 按包内 PythonHome + 默认 dll 应用并初始化。
        /// 引擎仍可用且 forceReload / 配置变更时：先 Shutdown 再 Initialize；
        /// 引擎不可用时直接 Initialize（同 dll 可由引擎层附着已运行 Runtime）。
        /// </summary>
        public static Status ApplyPythonConfigAndInit(bool forceReload)
        {
            string resolved = PythonHomeResolver.ResolvePythonHome();
            if (resolved == null)
            {
                return new Status(
                    false,
                    "PythonHome 未就绪",
                    "运行 Tools/bootstrap/Install-PythonHome.ps1，或见 Docs/skills/utagent-env-bootstrap");
            }

            string home = resolved;
            string dll = UTAgentConfig.ResolvePythonDll();
            bool homeChanged = !PathsEqual(sAppliedHome, home);
            bool dllChanged = !string.Equals(sAppliedDll, dll, StringComparison.OrdinalIgnoreCase);
            bool hasSnapshot = !string.IsNullOrEmpty(sAppliedDll);

            if (UTAgentBootstrap.IsAvailable && !forceReload && !hasSnapshot)
            {
                // Chat/CLI 已拉起引擎，快照尚未记录：认领当前配置，避免无谓重载
                sAppliedHome = home;
                sAppliedDll = dll;
                return new Status(true, "引擎已在运行", "");
            }

            bool needReload = forceReload
                || (hasSnapshot && (homeChanged || dllChanged));

            if (UTAgentBootstrap.IsAvailable && !needReload)
            {
                return new Status(true, "引擎已在运行", "");
            }

            // 同 dll 且引擎已不可用：直接 Initialize（引擎层会附着已运行 Runtime，不强制 Shutdown）
            // 仅在引擎仍可用且需要 forceReload / 配置变更时才 Shutdown
            bool didShutdown = false;
            if (UTAgentBootstrap.IsAvailable && needReload)
            {
                try
                {
                    UTAgentBootstrap.Shutdown();
                    didShutdown = true;
                }
                catch (Exception e)
                {
                    return new Status(
                        false,
                        "重置引擎失败",
                        FormatInitFailureDetail(e));
                }
            }

            try
            {
                UTAgentBootstrap.Initialize();
                sAppliedHome = home;
                sAppliedDll = dll;
                if (didShutdown)
                {
                    return new Status(true, "已按新配置重新初始化", "");
                }

                return new Status(true, "引擎已启动", "");
            }
            catch (Exception e)
            {
                ClearAppliedSnapshot();
                return new Status(
                    false,
                    "初始化失败",
                    FormatInitFailureDetail(e));
            }
        }

        /// <summary>
        /// 按异常类型拼用户可读详情；Soft-reattach / PythonDLL 锁死类明确要求重启 Editor。
        /// </summary>
        public static string FormatInitFailureDetail(Exception e)
        {
            string message = e?.Message ?? "未知错误";
            if (IsRestartRequiredInitFailure(e))
            {
                return message + "\n" + RestartEditorHint;
            }

            return message + "\n" + DomainReloadHint;
        }

        private static bool IsRestartRequiredInitFailure(Exception e)
        {
            for (Exception cur = e; cur != null; cur = cur.InnerException)
            {
                string msg = cur.Message ?? "";
                if (msg.IndexOf("PythonDLL", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("must be set before runtime is initialized", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("Runtime 已锁定", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("探活失败", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("Soft-reattach", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("请重启 Unity Editor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 手动 Shutdown 后清快照，下次保存会重新 Initialize。
        /// </summary>
        public static void ClearAppliedSnapshot()
        {
            sAppliedHome = "";
            sAppliedDll = "";
        }

        private static bool PathsEqual(string a, string b)
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
                    System.IO.Path.GetFullPath(a.Trim()),
                    System.IO.Path.GetFullPath(b.Trim()),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string FormatStatusBox(Status status)
        {
            var sb = new StringBuilder();
            sb.Append(status.Ready ? "✓ " : "○ ");
            sb.Append(status.Summary);
            if (!string.IsNullOrWhiteSpace(status.Detail))
            {
                sb.Append("\n").Append(status.Detail);
            }

            return sb.ToString().TrimEnd();
        }
    }
}

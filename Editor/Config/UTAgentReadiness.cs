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
                    "在 Python Tab 选择安装目录，或设置 PYTHONHOME / 拷入 Assets/UTAgent/PythonHome/");
            }

            if (!UTAgentBootstrap.IsAvailable)
            {
                string hint = UTAgentBootstrap.IsInvalidated
                    ? "引擎因脚本编译失效，发送消息时将自动恢复"
                    : "引擎未启动，发送消息时将自动初始化";
                return new Status(false, "引擎未就绪", hint);
            }

            if (runner != null && runner.IsConfigured())
            {
                return new Status(true, "可以对话", "");
            }

            return new Status(false, "Agent 未配置", "保存设置后去 Chat 发一条消息即可");
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
                    "Settings → Python：选择含 python312.dll 的目录（只需选一次）");
            }

            if (!UTAgentBootstrap.IsAvailable)
            {
                try
                {
                    UTAgentBootstrap.Initialize();
                }
                catch (Exception e)
                {
                    return new Status(false, "Python 初始化失败", e.Message);
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
        /// 仅同步 Python 引擎（保存 Python 路径后调用）。
        /// </summary>
        public static Status TryEnsurePythonEngine()
        {
            if (PythonHomeResolver.ResolvePythonHome() == null)
            {
                return new Status(false, "路径无效", "请选择有效的 Python 安装目录");
            }

            if (UTAgentBootstrap.IsAvailable)
            {
                return new Status(true, "引擎已在运行", "");
            }

            try
            {
                UTAgentBootstrap.Initialize();
                return new Status(true, "引擎已启动", "");
            }
            catch (Exception e)
            {
                return new Status(false, "初始化失败", e.Message);
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

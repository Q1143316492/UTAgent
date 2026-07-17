using System;
using UTAgent.Editor.Config;

namespace UTAgent.Editor.Agent
{
    public sealed partial class UTAgentRunner
    {
        // ----- after-tool 钩子（对齐 Pi afterToolCall 扩展点） -----

        /// <summary>
        /// exec 后置处理：可改写 content/preview、注入 reminder、置 TerminateAfterTools。
        /// 在 append_tool_result 之前调用。产品默认含 stdout 截断（afterToolTruncateChars）。
        /// </summary>
        private void AfterToolProcess(TurnState turn, string code, ref string content, ref string preview)
        {
            // code 供后续策略（无进展等）使用
            _ = code;
            ApplyStdoutTruncateIfEnabled(turn, ref content, ref preview);
        }

        /// <summary>
        /// 产品级 stdout 截断：N&gt;0 且 content 超长时改写并记录 after-tool log。不置 terminate。
        /// </summary>
        private static void ApplyStdoutTruncateIfEnabled(TurnState turn, ref string content, ref string preview)
        {
            int n = UTAgentConfig.ResolveAfterToolTruncateChars();
            if (n <= 0 || string.IsNullOrEmpty(content))
            {
                return;
            }

            int originalLen = content.Length;
            if (originalLen <= n)
            {
                return;
            }

            string marker = $"\n…[truncated by after-tool, original={originalLen}]";
            content = content.Substring(0, n) + marker;
            if (!string.IsNullOrEmpty(preview) && preview.Length > n)
            {
                preview = preview.Substring(0, n) + marker;
            }

            LogAfterTool(turn, "truncate", $"{originalLen} chars", "rewrite");
        }

        /// <summary>
        /// 供后续策略调用：注入 reminder 并记 after-tool 日志。
        /// </summary>
        private void AfterToolInjectReminder(TurnState turn, string domain, string reminder)
        {
            InjectReminder(turn, reminder);
            LogAfterTool(turn, domain, "-", "inject reminder");
        }

        /// <summary>
        /// 供后续策略调用：本批 tool 结束后终止自动下一轮 LLM。
        /// </summary>
        private static void AfterToolRequestTerminate(TurnState turn, string domain)
        {
            turn.TerminateAfterTools = true;
            LogAfterTool(turn, domain, "-", "terminate");
        }

        private static void LogAfterTool(TurnState turn, string domain, string detail, string action)
        {
            string text = string.IsNullOrEmpty(detail) || detail == "-"
                ? $"{domain} → {action}"
                : $"{domain}, {detail} → {action}";
            PushProgress(turn, "after-tool", text);
        }
    }
}

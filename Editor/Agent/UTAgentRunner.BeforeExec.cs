using System;
using System.Text.RegularExpressions;
using Debug = UnityEngine.Debug;

namespace UTAgent.Editor
{
    public sealed partial class UTAgentRunner
    {
        // ----- before-exec 域校验（对齐 Pi beforeToolCall 扩展点） -----

        // 强信号：Wnd* 命名
        private static readonly Regex sWndName = new Regex(@"\bWnd[A-Z]", RegexOptions.Compiled);
        // 强信号：TMP UI 类型
        private static readonly Regex sTmpTypes = new Regex(@"TMP_InputField|TextMeshProUGUI", RegexOptions.Compiled);
        // 强信号：AddComponent(UI 组件)
        private static readonly Regex sUiComponentAdd = new Regex(
            @"AddComponent\([^)]*(Image|Button|VerticalLayoutGroup|HorizontalLayoutGroup|LayoutGroup|Toggle|Slider|ScrollRect|Dropdown|TMP_InputField|TextMeshProUGUI)",
            RegexOptions.Compiled);
        // RectTransform 锚点操作（需与 Canvas 共现才算强信号）
        private static readonly Regex sRectAnchor = new Regex(@"anchorMin|anchorMax|sizeDelta|anchoredPosition", RegexOptions.Compiled);
        // Canvas 字样
        private static readonly Regex sCanvasOp = new Regex(@"\bCanvas\b", RegexOptions.Compiled);
        // 弱信号：UI 命名前缀（需与 Canvas 或 AddComponent(UI 组件) 共现）
        private static readonly Regex sUiNamePrefix = new Regex(@"\b(Btn|Txt|Grp|Input)[A-Z]", RegexOptions.Compiled);
        // 排查域信号
        private static readonly Regex sDescribeGo = new Regex(@"describe_go", RegexOptions.Compiled);

        /// <summary>
        /// exec 前置域校验：UI 域 exec 未 load 对应 skill 时拦截，注入 user 提醒。
        /// 对齐 Pi beforeToolCall 扩展点。返回 true 放行；false 拦截（已注入提醒，调用方 continue 跳过本轮 exec）。
        /// </summary>
        private bool BeforeExecCheck(TurnState turn, string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return true;
            }

            // 排查域优先：describe_go 要求 editor-ui-debug
            if (sDescribeGo.IsMatch(code))
            {
                if (HasLoadedSkill(turn, "editor-ui-debug"))
                {
                    LogBeforeExec(turn, "debug-domain", "loaded", "allow");
                    return true;
                }
                InjectSkillReminder(turn, "editor-ui-debug");
                LogBeforeExec(turn, "debug-domain", "missing", "inject reminder");
                return false;
            }

            // UI 域信号判定
            if (!IsUiDomain(code))
            {
                LogBeforeExec(turn, "non-ui", "-", "allow");
                return true;
            }

            if (HasLoadedSkill(turn, "editor-ui"))
            {
                LogBeforeExec(turn, "ui-domain", "loaded", "allow");
                return true;
            }

            InjectSkillReminder(turn, "editor-ui");
            LogBeforeExec(turn, "ui-domain", "missing", "inject reminder");
            return false;
        }

        /// <summary>
        /// UI 域信号判定：强信号直接命中；弱信号需组合共现。
        /// </summary>
        private static bool IsUiDomain(string code)
        {
            if (sWndName.IsMatch(code))
            {
                return true;
            }
            if (sTmpTypes.IsMatch(code))
            {
                return true;
            }
            if (sUiComponentAdd.IsMatch(code))
            {
                return true;
            }
            // RectTransform 锚点 + Canvas 共现
            if (sRectAnchor.IsMatch(code) && sCanvasOp.IsMatch(code))
            {
                return true;
            }
            // 弱信号组合：UI 命名前缀 + (Canvas 或 AddComponent(UI 组件))
            if (sUiNamePrefix.IsMatch(code) && (sCanvasOp.IsMatch(code) || sUiComponentAdd.IsMatch(code)))
            {
                return true;
            }
            return false;
        }

        private static void LogBeforeExec(TurnState turn, string domain, string skillState, string action)
        {
            string text = $"{domain}, skill={skillState} → {action}";
            PushProgress(turn, "before-exec", text);
        }

        /// <summary>
        /// 查询当前 history 是否已成功 load 某 skill（通过 agent.get_loaded_skills()）。
        /// </summary>
        private bool HasLoadedSkill(TurnState turn, string skillName)
        {
            string result = SafeExec(ModuleImport + "agent.get_loaded_skills()\n");
            string arr = ExtractJsonArrayField(result, "skills");
            if (string.IsNullOrEmpty(arr))
            {
                return false;
            }
            // arr 形如 ["editor-ui","python-interop"]；带引号包含判定，避免 editor-ui / editor-ui-debug 误匹配
            return arr.Contains("\"" + skillName + "\"");
        }

        /// <summary>
        /// 拦截时向 history 注入 role: user 提醒，并推 observation 进度事件。
        /// </summary>
        private void InjectSkillReminder(TurnState turn, string skillName)
        {
            string reminder = $"检测到 UI 域操作，请先 loadSkill(\"{skillName}\") 再执行相关代码。";
            PushProgress(turn, "observation", $"⚠ {reminder}");
            try
            {
                SafeExec(ModuleImport +
                    $"agent.append_user_message({EscapePy(reminder)})\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgentRunner] append_user_message 失败：{e.Message}");
            }
        }
    }
}

using System;
using System.Text.RegularExpressions;
using UTAgent.Editor.Core;
using Debug = UnityEngine.Debug;

namespace UTAgent.Editor.Agent
{
    public sealed partial class UTAgentRunner
    {
        // ----- before-exec（对齐 Pi beforeToolCall）-----
        // L1 全局策略 → UTAgentExecPolicy；以下为 Chat 域规则（依赖 history / UI 语义）

        /// <summary>
        /// UI 强信号：Wnd* 命名。
        /// </summary>
        private static readonly Regex sWndName = new Regex(@"\bWnd[A-Z]", RegexOptions.Compiled);

        /// <summary>
        /// UI 强信号：TMP 控件类型。
        /// </summary>
        private static readonly Regex sTmpTypes = new Regex(
            @"TMP_InputField|TextMeshProUGUI",
            RegexOptions.Compiled);

        /// <summary>
        /// UI 强信号：AddComponent(常见 UI 组件)。
        /// </summary>
        private static readonly Regex sUiComponentAdd = new Regex(
            @"AddComponent\([^)]*(Image|Button|VerticalLayoutGroup|HorizontalLayoutGroup|LayoutGroup|Toggle|Slider|ScrollRect|Dropdown|TMP_InputField|TextMeshProUGUI)",
            RegexOptions.Compiled);

        /// <summary>
        /// RectTransform 锚点/尺寸操作（须与 Canvas 共现才算 UI 强信号）。
        /// </summary>
        private static readonly Regex sRectAnchor = new Regex(
            @"anchorMin|anchorMax|sizeDelta|anchoredPosition",
            RegexOptions.Compiled);

        /// <summary>
        /// Canvas 字样。
        /// </summary>
        private static readonly Regex sCanvasOp = new Regex(@"\bCanvas\b", RegexOptions.Compiled);

        /// <summary>
        /// UI 弱信号：Btn/Txt/Panel/Input 前缀（须与 Canvas 或 UI AddComponent 共现）。
        /// </summary>
        private static readonly Regex sUiNamePrefix = new Regex(
            @"\b(Btn|Txt|Panel|Input)[A-Z]",
            RegexOptions.Compiled);

        /// <summary>
        /// 排查域：describe_go 原语。
        /// </summary>
        private static readonly Regex sDescribeGo = new Regex(@"describe_go", RegexOptions.Compiled);

        /// <summary>
        /// LayoutGroup 创建（项目配方：同段须设 childControl*）。
        /// </summary>
        private static readonly Regex sAddLayoutGroup = new Regex(
            @"AddComponent\([^)]*(?:Vertical|Horizontal|Grid)?LayoutGroup",
            RegexOptions.Compiled);

        /// <summary>
        /// LayoutGroup.childControlWidth 赋值。
        /// </summary>
        private static readonly Regex sChildControlWidth = new Regex(
            @"childControlWidth\s*=",
            RegexOptions.Compiled);

        /// <summary>
        /// LayoutGroup.childControlHeight 赋值。
        /// </summary>
        private static readonly Regex sChildControlHeight = new Regex(
            @"childControlHeight\s*=",
            RegexOptions.Compiled);

        /// <summary>
        /// exec 前置校验：先 L1 共享策略，再 Chat 域规则。
        /// 返回 true 放行；false 拦截（已注入提醒，调用方 continue 跳过本轮 exec）。
        /// </summary>
        private bool BeforeExecCheck(TurnState turn, string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return true;
            }

            // ----- L1 全局（与 CLI 共用 UTAgentExecPolicy）-----
            UTAgentExecPolicy.Result shared = UTAgentExecPolicy.EvaluateShared(code);
            if (!shared.Allowed)
            {
                UTAgentExecPolicy.RecordBlock(shared.Domain, code.Length, "chat");
                InjectReminder(turn, shared.Message);
                string skillState = shared.Domain == "code-too-long"
                    ? $"{code.Length} chars"
                    : "-";
                LogBeforeExec(turn, shared.Domain, skillState, "inject reminder");
                return false;
            }

            // ----- 域规则（仅 Chat；CLI 不跑）-----
            return BeforeExecDomainCheck(turn, code);
        }

        /// <summary>
        /// Chat 域规则：layout-control、debug/UI skill 门。
        /// </summary>
        private bool BeforeExecDomainCheck(TurnState turn, string code)
        {
            // LayoutGroup 四布尔：AddComponent Layout 后同段须设 childControlWidth/Height
            if (sAddLayoutGroup.IsMatch(code))
            {
                if (!sChildControlWidth.IsMatch(code) || !sChildControlHeight.IsMatch(code))
                {
                    InjectReminder(turn,
                        "AddComponent(LayoutGroup) 后必须同段设置 childControlWidth=True 与 childControlHeight=True（否则子节点易宽高为 0）。见 editor-ui LayoutGroup 硬规则。");
                    LogBeforeExec(turn, "layout-control", "-", "inject reminder");
                    return false;
                }
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
            InjectReminder(turn, reminder);
        }

        /// <summary>
        /// 向 history 注入 role: user 提醒并推 observation 进度事件（通用，供体积/反射守卫复用）。
        /// </summary>
        private void InjectReminder(TurnState turn, string reminder)
        {
            PushProgress(turn, "observation", $"⚠ {reminder}");
            try
            {
                SafeExec(ModuleImport +
                    $"agent.append_user_message({EscapePy(reminder)}, kind=\"reminder\", ephemeral=True)\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgentRunner] append_user_message 失败：{e.Message}");
            }
        }
    }
}

using System;
using System.Text.RegularExpressions;
using Debug = UnityEngine.Debug;

namespace UTAgent.Editor.Agent
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
        private static readonly Regex sUiNamePrefix = new Regex(@"\b(Btn|Txt|Panel|Input)[A-Z]", RegexOptions.Compiled);
        // 排查域信号
        private static readonly Regex sDescribeGo = new Regex(@"describe_go", RegexOptions.Compiled);
        // 重型全量反射黑名单：GetComponents(typeof(Component)) / GetComponents(CS.UnityEngine.Component) 及 InParent/InChildren 变体（不拦指定类型如 GetComponents(Image)）
        private static readonly Regex sHeavyReflection = new Regex(
            @"GetComponents(?:InParent|InChildren)?\(\s*(?:typeof\s*\(\s*Component\s*\)|(?:[A-Za-z_][A-Za-z0-9_.]*\.)?Component\s*)(?:,[^)]*)?\)",
            RegexOptions.Compiled);
        // 单步代码体积上限（防 ReAct 雪球长脚本）
        private const int kCodeSizeLimit = 4000;
        // LayoutGroup 创建：须同段设置 childControlWidth/Height（防零宽塌陷）
        private static readonly Regex sAddLayoutGroup = new Regex(
            @"AddComponent\([^)]*(?:Vertical|Horizontal|Grid)?LayoutGroup",
            RegexOptions.Compiled);
        private static readonly Regex sChildControlWidth = new Regex(
            @"childControlWidth\s*=",
            RegexOptions.Compiled);
        private static readonly Regex sChildControlHeight = new Regex(
            @"childControlHeight\s*=",
            RegexOptions.Compiled);

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

            // 体积守卫：单步过长（防 ReAct 雪球长脚本），在域判定之前
            int n = code.Length;
            if (n > kCodeSizeLimit)
            {
                InjectReminder(turn, $"单步代码过长（{n} chars > {kCodeSizeLimit}），拆成小步或用原语（add_to_layout / add_free_child）。");
                LogBeforeExec(turn, "code-too-long", $"{n} chars", "inject reminder");
                return false;
            }

            // 重型全量反射守卫：禁止 GetComponents(typeof(Component)) 等全量反射（易致 Unity 卡死/闪退）
            if (sHeavyReflection.IsMatch(code))
            {
                InjectReminder(turn, "禁止全量反射 GetComponents(typeof(Component))，用 describe_go 或指定具体类型（如 GetComponents(Image)）。");
                LogBeforeExec(turn, "heavy-reflection", "-", "inject reminder");
                return false;
            }

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

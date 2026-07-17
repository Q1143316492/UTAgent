using System.Text.RegularExpressions;
using UTAgent.Editor.Config;

namespace UTAgent.Editor.Agent
{
    public sealed partial class UTAgentRunner
    {
        // ----- after-tool 无进展矫正（可关；不进 ExecuteToolCalls 主循环） -----

        private static readonly Regex sReconHint = new Regex(
            @"find_objects?\s*\(|get_hierarchy\s*\(|describe_go\s*\(|get_type_details\s*\(|" +
            @"GameObject\.Find\s*\(|FindObjectsOfType|FindObjectOfType",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex sMutationHint = new Regex(
            @"create_tmp_button|create_layout_panel|create_tmp_input_field|add_to_layout|add_free_child|" +
            @"prepare_scene_object|destroy_object|destroy_all|create_primitive|DestroyImmediate|" +
            @"AddComponent\s*\(|SetParent\s*\(|new\s+GameObject\s*\(|" +
            @"\.color\s*=|childControlWidth\s*=|childControlHeight\s*=|" +
            @"preferredWidth\s*=|preferredHeight\s*=|interactable\s*=|SaveScene",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private const int kNoProgressMaxInjectPerTurn = 1;

        /// <summary>
        /// 无进展策略：连续纯侦察达阈值则 soft inject。默认关闭。
        /// </summary>
        private void ApplyNoProgressIfEnabled(TurnState turn, string code)
        {
            if (!UTAgentConfig.ResolveNoProgressEnabled())
            {
                return;
            }

            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            if (LooksLikeMutation(code))
            {
                turn.NoProgressStreak = 0;
                return;
            }

            if (!LooksLikeReconOnly(code))
            {
                return;
            }

            turn.NoProgressStreak++;
            int threshold = UTAgentConfig.ResolveNoProgressStreak();
            if (turn.NoProgressStreak < threshold)
            {
                return;
            }

            if (turn.NoProgressInjectCount >= kNoProgressMaxInjectPerTurn)
            {
                return;
            }

            turn.NoProgressInjectCount++;
            turn.NoProgressStreak = 0;
            InjectReminder(turn,
                $"检测到连续 {threshold} 步仅侦察无变更（空转）。请停止盲找：改用 find_objects（含 inactive）、原语创建/改属性，或明确放弃并说明原因。");
            LogAfterTool(turn, "no-progress", $"streak={threshold}", "inject reminder");
        }

        /// <summary>含变更信号则优先于侦察（供 L0 镜像与策略共用语义）。</summary>
        internal static bool LooksLikeMutation(string code)
        {
            return !string.IsNullOrEmpty(code) && sMutationHint.IsMatch(code);
        }

        /// <summary>纯侦察：有侦察信号且无变更信号。</summary>
        internal static bool LooksLikeReconOnly(string code)
        {
            if (string.IsNullOrEmpty(code) || LooksLikeMutation(code))
            {
                return false;
            }

            return sReconHint.IsMatch(code);
        }
    }
}

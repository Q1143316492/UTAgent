using System.Collections.Generic;
using UTAgent.Editor.Core;

namespace UTAgent.Editor.Agent
{
    /// <summary>
    /// Pi 风格 steering / follow-up 队列：运行中纠偏与终轮追加，挂在 Runner 循环边界。
    /// </summary>
    public sealed partial class UTAgentRunner
    {
        private readonly Queue<string> mSteering = new Queue<string>();
        private readonly Queue<string> mFollowUp = new Queue<string>();

        /// <summary>
        /// 是否有进行中的 turn。
        /// </summary>
        public bool HasActiveTurn => mActiveTurns.Count > 0;

        /// <summary>
        /// 运行中纠偏：入队，本批 tool 结束后、下一轮 LLM 前注入。无活跃 turn 时返回 false。
        /// </summary>
        public bool Steer(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || mActiveTurns.Count == 0)
            {
                return false;
            }

            mSteering.Enqueue(text.Trim());
            return true;
        }

        /// <summary>
        /// 终轮 follow-up：入队，无 tool_calls 结束前注入并再跑一轮。无活跃 turn 时返回 false。
        /// </summary>
        public bool FollowUp(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || mActiveTurns.Count == 0)
            {
                return false;
            }

            mFollowUp.Enqueue(text.Trim());
            return true;
        }

        /// <summary>
        /// 向 history 追加 user 消息（Chat 打断续跑前写入；对 LLM 可见）。
        /// </summary>
        public void AppendUserMessage(string text, string kind = "user")
        {
            if (string.IsNullOrWhiteSpace(text) || !UTAgentBootstrap.IsAvailable)
            {
                return;
            }

            SafeExec(ModuleImport +
                $"agent.append_user_message({EscapePy(text.Trim())}, {EscapePy(kind)}, False)\n");
        }

        private void ClearSteeringQueues()
        {
            mSteering.Clear();
            mFollowUp.Clear();
        }

        /// <summary>
        /// 取一条 steering 注入 history（对 LLM 可见，非 ephemeral）。
        /// </summary>
        private bool DrainSteeringBeforeNextLlm(TurnState turn)
        {
            if (mSteering.Count == 0)
            {
                return false;
            }

            string text = mSteering.Dequeue();
            InjectUserMessage(turn, text, "steering", "steering");
            return true;
        }

        /// <summary>
        /// 取一条 follow-up 注入 history；调用方负责再 PrepareNextRequest。
        /// </summary>
        private bool DrainFollowUpBeforeFinish(TurnState turn)
        {
            if (mFollowUp.Count == 0)
            {
                return false;
            }

            string text = mFollowUp.Dequeue();
            InjectUserMessage(turn, text, "follow-up", "follow-up");
            return true;
        }

        private void InjectUserMessage(TurnState turn, string text, string kind, string logKind)
        {
            string preview = text.Length > 80 ? text.Substring(0, 80) + "…" : text;
            PushProgress(turn, "status", $"{logKind}: {preview} → inject");
            SafeExec(ModuleImport +
                $"agent.append_user_message({EscapePy(text)}, {EscapePy(kind)}, False)\n");
        }
    }
}

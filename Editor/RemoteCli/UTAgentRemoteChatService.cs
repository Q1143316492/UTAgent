using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UTAgent.Editor.Agent;
using UTAgent.Editor.Config;
using UTAgent.Editor.Core;
using UTAgent.Editor.PythonInterop;

namespace UTAgent.Editor.RemoteCli
{
    /// <summary>
    /// RemoteCli 侧 headless Chat：复用 <see cref="UTAgentRunner"/>，与 Chat 窗口独立会话。
    /// </summary>
    public sealed class UTAgentRemoteChatService
    {
        private static UTAgentRemoteChatService sInstance;

        private static readonly Regex mStepRegex = new Regex(@"第 (\d+) 步", RegexOptions.Compiled);

        private readonly UTAgentRunner mRunner = new UTAgentRunner();
        private readonly Dictionary<string, RemoteChatTurn> mTurns = new Dictionary<string, RemoteChatTurn>();
        private string mRunningTurnId;

        public static UTAgentRemoteChatService Instance => sInstance ??= new UTAgentRemoteChatService();

        private UTAgentRemoteChatService()
        {
        }

        public bool HasRunningTurn
        {
            get
            {
                lock (mTurns)
                {
                    return mRunningTurnId != null
                        && mTurns.TryGetValue(mRunningTurnId, out RemoteChatTurn t)
                        && t.Status == "running";
                }
            }
        }

        public bool TryStartChat(string message, out RemoteChatTurn turn, out string error, out int httpStatus)
        {
            turn = null;
            error = null;
            httpStatus = 200;

            if (string.IsNullOrWhiteSpace(message))
            {
                error = "missing message";
                httpStatus = 400;
                return false;
            }

            lock (mTurns)
            {
                if (mRunningTurnId != null
                    && mTurns.TryGetValue(mRunningTurnId, out RemoteChatTurn active)
                    && active.Status == "running")
                {
                    error = $"已有任务运行中（turn_id={mRunningTurnId}）";
                    httpStatus = 409;
                    return false;
                }
            }

            if (!UTAgentBootstrap.IsAvailable)
            {
                error = UTAgentBootstrap.IsInvalidated
                    ? "引擎因域重载失效，请 POST /initialize 或运行 utagent init"
                    : "引擎未初始化，请 POST /initialize 或运行 utagent init";
                httpStatus = 503;
                return false;
            }

            if (!EnsureRunnerConfigured(out string configError))
            {
                error = configError;
                httpStatus = 503;
                return false;
            }

            turn = new RemoteChatTurn
            {
                TurnId = Guid.NewGuid().ToString("N").Substring(0, 8),
                Status = "running",
                Message = message.Trim(),
                Step = 0,
                LastStatus = "已提交",
                StartedAtUtcMs = UtcNowMs(),
                UpdatedAtUtcMs = UtcNowMs(),
            };

            lock (mTurns)
            {
                mTurns[turn.TurnId] = turn;
                mRunningTurnId = turn.TurnId;
            }

            RemoteChatTurn captured = turn;
            mRunner.SendMessageAsync(
                turn.Message,
                null,
                (finalText, isError, outcome, events) =>
                {
                    lock (mTurns)
                    {
                        captured.Status = "done";
                        captured.FinalText = finalText ?? string.Empty;
                        captured.IsError = isError;
                        captured.Outcome = outcome ?? "success";
                        captured.UpdatedAtUtcMs = UtcNowMs();
                        if (mRunningTurnId == captured.TurnId)
                        {
                            mRunningTurnId = null;
                        }
                    }
                },
                evt =>
                {
                    if (evt.Type != "status" || string.IsNullOrWhiteSpace(evt.Text))
                    {
                        return;
                    }

                    lock (mTurns)
                    {
                        captured.LastStatus = evt.Text;
                        Match match = mStepRegex.Match(evt.Text);
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int step))
                        {
                            captured.Step = step;
                        }

                        captured.UpdatedAtUtcMs = UtcNowMs();
                    }
                });

            return true;
        }

        public bool TryGetTurn(string turnId, out RemoteChatTurn turn)
        {
            lock (mTurns)
            {
                if (string.IsNullOrEmpty(turnId))
                {
                    turn = null;
                    return false;
                }

                return mTurns.TryGetValue(turnId, out turn);
            }
        }

        public string BuildTurnJson(RemoteChatTurn turn)
        {
            if (turn == null)
            {
                return "{\"ok\":false,\"error\":\"turn not found\"}";
            }

            return "{\"ok\":true" +
                $",\"turn_id\":{BridgeJson.EscapeJson(turn.TurnId)}" +
                $",\"status\":{BridgeJson.EscapeJson(turn.Status)}" +
                $",\"step\":{turn.Step}" +
                $",\"last_status\":{BridgeJson.EscapeJson(turn.LastStatus ?? string.Empty)}" +
                $",\"outcome\":{BridgeJson.EscapeJson(turn.Outcome ?? string.Empty)}" +
                $",\"is_error\":{BridgeJson.ToLower(turn.IsError)}" +
                $",\"final_text\":{BridgeJson.EscapeJson(turn.FinalText ?? string.Empty)}" +
                $",\"log_directory\":{BridgeJson.EscapeJson(UTAgentSessionLogger.ResolveLogDirectory())}" +
                "}";
        }

        private bool EnsureRunnerConfigured(out string error)
        {
            error = null;
            UTAgentReadiness.Status status = UTAgentReadiness.TryEnsureChatReady(mRunner);
            if (!status.Ready)
            {
                error = string.IsNullOrWhiteSpace(status.Detail)
                    ? status.Summary
                    : $"{status.Summary}: {status.Detail}";
                return false;
            }

            return true;
        }

        private static long UtcNowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public sealed class RemoteChatTurn
        {
            public string TurnId;
            public string Status;
            public string Message;
            public int Step;
            public string LastStatus;
            public string FinalText;
            public string Outcome;
            public bool IsError;
            public long StartedAtUtcMs;
            public long UpdatedAtUtcMs;
        }
    }
}

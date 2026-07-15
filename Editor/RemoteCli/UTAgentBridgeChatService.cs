using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UTAgent.Editor.Agent;
using UTAgent.Editor.Core;
using UTAgent.Editor.PythonInterop;

namespace UTAgent.Editor.RemoteCli
{
    /// <summary>
    /// Bridge 侧 headless Chat：复用 <see cref="UTAgentRunner"/>，与 Chat 窗口独立会话。
    /// </summary>
    public sealed class UTAgentBridgeChatService
    {
        private static UTAgentBridgeChatService sInstance;

        private static readonly Regex mStepRegex = new Regex(@"第 (\d+) 步", RegexOptions.Compiled);

        private readonly UTAgentRunner mRunner = new UTAgentRunner();
        private readonly Dictionary<string, BridgeChatTurn> mTurns = new Dictionary<string, BridgeChatTurn>();
        private string mRunningTurnId;

        public static UTAgentBridgeChatService Instance => sInstance ??= new UTAgentBridgeChatService();

        private UTAgentBridgeChatService()
        {
        }

        public bool HasRunningTurn
        {
            get
            {
                lock (mTurns)
                {
                    return mRunningTurnId != null
                        && mTurns.TryGetValue(mRunningTurnId, out BridgeChatTurn t)
                        && t.Status == "running";
                }
            }
        }

        public bool TryStartChat(string message, out BridgeChatTurn turn, out string error, out int httpStatus)
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
                    && mTurns.TryGetValue(mRunningTurnId, out BridgeChatTurn active)
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

            turn = new BridgeChatTurn
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

            BridgeChatTurn captured = turn;
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

        public bool TryGetTurn(string turnId, out BridgeChatTurn turn)
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

        public string BuildTurnJson(BridgeChatTurn turn)
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
            if (!UTAgentBootstrap.IsAvailable)
            {
                error = "引擎不可用";
                return false;
            }

            string apiKey = UTAgentPrefs.MigrateString(
                UTAgentPrefs.AgentApiKeyKey,
                UTAgentPrefs.LegacyAgentApiKeyKey);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                error = "未配置 API Key，请在 UT Agent Chat 设置中保存 LLM 配置";
                return false;
            }

            string baseUrl = UTAgentPrefs.MigrateString(
                UTAgentPrefs.AgentBaseUrlKey,
                UTAgentPrefs.LegacyAgentBaseUrlKey);
            string model = UTAgentPrefs.MigrateString(
                UTAgentPrefs.AgentModelKey,
                UTAgentPrefs.LegacyAgentModelKey,
                UTAgentPrefs.DefaultModel);
            int maxSteps = UTAgentPrefs.MigrateInt(
                UTAgentPrefs.AgentMaxStepsKey,
                UTAgentPrefs.LegacyAgentMaxStepsKey,
                UTAgentPrefs.DefaultMaxSteps);

            if (mRunner.IsConfigured())
            {
                return true;
            }

            string result = mRunner.Configure(apiKey, baseUrl, model, maxSteps);
            if (!mRunner.IsConfigured())
            {
                error = string.IsNullOrWhiteSpace(result) ? "Runner configure 失败" : result;
                return false;
            }

            return true;
        }

        private static long UtcNowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public sealed class BridgeChatTurn
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

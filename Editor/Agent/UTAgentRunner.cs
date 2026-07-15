using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UTAgent.Editor.Config;
using UTAgent.Editor.Core;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace UTAgent.Editor.Agent
{
    /// <summary>
    /// UT Agent 运行器（异步版）。对照 puerts 的 AgentScriptManager：
    /// 主线程状态机驱动 + UnityWebRequest 异步 HTTP + SSE 流式；Python 只暴露单步原子入口。
    /// 编排对齐 puerts：OpenAI tools + tool_calls，主 tool 为 execPython。
    /// </summary>
    public sealed partial class UTAgentRunner
    {
        private const string ModuleImport = "import agent\n";

        private readonly List<TurnState> mActiveTurns = new List<TurnState>();
        private bool mConfigured;

        /// <summary>
        /// outcome 是否允许 UI 显示「继续」。
        /// </summary>
        public static bool CanContinueFromOutcome(string outcome)
        {
            return outcome == "error"
                || outcome == "aborted"
                || outcome == "max_steps_summary";
        }

        /// <summary>
        /// 自定义 DownloadHandler：照抄 puerts StreamingDownloadHandler。
        /// Unity 在主线程每次收到新字节就调 ReceiveData 写缓冲；
        /// PollPendingRequests 每帧 DrainChunks 排空。
        /// </summary>
        private sealed class StreamingDownloadHandler : DownloadHandlerScript
        {
            private readonly StringBuilder mPendingChunks = new StringBuilder();
            public bool IsCompleted { get; private set; }

            public StreamingDownloadHandler() : base(new byte[4096]) { }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0)
                    return true;
                mPendingChunks.Append(Encoding.UTF8.GetString(data, 0, dataLength));
                return true;
            }

            protected override void CompleteContent()
            {
                IsCompleted = true;
            }

            public string DrainChunks()
            {
                if (mPendingChunks.Length == 0)
                    return null;
                var result = mPendingChunks.ToString();
                mPendingChunks.Clear();
                return result;
            }

            /// <summary>
            /// 读取尚未 drain 的完整响应体（错误诊断用）。
            /// </summary>
            public string GetBufferedText()
            {
                return mPendingChunks.ToString();
            }

            protected override float GetProgress()
            {
                return 0f;
            }
        }

        /// <summary>
        /// 配置 Agent（API Key / Base URL / Model / MaxSteps）。重复调用安全。
        /// </summary>
        public string Configure(string apiKey, string baseURL, string model, int maxSteps)
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                mConfigured = false;
                return "[Runner] 引擎不可用，请先初始化";
            }
            var script = ModuleImport +
                $"agent.configure({EscapePy(apiKey)}, {EscapePy(baseURL)}, {EscapePy(model)}, {maxSteps})\n";
            string result = SafeExec(script);
            mConfigured = ParseExecOk(result, out _);
            return result;
        }

        /// <summary>
        /// Python agent 模块重载后须调用，避免 C# 缓存与 Python 状态不一致。
        /// </summary>
        public void InvalidateConfigured()
        {
            mConfigured = false;
        }

        /// <summary>
        /// 异步发送一条消息（可选附带图片）。onResponse 在主线程最终触发一次；
        /// onProgress 每次状态推进触发（SSE 流式时每收到 delta 文字即触）。全程不阻塞主线程。
        /// </summary>
        public void SendMessageAsync(string text, string imagePath,
            TurnResponseHandler onResponse,
            Action<ProgressEvent> onProgress)
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                onResponse?.Invoke("引擎因域重载失效，请重新点击初始化", true, "error", null);
                return;
            }
            if (!IsConfigured())
            {
                onResponse?.Invoke("请先在设置里填 API Key 并 Apply。", true, "error", null);
                return;
            }
            string imageBase64 = "";
            string mimeType = "";
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                byte[] bytes = File.ReadAllBytes(imagePath);
                imageBase64 = Convert.ToBase64String(bytes);
                mimeType = MimeFromExt(imagePath);
            }
            var turn = new TurnState
            {
                UserText = text,
                ImageBase64 = imageBase64,
                ImageMime = mimeType,
                Response = onResponse,
                Progress = onProgress,
                StepCount = 0,
                MaxSteps = GetMaxStepsFromConfig(),
                Logger = UTAgentSessionLogger.BeginTurn(text, UTAgentConfig.ResolveModelId(), imagePath),
            };
            mActiveTurns.Add(turn);
            if (!PrepareNextRequest(turn))
            {
                FinishTurn(turn, "准备请求失败（看 Console）", true);
            }
        }

        /// <summary>
        /// 从当前 history 续跑（不 begin_turn、不复述原任务）。用于 Stop 暂停后、error、max_steps 总结后。
        /// </summary>
        public void ContinueAsync(TurnResponseHandler onResponse, Action<ProgressEvent> onProgress)
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                onResponse?.Invoke("引擎因域重载失效，请重新点击初始化", true, "error", null);
                return;
            }
            if (!IsConfigured())
            {
                onResponse?.Invoke("请先在设置里填 API Key 并 Apply。", true, "error", null);
                return;
            }
            if (GetHistoryLength() <= 0)
            {
                onResponse?.Invoke("无对话历史可继续，请先发送消息。", true, "error", null);
                return;
            }
            string continueResult = SafeExec(ModuleImport + "agent.continue_turn()\n");
            if (!ParseExecOk(continueResult, out string continueError))
            {
                string msg;
                if (string.IsNullOrEmpty(continueResult))
                {
                    msg = "无法续跑：agent 模块未加载最新代码，请点击「初始化引擎」后重试";
                }
                else if (!string.IsNullOrEmpty(continueError))
                {
                    msg = continueError == "history empty"
                        ? "无法续跑：对话历史已空（若刚初始化过引擎，请重新发送任务）"
                        : $"无法续跑：{continueError}";
                }
                else
                {
                    msg = "无法续跑：对话历史无效";
                }
                onResponse?.Invoke(msg, true, "error", null);
                return;
            }
            var turn = new TurnState
            {
                IsContinue = true,
                IsFirst = false,
                Response = onResponse,
                Progress = onProgress,
                StepCount = 0,
                MaxSteps = GetMaxStepsFromConfig(),
                Logger = UTAgentSessionLogger.BeginContinueTurn(
                    UTAgentConfig.ResolveModelId()),
            };
            mActiveTurns.Add(turn);
            if (!PrepareNextRequest(turn))
            {
                FinishTurn(turn, "准备续跑请求失败（看 Console）", true);
            }
        }

        /// <summary>
        /// 暂停当前轮。UnityWebRequest 立即取消；exec 期由 _abort_flag 协作式停；不回滚 history。
        /// </summary>
        public void Abort()
        {
            for (int i = mActiveTurns.Count - 1; i >= 0; i--)
            {
                var turn = mActiveTurns[i];
                turn.AbortRequested = true;
                try
                {
                    turn.Request?.Abort();
                }
                catch (Exception)
                {
                }
            }
            if (UTAgentBootstrap.IsAvailable)
            {
                SafeExec(ModuleImport + "agent.abort()\n");
            }
        }

        /// <summary>
        /// 清空对话历史。
        /// </summary>
        public void ClearHistory()
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                return;
            }
            SafeExec(ModuleImport + "agent.clear_history()\n");
        }

        /// <summary>
        /// Agent 是否已配置（有 API Key 且 configure 成功）。
        /// </summary>
        public bool IsConfigured()
        {
            return UTAgentBootstrap.IsAvailable && mConfigured;
        }

        /// <summary>
        /// 当前对话历史长度。
        /// </summary>
        public int GetHistoryLength()
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                return 0;
            }
            var output = SafeExec(ModuleImport + "agent.get_history_length()\n");
            return ParseInt(output);
        }

        // ----- 内部：LLM 请求准备 -----

        private bool PrepareNextRequest(TurnState turn)
        {
            if (turn.MaxSteps > 0 && turn.StepCount >= turn.MaxSteps && !turn.IsFinalSummaryStep)
            {
                if (!BeginMaxStepsSummary(turn))
                {
                    return false;
                }
            }

            string messagesArray = null;
            try
            {
                if (turn.IsFirst)
                {
                    turn.IsFirst = false;
                    string beginOutput = SafeExec(ModuleImport +
                        $"agent.begin_turn({EscapePy(turn.UserText)}, {EscapePy(turn.ImageBase64)}, {EscapePy(turn.ImageMime)})\n");
                    _ = beginOutput;
                }
                SafeExec(ModuleImport + $"agent.ensure_model({EscapePy(turn.Model)})\n");
                SafeExec(ModuleImport + "agent.process_pending_images()\n");
                string prepareOutput = SafeExec(ModuleImport +
                    $"agent.build_llm_messages_json({turn.StepCount + 1}, {GetMaxInputTokensFromConfig()}, {GetMinKeepMessagesFromConfig()}, {EscapePy(turn.Model)})\n");
                LogPrepareResult(prepareOutput);
                messagesArray = ExtractLastJsonLine(prepareOutput);
                if (string.IsNullOrWhiteSpace(messagesArray) || !messagesArray.TrimStart().StartsWith("["))
                {
                    Debug.LogError("[UTAgentRunner] build_llm_messages_json 未返回合法 messages 数组");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UTAgentRunner] 取 messages 失败：{e}");
                return false;
            }
            string baseUrl = (turn.BaseUrl ?? "").Trim();
            if (string.IsNullOrEmpty(baseUrl))
            {
                baseUrl = "https://api.openai.com/v1";
            }
            if (!baseUrl.EndsWith("/v1"))
            {
                baseUrl = baseUrl.TrimEnd('/') + "/v1";
            }
            string url = baseUrl + "/chat/completions";
            turn.Url = url;

            string deepSeekExtras = BuildDeepSeekRequestExtras(turn.Model);
            string body;
            if (turn.IsFinalSummaryStep)
            {
                body = $"{{\"model\":{JsonStr(turn.Model)},\"messages\":{messagesArray}," +
                    $"\"stream\":true,\"tool_choice\":\"none\"{deepSeekExtras}}}";
                PushProgress(turn, "max_steps_status",
                    $"已达最大步数 {turn.MaxSteps}，正在生成总结…");
            }
            else
            {
                body = $"{{\"model\":{JsonStr(turn.Model)},\"messages\":{messagesArray}," +
                    $"\"tools\":{BuildToolSchema()},\"stream\":true,\"tool_choice\":\"auto\"{deepSeekExtras}}}";
            }
            turn.RequestBody = body;

            turn.StreamTextBuf.Clear();
            turn.StreamThinkingBuf.Clear();
            turn.SseLineBuf.Clear();
            turn.ToolCallBuf.Clear();

            turn.Logger?.BeginStep(turn.StepCount + 1);
            turn.Logger?.LogLlmRequest(url, body);

            PushProgress(turn, "status", $"调用 LLM（第 {turn.StepCount + 1} 步）…");
            return StartWebRequest(turn);
        }

        private bool StartWebRequest(TurnState turn)
        {
            try
            {
                var req = new UnityWebRequest(turn.Url, "POST");
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(turn.RequestBody));
                var streamHandler = new StreamingDownloadHandler();
                req.downloadHandler = streamHandler;
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", $"Bearer {turn.ApiKey}");
                req.timeout = 0;
                turn.Request = req;
                turn.Operation = req.SendWebRequest();
                turn.StreamHandler = streamHandler;
                EditorApplication.update += Poll;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UTAgentRunner] HTTP 起请求失败：{e}");
                return false;
            }
        }

        // ----- Poll + SSE 解析 -----

        private void Poll()
        {
            if (mActiveTurns.Count == 0)
            {
                EditorApplication.update -= Poll;
                return;
            }
            for (int i = mActiveTurns.Count - 1; i >= 0; i--)
            {
                var turn = mActiveTurns[i];

                if (turn.AbortRequested && turn.StreamHandler == null)
                {
                    mActiveTurns.RemoveAt(i);
                    FinishTurn(turn, "已暂停", false, "aborted");
                    continue;
                }

                if (turn.StreamHandler != null)
                {
                    if (turn.AbortRequested)
                    {
                        mActiveTurns.RemoveAt(i);
                        FinishTurn(turn, "已暂停", false, "aborted");
                        continue;
                    }
                    var chunk = turn.StreamHandler.DrainChunks();
                    if (chunk != null)
                    {
                        ParseSseChunk(turn, chunk);
                    }
                    if (!turn.Operation.isDone)
                    {
                        continue;
                    }
                    HandleStreamDone(turn);
                    continue;
                }
            }
            if (mActiveTurns.Count == 0)
            {
                EditorApplication.update -= Poll;
            }
        }

        /// <summary>
        /// SSE "data:" 行解析。合并 content delta；累积 tool_calls 分片。
        /// </summary>
        private static void ParseSseChunk(TurnState turn, string chunk)
        {
            if (turn.AbortRequested)
            {
                return;
            }
            turn.SseLineBuf.Append(chunk);
            string buf = turn.SseLineBuf.ToString();
            turn.SseLineBuf.Clear();
            string contentBuf = "";

            int pos = 0;
            while (pos < buf.Length)
            {
                int end = buf.IndexOf("\n\n", pos, StringComparison.Ordinal);
                if (end < 0)
                {
                    turn.SseLineBuf.Append(buf.Substring(pos));
                    break;
                }
                string message = buf.Substring(pos, end - pos);
                pos = end + 2;
                string prefix = "data: ";
                int idx = message.IndexOf(prefix, StringComparison.Ordinal);
                if (idx < 0)
                {
                    continue;
                }
                string json = message.Substring(idx + prefix.Length).Trim();
                if (json == "[DONE]")
                {
                    continue;
                }
                string dc = ExtractDeltaString(json, "content");
                string dr = ExtractDeltaString(json, "reasoning_content");
                if (!string.IsNullOrEmpty(dr))
                {
                    turn.StreamThinkingBuf.Append(dr);
                    PushProgress(turn, "thinking", dr);
                }
                if (!string.IsNullOrEmpty(dc))
                {
                    contentBuf += dc;
                }
                AccumulateToolCallsFromSseJson(json, turn);
            }
            if (contentBuf.Length > 0)
            {
                turn.StreamTextBuf.Append(contentBuf);
                PushProgress(turn, "stream", contentBuf);
            }
        }

        /// <summary>
        /// 从 SSE delta JSON 累积 tool_calls（按 index 合并 id/name/arguments 分片）。
        /// </summary>
        private static void AccumulateToolCallsFromSseJson(string json, TurnState turn)
        {
            int tcIdx = json.IndexOf("\"tool_calls\"", StringComparison.Ordinal);
            if (tcIdx < 0)
            {
                return;
            }
            string slice = json.Substring(tcIdx);
            int index = ExtractInt(slice, "index");
            if (index < 0)
            {
                return;
            }
            if (!turn.ToolCallBuf.TryGetValue(index, out var acc))
            {
                acc = new ToolCallAccumulator();
                turn.ToolCallBuf[index] = acc;
            }
            string id = ExtractString(slice, "id");
            if (!string.IsNullOrEmpty(id))
            {
                acc.Id = id;
            }
            string name = ExtractString(slice, "name");
            if (!string.IsNullOrEmpty(name))
            {
                acc.Name = name;
            }
            string args = ExtractString(slice, "arguments");
            if (!string.IsNullOrEmpty(args))
            {
                acc.Arguments.Append(args);
            }
        }

        private void HandleStreamDone(TurnState turn)
        {
            var req = turn.Operation.webRequest;
            try
            {
                bool hasError = req.result == UnityWebRequest.Result.ConnectionError
                    || req.result == UnityWebRequest.Result.DataProcessingError
                    || req.result == UnityWebRequest.Result.ProtocolError;
                if (hasError)
                {
                    string responseBody = turn.StreamHandler?.GetBufferedText();
                    if (string.IsNullOrEmpty(responseBody))
                    {
                        responseBody = req.downloadHandler?.text;
                    }
                    string detail = FormatLlmHttpError(req, turn, responseBody);
                    Debug.LogWarning($"[UTAgentRunner] LLM 请求失败\n{detail}");
                    turn.Logger?.LogLlmError(detail);
                    string err = $"LLM 连接失败 @ {turn.Url}: HTTP {req.responseCode}";
                    string apiMsg = TryExtractApiErrorMessage(responseBody ?? "");
                    if (!string.IsNullOrEmpty(apiMsg))
                    {
                        err += $" — {apiMsg}";
                    }
                    else if (!string.IsNullOrEmpty(req.error))
                    {
                        err += $" — {req.error}";
                    }

                    turn.StreamHandler?.DrainChunks();
                    FinishTurn(turn, err, true);
                    return;
                }
            }
            catch (Exception e)
            {
                FinishTurn(turn, $"处理流式响应异常：{e.Message}", true);
                return;
            }

            if (turn.AbortRequested)
            {
                FinishTurn(turn, "已暂停", false, "aborted");
                return;
            }

            string content = turn.StreamTextBuf.ToString();
            string reasoningContent = turn.StreamThinkingBuf.ToString();
            bool hasToolCalls = turn.ToolCallBuf.Count > 0;

            turn.StreamTextBuf.Clear();
            turn.StreamThinkingBuf.Clear();

            PushProgress(turn, "stream_end", "");

            try { req.Dispose(); }
            catch { }
            turn.StreamHandler = null;

            if (hasToolCalls)
            {
                if (turn.IsFinalSummaryStep)
                {
                    string fallback = content;
                    if (string.IsNullOrWhiteSpace(fallback))
                    {
                        fallback = "已达步数上限；模型仍尝试调用工具，请查看上方执行记录。";
                    }
                    PushProgress(turn, "discard_stream", "");
                    try
                    {
                        SafeExec(ModuleImport +
                            $"agent.append_assistant_content({EscapePy(fallback)}, {EscapePy(reasoningContent)})\n");
                    }
                    catch (Exception e)
                    {
                        FinishTurn(turn, $"append_assistant_content 失败：{e.Message}", true);
                        return;
                    }
                    PushProgress(turn, "answer", fallback);
                    FinishTurn(turn, fallback, false, "max_steps_summary");
                    return;
                }
                PushProgress(turn, "discard_stream", "");
                turn.StepCount++;
                PushProgress(turn, "status", $"LLM 返回 tool_calls（第 {turn.StepCount} 步）");
                string toolCallsJson = SerializeToolCalls(turn.ToolCallBuf);
                turn.ToolCallBuf.Clear();
                turn.Logger?.LogToolCalls(toolCallsJson);
                try
                {
                    SafeExec(ModuleImport + $"agent.ensure_model({EscapePy(turn.Model)})\n");
                    SafeExec(ModuleImport +
                        $"agent.append_assistant_tool_calls({EscapePy(toolCallsJson)}, {EscapePy(reasoningContent)})\n");
                }
                catch (Exception e)
                {
                    FinishTurn(turn, $"append_assistant_tool_calls 失败：{e.Message}", true);
                    return;
                }
                ExecuteToolCalls(turn, toolCallsJson);
                return;
            }

            if (string.IsNullOrEmpty(content))
            {
                FinishTurn(turn, "LLM 返回空回复", true);
                return;
            }

            turn.StepCount++;
            PushProgress(turn, "status", $"LLM 已回复（第 {turn.StepCount} 步）");
            PushProgress(turn, "discard_stream", "");
            try
            {
                SafeExec(ModuleImport + $"agent.ensure_model({EscapePy(turn.Model)})\n");
                SafeExec(ModuleImport +
                    $"agent.append_assistant_content({EscapePy(content)}, {EscapePy(reasoningContent)})\n");
            }
            catch (Exception e)
            {
                FinishTurn(turn, $"append_assistant_content 失败：{e.Message}", true);
                return;
            }
            PushProgress(turn, "answer", content);
            if (turn.IsFinalSummaryStep)
            {
                FinishTurn(turn, content, false, "max_steps_summary");
                return;
            }
            FinishTurn(turn, content, false);
        }

        /// <summary>
        /// 执行 tool_calls：execPython → execute_python_code → append_tool_result → 下一轮 LLM。
        /// </summary>
        private void ExecuteToolCalls(TurnState turn, string toolCallsJson)
        {
            if (turn.AbortRequested)
            {
                FinishTurn(turn, "已暂停", false, "aborted");
                return;
            }
            if (turn.MaxSteps > 0 && turn.StepCount > turn.MaxSteps)
            {
                if (!turn.IsFinalSummaryStep && BeginMaxStepsSummary(turn))
                {
                    if (!PrepareNextRequest(turn))
                    {
                        FinishTurn(turn, "准备收尾请求失败（看 Console）", true);
                    }
                    return;
                }
                FinishTurn(turn, $"已达到最大步数 {turn.MaxSteps}，未能完成", true, "max_steps");
                return;
            }

            var calls = ParseToolCallsFromJson(toolCallsJson);
            foreach (var call in calls)
            {
                if (turn.AbortRequested)
                {
                    FinishTurn(turn, "已暂停", false, "aborted");
                    return;
                }
                if (call.Name == "loadSkill")
                {
                    string skillName = ExtractString(call.Arguments, "name");
                    if (string.IsNullOrEmpty(skillName))
                    {
                        Debug.LogWarning("[UTAgentRunner] loadSkill 缺少 name 参数");
                        continue;
                    }
                    PushProgress(turn, "tool_call", $"loadSkill({skillName})");
                    PushProgress(turn, "status", $"加载 skill「{skillName}」…");
                    string loadResult;
                    try
                    {
                        loadResult = SafeExec(ModuleImport +
                            $"agent.load_skill({EscapePy(skillName)})\n");
                    }
                    catch (Exception e)
                    {
                        FinishTurn(turn, $"load_skill 失败：{e.Message}", true);
                        return;
                    }
                    bool skillOk = ExtractBool(loadResult, "skill_ok");
                    PushProgress(turn, "status", $"loadSkill: {skillName} {(skillOk ? "ok" : "fail")}");
                    string skillContent = ExtractString(loadResult, "content");
                    string skillPreview = ExtractString(loadResult, "preview");
                    if (!string.IsNullOrEmpty(skillPreview))
                    {
                        PushProgress(turn, "observation", skillPreview);
                    }
                    turn.Logger?.LogToolResult(call.Id, $"loadSkill({skillName})", skillContent, skillPreview);
                    try
                    {
                        SafeExec(ModuleImport +
                            $"agent.append_tool_result({EscapePy(call.Id)}, {EscapePy(skillContent)})\n");
                    }
                    catch (Exception e)
                    {
                        FinishTurn(turn, $"append_tool_result 失败：{e.Message}", true);
                        return;
                    }
                    continue;
                }
                if (call.Name != "execPython")
                {
                    Debug.LogWarning($"[UTAgentRunner] 未知 tool：{call.Name}，跳过");
                    continue;
                }
                string code = ExtractString(call.Arguments, "code");
                if (string.IsNullOrEmpty(code))
                {
                    Debug.LogWarning("[UTAgentRunner] execPython 缺少 code 参数");
                    continue;
                }
                if (!BeforeExecCheck(turn, code))
                {
                    continue;
                }
                PushProgress(turn, "tool_call", code);
                PushProgress(turn, "status", $"执行 execPython（第 {turn.StepCount} 步）…");
                string execResult;
                try
                {
                    execResult = SafeExec(ModuleImport +
                        $"agent.execute_python_code({EscapePy(code)})\n");
                }
                catch (Exception e)
                {
                    FinishTurn(turn, $"execute_python_code 失败：{e.Message}", true);
                    return;
                }
                if (ExtractBool(execResult, "aborted"))
                {
                    FinishTurn(turn, "已暂停", false, "aborted");
                    return;
                }
                string resultContent = ExtractString(execResult, "content");
                string preview = ExtractString(execResult, "preview");
                if (!string.IsNullOrEmpty(preview))
                {
                    PushProgress(turn, "observation", preview);
                }
                turn.Logger?.LogToolResult(call.Id, code, resultContent, preview);
                try
                {
                    SafeExec(ModuleImport +
                        $"agent.append_tool_result({EscapePy(call.Id)}, {EscapePy(resultContent)})\n");
                }
                catch (Exception e)
                {
                    FinishTurn(turn, $"append_tool_result 失败：{e.Message}", true);
                    return;
                }
            }

            SafeExec(ModuleImport + "agent.process_pending_images()\n");

            if (!PrepareNextRequest(turn))
            {
                FinishTurn(turn, "准备下一轮请求失败（看 Console）", true);
            }
        }

        /// <summary>
        /// 从序列化后的 tool_calls JSON 数组解析 id/name/arguments。
        /// </summary>
        private static List<ParsedToolCall> ParseToolCallsFromJson(string json)
        {
            var result = new List<ParsedToolCall>();
            if (string.IsNullOrEmpty(json))
            {
                return result;
            }
            int pos = 0;
            while (pos < json.Length)
            {
                int idIdx = json.IndexOf("\"id\"", pos, StringComparison.Ordinal);
                if (idIdx < 0)
                {
                    break;
                }
                var call = new ParsedToolCall
                {
                    Id = ExtractString(json.Substring(idIdx), "id"),
                };
                int nameSearchStart = idIdx;
                int fnIdx = json.IndexOf("\"function\"", nameSearchStart, StringComparison.Ordinal);
                if (fnIdx >= 0)
                {
                    string fnSlice = json.Substring(fnIdx);
                    call.Name = ExtractString(fnSlice, "name");
                    call.Arguments = ExtractString(fnSlice, "arguments");
                }
                if (!string.IsNullOrEmpty(call.Id))
                {
                    result.Add(call);
                }
                pos = idIdx + 4;
            }
            return result;
        }

        private sealed class ParsedToolCall
        {
            public string Id = "";
            public string Name = "";
            public string Arguments = "";
        }

        private bool BeginMaxStepsSummary(TurnState turn)
        {
            if (turn.IsFinalSummaryStep)
            {
                return true;
            }
            turn.IsFinalSummaryStep = true;
            try
            {
                SafeExec(ModuleImport + "agent.inject_max_steps_message()\n");
            }
            catch (Exception e)
            {
                Debug.LogError($"[UTAgentRunner] inject_max_steps_message 失败：{e}");
                return false;
            }
            return true;
        }

        private static void LogPrepareResult(string prepareOutput)
        {
            if (string.IsNullOrWhiteSpace(prepareOutput))
            {
                return;
            }
            int pruned = ExtractInt(prepareOutput, "pruned_chars");
            bool emergency = ExtractBool(prepareOutput, "emergency_trim");
            if (pruned > 0 || emergency)
            {
                int tokens = ExtractInt(prepareOutput, "estimated_tokens");
                Debug.Log(
                    $"[UTAgentRunner] 历史裁剪 pruned_chars={pruned} emergency_trim={emergency} estimated_tokens={tokens}");
            }
        }

        private void FinishTurn(TurnState turn, string finalText, bool isError,
            string error = null)
        {
            if (error == "aborted")
            {
                PushProgress(turn, "stream_end", "");
                PushProgress(turn, "discard_stream", "");
                PushProgress(turn, "status", "已暂停");
            }
            else if (error == "max_steps")
            {
                if (UTAgentBootstrap.IsAvailable)
                {
                    SafeExec(ModuleImport + "agent.finalize_error('max_steps')\n");
                }
            }
            try { turn.StreamHandler?.Dispose(); }
            catch { }
            turn.StreamHandler = null;
            if (isError && !string.IsNullOrWhiteSpace(finalText))
            {
                PushProgress(turn, "error", finalText);
            }
            PushProgress(turn, "complete", "");
            string outcome = "success";
            if (error == "aborted")
            {
                outcome = "aborted";
                isError = false;
            }
            else if (error == "max_steps_summary")
            {
                outcome = "max_steps_summary";
                isError = false;
            }
            else if (error == "max_steps")
            {
                outcome = "max_steps";
            }
            else if (isError)
            {
                outcome = "error";
            }
            turn.Logger?.EndTurn(outcome, finalText, isError);
            mActiveTurns.Remove(turn);
            if (mActiveTurns.Count == 0)
            {
                EditorApplication.update -= Poll;
            }
            turn.Response?.Invoke(finalText, isError, outcome, turn.Events);
        }

        private static void PushProgress(TurnState turn, string type, string text)
        {
            turn.Events.Add(new ProgressEvent { Type = type, Text = text });
            turn.Logger?.LogProgress(type, text);
            turn.Progress?.Invoke(new ProgressEvent { Type = type, Text = text });
        }

        // ----- Python Exec 工具 -----

        private static string SafeExec(string script)
        {
            try
            {
                var (output, error) = UTAgentBootstrap.Exec(script);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogWarning($"[UTAgentRunner] stderr:\n{error}");
                }
                return string.IsNullOrEmpty(output) ? "" : output.Trim();
            }
            catch (Exception e)
            {
                Debug.LogError($"[UTAgentRunner] 执行失败：{e}");
                return "";
            }
        }

        private sealed class TurnState
        {
            public string UserText;
            public string ImageBase64 = "";
            public string ImageMime = "";
            public string Url;
            public string RequestBody;
            public UnityWebRequest Request;
            public UnityWebRequestAsyncOperation Operation;
            public StreamingDownloadHandler StreamHandler;
            public StringBuilder StreamTextBuf = new StringBuilder();
            public StringBuilder StreamThinkingBuf = new StringBuilder();
            public StringBuilder SseLineBuf = new StringBuilder();
            public readonly Dictionary<int, ToolCallAccumulator> ToolCallBuf =
                new Dictionary<int, ToolCallAccumulator>();
            public TurnResponseHandler Response;
            public Action<ProgressEvent> Progress;
            public List<ProgressEvent> Events = new List<ProgressEvent>();
            public bool AbortRequested;
            public bool IsFirst = true;
            public bool IsContinue;
            public bool IsFinalSummaryStep;
            public int StepCount;
            public int MaxSteps;
            public UTAgentSessionLogger Logger;

            public string ApiKey => UTAgentConfig.ResolveApiKey();
            public string BaseUrl => UTAgentConfig.ResolveBaseUrl();
            public string Model => UTAgentConfig.ResolveModelId();
        }
    }
}

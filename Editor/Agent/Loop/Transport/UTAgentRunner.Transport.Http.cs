using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace UTAgent.Editor.Agent
{
    /// <summary>
    /// Runner 传输层：UnityWebRequest 启停与流式 DownloadHandler。
    /// 编排状态机见 UTAgentRunner.cs；SSE 解析见 Transport.Sse。
    /// </summary>
    public sealed partial class UTAgentRunner
    {
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
        /// 无 tools、非流式摘要请求；完成后 apply 再 PrepareNextRequest。不计 StepCount。
        /// </summary>
        private bool StartCompactionRequest(TurnState turn, string compactionMessagesJson)
        {
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
            string body = $"{{\"model\":{JsonStr(turn.Model)},\"messages\":{compactionMessagesJson}," +
                $"\"stream\":false{deepSeekExtras}}}";
            turn.RequestBody = body;
            turn.IsCompacting = true;
            turn.StreamHandler = null;
            turn.StreamTextBuf.Clear();
            turn.StreamThinkingBuf.Clear();
            turn.SseLineBuf.Clear();
            turn.ToolCallBuf.Clear();
            turn.Logger?.LogLlmRequest(url, body);
            try
            {
                var req = new UnityWebRequest(url, "POST");
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", $"Bearer {turn.ApiKey}");
                req.timeout = 120;
                turn.Request = req;
                turn.Operation = req.SendWebRequest();
                EditorApplication.update += Poll;
                return true;
            }
            catch (Exception e)
            {
                turn.IsCompacting = false;
                turn.JustCompacted = true;
                Debug.LogError($"[UTAgentRunner] compaction HTTP 起请求失败：{e}");
                turn.Logger?.LogCompaction("fail", "start");
                return PrepareNextRequest(turn);
            }
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
    }
}

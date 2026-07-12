using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UTAgent.Editor.Bridges;

namespace UTAgent.Editor.Bridge
{
    /// <summary>
    /// Editor localhost HTTP 桥，供 Cursor CLI 调用 exec / log / ping。
    /// 请求在 <see cref="EditorApplication.update"/> 主线程队列中处理。
    /// </summary>
    [InitializeOnLoad]
    public static class UTAgentEditorHttpServer
    {
        public const string PrefKeyEnabled = "UTAgent.Bridge_Enabled";
        public const string PrefKeyPort = "UTAgent.Bridge_Port";
        public const int DefaultPort = 17861;
        public const int MaxLogLines = 500;

        private static readonly Queue<BridgeWorkItem> mQueue = new Queue<BridgeWorkItem>();
        private static HttpListener mListener;
        private static bool mRunning;
        private static bool mStopping;
        private static int mPort = DefaultPort;
        private static int mAcceptGeneration;

        /// <summary>
        /// HttpListener 是否正在监听（比 mRunning 标志可靠）。
        /// </summary>
        public static bool IsListening => mListener != null && mListener.IsListening;

        public static bool IsRunning => IsListening;

        public static int Port => mPort;

        static UTAgentEditorHttpServer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.delayCall += EnsureMatchesPrefs;
        }

        /// <summary>
        /// 按 EditorPrefs 同步启停；窗口打开 / 域重载后调用，无需每次手点应用。
        /// </summary>
        public static void EnsureMatchesPrefs()
        {
            if (IsEnabledPref())
            {
                if (!IsListening)
                {
                    Start();
                }
            }
            else if (IsListening)
            {
                Stop();
            }
        }

        /// <summary>
        /// UI 用状态文案（基于实际监听 + 已保存偏好）。
        /// </summary>
        public static string GetStatusLabel()
        {
            if (!IsEnabledPref())
            {
                return "未启用";
            }

            if (IsListening)
            {
                return $"运行中  127.0.0.1:{Port}";
            }

            return "已启用但未监听（点「应用」或查看 Console）";
        }

        public static bool HasPendingUiChanges(bool uiEnabled, int uiPort)
        {
            if (uiPort < 1024 || uiPort > 65535)
            {
                uiPort = DefaultPort;
            }

            return uiEnabled != IsEnabledPref() || uiPort != GetPortPref();
        }

        public static bool NeedsApply(bool uiEnabled, int uiPort)
        {
            if (uiPort < 1024 || uiPort > 65535)
            {
                uiPort = DefaultPort;
            }

            if (HasPendingUiChanges(uiEnabled, uiPort))
            {
                return true;
            }

            if (uiEnabled && !IsListening)
            {
                return true;
            }

            if (!uiEnabled && IsListening)
            {
                return true;
            }

            return false;
        }

        public static bool IsEnabledPref()
        {
            // 默认关闭：用户须在 Chat 设置里勾选并点「应用 Bridge」
            return EditorPrefs.GetBool(PrefKeyEnabled, false);
        }

        public static void SetEnabledPref(bool enabled)
        {
            EditorPrefs.SetBool(PrefKeyEnabled, enabled);
        }

        public static int GetPortPref()
        {
            int port = EditorPrefs.GetInt(PrefKeyPort, DefaultPort);
            if (port < 1024 || port > 65535)
            {
                return DefaultPort;
            }

            return port;
        }

        public static void SetPortPref(int port)
        {
            EditorPrefs.SetInt(PrefKeyPort, port);
        }

        /// <summary>
        /// 应用 Bridge 设置。配置未变且已在正确状态时跳过，避免重复 Stop/Start。
        /// </summary>
        /// <returns>是否实际执行了启停</returns>
        public static bool ApplySettings(bool enabled, int port)
        {
            if (port < 1024 || port > 65535)
            {
                port = DefaultPort;
            }

            bool prefsUnchanged = enabled == IsEnabledPref() && port == GetPortPref();
            if (prefsUnchanged)
            {
                if (!enabled && !IsListening)
                {
                    return false;
                }

                if (enabled && IsListening && Port == port)
                {
                    return false;
                }
            }

            SetEnabledPref(enabled);
            SetPortPref(port);
            Stop();
            if (enabled)
            {
                Start();
            }

            return true;
        }

        public static void Start()
        {
            if (mStopping)
            {
                return;
            }

            if (IsListening)
            {
                return;
            }

            // 清掉可能残留的 dead listener
            if (mListener != null)
            {
                Stop();
            }

            mPort = GetPortPref();
            try
            {
                mListener = new HttpListener();
                mListener.Prefixes.Add($"http://127.0.0.1:{mPort}/");
                mListener.Start();
                mRunning = true;
                int generation = ++mAcceptGeneration;
                _ = AcceptLoopAsync(generation);
                Debug.Log($"[UTAgent Bridge] 开始监听 http://127.0.0.1:{mPort}/");
            }
            catch (Exception e)
            {
                mRunning = false;
                mListener = null;
                Debug.LogWarning($"[UTAgent Bridge] 启动失败（端口 {mPort}）：{e.Message}");
            }
        }

        public static void Stop()
        {
            mAcceptGeneration++;
            mStopping = true;
            mRunning = false;
            if (mListener != null)
            {
                try
                {
                    mListener.Stop();
                    mListener.Close();
                }
                catch (Exception)
                {
                    // 域重载或重复 Stop 时忽略
                }

                mListener = null;
            }

            lock (mQueue)
            {
                while (mQueue.Count > 0)
                {
                    var item = mQueue.Dequeue();
                    item.CompleteUnavailable("Bridge 已停止");
                }
            }

            mStopping = false;
        }

        private static void OnBeforeAssemblyReload()
        {
            Stop();
        }

        private static async Task AcceptLoopAsync(int generation)
        {
            while (mRunning && generation == mAcceptGeneration && mListener != null && mListener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await mListener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    if (mRunning)
                    {
                        Debug.LogWarning($"[UTAgent Bridge] 接受请求失败：{e.Message}");
                    }

                    break;
                }

                if (!mRunning || generation != mAcceptGeneration || mListener == null || !mListener.IsListening)
                {
                    break;
                }

                string path = context.Request.Url.AbsolutePath.TrimEnd('/');
                if (string.IsNullOrEmpty(path))
                {
                    path = "/";
                }

                string method = context.Request.HttpMethod.ToUpperInvariant();
                string body = string.Empty;
                if (method == "POST")
                {
                    using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                    {
                        body = reader.ReadToEnd();
                    }
                }

                var work = new BridgeWorkItem(context, method, path, body, context.Request.QueryString);
                lock (mQueue)
                {
                    mQueue.Enqueue(work);
                }
            }

            if (mRunning && generation == mAcceptGeneration)
            {
                mRunning = false;
                if (!mStopping)
                {
                    Debug.LogWarning("[UTAgent Bridge] 监听循环已退出");
                }
            }
        }

        private static void OnEditorUpdate()
        {
            while (true)
            {
                BridgeWorkItem item;
                lock (mQueue)
                {
                    if (mQueue.Count == 0)
                    {
                        break;
                    }

                    item = mQueue.Dequeue();
                }

                item.Process();
            }
        }

        private sealed class BridgeWorkItem
        {
            private readonly HttpListenerContext mContext;
            private readonly string mMethod;
            private readonly string mPath;
            private readonly string mBody;
            private readonly System.Collections.Specialized.NameValueCollection mQuery;

            public BridgeWorkItem(
                HttpListenerContext context,
                string method,
                string path,
                string body,
                System.Collections.Specialized.NameValueCollection query)
            {
                mContext = context;
                mMethod = method;
                mPath = path;
                mBody = body ?? string.Empty;
                mQuery = query;
            }

            public void Process()
            {
                try
                {
                    Route();
                }
                catch (Exception e)
                {
                    WriteJson(500, $"{{\"ok\":false,\"error\":{BridgeJson.EscapeJson(e.Message)}}}");
                }
            }

            public void CompleteUnavailable(string message)
            {
                WriteJson(503, $"{{\"ok\":false,\"error\":{BridgeJson.EscapeJson(message)}}}");
            }

            private void Route()
            {
                if (mPath == "/ping" && mMethod == "GET")
                {
                    HandlePing();
                    return;
                }

                if (mPath == "/initialize" && mMethod == "POST")
                {
                    HandleInitialize();
                    return;
                }

                if (mPath == "/exec" && mMethod == "POST")
                {
                    HandleExec();
                    return;
                }

                if (mPath == "/log/tail" && mMethod == "GET")
                {
                    HandleLogTail();
                    return;
                }

                if (mPath == "/log/errors" && mMethod == "GET")
                {
                    HandleLogErrors();
                    return;
                }

                if (mPath == "/scene/find" && mMethod == "GET")
                {
                    HandleSceneFind();
                    return;
                }

                if (mPath == "/chat" && mMethod == "POST")
                {
                    HandleChatPost();
                    return;
                }

                if ((mPath == "/chat/wait" || mPath == "/chat/status") && mMethod == "GET")
                {
                    HandleChatStatus();
                    return;
                }

                WriteJson(404, "{\"ok\":false,\"error\":\"unknown route\"}");
            }

            private void HandleChatPost()
            {
                string message = BridgeJson.Args.GetString(mBody, "message");
                if (!UTAgentBridgeChatService.Instance.TryStartChat(
                        message,
                        out UTAgentBridgeChatService.BridgeChatTurn turn,
                        out string error,
                        out int httpStatus))
                {
                    WriteJson(httpStatus,
                        "{\"ok\":false" +
                        $",\"error\":{BridgeJson.EscapeJson(error ?? "start failed")}" +
                        (httpStatus == 409 && UTAgentBridgeChatService.Instance.HasRunningTurn
                            ? ",\"hint\":\"等待当前 turn 结束或使用 chat status 查询\""
                            : string.Empty) +
                        "}");
                    return;
                }

                WriteJson(200,
                    "{\"ok\":true" +
                    $",\"turn_id\":{BridgeJson.EscapeJson(turn.TurnId)}" +
                    ",\"status\":\"running\"}");
            }

            private void HandleChatStatus()
            {
                string turnId = mQuery["turn_id"] ?? string.Empty;
                if (!UTAgentBridgeChatService.Instance.TryGetTurn(turnId, out UTAgentBridgeChatService.BridgeChatTurn turn))
                {
                    WriteJson(404, "{\"ok\":false,\"error\":\"turn not found\"}");
                    return;
                }

                WriteJson(200, UTAgentBridgeChatService.Instance.BuildTurnJson(turn));
            }

            private void HandlePing()
            {
                string json =
                    "{\"ok\":true" +
                    ",\"editor_alive\":true" +
                    $",\"engine_available\":{BridgeJson.ToLower(UTAgentBootstrap.IsAvailable)}" +
                    $",\"invalidated\":{BridgeJson.ToLower(UTAgentBootstrap.IsInvalidated)}" +
                    $",\"port\":{mPort}" +
                    $",\"log_directory\":{BridgeJson.EscapeJson(UTAgentSessionLogger.ResolveLogDirectory())}" +
                    $",\"unity_version\":{BridgeJson.EscapeJson(Application.unityVersion)}" +
                    ",\"bridge_running\":" + BridgeJson.ToLower(IsListening) +
                    "}";
                if (!UTAgentBootstrap.IsAvailable && UTAgentBootstrap.IsInvalidated)
                {
                    json = json.TrimEnd('}') +
                           ",\"hint\":\"引擎因域重载失效，请 POST /initialize 或运行 utagent init\"}";
                }

                WriteJson(200, json);
            }

            private void HandleInitialize()
            {
                try
                {
                    UTAgentBootstrap.Initialize();
                    WriteJson(200,
                        $"{{\"ok\":true,\"engine_available\":{BridgeJson.ToLower(UTAgentBootstrap.IsAvailable)}}}");
                }
                catch (Exception e)
                {
                    WriteJson(500,
                        $"{{\"ok\":false,\"engine_available\":false,\"error\":{BridgeJson.EscapeJson(e.Message)}}}");
                }
            }

            private void HandleExec()
            {
                if (!UTAgentBootstrap.IsAvailable)
                {
                    string hint = UTAgentBootstrap.IsInvalidated
                        ? "引擎因域重载失效，请 POST /initialize 或运行 utagent init"
                        : "引擎未初始化，请 POST /initialize 或运行 utagent init";
                    WriteJson(503,
                        "{\"ok\":false" +
                        ",\"engine_available\":false" +
                        $",\"invalidated\":{BridgeJson.ToLower(UTAgentBootstrap.IsInvalidated)}" +
                        $",\"hint\":{BridgeJson.EscapeJson(hint)}" +
                        ",\"output\":\"\",\"error\":\"\"}");
                    return;
                }

                string code = BridgeJson.Args.GetString(mBody, "code");
                if (string.IsNullOrWhiteSpace(code))
                {
                    WriteJson(400, "{\"ok\":false,\"error\":\"missing code\"}");
                    return;
                }

                try
                {
                    var (output, error) = UTAgentBootstrap.Exec(code);
                    bool hasError = !string.IsNullOrWhiteSpace(error);
                    WriteJson(200,
                        "{\"ok\":" + BridgeJson.ToLower(!hasError) +
                        $",\"output\":{BridgeJson.EscapeJson(output ?? string.Empty)}" +
                        $",\"error\":{BridgeJson.EscapeJson(error ?? string.Empty)}" +
                        ",\"engine_available\":true}");
                }
                catch (Exception e)
                {
                    WriteJson(200,
                        "{\"ok\":false" +
                        ",\"output\":\"\"" +
                        $",\"error\":{BridgeJson.EscapeJson(e.ToString())}" +
                        ",\"engine_available\":true}");
                }
            }

            private void HandleLogTail()
            {
                int n = ParseLineCount(mQuery["n"], 80);
                var (path, lines) = ReadLatestLogTail(n);
                var sb = new StringBuilder();
                sb.Append("{\"ok\":true");
                sb.Append(path == null ? ",\"path\":null" : $",\"path\":{BridgeJson.EscapeJson(path)}");
                sb.Append(",\"lines\":[");
                for (int i = 0; i < lines.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append(BridgeJson.EscapeJson(lines[i]));
                }

                sb.Append("]}");
                WriteJson(200, sb.ToString());
            }

            private void HandleLogErrors()
            {
                int n = ParseLineCount(mQuery["n"], 200);
                var (path, lines) = ReadLatestLogTail(n);
                var matches = new List<string>();
                foreach (string line in lines)
                {
                    if (IsErrorLine(line))
                    {
                        matches.Add(line);
                    }
                }

                var sb = new StringBuilder();
                sb.Append("{\"ok\":true");
                sb.Append(path == null ? ",\"path\":null" : $",\"path\":{BridgeJson.EscapeJson(path)}");
                sb.Append(",\"matches\":[");
                for (int i = 0; i < matches.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append(BridgeJson.EscapeJson(matches[i]));
                }

                sb.Append("]}");
                WriteJson(200, sb.ToString());
            }

            private void HandleSceneFind()
            {
                string name = mQuery["name"] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    WriteJson(400, "{\"ok\":false,\"error\":\"missing name\"}");
                    return;
                }

                string bridgeJson = UTAgentPythonBridge.Instance.FindObjects(name);
                int count = 0;
                var names = new List<string>();
                if (bridgeJson.Contains("\"success\":true"))
                {
                    int countIdx = bridgeJson.IndexOf("\"count\":", StringComparison.Ordinal);
                    if (countIdx >= 0)
                    {
                        int start = countIdx + 8;
                        int end = start;
                        while (end < bridgeJson.Length && char.IsDigit(bridgeJson[end]))
                        {
                            end++;
                        }

                        if (end > start && int.TryParse(bridgeJson.Substring(start, end - start), out int parsed))
                        {
                            count = parsed;
                        }
                    }

                    int searchFrom = 0;
                    while (true)
                    {
                        int nameKey = bridgeJson.IndexOf("\"name\":", searchFrom, StringComparison.Ordinal);
                        if (nameKey < 0)
                        {
                            break;
                        }

                        int q1 = bridgeJson.IndexOf('"', nameKey + 7);
                        if (q1 < 0)
                        {
                            break;
                        }

                        int q2 = bridgeJson.IndexOf('"', q1 + 1);
                        if (q2 < 0)
                        {
                            break;
                        }

                        names.Add(bridgeJson.Substring(q1 + 1, q2 - q1 - 1));
                        searchFrom = q2 + 1;
                    }
                }

                var sb = new StringBuilder();
                sb.Append("{\"ok\":true");
                sb.Append($",\"count\":{count}");
                sb.Append(",\"names\":[");
                for (int i = 0; i < names.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append(BridgeJson.EscapeJson(names[i]));
                }

                sb.Append("]}");
                WriteJson(200, sb.ToString());
            }

            private void WriteJson(int statusCode, string json)
            {
                try
                {
                    var response = mContext.Response;
                    response.StatusCode = statusCode;
                    response.ContentType = "application/json; charset=utf-8";
                    byte[] buffer = Encoding.UTF8.GetBytes(json);
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                catch (Exception)
                {
                    // 客户端已断开
                }
            }
        }

        private static int ParseLineCount(string raw, int defaultValue)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }

            if (!int.TryParse(raw, out int n) || n <= 0)
            {
                return defaultValue;
            }

            return Math.Min(n, MaxLogLines);
        }

        private static bool IsErrorLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            return line.IndexOf("Traceback", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("HTTP 400", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("[ERROR]", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("LLM error", StringComparison.OrdinalIgnoreCase) >= 0
                || line.StartsWith("--- step ", StringComparison.Ordinal);
        }

        private static (string Path, List<string> Lines) ReadLatestLogTail(int lineCount)
        {
            string dir = UTAgentSessionLogger.ResolveLogDirectory();
            if (!Directory.Exists(dir))
            {
                return (null, new List<string>());
            }

            string[] files = Directory.GetFiles(dir, "agent_*.log");
            if (files.Length == 0)
            {
                return (null, new List<string>());
            }

            string latest = files[0];
            DateTime latestTime = File.GetLastWriteTimeUtc(latest);
            for (int i = 1; i < files.Length; i++)
            {
                DateTime t = File.GetLastWriteTimeUtc(files[i]);
                if (t > latestTime)
                {
                    latestTime = t;
                    latest = files[i];
                }
            }

            var allLines = new List<string>();
            foreach (string line in File.ReadLines(latest, Encoding.UTF8))
            {
                allLines.Add(line);
            }

            int start = Math.Max(0, allLines.Count - lineCount);
            return (latest, allLines.GetRange(start, allLines.Count - start));
        }
    }
}

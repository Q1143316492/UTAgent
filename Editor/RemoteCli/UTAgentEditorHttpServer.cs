using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UTAgent.Editor.Agent;
using UTAgent.Editor.PythonInterop;
using UTAgent.Editor.Config;
using UTAgent.Editor.Core;

namespace UTAgent.Editor.RemoteCli
{
    /// <summary>
    /// Editor localhost HTTP 桥，供 Cursor CLI 调用 exec / log / ping。
    /// 请求在 <see cref="EditorApplication.update"/> 主线程队列中处理。
    /// </summary>
    [InitializeOnLoad]
    public static partial class UTAgentEditorHttpServer
    {
        public const int DefaultPort = UTAgentConfig.DefaultBridgePort;
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
        }

        /// <summary>
        /// 按 JSON 配置同步启停。仅在打开 Chat（<see cref="UTAgentConfig.PrepareForChat"/>）或 Settings 手动保存 CLI 时调用，不在 Editor 启动时自动监听。
        /// </summary>
        public static void EnsureMatchesConfig()
        {
            UTAgentConfig.EnsureLoaded();
            BridgeDto bridge = UTAgentConfig.Current.bridge;
            if (bridge.enabled)
            {
                if (!IsListening || Port != bridge.port)
                {
                    ApplyBridgeConfig();
                }
            }
            else if (IsListening)
            {
                Stop();
            }
        }

        /// <summary>
        /// 兼容旧调用。
        /// </summary>
        public static void EnsureMatchesPrefs()
        {
            EnsureMatchesConfig();
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

            return "已启用，打开 Chat 或 Settings 保存后会启动";
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
            UTAgentConfig.EnsureLoaded();
            return UTAgentConfig.Current.bridge.enabled;
        }

        public static int GetPortPref()
        {
            UTAgentConfig.EnsureLoaded();
            return UTAgentConfig.Current.bridge.port;
        }

        /// <summary>
        /// 将当前 JSON 中的 bridge 配置写入并启停服务。
        /// </summary>
        public static bool ApplyBridgeConfig()
        {
            UTAgentConfig.EnsureLoaded();
            BridgeDto bridge = UTAgentConfig.Current.bridge;
            return ApplySettings(bridge.enabled, bridge.port);
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

            bool sameConfig = UTAgentConfig.Current.bridge.enabled == enabled
                && UTAgentConfig.Current.bridge.port == port;
            if (sameConfig)
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

            UTAgentConfig.Current.bridge.enabled = enabled;
            UTAgentConfig.Current.bridge.port = port;
            UTAgentConfig.SaveLocal();

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
                Debug.Log($"[UTAgent RemoteCli] 开始监听 http://127.0.0.1:{mPort}/");
            }
            catch (Exception e)
            {
                mRunning = false;
                mListener = null;
                Debug.LogWarning($"[UTAgent RemoteCli] 启动失败（端口 {mPort}）：{e.Message}");
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
                    item.CompleteUnavailable("Remote CLI 已停止");
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
                        Debug.LogWarning($"[UTAgent RemoteCli] 接受请求失败：{e.Message}");
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
                    Debug.LogWarning("[UTAgent RemoteCli] 监听循环已退出");
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

        private sealed partial class BridgeWorkItem
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

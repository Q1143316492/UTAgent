using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

using UTAgent.Editor.Bridge;

namespace UTAgent.Editor
{
    /// <summary>
    /// Agent 消息分块。UI 按 Type 分块渲染，不压平为字符串。
    /// </summary>
    public struct MessageBlock
    {
        public string Type;
        public string Text;
        public bool IsStreaming;
    }

    public partial class UTAgentChatWindow : EditorWindow
    {
        private const string PrefKeyApiKey = "UTAgent.Agent_ApiKey";
        private const string PrefKeyBaseURL = "UTAgent.Agent_BaseURL";
        private const string PrefKeyModel = "UTAgent.Agent_Model";
        private const string PrefKeyMaxSteps = "UTAgent.Agent_MaxSteps";

        private readonly UTAgentRunner mRunner = new UTAgentRunner();
        private readonly UTAgentChatScroll mMessageScroll = new UTAgentChatScroll();
        private readonly List<ChatMessage> mMessages = new List<ChatMessage>();
        private string mInput = "";
        private bool mWaiting;
        private int mProgressIndex = -1;

        private bool mShowSettings;
        private string mApiKey = "";
        private string mBaseURL = "";
        private string mModel = "gpt-4o-mini";
        private int mMaxSteps = 25;
        private string mLogDirectory = "";
        private bool mBridgeEnabled;
        private int mBridgePort = UTAgentEditorHttpServer.DefaultPort;

        private string mAttachedImagePath;
        private Texture2D mAttachedPreview;

        private bool mStylesInit;
        private GUIStyle mUserBubbleStyle;
        private GUIStyle mAgentBubbleStyle;
        private GUIStyle mUserLabelStyle;
        private GUIStyle mAgentLabelStyle;
        private GUIStyle mTimestampStyle;
        private GUIStyle mCodeStyle;
        private GUIStyle mThinkingStyle;
        private GUIStyle mObservationStyle;
        private GUIStyle mTextStyle;
        private GUIStyle mStatusStyle;
        private GUIStyle mErrorStyle;
        private Texture2D mUserBubbleTex;
        private Texture2D mAgentBubbleTex;
        private Texture2D mCopyBtnNormalTex;
        private Texture2D mCopyBtnHoverTex;
        private bool mWasProSkin;

        private const string InputControlName = "UTAgentChatInput";

        private int mCopiedMessageIndex = -1;
        private double mCopiedMessageTime;

        [MenuItem("Window/UT Agent/Agent Chat")]
        private static void Open()
        {
            GetWindow<UTAgentChatWindow>("UT Agent");
        }

        private void OnEnable()
        {
            mStylesInit = false;
            mMessageScroll.Reset();
            EndWaitingTurn();
            LoadSettings();
            UTAgentEditorHttpServer.EnsureMatchesPrefs();
            TrySilentConfigureRunner();
            if (UTAgentBootstrap.IsAvailable)
            {
                RefreshPythonModulesFromDisk();
            }
        }

        private void OnDisable()
        {
            EditorApplication.delayCall -= OnAbortSafetyReset;
        }

        private void OnDestroy()
        {
            EditorApplication.delayCall -= OnAbortSafetyReset;
            mRunner.Abort();
            ClearPreview();
            DestroyTex(ref mUserBubbleTex);
            DestroyTex(ref mAgentBubbleTex);
            DestroyTex(ref mCopyBtnNormalTex);
            DestroyTex(ref mCopyBtnHoverTex);
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(28));
            GUILayout.Label("UT Agent", EditorStyles.boldLabel);
            string status;
            if (mWaiting) status = "● Thinking...";
            else if (UTAgentBootstrap.IsAvailable && mRunner.IsConfigured())
            {
                status = "● Ready";
            }
            else if (UTAgentBootstrap.IsAvailable)
            {
                status = "○ 未配置 API";
            }
            else status = "✕ Offline";
            GUILayout.FlexibleSpace();
            GUILayout.Label(status, EditorStyles.miniLabel);
            if (!UTAgentBootstrap.IsAvailable)
            {
                if (GUILayout.Button("初始化引擎", EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    try { UTAgentBootstrap.Initialize(); } catch (Exception e) { AddMessage($"[初始化失败] {e}", false); }
                    Repaint();
                }
            }
            else
            {
                if (GUILayout.Button("刷新 Python", EditorStyles.toolbarButton, GUILayout.Width(88)))
                {
                    RefreshPythonModulesFromDisk(force: true);
                }
            }
            if (GUILayout.Button("⚙", EditorStyles.toolbarButton, GUILayout.Width(28)))
                mShowSettings = !mShowSettings;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        private void OnGUI()
        {
            if (!mStylesInit || mWasProSkin != EditorGUIUtility.isProSkin)
            {
                InitStyles();
                mStylesInit = true;
                mWasProSkin = EditorGUIUtility.isProSkin;
            }
            DrawHeader();
            if (mShowSettings)
            {
                DrawSettings();
            }

            DrawMessageList();
            EditorGUILayout.Space(4);
            DrawInputArea();
        }

        private void DrawMessageList()
        {
            mMessageScroll.Draw(DrawMessageListContent, mMessages.Count > 0, GetMessageContentVersion());
        }

        private void DrawWelcome()
        {
            GUILayout.Space(40);
            GUILayout.Label("🤖", new GUIStyle(EditorStyles.label) { fontSize = 36, alignment = TextAnchor.MiddleCenter });
            GUILayout.Label("UT Agent", new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, alignment = TextAnchor.MiddleCenter });
            GUILayout.Label(mRunner.IsConfigured()
                    ? "输入任务，LLM 通过 execPython 操作 Unity。"
                    : "展开 ⚙ → LLM 配置 → 保存 API Key。",
                new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(40);
        }

        private void DrawInputArea()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (!string.IsNullOrEmpty(mAttachedImagePath))
            {
                EditorGUILayout.BeginHorizontal();
                if (mAttachedPreview != null) GUILayout.Label(mAttachedPreview, GUILayout.Width(32), GUILayout.Height(32));
                GUILayout.Label(Path.GetFileName(mAttachedImagePath), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("×", GUILayout.Width(22))) { ClearAttached(); Repaint(); }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("📎", GUILayout.Width(32))) OpenImageBrowser();
            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && !Event.current.shift;
            GUI.SetNextControlName(InputControlName);
            mInput = EditorGUILayout.TextArea(mInput, GUILayout.MinHeight(36), GUILayout.ExpandHeight(false));
            if (mWaiting)
            {
                if (GUILayout.Button("Stop", GUILayout.Width(56)))
                {
                    mRunner.Abort();
                    EditorApplication.delayCall -= OnAbortSafetyReset;
                    EditorApplication.delayCall += OnAbortSafetyReset;
                }
            }
            else
            {
                if (CanContinueFromLastMessage() && GUILayout.Button("续跑", GUILayout.Width(56)))
                {
                    RunContinue();
                }
                else if (GUILayout.Button("Send", GUILayout.Width(56)))
                {
                    Send(mInput);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", GUILayout.Width(56)))
            {
                if (EditorUtility.DisplayDialog("Clear", "清空所有消息与历史？", "Clear", "Cancel"))
                {
                    mRunner.Abort();
                    mMessages.Clear();
                    mRunner.ClearHistory();
                    EndWaitingTurn();
                    mMessageScroll.Reset();
                    Repaint();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            if (enter && !mWaiting && !string.IsNullOrWhiteSpace(mInput))
            {
                Send(mInput);
                GUIUtility.ExitGUI();
            }
        }

        private void Send(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            if (mWaiting)
            {
                return;
            }
            if (!UTAgentBootstrap.IsAvailable)
            {
                AddMessage("引擎因域重载失效，请重新点击初始化", false);
                return;
            }
            if (!mRunner.IsConfigured())
            {
                AddMessage("请先在设置里填 API Key 并 Apply。", false);
                return;
            }
            text = text.Trim();
            if (TryContinueFromInput(text))
            {
                return;
            }
            string img = mAttachedImagePath;
            mMessageScroll.OnSend();
            AddMessage(text, true);
            mInput = "";
            RunTurn(text, img);
            ClearAttached();
            Repaint();
        }

        private bool CanContinueFromLastMessage()
        {
            for (int i = mMessages.Count - 1; i >= 0; i--)
            {
                if (!mMessages[i].IsUser && mMessages[i].ShowContinue)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryContinueFromInput(string text)
        {
            if (!CanContinueFromLastMessage())
            {
                return false;
            }
            if (text != "继续" && !text.Equals("continue", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            mInput = "";
            mMessageScroll.OnSend();
            RunContinue();
            Repaint();
            return true;
        }

        private void ContinueFromMessage(ChatMessage msg)
        {
            if (mWaiting)
                return;
            RunContinue();
        }

        private void RunTurn(string text, string imagePath)
        {
            if (mWaiting)
                return;

            mWaiting = true;
            var progressMsg = new ChatMessage
            {
                IsUser = false,
                Timestamp = DateTime.Now.ToString("HH:mm"),
                Blocks = new List<MessageBlock>(),
            };
            mMessages.Add(progressMsg);
            mProgressIndex = mMessages.Count - 1;
            Repaint();
            mRunner.SendMessageAsync(text, imagePath, CompleteTurn, OnProgressUpdate);
        }

        private void RunContinue()
        {
            if (mWaiting)
            {
                return;
            }

            mWaiting = true;
            var progressMsg = new ChatMessage
            {
                IsUser = false,
                Timestamp = DateTime.Now.ToString("HH:mm"),
                Blocks = new List<MessageBlock>
                {
                    new MessageBlock
                    {
                        Type = "status",
                        Text = "从当前对话续跑（保留上文上下文，不重复原任务）",
                    },
                },
            };
            mMessages.Add(progressMsg);
            mProgressIndex = mMessages.Count - 1;
            Repaint();
            mRunner.ContinueAsync(CompleteTurn, OnProgressUpdate);
        }

        private void CompleteTurn(string finalText, bool isError, string outcome, List<ProgressEvent> events)
        {
            try
            {
                FinalizeProgress(finalText, isError, outcome);
            }
            finally
            {
                EndWaitingTurn();
                Repaint();
            }
        }

        private void EndWaitingTurn()
        {
            mWaiting = false;
            mProgressIndex = -1;
        }

        private void OnAbortSafetyReset()
        {
            EditorApplication.delayCall -= OnAbortSafetyReset;
            if (!mWaiting)
            {
                return;
            }

            EndWaitingTurn();
            Repaint();
        }

        private void OnProgressUpdate(ProgressEvent evt)
        {
            if (mProgressIndex < 0 || mProgressIndex >= mMessages.Count) return;

            var msg = mMessages[mProgressIndex];
            ApplyProgressEvent(msg.Blocks, evt);
            mMessages[mProgressIndex] = msg;
            Repaint();
        }

        private static void ApplyProgressEvent(List<MessageBlock> blocks, ProgressEvent evt)
        {
            string type = NormalizeProgressType(evt.Type);
            if (type == "discard_stream")
            {
                blocks.RemoveAll(b => b.Type == "stream");
                return;
            }
            if (type == "stream_end")
            {
                for (int i = blocks.Count - 1; i >= 0; i--)
                {
                    if (blocks[i].Type != "stream")
                        continue;
                    var block = blocks[i];
                    block.IsStreaming = false;
                    blocks[i] = block;
                    break;
                }
                return;
            }
            if (type == "complete")
                return;
            if (type == "tool_call")
                blocks.RemoveAll(b => b.Type == "stream");
            if ((type == "stream" || type == "thinking")
                && blocks.Count > 0
                && blocks[blocks.Count - 1].Type == type)
            {
                var last = blocks[blocks.Count - 1];
                last.Text += evt.Text;
                if (type == "stream")
                    last.IsStreaming = true;
                blocks[blocks.Count - 1] = last;
                return;
            }
            blocks.Add(new MessageBlock
            {
                Type = type,
                Text = evt.Text ?? "",
                IsStreaming = type == "stream",
            });
        }

        private static string NormalizeProgressType(string type)
        {
            if (string.IsNullOrEmpty(type))
                return "status";
            if (type == "text")
                return "status";
            return type;
        }

        private void FinalizeProgress(string finalText, bool isError, string outcome)
        {
            if (mProgressIndex < 0 || mProgressIndex >= mMessages.Count)
                return;

            var msg = mMessages[mProgressIndex];
            for (int i = 0; i < msg.Blocks.Count; i++)
            {
                if (!msg.Blocks[i].IsStreaming)
                    continue;
                var block = msg.Blocks[i];
                block.IsStreaming = false;
                msg.Blocks[i] = block;
            }

            msg.Blocks.RemoveAll(b => b.Type == "stream" && string.IsNullOrWhiteSpace(b.Text));

            bool hasAnswer = msg.Blocks.Exists(b => b.Type == "answer");
            bool hasToolCall = msg.Blocks.Exists(b => b.Type == "tool_call");
            int streamIdx = msg.Blocks.FindIndex(b => b.Type == "stream");
            if (!hasAnswer && streamIdx >= 0 && !hasToolCall)
            {
                var stream = msg.Blocks[streamIdx];
                msg.Blocks.RemoveAt(streamIdx);
                if (!string.IsNullOrWhiteSpace(stream.Text))
                {
                    msg.Blocks.Add(new MessageBlock { Type = "answer", Text = stream.Text.Trim() });
                    hasAnswer = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(finalText) && !hasAnswer
                && !msg.Blocks.Exists(b => b.Type == "error")
                && isError)
            {
                msg.Blocks.Add(new MessageBlock { Type = "error", Text = finalText.Trim() });
            }
            else if (!string.IsNullOrWhiteSpace(finalText) && !hasAnswer && !isError
                && outcome != "aborted")
            {
                msg.Blocks.Add(new MessageBlock { Type = "answer", Text = finalText.Trim() });
            }

            if (msg.Blocks.Count == 0)
            {
                msg.Blocks.Add(new MessageBlock
                {
                    Type = isError ? "error" : "answer",
                    Text = isError ? (finalText ?? "失败") : (finalText ?? "(无回复，请看 Console)"),
                });
            }

            msg.ShowContinue = UTAgentRunner.CanContinueFromOutcome(outcome);
            mMessages[mProgressIndex] = msg;
            mProgressIndex = -1;
        }

        private static string GetDisplayText(ChatMessage msg)
        {
            if (msg.IsUser)
                return msg.Text ?? "";

            var sb = new StringBuilder();
            foreach (var block in msg.Blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Text))
                    continue;
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine();
                }
                switch (block.Type)
                {
                    case "thinking":
                        sb.Append("[Thinking]\n");
                        sb.Append(block.Text);
                        break;
                    case "tool_call":
                        if (block.Text != null && block.Text.StartsWith("loadSkill", StringComparison.Ordinal))
                        {
                            sb.Append("[CALL] loadSkill\n");
                        }
                        else
                        {
                            sb.Append("[CALL] execPython\n");
                        }
                        sb.Append(block.Text);
                        break;
                    case "observation":
                        sb.Append("[Observation]\n");
                        sb.Append(block.Text);
                        break;
                    default:
                        sb.Append(block.Text);
                        break;
                }
            }
            if (sb.Length > 0)
                return sb.ToString();
            return msg.Text ?? "";
        }

        private void AddMessage(string text, bool isUser)
        {
            var msg = new ChatMessage
            {
                Text = isUser ? text : "",
                IsUser = isUser,
                Timestamp = DateTime.Now.ToString("HH:mm"),
                Blocks = new List<MessageBlock>(),
            };
            if (!isUser && !string.IsNullOrEmpty(text))
            {
                msg.Blocks.Add(new MessageBlock { Type = "answer", Text = text });
            }
            mMessages.Add(msg);
            Repaint();
        }

        private void OpenImageBrowser()
        {
            string path = EditorUtility.OpenFilePanel("Select Image", "", "png,jpg,jpeg,bmp,gif,webp");
            if (!string.IsNullOrEmpty(path)) { mAttachedImagePath = path; LoadPreview(path); Repaint(); }
        }

        private void LoadPreview(string path)
        {
            ClearPreview();
            try
            {
                byte[] b = File.ReadAllBytes(path);
                var t = new Texture2D(2, 2);
                if (t.LoadImage(b)) mAttachedPreview = t;
                else UnityEngine.Object.DestroyImmediate(t);
            }
            catch (Exception e) { Debug.LogWarning($"[UTAgent] 加载图片失败：{e.Message}"); }
        }

        private void ClearAttached() { mAttachedImagePath = null; ClearPreview(); }

        private void ClearPreview()
        {
            if (mAttachedPreview != null) { UnityEngine.Object.DestroyImmediate(mAttachedPreview); mAttachedPreview = null; }
        }

        private void LoadSettings()
        {
            mApiKey = MigratePrefString(PrefKeyApiKey, "PythonBridge.Agent_ApiKey");
            mBaseURL = MigratePrefString(PrefKeyBaseURL, "PythonBridge.Agent_BaseURL");
            mModel = MigratePrefString(PrefKeyModel, "PythonBridge.Agent_Model", "gpt-4o-mini");
            mMaxSteps = MigratePrefInt(PrefKeyMaxSteps, "PythonBridge.Agent_MaxSteps", 25);
            mLogDirectory = MigratePrefString(
                UTAgentSessionLogger.PrefKeyLogDirectory,
                "PythonBridge.Agent_LogDirectory");
            mBridgeEnabled = EditorPrefs.GetBool(UTAgentEditorHttpServer.PrefKeyEnabled, false);
            mBridgePort = UTAgentEditorHttpServer.GetPortPref();
        }

        private static string MigratePrefString(string newKey, string oldKey, string defaultValue = "")
        {
            var value = EditorPrefs.GetString(newKey, "");
            if (string.IsNullOrEmpty(value))
            {
                value = EditorPrefs.GetString(oldKey, defaultValue);
                if (!string.IsNullOrEmpty(value))
                {
                    EditorPrefs.SetString(newKey, value);
                }
            }
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        private static int MigratePrefInt(string newKey, string oldKey, int defaultValue)
        {
            if (EditorPrefs.HasKey(newKey))
            {
                return EditorPrefs.GetInt(newKey, defaultValue);
            }
            if (EditorPrefs.HasKey(oldKey))
            {
                var value = EditorPrefs.GetInt(oldKey, defaultValue);
                EditorPrefs.SetInt(newKey, value);
                return value;
            }
            return defaultValue;
        }

        private void SaveSettings()
        {
            EditorPrefs.SetString(PrefKeyApiKey, mApiKey);
            EditorPrefs.SetString(PrefKeyBaseURL, mBaseURL);
            EditorPrefs.SetString(PrefKeyModel, mModel);
            EditorPrefs.SetInt(PrefKeyMaxSteps, mMaxSteps);
            EditorPrefs.SetString(UTAgentSessionLogger.PrefKeyLogDirectory, mLogDirectory ?? "");
        }

        private void ApplySettings()
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                try { UTAgentBootstrap.Initialize(); } catch (Exception e) { AddMessage($"[初始化失败] {e}", false); return; }
            }
            UTAgentSessionLogger.EnsureLogDirectory(
                string.IsNullOrWhiteSpace(mLogDirectory) ? null : mLogDirectory);
            string result = mRunner.Configure(mApiKey, mBaseURL, mModel, mMaxSteps);
            AddMessage(result, false);
        }

        private void RefreshPythonModulesFromDisk(bool force = false)
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                return;
            }

            bool purged = UTAgentBootstrap.RefreshPythonModuleCache(force);
            if (!purged)
            {
                return;
            }

            if (mRunner.IsConfigured())
            {
                string result = mRunner.Configure(mApiKey, mBaseURL, mModel, mMaxSteps);
                AddMessage($"Python 模块已重新加载；Agent 已重新配置。\n{result}", false);
            }
            else
            {
                AddMessage("Python 模块已重新加载。", false);
            }
        }

        private sealed class ChatMessage
        {
            public string Text = "";
            public List<MessageBlock> Blocks = new List<MessageBlock>();
            public bool IsUser;
            public bool ShowContinue;
            public string Timestamp = "";
        }
    }
}

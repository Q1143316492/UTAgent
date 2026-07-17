using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UTAgent.Editor.Config;
using UTAgent.Editor.Core;

using UTAgent.Editor.RemoteCli;

namespace UTAgent.Editor.Agent
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
        private readonly UTAgentRunner mRunner = new UTAgentRunner();
        private readonly UTAgentChatScroll mMessageScroll = new UTAgentChatScroll();
        private readonly List<ChatMessage> mMessages = new List<ChatMessage>();
        private string mInput = "";
        private Vector2 mInputScroll;
        private readonly List<string> mInputUndoStack = new List<string>();
        private int mInputUndoIndex = -1;
        private bool mInputFieldActive;
        private bool mSuppressInputCommit;
        private bool mWaiting;
        private int mProgressIndex = -1;
        /// <summary>
        /// 用户点过输入框后，Enter 仍可打断（不依赖瞬时焦点名）。
        /// </summary>
        private bool mLockInputFocus;
        /// <summary>
        /// 运行中 Enter 打断时暂存的用户消息；Abort 结束后写入 history 并续跑。
        /// </summary>
        private readonly List<string> mOutboundQueue = new List<string>();
        /// <summary>
        /// Enter 打断触发的 Abort 结束后自动续跑。
        /// </summary>
        private bool mFlushInterrupt;

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
        private GUIStyle mInputStyle;
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
            ResetInputUndoState(mInput);
            EndWaitingTurn();
            UTAgentConfig.PrepareForChat();
            if (UTAgentBootstrap.IsAvailable)
            {
                RefreshPythonModulesFromDisk();
            }

            EditorApplication.delayCall += TryBootstrapSessionUiOnEnable;
        }

        private void TryBootstrapSessionUiOnEnable()
        {
            if (this == null)
            {
                return;
            }

            UTAgentReadiness.Status readiness = UTAgentReadiness.GetChatStatus(mRunner);
            if (!readiness.Ready && UTAgentBootstrap.IsAvailable)
            {
                readiness = UTAgentReadiness.TryEnsureChatReady(mRunner);
            }

            if (readiness.Ready)
            {
                EnsureSessionRestored();
                Repaint();
            }
        }

        private void OnDisable()
        {
            EditorApplication.delayCall -= OnAbortSafetyReset;
            EditorApplication.delayCall -= TryBootstrapSessionUiOnEnable;
        }

        private void OnDestroy()
        {
            EditorApplication.delayCall -= OnAbortSafetyReset;
            EditorApplication.delayCall -= TryBootstrapSessionUiOnEnable;
            mRunner.Abort();
            ClearPreview();
            DestroyTex(ref mUserBubbleTex);
            DestroyTex(ref mAgentBubbleTex);
            DestroyTex(ref mCopyBtnNormalTex);
            DestroyTex(ref mCopyBtnHoverTex);
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(28), GUILayout.ExpandWidth(true));
            GUILayout.Label("UT Agent", EditorStyles.boldLabel);
            string sessionLabel = mRunner.Session.HasOpenSession
                ? mRunner.Session.DisplayLabel()
                : "—";
            GUILayout.Label($"会话 {sessionLabel}", EditorStyles.miniLabel, GUILayout.MaxWidth(120));
            string status;
            if (mWaiting) status = "● Thinking...";
            else if (UTAgentReadiness.GetChatStatus(mRunner).Ready)
            {
                status = "● Ready";
            }
            else
            {
                status = "○ " + UTAgentReadiness.GetChatStatus(mRunner).Summary;
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(status, EditorStyles.miniLabel);
            if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                NewChatSession();
            }
            if (GUILayout.Button("会话…", EditorStyles.toolbarButton, GUILayout.Width(48)))
            {
                UTAgentSessionWindow.Open(mRunner, OnSessionPanelChanged);
            }
            if (UTAgentBootstrap.IsAvailable)
            {
                if (GUILayout.Button("刷新 Python", EditorStyles.toolbarButton, GUILayout.Width(88)))
                {
                    RefreshPythonModulesFromDisk(force: true);
                }
            }
            if (GUILayout.Button("⚙", EditorStyles.toolbarButton, GUILayout.Width(28)))
            {
                OpenSettingsWindow();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void OnGUI()
        {
            if (!mStylesInit || mWasProSkin != EditorGUIUtility.isProSkin)
            {
                InitStyles();
                mStylesInit = true;
                mWasProSkin = EditorGUIUtility.isProSkin;
            }

            // 尽早拦截 Undo，避免被 Unity 全局撤销抢走；且须在 TextArea 之前改好 mInput
            bool didUndoRedo = TryHandleInputUndoRedo();

            const float headerH = 32f;
            float inputH = MeasureInputAreaHeight();
            float listH = Mathf.Max(40f, position.height - headerH - inputH);

            // 先画输入框：控件 ID 在消息列表之前分配，流式增高不会改写输入框 ID / 抢焦点
            var inputRect = new Rect(0f, position.height - inputH, position.width, inputH);
            GUILayout.BeginArea(inputRect);
            DrawInputArea(didUndoRedo);
            GUILayout.EndArea();

            var headerRect = new Rect(0f, 0f, position.width, headerH);
            GUILayout.BeginArea(headerRect);
            DrawHeader();
            GUILayout.EndArea();

            var listRect = new Rect(0f, headerH, position.width, listH);
            GUILayout.BeginArea(listRect);
            DrawMessageList();
            GUILayout.EndArea();
        }

        private float MeasureInputAreaHeight()
        {
            float attach = string.IsNullOrEmpty(mAttachedImagePath) ? 0f : 36f;
            float inputWidth = Mathf.Max(80f, position.width - 32f - 56f - 48f);
            GUIStyle style = mInputStyle ?? EditorStyles.textArea;
            string measure = string.IsNullOrEmpty(mInput) ? " " : mInput;
            float contentHeight = Mathf.Max(54f, style.CalcHeight(new GUIContent(measure), inputWidth - 8f));
            float viewport = Mathf.Min(contentHeight, 160f);
            float queue = 0f;
            if (mWaiting && mOutboundQueue.Count > 0)
            {
                int show = Mathf.Min(mOutboundQueue.Count, 5);
                queue = 28f + show * 16f + (mOutboundQueue.Count > show ? 14f : 0f);
            }
            // helpBox 边距 + 底栏提示行 + Stop/Send 列
            return attach + viewport + queue + 28f + 20f + 12f;
        }

        private void DrawMessageList()
        {
            // 点击消息区则释放输入锁，便于复制气泡文本
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && mMessageScroll.ContainsMouse(evt.mousePosition))
            {
                mLockInputFocus = false;
                mInputFieldActive = false;
            }

            mMessageScroll.Draw(DrawMessageListContent, mMessages.Count > 0, GetMessageContentVersion());
        }

        private void DrawWelcome()
        {
            GUILayout.Space(40);
            GUILayout.Label("🤖", new GUIStyle(EditorStyles.label) { fontSize = 36, alignment = TextAnchor.MiddleCenter });
            GUILayout.Label("UT Agent", new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, alignment = TextAnchor.MiddleCenter });
            GUILayout.Label(mRunner.IsConfigured()
                    ? "输入任务，LLM 通过 execPython 操作 Unity。"
                    : "配置好环境变量与 Python 后发消息即可，引擎会自动启动。",
                new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(40);
        }

        private void DrawInputArea(bool didUndoRedo)
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

            Event evt = Event.current;
            bool inputHot = mInputFieldActive
                || mLockInputFocus
                || GUI.GetNameOfFocusedControl() == InputControlName;
            if (evt.type == EventType.KeyDown && inputHot)
            {
                bool keyEnter = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter;
                bool charNewline = evt.character == '\n' || evt.character == '\r';

                // Unity 常跟一条仅 character 的换行事件；若也当 Enter 会「入队后立刻空 Enter flush」
                if (charNewline && !keyEnter)
                {
                    evt.Use();
                    GUIUtility.ExitGUI();
                }

                if (keyEnter)
                {
                    if (!string.IsNullOrWhiteSpace(mInput))
                    {
                        evt.Use();
                        string toSend = mInput;
                        ClearInputAfterSend();
                        Send(toSend);
                        GUIUtility.ExitGUI();
                    }
                    else if (mWaiting && mOutboundQueue.Count > 0)
                    {
                        evt.Use();
                        FlushOutboundInterrupt();
                        GUIUtility.ExitGUI();
                    }
                    else
                    {
                        // 空 Enter 且无队列：吞掉，避免 TextArea 插入换行
                        evt.Use();
                    }
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("📎", GUILayout.Width(32), GUILayout.Height(32)))
            {
                OpenImageBrowser();
            }

            float inputWidth = Mathf.Max(80f, position.width - 32f - 56f - 48f);
            string measure = string.IsNullOrEmpty(mInput) ? " " : mInput;
            float contentHeight = Mathf.Max(54f, mInputStyle.CalcHeight(new GUIContent(measure), inputWidth - 8f));
            const float maxViewport = 160f;
            bool needsScroll = contentHeight > maxViewport + 0.5f;
            float viewportHeight = needsScroll ? maxViewport : contentHeight;

            if (didUndoRedo)
            {
                // 打断 IMGUI TextEditor，否则会继续吐出撤销前的文本把 mInput 盖回去
                EditorGUIUtility.editingTextField = false;
            }

            GUI.SetNextControlName(InputControlName);
            string nextInput;
            if (needsScroll)
            {
                mInputScroll = EditorGUILayout.BeginScrollView(
                    mInputScroll,
                    false,
                    true,
                    GUILayout.Height(viewportHeight),
                    GUILayout.ExpandWidth(true));
                nextInput = EditorGUILayout.TextArea(
                    mInput,
                    mInputStyle,
                    GUILayout.Height(contentHeight),
                    GUILayout.ExpandWidth(true));
                EditorGUILayout.EndScrollView();
            }
            else
            {
                mInputScroll = Vector2.zero;
                nextInput = EditorGUILayout.TextArea(
                    mInput,
                    mInputStyle,
                    GUILayout.Height(contentHeight),
                    GUILayout.ExpandWidth(true));
            }

            if (GUI.GetNameOfFocusedControl() == InputControlName)
            {
                mInputFieldActive = true;
                mLockInputFocus = true;
            }
            else if (evt.type == EventType.MouseDown)
            {
                // 点到输入框区域则上锁；点到别处（非消息区已在 DrawMessageList 处理）不在此释放
                Rect inputRect = GUILayoutUtility.GetLastRect();
                if (inputRect.Contains(evt.mousePosition))
                {
                    mInputFieldActive = true;
                    mLockInputFocus = true;
                }
            }

            if (didUndoRedo || mSuppressInputCommit)
            {
                mSuppressInputCommit = false;
                GUI.FocusControl(InputControlName);
                mInputFieldActive = true;
            }
            else
            {
                CommitInputText(nextInput);
                // 名称焦点偶发对不上时，只要文本在变就视为输入框活跃
                if (nextInput != null && GUI.GetNameOfFocusedControl() == InputControlName)
                {
                    mInputFieldActive = true;
                }
            }

            EditorGUILayout.BeginVertical(GUILayout.Width(56));
            if (mWaiting)
            {
                if (GUILayout.Button("Stop", GUILayout.Height(24)))
                {
                    mOutboundQueue.Clear();
                    mFlushInterrupt = false;
                    mRunner.Abort();
                    EditorApplication.delayCall -= OnAbortSafetyReset;
                    EditorApplication.delayCall += OnAbortSafetyReset;
                }
            }
            else if (CanContinueFromLastMessage() && GUILayout.Button("续跑", GUILayout.Height(24)))
            {
                RunContinue();
            }
            else if (GUILayout.Button("Send", GUILayout.Height(24)))
            {
                Send(mInput);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (mWaiting && mOutboundQueue.Count > 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"待发送 {mOutboundQueue.Count} 条", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("发送", GUILayout.Width(48), GUILayout.Height(20)))
                {
                    FlushOutboundInterrupt();
                }
                if (GUILayout.Button("清空", GUILayout.Width(40), GUILayout.Height(20)))
                {
                    mOutboundQueue.Clear();
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();
                int show = Mathf.Min(mOutboundQueue.Count, 5);
                for (int i = 0; i < show; i++)
                {
                    string preview = mOutboundQueue[i];
                    if (preview.Length > 72)
                    {
                        preview = preview.Substring(0, 72) + "…";
                    }
                    GUILayout.Label($"· {preview}", EditorStyles.wordWrappedMiniLabel);
                }
                if (mOutboundQueue.Count > show)
                {
                    GUILayout.Label($"…另有 {mOutboundQueue.Count - show} 条", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(
                mWaiting
                    ? "Enter 入队 · 空 Enter / 发送 = 打断续跑"
                    : "Enter 发送",
                EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", GUILayout.Width(56)))
            {
                if (EditorUtility.DisplayDialog(
                        "Clear",
                        "新建会话并清空当前消息？其它已保存会话不会删除。",
                        "新建会话",
                        "Cancel"))
                {
                    NewChatSession();
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 输入框聚焦时拦截 Undo/Redo，避免落到 Unity 全局撤销；并维护本地输入历史。
        /// </summary>
        private bool TryHandleInputUndoRedo()
        {
            if (focusedWindow != this)
            {
                return false;
            }

            if (!mInputFieldActive && GUI.GetNameOfFocusedControl() != InputControlName)
            {
                return false;
            }

            Event evt = Event.current;
            bool isUndo = false;
            bool isRedo = false;

            if (evt.type == EventType.ValidateCommand
                && (evt.commandName == "Undo" || evt.commandName == "Redo"))
            {
                evt.Use();
                return false;
            }

            if (evt.type == EventType.ExecuteCommand)
            {
                isUndo = evt.commandName == "Undo";
                isRedo = evt.commandName == "Redo";
            }
            else if (evt.type == EventType.KeyDown && (evt.control || evt.command) && !evt.alt)
            {
                if (evt.keyCode == KeyCode.Z && !evt.shift)
                {
                    isUndo = true;
                }
                else if (evt.keyCode == KeyCode.Y || (evt.keyCode == KeyCode.Z && evt.shift))
                {
                    isRedo = true;
                }
            }

            if (!isUndo && !isRedo)
            {
                return false;
            }

            evt.Use();
            if (isUndo)
            {
                ApplyInputUndo();
            }
            else
            {
                ApplyInputRedo();
            }

            mSuppressInputCommit = true;
            return true;
        }

        private void CommitInputText(string next)
        {
            next ??= "";
            bool hadTab = next.IndexOf('\t') >= 0;
            next = next.Replace("\t", "    ");
            if (next == mInput)
            {
                return;
            }

            if (mInputUndoIndex < mInputUndoStack.Count - 1)
            {
                mInputUndoStack.RemoveRange(
                    mInputUndoIndex + 1,
                    mInputUndoStack.Count - mInputUndoIndex - 1);
            }

            if (mInputUndoStack.Count == 0)
            {
                mInputUndoStack.Add(mInput ?? "");
                mInputUndoIndex = 0;
            }

            mInputUndoStack.Add(next);
            mInputUndoIndex = mInputUndoStack.Count - 1;
            const int maxSteps = 80;
            if (mInputUndoStack.Count > maxSteps)
            {
                int remove = mInputUndoStack.Count - maxSteps;
                mInputUndoStack.RemoveRange(0, remove);
                mInputUndoIndex -= remove;
            }

            mInput = next;
            mInputFieldActive = true;
            if (hadTab)
            {
                // Tab 已换成空格，打断 TextEditor 以免下一帧又带出 \t
                EditorGUIUtility.editingTextField = false;
            }
        }

        private void ApplyInputUndo()
        {
            if (mInputUndoStack.Count == 0)
            {
                mInputUndoStack.Add(mInput ?? "");
                mInputUndoIndex = 0;
                return;
            }

            if (mInputUndoIndex == mInputUndoStack.Count - 1
                && mInputUndoStack[mInputUndoIndex] != (mInput ?? ""))
            {
                CommitInputText(mInput);
            }

            if (mInputUndoIndex <= 0)
            {
                mInput = mInputUndoStack[0];
                mInputUndoIndex = 0;
                return;
            }

            mInputUndoIndex--;
            mInput = mInputUndoStack[mInputUndoIndex];
        }

        private void ApplyInputRedo()
        {
            if (mInputUndoIndex < 0 || mInputUndoIndex >= mInputUndoStack.Count - 1)
            {
                return;
            }

            mInputUndoIndex++;
            mInput = mInputUndoStack[mInputUndoIndex];
        }

        private void ResetInputUndoState(string text)
        {
            mInput = text ?? "";
            mInputUndoStack.Clear();
            mInputUndoStack.Add(mInput);
            mInputUndoIndex = 0;
            mInputScroll = Vector2.zero;
            mSuppressInputCommit = false;
        }

        /// <summary>
        /// 发送后清空输入，打断 TextArea 内嵌编辑器，避免 Enter 残留换行/重复提交。
        /// </summary>
        private void ClearInputAfterSend()
        {
            mInput = "";
            mInputUndoStack.Clear();
            mInputUndoStack.Add("");
            mInputUndoIndex = 0;
            mInputScroll = Vector2.zero;
            mSuppressInputCommit = true;
            EditorGUIUtility.editingTextField = false;
            // 发送后仍允许 Enter 打断；不强制 FocusTextInControl（会与流式刷新打架）
            mLockInputFocus = true;
        }

        private void Send(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            text = text.Trim();

            // 运行中：入待发送队列，不立刻打断
            if (mWaiting)
            {
                mOutboundQueue.Add(text);
                ClearInputAfterSend();
                ClearAttached();
                Repaint();
                return;
            }

            UTAgentConfig.PrepareForChat();
            UTAgentReadiness.Status readiness = UTAgentReadiness.TryEnsureChatReady(mRunner);
            if (!readiness.Ready)
            {
                AddMessage($"{readiness.Summary}\n{readiness.Detail}", false);
                return;
            }
            EnsureSessionRestored();
            if (TryContinueFromInput(text))
            {
                return;
            }
            string img = mAttachedImagePath;
            mMessageScroll.OnSend();
            AddMessage(text, true);
            ClearInputAfterSend();
            RunTurn(text, img);
            ClearAttached();
            Repaint();
        }

        /// <summary>
        /// 发送队列：打断当前 turn，把待发写入 history 后续跑。
        /// </summary>
        private void FlushOutboundInterrupt()
        {
            if (!mWaiting || mOutboundQueue.Count == 0 || mFlushInterrupt)
            {
                return;
            }

            mFlushInterrupt = true;
            mRunner.Abort();
            EditorApplication.delayCall -= OnAbortSafetyReset;
            EditorApplication.delayCall += OnAbortSafetyReset;
            Repaint();
        }

        private void FlushOutboundAndContinue()
        {
            if (mOutboundQueue.Count == 0)
            {
                return;
            }

            mMessageScroll.OnSend();
            for (int i = 0; i < mOutboundQueue.Count; i++)
            {
                string text = mOutboundQueue[i];
                AddMessage(text, true);
                mRunner.AppendUserMessage(text, "user");
            }

            mOutboundQueue.Clear();
            RunContinue();
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

            ClearInputAfterSend();
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
            EditorApplication.delayCall -= OnAbortSafetyReset;

            bool willFlush = mFlushInterrupt && outcome == "aborted" && mOutboundQueue.Count > 0;
            if (willFlush)
            {
                mFlushInterrupt = false;
            }

            try
            {
                FinalizeProgress(finalText, isError, outcome, suppressContinue: willFlush);
            }
            finally
            {
                EndWaitingTurn();
                if (willFlush)
                {
                    FlushOutboundAndContinue();
                }
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
            // 仅当 Abort 未走到 CompleteTurn 时兜底
            if (!mWaiting || mRunner.HasActiveTurn)
            {
                return;
            }

            bool willFlush = mFlushInterrupt && mOutboundQueue.Count > 0;
            mFlushInterrupt = false;
            EndWaitingTurn();
            if (willFlush)
            {
                FlushOutboundAndContinue();
            }
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

        private void FinalizeProgress(string finalText, bool isError, string outcome, bool suppressContinue = false)
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

            msg.ShowContinue = !suppressContinue && UTAgentRunner.CanContinueFromOutcome(outcome);
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

        /// <summary>
        /// configure 后 / 发送前：保证 Python history 与打开的 session 一致。
        /// </summary>
        private void EnsureSessionRestored()
        {
            if (!mRunner.IsConfigured())
            {
                return;
            }

            bool hadMessages = mMessages.Count > 0;
            if (!mRunner.EnsureSessionHistorySynced(out string loadJson, out string err))
            {
                if (!string.IsNullOrEmpty(err) && err != "未配置")
                {
                    Debug.LogWarning($"[UTAgentChat] session 同步失败：{err}");
                }

                return;
            }

            // 仅在 UI 仍为空时灌气泡，避免覆盖用户正在看的列表；
            // 若 history 刚从磁盘重载且 UI 空，则重建。
            if (!hadMessages && !string.IsNullOrEmpty(loadJson))
            {
                ApplyUiMessagesFromSessionJson(loadJson);
            }
        }

        private void NewChatSession()
        {
            mOutboundQueue.Clear();
            mFlushInterrupt = false;
            mRunner.Abort();
            EndWaitingTurn();
            mMessages.Clear();
            mMessageScroll.Reset();
            if (mRunner.IsConfigured() || UTAgentBootstrap.IsAvailable)
            {
                if (!mRunner.IsConfigured())
                {
                    UTAgentReadiness.TryEnsureChatReady(mRunner);
                }

                if (mRunner.IsConfigured())
                {
                    mRunner.ClearHistoryAndNewSession();
                }
            }

            Repaint();
        }

        private void OnSessionPanelChanged()
        {
            // 面板内打开/删除后刷新 Chat UI
            if (!mRunner.Session.HasOpenSession)
            {
                mMessages.Clear();
                mMessageScroll.Reset();
                Repaint();
                return;
            }

            if (mRunner.OpenSession(mRunner.Session.CurrentSessionId, out string loadJson, out _))
            {
                mMessages.Clear();
                mMessageScroll.Reset();
                ApplyUiMessagesFromSessionJson(loadJson);
            }

            Repaint();
        }

        private void ShowRestoreSessionMenu()
        {
            UTAgentSessionWindow.Open(mRunner, OnSessionPanelChanged);
        }

        private void RestoreSessionById(string sessionId)
        {
            if (mWaiting)
            {
                EditorUtility.DisplayDialog("恢复会话", "请先等待当前回合结束或 Stop。", "OK");
                return;
            }

            mOutboundQueue.Clear();
            mFlushInterrupt = false;
            mRunner.Abort();
            EndWaitingTurn();
            if (!mRunner.OpenSession(sessionId, out string loadJson, out string err))
            {
                EditorUtility.DisplayDialog("恢复会话", err ?? "打开失败", "OK");
                return;
            }

            mMessages.Clear();
            mMessageScroll.Reset();
            ApplyUiMessagesFromSessionJson(loadJson);
            Repaint();
        }

        private void ApplyUiMessagesFromSessionJson(string loadJson)
        {
            if (string.IsNullOrEmpty(loadJson))
            {
                return;
            }

            List<(bool isUser, string text, string block)> items =
                UTAgentRunner.ParseUiMessages(loadJson);
            foreach ((bool isUser, string text, string block) in items)
            {
                if (isUser)
                {
                    AddMessage(text, true);
                    continue;
                }

                var msg = new ChatMessage
                {
                    Text = "",
                    IsUser = false,
                    Timestamp = DateTime.Now.ToString("HH:mm"),
                    Blocks = new List<MessageBlock>(),
                };
                string type = string.IsNullOrEmpty(block) ? "answer" : block;
                if (type == "tool_call")
                {
                    type = "tool_call";
                }
                else if (type == "observation")
                {
                    type = "observation";
                }
                else if (type == "compaction")
                {
                    type = "answer";
                }
                else
                {
                    type = "answer";
                }

                if (!string.IsNullOrEmpty(text))
                {
                    msg.Blocks.Add(new MessageBlock { Type = type, Text = text });
                }

                mMessages.Add(msg);
            }
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

            mRunner.InvalidateConfigured();
            UTAgentReadiness.Status status = UTAgentReadiness.TryEnsureChatReady(mRunner);
            if (mRunner.IsConfigured())
            {
                EnsureSessionRestored();
                AddMessage($"Python 模块已重新加载；Agent 已就绪。\n{status.Detail}", false);
            }
            else
            {
                AddMessage($"Python 模块已重新加载。{status.Summary}：{status.Detail}", false);
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

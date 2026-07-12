using UnityEditor;
using UnityEngine;
using UTAgent.Editor.Bridge;

namespace UTAgent.Editor
{
    public partial class UTAgentChatWindow
    {
        private bool mFoldLlmSettings = true;
        private bool mFoldBridgeSettings = true;
        private bool mFoldLogSettings;

        private string mBridgeFeedback = "";
        private double mBridgeFeedbackUntil;

        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawLlmSettingsSection();
            DrawSettingsSeparator();
            DrawBridgeSettingsSection();
            DrawSettingsSeparator();
            DrawLogSettingsSection();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        private static void DrawSettingsSeparator()
        {
            EditorGUILayout.Space(6);
            Rect line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.35f));
            EditorGUILayout.Space(6);
        }

        private void DrawLlmSettingsSection()
        {
            mFoldLlmSettings = EditorGUILayout.Foldout(mFoldLlmSettings, "LLM 配置", true, EditorStyles.foldoutHeader);
            if (!mFoldLlmSettings)
            {
                return;
            }

            mApiKey = EditorGUILayout.PasswordField("API Key", mApiKey);
            mBaseURL = EditorGUILayout.TextField("Base URL", mBaseURL);
            mModel = EditorGUILayout.TextField("Model", mModel);
            mMaxSteps = EditorGUILayout.IntSlider("Max Steps", mMaxSteps, 1, 100);
            if (GUILayout.Button("保存 LLM 配置"))
            {
                SaveSettings();
                ApplySettings();
            }
        }

        private void DrawBridgeSettingsSection()
        {
            mFoldBridgeSettings = EditorGUILayout.Foldout(mFoldBridgeSettings, "Cursor CLI 桥接", true, EditorStyles.foldoutHeader);
            if (!mFoldBridgeSettings)
            {
                return;
            }

            mBridgeEnabled = EditorGUILayout.Toggle("启用 localhost 桥", mBridgeEnabled);
            mBridgePort = EditorGUILayout.IntField("端口", mBridgePort);
            if (mBridgePort < 1024 || mBridgePort > 65535)
            {
                mBridgePort = UTAgentEditorHttpServer.DefaultPort;
            }

            string statusText = UTAgentEditorHttpServer.GetStatusLabel();
            EditorGUILayout.LabelField("状态", statusText);

            if (UTAgentEditorHttpServer.HasPendingUiChanges(mBridgeEnabled, mBridgePort))
            {
                EditorGUILayout.HelpBox("有未应用的更改，请点击下方按钮保存。", MessageType.Warning);
            }
            else if (!string.IsNullOrEmpty(mBridgeFeedback)
                && EditorApplication.timeSinceStartup < mBridgeFeedbackUntil)
            {
                EditorGUILayout.HelpBox(mBridgeFeedback, MessageType.Info);
            }

            EditorGUI.BeginDisabledGroup(!UTAgentEditorHttpServer.NeedsApply(mBridgeEnabled, mBridgePort));
            if (GUILayout.Button("应用 Bridge 设置"))
            {
                bool changed = UTAgentEditorHttpServer.ApplySettings(mBridgeEnabled, mBridgePort);
                mBridgeFeedback = changed
                    ? (UTAgentEditorHttpServer.IsListening
                        ? "Bridge 已启动"
                        : (mBridgeEnabled ? "Bridge 启动失败，见 Console" : "Bridge 已停止"))
                    : "已是当前状态，无需重复应用";
                mBridgeFeedbackUntil = EditorApplication.timeSinceStartup + 2.5;
                Repaint();
            }

            EditorGUI.EndDisabledGroup();
        }

        private void DrawLogSettingsSection()
        {
            mFoldLogSettings = EditorGUILayout.Foldout(mFoldLogSettings, "会话日志", true, EditorStyles.foldoutHeader);
            if (!mFoldLogSettings)
            {
                return;
            }

            mLogDirectory = EditorGUILayout.TextField("日志目录", mLogDirectory);
            string hint = string.IsNullOrWhiteSpace(mLogDirectory)
                ? UTAgentSessionLogger.GetDefaultLogDirectory()
                : "留空则使用默认目录";
            EditorGUILayout.LabelField(" ", hint, EditorStyles.miniLabel);
            if (GUILayout.Button("打开日志目录"))
            {
                UTAgentSessionLogger.RevealLogDirectory();
            }
        }

        private void TrySilentConfigureRunner()
        {
            if (string.IsNullOrWhiteSpace(mApiKey))
            {
                return;
            }

            if (!UTAgentBootstrap.IsAvailable)
            {
                return;
            }

            if (mRunner.IsConfigured())
            {
                return;
            }

            UTAgentSessionLogger.EnsureLogDirectory(
                string.IsNullOrWhiteSpace(mLogDirectory) ? null : mLogDirectory);
            mRunner.Configure(mApiKey, mBaseURL, mModel, mMaxSteps);
        }
    }
}

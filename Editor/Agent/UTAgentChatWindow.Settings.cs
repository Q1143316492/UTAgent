using UnityEditor;
using UnityEngine;
using UTAgent.Editor.Core;
using UTAgent.Editor.RemoteCli;

namespace UTAgent.Editor.Agent
{
    public partial class UTAgentChatWindow
    {
        private bool mFoldAdvancedSettings;

        private string mPythonHome = "";
        private string mPythonDll = UTAgentPrefs.DefaultPythonDll;

        private string mSettingsFeedback = "";
        private MessageType mSettingsFeedbackType = MessageType.Info;
        private double mSettingsFeedbackUntil;

        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawSettingsStatusSummary();

            mApiKey = EditorGUILayout.PasswordField("API Key", mApiKey);
            mModel = EditorGUILayout.TextField("Model", mModel);
            mBridgeEnabled = EditorGUILayout.Toggle("启用 Remote CLI（utagent 命令）", mBridgeEnabled);

            mFoldAdvancedSettings = EditorGUILayout.Foldout(mFoldAdvancedSettings, "高级", true);
            if (mFoldAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                mBaseURL = EditorGUILayout.TextField("Base URL", mBaseURL);
                mMaxSteps = EditorGUILayout.IntSlider("Max Steps", mMaxSteps, 1, 100);
                mPythonHome = EditorGUILayout.TextField("Python 目录", mPythonHome);
                mPythonDll = EditorGUILayout.TextField("python*.dll", string.IsNullOrWhiteSpace(mPythonDll) ? UTAgentPrefs.DefaultPythonDll : mPythonDll);
                mBridgePort = EditorGUILayout.IntField("Remote CLI 端口", mBridgePort);
                if (mBridgePort < 1024 || mBridgePort > 65535)
                {
                    mBridgePort = UTAgentEditorHttpServer.DefaultPort;
                }

                mLogDirectory = EditorGUILayout.TextField("日志目录", mLogDirectory);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("打开日志目录", GUILayout.Width(100)))
                {
                    UTAgentSessionLogger.RevealLogDirectory();
                }

                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
            }

            if (!string.IsNullOrEmpty(mSettingsFeedback)
                && EditorApplication.timeSinceStartup < mSettingsFeedbackUntil)
            {
                EditorGUILayout.HelpBox(mSettingsFeedback, mSettingsFeedbackType);
            }

            if (GUILayout.Button("保存并应用", GUILayout.Height(28)))
            {
                ApplyAllSettings();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        private void DrawSettingsStatusSummary()
        {
            string pythonHome = PythonHomeResolver.ResolvePythonHome();
            string pythonDisplay = PythonHomeResolver.GetDisplayPythonHome();
            bool pythonOk = pythonHome != null;
            bool apiOk = !string.IsNullOrWhiteSpace(mApiKey);
            bool engineOk = UTAgentBootstrap.IsAvailable;
            bool runnerOk = engineOk && mRunner.IsConfigured();
            bool ready = runnerOk;

            var lines = new System.Text.StringBuilder();
            lines.AppendLine(pythonOk
                ? $"✓ Python  {pythonHome}"
                : $"✗ Python  未找到 — 将 CPython 拷入\n  {pythonDisplay}");
            lines.AppendLine(apiOk ? "✓ API Key  已填写" : "✗ API Key  未填写");
            lines.AppendLine(engineOk ? "✓ 引擎  已初始化" : "○ 引擎  未初始化");
            lines.AppendLine(ready ? "✓ 可以对话" : "○ 还不能对话 — 填好 API Key 后点「保存并应用」");

            MessageType boxType = ready ? MessageType.Info : (pythonOk ? MessageType.Warning : MessageType.Error);
            EditorGUILayout.HelpBox(lines.ToString().TrimEnd(), boxType);
        }

        private void ApplyAllSettings()
        {
            SaveSettings();
            UTAgentEditorHttpServer.ApplySettings(mBridgeEnabled, mBridgePort);

            if (!UTAgentBootstrap.IsAvailable)
            {
                try
                {
                    UTAgentBootstrap.Initialize();
                }
                catch (System.Exception e)
                {
                    ShowSettingsFeedback($"初始化失败：{e.Message}", MessageType.Error, 8);
                    return;
                }
            }

            mRunner.InvalidateConfigured();
            string configureResult = mRunner.ConfigureFromPrefs();
            bool runnerOk = mRunner.IsConfigured();

            if (runnerOk)
            {
                string bridgeNote = mBridgeEnabled
                    ? (UTAgentEditorHttpServer.IsListening
                        ? $"Remote CLI 127.0.0.1:{UTAgentEditorHttpServer.Port}"
                        : "Remote CLI 启用失败，见 Console")
                    : "Remote CLI 未启用";
                ShowSettingsFeedback($"已保存。可以对话。\n{bridgeNote}", MessageType.Info, 5);
            }
            else if (string.IsNullOrWhiteSpace(mApiKey))
            {
                ShowSettingsFeedback("已保存，但还不能对话：请填写 API Key。", MessageType.Warning, 6);
            }
            else
            {
                ShowSettingsFeedback($"已保存，但 Agent 配置失败：\n{configureResult}", MessageType.Error, 8);
            }

            Repaint();
        }

        private void ShowSettingsFeedback(string message, MessageType type, double seconds)
        {
            mSettingsFeedback = message;
            mSettingsFeedbackType = type;
            mSettingsFeedbackUntil = EditorApplication.timeSinceStartup + seconds;
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

            mRunner.ConfigureFromPrefs();
        }
    }
}

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UTAgent.Editor.Agent;
using UTAgent.Editor.Core;
using UTAgent.Editor.RemoteCli;

namespace UTAgent.Editor.Config
{
    /// <summary>
    /// UTAgent 设置：只改配置并保存；运行时由 <see cref="UTAgentReadiness"/> 按需拉起。
    /// </summary>
    public sealed class UTAgentSettingsWindow : EditorWindow
    {
        private const int TabPython = 0;
        private const int TabChat = 1;
        private const int TabBridge = 2;
        private const int TabLog = 3;

        private static readonly string[] sTabLabels = { "① Python", "② 大模型", "③ CLI", "日志" };

        private int mTab;
        private readonly UTAgentRunner mRunner = new UTAgentRunner();

        private int mProviderIndex;
        private int mModelIndex;
        private int mMaxSteps = 25;
        private string mApiKeyEnvVar = UTAgentConfig.DefaultApiKeyEnvVar;
        private string mBaseUrlOverride = "";
        private bool mFoldBaseUrlOverride;

        private string mPythonDll = UTAgentConfig.DefaultPythonDll;
        private bool mBridgeEnabled = true;
        private int mBridgePort = UTAgentConfig.DefaultBridgePort;
        private string mLogDirectory = "";

        private string mFeedback = "";
        private MessageType mFeedbackType = MessageType.Info;
        private double mFeedbackUntil;

        [MenuItem("Window/UT Agent/Settings")]
        public static void Open()
        {
            var window = GetWindow<UTAgentSettingsWindow>("UTAgent Settings");
            window.minSize = new Vector2(440, 400);
            window.Show();
        }

        private void OnEnable()
        {
            UTAgentConfig.EnsureLoaded();
            LoadFromConfig();
        }

        private void OnGUI()
        {
            UTAgentConfig.EnsureLoaded();
            DrawSetupGuide();
            DrawRuntimeStatus();

            if (UTAgentConfig.ShowLegacyApiKeyWarning)
            {
                EditorGUILayout.HelpBox(
                    "旧版 API Key 已从 EditorPrefs 清除，请设置环境变量后保存大模型设置。",
                    MessageType.Warning);
            }

            mTab = GUILayout.Toolbar(mTab, sTabLabels);
            EditorGUILayout.Space(6);

            switch (mTab)
            {
                case TabPython:
                    DrawPythonTab();
                    break;
                case TabChat:
                    DrawChatTab();
                    break;
                case TabBridge:
                    DrawBridgeTab();
                    break;
                case TabLog:
                    DrawLogTab();
                    break;
            }

            DrawFeedback();
        }

        private void DrawSetupGuide()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("设置流程（按顺序）", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);
            DrawStepButton(TabPython, IsStepPythonDone(), "第一步", "初始化 Python 环境");
            DrawStepButton(TabChat, IsStepLlmDone(), "第二步", "配置大模型（API Key 用环境变量）");
            DrawStepButton(TabBridge, IsStepCliDone(), "第三步", "Remote CLI（默认开启）");
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        private void DrawStepButton(int tabIndex, bool done, string stepLabel, string title)
        {
            bool isCurrent = mTab == tabIndex;
            string mark = done ? "✓" : "○";
            string label = $"{mark} {stepLabel}  {title}";
            var style = new GUIStyle(isCurrent ? EditorStyles.boldLabel : EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
            };
            if (isCurrent)
            {
                GUI.backgroundColor = new Color(0.75f, 0.9f, 1f);
            }

            if (GUILayout.Button(label, style, GUILayout.Height(22)))
            {
                mTab = tabIndex;
            }

            GUI.backgroundColor = Color.white;
        }

        private static bool IsStepPythonDone()
        {
            return UTAgentBootstrap.IsAvailable
                || PythonHomeResolver.ResolvePythonHome() != null;
        }

        private bool IsStepLlmDone()
        {
            return UTAgentConfig.TryCheckApiKey(mApiKeyEnvVar, out _);
        }

        private bool IsStepCliDone()
        {
            return mBridgeEnabled;
        }

        private void DrawRuntimeStatus()
        {
            UTAgentReadiness.Status status = UTAgentReadiness.GetChatStatus(mRunner);
            MessageType boxType = status.Ready ? MessageType.Info : MessageType.Warning;
            EditorGUILayout.HelpBox(UTAgentReadiness.FormatStatusBox(status), boxType);
            EditorGUILayout.Space(2);
        }

        private void DrawPythonTab()
        {
            DrawStepHeader("第一步：初始化 Python 环境");

            string savedHome = UTAgentConfig.ResolvePythonHomeFromConfig();
            string resolved = PythonHomeResolver.ResolvePythonHome();
            bool engineUp = UTAgentBootstrap.IsAvailable;

            if (!string.IsNullOrWhiteSpace(savedHome))
            {
                EditorGUILayout.LabelField("已保存目录", savedHome);
            }

            EditorGUILayout.LabelField(
                "引擎状态",
                engineUp ? "✓ 运行中" : (resolved != null ? "○ 未启动（点保存后自动启动）" : "✗ 未找到 Python"));

            if (!PythonHomeResolver.HasSavedPythonHome())
            {
                if (GUILayout.Button("选择 Python 文件夹…", GUILayout.Height(32)))
                {
                    PickPythonFolder();
                }
            }

            mPythonDll = EditorGUILayout.TextField("python*.dll", mPythonDll);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存并初始化", GUILayout.Height(32)))
            {
                SavePythonSettings();
            }

            if (GUILayout.Button("重置引擎", GUILayout.Height(32)))
            {
                ResetPython();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawChatTab()
        {
            DrawStepHeader("第二步：配置大模型");

            EditorGUILayout.LabelField(
                $"API Key 通过环境变量 {mApiKeyEnvVar} 提供，不写入 JSON。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            DrawProviderModelFields();
            mMaxSteps = EditorGUILayout.IntSlider("Max Steps", mMaxSteps, 1, 100);
            mApiKeyEnvVar = EditorGUILayout.TextField("API Key 环境变量名", mApiKeyEnvVar);

            bool apiOk = UTAgentConfig.TryCheckApiKey(mApiKeyEnvVar, out string apiMsg);
            EditorGUILayout.HelpBox(apiMsg, apiOk ? MessageType.Info : MessageType.Warning);

            if (GUILayout.Button("保存大模型设置", GUILayout.Height(32)))
            {
                SaveChatSettings();
            }
        }

        private void DrawBridgeTab()
        {
            DrawStepHeader("第三步：Remote CLI（默认开启）");

            EditorGUILayout.LabelField(
                "供 Cursor / utagent 命令调用。首次打开 Chat 时会按此配置启动监听。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            mBridgeEnabled = EditorGUILayout.Toggle("启用 Remote CLI", mBridgeEnabled);
            mBridgePort = EditorGUILayout.IntField("端口", mBridgePort);
            if (mBridgePort < 1024 || mBridgePort > 65535)
            {
                mBridgePort = UTAgentConfig.DefaultBridgePort;
            }

            EditorGUILayout.LabelField("状态", UTAgentEditorHttpServer.GetStatusLabel());

            if (GUILayout.Button("保存 CLI 设置", GUILayout.Height(32)))
            {
                SaveAndApplyBridge();
            }
        }

        private void DrawLogTab()
        {
            mLogDirectory = EditorGUILayout.TextField("日志目录", mLogDirectory);
            EditorGUILayout.LabelField("默认", UTAgentSessionLogger.GetDefaultLogDirectory());

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存", GUILayout.Height(30)))
            {
                SaveLogSettings();
            }

            if (GUILayout.Button("打开日志目录", GUILayout.Height(30)))
            {
                UTAgentSessionLogger.RevealLogDirectory();
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void DrawStepHeader(string title)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
        }

        private void DrawProviderModelFields()
        {
            ProviderDto[] providers = UTAgentConfig.Current.providers ?? Array.Empty<ProviderDto>();
            if (providers.Length == 0)
            {
                EditorGUILayout.HelpBox("defaults.json 中无 Provider 定义", MessageType.Error);
                return;
            }

            string[] providerLabels = providers.Select(p => p.displayName ?? p.id).ToArray();
            mProviderIndex = EditorGUILayout.Popup("Provider", mProviderIndex, providerLabels);
            if (mProviderIndex < 0 || mProviderIndex >= providers.Length)
            {
                mProviderIndex = 0;
            }

            ProviderDto provider = providers[mProviderIndex];
            ModelDto[] models = provider.models ?? Array.Empty<ModelDto>();
            if (models.Length == 0)
            {
                EditorGUILayout.HelpBox("该 Provider 无模型列表", MessageType.Warning);
                return;
            }

            string[] modelLabels = models.Select(m => m.displayName ?? m.id).ToArray();
            mModelIndex = EditorGUILayout.Popup("Model", mModelIndex, modelLabels);
            if (mModelIndex < 0 || mModelIndex >= models.Length)
            {
                mModelIndex = 0;
            }

            string resolvedUrl = string.IsNullOrWhiteSpace(mBaseUrlOverride)
                ? provider.baseUrl
                : mBaseUrlOverride;
            EditorGUILayout.LabelField("Base URL", resolvedUrl ?? "");

            mFoldBaseUrlOverride = EditorGUILayout.Foldout(mFoldBaseUrlOverride, "高级：覆盖 Base URL", true);
            if (mFoldBaseUrlOverride)
            {
                EditorGUI.indentLevel++;
                mBaseUrlOverride = EditorGUILayout.TextField("覆盖 URL（可留空）", mBaseUrlOverride);
                EditorGUI.indentLevel--;
            }
        }

        private void PickPythonFolder()
        {
            string start = PythonHomeResolver.GetDisplayPythonHome();
            string picked = EditorUtility.OpenFolderPanel("选择 Python 安装目录", start, "");
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            UTAgentConfig.Current.python.home = picked;
            mPythonDll = UTAgentConfig.Current.python.dll;
            UTAgentConfig.SaveLocal();
            UTAgentReadiness.Status status = UTAgentReadiness.TryEnsurePythonEngine();
            ShowStatusFeedback("Python 目录已保存", status);
            Repaint();
        }

        private void SaveChatSettings()
        {
            SyncChatFieldsToConfig();
            UTAgentConfig.SaveLocal();
            ShowFeedback("大模型设置已保存。", MessageType.Info, 3);
        }

        private void SavePythonSettings()
        {
            UTAgentConfig.Current.python.dll = string.IsNullOrWhiteSpace(mPythonDll)
                ? UTAgentConfig.DefaultPythonDll
                : mPythonDll.Trim();
            UTAgentConfig.SaveLocal();
            UTAgentReadiness.Status status = UTAgentReadiness.TryEnsurePythonEngine();
            ShowStatusFeedback("Python 设置已保存", status);
        }

        private void ResetPython()
        {
            try
            {
                UTAgentBootstrap.Shutdown();
                mRunner.InvalidateConfigured();
                ShowFeedback("引擎已重置。", MessageType.Info, 4);
            }
            catch (Exception e)
            {
                ShowFeedback($"重置失败：{e.Message}", MessageType.Error, 6);
            }
        }

        private void SaveAndApplyBridge()
        {
            UTAgentConfig.Current.bridge.enabled = mBridgeEnabled;
            UTAgentConfig.Current.bridge.port = mBridgePort;
            UTAgentConfig.SaveLocal();
            UTAgentEditorHttpServer.ApplyBridgeConfig();
            ShowFeedback(
                $"CLI：{UTAgentEditorHttpServer.GetStatusLabel()}",
                UTAgentEditorHttpServer.IsListening ? MessageType.Info : MessageType.Warning,
                5);
        }

        private void SaveLogSettings()
        {
            string normalized = PythonPathConfig.NormalizeOptionalPath(
                mLogDirectory,
                UTAgentSessionLogger.GetDefaultLogDirectory());
            UTAgentConfig.Current.log.directory = normalized;
            UTAgentConfig.SaveLocal();
            ShowFeedback("日志目录已保存。", MessageType.Info, 3);
        }

        private void SyncChatFieldsToConfig()
        {
            ProviderDto[] providers = UTAgentConfig.Current.providers ?? Array.Empty<ProviderDto>();
            if (providers.Length > 0 && mProviderIndex >= 0 && mProviderIndex < providers.Length)
            {
                UTAgentConfig.Current.llm.providerId = providers[mProviderIndex].id;
                ModelDto[] models = providers[mProviderIndex].models ?? Array.Empty<ModelDto>();
                if (models.Length > 0 && mModelIndex >= 0 && mModelIndex < models.Length)
                {
                    UTAgentConfig.Current.llm.modelId = models[mModelIndex].id;
                }
            }

            UTAgentConfig.Current.llm.maxSteps = mMaxSteps;
            UTAgentConfig.Current.apiKeyEnvVar = string.IsNullOrWhiteSpace(mApiKeyEnvVar)
                ? UTAgentConfig.DefaultApiKeyEnvVar
                : mApiKeyEnvVar.Trim();
            UTAgentConfig.Current.llm.baseUrlOverride = mBaseUrlOverride ?? "";
        }

        private void LoadFromConfig()
        {
            UTAgentConfigDto config = UTAgentConfig.Current;
            mMaxSteps = config.llm.maxSteps;
            mApiKeyEnvVar = config.apiKeyEnvVar;
            mBaseUrlOverride = config.llm.baseUrlOverride ?? "";
            mPythonDll = config.python.dll;
            mBridgeEnabled = config.bridge.enabled;
            mBridgePort = config.bridge.port;
            mLogDirectory = string.IsNullOrWhiteSpace(config.log.directory)
                ? UTAgentSessionLogger.GetDefaultLogDirectory()
                : config.log.directory;

            ProviderDto[] providers = config.providers ?? Array.Empty<ProviderDto>();
            mProviderIndex = 0;
            for (int i = 0; i < providers.Length; i++)
            {
                if (string.Equals(providers[i].id, config.llm.providerId, StringComparison.OrdinalIgnoreCase))
                {
                    mProviderIndex = i;
                    break;
                }
            }

            ModelDto[] models = providers.Length > mProviderIndex
                ? providers[mProviderIndex].models ?? Array.Empty<ModelDto>()
                : Array.Empty<ModelDto>();
            mModelIndex = 0;
            for (int i = 0; i < models.Length; i++)
            {
                if (string.Equals(models[i].id, config.llm.modelId, StringComparison.OrdinalIgnoreCase))
                {
                    mModelIndex = i;
                    break;
                }
            }
        }

        private void ShowStatusFeedback(string prefix, UTAgentReadiness.Status status)
        {
            string message = status.Ready
                ? $"{prefix}，{status.Summary}"
                : $"{prefix}。{status.Summary}：{status.Detail}";
            ShowFeedback(message, status.Ready ? MessageType.Info : MessageType.Warning, 6);
        }

        private void DrawFeedback()
        {
            if (!string.IsNullOrEmpty(mFeedback)
                && EditorApplication.timeSinceStartup < mFeedbackUntil)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(mFeedback, mFeedbackType);
            }
        }

        private void ShowFeedback(string message, MessageType type, double seconds)
        {
            mFeedback = message;
            mFeedbackType = type;
            mFeedbackUntil = EditorApplication.timeSinceStartup + seconds;
        }
    }
}

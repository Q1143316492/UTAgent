using System;
using System.Diagnostics;
using System.IO;
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

        private static readonly string[] sTabLabels = { "Python", "大模型", "CLI", "日志" };

        private int mTab = TabChat;
        private readonly UTAgentRunner mRunner = new UTAgentRunner();

        private int mProviderIndex;
        private int mModelIndex;
        private int mMaxSteps = 25;
        private string mApiKeyEnvVar = UTAgentConfig.DefaultApiKeyEnvVar;
        private string mBaseUrlOverride = "";
        private bool mFoldBaseUrlOverride;
        private bool mFoldPythonAdvanced;
        private bool mUnityAssembliesOnly;

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
            DrawRuntimeStatusIfNeeded();

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

        /// <summary>
        /// 仅在未就绪时提示（常见是缺 API Key）；已就绪不占空间。
        /// </summary>
        private void DrawRuntimeStatusIfNeeded()
        {
            UTAgentReadiness.Status status = UTAgentReadiness.GetChatStatus(mRunner);
            if (status.Ready)
            {
                return;
            }

            EditorGUILayout.HelpBox(UTAgentReadiness.FormatStatusBox(status), MessageType.Warning);
            EditorGUILayout.Space(2);
        }

        private void DrawPythonTab()
        {
            bool homeReady = PythonHomeResolver.IsPythonHomeReady();
            bool engineUp = UTAgentBootstrap.IsAvailable;

            string statusLine;
            if (!homeReady)
            {
                statusLine = "✗ 未安装 PythonHome（将下载到 Assets/UTAgent/PythonHome）";
            }
            else if (engineUp)
            {
                statusLine = "✓ Python 已就绪";
            }
            else
            {
                statusLine = "○ PythonHome 已就绪，引擎未启动";
            }

            EditorGUILayout.LabelField(statusLine, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6);

            string buttonLabel = !homeReady
                ? "下载并初始化"
                : (engineUp ? "重新初始化" : "初始化");

            if (GUILayout.Button(buttonLabel, GUILayout.Height(36)))
            {
                BootstrapAndInitializePython();
            }

            mFoldPythonAdvanced = EditorGUILayout.Foldout(mFoldPythonAdvanced, "高级", true);
            if (mFoldPythonAdvanced)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("路径", PythonHomeResolver.GetDisplayPythonHome());

                EditorGUI.BeginChangeCheck();
                bool unityOnly = EditorGUILayout.ToggleLeft(
                    "仅扫描 Unity 程序集（试验）",
                    mUnityAssembliesOnly);
                if (EditorGUI.EndChangeCheck())
                {
                    mUnityAssembliesOnly = unityOnly;
                    UTAgentConfig.Current.python.unityAssembliesOnly = unityOnly;
                    UTAgentConfig.SaveLocal();
                    ShowFeedback(
                        unityOnly
                            ? "已开启 Unity-only Scan。请「重置引擎」后再初始化；业务 CS.* 将不可用。"
                            : "已关闭 Unity-only Scan。请「重置引擎」后再初始化以恢复全量扫描。",
                        MessageType.Warning,
                        8);
                }

                EditorGUILayout.HelpBox(
                    "默认关闭。开启后 pythonnet 只扫 Unity/系统程序集，可缩短大工程初始化；过滤关键字 InitTiming。",
                    MessageType.None);

                if (GUILayout.Button("重置引擎", GUILayout.Height(24)))
                {
                    ResetPython();
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawChatTab()
        {
            EditorGUILayout.LabelField(
                $"日常只需配置 API Key（环境变量 {mApiKeyEnvVar}，不写入 JSON）。",
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
            EditorGUILayout.LabelField(
                "供 Cursor / utagent 命令调用。默认开启；首次打开 Chat 时按此配置启动监听。",
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
            mLogDirectory = EditorGUILayout.TextField("运行产物目录", mLogDirectory);
            EditorGUILayout.LabelField("默认", UTAgentSessionLogger.GetDefaultLogDirectory());
            EditorGUILayout.HelpBox(
                "子目录：logs/（审计）、screenshots/、sessions/、exec/（临时 --file 脚本）",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存", GUILayout.Height(30)))
            {
                SaveLogSettings();
            }

            if (GUILayout.Button("打开产物目录", GUILayout.Height(30)))
            {
                UTAgentSessionLogger.RevealLogDirectory();
            }

            EditorGUILayout.EndHorizontal();
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

        private void BootstrapAndInitializePython()
        {
            if (!PythonHomeResolver.IsPythonHomeReady())
            {
                bool ok = EditorUtility.DisplayDialog(
                    "下载 PythonHome",
                    "包内 PythonHome 尚未就绪。将运行 Install-PythonHome.ps1 下载官方 embeddable CPython 3.12（需要网络）。\n\n继续？",
                    "下载并继续",
                    "取消");
                if (!ok)
                {
                    return;
                }

                if (!RunInstallPythonHomeScript())
                {
                    return;
                }
            }

            // 已可用时 forceReload：Shutdown→冷启动；不可用时走 Initialize（同 dll 可附着，无需无意义 Shutdown）
            bool forceReload = UTAgentBootstrap.IsAvailable;
            UTAgentReadiness.Status status = UTAgentReadiness.ApplyPythonConfigAndInit(
                forceReload: forceReload);
            ShowStatusFeedback(
                status.Ready
                    ? (forceReload ? "已重新初始化" : "Python 环境已就绪")
                    : "初始化未完成",
                status);
            Repaint();
        }

        private bool RunInstallPythonHomeScript()
        {
            string script = PythonHomeResolver.GetInstallPythonHomeScriptPath();
            if (!File.Exists(script))
            {
                ShowFeedback(
                    $"未找到脚本：{script}\n请按 Docs/skills/utagent-env-bootstrap 手动安装。",
                    MessageType.Error,
                    12);
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (Process process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        ShowFeedback(
                            $"无法启动 PowerShell。请手动执行：\n{script}",
                            MessageType.Error,
                            12);
                        return false;
                    }

                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    AssetDatabase.Refresh();

                    if (process.ExitCode != 0 || !PythonHomeResolver.IsPythonHomeReady())
                    {
                        string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                        ShowFeedback(
                            $"下载失败 (exit={process.ExitCode})。\n{detail}\n请手动执行：\n{script}",
                            MessageType.Error,
                            14);
                        return false;
                    }
                }

                ShowFeedback("PythonHome 已安装。", MessageType.Info, 4);
                return true;
            }
            catch (Exception e)
            {
                ShowFeedback(
                    $"无法运行下载脚本：{e.Message}\n请手动执行：\n{script}",
                    MessageType.Error,
                    12);
                return false;
            }
        }

        private void SaveChatSettings()
        {
            SyncChatFieldsToConfig();
            UTAgentConfig.SaveLocal();
            ShowFeedback("大模型设置已保存。", MessageType.Info, 3);
        }

        private void ResetPython()
        {
            try
            {
                UTAgentBootstrap.Shutdown();
                UTAgentReadiness.ClearAppliedSnapshot();
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
            mBridgeEnabled = config.bridge.enabled;
            mBridgePort = config.bridge.port;
            mUnityAssembliesOnly = config.python != null && config.python.unityAssembliesOnly;
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
            if (status.Ready)
            {
                ShowFeedback($"{prefix}，{status.Summary}", MessageType.Info, 6);
                return;
            }

            string detail = string.IsNullOrWhiteSpace(status.Detail)
                ? status.Summary
                : $"{status.Summary}：{status.Detail}";
            ShowFeedback($"{prefix}。{detail}", MessageType.Warning, 10);
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

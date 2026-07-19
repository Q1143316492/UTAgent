using System.IO;
using UnityEditor;
using UnityEngine;

namespace UTAgent.Editor.Config
{
    /// <summary>
    /// 首次打开 Chat 时将旧版 Unity EditorPrefs 键迁入 utagent.local.json（不含 API Key）。
    /// 仅迁移用；运行时配置真源是 <see cref="UTAgentConfig"/> JSON。
    /// </summary>
    public static class UTAgentConfigMigration
    {
        private const string MigratedKey = "UTAgent.ConfigMigrated";

        // 旧键字面量须保持不变，以便本机未迁完的用户仍能灌进 local json
        private const string BridgeEnabledKey = "UTAgent.Bridge_Enabled";
        private const string BridgePortKey = "UTAgent.Bridge_Port";
        private const string AgentApiKeyKey = "UTAgent.Agent_ApiKey";
        private const string AgentBaseUrlKey = "UTAgent.Agent_BaseURL";
        private const string AgentModelKey = "UTAgent.Agent_Model";
        private const string AgentMaxStepsKey = "UTAgent.Agent_MaxSteps";
        private const string AgentLogDirectoryKey = "UTAgent.Agent_LogDirectory";
        private const string PythonHomeKey = "UTAgent.Python_Home";
        private const string PythonDllKey = "UTAgent.Python_Dll";
        private const string LegacyAgentApiKeyKey = "PythonBridge.Agent_ApiKey";
        private const string LegacyAgentBaseUrlKey = "PythonBridge.Agent_BaseURL";
        private const string LegacyAgentModelKey = "PythonBridge.Agent_Model";
        private const string LegacyAgentMaxStepsKey = "PythonBridge.Agent_MaxSteps";
        private const string LegacyAgentLogDirectoryKey = "PythonBridge.Agent_LogDirectory";

        public static bool TryMigrateFromEditorPrefs(UTAgentConfigDto defaults, out bool showLegacyApiKeyWarning)
        {
            showLegacyApiKeyWarning = false;
            if (EditorPrefs.GetBool(MigratedKey, false) && File.Exists(UTAgentConfig.LocalPath))
            {
                return false;
            }

            if (File.Exists(UTAgentConfig.LocalPath))
            {
                EditorPrefs.SetBool(MigratedKey, true);
                return false;
            }

            string legacyApiKey = MigrateString(AgentApiKeyKey, LegacyAgentApiKeyKey);
            if (!string.IsNullOrWhiteSpace(legacyApiKey))
            {
                showLegacyApiKeyWarning = true;
                EditorPrefs.SetString(AgentApiKeyKey, "");
                EditorPrefs.SetString(LegacyAgentApiKeyKey, "");
            }

            var local = new UTAgentConfigDto
            {
                apiKeyEnvVar = defaults.apiKeyEnvVar,
                llm = new LlmDto
                {
                    providerId = defaults.llm.providerId,
                    modelId = MigrateModel(defaults),
                    maxSteps = MigrateInt(
                        AgentMaxStepsKey,
                        LegacyAgentMaxStepsKey,
                        UTAgentConfig.DefaultMaxSteps),
                    baseUrlOverride = MigrateBaseUrlOverride(defaults),
                },
                python = new PythonDto
                {
                    home = EditorPrefs.GetString(PythonHomeKey, ""),
                    dll = ReadPythonDll(),
                },
                bridge = new BridgeDto
                {
                    enabled = EditorPrefs.GetBool(BridgeEnabledKey, false),
                    port = ReadBridgePort(),
                },
                log = new LogDto
                {
                    directory = MigrateString(AgentLogDirectoryKey, LegacyAgentLogDirectoryKey),
                },
            };

            string dir = UTAgentConfig.ConfigDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonUtility.ToJson(local, true);
            File.WriteAllText(UTAgentConfig.LocalPath, json);
            EditorPrefs.SetBool(MigratedKey, true);
            Debug.Log("[UTAgent] 已将旧 EditorPrefs 配置迁移至 Config/utagent.local.json");
            return true;
        }

        private static int ReadBridgePort()
        {
            int port = EditorPrefs.GetInt(BridgePortKey, UTAgentConfig.DefaultBridgePort);
            if (port < 1024 || port > 65535)
            {
                return UTAgentConfig.DefaultBridgePort;
            }

            return port;
        }

        private static string ReadPythonDll()
        {
            string value = EditorPrefs.GetString(PythonDllKey, UTAgentConfig.DefaultPythonDll);
            return string.IsNullOrWhiteSpace(value) ? UTAgentConfig.DefaultPythonDll : value.Trim();
        }

        private static string MigrateModel(UTAgentConfigDto defaults)
        {
            string model = MigrateString(AgentModelKey, LegacyAgentModelKey, defaults.llm.modelId);
            return string.IsNullOrWhiteSpace(model) ? defaults.llm.modelId : model;
        }

        private static string MigrateBaseUrlOverride(UTAgentConfigDto defaults)
        {
            string baseUrl = MigrateString(AgentBaseUrlKey, LegacyAgentBaseUrlKey);
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return "";
            }

            ProviderDto provider = defaults.providers != null && defaults.providers.Length > 0
                ? defaults.providers[0]
                : null;
            if (provider != null
                && string.Equals(baseUrl.Trim(), provider.baseUrl?.Trim(), System.StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return baseUrl.Trim();
        }

        private static string MigrateString(string newKey, string oldKey, string defaultValue = "")
        {
            string value = EditorPrefs.GetString(newKey, "");
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

        private static int MigrateInt(string newKey, string oldKey, int defaultValue)
        {
            if (EditorPrefs.HasKey(newKey))
            {
                return EditorPrefs.GetInt(newKey, defaultValue);
            }

            if (EditorPrefs.HasKey(oldKey))
            {
                int value = EditorPrefs.GetInt(oldKey, defaultValue);
                EditorPrefs.SetInt(newKey, value);
                return value;
            }

            return defaultValue;
        }
    }
}

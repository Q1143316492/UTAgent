using System.IO;
using UnityEditor;
using UnityEngine;
using UTAgent.Editor.Core;

namespace UTAgent.Editor.Config
{
    /// <summary>
    /// 首次打开 Chat 时将 legacy EditorPrefs 迁入 utagent.local.json（不含 API Key）。
    /// </summary>
    public static class UTAgentConfigMigration
    {
        private const string MigratedKey = "UTAgent.ConfigMigrated";

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

            string legacyApiKey = UTAgentPrefs.MigrateString(
                UTAgentPrefs.AgentApiKeyKey,
                UTAgentPrefs.LegacyAgentApiKeyKey);
            if (!string.IsNullOrWhiteSpace(legacyApiKey))
            {
                showLegacyApiKeyWarning = true;
                EditorPrefs.SetString(UTAgentPrefs.AgentApiKeyKey, "");
                EditorPrefs.SetString(UTAgentPrefs.LegacyAgentApiKeyKey, "");
            }

            var local = new UTAgentConfigDto
            {
                apiKeyEnvVar = defaults.apiKeyEnvVar,
                llm = new LlmDto
                {
                    providerId = defaults.llm.providerId,
                    modelId = MigrateModel(defaults),
                    maxSteps = UTAgentPrefs.MigrateInt(
                        UTAgentPrefs.AgentMaxStepsKey,
                        UTAgentPrefs.LegacyAgentMaxStepsKey,
                        UTAgentConfig.DefaultMaxSteps),
                    baseUrlOverride = MigrateBaseUrlOverride(defaults),
                },
                python = new PythonDto
                {
                    home = EditorPrefs.GetString(UTAgentPrefs.PythonHomeKey, ""),
                    dll = ReadPythonDll(),
                },
                bridge = new BridgeDto
                {
                    enabled = EditorPrefs.GetBool(UTAgentPrefs.BridgeEnabledKey, false),
                    port = ReadBridgePort(),
                },
                log = new LogDto
                {
                    directory = UTAgentPrefs.MigrateString(
                        UTAgentPrefs.AgentLogDirectoryKey,
                        UTAgentPrefs.LegacyAgentLogDirectoryKey),
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
            Debug.Log("[UTAgent] 已将 EditorPrefs 配置迁移至 Config/utagent.local.json");
            return true;
        }

        private static int ReadBridgePort()
        {
            int port = EditorPrefs.GetInt(UTAgentPrefs.BridgePortKey, UTAgentPrefs.DefaultBridgePort);
            if (port < 1024 || port > 65535)
            {
                return UTAgentPrefs.DefaultBridgePort;
            }

            return port;
        }

        private static string ReadPythonDll()
        {
            string value = EditorPrefs.GetString(UTAgentPrefs.PythonDllKey, UTAgentPrefs.DefaultPythonDll);
            return string.IsNullOrWhiteSpace(value) ? UTAgentPrefs.DefaultPythonDll : value.Trim();
        }

        private static string MigrateModel(UTAgentConfigDto defaults)
        {
            string model = UTAgentPrefs.MigrateString(
                UTAgentPrefs.AgentModelKey,
                UTAgentPrefs.LegacyAgentModelKey,
                defaults.llm.modelId);
            return string.IsNullOrWhiteSpace(model) ? defaults.llm.modelId : model;
        }

        private static string MigrateBaseUrlOverride(UTAgentConfigDto defaults)
        {
            string baseUrl = UTAgentPrefs.MigrateString(
                UTAgentPrefs.AgentBaseUrlKey,
                UTAgentPrefs.LegacyAgentBaseUrlKey);
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
    }
}

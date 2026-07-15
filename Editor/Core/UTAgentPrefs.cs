using UnityEditor;

namespace UTAgent.Editor.Core
{
    /// <summary>
    /// UTAgent EditorPrefs 配置中心。key 字面量与重构前一致，向后兼容现有配置。
    /// </summary>
    public static class UTAgentPrefs
    {
        public const string BridgeEnabledKey = "UTAgent.Bridge_Enabled";
        public const string BridgePortKey = "UTAgent.Bridge_Port";
        public const int DefaultBridgePort = 17861;

        public const string AgentApiKeyKey = "UTAgent.Agent_ApiKey";
        public const string AgentBaseUrlKey = "UTAgent.Agent_BaseURL";
        public const string AgentModelKey = "UTAgent.Agent_Model";
        public const string AgentMaxStepsKey = "UTAgent.Agent_MaxSteps";
        public const string AgentMaxInputTokensKey = "UTAgent.Agent_MaxInputTokens";
        public const string AgentMinKeepMessagesKey = "UTAgent.Agent_MinKeepMessages";
        public const string AgentLogDirectoryKey = "UTAgent.Agent_LogDirectory";

        public const string PythonHomeKey = "UTAgent.Python_Home";
        public const string PythonDllKey = "UTAgent.Python_Dll";
        public const string DefaultPythonDll = "python312.dll";

        public const string LegacyAgentApiKeyKey = "PythonBridge.Agent_ApiKey";
        public const string LegacyAgentBaseUrlKey = "PythonBridge.Agent_BaseURL";
        public const string LegacyAgentModelKey = "PythonBridge.Agent_Model";
        public const string LegacyAgentMaxStepsKey = "PythonBridge.Agent_MaxSteps";
        public const string LegacyAgentLogDirectoryKey = "PythonBridge.Agent_LogDirectory";

        public const string DefaultModel = "gpt-4o-mini";
        public const int DefaultMaxSteps = 25;
        public const int DefaultMaxInputTokens = 100000;
        public const int DefaultMinKeepMessages = 20;

        public static bool GetBridgeEnabled()
        {
            return EditorPrefs.GetBool(BridgeEnabledKey, false);
        }

        public static void SetBridgeEnabled(bool enabled)
        {
            EditorPrefs.SetBool(BridgeEnabledKey, enabled);
        }

        public static int GetBridgePort()
        {
            int port = EditorPrefs.GetInt(BridgePortKey, DefaultBridgePort);
            if (port < 1024 || port > 65535)
            {
                return DefaultBridgePort;
            }

            return port;
        }

        public static void SetBridgePort(int port)
        {
            EditorPrefs.SetInt(BridgePortKey, port);
        }

        public static string GetAgentApiKey()
        {
            return MigrateString(AgentApiKeyKey, LegacyAgentApiKeyKey);
        }

        public static void SetAgentApiKey(string value)
        {
            EditorPrefs.SetString(AgentApiKeyKey, value ?? "");
        }

        public static string GetAgentBaseUrl()
        {
            return MigrateString(AgentBaseUrlKey, LegacyAgentBaseUrlKey);
        }

        public static void SetAgentBaseUrl(string value)
        {
            EditorPrefs.SetString(AgentBaseUrlKey, value ?? "");
        }

        public static string GetAgentModel()
        {
            string value = MigrateString(AgentModelKey, LegacyAgentModelKey, DefaultModel);
            return string.IsNullOrEmpty(value) ? DefaultModel : value;
        }

        public static void SetAgentModel(string value)
        {
            EditorPrefs.SetString(AgentModelKey, value ?? DefaultModel);
        }

        public static int GetAgentMaxSteps()
        {
            return MigrateInt(AgentMaxStepsKey, LegacyAgentMaxStepsKey, DefaultMaxSteps);
        }

        public static void SetAgentMaxSteps(int value)
        {
            EditorPrefs.SetInt(AgentMaxStepsKey, value);
        }

        public static int GetAgentMaxInputTokens()
        {
            return EditorPrefs.GetInt(AgentMaxInputTokensKey, DefaultMaxInputTokens);
        }

        public static int GetAgentMinKeepMessages()
        {
            return EditorPrefs.GetInt(AgentMinKeepMessagesKey, DefaultMinKeepMessages);
        }

        public static string GetAgentLogDirectory()
        {
            return MigrateString(AgentLogDirectoryKey, LegacyAgentLogDirectoryKey);
        }

        public static void SetAgentLogDirectory(string value)
        {
            EditorPrefs.SetString(AgentLogDirectoryKey, value ?? "");
        }

        public static string GetPythonHome()
        {
            return EditorPrefs.GetString(PythonHomeKey, "");
        }

        public static void SetPythonHome(string value)
        {
            EditorPrefs.SetString(PythonHomeKey, value ?? "");
        }

        public static string GetPythonDll()
        {
            string value = EditorPrefs.GetString(PythonDllKey, DefaultPythonDll);
            return string.IsNullOrWhiteSpace(value) ? DefaultPythonDll : value.Trim();
        }

        public static void SetPythonDll(string value)
        {
            EditorPrefs.SetString(PythonDllKey, string.IsNullOrWhiteSpace(value) ? DefaultPythonDll : value.Trim());
        }

        public static string MigrateString(string newKey, string oldKey, string defaultValue = "")
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

        public static int MigrateInt(string newKey, string oldKey, int defaultValue)
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

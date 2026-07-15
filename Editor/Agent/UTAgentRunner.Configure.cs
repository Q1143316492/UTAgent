using UTAgent.Editor.Core;

namespace UTAgent.Editor.Agent
{
    public sealed partial class UTAgentRunner
    {
        /// <summary>
        /// 从 <see cref="UTAgentPrefs"/> 读取 Agent 配置并调用 <see cref="Configure"/>（含 legacy key 迁移）。
        /// </summary>
        public string ConfigureFromPrefs()
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                mConfigured = false;
                return "[Runner] 引擎不可用，请先初始化";
            }

            string apiKey = UTAgentPrefs.MigrateString(
                UTAgentPrefs.AgentApiKeyKey,
                UTAgentPrefs.LegacyAgentApiKeyKey);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                mConfigured = false;
                return "[Runner] 未配置 API Key";
            }

            string baseUrl = UTAgentPrefs.MigrateString(
                UTAgentPrefs.AgentBaseUrlKey,
                UTAgentPrefs.LegacyAgentBaseUrlKey);
            string model = UTAgentPrefs.MigrateString(
                UTAgentPrefs.AgentModelKey,
                UTAgentPrefs.LegacyAgentModelKey,
                UTAgentPrefs.DefaultModel);
            int maxSteps = UTAgentPrefs.MigrateInt(
                UTAgentPrefs.AgentMaxStepsKey,
                UTAgentPrefs.LegacyAgentMaxStepsKey,
                UTAgentPrefs.DefaultMaxSteps);

            string logDir = UTAgentPrefs.MigrateString(
                UTAgentPrefs.AgentLogDirectoryKey,
                UTAgentPrefs.LegacyAgentLogDirectoryKey);
            UTAgentSessionLogger.EnsureLogDirectory(
                string.IsNullOrWhiteSpace(logDir) ? null : logDir);

            return Configure(apiKey, baseUrl, model, maxSteps);
        }
    }
}

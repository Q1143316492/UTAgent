using UTAgent.Editor.Config;
using UTAgent.Editor.Core;

namespace UTAgent.Editor.Agent
{
    public sealed partial class UTAgentRunner
    {
        /// <summary>
        /// 从 <see cref="UTAgentConfig"/> 与环境变量读取 Agent 配置并调用 <see cref="Configure"/>。
        /// </summary>
        public string ConfigureFromConfig()
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                mConfigured = false;
                return "[Runner] 引擎不可用，请先在 Settings → Python 初始化引擎";
            }

            string apiKey = UTAgentConfig.ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                mConfigured = false;
                string envName = UTAgentConfig.Current.apiKeyEnvVar;
                return $"[Runner] 未设置环境变量 {envName}";
            }

            string baseUrl = UTAgentConfig.ResolveBaseUrl();
            string model = UTAgentConfig.ResolveModelId();
            int maxSteps = UTAgentConfig.ResolveMaxSteps();

            UTAgentSessionLogger.EnsureLogDirectory(UTAgentConfig.ResolveLogDirectory());

            return Configure(apiKey, baseUrl, model, maxSteps);
        }

        /// <summary>
        /// 兼容旧调用；请改用 <see cref="ConfigureFromConfig"/>。
        /// </summary>
        public string ConfigureFromPrefs()
        {
            return ConfigureFromConfig();
        }
    }
}

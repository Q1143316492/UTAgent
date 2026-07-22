using System;

namespace UTAgent.Editor.Config
{
    [Serializable]
    public sealed class UTAgentConfigDto
    {
        public string apiKeyEnvVar = "UTAGENT_API_KEY";
        public ProviderDto[] providers;
        public LlmDto llm = new LlmDto();
        public PythonDto python = new PythonDto();
        public BridgeDto bridge = new BridgeDto();
        public LogDto log = new LogDto();
    }

    [Serializable]
    public sealed class ProviderDto
    {
        public string id;
        public string displayName;
        public string baseUrl;
        public ModelDto[] models;
    }

    [Serializable]
    public sealed class ModelDto
    {
        public string id;
        public string displayName;
        /// <summary>模型上下文窗口（tokens）。0 表示用启发式：deepseek→1M，其它→200k。</summary>
        public int contextWindow;
    }

    [Serializable]
    public sealed class LlmDto
    {
        public string providerId = "deepseek";
        public string modelId = "deepseek-v4-flash";
        public int maxSteps = 25;
        public string baseUrlOverride = "";
        public bool compactionEnabled = true;
        /// <summary>
        /// 压缩触发预算占 contextWindow 的百分比（1–100）。0 表示默认 75。
        /// 预留余量给本轮输出 / thinking。
        /// </summary>
        public int compactionInputPercent = 75;
        /// <summary>手动覆盖压缩触发 token 预算；0 表示按 contextWindow × percent 计算。</summary>
        public int maxInputTokensOverride = 0;
        /// <summary>
        /// after-tool 截断：tool result content 超过该字符数则截断。0 = 禁用；出厂默认 8000。
        /// </summary>
        public int afterToolTruncateChars = 8000;
        /// <summary>after-tool 无进展矫正总开关；默认关闭便于迭代。</summary>
        public bool noProgressEnabled;
        /// <summary>连续纯侦察步数阈值；默认 3。</summary>
        public int noProgressStreak = 3;
    }

    [Serializable]
    public sealed class PythonDto
    {
        public string home = "";
        public string dll = "python312.dll";
        /// <summary>
        /// 脚本域重载前是否轻量关闭嵌入式引擎。默认 false（避免拖慢 Reload）；
        /// 开启后走 Finalize 快路径，非全量 Shutdown。JsonUtility 缺字段时靠 Merge 保默认。
        /// </summary>
        public bool shutdownBeforeDomainReload;
    }

    [Serializable]
    public sealed class BridgeDto
    {
        public bool enabled = true;
        public int port = 17861;
    }

    [Serializable]
    public sealed class LogDto
    {
        public string directory = "";
    }
}

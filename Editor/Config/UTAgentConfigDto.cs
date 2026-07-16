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
    }

    [Serializable]
        public sealed class LlmDto
        {
            public string providerId = "deepseek";
            public string modelId = "deepseek-v4-flash";
            public int maxSteps = 25;
            public string baseUrlOverride = "";
            public bool compactionEnabled = true;
        }

    [Serializable]
    public sealed class PythonDto
    {
        public string home = "";
        public string dll = "python312.dll";
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

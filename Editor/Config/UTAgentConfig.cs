using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UTAgent.Editor.Core;
using UTAgent.Editor.RemoteCli;

namespace UTAgent.Editor.Config
{
    /// <summary>
    /// UTAgent 配置中心：defaults + local JSON 合并；API Key 仅从环境变量读取。
    /// 不在 Editor 启动时加载；打开 Chat / Settings 时再读。
    /// </summary>
    public static class UTAgentConfig
    {
        public const string DefaultApiKeyEnvVar = "UTAGENT_API_KEY";
        public const string DefaultPythonDll = "python312.dll";
        public const int DefaultBridgePort = 17861;
        public const int DefaultMaxSteps = 25;

        private static UTAgentConfigDto mCurrent;
        private static bool mLoaded;
        private static bool mShowLegacyApiKeyWarning;

        public static UTAgentConfigDto Current
        {
            get
            {
                EnsureLoaded();
                return mCurrent;
            }
        }

        public static bool ShowLegacyApiKeyWarning => mShowLegacyApiKeyWarning;

        public static void EnsureLoaded()
        {
            if (mLoaded)
            {
                return;
            }

            Load();
        }

        public static void Reload()
        {
            mLoaded = false;
            Load();
        }

        private static void Load()
        {
            UTAgentConfigDto defaults = ReadJsonFile(DefaultsPath);
            if (defaults == null)
            {
                defaults = CreateBuiltinDefaults();
            }

            UTAgentConfigDto local = ReadJsonFile(LocalPath);
            mCurrent = Merge(defaults, local);
            NormalizeCurrent(mCurrent);
            mLoaded = true;
        }

        /// <summary>
        /// 打开 Chat 时：必要时做 EditorPrefs 迁移并刷新配置。
        /// </summary>
        public static void PrepareForChat()
        {
            EnsureLoaded();
            if (!File.Exists(LocalPath))
            {
                UTAgentConfigDto defaults = ReadJsonFile(DefaultsPath) ?? CreateBuiltinDefaults();
                if (UTAgentConfigMigration.TryMigrateFromEditorPrefs(defaults, out bool showWarning))
                {
                    mShowLegacyApiKeyWarning = showWarning;
                    if (showWarning)
                    {
                        Debug.LogWarning(
                            "[UTAgent] 旧版 API Key 已从 EditorPrefs 清除，请设置环境变量 " +
                            $"{defaults.apiKeyEnvVar}。");
                    }

                    Reload();
                }
            }

            UTAgentEditorHttpServer.EnsureMatchesConfig();
        }

        public static void SaveLocal()
        {
            EnsureLoaded();
            string dir = ConfigDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var local = new UTAgentConfigDto
            {
                apiKeyEnvVar = mCurrent.apiKeyEnvVar,
                llm = CloneLlm(mCurrent.llm),
                python = ClonePython(mCurrent.python),
                bridge = CloneBridge(mCurrent.bridge),
                log = CloneLog(mCurrent.log),
            };

            string json = JsonUtility.ToJson(local, true);
            File.WriteAllText(LocalPath, json);
        }

        public static string ResolveApiKey()
        {
            return ReadEnvironmentVariable(GetEffectiveApiKeyEnvVarName(null));
        }

        /// <summary>
        /// 检查环境变量是否已设置（不暴露 key 内容）。<paramref name="envVarNameOverride"/> 为 UI 未保存前的临时名。
        /// </summary>
        public static bool TryCheckApiKey(out string message)
        {
            return TryCheckApiKey(null, out message);
        }

        public static bool TryCheckApiKey(string envVarNameOverride, out string message)
        {
            string envName = GetEffectiveApiKeyEnvVarName(envVarNameOverride);
            string value = ReadEnvironmentVariable(envName);
            if (string.IsNullOrWhiteSpace(value))
            {
                message =
                    $"未找到环境变量 {envName}（已查：当前进程 / 用户 / 系统）。\n" +
                    "若在 Windows「用户变量」里刚添加，需完全退出并重启 Unity Editor 后进程才会继承；" +
                    "用户级变量在未重启前仍可从注册表读到，若本条仍失败请核对变量名。";
                return false;
            }

            string source = DescribeEnvironmentVariableSource(envName);
            message = $"已设置 {envName}（长度 {value.Length}，来源：{source}，内容不显示）";
            return true;
        }

        /// <summary>
        /// 依次读 Process → User → Machine（Windows 用户变量在未重启 Unity 时通常只在 User 级可读）。
        /// </summary>
        public static string ReadEnvironmentVariable(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string trimmed = name.Trim();
            string fromProcess = Environment.GetEnvironmentVariable(trimmed, EnvironmentVariableTarget.Process);
            if (!string.IsNullOrWhiteSpace(fromProcess))
            {
                return fromProcess;
            }

            string fromUser = Environment.GetEnvironmentVariable(trimmed, EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(fromUser))
            {
                return fromUser;
            }

            string fromMachine = Environment.GetEnvironmentVariable(trimmed, EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrWhiteSpace(fromMachine))
            {
                return fromMachine;
            }

            return null;
        }

        private static string GetEffectiveApiKeyEnvVarName(string envVarNameOverride)
        {
            if (!string.IsNullOrWhiteSpace(envVarNameOverride))
            {
                return envVarNameOverride.Trim();
            }

            string fromConfig = Current.apiKeyEnvVar;
            if (!string.IsNullOrWhiteSpace(fromConfig))
            {
                return fromConfig.Trim();
            }

            return DefaultApiKeyEnvVar;
        }

        private static string DescribeEnvironmentVariableSource(string name)
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)))
            {
                return "当前 Unity 进程";
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)))
            {
                return "Windows 用户变量";
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine)))
            {
                return "Windows 系统变量";
            }

            return "未知";
        }

        public static string ResolveBaseUrl()
        {
            LlmDto llm = Current.llm;
            if (!string.IsNullOrWhiteSpace(llm.baseUrlOverride))
            {
                return llm.baseUrlOverride.Trim();
            }

            ProviderDto provider = FindProvider(llm.providerId);
            if (provider != null && !string.IsNullOrWhiteSpace(provider.baseUrl))
            {
                return provider.baseUrl.Trim();
            }

            return "";
        }

        public static string ResolveModelId()
        {
            string model = Current.llm?.modelId;
            if (string.IsNullOrWhiteSpace(model))
            {
                return "deepseek-v4-flash";
            }

            return model.Trim();
        }

        public static int ResolveMaxSteps()
        {
            int steps = Current.llm?.maxSteps ?? DefaultMaxSteps;
            if (steps < 1)
            {
                return DefaultMaxSteps;
            }

            return steps;
        }

        public static string ResolvePythonHomeFromConfig()
        {
            string home = Current.python?.home;
            if (string.IsNullOrWhiteSpace(home))
            {
                return "";
            }

            return home.Trim();
        }

        public static string ResolvePythonDll()
        {
            string dll = Current.python?.dll;
            if (string.IsNullOrWhiteSpace(dll))
            {
                return DefaultPythonDll;
            }

            return dll.Trim();
        }

        public static string ResolveLogDirectory()
        {
            string configured = Current.log?.directory;
            if (string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(PythonPathConfig.DefaultLogDirectory);
            }

            return Path.GetFullPath(configured.Trim());
        }

        public static ProviderDto FindProvider(string providerId)
        {
            if (Current.providers == null || string.IsNullOrWhiteSpace(providerId))
            {
                return null;
            }

            return Current.providers.FirstOrDefault(p =>
                string.Equals(p.id, providerId, StringComparison.OrdinalIgnoreCase));
        }

        public static ModelDto[] GetModelsForProvider(string providerId)
        {
            ProviderDto provider = FindProvider(providerId);
            return provider?.models ?? Array.Empty<ModelDto>();
        }

        public static string ConfigDirectory =>
            Path.Combine(Application.dataPath, "UTAgent", "Config");

        public static string DefaultsPath =>
            Path.Combine(ConfigDirectory, "utagent.defaults.json");

        public static string LocalPath =>
            Path.Combine(ConfigDirectory, "utagent.local.json");

        private static UTAgentConfigDto ReadJsonFile(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JsonUtility.FromJson<UTAgentConfigDto>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[UTAgent] 解析配置失败 {path}: {e.Message}");
                return null;
            }
        }

        private static UTAgentConfigDto Merge(UTAgentConfigDto defaults, UTAgentConfigDto local)
        {
            var merged = CloneRoot(defaults);
            if (local == null)
            {
                return merged;
            }

            if (!string.IsNullOrWhiteSpace(local.apiKeyEnvVar))
            {
                merged.apiKeyEnvVar = local.apiKeyEnvVar.Trim();
            }

            if (local.llm != null)
            {
                MergeLlm(merged.llm, local.llm);
            }

            if (local.python != null)
            {
                MergePython(merged.python, local.python);
            }

            if (local.bridge != null)
            {
                MergeBridge(merged.bridge, local.bridge);
            }

            if (local.log != null)
            {
                MergeLog(merged.log, local.log);
            }

            return merged;
        }

        private static void NormalizeCurrent(UTAgentConfigDto config)
        {
            if (config.llm == null)
            {
                config.llm = new LlmDto();
            }

            if (config.python == null)
            {
                config.python = new PythonDto();
            }

            if (config.bridge == null)
            {
                config.bridge = new BridgeDto();
            }

            if (config.log == null)
            {
                config.log = new LogDto();
            }

            if (config.bridge.port < 1024 || config.bridge.port > 65535)
            {
                config.bridge.port = DefaultBridgePort;
            }

            if (string.IsNullOrWhiteSpace(config.python.dll))
            {
                config.python.dll = DefaultPythonDll;
            }
        }

        private static UTAgentConfigDto CreateBuiltinDefaults()
        {
            return new UTAgentConfigDto
            {
                apiKeyEnvVar = DefaultApiKeyEnvVar,
                providers = new[]
                {
                    new ProviderDto
                    {
                        id = "deepseek",
                        displayName = "DeepSeek",
                        baseUrl = "https://api.deepseek.com",
                        models = new[]
                        {
                            new ModelDto { id = "deepseek-v4-flash", displayName = "V4 Flash" },
                            new ModelDto { id = "deepseek-v4-pro", displayName = "V4 Pro" },
                        },
                    },
                },
                llm = new LlmDto(),
                python = new PythonDto(),
                bridge = new BridgeDto { enabled = true },
                log = new LogDto(),
            };
        }

        private static UTAgentConfigDto CloneRoot(UTAgentConfigDto source)
        {
            return new UTAgentConfigDto
            {
                apiKeyEnvVar = source.apiKeyEnvVar,
                providers = source.providers,
                llm = CloneLlm(source.llm),
                python = ClonePython(source.python),
                bridge = CloneBridge(source.bridge),
                log = CloneLog(source.log),
            };
        }

        private static LlmDto CloneLlm(LlmDto source)
        {
            if (source == null)
            {
                return new LlmDto();
            }

            return new LlmDto
            {
                providerId = source.providerId,
                modelId = source.modelId,
                maxSteps = source.maxSteps,
                baseUrlOverride = source.baseUrlOverride,
            };
        }

        private static PythonDto ClonePython(PythonDto source)
        {
            if (source == null)
            {
                return new PythonDto();
            }

            return new PythonDto
            {
                home = source.home,
                dll = source.dll,
            };
        }

        private static BridgeDto CloneBridge(BridgeDto source)
        {
            if (source == null)
            {
                return new BridgeDto();
            }

            return new BridgeDto
            {
                enabled = source.enabled,
                port = source.port,
            };
        }

        private static LogDto CloneLog(LogDto source)
        {
            if (source == null)
            {
                return new LogDto();
            }

            return new LogDto
            {
                directory = source.directory,
            };
        }

        private static void MergeLlm(LlmDto target, LlmDto local)
        {
            if (!string.IsNullOrWhiteSpace(local.providerId))
            {
                target.providerId = local.providerId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(local.modelId))
            {
                target.modelId = local.modelId.Trim();
            }

            if (local.maxSteps > 0)
            {
                target.maxSteps = local.maxSteps;
            }

            if (local.baseUrlOverride != null)
            {
                target.baseUrlOverride = local.baseUrlOverride;
            }
        }

        private static void MergePython(PythonDto target, PythonDto local)
        {
            if (local.home != null)
            {
                target.home = local.home;
            }

            if (!string.IsNullOrWhiteSpace(local.dll))
            {
                target.dll = local.dll.Trim();
            }
        }

        private static void MergeBridge(BridgeDto target, BridgeDto local)
        {
            target.enabled = local.enabled;
            if (local.port >= 1024 && local.port <= 65535)
            {
                target.port = local.port;
            }
        }

        private static void MergeLog(LogDto target, LogDto local)
        {
            if (local.directory != null)
            {
                target.directory = local.directory;
            }
        }
    }
}

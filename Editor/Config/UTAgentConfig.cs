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
        public const int DefaultDeepSeekContextWindow = 1000000;
        public const int DefaultGenericContextWindow = 200000;
        public const int DefaultCompactionInputPercent = 75;
        public const int MinInputTokenBudget = 8000;

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

            string localRaw = "";
            UTAgentConfigDto local = null;
            if (File.Exists(LocalPath))
            {
                try
                {
                    localRaw = File.ReadAllText(LocalPath) ?? "";
                    local = JsonUtility.FromJson<UTAgentConfigDto>(localRaw);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UTAgentConfig] 读取 local 失败：{e.Message}");
                }
            }

            mCurrent = Merge(defaults, local, localRaw);
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

            // 不再持久化外部 home；旧 local 中的路径在下次保存时清掉
            if (local.python != null)
            {
                local.python.home = "";
                mCurrent.python.home = "";
            }

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

        /// <summary>
        /// after-tool 截断阈值（字符）。0 = 禁用；出厂默认 8000（产品开启）。
        /// </summary>
        public static int ResolveAfterToolTruncateChars()
        {
            int n = Current.llm?.afterToolTruncateChars ?? 8000;
            if (n < 0)
            {
                return 0;
            }

            return n;
        }

        /// <summary>after-tool 无进展矫正是否开启（默认 false）。</summary>
        public static bool ResolveNoProgressEnabled()
        {
            return Current.llm != null && Current.llm.noProgressEnabled;
        }

        /// <summary>无进展连续纯侦察阈值（至少 1；默认 3）。</summary>
        public static int ResolveNoProgressStreak()
        {
            int n = Current.llm?.noProgressStreak ?? 3;
            if (n < 1)
            {
                return 3;
            }

            return n;
        }

        /// <summary>
        /// 是否启用 LLM 摘要 compaction（超 token 预算时）；关闭则直接静态 emergency trim。
        /// </summary>
        public static bool ResolveCompactionEnabled()
        {
            return Current.llm == null || Current.llm.compactionEnabled;
        }

        /// <summary>
        /// 当前模型上下文窗口（tokens）。优先 model.contextWindow；否则启发式（deepseek→1M，其它→200k）。
        /// </summary>
        public static int ResolveContextWindow()
        {
            string modelId = ResolveModelId();
            string providerId = Current.llm?.providerId;
            ModelDto model = FindModel(providerId, modelId);
            if (model != null && model.contextWindow > 0)
            {
                return model.contextWindow;
            }

            return InferContextWindow(modelId, providerId);
        }

        /// <summary>
        /// compaction 触发用的 input token 预算（发给 prepare 的 max_input_tokens）。
        /// 优先 <c>llm.maxInputTokensOverride</c>；否则 <c>contextWindow × compactionInputPercent%</c>（默认 75%）。
        /// </summary>
        public static int ResolveMaxInputTokens()
        {
            LlmDto llm = Current.llm;
            if (llm != null && llm.maxInputTokensOverride > 0)
            {
                return llm.maxInputTokensOverride;
            }

            int window = ResolveContextWindow();
            int percent = llm?.compactionInputPercent ?? 0;
            if (percent <= 0 || percent > 100)
            {
                percent = DefaultCompactionInputPercent;
            }

            long budget = (long)window * percent / 100L;
            if (budget < MinInputTokenBudget)
            {
                budget = MinInputTokenBudget;
            }

            if (budget > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)budget;
        }

        public static ModelDto FindModel(string providerId, string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return null;
            }

            ModelDto[] models = GetModelsForProvider(providerId);
            for (int i = 0; i < models.Length; i++)
            {
                ModelDto m = models[i];
                if (m != null && string.Equals(m.id, modelId, StringComparison.OrdinalIgnoreCase))
                {
                    return m;
                }
            }

            return null;
        }

        /// <summary>无配置时的上下文窗口启发式。</summary>
        public static int InferContextWindow(string modelId, string providerId)
        {
            string m = (modelId ?? "").Trim().ToLowerInvariant();
            string p = (providerId ?? "").Trim().ToLowerInvariant();
            if (m.Contains("deepseek") || p.Contains("deepseek"))
            {
                return DefaultDeepSeekContextWindow;
            }

            return DefaultGenericContextWindow;
        }

        /// <summary>
        /// 已废弃：运行时只认包内 PythonHome，忽略 json 中的 <c>python.home</c>。
        /// </summary>
        public static string ResolvePythonHomeFromConfig()
        {
            return "";
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

        private static UTAgentConfigDto Merge(
            UTAgentConfigDto defaults,
            UTAgentConfigDto local,
            string localRaw = "")
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
                MergeLlm(merged.llm, local.llm, localRaw);
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
                            new ModelDto
                            {
                                id = "deepseek-v4-flash",
                                displayName = "V4 Flash",
                                contextWindow = DefaultDeepSeekContextWindow,
                            },
                            new ModelDto
                            {
                                id = "deepseek-v4-pro",
                                displayName = "V4 Pro",
                                contextWindow = DefaultDeepSeekContextWindow,
                            },
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
                compactionEnabled = source.compactionEnabled,
                compactionInputPercent = source.compactionInputPercent,
                maxInputTokensOverride = source.maxInputTokensOverride,
                afterToolTruncateChars = source.afterToolTruncateChars,
                noProgressEnabled = source.noProgressEnabled,
                noProgressStreak = source.noProgressStreak,
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

        private static void MergeLlm(LlmDto target, LlmDto local, string localRaw = "")
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

            // JsonUtility 缺字段时 bool=false；仅 local 显式写出 compactionEnabled 时覆盖
            if (!string.IsNullOrEmpty(localRaw) &&
                localRaw.IndexOf("\"compactionEnabled\"", StringComparison.Ordinal) >= 0)
            {
                target.compactionEnabled = local.compactionEnabled;
            }

            if (!string.IsNullOrEmpty(localRaw) &&
                localRaw.IndexOf("\"compactionInputPercent\"", StringComparison.Ordinal) >= 0 &&
                local.compactionInputPercent > 0)
            {
                target.compactionInputPercent = local.compactionInputPercent;
            }

            if (!string.IsNullOrEmpty(localRaw) &&
                localRaw.IndexOf("\"maxInputTokensOverride\"", StringComparison.Ordinal) >= 0 &&
                local.maxInputTokensOverride > 0)
            {
                target.maxInputTokensOverride = local.maxInputTokensOverride;
            }

            // 允许 local 显式写 0 关闭；仅当 JSON 含字段时覆盖
            if (!string.IsNullOrEmpty(localRaw) &&
                localRaw.IndexOf("\"afterToolTruncateChars\"", StringComparison.Ordinal) >= 0)
            {
                target.afterToolTruncateChars = local.afterToolTruncateChars < 0
                    ? 0
                    : local.afterToolTruncateChars;
            }

            if (!string.IsNullOrEmpty(localRaw) &&
                localRaw.IndexOf("\"noProgressEnabled\"", StringComparison.Ordinal) >= 0)
            {
                target.noProgressEnabled = local.noProgressEnabled;
            }

            if (!string.IsNullOrEmpty(localRaw) &&
                localRaw.IndexOf("\"noProgressStreak\"", StringComparison.Ordinal) >= 0 &&
                local.noProgressStreak > 0)
            {
                target.noProgressStreak = local.noProgressStreak;
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

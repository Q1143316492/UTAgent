using System;
using System.IO;
using System.Text.RegularExpressions;
using UTAgent.Editor.Config;

namespace UTAgent.Editor.Core
{
    /// <summary>
    /// L1 共享执行策略：与 Chat history 无关的代码守卫。
    /// Chat（before-exec）与 CLI（POST /exec）共用同一判定。
    /// </summary>
    public static class UTAgentExecPolicy
    {
        /// <summary>
        /// 单步代码体积上限（防 ReAct 雪球长脚本）。
        /// 调限须先观测 Out/logs/exec_policy_*.log；本常量勿对齐模型上下文窗口。
        /// </summary>
        public const int CodeSizeLimit = 4000;

        /// <summary>
        /// 重型全量反射：GetComponents(typeof(Component)) / GetComponents(...Component) 及 InParent/InChildren。
        /// </summary>
        private static readonly Regex sHeavyReflection = new Regex(
            @"GetComponents(?:InParent|InChildren)?\(\s*(?:typeof\s*\(\s*Component\s*\)|(?:[A-Za-z_][A-Za-z0-9_.]*\.)?Component\s*)(?:,[^)]*)?\)",
            RegexOptions.Compiled);

        /// <summary>
        /// 危险磁盘递归：os.walk(…)。
        /// </summary>
        private static readonly Regex sOsWalk = new Regex(@"os\.walk\s*\(", RegexOptions.Compiled);

        /// <summary>
        /// 危险磁盘递归：Path.rglob(…) / .rglob(…)。
        /// </summary>
        private static readonly Regex sPathRglob = new Regex(@"\.rglob\s*\(", RegexOptions.Compiled);

        /// <summary>
        /// 危险磁盘递归：glob.glob/iglob(…, recursive=True)。
        /// </summary>
        private static readonly Regex sGlobRecursive = new Regex(
            @"glob\.(?:glob|iglob)\s*\([^)]*recursive\s*=\s*True",
            RegexOptions.Compiled);

        /// <summary>
        /// 共享策略命中结果。Allowed=true 表示 L1 放行（仍可能被 Chat 域规则拦截）。
        /// </summary>
        public readonly struct Result
        {
            public Result(bool allowed, string domain, string message)
            {
                Allowed = allowed;
                Domain = domain ?? string.Empty;
                Message = message ?? string.Empty;
            }

            public bool Allowed { get; }

            /// <summary>
            /// 策略域名：code-too-long / heavy-reflection / fs-walk。
            /// </summary>
            public string Domain { get; }

            /// <summary>
            /// 给人/LLM 看的拒绝说明（中文）。
            /// </summary>
            public string Message { get; }
        }

        /// <summary>
        /// 评估 L1 共享策略。空 code 视为放行。
        /// </summary>
        public static Result EvaluateShared(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return new Result(true, string.Empty, string.Empty);
            }

            int n = code.Length;
            if (n > CodeSizeLimit)
            {
                return new Result(
                    false,
                    "code-too-long",
                    $"单步代码过长（{n} chars > {CodeSizeLimit}），拆成小步或用原语（add_to_layout / add_free_child）。");
            }

            if (sHeavyReflection.IsMatch(code))
            {
                return new Result(
                    false,
                    "heavy-reflection",
                    "禁止全量反射 GetComponents(typeof(Component))，用 describe_go 或指定具体类型（如 GetComponents(Image)）。");
            }

            if (sOsWalk.IsMatch(code) || sPathRglob.IsMatch(code) || sGlobRecursive.IsMatch(code))
            {
                return new Result(
                    false,
                    "fs-walk",
                    "禁止 os.walk / Path.rglob / 递归 glob 扫描磁盘（易卡住编辑器）。查找资源请用 CS.UnityEditor.AssetDatabase.FindAssets 或 LoadAssetAtPath。");
            }

            return new Result(true, string.Empty, string.Empty);
        }

        /// <summary>
        /// 将 L1 拦截写入 Out/logs/exec_policy_yyyyMMdd.log，便于按日汇总（不调限前先观测）。
        /// source：cli | chat
        /// </summary>
        public static void RecordBlock(string domain, int codeChars, string source)
        {
            if (string.IsNullOrEmpty(domain))
            {
                return;
            }

            try
            {
                string root = UTAgentConfig.ResolveLogDirectory();
                string logsDir = Path.Combine(root, "logs");
                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }

                string path = Path.Combine(logsDir, $"exec_policy_{DateTime.Now:yyyyMMdd}.log");
                string line =
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t[exec-policy]\tdomain={domain}\tchars={codeChars}\tsource={source ?? "?"}\tlimit={CodeSizeLimit}";
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // 审计失败不影响主路径
            }
        }
    }
}

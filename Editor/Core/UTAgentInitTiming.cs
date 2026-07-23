using UnityEngine;

namespace UTAgent.Editor.Core
{
    /// <summary>
    /// Soft-reattach / Initialize 分段耗时日志。Console 过滤：<c>[UTAgent][InitTiming]</c>。
    /// </summary>
    internal static class UTAgentInitTiming
    {
        public const string Tag = "[UTAgent][InitTiming]";

        public static void Log(string stage, long ms)
        {
            Debug.Log($"{Tag} {stage} ms={ms}");
        }

        public static void Log(string stage, long ms, string extra)
        {
            if (string.IsNullOrEmpty(extra))
            {
                Log(stage, ms);
                return;
            }

            Debug.Log($"{Tag} {stage} ms={ms} {extra}");
        }

        public static void LogInfo(string stage, string detail)
        {
            Debug.Log($"{Tag} {stage} {detail}");
        }
    }
}

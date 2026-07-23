using System;
using UnityEngine;

namespace UTAgent.Editor.Core
{
    /// <summary>
    /// 将配置中的 Unity-only Scan 开关注入 pythonnet（环境变量，须在 Initialize 前调用）。
    /// </summary>
    internal static class PythonnetScanAllowlist
    {
        public const string EnvVarName = "PYTHONNET_UNITY_ASSEMBLIES_ONLY";

        /// <summary>
        /// 按 <c>python.unityAssembliesOnly</c> 设置环境变量；默认关闭写 <c>0</c>。
        /// </summary>
        public static void ApplyFromConfig(bool unityAssembliesOnly)
        {
            string value = unityAssembliesOnly ? "1" : "0";
            Environment.SetEnvironmentVariable(EnvVarName, value);
            Debug.Log(
                $"[UTAgent][InitTiming] unity_assemblies_only={unityAssembliesOnly} env={EnvVarName}={value}");
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace UTAgent.Editor.CsharpExec
{
    /// <summary>
    /// 不依赖 LLM 的 Emit 冒烟菜单（受 <see cref="CsharpEmitExec.Enabled"/> 控制）。
    /// </summary>
    public static class CsharpEmitSpikeMenu
    {
        private const string MenuPath = "UTAgent/Spike/Roslyn Emit Exec";

        [MenuItem(MenuPath, false, 1000)]
        public static void RunSmoke()
        {
            if (!CsharpEmitExec.Enabled)
            {
                Debug.LogWarning(
                    "[CsharpEmitSpike] 已关闭：将 CsharpEmitExec.Enabled 改为 true 后重编译即可开启");
                return;
            }

            var (output, error) = CsharpEmitExec.Run(CsharpEmitExec.SmokeSource);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError("[CsharpEmitSpike] " + error);
                return;
            }

            Debug.Log("[CsharpEmitSpike] ok, created GO name=" + output);
        }

        [MenuItem(MenuPath, true)]
        public static bool RunSmokeValidate()
        {
            return CsharpEmitExec.Enabled;
        }
    }
}

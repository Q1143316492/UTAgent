using UnityEngine;

namespace UTAgent
{
    /// <summary>
    /// 挂在 Boot 场景任意常驻物体：进入 Play 时初始化 Python 引擎与 App。
    /// 关卡场景预制体另挂 <see cref="UTAgentWindowHost"/>（如 WndCreateRole.py）即可。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class UTAgentPlayFlow : MonoBehaviour
    {
        private static UTAgentPlayFlow mInstance;

        private void Awake()
        {
            if (mInstance != null && mInstance != this)
            {
                Destroy(gameObject);
                return;
            }

            mInstance = this;
            DontDestroyOnLoad(gameObject);
            TryInitializePython();
        }

        private void OnDestroy()
        {
            if (mInstance == this)
            {
                mInstance = null;
            }
        }

        private void TryInitializePython()
        {
            try
            {
                // 每次 Play 都走 Initialize（幂等）；内部会 App.reload，不退出 Play 时 Shutdown
                UTAgentPlayHost.Initialize();
                Debug.Log("[UTAgent] PlayFlow：Python 引擎与 App 已就绪");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UTAgent] PlayFlow：初始化失败：{e}");
            }
        }
    }
}

using UnityEngine;

namespace UTAgent
{
    /// <summary>
    /// 驱动 Python Timer 的 Play Mode tick。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class UTAgentTimerDriver : MonoBehaviour
    {
        private static UTAgentTimerDriver mInstance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureDriver()
        {
            if (mInstance != null)
            {
                return;
            }

            var go = new GameObject("__UTAgentTimerDriver");
            DontDestroyOnLoad(go);
            mInstance = go.AddComponent<UTAgentTimerDriver>();
        }

        private void OnDestroy()
        {
            if (mInstance == this)
            {
                mInstance = null;
            }
        }

        private void Update()
        {
            UTAgentPlayHost.TickTimers(Time.deltaTime);
        }
    }
}

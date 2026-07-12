using System.Collections;
using UnityEngine;

namespace UTAgent
{
    /// <summary>
    /// 通用 Python 生命周期挂载（Cube 等）。Play Mode + 引擎已初始化时生效。
    /// </summary>
    public sealed class UTAgentBehaviour : MonoBehaviour
    {
        [SerializeField]
        private string pythonModule;

        [SerializeField]
        private string pythonClass;

        [SerializeField]
        private bool dispatchUpdate;

        private int mHandle;
        private bool mCreated;

        private void Awake()
        {
            mHandle = gameObject.GetInstanceID();
        }

        private void Start()
        {
            StartCoroutine(StartWhenReady());
        }

        private IEnumerator StartWhenReady()
        {
            while (!UTAgentPlayHost.IsAvailable)
            {
                yield return null;
            }

            TryCreate();
            Dispatch("start");
        }

        private void OnEnable()
        {
            Dispatch("on_enable");
        }

        private void OnDisable()
        {
            Dispatch("on_disable");
        }

        private void Update()
        {
            if (!dispatchUpdate)
            {
                return;
            }
            Dispatch("update");
        }

        private void OnDestroy()
        {
            if (mCreated && UTAgentPlayHost.IsAvailable)
            {
                UTAgentPlayHost.Destroy(mHandle);
            }
        }

        private void TryCreate()
        {
            if (mCreated)
            {
                return;
            }
            if (!UTAgentPlayHost.IsAvailable)
            {
                Debug.LogWarning($"[UTAgent] {name} 跳过 Python 创建：引擎未初始化");
                return;
            }
            if (string.IsNullOrWhiteSpace(pythonModule))
            {
                Debug.LogWarning($"[UTAgent] {name} 未配置 pythonModule");
                return;
            }

            var json = UTAgentPlayHost.Create(
                mHandle,
                "UnityBehaviour",
                pythonModule,
                pythonClass ?? string.Empty);
            if (UTAgentJsonResult.IsSuccess(json))
            {
                mCreated = true;
            }
            else
            {
                Debug.LogWarning($"[UTAgent] 创建 Python 实例失败：{json}");
            }
        }

        private void Dispatch(string method)
        {
            if (!mCreated || !UTAgentPlayHost.IsAvailable)
            {
                return;
            }
            UTAgentPlayHost.Dispatch(mHandle, method, null);
        }
    }
}

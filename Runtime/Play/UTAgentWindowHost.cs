using System.Collections;
using UnityEngine;

namespace UTAgent
{
    /// <summary>
    /// UI 面板 Python 生命周期桥。对外提供 Init/Show/Hide/Release。
    /// </summary>
    public sealed class UTAgentWindowHost : MonoBehaviour
    {
        [SerializeField]
        private string pythonModule;

        [SerializeField]
        private string pythonClass;

        [SerializeField]
        private bool autoShowOnStart = true;

        private int mHandle;
        private bool mCreated;

        private void Awake()
        {
            mHandle = gameObject.GetInstanceID();
            UTAgentUiRoots.TryReparentToMenu(transform);
        }

        private void Start()
        {
            if (!autoShowOnStart)
            {
                return;
            }

            StartCoroutine(ShowWhenReady());
        }

        private IEnumerator ShowWhenReady()
        {
            while (!UTAgentPlayHost.IsAvailable)
            {
                yield return null;
            }

            Init();
            Show();
        }

        /// <summary>
        /// 对齐 WindowBase.OnInit。
        /// </summary>
        public void Init()
        {
            EnsureCreated();
            DispatchLifecycle("on_init", "{}");
        }

        /// <summary>
        /// 对齐 WindowBase.OnShow。
        /// </summary>
        public void Show()
        {
            EnsureCreated();
            DispatchLifecycle("on_show", null);
        }

        /// <summary>
        /// 对齐 WindowBase.OnHide。
        /// </summary>
        public void Hide()
        {
            DispatchLifecycle("on_hide", null);
        }

        /// <summary>
        /// 对齐 WindowBase.OnRelease。
        /// </summary>
        public void Release()
        {
            DispatchLifecycle("on_release", null);
            if (mCreated && UTAgentPlayHost.IsAvailable)
            {
                UTAgentPlayHost.Destroy(mHandle);
                mCreated = false;
            }
        }

        private void OnDestroy()
        {
            if (mCreated && UTAgentPlayHost.IsAvailable)
            {
                UTAgentPlayHost.Destroy(mHandle);
                mCreated = false;
            }
        }

        private void DispatchLifecycle(string method, string argsJson)
        {
            var json = UTAgentPlayHost.Dispatch(mHandle, method, argsJson);
            if (UTAgentJsonResult.IsSuccess(json))
            {
                return;
            }

            Debug.LogError($"[UTAgent] {name}.{method} 失败: {UTAgentJsonResult.GetMessage(json)}");
        }

        /// <summary>
        /// 由 WindowManager 桥或场景配置写入 Python 模块信息。
        /// </summary>
        public void Configure(string module, string className, bool autoShow = false)
        {
            pythonModule = module;
            pythonClass = className;
            autoShowOnStart = autoShow;
        }

        private void EnsureCreated()
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
                "WndBase",
                pythonModule,
                pythonClass ?? string.Empty);
            if (UTAgentJsonResult.IsSuccess(json))
            {
                mCreated = true;
            }
            else
            {
                Debug.LogWarning($"[UTAgent] 创建 WndBase 实例失败：{json}");
            }
        }
    }
}

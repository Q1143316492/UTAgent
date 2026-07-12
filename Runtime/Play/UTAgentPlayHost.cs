using System;

namespace UTAgent
{
    /// <summary>
    /// Play Mode 生命周期门面。Runtime 组件调用；Editor 程序集在加载时注入实现。
    /// </summary>
    public static class UTAgentPlayHost
    {
        public static Func<bool> IsAvailableProvider { get; set; }
        public static Action InitializeProvider { get; set; }
        public static Func<int, string, string, string, string> CreateProvider { get; set; }
        public static Func<int, string, string, string> DispatchProvider { get; set; }
        public static Func<int, string> DestroyProvider { get; set; }
        public static Action<float> TickTimersProvider { get; set; }
        public static Func<string, string> OpenWindowProvider { get; set; }
        public static Func<string, string> CloseWindowProvider { get; set; }
        public static Func<string, bool> IsWindowOpenProvider { get; set; }

        public static bool IsAvailable => IsAvailableProvider != null && IsAvailableProvider.Invoke();

        public static void Initialize()
        {
            if (InitializeProvider == null)
            {
                throw new InvalidOperationException(
                    "[UTAgent] PlayHost 未绑定，请在 Unity Editor 中 Play（非独立构建）");
            }

            InitializeProvider.Invoke();
        }

        public static string Create(int handle, string typeName, string modulePath, string className)
        {
            if (CreateProvider == null)
            {
                return JsonError("PlayHost 未绑定");
            }

            return CreateProvider.Invoke(handle, typeName, modulePath, className);
        }

        public static string Dispatch(int handle, string method, string argsJson)
        {
            if (DispatchProvider == null)
            {
                return JsonError("PlayHost 未绑定");
            }

            return DispatchProvider.Invoke(handle, method, argsJson);
        }

        public static string Destroy(int handle)
        {
            if (DestroyProvider == null)
            {
                return JsonError("PlayHost 未绑定");
            }

            return DestroyProvider.Invoke(handle);
        }

        public static void TickTimers(float deltaTime)
        {
            if (!IsAvailable)
            {
                return;
            }

            TickTimersProvider?.Invoke(deltaTime);
        }

        public static string OpenWindow(string name)
        {
            if (OpenWindowProvider == null)
            {
                return JsonError("OpenWindow 未绑定");
            }

            return OpenWindowProvider.Invoke(name);
        }

        public static string CloseWindow(string name)
        {
            if (CloseWindowProvider == null)
            {
                return JsonError("CloseWindow 未绑定");
            }

            return CloseWindowProvider.Invoke(name);
        }

        public static bool IsWindowOpen(string name)
        {
            if (IsWindowOpenProvider == null)
            {
                return false;
            }

            return IsWindowOpenProvider.Invoke(name);
        }

        private static string JsonError(string message)
        {
            return "{\"success\":false,\"message\":\"" + message + "\"}";
        }
    }
}

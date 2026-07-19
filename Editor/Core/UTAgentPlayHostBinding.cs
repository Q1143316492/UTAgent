using UTAgent.Editor.PythonInterop;

namespace UTAgent.Editor.Core
{
    /// <summary>
    /// 将 Editor 侧 Python 引擎实现注入 <see cref="UTAgentPlayHost"/>（Runtime→Editor 接线）。
    /// </summary>
    [UnityEditor.InitializeOnLoad]
    public static class UTAgentPlayHostBinding
    {
        static UTAgentPlayHostBinding()
        {
            var bridge = UTAgentPythonBridge.Instance;
            UTAgentPlayHost.IsAvailableProvider = () => UTAgentBootstrap.IsAvailable;
            UTAgentPlayHost.InitializeProvider = UTAgentBootstrap.Initialize;
            UTAgentPlayHost.CreateProvider = UTAgentPythonBridge.Create;
            UTAgentPlayHost.DispatchProvider = UTAgentPythonBridge.Dispatch;
            UTAgentPlayHost.DestroyProvider = UTAgentPythonBridge.Destroy;
            UTAgentPlayHost.TickTimersProvider = UTAgentPythonBridge.TickTimers;
            UTAgentPlayHost.OpenWindowProvider = bridge.OpenRegistered;
            UTAgentPlayHost.CloseWindowProvider = name => bridge.Close($"{{\"name\":\"{name}\"}}");
            UTAgentPlayHost.IsWindowOpenProvider = name =>
            {
                var json = bridge.IsOpen($"{{\"name\":\"{name}\"}}");
                return json.Contains("\"open\":true", System.StringComparison.Ordinal)
                    || json.Contains("\"open\": true", System.StringComparison.Ordinal);
            };
        }
    }
}

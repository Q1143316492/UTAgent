namespace UTAgent.Editor.PythonInterop
{
    /// <summary>
    /// 统一 Python 桥接门面。Bootstrap 将同一实例注册为 _unity_bridge / _ui_bridge / _wndmgr_bridge。
    /// </summary>
    public sealed partial class UTAgentPythonBridge
    {
        public static UTAgentPythonBridge Instance { get; } = new();

        private UTAgentPythonBridge()
        {
        }
    }
}

using UTAgent.Editor.Config;

namespace UTAgent.Editor.Bridges
{
    public sealed partial class UTAgentPythonBridge
    {
        /// <summary>
        /// unity_bind.CS 路径白名单校验（对标 Puerts Filter）。
        /// </summary>
        public bool CsIsAllowed(string path)
        {
            return UnityBindWhitelist.IsAllowedPath(path ?? string.Empty);
        }
    }
}

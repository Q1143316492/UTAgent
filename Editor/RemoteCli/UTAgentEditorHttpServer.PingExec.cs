using System;
using UnityEngine;
using UTAgent.Editor.Agent;
using UTAgent.Editor.Core;
using UTAgent.Editor.PythonInterop;

namespace UTAgent.Editor.RemoteCli
{
    public static partial class UTAgentEditorHttpServer
    {
        private sealed partial class BridgeWorkItem
        {
            private void HandlePing()
            {
                string json =
                    "{\"ok\":true" +
                    ",\"editor_alive\":true" +
                    $",\"engine_available\":{BridgeJson.ToLower(UTAgentBootstrap.IsAvailable)}" +
                    $",\"invalidated\":{BridgeJson.ToLower(UTAgentBootstrap.IsInvalidated)}" +
                    $",\"port\":{mPort}" +
                    $",\"log_directory\":{BridgeJson.EscapeJson(UTAgentSessionLogger.ResolveLogDirectory())}" +
                    $",\"unity_version\":{BridgeJson.EscapeJson(Application.unityVersion)}" +
                    ",\"bridge_running\":" + BridgeJson.ToLower(IsListening) +
                    "}";
                if (!UTAgentBootstrap.IsAvailable && UTAgentBootstrap.IsInvalidated)
                {
                    json = json.TrimEnd('}') +
                           ",\"hint\":\"引擎因域重载失效，请 POST /initialize 或运行 utagent init\"}";
                }

                WriteJson(200, json);
            }

            private void HandleInitialize()
            {
                try
                {
                    UTAgentBootstrap.Initialize();
                    WriteJson(200,
                        $"{{\"ok\":true,\"engine_available\":{BridgeJson.ToLower(UTAgentBootstrap.IsAvailable)}}}");
                }
                catch (Exception e)
                {
                    WriteJson(500,
                        $"{{\"ok\":false,\"engine_available\":false,\"error\":{BridgeJson.EscapeJson(e.Message)}}}");
                }
            }

            private void HandleExec()
            {
                if (!UTAgentBootstrap.IsAvailable)
                {
                    string hint = UTAgentBootstrap.IsInvalidated
                        ? "引擎因域重载失效，请 POST /initialize 或运行 utagent init"
                        : "引擎未初始化，请 POST /initialize 或运行 utagent init";
                    WriteJson(503,
                        "{\"ok\":false" +
                        ",\"engine_available\":false" +
                        $",\"invalidated\":{BridgeJson.ToLower(UTAgentBootstrap.IsInvalidated)}" +
                        $",\"hint\":{BridgeJson.EscapeJson(hint)}" +
                        ",\"output\":\"\",\"error\":\"\"}");
                    return;
                }

                string code = BridgeJson.Args.GetString(mBody, "code");
                if (string.IsNullOrWhiteSpace(code))
                {
                    WriteJson(400, "{\"ok\":false,\"error\":\"missing code\"}");
                    return;
                }

                try
                {
                    var (output, error) = UTAgentBootstrap.Exec(code);
                    bool hasError = !string.IsNullOrWhiteSpace(error);
                    WriteJson(200,
                        "{\"ok\":" + BridgeJson.ToLower(!hasError) +
                        $",\"output\":{BridgeJson.EscapeJson(output ?? string.Empty)}" +
                        $",\"error\":{BridgeJson.EscapeJson(error ?? string.Empty)}" +
                        ",\"engine_available\":true}");
                }
                catch (Exception e)
                {
                    WriteJson(200,
                        "{\"ok\":false" +
                        ",\"output\":\"\"" +
                        $",\"error\":{BridgeJson.EscapeJson(e.ToString())}" +
                        ",\"engine_available\":true}");
                }
            }
        }
    }
}

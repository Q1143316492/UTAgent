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
            private void HandleChatPost()
            {
                string message = BridgeJson.Args.GetString(mBody, "message");
                if (!UTAgentRemoteChatService.Instance.TryStartChat(
                        message,
                        out UTAgentRemoteChatService.RemoteChatTurn turn,
                        out string error,
                        out int httpStatus))
                {
                    WriteJson(httpStatus,
                        "{\"ok\":false" +
                        $",\"error\":{BridgeJson.EscapeJson(error ?? "start failed")}" +
                        (httpStatus == 409 && UTAgentRemoteChatService.Instance.HasRunningTurn
                            ? ",\"hint\":\"等待当前 turn 结束或使用 chat status 查询\""
                            : string.Empty) +
                        "}");
                    return;
                }

                WriteJson(200,
                    "{\"ok\":true" +
                    $",\"turn_id\":{BridgeJson.EscapeJson(turn.TurnId)}" +
                    ",\"status\":\"running\"}");
            }

            private void HandleChatStatus()
            {
                string turnId = mQuery["turn_id"] ?? string.Empty;
                if (!UTAgentRemoteChatService.Instance.TryGetTurn(turnId, out UTAgentRemoteChatService.RemoteChatTurn turn))
                {
                    WriteJson(404, "{\"ok\":false,\"error\":\"turn not found\"}");
                    return;
                }

                WriteJson(200, UTAgentRemoteChatService.Instance.BuildTurnJson(turn));
            }
        }
    }
}

using System;
using UnityEditor;

namespace UTAgent.Editor.Agent
{
    /// <summary>
    /// Runner 传输层：Editor update Poll + SSE chunk 解析。
    /// HandleStreamDone / HandleCompactionDone 仍在主文件（编排回调）。
    /// </summary>
    public sealed partial class UTAgentRunner
    {
        // ----- Poll + SSE 解析 -----

        private void Poll()
        {
            if (mActiveTurns.Count == 0)
            {
                EditorApplication.update -= Poll;
                return;
            }
            for (int i = mActiveTurns.Count - 1; i >= 0; i--)
            {
                var turn = mActiveTurns[i];

                if (turn.AbortRequested && turn.StreamHandler == null && !turn.IsCompacting)
                {
                    mActiveTurns.RemoveAt(i);
                    FinishTurn(turn, "已暂停", false, "aborted");
                    continue;
                }

                if (turn.IsCompacting)
                {
                    if (turn.AbortRequested)
                    {
                        mActiveTurns.RemoveAt(i);
                        try
                        {
                            turn.Request?.Abort();
                        }
                        catch
                        {
                        }
                        FinishTurn(turn, "已暂停", false, "aborted");
                        continue;
                    }
                    if (turn.Operation == null || !turn.Operation.isDone)
                    {
                        continue;
                    }
                    HandleCompactionDone(turn);
                    continue;
                }

                if (turn.StreamHandler != null)
                {
                    if (turn.AbortRequested)
                    {
                        mActiveTurns.RemoveAt(i);
                        FinishTurn(turn, "已暂停", false, "aborted");
                        continue;
                    }
                    var chunk = turn.StreamHandler.DrainChunks();
                    if (chunk != null)
                    {
                        ParseSseChunk(turn, chunk);
                    }
                    if (!turn.Operation.isDone)
                    {
                        continue;
                    }
                    HandleStreamDone(turn);
                    continue;
                }
            }
            if (mActiveTurns.Count == 0)
            {
                EditorApplication.update -= Poll;
            }
        }

        /// <summary>
        /// SSE "data:" 行解析。合并 content delta；累积 tool_calls 分片。
        /// </summary>
        private static void ParseSseChunk(TurnState turn, string chunk)
        {
            if (turn.AbortRequested)
            {
                return;
            }
            turn.SseLineBuf.Append(chunk);
            string buf = turn.SseLineBuf.ToString();
            turn.SseLineBuf.Clear();
            string contentBuf = "";

            int pos = 0;
            while (pos < buf.Length)
            {
                int end = buf.IndexOf("\n\n", pos, StringComparison.Ordinal);
                if (end < 0)
                {
                    turn.SseLineBuf.Append(buf.Substring(pos));
                    break;
                }
                string message = buf.Substring(pos, end - pos);
                pos = end + 2;
                string prefix = "data: ";
                int idx = message.IndexOf(prefix, StringComparison.Ordinal);
                if (idx < 0)
                {
                    continue;
                }
                string json = message.Substring(idx + prefix.Length).Trim();
                if (json == "[DONE]")
                {
                    continue;
                }
                string dc = ExtractDeltaString(json, "content");
                string dr = ExtractDeltaString(json, "reasoning_content");
                if (!string.IsNullOrEmpty(dr))
                {
                    turn.StreamThinkingBuf.Append(dr);
                    PushProgress(turn, "thinking", dr);
                }
                if (!string.IsNullOrEmpty(dc))
                {
                    contentBuf += dc;
                }
                AccumulateToolCallsFromSseJson(json, turn);
            }
            if (contentBuf.Length > 0)
            {
                turn.StreamTextBuf.Append(contentBuf);
                PushProgress(turn, "stream", contentBuf);
            }
        }

        /// <summary>
        /// 从 SSE delta JSON 累积 tool_calls（按 index 合并 id/name/arguments 分片）。
        /// </summary>
        private static void AccumulateToolCallsFromSseJson(string json, TurnState turn)
        {
            int tcIdx = json.IndexOf("\"tool_calls\"", StringComparison.Ordinal);
            if (tcIdx < 0)
            {
                return;
            }
            string slice = json.Substring(tcIdx);
            int index = ExtractInt(slice, "index");
            if (index < 0)
            {
                return;
            }
            if (!turn.ToolCallBuf.TryGetValue(index, out var acc))
            {
                acc = new ToolCallAccumulator();
                turn.ToolCallBuf[index] = acc;
            }
            string id = ExtractString(slice, "id");
            if (!string.IsNullOrEmpty(id))
            {
                acc.Id = id;
            }
            string name = ExtractString(slice, "name");
            if (!string.IsNullOrEmpty(name))
            {
                acc.Name = name;
            }
            string args = ExtractString(slice, "arguments");
            if (!string.IsNullOrEmpty(args))
            {
                acc.Arguments.Append(args);
            }
        }
    }
}

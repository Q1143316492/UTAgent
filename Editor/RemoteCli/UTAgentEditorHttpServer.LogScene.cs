using System;
using System.Collections.Generic;
using System.Text;
using UTAgent.Editor.PythonInterop;

namespace UTAgent.Editor.RemoteCli
{
    public static partial class UTAgentEditorHttpServer
    {
        private sealed partial class BridgeWorkItem
        {
            private void HandleLogTail()
            {
                int n = ParseLineCount(mQuery["n"], 80);
                var (path, lines) = ReadLatestLogTail(n);
                var sb = new StringBuilder();
                sb.Append("{\"ok\":true");
                sb.Append(path == null ? ",\"path\":null" : $",\"path\":{BridgeJson.EscapeJson(path)}");
                sb.Append(",\"lines\":[");
                for (int i = 0; i < lines.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append(BridgeJson.EscapeJson(lines[i]));
                }

                sb.Append("]}");
                WriteJson(200, sb.ToString());
            }

            private void HandleLogErrors()
            {
                int n = ParseLineCount(mQuery["n"], 200);
                var (path, lines) = ReadLatestLogTail(n);
                var matches = new List<string>();
                foreach (string line in lines)
                {
                    if (IsErrorLine(line))
                    {
                        matches.Add(line);
                    }
                }

                var sb = new StringBuilder();
                sb.Append("{\"ok\":true");
                sb.Append(path == null ? ",\"path\":null" : $",\"path\":{BridgeJson.EscapeJson(path)}");
                sb.Append(",\"matches\":[");
                for (int i = 0; i < matches.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append(BridgeJson.EscapeJson(matches[i]));
                }

                sb.Append("]}");
                WriteJson(200, sb.ToString());
            }

            private void HandleSceneFind()
            {
                string name = mQuery["name"] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    WriteJson(400, "{\"ok\":false,\"error\":\"missing name\"}");
                    return;
                }

                string bridgeJson = UTAgentPythonBridge.Instance.FindObjects(name);
                int count = 0;
                var names = new List<string>();
                if (bridgeJson.Contains("\"success\":true"))
                {
                    int countIdx = bridgeJson.IndexOf("\"count\":", StringComparison.Ordinal);
                    if (countIdx >= 0)
                    {
                        int start = countIdx + 8;
                        int end = start;
                        while (end < bridgeJson.Length && char.IsDigit(bridgeJson[end]))
                        {
                            end++;
                        }

                        if (end > start && int.TryParse(bridgeJson.Substring(start, end - start), out int parsed))
                        {
                            count = parsed;
                        }
                    }

                    int searchFrom = 0;
                    while (true)
                    {
                        int nameKey = bridgeJson.IndexOf("\"name\":", searchFrom, StringComparison.Ordinal);
                        if (nameKey < 0)
                        {
                            break;
                        }

                        int q1 = bridgeJson.IndexOf('"', nameKey + 7);
                        if (q1 < 0)
                        {
                            break;
                        }

                        int q2 = bridgeJson.IndexOf('"', q1 + 1);
                        if (q2 < 0)
                        {
                            break;
                        }

                        names.Add(bridgeJson.Substring(q1 + 1, q2 - q1 - 1));
                        searchFrom = q2 + 1;
                    }
                }

                var sb = new StringBuilder();
                sb.Append("{\"ok\":true");
                sb.Append($",\"count\":{count}");
                sb.Append(",\"names\":[");
                for (int i = 0; i < names.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append(BridgeJson.EscapeJson(names[i]));
                }

                sb.Append("]}");
                WriteJson(200, sb.ToString());
            }
        }
    }
}

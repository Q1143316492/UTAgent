using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace UTAgent.Editor
{
    public sealed partial class UTAgentRunner
    {
        /// <summary>
        /// 流式累积的 tool_call 片段（按 index 合并）。
        /// </summary>
        private sealed class ToolCallAccumulator
        {
            public string Id = "";
            public string Name = "";
            public readonly StringBuilder Arguments = new StringBuilder();
        }

        private const string AgentToolSchema =
            "[{\"type\":\"function\",\"function\":{" +
            "\"name\":\"execPython\"," +
            "\"description\":\"在 Unity Editor 内执行 Python。code 为完整脚本，可 import unity。示例：import unity\\nunity.get_hierarchy()\"," +
            "\"parameters\":{\"type\":\"object\",\"properties\":{" +
            "\"code\":{\"type\":\"string\",\"description\":\"Python 源码\"}," +
            "\"timeout\":{\"type\":\"number\",\"description\":\"秒，默认 30\"}" +
            "},\"required\":[\"code\"]}}}," +
            "{\"type\":\"function\",\"function\":{" +
            "\"name\":\"loadSkill\"," +
            "\"description\":\"按需加载 skills/*.md.txt 全文。Editor 拼/改 Canvas UI、预制体、布局前须先 loadSkill(\\\"editor-ui\\\")。\"," +
            "\"parameters\":{\"type\":\"object\",\"properties\":{" +
            "\"name\":{\"type\":\"string\",\"description\":\"skill 文件名，如 editor-ui\"}" +
            "},\"required\":[\"name\"]}}}]";

        private static string ExtractValue(string json)
        {
            string needle = "\"value\":";
            int idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0)
            {
                return "";
            }
            int i = idx + needle.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\n'))
            {
                i++;
            }
            if (i >= json.Length)
            {
                return "";
            }
            char first = json[i];
            if (first == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < json.Length)
                {
                    char c = json[i];
                    if (c == '\\' && i + 1 < json.Length)
                    {
                        char n = json[i + 1];
                        switch (n)
                        {
                            case 'n':
                                sb.Append('\n');
                                break;
                            case 't':
                                sb.Append('\t');
                                break;
                            case 'r':
                                sb.Append('\r');
                                break;
                            case '"':
                                sb.Append('"');
                                break;
                            case '\\':
                                sb.Append('\\');
                                break;
                            default:
                                sb.Append(n);
                                break;
                        }
                        i += 2;
                        continue;
                    }
                    if (c == '"')
                    {
                        break;
                    }
                    sb.Append(c);
                    i++;
                }
                return sb.ToString();
            }
            if (first == 'n' && i + 4 <= json.Length && json.Substring(i, 4) == "null")
            {
                return "null";
            }
            int depth = 0;
            char open = first;
            char close = (first == '[') ? ']' : '}';
            var raw = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i];
                if (c == open)
                {
                    depth++;
                }
                else if (c == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        raw.Append(c);
                        i++;
                        break;
                    }
                }
                raw.Append(c);
                i++;
            }
            return raw.ToString();
        }

        /// <summary>
        /// 取 Python 输出的最后一行 JSON 数组（build_llm_messages_json 协议）。
        /// </summary>
        private static string ExtractLastJsonLine(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return "";
            }

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    return line;
                }
            }

            return "";
        }

        private static string ExtractString(string json, string key)
        {
            string needle = "\"" + key + "\":";
            int idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0)
            {
                return "";
            }
            int i = idx + needle.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\n'))
            {
                i++;
            }
            if (i >= json.Length)
            {
                return "";
            }
            if (json[i] == 'n' && i + 4 <= json.Length && json.Substring(i, 4) == "null")
            {
                return "";
            }
            if (json[i] != '"')
            {
                return "";
            }
            i++;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char n = json[i + 1];
                    switch (n)
                    {
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case '"':
                            sb.Append('"');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        default:
                            sb.Append(n);
                            break;
                    }
                    i += 2;
                    continue;
                }
                if (c == '"')
                {
                    break;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static string ExtractDeltaString(string json, string key)
        {
            string needle = "\"delta\":";
            int idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx >= 0)
            {
                int i = idx + needle.Length;
                while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\n'))
                {
                    i++;
                }
                if (i < json.Length && json[i] == '{')
                {
                    int depth = 0;
                    int start = i;
                    while (i < json.Length)
                    {
                        char c = json[i];
                        if (c == '{')
                        {
                            depth++;
                        }
                        else if (c == '}')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                string delta = json.Substring(start, i - start + 1);
                                string v = ExtractString(delta, key);
                                if (!string.IsNullOrEmpty(v))
                                {
                                    return v;
                                }
                                break;
                            }
                        }
                        i++;
                    }
                }
            }

            return ExtractString(json, key);
        }

        private static int ExtractInt(string json, string key)
        {
            string needle = "\"" + key + "\":";
            int idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0)
            {
                return -1;
            }
            int i = idx + needle.Length;
            while (i < json.Length && json[i] == ' ')
            {
                i++;
            }
            var sb = new StringBuilder();
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '-'))
            {
                sb.Append(json[i]);
                i++;
            }
            return int.TryParse(sb.ToString(), out var v) ? v : -1;
        }

        private static bool ExtractBool(string json, string key)
        {
            string needle = "\"" + key + "\":";
            int idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0)
            {
                return false;
            }
            int i = idx + needle.Length;
            while (i < json.Length && json[i] == ' ')
            {
                i++;
            }
            return i + 4 <= json.Length && json.Substring(i, 4) == "true";
        }

        private static int GetMaxStepsFromConfig()
        {
            return EditorPrefs.GetInt("UTAgent.Agent_MaxSteps", 25);
        }

        private static int GetMaxInputTokensFromConfig()
        {
            return EditorPrefs.GetInt("UTAgent.Agent_MaxInputTokens", 100000);
        }

        private static int GetMinKeepMessagesFromConfig()
        {
            return EditorPrefs.GetInt("UTAgent.Agent_MinKeepMessages", 20);
        }

        private static string BuildMessagesArray(string systemPrompt, string messagesJson)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            string prompt = string.IsNullOrEmpty(systemPrompt)
                ? "你是一个运行在 Unity Editor 内的 Python 桥接 Agent。"
                : systemPrompt;
            sb.Append("{\"role\":\"system\",\"content\":");
            sb.Append(JsonStr(prompt));
            sb.Append('}');
            string inner = messagesJson.Trim();
            if (inner.StartsWith("[") && inner.EndsWith("]"))
            {
                inner = inner.Substring(1, inner.Length - 2).Trim();
            }
            if (!string.IsNullOrWhiteSpace(inner))
            {
                sb.Append(',');
                sb.Append(inner);
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>
        /// 将累积的 tool_calls 序列化为 OpenAI 兼容 JSON 数组字符串。
        /// </summary>
        private static string SerializeToolCalls(Dictionary<int, ToolCallAccumulator> buf)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            bool first = true;
            var keys = new List<int>(buf.Keys);
            keys.Sort();
            foreach (int key in keys)
            {
                var acc = buf[key];
                if (string.IsNullOrEmpty(acc.Id) && string.IsNullOrEmpty(acc.Name))
                {
                    continue;
                }
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                sb.Append("{\"id\":");
                sb.Append(JsonStr(acc.Id));
                sb.Append(",\"type\":\"function\",\"function\":{");
                sb.Append("\"name\":");
                sb.Append(JsonStr(acc.Name));
                sb.Append(",\"arguments\":");
                sb.Append(JsonStr(acc.Arguments.ToString()));
                sb.Append("}}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string EscapePy(string s)
        {
            if (s == null)
            {
                return "''";
            }
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\'':
                        sb.Append("\\'");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return $"'{sb}'";
        }

        private static string JsonStr(string s)
        {
            if (s == null)
            {
                return "null";
            }
            var sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append($"\\u{(int)c:X4}");
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string MimeFromExt(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".png":
                    return "image/png";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".gif":
                    return "image/gif";
                case ".bmp":
                    return "image/bmp";
                case ".webp":
                    return "image/webp";
                default:
                    return "image/png";
            }
        }

        /// <summary>
        /// 解析 agent 返回的 <c>{"ok": true/false, ...}</c>。
        /// </summary>
        private static bool ParseExecOk(string output, out string message)
        {
            message = ExtractString(output, "message");
            if (string.IsNullOrEmpty(output))
            {
                return false;
            }

            return ExtractBool(output, "ok");
        }

        /// <summary>
        /// 解析 <c>{"ok": true, "value": bool}</c> 类查询（如 is_configured）。
        /// </summary>
        private static bool ParseBool(string output)
        {
            if (ExtractBool(output, "value"))
            {
                return true;
            }

            return output.Contains("\"value\": true");
        }

        private static int ParseInt(string output)
        {
            var match = System.Text.RegularExpressions.Regex.Match(output, "\"value\"\\s*:\\s*(\\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var v))
            {
                return v;
            }
            return 0;
        }

        private static string BuildDeepSeekRequestExtras(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return "";
            }

            string lower = model.Trim().ToLowerInvariant();
            if (!lower.Contains("deepseek"))
            {
                return "";
            }

            return ",\"thinking\":{\"type\":\"enabled\"}";
        }

        private static string FormatLlmHttpError(
            UnityEngine.Networking.UnityWebRequest req,
            TurnState turn,
            string responseBody = null)
        {
            var sb = new StringBuilder();
            sb.Append($"  HTTP {(long)req.responseCode}");
            sb.Append($"\n  url: {turn.Url}");
            sb.Append($"\n  step: {turn.StepCount + 1}, model: {turn.Model}");
            sb.Append($"\n  request_body_chars: {turn.RequestBody?.Length ?? 0}");
            if (!string.IsNullOrEmpty(req.error))
            {
                sb.Append($"\n  transport: {req.error}");
            }

            if (string.IsNullOrEmpty(responseBody))
            {
                responseBody = turn.StreamHandler?.GetBufferedText();
            }
            if (string.IsNullOrEmpty(responseBody))
            {
                responseBody = req.downloadHandler?.text;
            }

            if (!string.IsNullOrEmpty(responseBody))
            {
                string snippet = responseBody.Length > 1500 ? responseBody.Substring(0, 1500) + "…" : responseBody;
                sb.Append($"\n  response_body: {snippet}");
                string apiMsg = TryExtractApiErrorMessage(responseBody);
                if (!string.IsNullOrEmpty(apiMsg))
                {
                    sb.Append($"\n  api_message: {apiMsg}");
                }
            }

            return sb.ToString();
        }

        private static string TryExtractApiErrorMessage(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return null;
            }

            if (!body.Contains("\"error\"", StringComparison.Ordinal))
            {
                return null;
            }

            string msg = ExtractString(body, "message");
            return string.IsNullOrEmpty(msg) ? null : msg;
        }

        /// <summary>
        /// 从 JSON 对象字符串提取数组字段原文（含方括号）。
        /// </summary>
        private static string ExtractJsonArrayField(string json, string key)
        {
            if (string.IsNullOrEmpty(json))
            {
                return "";
            }

            string needle = "\"" + key + "\":";
            int idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0)
            {
                return "";
            }

            int i = idx + needle.Length;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
            {
                i++;
            }

            if (i >= json.Length || json[i] != '[')
            {
                return "";
            }

            int depth = 0;
            int start = i;
            for (; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return json.Substring(start, i - start + 1);
                    }
                }
            }

            return "";
        }
    }
}

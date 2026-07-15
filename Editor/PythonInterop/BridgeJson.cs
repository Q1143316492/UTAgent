using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UTAgent.Editor.PythonInterop
{
    /// <summary>
    /// Python 桥接层共享 JSON 工具。
    /// </summary>
    internal static class BridgeJson
    {
        public static string SuccessTrue()
        {
            return "{\"success\":true}";
        }

        public static string Error(string message)
        {
            return $"{{\"success\":false,\"message\":{EscapeJson(message)}}}";
        }

        public static string EscapeJson(string s)
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
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
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

        public static string EscapeJsonInline(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        public static string ToLower(bool value)
        {
            return value ? "true" : "false";
        }

        public static class Args
        {
            public static string GetString(string json, string key)
            {
                if (string.IsNullOrEmpty(json))
                {
                    return string.Empty;
                }

                var pattern = $"\"{key}\"";
                int idx = json.IndexOf(pattern, StringComparison.Ordinal);
                if (idx < 0)
                {
                    return string.Empty;
                }

                int colon = json.IndexOf(':', idx + pattern.Length);
                if (colon < 0)
                {
                    return string.Empty;
                }

                int i = colon + 1;
                while (i < json.Length && char.IsWhiteSpace(json[i]))
                {
                    i++;
                }

                if (i >= json.Length)
                {
                    return string.Empty;
                }

                if (json[i] == '"')
                {
                    int k = i + 1;
                    while (k < json.Length)
                    {
                        if (json[k] == '\\')
                        {
                            k += 2;
                            continue;
                        }

                        if (json[k] == '"')
                        {
                            break;
                        }

                        k++;
                    }

                    if (k >= json.Length)
                    {
                        return string.Empty;
                    }

                    var token = json.Substring(i, k - i + 1);
                    return UnquoteJsonString(token) ?? string.Empty;
                }

                int j = i;
                while (j < json.Length && json[j] != ',' && json[j] != '}')
                {
                    j++;
                }

                return json.Substring(i, j - i).Trim();
            }

            public static int GetInt(string json, string key)
            {
                var raw = GetString(json, key);
                if (string.IsNullOrEmpty(raw))
                {
                    return 0;
                }

                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }

                return 0;
            }

            public static bool GetBool(string json, string key)
            {
                var raw = GetString(json, key);
                if (string.IsNullOrEmpty(raw))
                {
                    return false;
                }

                return raw.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 解析 BindInvoke 的 {"args":[...]} 数组为原始 JSON 片段列表。
        /// </summary>
        public static List<string> ParseArgsArray(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            int keyIdx = json.IndexOf("\"args\"", StringComparison.Ordinal);
            if (keyIdx < 0)
            {
                return result;
            }

            int bracket = json.IndexOf('[', keyIdx);
            if (bracket < 0)
            {
                return result;
            }

            int depth = 0;
            int start = -1;
            for (int i = bracket; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '[')
                {
                    if (depth == 0)
                    {
                        start = i + 1;
                    }

                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        ParseArrayElements(json, start, i, result);
                        break;
                    }
                }
            }

            return result;
        }

        private static void ParseArrayElements(string json, int start, int end, List<string> result)
        {
            int i = start;
            while (i < end)
            {
                while (i < end && char.IsWhiteSpace(json[i]))
                {
                    i++;
                }

                if (i >= end)
                {
                    break;
                }

                if (json[i] == ',')
                {
                    i++;
                    continue;
                }

                int elemStart = i;
                if (json[i] == '"')
                {
                    i++;
                    while (i < end)
                    {
                        if (json[i] == '\\')
                        {
                            i += 2;
                            continue;
                        }

                        if (json[i] == '"')
                        {
                            i++;
                            break;
                        }

                        i++;
                    }
                }
                else
                {
                    while (i < end && json[i] != ',')
                    {
                        i++;
                    }
                }

                result.Add(json.Substring(elemStart, i - elemStart).Trim());
            }
        }

        public static string UnquoteJsonString(string token)
        {
            if (string.IsNullOrEmpty(token) || token == "null")
            {
                return null;
            }

            if (token.Length >= 2 && token[0] == '"' && token[^1] == '"')
            {
                return token.Substring(1, token.Length - 2)
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\")
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t");
            }

            return token;
        }
    }
}

using System;
using System.Text;

namespace UTAgent.Editor.Agent
{
    /// <summary>
    /// 从 JSON 文本提取字符串字段（Runner / SessionLogger 共用）。
    /// </summary>
    internal static class UTAgentJsonExtract
    {
        public static string GetString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
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
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 't':
                            sb.Append('\t');
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
    }
}

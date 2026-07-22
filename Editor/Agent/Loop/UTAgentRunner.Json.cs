using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UTAgent.Editor.Config;
using UTAgent.Editor.Core;

namespace UTAgent.Editor.Agent
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

        /// <summary>
        /// 动态构造 OpenAI tools schema：loadSkill description 含 Available Skills + MUST（对标 Puerts createSkillTools）。
        /// </summary>
        private static string BuildToolSchema()
        {
            string loadSkillDesc = BuildLoadSkillDescription();
            var sb = new StringBuilder();
            sb.Append("[{\"type\":\"function\",\"function\":{");
            sb.Append("\"name\":\"execPython\",");
            sb.Append("\"description\":").Append(JsonStr("在 Unity Editor 内执行 Python。code 为完整脚本，可 import unity。示例：import unity\nunity.get_hierarchy()")).Append(",");
            sb.Append("\"parameters\":{\"type\":\"object\",\"properties\":{");
            sb.Append("\"code\":{\"type\":\"string\",\"description\":\"Python 源码\"},");
            sb.Append("\"timeout\":{\"type\":\"number\",\"description\":\"秒，默认 30\"}");
            sb.Append("},\"required\":[\"code\"]}}},");

            if (CsharpExec.CsharpEmitExec.Enabled)
            {
                sb.Append("{\"type\":\"function\",\"function\":{");
                sb.Append("\"name\":\"execCsharp\",");
                sb.Append("\"description\":").Append(JsonStr(
                    "【实验/尖刺】在 Unity Editor 内 Emit+Load 执行 C#。code 须为可编译单元，含 public static class Dyn { public static string Run() { ... } }。" +
                    "勿用于日常任务（日常请用 execPython）。不触发 Domain Reload。")).Append(",");
                sb.Append("\"parameters\":{\"type\":\"object\",\"properties\":{");
                sb.Append("\"code\":{\"type\":\"string\",\"description\":\"完整 C# 源码（含 Dyn.Run 入口）\"}");
                sb.Append("},\"required\":[\"code\"]}}},");
            }

            sb.Append("{\"type\":\"function\",\"function\":{");
            sb.Append("\"name\":\"loadSkill\",");
            sb.Append("\"description\":").Append(JsonStr(loadSkillDesc)).Append(",");
            sb.Append("\"parameters\":{\"type\":\"object\",\"properties\":{");
            sb.Append("\"name\":{\"type\":\"string\",\"description\":\"skill 文件名，如 editor-ui\"}");
            sb.Append("},\"required\":[\"name\"]}}}]");
            return sb.ToString();
        }

        /// <summary>
        /// 构造 loadSkill tool description：MUST 句 + Available Skills 动态列表（读 skills/*.md.txt frontmatter）。
        /// 对标 Puerts main.mjs createSkillTools。
        /// </summary>
        private static string BuildLoadSkillDescription()
        {
            var sb = new StringBuilder();
            sb.Append("Load a specialized skill that provides domain-specific instructions and workflows.\n\n");
            sb.Append("**IMPORTANT**: You MUST call this tool BEFORE performing any task that involves a domain listed below. ");
            sb.Append("Do NOT rely on your own knowledge — always load the skill first.\n\n");
            sb.Append("## Available Skills\n");
            sb.Append(BuildSkillListMarkdown());
            return sb.ToString();
        }

        /// <summary>
        /// 扫描 skills 目录，从 frontmatter 解析 name/description，生成 markdown 列表。
        /// </summary>
        private static string BuildSkillListMarkdown()
        {
            string skillDir = System.IO.Path.Combine(
                UnityEngine.Application.dataPath, "UTAgent/Python/agent/skills");
            if (!System.IO.Directory.Exists(skillDir))
            {
                return "- (none)\n";
            }

            var entries = new List<string>();
            string[] files = System.IO.Directory.GetFiles(skillDir, "*.md.txt");
            System.Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                string skillId = System.IO.Path.GetFileNameWithoutExtension(file);
                skillId = skillId.Substring(0, skillId.Length - 3); // 去掉 .md
                string content = File.ReadAllText(file);
                string fmName, fmDesc;
                ParseSkillFrontmatter(content, out fmName, out fmDesc);
                string display = string.IsNullOrEmpty(fmName) ? skillId : fmName;
                if (!string.IsNullOrEmpty(fmDesc))
                {
                    entries.Add($"- `{display}` — {fmDesc}");
                }
                else
                {
                    entries.Add($"- `{display}`");
                }
            }

            if (entries.Count == 0)
            {
                return "- (none)\n";
            }
            return string.Join("\n", entries) + "\n";
        }

        /// <summary>
        /// 从 skill 文件 YAML frontmatter 解析 name 与 description（与 agent.py _parse_skill_frontmatter 同格式）。
        /// </summary>
        private static void ParseSkillFrontmatter(string content, out string name, out string description)
        {
            name = "";
            description = "";
            if (string.IsNullOrEmpty(content) || !content.StartsWith("---"))
            {
                return;
            }
            int end = content.IndexOf("---", 3, StringComparison.Ordinal);
            if (end < 0)
            {
                return;
            }
            string block = content.Substring(3, end - 3);
            foreach (string line in block.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("name:"))
                {
                    name = trimmed.Substring(5).Trim().Trim('"').Trim('\'');
                }
                else if (trimmed.StartsWith("description:"))
                {
                    description = trimmed.Substring(12).Trim().Trim('"').Trim('\'');
                }
            }
        }

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

        /// <summary>
        /// 非流式 chat.completions 响应中的 assistant content。
        /// </summary>
        private static string ExtractCompletionMessageContent(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return "";
            }

            int msgIdx = json.IndexOf("\"message\"", StringComparison.Ordinal);
            if (msgIdx < 0)
            {
                return "";
            }

            return UTAgentJsonExtract.GetString(json.Substring(msgIdx), "content");
        }

        private static string ExtractString(string json, string key)
        {
            return UTAgentJsonExtract.GetString(json, key);
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
            return UTAgentConfig.ResolveMaxSteps();
        }

        private static int GetMaxInputTokensFromConfig()
        {
            return UTAgentConfig.ResolveMaxInputTokens();
        }

        private static int GetMinKeepMessagesFromConfig()
        {
            return UTAgentConfig.ResolveMinKeepMessages();
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

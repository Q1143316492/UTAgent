using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UTAgent.Editor.Config;
using UTAgent.Editor.Core;

namespace UTAgent.Editor.Agent
{
    /// <summary>
    /// Agent 会话日志：按小时追加到 agent_yyyyMMdd_HH.log；单轮对话锁定开始时刻的小时，跨小时不拆文件。
    /// 流式 thinking/stream 在内存聚合后一次性写出，避免逐 token JSON 刷屏。
    /// </summary>
    public sealed class UTAgentSessionLogger
    {
        private const int MaxInlineChars = 4000;

        private readonly StreamWriter mWriter;
        private readonly StringBuilder mStreamBuf = new StringBuilder();
        private readonly StringBuilder mThinkingBuf = new StringBuilder();

        private int mCurrentStep;
        private string mLastStatus;
        private bool mEnded;

        private UTAgentSessionLogger(StreamWriter writer)
        {
            mWriter = writer;
        }

        public static string GetDefaultLogDirectory()
        {
            return Path.GetFullPath(PythonPathConfig.DefaultLogDirectory);
        }

        public static string ResolveLogDirectory()
        {
            return UTAgentConfig.ResolveLogDirectory();
        }

        public static bool EnsureLogDirectory(string directory = null)
        {
            string dir = string.IsNullOrEmpty(directory) ? ResolveLogDirectory() : Path.GetFullPath(directory);
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgentSessionLogger] 创建日志目录失败：{e.Message}");
                return false;
            }
        }

        public static void RevealLogDirectory()
        {
            string dir = ResolveLogDirectory();
            EnsureLogDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        public static UTAgentSessionLogger BeginTurn(string userText, string model, string imagePath)
        {
            return BeginSessionBlock(userText, model, imagePath, "TURN BEGIN");
        }

        public static UTAgentSessionLogger BeginContinueTurn(string model)
        {
            return BeginSessionBlock("(continue)", model, null, "TURN CONTINUE");
        }

        private static UTAgentSessionLogger BeginSessionBlock(
            string userText, string model, string imagePath, string header)
        {
            string dir = ResolveLogDirectory();
            if (!EnsureLogDirectory(dir))
            {
                return null;
            }

            DateTime turnStart = DateTime.Now;
            string turnId = Guid.NewGuid().ToString("N").Substring(0, 4);
            string hourStamp = turnStart.ToString("yyyyMMdd_HH");
            string filePath = Path.Combine(dir, $"agent_{hourStamp}.log");

            try
            {
                bool fileExists = File.Exists(filePath) && new FileInfo(filePath).Length > 0;
                var writer = new StreamWriter(filePath, true, Encoding.UTF8) { AutoFlush = true };
                var logger = new UTAgentSessionLogger(writer);

                if (fileExists)
                {
                    writer.WriteLine();
                }

                writer.WriteLine(new string('=', 80));
                logger.WriteTimestamped($"{header} [{turnId}]");
                logger.WriteLine($"user: {TrimForLog(userText, 2000)}");
                logger.WriteLine($"model: {model}");
                if (!string.IsNullOrEmpty(imagePath))
                {
                    logger.WriteLine($"image: {imagePath}");
                }

                Debug.Log($"[UTAgentSessionLogger] 会话日志：{filePath}");
                return logger;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgentSessionLogger] 打开日志失败：{e.Message}");
                return null;
            }
        }

        public void BeginStep(int stepNumber)
        {
            FlushStreamBuffers();
            mCurrentStep = stepNumber;
            WriteLine($"--- step {stepNumber} ---");
        }

        public void LogLlmRequest(string url, string requestBody)
        {
            FlushStreamBuffers();
            WriteTimestamped($"LLM POST {url}");
            WriteLine(SummarizeRequestBody(requestBody));
        }

        public void LogLlmPrepare(int reminderInHistory, int reminderInLlm)
        {
            FlushStreamBuffers();
            WriteTimestamped(
                $"llm-prepare reminder_in_history={reminderInHistory} reminder_in_llm={reminderInLlm}");
        }

        public void LogCompaction(string phase, string detail = null)
        {
            FlushStreamBuffers();
            string line = $"compaction: {phase}";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                line += $" {detail.Trim()}";
            }
            WriteTimestamped(line);
        }

        public void LogLlmError(string detail)
        {
            FlushStreamBuffers();
            WriteTimestamped("LLM error");
            if (!string.IsNullOrWhiteSpace(detail))
            {
                WriteBlock(TrimForLog(detail, MaxInlineChars));
            }
        }

        public void LogToolCalls(string toolCallsJson)
        {
            FlushStreamBuffers();
            WriteTimestamped("tool_calls");
            WriteBlock(TrimForLog(FormatToolCalls(toolCallsJson), MaxInlineChars));
        }

        public void LogToolResult(string toolCallId, string code, string resultContent, string preview)
        {
            FlushStreamBuffers();
            WriteTimestamped($"tool_result id={toolCallId}");
            if (!string.IsNullOrWhiteSpace(code))
            {
                WriteLine("code:");
                WriteBlock(TrimForLog(code, MaxInlineChars));
            }

            if (!string.IsNullOrWhiteSpace(preview))
            {
                WriteLine("preview:");
                WriteBlock(TrimForLog(preview, MaxInlineChars));
            }

            if (!string.IsNullOrWhiteSpace(resultContent) && resultContent != preview)
            {
                WriteLine("result:");
                WriteBlock(TrimForLog(resultContent, MaxInlineChars));
            }
        }

        public void LogProgress(string type, string text)
        {
            if (mEnded)
            {
                return;
            }

            switch (type)
            {
                case "complete":
                    return;
                case "stream_end":
                case "discard_stream":
                    mStreamBuf.Clear();
                    return;
                case "stream":
                    AppendStreamDelta(mStreamBuf, text);
                    return;
                case "thinking":
                    AppendStreamDelta(mThinkingBuf, text);
                    return;
                case "status":
                    if (text == mLastStatus)
                    {
                        return;
                    }

                    FlushStreamBuffers();
                    mLastStatus = text;
                    WriteTimestamped($"status: {text}");
                    return;
                case "answer":
                    FlushStreamBuffers();
                    WriteTimestamped("answer");
                    WriteBlock(TrimForLog(text, MaxInlineChars));
                    return;
                case "error":
                    FlushStreamBuffers();
                    WriteTimestamped("error");
                    WriteBlock(TrimForLog(text, MaxInlineChars));
                    return;
                case "tool_call":
                    FlushStreamBuffers();
                    WriteTimestamped("tool_call");
                    WriteBlock(TrimForLog(text, MaxInlineChars));
                    return;
                case "observation":
                    FlushStreamBuffers();
                    WriteTimestamped("observation");
                    WriteBlock(TrimForLog(text, MaxInlineChars));
                    return;
                default:
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return;
                    }

                    FlushStreamBuffers();
                    WriteTimestamped($"{type}");
                    WriteBlock(TrimForLog(text, MaxInlineChars));
                    return;
            }
        }

        public void EndTurn(string outcome, string finalText, bool isError)
        {
            if (mEnded)
            {
                return;
            }

            mEnded = true;
            FlushStreamBuffers();
            WriteTimestamped($"TURN END {outcome}{(isError ? " (error)" : "")}");
            if (outcome == "aborted")
            {
                WriteLine("  (paused, history preserved)");
            }
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                WriteLine("final:");
                WriteBlock(TrimForLog(finalText, MaxInlineChars));
            }

            WriteLine(new string('=', 80));

            try
            {
                mWriter.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgentSessionLogger] 关闭日志失败：{e.Message}");
            }
        }

        private void FlushStreamBuffers()
        {
            if (mThinkingBuf.Length > 0)
            {
                WriteTimestamped("thinking");
                WriteBlock(TrimForLog(mThinkingBuf.ToString(), MaxInlineChars));
                mThinkingBuf.Clear();
            }

            if (mStreamBuf.Length > 0)
            {
                WriteTimestamped("stream");
                WriteBlock(TrimForLog(mStreamBuf.ToString(), MaxInlineChars));
                mStreamBuf.Clear();
            }
        }

        private static void AppendStreamDelta(StringBuilder buf, string text)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            buf.Append(text);
        }

        private void WriteTimestamped(string line)
        {
            WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");
        }

        private void WriteLine(string line)
        {
            if (mWriter == null || mEnded)
            {
                return;
            }

            mWriter.WriteLine(line);
        }

        private void WriteBlock(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                WriteLine("  (empty)");
                return;
            }

            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    WriteLine($"  {line}");
                }
            }
        }

        private static string SummarizeRequestBody(string requestBody)
        {
            if (string.IsNullOrEmpty(requestBody))
            {
                return "  body: (empty)";
            }

            int messageHints = CountSubstring(requestBody, "\"role\"");
            return $"  body: {requestBody.Length} chars, ~{messageHints} messages";
        }

        private static int CountSubstring(string text, string needle)
        {
            int count = 0;
            int index = 0;
            while (index < text.Length)
            {
                int found = text.IndexOf(needle, index, StringComparison.Ordinal);
                if (found < 0)
                {
                    break;
                }

                count++;
                index = found + needle.Length;
            }

            return count;
        }

        private static string FormatToolCalls(string toolCallsJson)
        {
            if (string.IsNullOrWhiteSpace(toolCallsJson))
            {
                return "(empty)";
            }

            string code = UTAgentJsonExtract.GetString(toolCallsJson, "code");
            if (!string.IsNullOrEmpty(code))
            {
                return code;
            }

            return toolCallsJson.Trim();
        }

        private static string TrimForLog(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            if (text.Length <= maxChars)
            {
                return text;
            }

            return text.Substring(0, maxChars) + $"\n... ({text.Length - maxChars} chars truncated)";
        }
    }
}

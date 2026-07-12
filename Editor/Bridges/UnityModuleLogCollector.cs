using System;
using System.Collections.Generic;
using UnityEngine;

namespace UTAgent.Editor.Bridges
{
    /// <summary>
    /// 收集 Unity Console 日志，供 <see cref="UTAgentPythonBridge.GetRecentLogs"/> 查询。
    /// 在首次访问时自动订阅 <see cref="Application.logMessageReceivedThreaded"/>。
    /// </summary>
    public static class UnityModuleLogCollector
    {
        private const int Capacity = 200;
        private static readonly List<LogEntry> mEntries = new();
        private static bool mSubscribed;
        private static readonly object mLock = new();

        /// <summary>
        /// 日志条目。
        /// </summary>
        public readonly struct LogEntry
        {
            public readonly long Timestamp;
            public readonly LogType Type;
            public readonly string Message;
            public readonly string StackTrace;

            public LogEntry(long timestamp, LogType type, string message, string stackTrace)
            {
                Timestamp = timestamp;
                Type = type;
                Message = message;
                StackTrace = stackTrace;
            }
        }

        /// <summary>
        /// 日志计数摘要。
        /// </summary>
        public readonly struct LogSummary
        {
            public readonly int Log;
            public readonly int Warning;
            public readonly int Error;
            public readonly int Total;

            public LogSummary(int log, int warning, int error, int total)
            {
                Log = log;
                Warning = warning;
                Error = error;
                Total = total;
            }
        }

        /// <summary>
        /// 获取最近 N 条日志。
        /// </summary>
        public static IReadOnlyList<LogEntry> GetRecentLogs(int count)
        {
            EnsureSubscribed();
            lock (mLock)
            {
                var start = Math.Max(0, mEntries.Count - count);
                return mEntries.GetRange(start, mEntries.Count - start);
            }
        }

        /// <summary>
        /// 获取日志计数摘要。
        /// </summary>
        public static LogSummary GetSummary()
        {
            EnsureSubscribed();
            lock (mLock)
            {
                int log = 0;
                int warning = 0;
                int error = 0;
                foreach (var entry in mEntries)
                {
                    switch (entry.Type)
                    {
                        case LogType.Log:
                            log++;
                            break;
                        case LogType.Warning:
                            warning++;
                            break;
                        case LogType.Error:
                        case LogType.Exception:
                        case LogType.Assert:
                            error++;
                            break;
                    }
                }
                return new LogSummary(log, warning, error, mEntries.Count);
            }
        }

        private static void EnsureSubscribed()
        {
            if (mSubscribed)
            {
                return;
            }
            lock (mLock)
            {
                if (mSubscribed)
                {
                    return;
                }
                Application.logMessageReceivedThreaded += OnLogMessageReceived;
                mSubscribed = true;
            }
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            lock (mLock)
            {
                mEntries.Add(new LogEntry(DateTimeOffset.Now.ToUnixTimeMilliseconds(), type, condition, stackTrace));
                if (mEntries.Count > Capacity)
                {
                    mEntries.RemoveAt(0);
                }
            }
        }
    }
}

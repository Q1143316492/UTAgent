using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UTAgent.Editor.Config;
using UnityEngine;

namespace UTAgent.Editor.Agent
{
    /// <summary>
    /// Session 列表项（list / continueRecent 用）。
    /// </summary>
    public sealed class UTAgentSessionInfo
    {
        public string Id;
        public string Name;
        public string Path;
        public string Summary;
        public string CreatedAt;
        public DateTime ModifiedUtc;
        public int HistoryLen;
    }

    /// <summary>
    /// Pi 风格多 session JSONL：目录、current 指针、create/open/list/continueRecent。
    /// 消息体读写委托 Python <c>session_store</c>（经 agent.persist/load_session）。
    /// </summary>
    public sealed class UTAgentSessionManager
    {
        public const string CurrentPointerFileName = "current.session";

        public string CurrentSessionId { get; private set; }
        public string CurrentSessionName { get; private set; }
        public string CurrentCreatedAt { get; private set; }
        public string CurrentFilePath { get; private set; }

        public bool HasOpenSession
        {
            get { return !string.IsNullOrEmpty(CurrentSessionId) && !string.IsNullOrEmpty(CurrentFilePath); }
        }

        /// <summary>
        /// sessions 根目录：{ResolveLogDirectory()}/sessions/
        /// </summary>
        public static string ResolveSessionsDirectory()
        {
            return Path.Combine(UTAgentConfig.ResolveLogDirectory(), "sessions");
        }

        public static string EnsureSessionsDirectory()
        {
            string dir = ResolveSessionsDirectory();
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return dir;
        }

        public static string PointerPath()
        {
            return Path.Combine(EnsureSessionsDirectory(), CurrentPointerFileName);
        }

        public void WriteCurrentPointer(string sessionId)
        {
            string path = PointerPath();
            File.WriteAllText(path, sessionId ?? "", Encoding.UTF8);
        }

        public string ReadCurrentPointer()
        {
            string path = PointerPath();
            if (!File.Exists(path))
            {
                return "";
            }

            try
            {
                return (File.ReadAllText(path, Encoding.UTF8) ?? "").Trim();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgentSession] 读 current.session 失败：{e.Message}");
                return "";
            }
        }

        public string SessionFilePath(string sessionId)
        {
            return Path.Combine(EnsureSessionsDirectory(), sessionId + ".jsonl");
        }

        /// <summary>
        /// 绑定已打开的 session 元数据（load 成功后由 Runner 调用）。
        /// </summary>
        public void BindOpen(string sessionId, string filePath, string name, string createdAt)
        {
            CurrentSessionId = sessionId;
            CurrentFilePath = filePath;
            CurrentSessionName = name ?? "";
            CurrentCreatedAt = createdAt ?? "";
            WriteCurrentPointer(sessionId);
        }

        public void ClearOpen()
        {
            CurrentSessionId = null;
            CurrentFilePath = null;
            CurrentSessionName = null;
            CurrentCreatedAt = null;
        }

        /// <summary>
        /// 磁盘上该 session 是否无消息 entry（仅 header 或空文件）。
        /// </summary>
        public bool IsSessionEmptyOnDisk(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return true;
            }

            string path = SessionFilePath(sessionId.Trim());
            return CountMessageEntries(path) <= 0;
        }

        public static int CountMessageEntries(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return 0;
            }

            int count = 0;
            try
            {
                using (var reader = new StreamReader(filePath, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        if (line.Contains("\"type\":\"session_header\"")
                            || line.Contains("\"type\": \"session_header\""))
                        {
                            continue;
                        }

                        count++;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgentSession] CountMessageEntries 失败：{e.Message}");
                return 0;
            }

            return count;
        }

        /// <summary>
        /// 删除单个 session 文件；若是当前打开则 ClearOpen。
        /// </summary>
        public bool DeleteSession(string sessionId, out string message)
        {
            message = "";
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                message = "sessionId 为空";
                return false;
            }

            string id = sessionId.Trim();
            string path = SessionFilePath(id);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                string meta = path + ".meta";
                if (File.Exists(meta))
                {
                    File.Delete(meta);
                }
            }
            catch (Exception e)
            {
                message = e.Message;
                return false;
            }

            if (CurrentSessionId == id)
            {
                ClearOpen();
                WriteCurrentPointer("");
            }

            return true;
        }

        /// <summary>
        /// 删除所有无消息的空 session。
        /// </summary>
        public int DeleteEmptySessions()
        {
            int deleted = 0;
            foreach (UTAgentSessionInfo info in List())
            {
                if (info.HistoryLen > 0)
                {
                    continue;
                }

                if (DeleteSession(info.Id, out _))
                {
                    deleted++;
                }
            }

            return deleted;
        }

        /// <summary>
        /// 删除全部 session 文件。
        /// </summary>
        public int DeleteAllSessions()
        {
            int deleted = 0;
            foreach (UTAgentSessionInfo info in List())
            {
                if (DeleteSession(info.Id, out _))
                {
                    deleted++;
                }
            }

            ClearOpen();
            WriteCurrentPointer("");
            return deleted;
        }

        public List<UTAgentSessionInfo> List()
        {
            var result = new List<UTAgentSessionInfo>();
            string dir = EnsureSessionsDirectory();
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.jsonl");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgentSession] list 失败：{e.Message}");
                return result;
            }

            foreach (string file in files)
            {
                UTAgentSessionInfo info = ReadListInfo(file);
                if (info != null)
                {
                    result.Add(info);
                }
            }

            result.Sort((a, b) => b.ModifiedUtc.CompareTo(a.ModifiedUtc));
            return result;
        }

        public UTAgentSessionInfo FindMostRecent()
        {
            List<UTAgentSessionInfo> list = List();
            if (list.Count == 0)
            {
                return null;
            }

            string pointer = ReadCurrentPointer();
            if (!string.IsNullOrEmpty(pointer))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Id == pointer && list[i].HistoryLen > 0)
                    {
                        return list[i];
                    }
                }
            }

            // 优先非空会话，避免「新建」刷出的空壳盖住真实对话
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].HistoryLen > 0)
                {
                    return list[i];
                }
            }

            if (!string.IsNullOrEmpty(pointer))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Id == pointer)
                    {
                        return list[i];
                    }
                }
            }

            return list[0];
        }

        private static UTAgentSessionInfo ReadListInfo(string filePath)
        {
            try
            {
                string id = Path.GetFileNameWithoutExtension(filePath);
                string createdAt = "";
                string name = "";
                string summary = "";
                int historyLen = 0;
                using (var reader = new StreamReader(filePath, Encoding.UTF8))
                {
                    string first = reader.ReadLine();
                    if (!string.IsNullOrEmpty(first))
                    {
                        createdAt = UTAgentJsonExtract.GetString(first, "createdAt");
                        name = UTAgentJsonExtract.GetString(first, "name");
                        string headerId = UTAgentJsonExtract.GetString(first, "id");
                        if (!string.IsNullOrEmpty(headerId))
                        {
                            id = headerId;
                        }
                    }

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        historyLen++;
                        if (string.IsNullOrEmpty(summary) && line.Contains("\"role\""))
                        {
                            // 粗提取首条 user 文本（完整摘要由 load 时 Python 提供）
                            if (line.Contains("\"role\":\"user\"") || line.Contains("\"role\": \"user\""))
                            {
                                summary = ExtractMessageTextPreview(line);
                            }
                        }
                    }
                }

                DateTime modified = File.GetLastWriteTimeUtc(filePath);
                if (string.IsNullOrEmpty(summary))
                {
                    summary = !string.IsNullOrEmpty(name) ? name : id;
                }

                return new UTAgentSessionInfo
                {
                    Id = id,
                    Name = name,
                    Path = filePath,
                    Summary = Truncate(summary, 48),
                    CreatedAt = createdAt,
                    ModifiedUtc = modified,
                    HistoryLen = historyLen,
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgentSession] 读 {filePath} 失败：{e.Message}");
                return null;
            }
        }

        private static string ExtractMessageTextPreview(string line)
        {
            // "text":"..." 或 "content":"..."
            string text = UTAgentJsonExtract.GetString(line, "text");
            if (string.IsNullOrEmpty(text))
            {
                text = UTAgentJsonExtract.GetString(line, "content");
            }

            return text ?? "";
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
            {
                return s ?? "";
            }

            return s.Substring(0, max) + "…";
        }

        public string DisplayLabel()
        {
            if (!HasOpenSession)
            {
                return "(草稿)";
            }

            if (!string.IsNullOrEmpty(CurrentSessionName))
            {
                return CurrentSessionName;
            }

            string id = CurrentSessionId ?? "";
            if (id.Length > 8)
            {
                return id.Substring(0, 8);
            }

            return id;
        }
    }
}

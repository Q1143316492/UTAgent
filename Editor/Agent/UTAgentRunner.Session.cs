using System;
using System.Collections.Generic;
using System.IO;
using UTAgent.Editor.Core;
using UnityEngine;

namespace UTAgent.Editor.Agent
{
    public sealed partial class UTAgentRunner
    {
        private readonly UTAgentSessionManager mSession = new UTAgentSessionManager();
        /// <summary>
        /// configure() 会清空 Python `_history`，须从磁盘再灌一次。
        /// </summary>
        private bool mSessionHistoryNeedsReload = true;
        /// <summary>
        /// 新建后的草稿态：尚未落盘，禁止 ContinueRecent 把旧会话又打开。
        /// </summary>
        private bool mDraftUntilFirstMessage;

        /// <summary>
        /// 当前 SessionManager（Chat 列表/指示用）。
        /// </summary>
        public UTAgentSessionManager Session
        {
            get { return mSession; }
        }

        /// <summary>
        /// 标记须从磁盘重载 history（Configure / Invalidate 后）。
        /// </summary>
        public void MarkSessionHistoryNeedsReload()
        {
            mSessionHistoryNeedsReload = true;
        }

        /// <summary>
        /// 导出当前 Python `_history` JSON（含 ui_messages）。
        /// </summary>
        public string ExportHistoryJson()
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                return "";
            }

            return SafeExec(ModuleImport + "agent.export_history()\n");
        }

        /// <summary>
        /// 用 JSON 数组整体替换 `_history`。
        /// </summary>
        public bool ReplaceHistory(string messagesJson, out string message)
        {
            message = "";
            if (!UTAgentBootstrap.IsAvailable)
            {
                message = "引擎不可用";
                return false;
            }

            string output = SafeExec(ModuleImport +
                $"agent.replace_history({EscapePy(messagesJson ?? "[]")})\n");
            bool ok = ParseExecOk(output, out message);
            if (ok)
            {
                mSessionHistoryNeedsReload = false;
            }

            return ok;
        }

        /// <summary>
        /// 新建会话：清空 memory；当前已是空壳则不建新文件；有内容则脱离旧文件（懒创建，首条消息再落盘）。
        /// </summary>
        public bool NewSession(out string message)
        {
            message = "";
            ClearSteeringQueues();
            if (!UTAgentBootstrap.IsAvailable)
            {
                message = "引擎不可用";
                return false;
            }

            SafeExec(ModuleImport + "agent.clear_history()\n");

            if (mSession.HasOpenSession && mSession.IsSessionEmptyOnDisk(mSession.CurrentSessionId))
            {
                // 已是空会话：不刷新文件
                mSessionHistoryNeedsReload = false;
                mDraftUntilFirstMessage = false;
                message = "reused empty";
                return true;
            }

            // 有内容的旧会话保留在磁盘；当前进入「无文件草稿」，首条消息再 CreateSessionShell
            mSession.ClearOpen();
            mSession.WriteCurrentPointer("");
            mSessionHistoryNeedsReload = false;
            mDraftUntilFirstMessage = true;
            message = "draft";
            return true;
        }

        /// <summary>
        /// 创建空 session 文件并绑定为当前（不清 `_history`）。
        /// </summary>
        private bool CreateSessionShell(out string message)
        {
            message = "";
            string sessionId = Guid.NewGuid().ToString("N");
            string path = mSession.SessionFilePath(sessionId);
            string cwd = EscapePy(Directory.GetCurrentDirectory());
            string output = SafeExec(ModuleImport +
                $"agent.create_session_file({EscapePy(path)}, {EscapePy(sessionId)}, '', {cwd})\n");
            if (!ParseExecOk(output, out message))
            {
                return false;
            }

            string createdAt = ExtractString(output, "createdAt");
            if (string.IsNullOrEmpty(createdAt))
            {
                createdAt = DateTime.UtcNow.ToString("o");
            }

            mSession.BindOpen(sessionId, path, "", createdAt);
            mDraftUntilFirstMessage = false;
            return true;
        }

        /// <summary>
        /// 打开指定 session：灌入 `_history`，返回 load 原始 JSON（含 ui_messages）。
        /// </summary>
        public bool OpenSession(string sessionId, out string loadJson, out string message)
        {
            loadJson = "";
            message = "";
            if (!UTAgentBootstrap.IsAvailable)
            {
                message = "引擎不可用";
                return false;
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                message = "sessionId 为空";
                return false;
            }

            ClearSteeringQueues();
            string path = mSession.SessionFilePath(sessionId.Trim());
            if (!File.Exists(path))
            {
                message = "session 文件不存在";
                return false;
            }

            loadJson = SafeExec(ModuleImport + $"agent.load_session({EscapePy(path)})\n");
            if (!ParseExecOk(loadJson, out message))
            {
                return false;
            }

            string id = ExtractString(loadJson, "session_id");
            if (string.IsNullOrEmpty(id))
            {
                id = sessionId.Trim();
            }

            string name = ExtractString(loadJson, "name");
            string createdAt = ExtractString(loadJson, "createdAt");
            mSession.BindOpen(id, path, name, createdAt);
            mSessionHistoryNeedsReload = false;
            mDraftUntilFirstMessage = false;
            return true;
        }

        /// <summary>
        /// 续最近非空会话；没有则进入草稿（不落盘空文件）。
        /// </summary>
        public bool ContinueRecentSession(out string loadJson, out string message)
        {
            loadJson = "";
            message = "";
            UTAgentSessionInfo recent = mSession.FindMostRecent();
            if (recent == null)
            {
                return NewSession(out message);
            }

            if (recent.HistoryLen <= 0)
            {
                // 仅有空壳：打开其中一个作当前，仍不新建
                return OpenSession(recent.Id, out loadJson, out message);
            }

            return OpenSession(recent.Id, out loadJson, out message);
        }

        /// <summary>
        /// configure 清空 history 后：从当前/最近 session 再灌入。
        /// </summary>
        public bool BootstrapSessionAfterConfigure(out string loadJson, out string message)
        {
            return EnsureSessionHistorySynced(out loadJson, out message);
        }

        /// <summary>
        /// 若 configure 刚清空 history，或内存为空而磁盘有会话，则从磁盘同步。
        /// </summary>
        public bool EnsureSessionHistorySynced(out string loadJson, out string message)
        {
            loadJson = "";
            message = "";
            if (!UTAgentBootstrap.IsAvailable || !mConfigured)
            {
                message = "未配置";
                return false;
            }

            int len = GetHistoryLength();
            if (!mSessionHistoryNeedsReload && len > 0)
            {
                return true;
            }

            // 用户点了新建、尚未发首条：保持空草稿，不要续最近
            if (mDraftUntilFirstMessage && !mSession.HasOpenSession)
            {
                message = "draft";
                return true;
            }

            // 内存空但磁盘有内容：无论 dirty 标志，强制灌回（防 configure 后失忆）
            if (len == 0
                && mSession.HasOpenSession
                && !mSession.IsSessionEmptyOnDisk(mSession.CurrentSessionId))
            {
                bool forced = OpenSession(mSession.CurrentSessionId, out loadJson, out message);
                if (forced)
                {
                    mSessionHistoryNeedsReload = false;
                }

                return forced;
            }

            if (mSession.HasOpenSession)
            {
                bool ok = OpenSession(mSession.CurrentSessionId, out loadJson, out message);
                if (ok)
                {
                    mSessionHistoryNeedsReload = false;
                }

                return ok;
            }

            bool continued = ContinueRecentSession(out loadJson, out message);
            if (continued)
            {
                mSessionHistoryNeedsReload = false;
            }

            return continued;
        }

        /// <summary>
        /// 将当前 `_history` 覆盖写入打开的 session 文件。
        /// </summary>
        public void PersistOpenSession()
        {
            if (!mSession.HasOpenSession || !UTAgentBootstrap.IsAvailable)
            {
                return;
            }

            try
            {
                string name = EscapePy(mSession.CurrentSessionName ?? "");
                string cwd = EscapePy(Directory.GetCurrentDirectory());
                string created = string.IsNullOrEmpty(mSession.CurrentCreatedAt)
                    ? "None"
                    : EscapePy(mSession.CurrentCreatedAt);
                SafeExec(ModuleImport +
                    $"agent.persist_session({EscapePy(mSession.CurrentFilePath)}, {EscapePy(mSession.CurrentSessionId)}, {name}, {cwd}, {created})\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgentRunner] persist_session 失败：{e.Message}");
            }
        }

        /// <summary>
        /// history 变更后同步落盘。无打开会话时首条消息才建壳。
        /// </summary>
        private void PersistHistoryMutation()
        {
            if (!mSession.HasOpenSession)
            {
                if (!CreateSessionShell(out _))
                {
                    return;
                }
            }

            PersistOpenSession();
        }

        /// <summary>
        /// Clear = 新建会话（保留其它 JSONL）。
        /// </summary>
        public bool ClearToNewSession(out string message)
        {
            return NewSession(out message);
        }

        /// <summary>
        /// 删除 session；若删的是当前则 NewSession（草稿）。
        /// </summary>
        public bool DeleteSession(string sessionId, out string message)
        {
            bool wasCurrent = mSession.HasOpenSession
                && string.Equals(mSession.CurrentSessionId, sessionId, StringComparison.Ordinal);
            if (!mSession.DeleteSession(sessionId, out message))
            {
                return false;
            }

            if (wasCurrent)
            {
                return NewSession(out message);
            }

            return true;
        }

        /// <summary>
        /// 从 load/export JSON 中提取 ui_messages 数组的原始子串（尽力）。
        /// </summary>
        public static bool TryExtractUiMessagesArray(string json, out string arrayJson)
        {
            arrayJson = "";
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            const string key = "\"ui_messages\"";
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0)
            {
                return false;
            }

            int i = idx + key.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == ':' || json[i] == '\n' || json[i] == '\r'))
            {
                i++;
            }

            if (i >= json.Length || json[i] != '[')
            {
                return false;
            }

            int start = i;
            int depth = 0;
            bool inString = false;
            bool escape = false;
            for (; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        arrayJson = json.Substring(start, i - start + 1);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 解析 ui_messages 为 (isUser, text, blockType) 列表。
        /// </summary>
        public static List<(bool isUser, string text, string block)> ParseUiMessages(string loadOrExportJson)
        {
            var list = new List<(bool, string, string)>();
            if (!TryExtractUiMessagesArray(loadOrExportJson, out string arrayJson))
            {
                return list;
            }

            int pos = 0;
            while (pos < arrayJson.Length)
            {
                int objStart = arrayJson.IndexOf('{', pos);
                if (objStart < 0)
                {
                    break;
                }

                int depth = 0;
                int objEnd = -1;
                bool inString = false;
                bool escape = false;
                for (int i = objStart; i < arrayJson.Length; i++)
                {
                    char c = arrayJson[i];
                    if (inString)
                    {
                        if (escape)
                        {
                            escape = false;
                            continue;
                        }

                        if (c == '\\')
                        {
                            escape = true;
                            continue;
                        }

                        if (c == '"')
                        {
                            inString = false;
                        }

                        continue;
                    }

                    if (c == '"')
                    {
                        inString = true;
                        continue;
                    }

                    if (c == '{')
                    {
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            objEnd = i;
                            break;
                        }
                    }
                }

                if (objEnd < 0)
                {
                    break;
                }

                string obj = arrayJson.Substring(objStart, objEnd - objStart + 1);
                string role = UTAgentJsonExtract.GetString(obj, "role");
                string text = UTAgentJsonExtract.GetString(obj, "text");
                string block = UTAgentJsonExtract.GetString(obj, "block");
                if (role == "user")
                {
                    list.Add((true, text ?? "", ""));
                }
                else if (role == "assistant")
                {
                    list.Add((false, text ?? "", string.IsNullOrEmpty(block) ? "answer" : block));
                }

                pos = objEnd + 1;
            }

            return list;
        }
    }
}

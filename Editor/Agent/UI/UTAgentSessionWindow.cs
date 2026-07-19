using System;
using System.Collections.Generic;
using UTAgent.Editor.Config;
using UTAgent.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace UTAgent.Editor.Agent
{
    /// <summary>
    /// 会话管理：打开 / 删除 / 清理空会话 / 清空全部。
    /// </summary>
    public sealed class UTAgentSessionWindow : EditorWindow
    {
        private UTAgentRunner mRunner;
        private Action mOnChanged;
        private Vector2 mScroll;
        private List<UTAgentSessionInfo> mList = new List<UTAgentSessionInfo>();

        public static void Open(UTAgentRunner runner, Action onChanged)
        {
            var win = GetWindow<UTAgentSessionWindow>(true, "UT Agent 会话", true);
            win.minSize = new Vector2(420, 320);
            win.mRunner = runner;
            win.mOnChanged = onChanged;
            win.RefreshList();
            win.ShowUtility();
        }

        private void OnEnable()
        {
            RefreshList();
        }

        private void RefreshList()
        {
            if (mRunner == null)
            {
                mList = new List<UTAgentSessionInfo>();
                return;
            }

            mList = mRunner.Session.List();
            Repaint();
        }

        private void OnGUI()
        {
            if (mRunner == null)
            {
                EditorGUILayout.HelpBox("未绑定 Runner。请从 Agent Chat 打开。", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("会话目录", UTAgentSessionManager.ResolveSessionsDirectory());
            string current = mRunner.Session.HasOpenSession
                ? mRunner.Session.DisplayLabel()
                : "(草稿 / 未落盘)";
            EditorGUILayout.LabelField("当前", current);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新", GUILayout.Width(56)))
            {
                RefreshList();
            }
            if (GUILayout.Button("删除全部空会话", GUILayout.Width(120)))
            {
                int n = mRunner.Session.DeleteEmptySessions();
                EditorUtility.DisplayDialog("清理", $"已删除 {n} 个空会话。", "OK");
                NotifyChanged();
                RefreshList();
            }
            GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
            if (GUILayout.Button("清空全部会话…", GUILayout.Width(110)))
            {
                if (EditorUtility.DisplayDialog(
                        "清空全部会话",
                        "将删除 sessions 目录下所有 JSONL，且不可恢复。确定？",
                        "全部删除",
                        "取消"))
                {
                    int n = mRunner.Session.DeleteAllSessions();
                    mRunner.ClearHistoryAndNewSession();
                    EditorUtility.DisplayDialog("清空", $"已删除 {n} 个会话。", "OK");
                    NotifyChanged();
                    RefreshList();
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            mScroll = EditorGUILayout.BeginScrollView(mScroll);
            if (mList.Count == 0)
            {
                EditorGUILayout.HelpBox("暂无已保存会话。发送首条消息后会自动创建。", MessageType.Info);
            }
            else
            {
                foreach (UTAgentSessionInfo info in mList)
                {
                    DrawRow(info);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawRow(UTAgentSessionInfo info)
        {
            bool isCurrent = mRunner.Session.CurrentSessionId == info.Id;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            string mark = isCurrent ? "● " : "○ ";
            string empty = info.HistoryLen <= 0 ? " [空]" : $" ({info.HistoryLen} 条)";
            EditorGUILayout.LabelField(
                mark + info.ModifiedUtc.ToLocalTime().ToString("MM-dd HH:mm") + empty,
                EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(info.Summary, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(info.Id, EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开", GUILayout.Width(56)))
            {
                OpenSession(info.Id);
            }
            GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
            if (GUILayout.Button("删除", GUILayout.Width(56)))
            {
                if (EditorUtility.DisplayDialog(
                        "删除会话",
                        $"删除会话？\n{info.Summary}\n{info.Id}",
                        "删除",
                        "取消"))
                {
                    if (mRunner.DeleteSession(info.Id, out string err))
                    {
                        NotifyChanged();
                        RefreshList();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("删除失败", err ?? "", "OK");
                    }
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        private void OpenSession(string sessionId)
        {
            UTAgentReadiness.Status readiness = UTAgentReadiness.TryEnsureChatReady(mRunner);
            if (!readiness.Ready)
            {
                EditorUtility.DisplayDialog("打开会话", readiness.Summary + "\n" + readiness.Detail, "OK");
                return;
            }

            if (mRunner.HasActiveTurn)
            {
                EditorUtility.DisplayDialog("打开会话", "请先等待当前回合结束或 Stop。", "OK");
                return;
            }

            if (!mRunner.OpenSession(sessionId, out _, out string err))
            {
                EditorUtility.DisplayDialog("打开失败", err ?? "", "OK");
                return;
            }

            NotifyChanged();
            RefreshList();
            Close();
        }

        private void NotifyChanged()
        {
            mOnChanged?.Invoke();
        }
    }
}

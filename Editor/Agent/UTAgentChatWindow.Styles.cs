using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UTAgent.Editor.Core;

namespace UTAgent.Editor.Agent
{
    public partial class UTAgentChatWindow
    {
        private static Texture2D MakeRoundedTex(int w, int h, Color color, int r)
        {
            var t = new Texture2D(w, h);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int cx = 0, cy = 0;
                    bool corner = false;
                    if (x < r && y < r)               { cx = r;     cy = r;     corner = true; }
                    else if (x >= w - r && y < r)      { cx = w - r; cy = r;     corner = true; }
                    else if (x < r && y >= h - r)      { cx = r;     cy = h - r; corner = true; }
                    else if (x >= w - r && y >= h - r) { cx = w - r; cy = h - r; corner = true; }
                    if (corner)
                    {
                        float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        if (dist > r) px[y * w + x] = Color.clear;
                        else if (dist > r - 1.5f) px[y * w + x] = new Color(color.r, color.g, color.b, color.a * (r - dist) / 1.5f);
                        else px[y * w + x] = color;
                    }
                    else
                    {
                        px[y * w + x] = color;
                    }
                }
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static void DestroyTex(ref Texture2D t)
        {
            if (t != null) { UnityEngine.Object.DestroyImmediate(t); t = null; }
        }

        private void InitStyles()
        {
            DestroyTex(ref mUserBubbleTex);
            DestroyTex(ref mAgentBubbleTex);
            DestroyTex(ref mCopyBtnNormalTex);
            DestroyTex(ref mCopyBtnHoverTex);
            bool dark = EditorGUIUtility.isProSkin;

            Color userBg = dark ? new Color(0.20f, 0.40f, 0.70f, 0.85f) : new Color(0.26f, 0.52f, 0.96f, 0.90f);
            Color agentBg = dark ? new Color(0.22f, 0.24f, 0.28f, 0.90f) : new Color(0.92f, 0.93f, 0.95f, 0.95f);
            mUserBubbleTex = MakeRoundedTex(32, 32, userBg, 8);
            mAgentBubbleTex = MakeRoundedTex(32, 32, agentBg, 8);

            Color copyBtnNormal = dark ? new Color(0.35f, 0.38f, 0.45f, 0.85f) : new Color(0.70f, 0.72f, 0.78f, 0.85f);
            Color copyBtnHover = dark ? new Color(0.45f, 0.50f, 0.60f, 0.95f) : new Color(0.55f, 0.58f, 0.65f, 0.95f);
            mCopyBtnNormalTex = MakeRoundedTex(16, 16, copyBtnNormal, 3);
            mCopyBtnHoverTex = MakeRoundedTex(16, 16, copyBtnHover, 3);

            mUserBubbleStyle = new GUIStyle
            {
                normal = { background = mUserBubbleTex },
                border = new RectOffset(10, 10, 10, 10),
                padding = new RectOffset(12, 12, 8, 8),
                margin = new RectOffset(60, 8, 2, 2),
                wordWrap = true,
                fontSize = 12,
            };

            mAgentBubbleStyle = new GUIStyle
            {
                normal = { background = mAgentBubbleTex },
                border = new RectOffset(10, 10, 10, 10),
                padding = new RectOffset(12, 12, 8, 8),
                margin = new RectOffset(8, 60, 2, 2),
                wordWrap = true,
                fontSize = 12,
            };

            mUserLabelStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 12,
                richText = true,
                wordWrap = true,
                stretchWidth = false,
                normal = { textColor = dark ? new Color(0.95f, 0.95f, 1f) : Color.white },
            };

            mAgentLabelStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 12,
                richText = true,
                wordWrap = true,
                stretchWidth = false,
                normal = { textColor = dark ? new Color(0.85f, 0.87f, 0.90f) : new Color(0.15f, 0.15f, 0.20f) },
            };

            mTimestampStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                normal = { textColor = dark ? new Color(0.5f, 0.52f, 0.58f) : new Color(0.55f, 0.55f, 0.60f) },
            };

            mTextStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 12,
                richText = true,
                wordWrap = true,
                stretchWidth = false,
                normal = { textColor = dark ? new Color(0.85f, 0.87f, 0.90f) : new Color(0.15f, 0.15f, 0.20f) },
            };

            mStatusStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                fontStyle = FontStyle.Italic,
                wordWrap = true,
                stretchWidth = false,
                normal = { textColor = dark ? new Color(0.55f, 0.58f, 0.65f) : new Color(0.50f, 0.52f, 0.58f) },
            };

            mCodeStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 11,
                richText = false,
                wordWrap = true,
                stretchWidth = false,
                normal = { textColor = dark ? new Color(0.7f, 0.85f, 1f) : new Color(0.05f, 0.2f, 0.5f) },
                padding = new RectOffset(8, 8, 4, 4),
            };

            mThinkingStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 11,
                richText = false,
                wordWrap = true,
                stretchWidth = false,
                normal = { textColor = dark ? new Color(0.65f, 0.62f, 0.78f) : new Color(0.40f, 0.35f, 0.55f) },
                fontStyle = FontStyle.Italic,
                padding = new RectOffset(8, 8, 4, 4),
            };

            mObservationStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 11,
                richText = false,
                wordWrap = true,
                stretchWidth = false,
                normal = { textColor = dark ? new Color(0.5f, 0.85f, 0.55f) : new Color(0.1f, 0.4f, 0.2f) },
                padding = new RectOffset(8, 8, 4, 4),
            };

            mErrorStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 12,
                wordWrap = true,
                stretchWidth = false,
                normal = { textColor = dark ? new Color(1f, 0.55f, 0.50f) : new Color(0.75f, 0.15f, 0.12f) },
                padding = new RectOffset(8, 8, 4, 4),
            };

            mInputStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                fontSize = 13,
                padding = new RectOffset(6, 6, 6, 6),
            };
        }

        private void DrawMessage(ChatMessage msg, int idx)
        {
            if (msg.IsUser)
                DrawUserMessage(msg, idx);
            else
                DrawAgentMessage(msg, idx);
        }

        private float GetMessageListContentWidth()
        {
            const float scrollbarAndPadding = 24f;
            return Mathf.Max(120f, position.width - scrollbarAndPadding);
        }

        private float GetBubbleContentWidth()
        {
            float bubbleColumn = position.width * 0.72f;
            float inner = bubbleColumn - mUserBubbleStyle.padding.horizontal - 4f;
            return Mathf.Max(120f, inner);
        }

        private void DrawSelectableText(string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            float width = GetBubbleContentWidth();
            float height = style.CalcHeight(new GUIContent(text), width);
            EditorGUILayout.SelectableLabel(
                text,
                style,
                GUILayout.Width(width),
                GUILayout.MaxWidth(width),
                GUILayout.Height(Mathf.Max(height, EditorGUIUtility.singleLineHeight)));
        }

        private void DrawUserMessage(ChatMessage msg, int idx)
        {
            float columnWidth = position.width * 0.72f;
            EditorGUILayout.BeginHorizontal(GUILayout.MaxWidth(GetMessageListContentWidth()));
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(GUILayout.Width(columnWidth), GUILayout.MaxWidth(columnWidth));
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(msg.Timestamp, mTimestampStyle);
            GUILayout.Space(4);
            GUILayout.Label("You 👤", new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold });
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginVertical(mUserBubbleStyle);
            DrawSelectableText(msg.Text, mUserLabelStyle);
            EditorGUILayout.EndVertical();
            Rect bubbleRect = GUILayoutUtility.GetLastRect();
            HandleBubbleInteraction(bubbleRect, msg, idx);
            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAgentMessage(ChatMessage msg, int idx)
        {
            float columnWidth = position.width * 0.72f;
            EditorGUILayout.BeginHorizontal(GUILayout.MaxWidth(GetMessageListContentWidth()));
            GUILayout.Space(4);
            EditorGUILayout.BeginVertical(GUILayout.Width(columnWidth), GUILayout.MaxWidth(columnWidth));
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("✨ Agent", new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold });
            GUILayout.Space(4);
            GUILayout.Label(msg.Timestamp, mTimestampStyle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginVertical(mAgentBubbleStyle);
            if (msg.Blocks == null || msg.Blocks.Count == 0)
                GUILayout.Label("(执行中…)", mStatusStyle);
            else
                DrawMessageBlocks(msg.Blocks);
            EditorGUILayout.EndVertical();
            Rect bubbleRect = GUILayoutUtility.GetLastRect();
            HandleBubbleInteraction(bubbleRect, msg, idx);
            if (msg.ShowContinue && !mWaiting)
            {
                GUILayout.Space(4);
                if (GUILayout.Button("▶ 续跑（保留上下文）", GUILayout.Height(22)))
                {
                    ContinueFromMessage(msg);
                }
            }
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMessageBlocks(List<MessageBlock> blocks)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                if (i > 0)
                {
                    GUILayout.Space(6);
                    DrawBlockSeparator();
                    GUILayout.Space(4);
                }
                switch (blocks[i].Type)
                {
                    case "status":
                        DrawStatusBlock(blocks[i]);
                        break;
                    case "max_steps_status":
                        DrawMaxStepsStatusBlock(blocks[i]);
                        break;
                    case "thinking":
                        DrawThinkingBlock(blocks[i]);
                        break;
                    case "tool_call":
                        DrawToolCallBlock(blocks[i]);
                        break;
                    case "observation":
                        DrawObservationBlock(blocks[i]);
                        break;
                    case "stream":
                        DrawStreamBlock(blocks[i]);
                        break;
                    case "error":
                        DrawErrorBlock(blocks[i]);
                        break;
                    default:
                        DrawAnswerBlock(blocks[i]);
                        break;
                }
            }
        }

        private void DrawBlockSeparator()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            Color sep = EditorGUIUtility.isProSkin
                ? new Color(0.4f, 0.42f, 0.48f, 0.35f)
                : new Color(0.75f, 0.77f, 0.82f, 0.55f);
            EditorGUI.DrawRect(rect, sep);
        }

        private void DrawStatusBlock(MessageBlock block)
        {
            if (string.IsNullOrWhiteSpace(block.Text))
            {
                return;
            }

            float width = GetBubbleContentWidth();
            GUILayout.Label(block.Text, mStatusStyle, GUILayout.MaxWidth(width));
        }

        private void DrawMaxStepsStatusBlock(MessageBlock block)
        {
            if (string.IsNullOrWhiteSpace(block.Text))
            {
                return;
            }

            DrawBorderedBlock(
                EditorGUIUtility.isProSkin ? new Color(0.85f, 0.65f, 0.25f) : new Color(0.75f, 0.55f, 0.10f),
                EditorGUIUtility.isProSkin ? new Color(0.22f, 0.20f, 0.14f, 0.55f) : new Color(1f, 0.97f, 0.90f, 0.9f),
                () =>
                {
                    GUILayout.Label("⏱ 步数上限", mStatusStyle);
                    float width = GetBubbleContentWidth();
                    GUILayout.Label(block.Text, mStatusStyle, GUILayout.MaxWidth(width));
                });
        }

        private void DrawThinkingBlock(MessageBlock block)
        {
            DrawBorderedBlock(
                EditorGUIUtility.isProSkin ? new Color(0.55f, 0.45f, 0.75f) : new Color(0.55f, 0.40f, 0.70f),
                EditorGUIUtility.isProSkin ? new Color(0.18f, 0.17f, 0.22f, 0.5f) : new Color(0.94f, 0.92f, 0.98f, 0.8f),
                () =>
                {
                    GUILayout.Label("💭 Thinking", mThinkingStyle);
                    if (!string.IsNullOrEmpty(block.Text))
                        DrawSelectableText(block.Text, mThinkingStyle);
                });
        }

        private void DrawToolCallBlock(MessageBlock block)
        {
            DrawBorderedBlock(
                EditorGUIUtility.isProSkin ? new Color(0.35f, 0.55f, 0.85f) : new Color(0.20f, 0.45f, 0.75f),
                EditorGUIUtility.isProSkin ? new Color(0.12f, 0.14f, 0.18f, 0.85f) : new Color(0.90f, 0.93f, 0.97f, 0.95f),
                () =>
                {
                    GUILayout.Label("[CALL] execPython", EditorStyles.miniLabel);
                    if (!string.IsNullOrEmpty(block.Text))
                    {
                        DrawSelectableText(block.Text, mCodeStyle);
                    }
                });
        }

        private void DrawObservationBlock(MessageBlock block)
        {
            DrawBorderedBlock(
                EditorGUIUtility.isProSkin ? new Color(0.35f, 0.75f, 0.45f) : new Color(0.15f, 0.55f, 0.30f),
                EditorGUIUtility.isProSkin ? new Color(0.14f, 0.20f, 0.16f, 0.5f) : new Color(0.92f, 0.98f, 0.93f, 0.85f),
                () =>
                {
                    GUILayout.Label("👁 Observation", mObservationStyle);
                    if (!string.IsNullOrEmpty(block.Text))
                        DrawSelectableText(block.Text, mObservationStyle);
                });
        }

        private void DrawStreamBlock(MessageBlock block)
        {
            string text = block.Text ?? "";
            if (block.IsStreaming)
                text += " ▌";
            if (string.IsNullOrEmpty(text))
                return;
            DrawSelectableText(text, mTextStyle);
        }

        private void DrawAnswerBlock(MessageBlock block)
        {
            if (string.IsNullOrWhiteSpace(block.Text))
                return;
            DrawSelectableText(block.Text, mAgentLabelStyle);
        }

        private void DrawErrorBlock(MessageBlock block)
        {
            DrawBorderedBlock(
                EditorGUIUtility.isProSkin ? new Color(0.90f, 0.35f, 0.30f) : new Color(0.80f, 0.20f, 0.15f),
                EditorGUIUtility.isProSkin ? new Color(0.22f, 0.14f, 0.14f, 0.55f) : new Color(1f, 0.94f, 0.94f, 0.9f),
                () =>
                {
                    GUILayout.Label("✕ Error", mErrorStyle);
                    if (!string.IsNullOrEmpty(block.Text))
                    {
                        float width = GetBubbleContentWidth();
                        GUILayout.Label(block.Text, mErrorStyle, GUILayout.MaxWidth(width));
                    }
                });
        }

        private void DrawBorderedBlock(Color borderColor, Color bgColor, System.Action drawContent)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            drawContent();
            EditorGUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint)
            {
                Rect blockRect = GUILayoutUtility.GetLastRect();
                EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.y, 3f, blockRect.height), borderColor);
            }
        }

        private void HandleBubbleInteraction(Rect bubbleRect, ChatMessage msg, int idx)
        {
            Event evt = Event.current;
            bool isHovering = bubbleRect.Contains(evt.mousePosition);
            string textToCopy = GetDisplayText(msg);

            if (isHovering && evt.type == EventType.ContextClick)
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("Copy Message"), false, () =>
                {
                    EditorGUIUtility.systemCopyBuffer = textToCopy;
                    mCopiedMessageIndex = idx;
                    mCopiedMessageTime = EditorApplication.timeSinceStartup;
                    Repaint();
                });
                menu.ShowAsContext();
                evt.Use();
            }

            bool showCopied = mCopiedMessageIndex == idx
                && EditorApplication.timeSinceStartup - mCopiedMessageTime < 1.5;
            if (isHovering || showCopied)
            {
                float btnWidth = showCopied ? 56f : 22f;
                float padding = 4f;
                Rect btnRect = new Rect(
                    bubbleRect.xMax - btnWidth - padding,
                    bubbleRect.yMin + padding,
                    btnWidth,
                    20f);

                if (showCopied)
                {
                    var copiedStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontSize = 10,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { background = mCopyBtnNormalTex, textColor = new Color(0.3f, 0.9f, 0.4f) },
                        border = new RectOffset(3, 3, 3, 3),
                        padding = new RectOffset(4, 4, 2, 2),
                    };
                    GUI.Label(btnRect, "✓ Copied", copiedStyle);
                    Repaint();
                }
                else
                {
                    bool hover = btnRect.Contains(evt.mousePosition);
                    var copyBtnStyle = new GUIStyle
                    {
                        fontSize = 12,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { background = hover ? mCopyBtnHoverTex : mCopyBtnNormalTex, textColor = Color.white },
                        border = new RectOffset(3, 3, 3, 3),
                        padding = new RectOffset(2, 2, 2, 2),
                    };
                    GUIContent copyIcon = EditorGUIUtility.IconContent("Clipboard");
                    if (copyIcon == null || copyIcon.image == null)
                        copyIcon = new GUIContent("📋");
                    if (GUI.Button(btnRect, copyIcon, copyBtnStyle))
                    {
                        EditorGUIUtility.systemCopyBuffer = textToCopy;
                        mCopiedMessageIndex = idx;
                        mCopiedMessageTime = EditorApplication.timeSinceStartup;
                        Repaint();
                    }
                }

                if (showCopied && EditorApplication.timeSinceStartup - mCopiedMessageTime >= 1.5)
                    mCopiedMessageIndex = -1;
            }

            if (isHovering && evt.type == EventType.Repaint)
                Repaint();
        }
    }
}

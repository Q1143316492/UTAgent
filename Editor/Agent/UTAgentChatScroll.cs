using System;
using UnityEditor;
using UnityEngine;

namespace UTAgent.Editor
{
    /// <summary>
    /// 消息列表滚动。贴底仅在 Layout 且内容变长时跳一次。
    /// 滚轮：子控件（SelectableLabel）会吃掉事件，因此在消息区手动处理滚轮并退出贴底。
    /// </summary>
    internal sealed class UTAgentChatScroll
    {
        private const float UnstickEpsilon = 2f;
        private const float ScrollWheelStep = 20f;

        private Vector2 mScroll;
        private bool mStickToBottom = true;
        private bool mForceScrollToBottom;
        private long mLastContentVersion = -1;
        private float mBottomAnchorY;
        private Rect mScrollViewRect;

        public void Reset()
        {
            mScroll = Vector2.zero;
            mStickToBottom = true;
            mForceScrollToBottom = false;
            mLastContentVersion = -1;
            mBottomAnchorY = 0f;
            mScrollViewRect = Rect.zero;
        }

        public void OnSend()
        {
            mStickToBottom = true;
            mForceScrollToBottom = true;
        }

        public void Draw(Action drawContent, bool showVerticalScrollbar, long contentVersion)
        {
            bool contentGrew = contentVersion != mLastContentVersion;
            if (contentGrew)
            {
                mLastContentVersion = contentVersion;
            }

            Event evt = Event.current;
            if (evt.type == EventType.ScrollWheel && IsMouseOverScrollView(evt))
            {
                UnstickAndApplyWheel(evt);
            }

            if (mStickToBottom
                && evt.type == EventType.Layout
                && (contentGrew || mForceScrollToBottom))
            {
                mScroll.y = float.MaxValue;
                mForceScrollToBottom = false;
            }

            mScroll.x = 0f;
            mScroll = EditorGUILayout.BeginScrollView(
                mScroll,
                false,
                showVerticalScrollbar,
                GUILayout.ExpandHeight(true));

            drawContent?.Invoke();

            EditorGUILayout.EndScrollView();
            mScroll.x = 0f;

            if (evt.type == EventType.Repaint || evt.type == EventType.Layout)
            {
                Rect rect = GUILayoutUtility.GetLastRect();
                if (rect.width > 1f)
                {
                    mScrollViewRect = rect;
                }
            }

            HandleDragIntent(evt);
        }

        private bool IsMouseOverScrollView(Event evt)
        {
            return mScrollViewRect.width > 1f && mScrollViewRect.Contains(evt.mousePosition);
        }

        private void UnstickAndApplyWheel(Event evt)
        {
            mStickToBottom = false;
            mScroll.y = Mathf.Max(0f, mScroll.y + evt.delta.y * ScrollWheelStep);
            evt.Use();
        }

        private void HandleDragIntent(Event evt)
        {
            if (evt.type == EventType.Repaint)
            {
                if (mStickToBottom)
                {
                    mBottomAnchorY = mScroll.y;
                }

                return;
            }

            if (evt.type != EventType.MouseDrag)
            {
                return;
            }

            if (!IsMouseOverScrollView(evt))
            {
                return;
            }

            if (mScroll.y < mBottomAnchorY - UnstickEpsilon)
            {
                mStickToBottom = false;
            }
        }
    }
}

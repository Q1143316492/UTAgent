using UnityEditor;
using UnityEngine;
using UTAgent.Editor.Core;

namespace UTAgent.Editor.Agent
{
    public partial class UTAgentChatWindow
    {
        private const float MessageListBottomPadding = 12f;
        private const float MessageSpacing = 4f;

        private long GetMessageContentVersion()
        {
            long version = mMessages.Count;
            for (int i = 0; i < mMessages.Count; i++)
            {
                ChatMessage msg = mMessages[i];
                version = unchecked(version * 31 + (msg.Text?.Length ?? 0));
                if (msg.Blocks == null)
                {
                    continue;
                }

                version = unchecked(version * 31 + msg.Blocks.Count);
                for (int b = 0; b < msg.Blocks.Count; b++)
                {
                    MessageBlock block = msg.Blocks[b];
                    version = unchecked(version * 31 + (block.Text?.Length ?? 0));
                }
            }

            return version;
        }

        private void DrawMessageListContent()
        {
            EditorGUILayout.BeginVertical(GUILayout.MaxWidth(GetMessageListContentWidth()));
            if (mMessages.Count == 0)
            {
                DrawWelcome();
            }
            else
            {
                for (int i = 0; i < mMessages.Count; i++)
                {
                    DrawMessage(mMessages[i], i);
                    GUILayout.Space(MessageSpacing);
                }

                GUILayout.Space(MessageListBottomPadding);
            }

            EditorGUILayout.EndVertical();
        }
    }
}

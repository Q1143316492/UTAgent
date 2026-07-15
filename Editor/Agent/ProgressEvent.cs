using System.Collections.Generic;

namespace UTAgent.Editor.Agent
{
    /// <summary>
    /// 结构化进度事件。Runner 推送给 UI，UI 按 Type 区分渲染。
    /// </summary>
    public struct ProgressEvent
    {
        public string Type;
        public string Text;
    }

    /// <summary>
    /// Agent 一轮结束回调：finalText、isError、outcome（success/error/aborted/max_steps_summary 等）、events。
    /// </summary>
    public delegate void TurnResponseHandler(
        string finalText, bool isError, string outcome, List<ProgressEvent> events);
}

using System.Text;

namespace UTAgent.Editor
{
    /// <summary>
    /// Python 输出捕获容器。__pybridge__ 模块的 log/err 把文本写进这里，Exec 取回。
    /// public 是为了让 pythonnet 反射绑定能访问 AppendOut/AppendErr。
    /// </summary>
    public sealed class BridgeSink
    {
        private readonly StringBuilder mOutput;
        private readonly StringBuilder mError;

        public BridgeSink(StringBuilder output, StringBuilder error)
        {
            mOutput = output;
            mError = error;
        }

        public void AppendOut(string text)
        {
            mOutput.Append(text);
        }

        public void AppendErr(string text)
        {
            mError.Append(text);
        }
    }
}

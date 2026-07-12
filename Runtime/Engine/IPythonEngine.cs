namespace UTAgent
{
    /// <summary>
    /// Python 引擎抽象接口。隐藏具体解释器实现（pythonnet / 移动端自定义 C API），
    /// 使上层 `unity` 模块等代码不依赖任何特定嵌入技术。
    /// </summary>
    public interface IPythonEngine
    {
        /// <summary>
        /// 当前引擎是否可用（已初始化且未因域重载等原因失效）。
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// 初始化 Python 解释器及相关环境。
        /// </summary>
        void Initialize();

        /// <summary>
        /// 关闭 Python 解释器并释放资源。未初始化时安全跳过。
        /// </summary>
        void Shutdown();

        /// <summary>
        /// 执行 Python 代码并返回标准输出与标准错误文本。
        /// </summary>
        /// <param name="code">要执行的 Python 源代码。</param>
        /// <returns>output 为 stdout/print 捕获内容，error 为 stderr 捕获内容。</returns>
        (string Output, string Error) Exec(string code);

        /// <summary>
        /// 向 Python 运行时注册一个自定义模块。Python 侧可通过 `import name` 访问。
        /// </summary>
        /// <typeparam name="T">模块宿主类型，其公共方法会暴露给 Python。</typeparam>
        /// <param name="name">模块名。</param>
        /// <param name="instance">模块实例。</param>
        void RegisterModule<T>(string name, T instance) where T : class;
    }
}

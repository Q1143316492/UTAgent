using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;

namespace UTAgent.Editor.CsharpExec
{
    /// <summary>
    /// 尖刺：C# 源码 → Unity 自带 csc Emit 到临时 DLL → Assembly.Load → Dyn.Run。
    /// 不引用工程内 Microsoft.CodeAnalysis.*（Unity 2022 asmdef 引用易 CS0234）；
    /// 也不使用 CSharpScript.EvaluateAsync。
    /// </summary>
    public static class CsharpEmitExec
    {
        /// <summary>
        /// 实验性 C# exec 总开关。默认关闭，不进 Agent tools、菜单不可用。
        /// 需要尖刺时改为 <c>true</c> 即可（改完等脚本编译）。
        /// </summary>
        public static bool Enabled = false;

        public const string EntryTypeName = "Dyn";
        public const string EntryMethodName = "Run";

        /// <summary>
        /// 菜单/文档用的固定冒烟源码：创建 GameObject 并返回名称。
        /// </summary>
        public const string SmokeSource =
            "using UnityEngine;\n" +
            "public static class Dyn {\n" +
            "  public static string Run() {\n" +
            "    var go = new GameObject(\"CsharpEmitSpikeGo\");\n" +
            "    return go.name;\n" +
            "  }\n" +
            "}\n";

        /// <summary>
        /// 编译并执行。成功时 Output 为入口返回值；失败时 Error 带 [csharp-emit:compile|runtime] 前缀。
        /// </summary>
        public static (string Output, string Error) Run(string code)
        {
            if (!Enabled)
            {
                return (string.Empty,
                    "[csharp-emit:disabled] CsharpEmitExec.Enabled=false（默认关闭；尖刺时改为 true）");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return (string.Empty, "[csharp-emit:compile] code 为空");
            }

            string workDir = null;
            try
            {
                if (!TryResolveUnityCsc(out string dotnet, out string cscDll, out string resolveError))
                {
                    return (string.Empty, "[csharp-emit:compile] " + resolveError);
                }

                workDir = Path.Combine(Path.GetTempPath(), "UTAgentCsharpEmit_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workDir);
                string srcPath = Path.Combine(workDir, "Dyn.cs");
                string dllPath = Path.Combine(workDir, "Dyn.dll");
                File.WriteAllText(srcPath, code, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                var refArgs = new StringBuilder();
                foreach (var location in CollectReferencePaths())
                {
                    refArgs.Append(" /r:\"").Append(location).Append('"');
                }

                string args =
                    "exec \"" + cscDll + "\" /nologo /t:library /out:\"" + dllPath + "\"" +
                    refArgs + " \"" + srcPath + "\"";

                var psi = new ProcessStartInfo
                {
                    FileName = dotnet,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workDir,
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return (string.Empty, "[csharp-emit:compile] 无法启动 dotnet/csc");
                }

                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(120_000);
                if (proc.ExitCode != 0 || !File.Exists(dllPath))
                {
                    var sb = new StringBuilder();
                    sb.Append("[csharp-emit:compile] csc exit=").Append(proc.ExitCode);
                    if (!string.IsNullOrWhiteSpace(stdout))
                    {
                        sb.AppendLine().Append(stdout.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        sb.AppendLine().Append(stderr.Trim());
                    }

                    return (string.Empty, sb.ToString());
                }

                var asm = Assembly.Load(File.ReadAllBytes(dllPath));
                return InvokeEntry(asm);
            }
            catch (Exception e)
            {
                return (string.Empty, "[csharp-emit:runtime] " + e);
            }
            finally
            {
                if (!string.IsNullOrEmpty(workDir))
                {
                    try
                    {
                        Directory.Delete(workDir, recursive: true);
                    }
                    catch
                    {
                        // 临时目录清理失败可忽略
                    }
                }
            }
        }

        private static (string Output, string Error) InvokeEntry(Assembly asm)
        {
            var entryType = asm.GetType(EntryTypeName)
                ?? asm.GetTypes().FirstOrDefault(t =>
                    t.GetMethod(EntryMethodName, BindingFlags.Public | BindingFlags.Static) != null);
            if (entryType == null)
            {
                return (string.Empty,
                    $"[csharp-emit:runtime] 未找到入口类型 `{EntryTypeName}` 或含静态 `{EntryMethodName}` 的类型");
            }

            var method = entryType.GetMethod(
                EntryMethodName,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (method == null)
            {
                return (string.Empty,
                    $"[csharp-emit:runtime] 类型 `{entryType.FullName}` 无 public static {EntryMethodName}()");
            }

            try
            {
                object result = method.Invoke(null, null);
                return (result?.ToString() ?? string.Empty, string.Empty);
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                return (string.Empty, "[csharp-emit:runtime] " + inner);
            }
        }

        private static bool TryResolveUnityCsc(out string dotnet, out string cscDll, out string error)
        {
            dotnet = null;
            cscDll = null;
            error = null;

            string editorDir = Path.GetDirectoryName(EditorApplication.applicationPath);
            if (string.IsNullOrEmpty(editorDir))
            {
                error = "无法解析 Editor 安装目录";
                return false;
            }

            dotnet = Path.Combine(editorDir, "Data", "NetCoreRuntime", "dotnet.exe");
            cscDll = Path.Combine(editorDir, "Data", "DotNetSdkRoslyn", "csc.dll");
            if (!File.Exists(dotnet))
            {
                error = "未找到 Unity NetCoreRuntime/dotnet.exe: " + dotnet;
                return false;
            }

            if (!File.Exists(cscDll))
            {
                error = "未找到 Unity DotNetSdkRoslyn/csc.dll: " + cscDll;
                return false;
            }

            return true;
        }

        private static List<string> CollectReferencePaths()
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TryAdd(string location)
            {
                if (string.IsNullOrEmpty(location) || !File.Exists(location))
                {
                    return;
                }

                if (!seen.Add(location))
                {
                    return;
                }

                list.Add(location);
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic)
                {
                    continue;
                }

                string location;
                try
                {
                    location = asm.Location;
                }
                catch
                {
                    continue;
                }

                TryAdd(location);
            }

            // 保证 netstandard / mscorlib 类 facade 可用（部分程序集 Location 为空）
            string editorDir = Path.GetDirectoryName(EditorApplication.applicationPath);
            if (!string.IsNullOrEmpty(editorDir))
            {
                string ns = Path.Combine(editorDir, "Data", "NetStandard", "ref", "2.1.0", "netstandard.dll");
                TryAdd(ns);
            }

            return list;
        }
    }
}

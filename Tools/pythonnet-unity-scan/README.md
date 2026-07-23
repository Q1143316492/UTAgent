# pythonnet Unity-only Scan 补丁（UTAgent）

源：`pythonnet` **v3.0.5**（`0826fc0`）+ `AssemblyManager` 白名单过滤。  
参考补丁副本：`AssemblyManager.v3.0.5.patched.cs.txt`（**必须用 `.cs.txt`**，勿用 `.cs`，否则 Unity 会编译报错）。

## 行为

环境变量 `PYTHONNET_UNITY_ASSEMBLIES_ONLY=1`（或 `true`）时，`Initialize` / `AssemblyLoad` 仅 Scan：

- `System*` / `mscorlib*` / `netstandard*` / `Mono.*` / `Python.Runtime*`
- `UnityEngine*` / `UnityEditor*` / `Unity.*` / `TMPro*` / `Unity`

未设置或 `0`/`false`：与上游一致（全量 Scan）。

## 重建（必须在 Assets 外）

**禁止**把 pythonnet 源码放在 `Assets/` 下（Unity 会当脚本编译，触发 C# 10 / file-scoped namespace 报错）。

```powershell
# 建议目录（仓库根外或 _local，已 gitignore 建议）
$root = "D:\Unity\Src\EqZeroUT2\_local\pythonnet_build"
New-Item -ItemType Directory -Force -Path $root | Out-Null
cd $root
if (-not (Test-Path pythonnet\.git)) {
  git clone --depth 1 --branch v3.0.5 https://github.com/pythonnet/pythonnet.git
}
# 将 AssemblyManager.v3.0.5.patched.cs.txt 覆盖到 pythonnet/src/runtime/AssemblyManager.cs
# （复制时去掉 .txt 后缀）
# 并按需去掉 Directory.Build.props 里的 NonCopyableAnalyzer（见历史笔记）
dotnet build pythonnet/src/runtime/Python.Runtime.csproj -c Release
Copy-Item pythonnet/pythonnet/runtime/Python.Runtime.dll `
  ..\..\Assets\UTAgent\Plugins\Python.Runtime.dll -Force
```

回滚：用 `Plugins/Python.Runtime.dll.bak-stock-3.0.5` 覆盖（若存在）。

只提交：`Plugins/Python.Runtime.dll`、本目录 README 与 `*.patched.cs.txt` 副本。

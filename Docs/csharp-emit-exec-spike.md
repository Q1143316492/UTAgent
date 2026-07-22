# C# Emit Exec 方案评价（尖刺复盘）

> 状态：**实验 / 已冒烟通过**（Unity 2022.3.62）  
> 结论先说：  
> 1. **能跑通**「无 Domain Reload 执行任意 C#」。  
> 2. **Harness 不完善 ≠ 做完就技术碾压 Python**。外进程 csc、程序集无法卸载、观测面窄，是当前实现族的物理事实，包装层补齐也抹不掉（详见 §4）。  
> 3. **Agent 高频试错**这条主线上，技术上仍更宜以 `execPython` 为主；C# 赢在同 CLR / 心智，属局部优势。  
> 日常仍以 `execPython` 为准；本能力仅并联验证。  
> **开关：** `CsharpEmitExec.Enabled`（默认 `false`）。改为 `true` 后重编译，才会出现 Agent 工具 `execCsharp` 与菜单 `UTAgent/Spike/Roslyn Emit Exec`。

相关实现：

- 宿主：`Editor/CsharpExec/CsharpEmitExec.cs`
- 菜单：`Editor/CsharpExec/CsharpEmitSpikeMenu.cs`
- Agent 工具：`execCsharp`（`UTAgentRunner`）
- OpenSpec：`openspec/changes/csharp-exec-spike/`

---

## 1. 一句话评价

| 维度 | 评价 |
|------|------|
| 能不能跑 | **能**。菜单与 `CsharpEmitExec.Run` 均可创建场景物体，且不走 Domain Reload。 |
| 技术是否整体更优 | **否**。循环延迟、长跑内存、观测默认面：Python 更优或持平；C# 局部更优在「同 CLR、少 pythonnet」。见 §4–§5。 |
| 和 Python exec 比谁适合 Agent | **Python 更适合**。L1/L3、print 回传、skill、policy、可丢弃脚本心智都已磨过；C# 路径还是「完整编译单元 + 约定入口」。 |
| 宿主代码够不够「厚」 | **够做尖刺，不够做产品**。缺：语句包装、日志捕获、引用预热、Compilation 缓存、policy、失败可执行提示。 |
| AI 能否稳定写对 | **简单任务尚可，拼 UI / 多类型引用易翻车**。失败面在「格式契约」而不在「会不会写 Unity API」。 |

---

## 2. 最终架构（实际落地，不是最初设想）

```
Agent / 菜单
    │  code: 完整 C# 编译单元（须含 Dyn.Run）
    ▼
CsharpEmitExec.Run(code)
    │  1. 写临时 Dyn.cs（UTF-8 无 BOM）
    │  2. 收集 AppDomain 已加载程序集 Location → csc /r:
    │  3. Unity 自带：
    │       Data/NetCoreRuntime/dotnet.exe
    │       exec Data/DotNetSdkRoslyn/csc.dll
    │  4. Assembly.Load(dll bytes)
    │  5. 找 Dyn.Run()（或任意 public static Run()）并 Invoke
    ▼
(output, error)   // error 前缀 [csharp-emit:compile|runtime]
```

刻意 **没有** 做成：

- `CSharpScript.EvaluateAsync`（Unity 2022 Scripting Host 易挂）
- 工程内 NuGet `Microsoft.CodeAnalysis.*` 进程内 Emit（asmdef 引用曾 CS0234）
- 把 `.cs` 写进 `Assets` 等 Unity 编译（Domain Reload，不适合 Agent 循环）

---

## 3. 中间踩过的坑（按时间线）

### 3.1 幻想：CSharpScript = Python exec

社区与桌面 .NET 上 `EvaluateAsync("1+2")` 很香，但 Unity 2022 上常见：

```text
NotImplementedException
  at CoreAssemblyLoaderImpl.LoadFromStream(...)
```

根因不是「C# 写得不像 Unity」，而是 **微软 Scripting 宿主假设桌面 CLR 的装载能力，Unity 运行时没完整实现**。  
尖刺从一开始就排除这条主路径是对的。

### 3.2 幻想：把 Roslyn DLL 拷进 Plugins 就能进程内 Emit

尝试过 `Microsoft.CodeAnalysis` / `CSharp` 4.8.0 + Immutable / Metadata / Unsafe。  
`EqZeroUT2.UTAgent.Editor.rsp` **已经列出** `-r:.../Microsoft.CodeAnalysis.CSharp.dll`，编译仍报：

```text
error CS0234: The type or namespace name 'CSharp' does not exist
  in the namespace 'Microsoft.CodeAnalysis'
error CS0246: The type or namespace name 'MetadataReference' could not be found
```

同机用 Unity 自带 `csc` **手动**引用这些 DLL 编译宿主源码是成功的——说明 DLL 文件本身可读，问题在 **Unity Bee/asmdef 引用解析环境**，不是「少写了一行 using」。

处理：撤回 Plugins 内 Roslyn，改用 **Editor 安装目录自带 csc** 做外进程编译。  
代价：每次 exec 有进程启动开销；收益：无 DLL 冲突、可回滚干净。

### 3.3 Domain Reload 会冲掉引擎

改 Editor 脚本 / 删 Plugins 后 Python 引擎会 `invalidated`，CLI 需 `utagent init`。这是验证噪声，不是方案本身缺陷，但提醒：**C# emit 宿主若再改，验证流程要预留 init**。

### 3.4 冒烟通过后的真实数字

在运行中的 Editor 上（CLI）：

| 步骤 | 结果 |
|------|------|
| `ExecuteMenuItem("UTAgent/Spike/Roslyn Emit Exec")` | `menu_ok=true`，Hierarchy 有 `CsharpEmitSpikeGo` |
| 反射调用 `CsharpEmitExec.Run(SmokeSource)` | `emit_output=CsharpEmitSpikeGo`，`emit_error=""` |
| 同会话继续 `utagent exec`（Python） | `python_ok=true` |

---

## 4. Harness 抹不掉的物理事实

「周边 harness 不完善只是还没做」——对 **体验层**（包装 `Dyn`、Log 回传、薄 L1、policy、skill）成立。  
下面三条是 **当前实现族的物理约束**：产品包装可以减轻痛感，但**不会 magically 消失**。宣称「做完 harness 就技术全面优于 Python」不成立。

### 4.1 外进程 csc

当前每次 `Run` 实际在做：

```
Unity Editor 进程
    │  Process.Start(dotnet exec csc.dll …)
    ▼
子进程：读临时 .cs + 一长串 /r: → 写出 .dll
    │  读回字节
    ▼
回到 Editor：Assembly.Load → Invoke
```

这不是「还没优化」的软件债，而是尖刺为避开工程内 Roslyn `CS0234` / Scripting Host 缺口，**主动选择**的路径。

| Harness 能做的 | Harness 做不到的 |
|----------------|------------------|
| 缓存「相同源码」跳过再编译 | 取消「至少有一次完整编译」这件事本身 |
| 常驻编译服务、减冷启动 | 在 **不** 解决进程内 Roslyn 引用的前提下，变成真正的进程内 Emit |
| 超时、取消、更好的错误文案 | 让 csc 启动 + 传上百个 `/r:` 的成本降到「解释两行 Python」量级 |

对比 Python：`scope.Exec(code)` 在 **同一进程、同一解释器**里消化字符串，没有「另起编译器进程」这一跳。

若未来攻下进程内 `CSharpCompilation.Emit`（或常驻编译守护进程），延迟可以靠近——那是 **另一条技术路线**，不是给现有尖刺加 skill 就能自动获得的。

### 4.2 程序集无法卸载

成功路径末尾是：

```csharp
Assembly.Load(File.ReadAllBytes(dllPath));
```

在经典 .NET / Unity Editor 的默认加载语义下：

- 该程序集进入当前 AppDomain（或等价上下文）后，**一般不能按次卸载**；
- 类型、静态状态、JIT 元数据会留下；
- Agent 若每步都 emit 新 DLL，**内存与类型表会单调上涨**。

| Harness 能做的 | Harness 做不到的 |
|----------------|------------------|
| 限频、合并步骤、警告文档 | 在默认 `Assembly.Load` 下「用完就扔掉程序集」 |
| 尽量复用同名/同哈希编译结果 | 假装和 Python 一样「跑完一段脚本就零残留」 |

真正卸载通常要 **可收集的 AssemblyLoadContext** 或整域回收；Unity Editor 对前者支持受限，后者等于 Domain Reload——正是 Agent 循环要躲开的。  
所以：这是 **长跑物理税**，不是缺一个 after-tool 钩子。

### 4.3 「跨语言观测」——这里指什么

容易误解成「C# 和 Python 两门语言」。更准确是两层：

**A. 观测通道窄（相对 Python exec）**

| | Python | C# emit（现状） |
|--|--|--|
| 主回传 | `print` / stdout 劫持进 tool result | 几乎只有 `Run()` **返回值字符串** |
| 侦察 | L1 `unity.*` echo、层级、截图配方成熟 | 无对等 L1；`Debug.Log` **默认不进** tool output |
| Agent 纠偏 | 能看见中间 print | 看不见中间 Log，除非再开别的工具 |

Harness **可以**补：劫持 `Debug.Log`、强制返回多行报告、加薄 L1。补完后观测能 **接近** Python，这是「还没做」的部分。

**B. 即便同语言，和「已编译进工程的 C#」仍不是同一种生命周期**

动态 emit 出来的类型：

- 不进 `Assets`，不进正常脚本编译图；
- 序列化、热重载、部分 Editor 工具链 **不会** 像对待 `MonoBehaviour` 源码那样对待它；
- 和 L3 `CS.*` 调 **已加载业务程序集** 不同——后者零编译，只是反射/绑定。

所以「跨语言观测」在评价里强调的是：  
**Agent 循环依赖的「执行 → 看见 → 再执行」管道，Python 已经打通；C# emit 默认几乎只有返回值。**  
这不是「C# 看不见 Unity」，而是 **tool 结果通道默认更盲**。Harness 能加厚通道；加厚之前就谈全面更优，是偷换概念。

### 4.4 三句话收束

1. **外进程 csc** → 每步编译税；harness 可缓存，不能变成零编译。  
2. **Assembly.Load** → 长跑泄漏税；harness 可限频，不能默认卸载。  
3. **观测** → 默认只有返回值；harness 可补 Log/L1，但那是欠债，不是已有优势。

因此：**完善 harness → C# 可以「够用」；≠ 技术上压过 Python 的 Agent 循环。**

---

## 5. 技术是否更优？（相对 Python exec）

把「心智更优」（Unity 同学只想写 C#）和「技术更优」（延迟、内存、观测、循环成本）分开。

| 轴 | Python（现状） | C# emit（尖刺） | 谁更优 |
|--|--|--|--|
| 单次延迟 | 进程内解释 | 外进程 csc + Load | **Python** |
| 无 Domain Reload | ✓ | ✓ | 平手 |
| 调 Unity API 保真 | 经 pythonnet / `CS.*` | 同 CLR 直接调 | **C# 略优** |
| 编译期发现错误 | 运行时才炸 | csc 先报 | **C# 略优** |
| 观测默认面 | print / L1 成熟 | 几乎只有返回值 | **Python** |
| 长跑内存 | 压力小 | Load 难卸载 | **Python** |
| 依赖形态 | 已付 CPython 嵌入税 | 绑 Editor 自带 csc 布局 | 各有税 |

**局部更优（C#）：** 少互操作怪癖、错误前置、与引擎同语言。  
**整体循环更优（Python）：** 快、看得清、可狂试、长跑更干净。  

拼 UI 类任务瓶颈多在模型与 assert，不在「少一层反射 0.x ms」——同 CLR **有加分，撑不起全面取代**。

---

## 6. 宿主代码「丰富度」评估

当前宿主大约 **240 行**，职责清晰，但对 Agent 产品来说仍偏骨架。

| 能力 | 现状 | 产品化还差什么 |
|------|------|----------------|
| 编译 + 加载 + Invoke | 有 | Compilation 缓存、并行/超时取消 |
| 约定入口 `Dyn.Run` | 有 | **顶层语句自动包成 class**（降模型负担） |
| 编译/运行错误前缀 | 有 | 结构化 JSON、可执行修复提示（对标 Python after-tool） |
| 引用收集 | AppDomain + netstandard | 懒加载模块预热、常用 Unity 模块白名单 |
| 日志 / print | **无** | 劫持 `Debug.Log` 或要求入口返回诊断字符串 |
| 安全策略 | **无** | C# 版 fs/进程危险面（不能照搬 Python 字符串规则） |
| L1 捷径 | **无** | `Ut.PrepareSceneObject` 等，否则每段都要手写样板 |
| CLI `POST /exec-csharp` | **无** | 有利于 Cursor 编排，不经 LLM |

**判断：** 宿主对「证明能跑」足够；对「模型少翻车、高频纠偏」不足。  
缺口主要在 **Agent 体验层**（包装、回传、policy、捷径），不在「会不会调 Unity API」。

---

## 7. AI 能否稳定写出？——取决于「它被要求写什么」

### 7.1 模型真正要交的东西

`execCsharp` 的 description 要求大致是：

> 完整可编译单元 + `public static class Dyn { public static string Run() { ... } }`

对比 Python：

```python
import unity
from unity_bind import CS
# 直接语句，print 即可见
```

| | Python exec | C# emit（现状） |
|--|--|--|
| 是否要 class/方法壳 | 否 | **是** |
| 缺壳会怎样 | — | 编译失败或找不到入口 |
| 回传观测 | `print` / L1 echo | **几乎只有返回值字符串** |
| 常用捷径 | `unity.*` | 无，须直接 Unity API |
| 类型/命名空间 | 动态 `CS.UnityEngine...` | 须正确 `using`，漏了就 CS0246 |
| 单次延迟 | 通常更低 | 外进程 csc，亚秒～数秒 |

**稳定度结论：**

- **小脚本**（创建 GO、改一个字段）：模型写 C# 通常没问题，壳子也学得快。  
- **中等 UI**（Canvas + Layout + TMP）：模型「会写 Unity C#」，但更容易在 **缺 using、缺引用模块未加载、返回值不可见、一次写太长** 上翻车；Python 侧已有 L1 + skill + health assert 闭环，C# 侧还没有。  
- **复杂多步纠偏**：没有 print/L1 echo 时，模型只能靠返回值或再开 Python 侦察——双工具更乱。

因此：**不是「AI 不会写 C#」，而是「当前契约比 Python 更脆、观测面更窄」→ 同样任务下稳定性更差。**

### 7.2 怎样才能让 AI 更稳（若继续做）

按收益排序：

1. **宿主模板包裹**：允许 Agent 只交 `Run()` 方法体或顶层语句，由宿主生成 `Dyn`。  
2. **强制/默认 `using` 集**：`UnityEngine`、`UnityEngine.UI`、`TMPro`、`UnityEditor`。  
3. **把 `Debug.Log` 聚合成 output**（或要求 `Run` 返回多行报告）。  
4. **薄 L1**：哪怕只有 `Find` / `Prepare` / `Screenshot` 三个静态方法。  
5. 再考虑 CLI `exec-csharp`，方便 Cursor 不经 Chat 编排。

未做这些之前，不宜宣称「可以替换 Python 主路径」。

---

## 8. 测试代码（请当契约样例，不是玩具）

### 8.1 宿主内置冒烟（已通过）

菜单 **UTAgent → Spike → Roslyn Emit Exec** 调用的源码即：

```csharp
using UnityEngine;
public static class Dyn {
  public static string Run() {
    var go = new GameObject("CsharpEmitSpikeGo");
    return go.name;
  }
}
```

期望：`output == "CsharpEmitSpikeGo"`，Hierarchy 出现同名物体，无 Domain Reload。

### 8.2 编译失败样例（应返回 `[csharp-emit:compile]`）

```csharp
using UnityEngine;
public static class Dyn {
  public static string Run() {
    return MissingType.Foo; // 故意写错
  }
}
```

期望：不创建物体；`error` 含 csc 诊断。

### 8.3 运行时失败样例（应返回 `[csharp-emit:runtime]`）

```csharp
using UnityEngine;
public static class Dyn {
  public static string Run() {
    throw new System.InvalidOperationException("boom");
  }
}
```

### 8.4 缺入口样例（应 `[csharp-emit:runtime] 未找到入口`）

```csharp
using UnityEngine;
public static class NotTheEntry {
  public static string Hello() => "x";
}
```

### 8.5 稍贴近真实 Agent 任务：在 Canvas 下挂一个简单面板

> 说明：这是模型「应该能写」的中等复杂度；**尚未**用 health assert 验收。缺 `using`、找不到 Canvas、模块未加载都会失败——用来感受契约脆性。

```csharp
using UnityEngine;
using UnityEngine.UI;

public static class Dyn {
  public static string Run() {
    var canvas = GameObject.Find("Canvas");
    if (canvas == null)
      return "FAIL: Canvas not found";

    const string rootName = "WndCsharpEmitDemo";
    var old = GameObject.Find(rootName);
    if (old != null)
      Object.DestroyImmediate(old);

    var wnd = new GameObject(rootName);
    wnd.transform.SetParent(canvas.transform, false);
    var img = wnd.AddComponent<Image>();
    img.color = new Color(0.15f, 0.15f, 0.18f, 0.98f);
    var rt = wnd.GetComponent<RectTransform>();
    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
    rt.sizeDelta = new Vector2(320, 180);

    return rootName + " ok";
  }
}
```

与 README 里 Python UI 配方对比：Python 版还有 `unity.prepare_scene_object`、TMP、Layout 一整套；C# 版要模型自己拼完，且 **没有** 对等的 assert 门禁文档。

### 8.6 用 CLI 验证宿主（不经 LLM）

以下脚本曾用于尖刺（可放 `Out/exec/`，该目录 gitignore）：

**刷新 / 探测类型是否进程序集：**

```python
import json
from unity_bind import CS

CS.UnityEditor.AssetDatabase.Refresh()
compiling = bool(CS.UnityEditor.EditorApplication.isCompiling)
failed = False
try:
    failed = bool(CS.UnityEditor.EditorUtility.scriptCompilationFailed)
except Exception:
    pass

found = False
for asm in CS.System.AppDomain.CurrentDomain.GetAssemblies():
    try:
        if asm.GetName().Name != "EqZeroUT2.UTAgent.Editor":
            continue
    except Exception:
        continue
    if asm.GetType("UTAgent.Editor.CsharpExec.CsharpEmitExec") is not None:
        found = True
        break

print(json.dumps({
    "compiling": compiling,
    "scriptCompilationFailed": failed,
    "CsharpEmitExec_found": found,
}, ensure_ascii=False))
```

**跑菜单冒烟：**

```python
import json
from unity_bind import CS

old = CS.UnityEngine.GameObject.Find("CsharpEmitSpikeGo")
if old is not None:
    CS.UnityEngine.Object.DestroyImmediate(old)

ok = CS.UnityEditor.EditorApplication.ExecuteMenuItem("UTAgent/Spike/Roslyn Emit Exec")
go = CS.UnityEngine.GameObject.Find("CsharpEmitSpikeGo")
print(json.dumps({
    "menu_ok": bool(ok),
    "go_found": go is not None,
    "go_name": go.name if go is not None else None,
}, ensure_ascii=False))
```

**直接调 `Run`（对标 tool 内核）：**

```python
import json
from unity_bind import CS

t = None
for asm in CS.System.AppDomain.CurrentDomain.GetAssemblies():
    try:
        if asm.GetName().Name != "EqZeroUT2.UTAgent.Editor":
            continue
    except Exception:
        continue
    t = asm.GetType("UTAgent.Editor.CsharpExec.CsharpEmitExec")
    if t is not None:
        break

smoke = t.GetField("SmokeSource").GetValue(None)
result = t.GetMethod("Run").Invoke(None, [smoke])
print(json.dumps({
    "emit_output": str(result.Item1),
    "emit_error": str(result.Item2) if result.Item2 else "",
    "emit_ok": not bool(result.Item2),
}, ensure_ascii=False))
```

**Chat `execCsharp` 应提交的 JSON 形态（示意）：**

```json
{
  "name": "execCsharp",
  "arguments": {
    "code": "using UnityEngine;\npublic static class Dyn {\n  public static string Run() {\n    var go = new GameObject(\"CsharpEmitSpikeGo\");\n    return go.name;\n  }\n}\n"
  }
}
```

注意：参数里是 **转义后的完整源码字符串**，不是文件路径。模型若只吐方法体、或忘记 `using`，会直接编译失败。

---

## 9. 与 Python 主路径怎么共存

```
推荐默认：
  Cursor / Chat → execPython（L1+L3）→ health assert

实验并联：
  execCsharp → 仅验证「Unity 同学心智 / C# 语法」是否值得加厚宿主

不要：
  同时让模型自由选两个 exec 且无 skill 约束（易抖动）
```

若产品目标是「团队只想看到 C#」：优先投资 **语句包装 + 观测 + 薄 L1**，而不是再赌进程内 Roslyn Scripting。

---

## 10. 总评与建议

**方案本身：**  
「外进程 Unity csc + `Assembly.Load` + 约定入口」在 2022.3 上 **已证明可行**，比 CSharpScript / 工程内 NuGet Roslyn 更老实。

**中间问题的启示：**  
坑主要在 **运行时/引用宿主**，不在「会不会写 Unity C#」。评价动态执行方案时，要把「编译器」和「装进 Unity 进程」分开看。

**Harness 与技术优劣（写进结论，避免误读）：**

- 周边 harness 不完善，只说明 **体验层**还能加（包装、Log、L1、policy）。  
- **不能**据此推断「加完就技术全面优于 Python」。§4 三条物理事实（外进程 csc、程序集难卸载、默认观测窄）仍在。  
- **技术判断：** Agent 高频试错主路径 → **Python 更优或持平**；C# → **局部更优**（同 CLR / 心智），适合并联与心智产品，不适合在无新证据时宣称碾压。

**代码丰富度：**  
宿主对尖刺够用；对 Agent 稳定度不够。决定 AI 能否稳定写出的，首先是 **契约厚度**（壳、using、回传、捷径），其次才是模型懂不懂 C#。

**下一步（若继续，应另开 change，勿在尖刺上堆）：**

1. 语句/方法体自动包装成 `Dyn`  
2. Log 捕获或结构化返回  
3. 3～5 个 Editor 捷径 API  
4. 一组固定回归源码（本文 §8）进自动化  
5. （可选，另一场赌局）再攻进程内 Emit / 常驻编译，专门打延迟——与「补 harness」分开立项  
6. 再决定是否暴露 CLI / 默认 tool

在此之前：**保持 `execPython` 为主；`execCsharp` 仅作实验开关。**

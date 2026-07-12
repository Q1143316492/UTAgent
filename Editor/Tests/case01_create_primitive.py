"""POC 验证用例 1：从 Python 创建 Unity 对象

验证场景：spec「从 Python 创建 Unity 对象」
预期：当前场景生成一个名为 PythonSpawned 的 Cube，Console 输出 "spawned: PythonSpawned"

把整段贴进 POC Window 执行。POC 阶段用 pythonnet 原生风格（clr）是临时妥协，
unity 模块建起来后改用 `import unity; unity.create_cube(...)`。
"""

import clr
clr.AddReference("UnityEngine")
import UnityEngine

go = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube)
go.name = "PythonSpawned"
UnityEngine.Debug.Log("spawned: " + go.name)
print("print from python:", go.name)

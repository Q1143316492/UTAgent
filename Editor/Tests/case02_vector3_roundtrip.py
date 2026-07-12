"""POC 验证用例 2：跨语言值类型往返

验证场景：spec「跨语言值类型传递」
预期：构造 Vector3(1, 2, 3)，读回 x/y/z 等于 1.0/2.0/3.0，证明值类型跨语言往返正确

把整段贴进 POC Window 执行。
"""

import clr
clr.AddReference("UnityEngine")
import UnityEngine

v = UnityEngine.Vector3(1, 2, 3)
print("x =", v.x, "y =", v.y, "z =", v.z)
assert abs(v.x - 1.0) < 1e-6, f"x mismatch: {v.x}"
assert abs(v.y - 2.0) < 1e-6, f"y mismatch: {v.y}"
assert abs(v.z - 3.0) < 1e-6, f"z mismatch: {v.z}"
print("Vector3 往返校验通过")

using System;
using UnityEngine;

namespace UTAgent.Editor.PythonInterop
{
    public sealed partial class UTAgentPythonBridge
    {
        /// <summary>
        /// 获取 GameObject 世界旋转欧拉角（度）。
        /// </summary>
        public string GetRotation(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"找不到对象：{name}");
            }
            var e = go.transform.eulerAngles;
            return $"{{\"success\":true,\"euler\":{{\"x\":{e.x},\"y\":{e.y},\"z\":{e.z}}}}}";
        }

        /// <summary>
        /// 以欧拉角（度）设置 GameObject 世界旋转。
        /// </summary>
        public string SetRotation(string name, float rx, float ry, float rz)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"找不到对象：{name}");
            }
            go.transform.rotation = Quaternion.Euler(rx, ry, rz);
            return "{\"success\":true}";
        }

        /// <summary>
        /// 获取 GameObject 本地缩放。
        /// </summary>
        public string GetScale(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"找不到对象：{name}");
            }
            var s = go.transform.localScale;
            return $"{{\"success\":true,\"scale\":{{\"x\":{s.x},\"y\":{s.y},\"z\":{s.z}}}}}";
        }

        /// <summary>
        /// 设置 GameObject 本地缩放。
        /// </summary>
        public string SetScale(string name, float sx, float sy, float sz)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"找不到对象：{name}");
            }
            go.transform.localScale = new Vector3(sx, sy, sz);
            return "{\"success\":true}";
        }

        /// <summary>
        /// 沿方向向量平移对象（方向归一化后乘 distance，世界空间）。
        /// </summary>
        public string MoveObject(string name, float dx, float dy, float dz, float distance)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"找不到对象：{name}");
            }
            var dir = new Vector3(dx, dy, dz);
            if (dir.sqrMagnitude < 1e-8f)
            {
                return Error("direction 不能为零向量");
            }
            go.transform.Translate(dir.normalized * distance, Space.World);
            return "{\"success\":true}";
        }

        /// <summary>
        /// 绕本地轴旋转对象（度）。
        /// </summary>
        public string RotateObject(string name, string axis, float angle)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"找不到对象：{name}");
            }
            Vector3 axisVec;
            switch (axis?.ToLowerInvariant())
            {
                case "x":
                    axisVec = Vector3.right;
                    break;
                case "y":
                    axisVec = Vector3.up;
                    break;
                case "z":
                    axisVec = Vector3.forward;
                    break;
                default:
                    return Error($"不支持的轴：{axis}，请使用 x/y/z");
            }
            go.transform.Rotate(axisVec, angle, Space.Self);
            return "{\"success\":true}";
        }

        /// <summary>
        /// 使对象朝向目标。usePosition 为 true 时使用坐标 (tx,ty,tz)，否则 targetName 为另一对象名。
        /// </summary>
        public string LookAt(
            string name,
            string targetName,
            float tx,
            float ty,
            float tz,
            bool usePosition)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"找不到对象：{name}");
            }
            Vector3 targetPos;
            if (usePosition)
            {
                targetPos = new Vector3(tx, ty, tz);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(targetName))
                {
                    return Error("target 须为非空对象名或传入坐标三元组");
                }
                var targetGo = GameObject.Find(targetName);
                if (targetGo == null)
                {
                    return Error($"找不到目标对象：{targetName}");
                }
                targetPos = targetGo.transform.position;
            }
            go.transform.LookAt(targetPos, Vector3.up);
            return "{\"success\":true}";
        }

        /// <summary>
        /// 销毁单个 GameObject（Edit Mode 使用 DestroyImmediate）。
        /// </summary>
        public string DestroyObject(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"找不到对象：{name}");
            }
            var destroyedName = go.name;
            UnityEngine.Object.DestroyImmediate(go);
            return $"{{\"success\":true,\"destroyed\":{BridgeJson.EscapeJson(destroyedName)}}}";
        }
    }
}

using System;
using UnityEngine;

namespace UTAgent.Editor.PythonInterop
{
    public sealed partial class UTAgentPythonBridge
    {
        /// <summary>
        /// ��ȡ GameObject ������תŷ���ǣ��ȣ���
        /// </summary>
        public string GetRotation(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"�Ҳ�������{name}");
            }
            var e = go.transform.eulerAngles;
            return $"{{\"success\":true,\"euler\":{{\"x\":{e.x},\"y\":{e.y},\"z\":{e.z}}}}}";
        }

        /// <summary>
        /// ��ŷ���ǣ��ȣ����� GameObject ������ת��
        /// </summary>
        public string SetRotation(string name, float rx, float ry, float rz)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"�Ҳ�������{name}");
            }
            go.transform.rotation = Quaternion.Euler(rx, ry, rz);
            return "{\"success\":true}";
        }

        /// <summary>
        /// ��ȡ GameObject �������š�
        /// </summary>
        public string GetScale(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"�Ҳ�������{name}");
            }
            var s = go.transform.localScale;
            return $"{{\"success\":true,\"scale\":{{\"x\":{s.x},\"y\":{s.y},\"z\":{s.z}}}}}";
        }

        /// <summary>
        /// ���� GameObject �������š�
        /// </summary>
        public string SetScale(string name, float sx, float sy, float sz)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"�Ҳ�������{name}");
            }
            go.transform.localScale = new Vector3(sx, sy, sz);
            return "{\"success\":true}";
        }

        /// <summary>
        /// �ط�������ƽ�ƶ��󣨷����һ������� distance������ռ䣩��
        /// </summary>
        public string MoveObject(string name, float dx, float dy, float dz, float distance)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"�Ҳ�������{name}");
            }
            var dir = new Vector3(dx, dy, dz);
            if (dir.sqrMagnitude < 1e-8f)
            {
                return Error("direction ����Ϊ������");
            }
            go.transform.Translate(dir.normalized * distance, Space.World);
            return "{\"success\":true}";
        }

        /// <summary>
        /// �Ʊ�������ת���󣨶ȣ���
        /// </summary>
        public string RotateObject(string name, string axis, float angle)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"�Ҳ�������{name}");
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
                    return Error($"��֧�ֵ��᣺{axis}�������� x/y/z");
            }
            go.transform.Rotate(axisVec, angle, Space.Self);
            return "{\"success\":true}";
        }

        /// <summary>
        /// ʹ������Ŀ�ꡣusePosition Ϊ true ʱ���������� (tx,ty,tz)������ targetName Ϊ��һ��������
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
                return Error($"�Ҳ�������{name}");
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
                    return Error("target �����Ƿǿն�������������Ԫ��");
                }
                var targetGo = GameObject.Find(targetName);
                if (targetGo == null)
                {
                    return Error($"�Ҳ���Ŀ�����{targetName}");
                }
                targetPos = targetGo.transform.position;
            }
            go.transform.LookAt(targetPos, Vector3.up);
            return "{\"success\":true}";
        }

        /// <summary>
        /// �������� GameObject��Edit Mode ��ȫ��DestroyImmediate����
        /// </summary>
        public string DestroyObject(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"�Ҳ�������{name}");
            }
            var destroyedName = go.name;
            UnityEngine.Object.DestroyImmediate(go);
            return $"{{\"success\":true,\"destroyed\":{EscapeJson(destroyedName)}}}";
        }
    }
}

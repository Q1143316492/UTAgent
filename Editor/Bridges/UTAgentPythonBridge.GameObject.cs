using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;


namespace UTAgent.Editor.Bridges
{
    public sealed partial class UTAgentPythonBridge
    {
        public string CreateCube(string name, float x, float y, float z)
        {
            return CreatePrimitive("Cube", name, x, y, z);
        }

        /// <summary>
        /// 创建指定类型的基础几何体。
        /// </summary>
        public string CreatePrimitive(string primType, string name, float x, float y, float z)
        {
            if (!Enum.TryParse<PrimitiveType>(primType, out var primitiveType))
            {
                return Error($"不支持的 primitive 类型：{primType}");
            }
            try
            {
                var go = GameObject.CreatePrimitive(primitiveType);
                go.name = string.IsNullOrWhiteSpace(name) ? primType : name;
                go.transform.position = new Vector3(x, y, z);
                return $"{{\"success\":true,\"name\":{EscapeJson(go.name)},\"instanceId\":{go.GetInstanceID()}}}";
            }
            catch (Exception e)
            {
                return Error($"创建失败：{e.Message}");
            }
        }

        /// <summary>
        /// 按名查找一个激活的 GameObject（GameObject.Find，重名时只返回其中一个）。
        /// </summary>
        public string FindObject(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return "{\"success\":false}";
            }
            return $"{{\"success\":true,\"name\":{EscapeJson(go.name)},\"instanceId\":{go.GetInstanceID()},\"active\":{ToLower(go.activeSelf)}}}";
        }

        /// <summary>
        /// 在当前活动场景中按名查找所有 GameObject（遍历层级，含非激活对象）。
        /// </summary>
        public string FindObjects(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Error("FindObjects: name 不能为空");
            }
            var matches = CollectGameObjectsByName(name);
            var items = matches
                .Select(go => $"{{\"name\":{EscapeJson(go.name)},\"instanceId\":{go.GetInstanceID()},\"active\":{ToLower(go.activeSelf)}}}");
            return $"{{\"success\":true,\"count\":{matches.Count},\"objects\":[{string.Join(",", items)}]}}";
        }

        /// <summary>
        /// 销毁当前活动场景中所有同名 GameObject（Edit Mode 安全：DestroyImmediate）。
        /// </summary>
        public string DestroyAllObjects(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Error("DestroyAllObjects: name 不能为空");
            }
            var matches = CollectGameObjectsByName(name);
            if (matches.Count == 0)
            {
                return "{\"success\":true,\"destroyedCount\":0,\"destroyed\":[]}";
            }
            // 先深后浅，避免父节点销毁时子节点已失效
            matches.Sort((a, b) => GetTransformDepth(b.transform).CompareTo(GetTransformDepth(a.transform)));
            var destroyedIds = new List<int>();
            foreach (var go in matches)
            {
                if (go == null)
                {
                    continue;
                }
                destroyedIds.Add(go.GetInstanceID());
                UnityEngine.Object.DestroyImmediate(go);
            }
            var idJson = string.Join(",", destroyedIds.Select(id => id.ToString()));
            return $"{{\"success\":true,\"destroyedCount\":{destroyedIds.Count},\"destroyedInstanceIds\":[{idJson}]}}";
        }

        private static List<GameObject> CollectGameObjectsByName(string name)
        {
            var matches = new List<GameObject>();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                CollectGameObjectsByNameRecursive(root.transform, name, matches);
            }
            return matches;
        }

        private static void CollectGameObjectsByNameRecursive(Transform transform, string name, List<GameObject> matches)
        {
            if (transform.name == name)
            {
                matches.Add(transform.gameObject);
            }
            for (int i = 0; i < transform.childCount; i++)
            {
                CollectGameObjectsByNameRecursive(transform.GetChild(i), name, matches);
            }
        }

        private static int GetTransformDepth(Transform transform)
        {
            int depth = 0;
            var current = transform;
            while (current.parent != null)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }

        /// <summary>
        /// 获取 GameObject 层次树。
        /// </summary>
        public string GetHierarchy(string name, int depth)
        {
            var roots = new List<Transform>();
            if (string.IsNullOrWhiteSpace(name))
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                foreach (var root in scene.GetRootGameObjects())
                {
                    roots.Add(root.transform);
                }
            }
            else
            {
                var go = GameObject.Find(name);
                if (go == null)
                {
                    return Error($"找不到对象：{name}");
                }
                roots.Add(go.transform);
            }
            var nodes = roots.Select(r => BuildHierarchyNode(r, depth, 0));
            return $"{{\"success\":true,\"hierarchy\":[{string.Join(",", nodes)}]}}";
        }

        /// <summary>
        /// 读取 GameObject 的世界坐标。
        /// </summary>
        public string GetPosition(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"找不到对象：{name}");
            }
            var p = go.transform.position;
            return $"({p.x},{p.y},{p.z})";
        }

        /// <summary>
        /// 设置 GameObject 的世界坐标。
        /// </summary>
        public string SetPosition(string name, float x, float y, float z)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"找不到对象：{name}");
            }
            go.transform.position = new Vector3(x, y, z);
            return "{\"success\":true}";
        }

        /// <summary>
        /// 输出日志到 Unity Console。
        /// </summary>
    }
}

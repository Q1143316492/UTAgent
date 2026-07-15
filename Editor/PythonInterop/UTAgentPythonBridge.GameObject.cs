using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;


namespace UTAgent.Editor.PythonInterop
{
    public sealed partial class UTAgentPythonBridge
    {
        public string CreateCube(string name, float x, float y, float z)
        {
            return CreatePrimitive("Cube", name, x, y, z);
        }

        /// <summary>
        /// ����ָ�����͵Ļ��������塣
        /// </summary>
        public string CreatePrimitive(string primType, string name, float x, float y, float z)
        {
            if (!Enum.TryParse<PrimitiveType>(primType, out var primitiveType))
            {
                return Error($"��֧�ֵ� primitive ���ͣ�{primType}");
            }
            try
            {
                var go = GameObject.CreatePrimitive(primitiveType);
                go.name = string.IsNullOrWhiteSpace(name) ? primType : name;
                go.transform.position = new Vector3(x, y, z);
                return $"{{\"success\":true,\"name\":{BridgeJson.EscapeJson(go.name)},\"instanceId\":{go.GetInstanceID()}}}";
            }
            catch (Exception e)
            {
                return Error($"����ʧ�ܣ�{e.Message}");
            }
        }

        /// <summary>
        /// ��������һ������� GameObject��GameObject.Find������ʱֻ��������һ������
        /// </summary>
        public string FindObject(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return "{\"success\":false}";
            }
            return $"{{\"success\":true,\"name\":{BridgeJson.EscapeJson(go.name)},\"instanceId\":{go.GetInstanceID()},\"active\":{BridgeJson.ToLower(go.activeSelf)}}}";
        }

        /// <summary>
        /// �ڵ�ǰ������а����������� GameObject�������㼶�����Ǽ�����󣩡�
        /// </summary>
        public string FindObjects(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Error("FindObjects: name ����Ϊ��");
            }
            var matches = CollectGameObjectsByName(name);
            var items = matches
                .Select(go => $"{{\"name\":{BridgeJson.EscapeJson(go.name)},\"instanceId\":{go.GetInstanceID()},\"active\":{BridgeJson.ToLower(go.activeSelf)}}}");
            return $"{{\"success\":true,\"count\":{matches.Count},\"objects\":[{string.Join(",", items)}]}}";
        }

        /// <summary>
        /// ���ٵ�ǰ�����������ͬ�� GameObject��Edit Mode ��ȫ��DestroyImmediate����
        /// </summary>
        public string DestroyAllObjects(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Error("DestroyAllObjects: name ����Ϊ��");
            }
            var matches = CollectGameObjectsByName(name);
            if (matches.Count == 0)
            {
                return "{\"success\":true,\"destroyedCount\":0,\"destroyed\":[]}";
            }
            // �����ǳ�����⸸�ڵ�����ʱ�ӽڵ���ʧЧ
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
        /// ��ȡ GameObject �������
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
                    return Error($"�Ҳ�������{name}");
                }
                roots.Add(go.transform);
            }
            var nodes = roots.Select(r => BuildHierarchyNode(r, depth, 0));
            return $"{{\"success\":true,\"hierarchy\":[{string.Join(",", nodes)}]}}";
        }

        /// <summary>
        /// ��ȡ GameObject ���������ꡣ
        /// </summary>
        public string GetPosition(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"�Ҳ�������{name}");
            }
            var p = go.transform.position;
            return $"({p.x},{p.y},{p.z})";
        }

        /// <summary>
        /// ���� GameObject ���������ꡣ
        /// </summary>
        public string SetPosition(string name, float x, float y, float z)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"�Ҳ�������{name}");
            }
            go.transform.position = new Vector3(x, y, z);
            return "{\"success\":true}";
        }

        /// <summary>
        /// �����־�� Unity Console��
        /// </summary>
    }
}

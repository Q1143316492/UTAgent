using System;
using System.Collections.Generic;
using System.Text;
using Python.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using UTAgent.Editor;

namespace UTAgent.Editor.Bridges
{
    public sealed partial class UTAgentPythonBridge
    {
        private static readonly Dictionary<string, UTAgentWindowHost> sOpenedWindows = new();

        public string OpenRegistered(string name)
        {
            if (!UTAgentBootstrap.IsAvailable)
            {
                return BridgeJson.Error("引擎不可用，请先初始化");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return BridgeJson.Error("name 不能为空");
            }

            try
            {
                using (Py.GIL())
                {
                    using var scope = Py.CreateScope();
                    scope.Set("window_name", name.ToPython());
                    scope.Exec(
                        "import json\n" +
                        "from unity.ui.core.registry import REGISTRY\n" +
                        "entry = REGISTRY.get(window_name)\n" +
                        "if entry is None:\n" +
                        "    _open_args_json = None\n" +
                        "else:\n" +
                        "    payload = dict(entry)\n" +
                        "    payload['name'] = window_name\n" +
                        "    _open_args_json = json.dumps(payload, ensure_ascii=False)\n");
                    using PyObject argsJson = scope.Get("_open_args_json");
                    if (argsJson.IsNone())
                    {
                        return BridgeJson.Error($"未注册窗口：{name}");
                    }

                    return Open(argsJson.ToString());
                }
            }
            catch (Exception e)
            {
                return BridgeJson.Error(e.Message);
            }
        }

        public string Open(string argsJson)
        {
            try
            {
                if (!UTAgentBootstrap.IsAvailable)
                {
                    return BridgeJson.Error("引擎不可用，请先初始化");
                }

                var name = BridgeJson.Args.GetString(argsJson, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    return BridgeJson.Error("name 不能为空");
                }

                if (sOpenedWindows.TryGetValue(name, out var existing) && existing != null)
                {
                    existing.Show();
                    return WndMgrSuccess(name, existing.gameObject);
                }

                var module = BridgeJson.Args.GetString(argsJson, "module");
                var className = BridgeJson.Args.GetString(argsJson, "class");
                var prefabPath = BridgeJson.Args.GetString(argsJson, "prefab");
                var layer = BridgeJson.Args.GetString(argsJson, "layer");
                if (string.IsNullOrWhiteSpace(layer))
                {
                    layer = "Menu";
                }

                if (string.IsNullOrWhiteSpace(prefabPath))
                {
                    return BridgeJson.Error("prefab 不能为空");
                }

                var prefab = Resources.Load<GameObject>(prefabPath);
                if (prefab == null)
                {
                    return BridgeJson.Error($"Resources 中找不到预制体：{prefabPath}");
                }

                if (!UTAgentUiRoots.TryEnsureLayerRoots(out var roots))
                {
                    return BridgeJson.Error("场景中未找到 Canvas 或分层根节点");
                }

                var parent = layer switch
                {
                    "Game" => roots.Game,
                    "Dialog" => roots.Dialog,
                    _ => roots.Menu,
                };

                var go = Object.Instantiate(prefab, parent);
                go.name = name;

                var host = go.GetComponent<UTAgentWindowHost>();
                if (host == null)
                {
                    host = go.AddComponent<UTAgentWindowHost>();
                }

                host.Configure(module, className, false);
                host.Init();
                host.Show();

                sOpenedWindows[name] = host;
                return WndMgrSuccess(name, go);
            }
            catch (Exception e)
            {
                return BridgeJson.Error(e.Message);
            }
        }

        public string Close(string argsJson)
        {
            try
            {
                var name = BridgeJson.Args.GetString(argsJson, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    return BridgeJson.Error("name 不能为空");
                }

                if (!sOpenedWindows.TryGetValue(name, out var host) || host == null)
                {
                    return "{\"success\":true,\"closed\":false}";
                }

                host.Hide();
                host.Release();
                Object.Destroy(host.gameObject);
                sOpenedWindows.Remove(name);
                return "{\"success\":true,\"closed\":true}";
            }
            catch (Exception e)
            {
                return BridgeJson.Error(e.Message);
            }
        }

        public string IsOpen(string argsJson)
        {
            try
            {
                var name = BridgeJson.Args.GetString(argsJson, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    return BridgeJson.Error("name 不能为空");
                }

                var open = sOpenedWindows.TryGetValue(name, out var host) && host != null;
                return $"{{\"success\":true,\"open\":{BridgeJson.ToLower(open)}}}";
            }
            catch (Exception e)
            {
                return BridgeJson.Error(e.Message);
            }
        }

        private static string WndMgrSuccess(string name, GameObject go)
        {
            return $"{{\"success\":true,\"name\":\"{BridgeJson.EscapeJsonInline(name)}\",\"handle\":{go.GetInstanceID()}}}";
        }

        public string InvokeMember(string typeName, string member, string argsJson)
        {
            try
            {
                if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(member))
                {
                    return BridgeJson.Error("typeName 与 member 不能为空");
                }

                switch (typeName, member)
                {
                    case ("WndBase", "get"):
                        return WndBaseGet(BridgeJson.Args.GetString(argsJson, "name"));
                    case ("WndBase", "get_widget"):
                        return WndBaseGetWidget(
                            BridgeJson.Args.GetInt(argsJson, "handle"),
                            BridgeJson.Args.GetString(argsJson, "name"));
                    case ("Image", "set_visible"):
                        return SetVisible(
                            BridgeJson.Args.GetInt(argsJson, "handle"),
                            BridgeJson.Args.GetBool(argsJson, "visible"));
                    case ("Text", "set_visible"):
                        return SetVisible(
                            BridgeJson.Args.GetInt(argsJson, "handle"),
                            BridgeJson.Args.GetBool(argsJson, "visible"));
                    case ("Text", "set_text"):
                        return TextSetText(
                            BridgeJson.Args.GetInt(argsJson, "handle"),
                            BridgeJson.Args.GetString(argsJson, "text"));
                    default:
                        return BridgeJson.Error($"未支持的调用：{typeName}.{member}");
                }
            }
            catch (Exception e)
            {
                return BridgeJson.Error(e.Message);
            }
        }

        private static string WndBaseGet(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BridgeJson.Error("name 不能为空");
            }

            var go = GameObject.Find(name);
            if (go == null)
            {
                return BridgeJson.Error($"找不到面板：{name}");
            }

            return HandleResult(go, "WndBase");
        }

        private static string WndBaseGetWidget(int parentHandle, string widgetName)
        {
            if (string.IsNullOrWhiteSpace(widgetName))
            {
                return BridgeJson.Error("name 不能为空");
            }

            var parent = ResolveHandle(parentHandle);
            if (parent == null)
            {
                return BridgeJson.Error($"无效的面板 handle：{parentHandle}");
            }

            if (!TryResolveWidget(parent.transform, widgetName, out var child, out var resolveError))
            {
                return BridgeJson.Error(resolveError);
            }

            if (child.GetComponent<Text>() != null)
            {
                return HandleResult(child.gameObject, "Text");
            }

            if (child.GetComponent<TextMeshProUGUI>() != null)
            {
                return HandleResult(child.gameObject, "Text");
            }

            if (child.GetComponent<Image>() != null)
            {
                return HandleResult(child.gameObject, "Image");
            }

            return BridgeJson.Error($"控件 {widgetName} 缺少 UGUI Text/Image 或 TextMeshProUGUI 组件");
        }

        private static bool TryResolveWidget(
            Transform root,
            string widgetName,
            out Transform child,
            out string error)
        {
            child = null;
            error = null;

            if (widgetName.Contains("/"))
            {
                child = root.Find(widgetName);
                if (child == null)
                {
                    error = $"面板 {root.name} 下找不到控件路径：{widgetName}";
                    return false;
                }

                return true;
            }

            child = root.Find(widgetName);
            if (child != null)
            {
                return true;
            }

            var matches = new List<Transform>();
            CollectDescendantsByName(root, widgetName, matches);
            if (matches.Count == 1)
            {
                child = matches[0];
                return true;
            }

            if (matches.Count > 1)
            {
                var sb = new StringBuilder();
                sb.Append($"控件名 \"{widgetName}\" 在 {root.name} 下有 {matches.Count} 个匹配，请用路径区分：");
                for (int i = 0; i < matches.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append("; ");
                    }

                    sb.Append(GetRelativePath(root, matches[i]));
                }

                error = sb.ToString();
                return false;
            }

            error = $"面板 {root.name} 下找不到控件：{widgetName}（可用路径如 CreatePanel/.../Desc）";
            return false;
        }

        private static void CollectDescendantsByName(Transform node, string name, List<Transform> results)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                var child = node.GetChild(i);
                if (child.name == name)
                {
                    results.Add(child);
                }

                CollectDescendantsByName(child, name, results);
            }
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            var parts = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Push(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                return target.name;
            }

            return string.Join("/", parts);
        }

        private static string SetVisible(int handle, bool visible)
        {
            var go = ResolveHandle(handle);
            if (go == null)
            {
                return BridgeJson.Error($"无效的 handle：{handle}");
            }

            go.SetActive(visible);
            return BridgeJson.SuccessTrue();
        }

        private static string TextSetText(int handle, string text)
        {
            var go = ResolveHandle(handle);
            if (go == null)
            {
                return BridgeJson.Error($"无效的 handle：{handle}");
            }

            var tmpText = go.GetComponent<TextMeshProUGUI>();
            if (tmpText != null)
            {
                tmpText.text = text ?? string.Empty;
                return BridgeJson.SuccessTrue();
            }

            var uiText = go.GetComponent<Text>();
            if (uiText != null)
            {
                uiText.text = text ?? string.Empty;
                return BridgeJson.SuccessTrue();
            }

            return BridgeJson.Error($"控件 {go.name} 无 TextMeshProUGUI 或 UGUI Text 组件");
        }

        private static GameObject ResolveHandle(int handle)
        {
            if (handle == 0)
            {
                return null;
            }

            var obj = Resources.InstanceIDToObject(handle);
            if (obj == null)
            {
                return null;
            }

            if (obj is GameObject go)
            {
                return go;
            }

            if (obj is Component comp)
            {
                return comp.gameObject;
            }

            return null;
        }

        private static string HandleResult(GameObject go, string typeName)
        {
            return $"{{\"success\":true,\"handle\":{go.GetInstanceID()},\"type\":{BridgeJson.EscapeJson(typeName)}}}";
        }
    }
}

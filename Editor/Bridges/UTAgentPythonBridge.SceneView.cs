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
        /// <summary>
        /// 截取 Unity Editor Scene 视图并返回 PNG base64（始终 Scene View，非 Game 视图）。
        /// </summary>
        public string CaptureSceneViewScreenshot(int maxWidth, int maxHeight)
        {
            if (maxWidth < MinScreenshotSize || maxWidth > MaxScreenshotWidth ||
                maxHeight < MinScreenshotSize || maxHeight > MaxScreenshotHeight)
            {
                return Error($"截图尺寸必须在 {MinScreenshotSize}-{MaxScreenshotWidth}x{MaxScreenshotHeight} 之间");
            }

            try
            {
                var tex = CaptureSceneView(maxWidth, maxHeight);
                if (tex == null)
                {
                    return Error("Scene 视图截图失败：无法获取活动 Scene 视图图像");
                }

                try
                {
                    var bytes = tex.EncodeToPNG();
                    if (bytes == null || bytes.Length == 0)
                    {
                        return Error("截图编码失败");
                    }
                    return BuildImageResponse(bytes, "scene");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }
            catch (Exception e)
            {
                return Error($"Scene 视图截图失败：{e.Message}");
            }
        }

        /// <summary>
        /// 截取当前视图并返回 PNG base64。
        /// Play Mode 优先使用 Game 视图；否则回退到 Scene 视图，支持纯编辑器验证。
        /// </summary>
        public string CaptureScreenshot(int maxWidth, int maxHeight)
        {
            if (maxWidth < MinScreenshotSize || maxWidth > MaxScreenshotWidth ||
                maxHeight < MinScreenshotSize || maxHeight > MaxScreenshotHeight)
            {
                return Error($"截图尺寸必须在 {MinScreenshotSize}-{MaxScreenshotWidth}x{MaxScreenshotHeight} 之间");
            }

            try
            {
                Texture2D tex = null;
                string source = null;
                if (Application.isPlaying)
                {
                    try
                    {
                        tex = ScreenCapture.CaptureScreenshotAsTexture();
                        source = "game";
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[UTAgent] Game 视图截图失败，将回退到 Scene 视图：{e.Message}");
                    }
                }

                if (tex == null)
                {
                    tex = CaptureSceneView(maxWidth, maxHeight);
                    source = "scene";
                }

                if (tex == null)
                {
                    return Error("截图失败：无法获取 Game 或 Scene 视图图像");
                }

                try
                {
                    var bytes = tex.EncodeToPNG();
                    if (bytes == null || bytes.Length == 0)
                    {
                        return Error("截图编码失败");
                    }
                    return BuildImageResponse(bytes, source);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }
            catch (Exception e)
            {
                return Error($"截图失败：{e.Message}");
            }
        }

        /// <summary>
        /// 通过反射渲染当前 Scene 视图相机，返回 Texture2D。不在 Play Mode 也能使用。
        /// </summary>
        private static Texture2D CaptureSceneView(int maxWidth, int maxHeight)
        {
            try
            {
                var editorAssembly = Assembly.Load("UnityEditor");
                var sceneViewType = editorAssembly.GetType("UnityEditor.SceneView");
                var lastActiveProperty = sceneViewType.GetProperty("lastActiveSceneView", BindingFlags.Static | BindingFlags.Public);
                var sceneView = lastActiveProperty?.GetValue(null);
                if (sceneView == null)
                {
                    Debug.LogWarning("[UTAgent] 找不到活动 Scene 视图");
                    return null;
                }

                var cameraProperty = sceneViewType.GetProperty("camera", BindingFlags.Instance | BindingFlags.Public);
                if (cameraProperty == null)
                {
                    Debug.LogWarning("[UTAgent] SceneView 没有 camera 属性");
                    return null;
                }

                var camera = cameraProperty.GetValue(sceneView) as Camera;
                if (camera == null)
                {
                    Debug.LogWarning("[UTAgent] SceneView camera 为空");
                    return null;
                }

                int width = maxWidth;
                int height = maxHeight;
                var positionProperty = sceneViewType.GetProperty("position", BindingFlags.Instance | BindingFlags.Public);
                if (positionProperty != null)
                {
                    var position = (Rect)positionProperty.GetValue(sceneView);
                    width = Mathf.Min((int)position.width, maxWidth);
                    height = Mathf.Min((int)position.height, maxHeight);
                }

                var rt = new RenderTexture(width, height, 24);
                var prevTarget = camera.targetTexture;
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;

                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                camera.targetTexture = prevTarget;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(rt);
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UTAgent] Scene 视图截图失败：{e.Message}");
                return null;
            }
        }

        private static string BuildImageResponse(byte[] bytes, string source)
        {
            var base64 = Convert.ToBase64String(bytes);
            return $"{{\"success\":true,\"message\":\"screenshot captured from {source}\",\"__image\":{{\"base64\":{EscapeJson(base64)},\"mediaType\":\"image/png\"}}}}";
        }

        // ----- Scene View 操控动词（反射，对齐 puerts ScreenCaptureBridge）-----
        // Runtime asmdef 不能直接引用 UnityEditor，所以全部走反射。

        private static object GetSceneView()
        {
            try
            {
                var t = Type.GetType("UnityEditor.SceneView,UnityEditor");
                if (t == null)
                {
                    return null;
                }
                var prop = t.GetProperty("lastActiveSceneView", BindingFlags.Static | BindingFlags.Public);
                return prop?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        public string GetSceneViewState()
        {
            var sv = GetSceneView();
            if (sv == null)
            {
                return Error("No active Scene view found.");
            }
            try
            {
                var svType = sv.GetType();
                var pivot = (Vector3)svType.GetProperty("pivot", BindingFlags.Instance | BindingFlags.Public).GetValue(sv);
                var rotation = (Quaternion)svType.GetProperty("rotation", BindingFlags.Instance | BindingFlags.Public).GetValue(sv);
                var euler = rotation.eulerAngles;
                var size = (float)svType.GetProperty("size", BindingFlags.Instance | BindingFlags.Public).GetValue(sv);
                var ortho = (bool)svType.GetProperty("orthographic", BindingFlags.Instance | BindingFlags.Public).GetValue(sv);
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                return "{"
                    + $"\"success\":true,"
                    + $"\"pivot\":{{\"x\":{pivot.x.ToString("F3", ic)},\"y\":{pivot.y.ToString("F3", ic)},\"z\":{pivot.z.ToString("F3", ic)}}},"
                    + $"\"rotation\":{{\"x\":{rotation.x.ToString("F4", ic)},\"y\":{rotation.y.ToString("F4", ic)},\"z\":{rotation.z.ToString("F4", ic)},\"w\":{rotation.w.ToString("F4", ic)}}},"
                    + $"\"eulerAngles\":{{\"x\":{euler.x.ToString("F1", ic)},\"y\":{euler.y.ToString("F1", ic)},\"z\":{euler.z.ToString("F1", ic)}}},"
                    + $"\"size\":{size.ToString("F3", ic)},"
                    + $"\"orthographic\":{ortho.ToString().ToLowerInvariant()}}}";
            }
            catch (Exception e)
            {
                return Error($"GetSceneViewState failed: {e.Message}");
            }
        }

        public string ZoomSceneView(string direction, float amount)
        {
            var sv = GetSceneView();
            if (sv == null)
            {
                return Error("No active Scene view found.");
            }
            var svType = sv.GetType();
            var sizeProp = svType.GetProperty("size", BindingFlags.Instance | BindingFlags.Public);
            var repaint = svType.GetMethod("Repaint", BindingFlags.Instance | BindingFlags.Public);
            try
            {
                float oldSize = (float)sizeProp.GetValue(sv);
                float factor;
                string d = (direction ?? "").ToLowerInvariant().Trim();
                switch (d)
                {
                    case "forward":
                    case "in":
                        factor = 1f / (1f + amount * 0.2f);
                        break;
                    case "backward":
                    case "out":
                        factor = 1f + amount * 0.2f;
                        break;
                    default:
                        return Error($"Unknown zoom direction '{direction}'. Use 'forward'/'in' or 'backward'/'out'.");
                }
                float newSize = Mathf.Clamp(oldSize * factor, 0.01f, 10000f);
                sizeProp.SetValue(sv, newSize);
                repaint?.Invoke(sv, null);
                return $"{{\"success\":true,\"operation\":\"zoom\",\"direction\":\"{direction}\",\"amount\":{amount},\"description\":\"Zoomed {direction}. Size: {oldSize:F2} -> {newSize:F2}\"}}";
            }
            catch (Exception e)
            {
                return Error($"ZoomSceneView failed: {e.Message}");
            }
        }

        public string PanSceneView(string direction, float amount)
        {
            var sv = GetSceneView();
            if (sv == null)
            {
                return Error("No active Scene view found.");
            }
            var svType = sv.GetType();
            try
            {
                var cam = (Camera)svType.GetProperty("camera", BindingFlags.Instance | BindingFlags.Public).GetValue(sv);
                if (cam == null)
                {
                    return Error("Scene view camera is not available.");
                }
                var pivotProp = svType.GetProperty("pivot", BindingFlags.Instance | BindingFlags.Public);
                var sizeProp = svType.GetProperty("size", BindingFlags.Instance | BindingFlags.Public);
                var repaint = svType.GetMethod("Repaint", BindingFlags.Instance | BindingFlags.Public);
                float panDist = amount * (float)sizeProp.GetValue(sv) * 0.1f;
                Vector3 offset;
                string d = (direction ?? "").ToLowerInvariant().Trim();
                switch (d)
                {
                    case "up":
                        offset = cam.transform.up * panDist;
                        break;
                    case "down":
                        offset = -cam.transform.up * panDist;
                        break;
                    case "left":
                        offset = -cam.transform.right * panDist;
                        break;
                    case "right":
                        offset = cam.transform.right * panDist;
                        break;
                    default:
                        return Error($"Unknown pan direction '{direction}'. Use 'up', 'down', 'left', or 'right'.");
                }
                Vector3 oldPivot = (Vector3)pivotProp.GetValue(sv);
                pivotProp.SetValue(sv, oldPivot + offset);
                repaint?.Invoke(sv, null);
                return $"{{\"success\":true,\"operation\":\"pan\",\"direction\":\"{direction}\",\"amount\":{amount},\"description\":\"Panned {direction}.\"}}";
            }
            catch (Exception e)
            {
                return Error($"PanSceneView failed: {e.Message}");
            }
        }

        public string OrbitSceneView(string direction, float amount)
        {
            var sv = GetSceneView();
            if (sv == null)
            {
                return Error("No active Scene view found.");
            }
            var svType = sv.GetType();
            try
            {
                var rotProp = svType.GetProperty("rotation", BindingFlags.Instance | BindingFlags.Public);
                var repaint = svType.GetMethod("Repaint", BindingFlags.Instance | BindingFlags.Public);
                Quaternion oldRot = (Quaternion)rotProp.GetValue(sv);
                float angleDeg = amount * 15f;
                Quaternion delta;
                string d = (direction ?? "").ToLowerInvariant().Trim();
                switch (d)
                {
                    case "up":
                        delta = Quaternion.AngleAxis(-angleDeg, oldRot * Vector3.right);
                        break;
                    case "down":
                        delta = Quaternion.AngleAxis(angleDeg, oldRot * Vector3.right);
                        break;
                    case "left":
                        delta = Quaternion.AngleAxis(-angleDeg, Vector3.up);
                        break;
                    case "right":
                        delta = Quaternion.AngleAxis(angleDeg, Vector3.up);
                        break;
                    default:
                        return Error($"Unknown orbit direction '{direction}'. Use 'up', 'down', 'left', or 'right'.");
                }
                rotProp.SetValue(sv, delta * oldRot);
                repaint?.Invoke(sv, null);
                return $"{{\"success\":true,\"operation\":\"orbit\",\"direction\":\"{direction}\",\"amount\":{amount},\"description\":\"Orbited {direction} by {angleDeg:F1} deg.\"}}";
            }
            catch (Exception e)
            {
                return Error($"OrbitSceneView failed: {e.Message}");
            }
        }

        public string SetSceneViewCamera(float px, float py, float pz, bool setPivot,
            float rx, float ry, float rz, bool setRotation, float size)
        {
            var sv = GetSceneView();
            if (sv == null)
            {
                return Error("No active Scene view found.");
            }
            var svType = sv.GetType();
            try
            {
                if (setPivot)
                {
                    svType.GetProperty("pivot", BindingFlags.Instance | BindingFlags.Public).SetValue(sv, new Vector3(px, py, pz));
                }
                if (setRotation)
                {
                    svType.GetProperty("rotation", BindingFlags.Instance | BindingFlags.Public).SetValue(sv, Quaternion.Euler(rx, ry, rz));
                }
                if (size > 0f)
                {
                    svType.GetProperty("size", BindingFlags.Instance | BindingFlags.Public).SetValue(sv, size);
                }
                svType.GetMethod("Repaint", BindingFlags.Instance | BindingFlags.Public)?.Invoke(sv, null);
                var p = (Vector3)svType.GetProperty("pivot", BindingFlags.Instance | BindingFlags.Public).GetValue(sv);
                var e = ((Quaternion)svType.GetProperty("rotation", BindingFlags.Instance | BindingFlags.Public).GetValue(sv)).eulerAngles;
                var s = (float)svType.GetProperty("size", BindingFlags.Instance | BindingFlags.Public).GetValue(sv);
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                return "{"
                    + $"\"success\":true,"
                    + $"\"pivot\":{{\"x\":{p.x.ToString("F3", ic)},\"y\":{p.y.ToString("F3", ic)},\"z\":{p.z.ToString("F3", ic)}}},"
                    + $"\"eulerAngles\":{{\"x\":{e.x.ToString("F1", ic)},\"y\":{e.y.ToString("F1", ic)},\"z\":{e.z.ToString("F1", ic)}}},"
                    + $"\"size\":{s.ToString("F3", ic)}}}";
            }
            catch (Exception e)
            {
                return Error($"SetSceneViewCamera failed: {e.Message}");
            }
        }

        public string FocusSceneViewOn(string name)
        {
            var sv = GetSceneView();
            if (sv == null)
            {
                return Error("No active Scene view found.");
            }
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"GameObject '{name}' not found.");
            }
            try
            {
                var selectionType = Type.GetType("UnityEditor.Selection,UnityEditor");
                var activeGOProp = selectionType?.GetProperty("activeGameObject", BindingFlags.Static | BindingFlags.Public);
                activeGOProp?.SetValue(null, go);

                // FrameSelected 在 Unity 版本间签名不同：
                //   2022+: static void FrameSelected() 或 static void FrameSelected(bool)
                //   2021-: instance void FrameSelected(bool)
                // 全量搜索，优先无参 static 版。
                var svTypeCur = sv.GetType();
                var frameMethod = svTypeCur.GetMethod("FrameSelected", BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public, null,
                    Type.EmptyTypes, null);
                if (frameMethod == null)
                {
                    frameMethod = svTypeCur.GetMethod("FrameSelected", BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public, null,
                        new[] { typeof(bool) }, null);
                }
                if (frameMethod != null)
                {
                    object[] args = frameMethod.GetParameters().Length == 0 ? null : new object[] { true };
                    frameMethod.Invoke(frameMethod.IsStatic ? null : sv, args);
                }
                else
                {
                    Debug.LogWarning("[UTAgent] FrameSelected method not found via reflection.");
                }
                var svType2 = sv.GetType();
                var p = (Vector3)svType2.GetProperty("pivot", BindingFlags.Instance | BindingFlags.Public).GetValue(sv);
                var s = (float)svType2.GetProperty("size", BindingFlags.Instance | BindingFlags.Public).GetValue(sv);
                var ic2 = System.Globalization.CultureInfo.InvariantCulture;
                return $"{{\"success\":true,\"focused\":{EscapeJson(go.name)},\"pivot\":{{\"x\":{p.x.ToString("F3", ic2)},\"y\":{p.y.ToString("F3", ic2)},\"z\":{p.z.ToString("F3", ic2)}}},\"size\":{s.ToString("F3", ic2)}}}";
            }
            catch (Exception e)
            {
                return Error($"FocusSceneViewOn failed: {e.Message}");
            }
        }

        public string SelectGameObject(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                return Error($"GameObject '{name}' not found.");
            }
            try
            {
                var selType = Type.GetType("UnityEditor.Selection,UnityEditor");
                selType?.GetProperty("activeGameObject", BindingFlags.Static | BindingFlags.Public)?.SetValue(null, go);
                var eguiType = Type.GetType("UnityEditor.EditorGUIUtility,UnityEditor");
                var ping = eguiType?.GetMethod("PingObject", BindingFlags.Static | BindingFlags.Public, null,
                    new[] { typeof(UnityEngine.Object) }, null);
                ping?.Invoke(null, new object[] { go });
                return $"{{\"success\":true,\"selected\":{EscapeJson(go.name)}}}";
            }
            catch (Exception e)
            {
                return Error($"SelectGameObject failed: {e.Message}");
            }
        }

        public string SaveScene()
        {
            try
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                var esmType = Type.GetType("UnityEditor.SceneManagement.EditorSceneManager,UnityEditor");
                esmType?.GetMethod("MarkSceneDirty", BindingFlags.Static | BindingFlags.Public, null,
                    new[] { typeof(UnityEngine.SceneManagement.Scene) }, null)?.Invoke(null, new object[] { scene });
                var savedMethod = esmType?.GetMethod("SaveScene", BindingFlags.Static | BindingFlags.Public, null,
                    new[] { typeof(UnityEngine.SceneManagement.Scene) }, null);
                bool saved = savedMethod != null && (bool)savedMethod.Invoke(null, new object[] { scene });
                if (saved)
                {
                    return $"{{\"success\":true,\"scene\":{EscapeJson(scene.name)},\"path\":{EscapeJson(scene.path)}}}";
                }
                return Error($"Failed to save scene '{scene.name}'.");
            }
            catch (Exception e)
            {
                return Error($"SaveScene failed: {e.Message}");
            }
        }
    }
}

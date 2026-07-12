using UnityEngine;

namespace UTAgent
{
    /// <summary>
    /// 在已有 Canvas 根节点下确保 Game/Menu/Dialog 层（与 GameCore.WindowManager 一致，不创建 Canvas）。
    /// </summary>
    public static class UTAgentUiRoots
    {
        private const string CanvasName = "Canvas";

        public struct LayerRoots
        {
            public Transform Game;
            public Transform Menu;
            public Transform Dialog;
        }

        public static bool TryEnsureLayerRoots(out LayerRoots roots)
        {
            roots = default;
            var canvasGo = GameObject.Find(CanvasName);
            if (canvasGo == null)
            {
                return false;
            }

            var canvas = canvasGo.GetComponent<Canvas>();
            if (canvas == null)
            {
                return false;
            }

            roots = new LayerRoots
            {
                Game = CreateLayerRoot(canvas.transform, "Game"),
                Menu = CreateLayerRoot(canvas.transform, "Menu"),
                Dialog = CreateLayerRoot(canvas.transform, "Dialog"),
            };
            return true;
        }

        public static bool TryReparentToMenu(Transform windowTransform)
        {
            if (windowTransform == null)
            {
                return false;
            }

            if (!TryEnsureLayerRoots(out var roots))
            {
                return false;
            }

            windowTransform.SetParent(roots.Menu, false);
            return true;
        }

        private static Transform CreateLayerRoot(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go.transform;
        }
    }
}

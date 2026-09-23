using Arikan;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardMapRoutePrefabUpgrade
    {
        [MenuItem("MazeParty/Board/Install Map Route Dots")]
        public static void Install()
        {
            const string path = "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try { Ensure(root); PrefabUtility.SaveAsPrefabAsset(root, path); AssetDatabase.SaveAssets(); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void Ensure(GameObject root)
        {
            foreach (var view in root.GetComponentsInChildren<BoardMinimapView>(true))
            {
                var data = new SerializedObject(view);
                if (data.FindProperty("shopRouteGraphic").objectReferenceValue != null) continue;
                var projection = (MiniMapView)data.FindProperty("projection").objectReferenceValue;
                var go = new GameObject("Key Shop Route Dots", typeof(RectTransform), typeof(CanvasRenderer), typeof(BoardMapRouteGraphic));
                go.layer = LayerMask.NameToLayer("UI");
                go.transform.SetParent(projection.otherDotCanvas, false);
                var graphic = go.GetComponent<BoardMapRouteGraphic>();
                var rect = graphic.rectTransform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                graphic.color = new Color(1f, .82f, .02f);
                graphic.raycastTarget = false;
                // Floor and tile details first, route second, player markers last.
                var players = data.FindProperty("players");
                var firstPlayer = (Image)players.GetArrayElementAtIndex(0).objectReferenceValue;
                go.transform.SetSiblingIndex(firstPlayer.transform.GetSiblingIndex());
                data.FindProperty("shopRouteGraphic").objectReferenceValue = graphic;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}

using System;
using Arikan;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>
    /// One-time visual migration for the full map plus the required local
    /// minimap vision layer. Re-running setup repairs bindings without
    /// overwriting an already-migrated layout.
    /// </summary>
    public static class BoardMapVisibilityPrefabUpgrade
    {
        private const string PrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";
        private const int CurrentPresentationVersion = 1;
        private static readonly Vector2 FullMapSize = new Vector2(780f, 780f);
        private static readonly Vector2 FullMapSurfaceSize =
            new Vector2(728f, 728f);
        private static readonly Vector2 FullMapPlayerSize =
            new Vector2(30f, 30f);

        [MenuItem("MazeParty/Board/Upgrade Map Visibility Presentation")]
        public static void UpgradePrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Ensure(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void Ensure(GameObject root)
        {
            var map = root != null ? root.GetComponent<BoardMapView>() : null;
            if (map == null)
            {
                throw new InvalidOperationException(
                    "BoardCanvas.prefab requires BoardMapView.");
            }

            var mapData = new SerializedObject(map);
            var live = mapData.FindProperty("liveMinimap")
                .objectReferenceValue as BoardMinimapView;
            var full = mapData.FindProperty("fullMap")
                .objectReferenceValue as BoardMinimapView;
            if (live == null || full == null)
            {
                throw new InvalidOperationException(
                    "Board map views must exist before visibility migration.");
            }

            EnsureVisionLayer(live);
            var version = mapData.FindProperty("mapPresentationVersion");
            if (version.intValue < CurrentPresentationVersion)
            {
                ApplyFullMapPresentation(mapData, full);
                version.intValue = CurrentPresentationVersion;
                mapData.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(map);
            }
            else
            {
                ValidateFullMapPresentation(mapData, full);
            }
        }

        private static void EnsureVisionLayer(BoardMinimapView live)
        {
            var data = new SerializedObject(live);
            var projection = data.FindProperty("projection")
                .objectReferenceValue as MiniMapView;
            var surface = projection != null ? projection.otherDotCanvas : null;
            if (surface == null)
            {
                throw new InvalidOperationException(
                    "The live minimap projection surface is missing.");
            }

            var property = data.FindProperty("visionGraphic");
            var graphic = property.objectReferenceValue as BoardMapVisionGraphic;
            if (graphic == null)
            {
                var existing = surface.Find("Camera Vision");
                graphic = existing != null
                    ? existing.GetComponent<BoardMapVisionGraphic>()
                    : null;
            }

            if (graphic == null)
            {
                var graphicObject = new GameObject(
                    "Camera Vision",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(BoardMapVisionGraphic));
                graphicObject.layer = LayerMask.NameToLayer("UI");
                graphicObject.transform.SetParent(surface, false);
                graphic = graphicObject.GetComponent<BoardMapVisionGraphic>();
                graphic.color = new Color(0.015f, 0.025f, 0.045f, 0.72f);
            }

            var rect = graphic.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
            graphic.raycastTarget = false;
            property.objectReferenceValue = graphic;
            data.ApplyModifiedPropertiesWithoutUndo();
            live.BindVisionGraphic(graphic);

            // Static board information is dimmed by the overlay. Player markers
            // remain above it and are visibility-filtered separately.
            graphic.transform.SetAsLastSibling();
            var players = data.FindProperty("players");
            for (var slot = 0; slot < players.arraySize; slot++)
            {
                var marker = players.GetArrayElementAtIndex(slot)
                    .objectReferenceValue as Image;
                if (marker != null)
                    marker.transform.SetAsLastSibling();
            }

            EditorUtility.SetDirty(graphic);
            EditorUtility.SetDirty(live);
        }

        private static void ApplyFullMapPresentation(
            SerializedObject mapData,
            BoardMinimapView full)
        {
            var panel = mapData.FindProperty("fullMapPanel")
                .objectReferenceValue as GameObject;
            if (panel == null)
                throw new InvalidOperationException("Full-map panel is missing.");

            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = FullMapSize;

            var data = new SerializedObject(full);
            var projection = data.FindProperty("projection")
                .objectReferenceValue as MiniMapView;
            if (projection == null || projection.otherDotCanvas == null)
            {
                throw new InvalidOperationException(
                    "Full-map projection surface is missing.");
            }

            var surface = projection.otherDotCanvas;
            surface.anchorMin = surface.anchorMax = surface.pivot =
                Vector2.one * 0.5f;
            surface.anchoredPosition = Vector2.zero;
            surface.sizeDelta = FullMapSurfaceSize;

            var players = data.FindProperty("players");
            var highlights = data.FindProperty("localHighlights");
            for (var slot = 0; slot < players.arraySize; slot++)
            {
                var marker = players.GetArrayElementAtIndex(slot)
                    .objectReferenceValue as Image;
                if (marker == null)
                    throw new InvalidOperationException(
                        "Full-map player marker is missing: " + slot);

                marker.rectTransform.sizeDelta = FullMapPlayerSize;
                var outline = marker.GetComponent<Outline>();
                if (outline == null)
                    outline = marker.gameObject.AddComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = true;

                var highlight = highlights.GetArrayElementAtIndex(slot)
                    .objectReferenceValue as GameObject;
                var highlightRect = highlight != null
                    ? highlight.GetComponent<RectTransform>()
                    : null;
                if (highlightRect != null)
                {
                    highlightRect.anchoredPosition = new Vector2(0f, 24f);
                    highlightRect.sizeDelta = new Vector2(40f, 20f);
                }

                EditorUtility.SetDirty(marker);
                EditorUtility.SetDirty(outline);
            }

            EditorUtility.SetDirty(panelRect);
            EditorUtility.SetDirty(surface);
            EditorUtility.SetDirty(full);
        }

        private static void ValidateFullMapPresentation(
            SerializedObject mapData,
            BoardMinimapView full)
        {
            var panel = mapData.FindProperty("fullMapPanel")
                .objectReferenceValue as GameObject;
            if (panel == null)
                throw new InvalidOperationException("Full-map panel is missing.");

            var data = new SerializedObject(full);
            var players = data.FindProperty("players");
            for (var slot = 0; slot < players.arraySize; slot++)
            {
                var marker = players.GetArrayElementAtIndex(slot)
                    .objectReferenceValue as Image;
                var outline = marker != null
                    ? marker.GetComponent<Outline>()
                    : null;
                if (outline == null || outline.effectColor != Color.white)
                {
                    throw new InvalidOperationException(
                        "Full-map player markers require a white Outline.");
                }
            }
        }
    }
}

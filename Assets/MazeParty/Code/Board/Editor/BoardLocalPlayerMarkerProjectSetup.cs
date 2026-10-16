#if UNITY_EDITOR
using System;
using System.Linq;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardLocalPlayerMarkerProjectSetup
    {
        public const string PrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardLocalPlayerMarker.prefab";
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";

        [MenuItem("MazeParty/Board/Install Local Player Marker")]
        public static void Install()
        {
            var scene = SceneManager.GetSceneByPath(BoardScenePath);
            var opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(BoardScenePath, OpenSceneMode.Additive);
            try
            {
                var presenter = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<BoardFlowCameraPresenter>(true))
                    .Single();
                if (presenter.LocalPlayerMarker == null)
                {
                    presenter.ConfigureLocalPlayerMarker(CreateInstance(presenter.transform));
                    EditorUtility.SetDirty(presenter);
                    EditorSceneManager.MarkSceneDirty(scene);
                }
                if (opened) EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        public static BoardLocalPlayerMarkerView CreateInstance(Transform parent)
        {
            var prefab = EnsurePrefab();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, parent);
            return instance.GetComponent<BoardLocalPlayerMarkerView>();
        }

        public static BoardLocalPlayerMarkerView EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                var view = existing.GetComponent<BoardLocalPlayerMarkerView>();
                if (view == null || !view.HasRequiredReferences)
                    throw new InvalidOperationException("Board local-player marker bindings are incomplete.");
                return view;
            }

            var root = new GameObject("BoardLocalPlayerMarker", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(BoardLocalPlayerMarkerView));
            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 110;
                canvas.enabled = false;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                var labelObject = new GameObject("MeLabel", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Text), typeof(Outline));
                labelObject.transform.SetParent(root.transform, false);
                var rect = (RectTransform)labelObject.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(100f, 60f);
                var label = labelObject.GetComponent<Text>();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.text = "ME";
                label.fontSize = 34;
                label.fontStyle = FontStyle.Bold;
                label.color = Color.black;
                label.alignment = TextAnchor.MiddleCenter;
                label.raycastTarget = false;
                var outline = labelObject.GetComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(2f, -2f);
                var view = root.GetComponent<BoardLocalPlayerMarkerView>();
                view.Configure(canvas, label);
                var playerLabels = new Text[MultiplayerConstants.MaxPlayers];
                var playerOutlines = new Outline[MultiplayerConstants.MaxPlayers];
                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                {
                    var playerLabel = UnityEngine.Object.Instantiate(labelObject, root.transform, false);
                    playerLabel.name = "P" + (slot + 1) + "Label";
                    playerLabels[slot] = playerLabel.GetComponent<Text>();
                    playerLabels[slot].text = "P" + (slot + 1);
                    playerOutlines[slot] = playerLabel.GetComponent<Outline>();
                }
                view.ConfigurePlayerMarkers(playerLabels, playerOutlines);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)
                .GetComponent<BoardLocalPlayerMarkerView>();
        }
    }
}
#endif

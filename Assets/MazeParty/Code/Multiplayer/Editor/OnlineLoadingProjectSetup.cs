using System;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class OnlineLoadingProjectSetup
    {
        public const string PrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/UI/OnlineLoadingCanvas.prefab";

        public static void EnsureSceneInstance(Scene scene, OnlineSessionController controller)
        {
            var prefab = LoadOrCreatePrefab();
            var view = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<OnlineLoadingView>(true))
                .SingleOrDefault();
            if (view == null)
                view = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene))
                    .GetComponent<OnlineLoadingView>();
            controller.ConfigureLoadingView(view);
            EditorUtility.SetDirty(controller);
        }

        public static GameObject LoadOrCreatePrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                var lobby = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab");
                var lobbyBackground = lobby.GetComponentsInChildren<Image>(true)
                    .Single(image => image.name == "Pre-Lobby Gameplay Background");
                var root = new GameObject("Online Loading Canvas", typeof(RectTransform),
                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster),
                    typeof(CanvasGroup), typeof(OnlineLoadingView));
                try
                {
                    root.layer = LayerMask.NameToLayer("UI");
                    var canvas = root.GetComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 900;
                    var scaler = root.GetComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    scaler.matchWidthOrHeight = 0.5f;
                    var group = root.GetComponent<CanvasGroup>();
                    group.alpha = 0f;
                    group.interactable = false;
                    group.blocksRaycasts = false;

                    var imageObject = new GameObject("Lobby Background", typeof(RectTransform), typeof(Image));
                    imageObject.layer = root.layer;
                    imageObject.transform.SetParent(root.transform, false);
                    var image = imageObject.GetComponent<Image>();
                    image.sprite = lobbyBackground.sprite;
                    image.color = Color.white;
                    image.raycastTarget = true;
                    image.rectTransform.anchorMin = Vector2.zero;
                    image.rectTransform.anchorMax = Vector2.one;
                    image.rectTransform.sizeDelta = Vector2.zero;
                    var aspect = imageObject.AddComponent<AspectRatioFitter>();
                    aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                    aspect.aspectRatio = image.sprite.rect.width / image.sprite.rect.height;

                    var textObject = new GameObject("Entering Text", typeof(RectTransform), typeof(Text), typeof(Outline));
                    textObject.layer = root.layer;
                    textObject.transform.SetParent(root.transform, false);
                    var label = textObject.GetComponent<Text>();
                    label.rectTransform.sizeDelta = new Vector2(1000f, 130f);
                    label.alignment = TextAnchor.MiddleCenter;
                    label.font = AssetDatabase.LoadAssetAtPath<Font>(GameFonts.EnglishKoreanAssetPath);
                    label.fontSize = 72;
                    label.fontStyle = FontStyle.Normal;
                    label.color = Color.white;
                    label.text = "Entering";
                    label.raycastTarget = false;
                    var outline = textObject.GetComponent<Outline>();
                    outline.effectColor = Color.black;
                    outline.effectDistance = new Vector2(3f, -3f);
                    outline.useGraphicAlpha = true;

                    var serialized = new SerializedObject(root.GetComponent<OnlineLoadingView>());
                    serialized.FindProperty("canvasGroup").objectReferenceValue = group;
                    serialized.FindProperty("background").objectReferenceValue = image;
                    serialized.FindProperty("loadingText").objectReferenceValue = label;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }

            var view = prefab.GetComponent<OnlineLoadingView>();
            if (view == null || !view.HasRequiredReferences)
                throw new InvalidOperationException("OnlineLoadingCanvas.prefab has incomplete bindings.");
            return prefab;
        }
    }
}

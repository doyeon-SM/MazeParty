using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardPresentationFeedbackSetup
    {
        public const string CloudPath = "Assets/MazeParty/Prefabs/Board/UI/BoardCameraCloudCanvas.prefab";
        [MenuItem("MazeParty/Gameplay/Apply Board Presentation Feedback")]
        public static void Apply()
        {
            var cloud = EnsureCloud();
            // The ground icon is already authored in its prefab. Validate it without
            // overwriting subsequent artist changes when setup is run again.
            var icon = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Board/UI/BoardTileEffectWorld.prefab");
            if (icon == null || !icon.GetComponent<BoardTileEffectWorldView>().HasRequiredReferences)
                throw new System.InvalidOperationException("Board tile effect prefab bindings are missing.");

            var prior = SceneManager.GetActiveScene();
            const string path = "Assets/MazeParty/Scenes/Board/Board.unity";
            var scene = SceneManager.GetSceneByPath(path);
            var loaded = scene.IsValid() && scene.isLoaded;
            if (!loaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var director = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameplayCameraDirector>(true)).Single();
                var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BoardCameraCloudView>(true)).SingleOrDefault();
                if (view == null) view = ((GameObject)PrefabUtility.InstantiatePrefab(cloud.gameObject, scene)).GetComponent<BoardCameraCloudView>();
                var data = new SerializedObject(director);
                data.FindProperty("cloudTransition").objectReferenceValue = view;
                data.FindProperty("boardResourceEventHeightOffset").floatValue = -.15f;
                data.FindProperty("smoothCameraTransitions").boolValue = true;
                data.ApplyModifiedPropertiesWithoutUndo();
                var anchor = director.BoardFramingAnchor;
                if (anchor != null)
                {
                    var framing = new SerializedObject(anchor);
                    framing.FindProperty("settings").FindPropertyRelative("yawDegrees").floatValue = 0f;
                    framing.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (!loaded) EditorSceneManager.CloseScene(scene, true);
                if (prior.IsValid()) SceneManager.SetActiveScene(prior);
            }
            AssetDatabase.SaveAssets();
        }

        private static BoardCameraCloudView EnsureCloud()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(CloudPath);
            if (existing != null) return existing.GetComponent<BoardCameraCloudView>();
            var root = new GameObject("Board Camera Cloud Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(CanvasGroup), typeof(GraphicRaycaster), typeof(BoardCameraCloudView));
            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                var group = root.GetComponent<CanvasGroup>(); group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;
                var veil = Image(root.transform, "Opaque Cloud Veil", new Color(.78f, .85f, .92f, 1f));
                veil.rectTransform.anchorMin = Vector2.zero; veil.rectTransform.anchorMax = Vector2.one;
                veil.rectTransform.offsetMin = veil.rectTransform.offsetMax = Vector2.zero;
                veil.raycastTarget = true;
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Ignore/Modern UI Pack/Textures/Icon/Weather/Cloud Filled.png");
                if (sprite == null) throw new System.InvalidOperationException("Cloud Filled sprite is missing.");
                var positions = new[] {new Vector2(-720,300),new Vector2(550,340),new Vector2(-530,-320),new Vector2(680,-290),new Vector2(0,20)};
                var layers = new RectTransform[positions.Length];
                for (var i = 0; i < positions.Length; i++)
                {
                    var image = Image(root.transform, "Cloud " + (i + 1), i % 2 == 0 ? Color.white : new Color(.9f,.94f,.98f,1f));
                    image.sprite = sprite; image.preserveAspect = true;
                    image.rectTransform.sizeDelta = new Vector2(1400, 1000);
                    image.rectTransform.anchoredPosition = positions[i];
                    if (i % 2 == 1) image.rectTransform.localRotation = Quaternion.Euler(0,0,180);
                    layers[i] = image.rectTransform;
                }
                var data = new SerializedObject(root.GetComponent<BoardCameraCloudView>());
                data.FindProperty("rootCanvas").objectReferenceValue = canvas;
                data.FindProperty("group").objectReferenceValue = group;
                data.FindProperty("opaqueVeil").objectReferenceValue = veil;
                var clouds = data.FindProperty("clouds"); clouds.arraySize = layers.Length;
                for (var i = 0; i < layers.Length; i++) clouds.GetArrayElementAtIndex(i).objectReferenceValue = layers[i];
                data.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, CloudPath).GetComponent<BoardCameraCloudView>();
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static Image Image(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
            return image;
        }
    }
}

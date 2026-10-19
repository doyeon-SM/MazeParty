using System;
using System.Linq;
using MazeParty.Multiplayer;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class OddOneOutProjectSetup
    {
        public const string ScenePath = "Assets/MazeParty/Scenes/Minigames/OddOneOut/OddOneOut.unity";
        public const string PrefabFolder = "Assets/MazeParty/Prefabs/Minigames/OddOneOut";
        public const string EnvironmentPath = PrefabFolder + "/OddOneOutEnvironment.prefab";
        public const string HudPath = PrefabFolder + "/UI/OddOneOutHud.prefab";
        public const string CommonHudPath = "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab";
        [MenuItem("MazeParty/Minigames/Build Odd One Out")]
        public static void BuildOddOneOutAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            EnsureFolder(PrefabFolder + "/UI"); EnsureFolder("Assets/MazeParty/Scenes/Minigames/OddOneOut");
            var hudPrefab = EnsureHud();
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded && scene.isDirty) throw new InvalidOperationException("Save OddOneOut before setup.");
            var opened = !scene.IsValid() || !scene.isLoaded;
            var exists = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;
            if (opened) scene = exists ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive) :
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var source = SceneManager.GetSceneByPath(SequenceMemoryProjectSetup.ScenePath);
            var openedSource = !source.IsValid() || !source.isLoaded;
            try
            {
                if (!exists)
                {
                    if (openedSource) source = EditorSceneManager.OpenScene(SequenceMemoryProjectSetup.ScenePath, OpenSceneMode.Additive);
                    if (source.isDirty) throw new InvalidOperationException("Save SequenceMemory before copying its authored environment.");
                    var sourceView = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SequenceMemoryNetworkView>(true)).Single();
                    var data = new SerializedObject(sourceView);
                    var originalArena = (GameObject)data.FindProperty("arenaPresentation").objectReferenceValue;
                    var originalCamera = (CinemachineCamera)data.FindProperty("sharedCamera").objectReferenceValue;
                    var originalAnchors = data.FindProperty("playerAnchors");
                    var paths = Enumerable.Range(0,4).Select(i => AnimationUtility.CalculateTransformPath(
                        (Transform)originalAnchors.GetArrayElementAtIndex(i).objectReferenceValue, originalArena.transform)).ToArray();
                    SceneManager.SetActiveScene(scene);
                    var environmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
                    if (environmentPrefab == null)
                    {
                        var copy = UnityEngine.Object.Instantiate(originalArena);
                        try
                        {
                            copy.name = "Odd One Out Environment";
                            var npc = (Transform)data.FindProperty("npcAnchor").objectReferenceValue;
                            if (npc != null)
                            {
                                var target = copy.transform.Find(AnimationUtility.CalculateTransformPath(npc, originalArena.transform));
                                if (target != null) UnityEngine.Object.DestroyImmediate(target.gameObject);
                            }
                            var authorityRoot = copy.transform.Find("Authority Colliders");
                            if (authorityRoot != null) UnityEngine.Object.DestroyImmediate(authorityRoot.gameObject);
                            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                            environmentPrefab = PrefabUtility.SaveAsPrefabAsset(copy, EnvironmentPath);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(copy); }
                    }
                    var root = new GameObject("Odd One Out Network State");
                    var arena = (GameObject)PrefabUtility.InstantiatePrefab(environmentPrefab, root.transform);
                    arena.name = "Odd One Out Environment";
                    var anchors = paths.Select(p => arena.transform.Find(p)).ToArray();
                    var cameraObject = UnityEngine.Object.Instantiate(originalCamera.gameObject, root.transform);
                    cameraObject.name = "CM_OddOneOutShared";
                    var camera = cameraObject.GetComponent<CinemachineCamera>(); camera.Priority = 0;
                    // Frame the four choices; this game has no NPC or sequence display above them.
                    camera.transform.position = new Vector3(SequenceMemoryProjectSetup.ArenaCenterX, 1f, -2f) - camera.transform.forward * 22f;
                    var lens = camera.Lens; lens.OrthographicSize = 6f; camera.Lens = lens;
                    foreach (var anchor in anchors) { anchor.rotation = Quaternion.Euler(0f, 180f, 0f); PrefabUtility.RecordPrefabInstancePropertyModifications(anchor); }
                    var players = new GameObject("Runtime Players").transform; players.SetParent(root.transform, false);
                    var score = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CoinCollectProjectSetup.ScoreHudPath), scene);
                    var hud = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab, scene);
                    EditorSceneManager.SaveScene(scene, ScenePath);
                    root.AddComponent<NetworkObject>(); var state = root.AddComponent<NetworkOddOneOutState>();
                    root.AddComponent<OddOneOutNetworkView>().Configure(state, camera, arena, players, anchors,
                        score.GetComponent<BouncingBallsHudBindings>(), hud.GetComponent<OddOneOutHudBindings>(),
                        AssetDatabase.LoadAssetAtPath<GameObject>(MinigameVfxProjectSetup.ArrivalFireworksPrefabPath));
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
                var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<OddOneOutNetworkView>(true)).Single();
                var bindings = new SerializedObject(view);
                if (bindings.FindProperty("practiceCountdown").objectReferenceValue == null)
                {
                    var commonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CommonHudPath);
                    if (commonPrefab == null) throw new InvalidOperationException("Shared minigame HUD required.");
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(commonPrefab, scene);
                    instance.name = "Odd One Out Practice Common HUD";
                    var countdown = instance.GetComponent<MinigameStartCountdownView>();
                    view.ConfigurePracticeCountdown(countdown); EditorUtility.SetDirty(view);
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
                if (!view.HasRequiredReferences) throw new InvalidOperationException("OddOneOut scene bindings are incomplete.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (openedSource && source.IsValid() && source.isLoaded) EditorSceneManager.CloseScene(source, true);
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
            SoundSystemProjectSetup.EnsureCues(); SoundSystemProjectSetup.RefreshLibraryMenu(); AssetDatabase.SaveAssets();
        }
        private static GameObject EnsureHud()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            if (existing != null)
            {
                if (existing.GetComponent<OddOneOutHudBindings>()?.HasRequiredReferences != true)
                    throw new InvalidOperationException("OddOneOut HUD bindings are incomplete.");
                return existing;
            }
            var sprites = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(ItemVisualFeedbackProjectSetup.InputSpritePath);
            if (sprites == null) throw new InvalidOperationException("Input icon asset required.");
            var glyph = sprites.spriteCharacterTable.Single(c => c.name == "ml").glyph.glyphRect;
            var root = new GameObject("Odd One Out HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(LocalizedFontScope));
            try
            {
                var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 36;
                var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
                var action = new GameObject("Click Action", typeof(RectTransform), typeof(Image));
                action.transform.SetParent(root.transform, false);
                var rect = (RectTransform)action.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f,0f);
                rect.anchoredPosition = new Vector2(0,120); rect.sizeDelta = new Vector2(300,96);
                var background = action.GetComponent<Image>(); background.color = new Color(.08f,.1f,.18f,.88f); background.raycastTarget = false;
                var icon = new GameObject("Mouse Left Icon", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                icon.transform.SetParent(action.transform,false); icon.texture = sprites.spriteSheet; icon.raycastTarget = false;
                icon.uvRect = new Rect((float)glyph.x / icon.texture.width, (float)glyph.y / icon.texture.height,
                    (float)glyph.width / icon.texture.width, (float)glyph.height / icon.texture.height);
                icon.rectTransform.anchoredPosition = new Vector2(-86,0); icon.rectTransform.sizeDelta = new Vector2(70f * glyph.width / glyph.height,70);
                var text = new GameObject("Action Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
                text.transform.SetParent(action.transform,false); text.rectTransform.anchoredPosition = new Vector2(40,0);
                text.rectTransform.sizeDelta = new Vector2(180,84); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 38; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.raycastTarget = false; text.text = "Sit";
                root.AddComponent<OddOneOutHudBindings>().Configure(canvas,action,icon,text);
                return PrefabUtility.SaveAsPrefabAsset(root,HudPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var split = path.LastIndexOf('/'); var parent = path.Substring(0,split);
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent,path.Substring(split+1));
        }
    }
}

using System;
using System.Linq;
using MazeParty.Multiplayer;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    public static class Race2ProjectSetup
    {
        public const string ScenePath = "Assets/MazeParty/Scenes/Minigames/Race2/Race2.unity";
        public const string Folder = "Assets/MazeParty/Prefabs/Minigames/Race2";
        public const string EnvironmentPath = Folder + "/Race2Environment.prefab";
        public const string BoxPath = Folder + "/Race2Box.prefab";
        public const string BoxSourcePath = "Assets/Ignore/Fantasy Lowpoly Pack (Demo)/Prefabs/box_003.prefab";
        [MenuItem("MazeParty/Minigames/Build Race 2")]
        public static void BuildRace2Assets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            EnsureFolder(Folder); EnsureFolder("Assets/MazeParty/Scenes/Minigames/Race2");
            var previous = SceneManager.GetActiveScene(); var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded && scene.isDirty) throw new InvalidOperationException("Save Race2 before setup.");
            var opened = !scene.IsValid() || !scene.isLoaded; var exists = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;
            if (opened) scene = exists ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive) : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            const string sourcePath = "Assets/MazeParty/Scenes/Minigames/Race/Race.unity";
            var source = SceneManager.GetSceneByPath(sourcePath); var openedSource = !source.IsValid() || !source.isLoaded;
            try
            {
                if (!exists)
                {
                    if (openedSource) source = EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Additive);
                    if (source.isDirty) throw new InvalidOperationException("Save Race before copying its authored map.");
                    var sourceView = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RaceNetworkView>(true)).Single();
                    var data = new SerializedObject(sourceView);
                    var originalArena = (GameObject)data.FindProperty("arenaPresentation").objectReferenceValue;
                    var originalCamera = (CinemachineCamera)data.FindProperty("sharedCamera").objectReferenceValue;
                    SceneManager.SetActiveScene(scene);
                    var environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
                    if (environment == null)
                    {
                        var copy = UnityEngine.Object.Instantiate(originalArena);
                        try
                        {
                            copy.name = "Race 2 Environment";
                            foreach (Transform child in copy.transform.Cast<Transform>().ToArray())
                                if (child.name == "Authority Colliders" || child.name == "Finish Line" || child.name.StartsWith("Start Marker")) UnityEngine.Object.DestroyImmediate(child.gameObject);
                            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                            ApplyDefaultEnvironmentLayout(copy);
                            environment = PrefabUtility.SaveAsPrefabAsset(copy, EnvironmentPath);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(copy); }
                    }
                    var box = AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath);
                    if (box == null)
                    {
                        var copy = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BoxSourcePath));
                        try
                        {
                            copy.name = "Race 2 Box"; copy.transform.localScale = Vector3.one * .65f;
                            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                            box = PrefabUtility.SaveAsPrefabAsset(copy, BoxPath);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(copy); }
                    }
                    var root = new GameObject("Race 2 Network State");
                    var arena = (GameObject)PrefabUtility.InstantiatePrefab(environment, root.transform);
                    arena.name = "Race 2 Environment";
                    var camera = UnityEngine.Object.Instantiate(originalCamera, root.transform); camera.name = "CM_Race2Personal"; camera.Priority = 0;
                    camera.transform.SetPositionAndRotation(Race2NetworkView.CalculatePlayerCameraPosition(0), Race2NetworkView.PlayerCameraRotation);
                    var lens = camera.Lens; lens.ModeOverride = LensSettings.OverrideModes.Perspective;
                    lens.FieldOfView = Race2NetworkView.PlayerCameraFieldOfView; lens.NearClipPlane = .1f; lens.FarClipPlane = 500f;
                    camera.Lens = lens;
                    var players = new GameObject("Runtime Players").transform; players.SetParent(root.transform, false);
                    var boxes = new GameObject("Runtime Boxes").transform; boxes.SetParent(root.transform, false);
                    var common = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OddOneOutProjectSetup.CommonHudPath), scene);
                    EditorSceneManager.SaveScene(scene, ScenePath);
                    root.AddComponent<NetworkObject>(); var state = root.AddComponent<NetworkRace2State>();
                    root.AddComponent<Race2NetworkView>().Configure(state, camera, arena, players, boxes, box,
                        AssetDatabase.LoadAssetAtPath<GameObject>(MinigameVfxProjectSetup.CartoonExplosionPrefabPath),
                        common.GetComponent<MinigameCommonHudView>(), common.GetComponent<MinigameStartCountdownView>());
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
                if (!scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Race2NetworkView>(true)).Single().HasRequiredReferences)
                    throw new InvalidOperationException("Race2 scene bindings incomplete.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (openedSource && source.IsValid() && source.isLoaded) EditorSceneManager.CloseScene(source, true);
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
            EnsurePreviewSlot();
            SoundSystemProjectSetup.EnsureCues();
            SoundSystemProjectSetup.RefreshLibraryMenu();
            AssetDatabase.SaveAssets();
        }
        // Used on initial authoring only. Build never resets an existing environment's design.
        public static void ApplyDefaultEnvironmentLayout(GameObject environment)
        {
            var floor = environment.transform.Find("Race Track");
            var markings = environment.transform.Find("Race Environment");
            var center = NetworkRaceState.TrackCenterX;
            var oldScale = floor.localScale;
            var newScale = new Vector3(600f, oldScale.y, 400f);
            var compensation = new Vector3(oldScale.x / newScale.x, 1f, oldScale.z / newScale.z);
            floor.localScale = newScale;
            // The copied map parents decorations beneath the scaled floor; retain their world sizes.
            var decorations = floor.Cast<Transform>().ToArray();
            for (var i = 0; i < decorations.Length; i++)
            {
                var cactus = decorations[i];
                cactus.localScale = Vector3.Scale(cactus.localScale, compensation);
                var side = i % 2 == 0 ? -1f : 1f;
                cactus.position = new Vector3(center + side * (20f + (i / 2 % 3) * 1.5f), 0f, -55f + i / 2 * 10f);
            }
            for (var i = 1; i <= 3; i++)
            {
                var line = markings.Find("Lane Divider " + i);
                line.position = new Vector3(center + (i - 2) * 8f, .025f, 0f);
                line.localScale = new Vector3(.12f, .05f, 120f);
            }
            var west = markings.Find("West Boundary Visual");
            west.position = new Vector3(center - 16.4f, .75f, 0f);
            west.localScale = new Vector3(.4f, 1.5f, 120.8f);
            var east = markings.Find("East Boundary Visual");
            east.position = new Vector3(center + 16.4f, .75f, 0f);
            east.localScale = west.localScale;
            var south = markings.Find("South Boundary Visual");
            south.position = new Vector3(center, .75f, -60.2f);
            south.localScale = new Vector3(33.2f, 1.5f, .4f);
            var start = markings.Find("Start Line");
            start.gameObject.SetActive(false);
            var laneCenters = environment.transform.Find("Race 2 Lane Centers");
            if (laneCenters == null)
            {
                laneCenters = new GameObject("Race 2 Lane Centers").transform;
                laneCenters.SetParent(environment.transform, false);
                for (var slot = 0; slot < 4; slot++)
                    for (var dash = 0; dash < 40; dash++)
                    {
                        var line = UnityEngine.Object.Instantiate(markings.Find("Lane Divider 1").gameObject, laneCenters).transform;
                        line.name = "P" + (slot + 1) + " Center " + (dash + 1);
                        line.position = new Vector3(center + (slot - 1.5f) * 8f, .025f, -58.5f + dash * 3f);
                        line.localScale = new Vector3(.06f, .05f, 1.4f);
                    }
            }
        }
        private static void EnsurePreviewSlot()
        {
            const string path = "Assets/MazeParty/Prefabs/Board/UI/BoardHudCanvas.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var data = new SerializedObject(root.GetComponent<BoardHudCanvasBindings>());
                var clips = data.FindProperty("minigamePreviewClips");
                var count = BoardHudCanvasBindings.RequiredMinigamePreviewClipSlotCount;
                if (clips.arraySize >= count) return;
                var oldCount = clips.arraySize; clips.arraySize = count;
                for (var i = oldCount; i < count; i++) clips.GetArrayElementAtIndex(i).objectReferenceValue = null;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var split = path.LastIndexOf('/'); var parent = path.Substring(0, split); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
        }
    }
}

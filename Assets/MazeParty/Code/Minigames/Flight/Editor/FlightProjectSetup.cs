using System;
using System.Linq;
using MazeParty.Gameplay.Minigames.Flight;
using MazeParty.Multiplayer;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    public static class FlightProjectSetup
    {
        public const string ScenePath = "Assets/MazeParty/Scenes/Minigames/Flight/Flight.unity";
        public const string Folder = "Assets/MazeParty/Prefabs/Minigames/Flight";
        public const string EnvironmentPath = Folder + "/FlightEnvironment.prefab";
        public const string ObstaclePath = Folder + "/FlightObstacle.prefab";
        public const string ObstacleSourcePath = "Assets/Ignore/FreeLowpolyScifiObjects/Prefabs/Objects/object_012.prefab";
        public const string SkyboxPath = "Assets/Ignore/Fantasy Skybox FREE/Cubemaps/Classic/FS000_Night_01.mat";
        [MenuItem("MazeParty/Minigames/Build Flight")]
        public static void BuildFlightAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            EnsureFolder(Folder); EnsureFolder("Assets/MazeParty/Scenes/Minigames/Flight");
            var previous = SceneManager.GetActiveScene(); var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded && scene.isDirty) throw new InvalidOperationException("Save Flight before setup.");
            var opened = !scene.IsValid() || !scene.isLoaded; var exists = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;
            if (opened) scene = exists ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive) : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                if (!exists)
                {
                    SceneManager.SetActiveScene(scene);
                    var environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
                    if (environment == null)
                    {
                        var copy = new GameObject("Flight Environment");
                        try
                        {
                            var sun = new GameObject("Flight Sun"); sun.transform.SetParent(copy.transform, false);
                            sun.transform.rotation = Quaternion.Euler(40, -30, 0);
                            var light = sun.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f;
                            environment = PrefabUtility.SaveAsPrefabAsset(copy, EnvironmentPath);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(copy); }
                    }
                    var obstacle = AssetDatabase.LoadAssetAtPath<GameObject>(ObstaclePath);
                    if (obstacle == null)
                    {
                        var copy = new GameObject("Flight Obstacle");
                        try
                        {
                            var normalized = new GameObject("Normalized Model").transform; normalized.SetParent(copy.transform, false);
                            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ObstacleSourcePath), normalized);
                            model.transform.localRotation = Quaternion.Euler(0, 90, 0) * Quaternion.Euler(0, 0, 90);
                            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                            var renderers = model.GetComponentsInChildren<Renderer>(true);
                            var bounds = renderers[0].bounds;
                            foreach (var renderer in renderers)
                            {
                                bounds.Encapsulate(renderer.bounds);
                                renderer.sharedMaterials = renderer.sharedMaterials.Select(ToonShaderProjectSetup.Resolve).ToArray();
                            }
                            model.transform.localPosition = -bounds.center + Vector3.up * bounds.size.y / 2;
                            normalized.localScale = new Vector3((float)FlightRules.GateWidth / bounds.size.x, 1 / bounds.size.y, .65f / bounds.size.z);
                            obstacle = PrefabUtility.SaveAsPrefabAsset(copy, ObstaclePath);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(copy); }
                    }
                    var root = new GameObject("Flight Network State");
                    var arena = (GameObject)PrefabUtility.InstantiatePrefab(environment, root.transform);
                    arena.name = "Flight Environment";
                    var cameraRoot = new GameObject("CM_FlightShared"); cameraRoot.transform.SetParent(root.transform, false);
                    cameraRoot.transform.SetPositionAndRotation(FlightNetworkView.CameraPosition, Quaternion.identity);
                    var camera = cameraRoot.AddComponent<CinemachineCamera>(); camera.Priority = 0;
                    var lens = camera.Lens; lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
                    lens.OrthographicSize = (float)FlightRules.ViewHalfHeight; lens.NearClipPlane = .1f; lens.FarClipPlane = 100;
                    camera.Lens = lens;
                    var players = new GameObject("Runtime Players").transform; players.SetParent(root.transform, false);
                    var obstacles = new GameObject("Runtime Obstacles").transform; obstacles.SetParent(root.transform, false);
                    var common = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OddOneOutProjectSetup.CommonHudPath), scene);
                    // NGO needs a saved scene identity before adding the in-scene NetworkObject.
                    EditorSceneManager.SaveScene(scene, ScenePath);
                    root.AddComponent<NetworkObject>(); var state = root.AddComponent<NetworkFlightState>();
                    root.AddComponent<FlightNetworkView>().Configure(state, camera, arena, players, obstacles, obstacle,
                        AssetDatabase.LoadAssetAtPath<GameObject>(MinigameVfxProjectSetup.CartoonExplosionPrefabPath),
                        AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath), common.GetComponent<MinigameCommonHudView>(),
                        common.GetComponent<MinigameStartCountdownView>());
                    MinigameNightLightingProjectSetup.ApplyToScene(scene);
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
                if (!scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FlightNetworkView>(true)).Single().HasRequiredReferences)
                    throw new InvalidOperationException("Flight scene bindings incomplete.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
            EnsurePreviewSlot();
            SoundSystemProjectSetup.EnsureCues(); SoundSystemProjectSetup.RefreshLibraryMenu(); AssetDatabase.SaveAssets();
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
                data.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, path);
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

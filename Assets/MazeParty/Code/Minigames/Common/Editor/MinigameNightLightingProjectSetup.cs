using System;
using System.Linq;
using MazeParty.Gameplay.Minigames;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    public static class MinigameNightLightingProjectSetup
    {
        public const string PrefabPath = "Assets/MazeParty/Prefabs/Minigames/Common/Environment/MinigameNightLighting.prefab";
        public const string LampPath = "Assets/MazeParty/Prefabs/Minigames/Common/Environment/ForestStreetLamp.prefab";
        public const string SkyboxPath = "Assets/Ignore/Fantasy Skybox FREE/Cubemaps/Classic/FS000_Night_01.mat";
        private const string ProfilePath = "Assets/MazeParty/Art/Board/MinigameNightColor.asset";
        private const string ForestLampPath = "Assets/MazeParty/Prefabs/Board/World/ForestStreetLamp.prefab";

        [MenuItem("MazeParty/Minigames/Apply Shared Night Lighting")]
        public static void ApplyAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Edit Mode required.");
            var previous = SceneManager.GetActiveScene();
            foreach (var definition in MinigameCatalog.RegisteredMinigames)
            {
                var path = "Assets/MazeParty/Scenes/Minigames/" + definition.SceneName + "/" + definition.SceneName + ".unity";
                var scene = SceneManager.GetSceneByPath(path);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                    throw new InvalidOperationException("Save scene edits before night lighting setup: " + path);
            }
            try
            {
                foreach (var definition in MinigameCatalog.RegisteredMinigames)
                {
                    var path = "Assets/MazeParty/Scenes/Minigames/" + definition.SceneName + "/" + definition.SceneName + ".unity";
                    var scene = SceneManager.GetSceneByPath(path);
                    var opened = !scene.IsValid() || !scene.isLoaded;
                    if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    try
                    {
                        ApplyToScene(scene);
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }
                    finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
                }
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        public static void ApplyToScene(Scene scene)
        {
            EnsureAssets();
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            try
            {
                var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
                RenderSettings.skybox = sky;
                BoardFeedbackAuthoring.ConfigureNightAmbient();
                var lights = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>(true))
                    .Where(light => light.type == LightType.Directional).ToArray();
                if (lights.Length != 1)
                    throw new InvalidOperationException("Expected one authored directional light: " + scene.path);
                var moon = lights[0];
                var existing = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MinigameNightLighting>(true)).SingleOrDefault();
                if (existing == null)
                {
                    moon.color = new Color(0.57f, 0.69f, 1f);
                    moon.intensity = 0.18f;
                    moon.shadows = LightShadows.Soft;
                    moon.shadowStrength = 0.35f;
                    moon.shadowNormalBias = 0.15f;
                    moon.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
                    moon.lightmapBakeType = LightmapBakeType.Realtime;
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(
                        AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), moon.transform.parent);
                    root.transform.localPosition = Vector3.zero;
                    existing = root.GetComponent<MinigameNightLighting>();
                    existing.Configure(sky, moon, root.GetComponent<Volume>());
                    PrefabUtility.RecordPrefabInstancePropertyModifications(existing);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(moon);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(moon.transform);
                }
                if (!existing.HasRequiredReferences) throw new InvalidOperationException("Incomplete night lighting: " + scene.path);
                RenderSettings.sun = moon;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                        BoardFeedbackAuthoring.ConfigureNightCamera(camera);
                    foreach (var view in root.GetComponentsInChildren<FlightNetworkView>(true))
                    {
                        var data = new SerializedObject(view);
                        data.FindProperty("skyboxMaterial").objectReferenceValue = sky;
                        data.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static void EnsureAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                var root = new GameObject("Minigame Night Lighting");
                try
                {
                    root.layer = LayerMask.NameToLayer("Ignore Raycast");
                    var volume = root.AddComponent<Volume>();
                    volume.isGlobal = true; volume.priority = 10f;
                    volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
                    root.AddComponent<MinigameNightLighting>().Configure(
                        AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath), null, volume);
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(LampPath) == null)
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(ForestLampPath));
                try
                {
                    root.name = "Forest Street Lamp";
                    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    PrefabUtility.SaveAsPrefabAsset(root, LampPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }
    }
}

using System;
using System.Linq;
using MazeParty.Gameplay.Minigames.CoinCollect;
using MazeParty.Multiplayer;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    public static class CoinCollectProjectSetup
    {
        public const string ScenePath = "Assets/MazeParty/Scenes/Minigames/CoinCollect/CoinCollect.unity";
        public const string PrefabFolder = "Assets/MazeParty/Prefabs/Minigames/CoinCollect";
        public const string EnvironmentPath = PrefabFolder + "/CoinCollectEnvironment.prefab";
        public const string ScoreHudPath = "Assets/MazeParty/Prefabs/Minigames/Common/UI/BouncingBallsHud.prefab";

        [MenuItem("MazeParty/Minigames/Build Coin Collect")]
        public static void BuildCoinCollectAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            EnsureFolder(PrefabFolder);
            EnsureFolder("Assets/MazeParty/Scenes/Minigames/CoinCollect");
            EnsureFolder("Assets/MazeParty/Materials/Toon/Converted");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath) == null &&
                !AssetDatabase.CopyAsset(GiftGrabProjectSetup.EnvironmentPrefabPath, EnvironmentPath))
                throw new InvalidOperationException("Gift Grab environment is required.");
            var coin = EnsureItem("Coin"); var money = EnsureItem("Money"); var bomb = EnsureItem("Bomb");
            var shadow = EnsureShadow();
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                throw new InvalidOperationException("Save CoinCollect scene changes before setup.");
            var opened = !scene.IsValid() || !scene.isLoaded;
            var exists = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;
            if (opened) scene = exists ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive) :
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                if (!exists)
                {
                    var root = new GameObject("Coin Collect Network State");
                    var arena = Child("Arena Presentation", root.transform);
                    var environment = (GameObject)PrefabUtility.InstantiatePrefab(
                        AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath), arena.transform);
                    environment.name = "Coin Collect Environment";
                    var floor = Child("Authority Floor", arena.transform);
                    floor.transform.position = new Vector3(CoinCollectRules.ArenaCenterX, -.45f, 0f);
                    floor.AddComponent<BoxCollider>().size = new Vector3(18f, .8f, 18f);
                    var light = Child("Coin Collect Directional Light", arena.transform).AddComponent<Light>();
                    light.type = LightType.Directional; light.intensity = 1.2f;
                    light.color = new Color(1f, .94f, .84f); light.transform.rotation = Quaternion.Euler(52f, -34f, 0f);
                    var camera = Child("CM_CoinCollectShared", root.transform).AddComponent<CinemachineCamera>();
                    camera.transform.SetPositionAndRotation(CoinCollectNetworkView.SharedCameraPosition, CoinCollectNetworkView.SharedCameraRotation);
                    var lens = camera.Lens;
                    lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
                    lens.OrthographicSize = CoinCollectNetworkView.SharedCameraSize; lens.NearClipPlane = .1f; lens.FarClipPlane = 80f;
                    camera.Lens = lens; camera.Priority = 0;
                    var players = Child("Runtime Players", root.transform).transform;
                    var items = Child("Runtime Items", root.transform).transform;
                    var hud = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ScoreHudPath), scene);
                    hud.name = "Coin Collect Shared Score HUD";
                    // Assign a stable scene identity before attaching the in-scene NGO object.
                    EditorSceneManager.SaveScene(scene, ScenePath);
                    root.AddComponent<NetworkObject>();
                    var state = root.AddComponent<NetworkCoinCollectState>();
                    root.AddComponent<CoinCollectNetworkView>().Configure(state, camera, arena, players, items,
                        coin, money, bomb, shadow, hud.GetComponent<BouncingBallsHudBindings>());
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
                var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CoinCollectNetworkView>(true)).Single();
                if (!view.HasRequiredReferences) throw new InvalidOperationException("CoinCollect scene bindings are incomplete.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
            SoundSystemProjectSetup.EnsureCues();
            SoundSystemProjectSetup.RefreshLibraryMenu();
            AssetDatabase.SaveAssets();
        }
        private static GameObject Child(string name, Transform parent)
        {
            var child = new GameObject(name); child.transform.SetParent(parent, false); return child;
        }
        private static GameObject EnsureItem(string name)
        {
            var path = PrefabFolder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Ignore/BTM_Assets/BTM_Items_Gems/Prefabs/" + name + ".prefab");
            if (source == null) throw new InvalidOperationException("Missing BTM " + name);
            var root = new GameObject(name);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(ToonShaderProjectSetup.Resolve).ToArray();
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                var size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                var scale = 1.1f / Mathf.Max(.01f, size);
                model.transform.localScale *= scale;
                model.transform.localPosition -= bounds.center * scale;
                foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(behaviour);
                foreach (var animator in model.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
                foreach (var b in model.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(b);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static GameObject EnsureShadow()
        {
            var path = PrefabFolder + "/LandingShadow.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var materialPath = PrefabFolder + "/LandingShadow.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Landing Shadow" };
                material.SetColor("_BaseColor", new Color(.06f, .04f, .02f, .45f));
                material.SetFloat("_Surface", 1f); material.SetFloat("_SrcBlend", 5f); material.SetFloat("_DstBlend", 10f);
                material.SetFloat("_ZWrite", 0f); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = 3000;
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var root = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            try
            {
                root.name = "Landing Shadow"; root.transform.localScale = new Vector3(1.1f, .005f, 1.1f);
                UnityEngine.Object.DestroyImmediate(root.GetComponent<Collider>());
                var renderer = root.GetComponent<Renderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var split = path.LastIndexOf('/'); var parent = path.Substring(0, split);
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
        }
    }
}

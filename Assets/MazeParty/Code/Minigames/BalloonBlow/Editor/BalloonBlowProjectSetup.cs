using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.BalloonBlow;
using MazeParty.Multiplayer;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    /// <summary>
    /// Builds the additive Balloon Blow arena and its replaceable prototype
    /// presentation. Board continues to own the output Camera and
    /// AudioListener.
    /// </summary>
    public static class BalloonBlowProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Rebuild Balloon Blow";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string ScenesFolder = "Assets/MazeParty/Scenes/Minigames/BalloonBlow";
        private const string CorePrefabFolder =
            ProjectRoot + "/Prefabs/Minigames/BalloonBlow";
        public const string EnvironmentPrefabPath =
            CorePrefabFolder + "/BalloonBlowEnvironment.prefab";
        private const string MaterialFolder =
            ProjectRoot + "/Art/Minigames/BalloonBlow/Materials";
        private const string StableFootingScenePath =
            "Assets/MazeParty/Scenes/Minigames/StableFooting/StableFooting.unity";

        public const string BalloonBlowScenePath =
            "Assets/MazeParty/Scenes/Minigames/BalloonBlow/BalloonBlow.unity";

        private static readonly Vector3[] PlayerPositions =
        {
            new Vector3(-5.25f, BalloonBlowNetworkView.PlayerPresentationHeight, -2.2f),
            new Vector3(-1.75f, BalloonBlowNetworkView.PlayerPresentationHeight, -2.2f),
            new Vector3(1.75f, BalloonBlowNetworkView.PlayerPresentationHeight, -2.2f),
            new Vector3(5.25f, BalloonBlowNetworkView.PlayerPresentationHeight, -2.2f)
        };

        private static readonly Color[] PlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        [MenuItem(MenuPath)]
        public static void RebuildBalloonBlow()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "Balloon Blow rebuild canceled; open scene changes " +
                    "were left untouched.");
                return;
            }

            BuildBalloonBlowAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var scene = SceneManager.GetSceneByPath(BalloonBlowScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.SetActiveScene(scene);
            }
            else
            {
                EditorSceneManager.OpenScene(
                    BalloonBlowScenePath,
                    OpenSceneMode.Single);
            }

            Debug.Log(
                "Balloon Blow rebuilt: four mouth-blown balloons, shared " +
                "camera, and replaceable arena art. Progress is conveyed " +
                "by balloon growth and pop state.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildBalloonBlow()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildBalloonBlowAssets()
        {
            EnsureFolders();
            var materials = CreateMaterials();
            BuildBalloonBlowScene(materials);
            MinigameVfxProjectSetup.InstallScene(BalloonBlowScenePath);
            AssetDatabase.SaveAssets();
        }

        private static void BuildBalloonBlowScene(
            BalloonBlowMaterials materials)
        {
            var previousActive = SceneManager.GetActiveScene();
            var previousActivePath = previousActive.path;
            var loadedScene = SceneManager.GetSceneByPath(
                BalloonBlowScenePath);
            var replaceSingleOpenScene =
                SceneManager.sceneCount == 1 &&
                ((loadedScene.IsValid() && loadedScene.isLoaded) ||
                 string.IsNullOrEmpty(previousActivePath));

            if (loadedScene.IsValid() && loadedScene.isLoaded)
            {
                if (loadedScene.isDirty)
                {
                    throw new InvalidOperationException(
                        "BalloonBlow.unity has unsaved changes. Save or " +
                        "discard them before rebuilding the generated scene.");
                }

                if (!replaceSingleOpenScene)
                {
                    EditorSceneManager.CloseScene(loadedScene, true);
                }
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                replaceSingleOpenScene
                    ? NewSceneMode.Single
                    : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var root = new GameObject("Balloon Blow Network State");
            var arenaPresentation = new GameObject("Arena Presentation");
            arenaPresentation.transform.SetParent(root.transform, false);

            var arena = CreateArena(
                arenaPresentation.transform,
                materials);
            CreateLighting(arenaPresentation.transform);
            var sharedCamera = CreateSharedCamera(root.transform);

            var runtimePlayers = new GameObject("Runtime Players");
            runtimePlayers.transform.SetParent(root.transform, false);

            var audioAnchor = new GameObject("Audio Replacement Anchor");
            audioAnchor.transform.SetParent(root.transform, false);
            audioAnchor.transform.position = new Vector3(0f, 2f, 2.8f);
            var cueAudioSource = audioAnchor.AddComponent<AudioSource>();
            cueAudioSource.playOnAwake = false;
            cueAudioSource.loop = false;
            cueAudioSource.spatialBlend = 0f;
            audioAnchor.AddComponent<MazeParty.Gameplay.AudioChannelSource>().Configure(
                cueAudioSource,
                MazeParty.Gameplay.AudioChannel.Sfx,
                cueAudioSource.volume);

            CreateArtReplacementAnchors(root.transform);

            // Saving before adding NetworkObject gives the in-scene object a
            // stable GlobalObjectIdHash for Netcode scene replication.
            EditorSceneManager.SaveScene(scene, BalloonBlowScenePath);

            root.AddComponent<NetworkObject>();
            var state = root.AddComponent<NetworkBalloonBlowState>();
            var view = root.AddComponent<BalloonBlowNetworkView>();

            view.Configure(
                state,
                sharedCamera,
                runtimePlayers.transform,
                arena.PlayerAnchors,
                arena.BalloonAnchors,
                arenaPresentation,
                cueAudioSource);

            ValidateSceneContract(root);
            EditorSceneManager.SaveScene(scene, BalloonBlowScenePath);
            EnsureBalloonBlowInBuildSettings();

            if (previousActivePath == BalloonBlowScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() &&
                     previousActive.isLoaded &&
                     previousActive.path != BalloonBlowScenePath)
            {
                SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }

        }

        private static ArenaReferences CreateArena(
            Transform parent,
            BalloonBlowMaterials materials)
        {
            CreateAuthorityColliders(parent);
            MinigameCorePrefabUtility.InstantiateOrSeed(
                EnvironmentPrefabPath,
                parent,
                () => CreateEnvironmentTemplate(materials),
                "Balloon Blow Environment");

            var playerRoot = new GameObject("Player Anchors").transform;
            playerRoot.SetParent(parent, false);
            var balloonRoot = new GameObject("Balloon Anchors").transform;
            balloonRoot.SetParent(parent, false);
            var playerAnchors =
                new Transform[BalloonBlowRules.PlayerCount];
            var balloonAnchors =
                new Transform[BalloonBlowRules.PlayerCount];
            for (var slot = 0;
                 slot < BalloonBlowRules.PlayerCount;
                 slot++)
            {
                var playerAnchor = new GameObject(
                    "Player Anchor " + (slot + 1)).transform;
                playerAnchor.SetParent(playerRoot, false);
                playerAnchor.position = PlayerPositions[slot];
                playerAnchor.rotation = Quaternion.identity;
                playerAnchors[slot] = playerAnchor;

                var balloonAnchor = new GameObject(
                    "Balloon Anchor " + (slot + 1)).transform;
                balloonAnchor.SetParent(balloonRoot, false);
                balloonAnchor.position = new Vector3(
                    PlayerPositions[slot].x,
                    1.85f,
                    0.35f);
                CreatePrimitive(
                    "Balloon Body",
                    PrimitiveType.Sphere,
                    balloonAnchor,
                    Vector3.zero,
                    Quaternion.identity,
                    new Vector3(1f, 1.14f, 0.94f),
                    materials.Balloon[slot],
                    false);
                CreatePrimitive(
                    "Balloon Knot",
                    PrimitiveType.Cube,
                    balloonAnchor,
                    new Vector3(0f, -0.58f, 0f),
                    Quaternion.Euler(0f, 0f, 45f),
                    new Vector3(0.18f, 0.18f, 0.14f),
                    materials.Balloon[slot],
                    false);
                balloonAnchor = MinigameCorePrefabUtility.Connect(
                    balloonAnchor.gameObject,
                    CorePrefabFolder + "/Balloon" + (slot + 1) + ".prefab")
                    .transform;
                balloonAnchors[slot] = balloonAnchor;

            }

            return new ArenaReferences
            {
                PlayerAnchors = playerAnchors,
                BalloonAnchors = balloonAnchors
            };
        }

        private static void CreateAuthorityColliders(Transform parent)
        {
            var colliderRoot = new GameObject("Authority Colliders").transform;
            colliderRoot.SetParent(parent, false);
            MinigameCorePrefabUtility.CreateSceneOwnedBoxCollider(
                "Stage Floor",
                colliderRoot,
                new Vector3(0f, -0.45f, 0f),
                Quaternion.identity,
                new Vector3(15.5f, 0.8f, 9f));
        }

        private static GameObject CreateEnvironmentTemplate(
            BalloonBlowMaterials materials)
        {
            var environment = new GameObject("Balloon Blow Environment");
            var parent = environment.transform;
            CreatePrimitive(
                "Stage Floor Visual",
                PrimitiveType.Cube,
                parent,
                new Vector3(0f, -0.45f, 0f),
                Quaternion.identity,
                new Vector3(15.5f, 0.8f, 9f),
                materials.Stage,
                false);
            CreatePrimitive(
                "Backdrop",
                PrimitiveType.Cube,
                parent,
                new Vector3(0f, 2.6f, 4.25f),
                Quaternion.identity,
                new Vector3(15.5f, 6f, 0.35f),
                materials.Backdrop,
                false);
            CreatePrimitive(
                "Front Trim",
                PrimitiveType.Cube,
                parent,
                new Vector3(0f, 0.05f, -4.1f),
                Quaternion.identity,
                new Vector3(15.5f, 0.45f, 0.35f),
                materials.Trim,
                false);
            CreatePrimitive(
                "Rules Plaque",
                PrimitiveType.Cube,
                parent,
                new Vector3(0f, 2.35f, 4.02f),
                Quaternion.identity,
                new Vector3(8.6f, 2.8f, 0.18f),
                materials.Plaque,
                false);
            MinigameCorePrefabUtility.StripColliders(environment);
            return environment;
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject = new GameObject("Balloon Blow Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(48f, -25f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.94f, 0.88f);
        }

        private static CinemachineCamera CreateSharedCamera(Transform parent)
        {
            var cameraObject = new GameObject("CM_BalloonBlowShared");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.SetPositionAndRotation(
                BalloonBlowNetworkView.SharedCameraPosition,
                BalloonBlowNetworkView.SharedCameraRotation);

            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize =
                BalloonBlowNetworkView.SharedCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 100f;
            camera.Lens = lens;
            return camera;
        }

        private static void CreateArtReplacementAnchors(Transform parent)
        {
            var root = new GameObject("Art Replacement Anchors").transform;
            root.SetParent(parent, false);
            new GameObject("Arena Art Anchor").transform.SetParent(root, false);
            new GameObject("Balloon Art Anchor").transform.SetParent(root, false);
            new GameObject("VFX Anchor").transform.SetParent(root, false);
        }

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType primitiveType,
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            Material material,
            bool keepCollider)
        {
            var gameObject = GameObject.CreatePrimitive(primitiveType);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localRotation = localRotation;
            gameObject.transform.localScale = localScale;
            gameObject.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
            {
                var collider = gameObject.GetComponent<Collider>();
                if (collider != null)
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }
            }
            return gameObject;
        }

        private static BalloonBlowMaterials CreateMaterials()
        {
            var balloon = new Material[BalloonBlowRules.PlayerCount];
            for (var slot = 0; slot < BalloonBlowRules.PlayerCount; slot++)
            {
                balloon[slot] = CreateOrLoadMaterial(
                    "BalloonBlowBalloon" + (slot + 1),
                    PlayerColors[slot],
                    PlayerColors[slot] * 0.18f);
            }

            return new BalloonBlowMaterials
            {
                Stage = CreateOrLoadMaterial(
                    "BalloonBlowStage",
                    new Color(0.11f, 0.13f, 0.2f)),
                Backdrop = CreateOrLoadMaterial(
                    "BalloonBlowBackdrop",
                    new Color(0.055f, 0.035f, 0.12f)),
                Trim = CreateOrLoadMaterial(
                    "BalloonBlowTrim",
                    new Color(0.95f, 0.48f, 0.14f),
                    new Color(0.22f, 0.055f, 0.01f)),
                Plaque = CreateOrLoadMaterial(
                    "BalloonBlowPlaque",
                    new Color(0.16f, 0.08f, 0.23f)),
                Balloon = balloon
            };
        }

        private static Material CreateOrLoadMaterial(
            string name,
            Color color,
            Color? emission = null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Universal Render Pipeline/Lit is required for Balloon " +
                    "Blow prototype materials.");
            }

            var path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.35f);
            }
            if (emission.HasValue && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ValidateSceneContract(GameObject root)
        {
            var playerAnchors = FindDescendant(root.transform, "Player Anchors");
            var balloonAnchors = FindDescendant(root.transform, "Balloon Anchors");

            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkBalloonBlowState>() == null ||
                root.GetComponent<BalloonBlowNetworkView>() == null ||
                playerAnchors == null ||
                playerAnchors.childCount != BalloonBlowRules.PlayerCount ||
                balloonAnchors == null ||
                balloonAnchors.childCount != BalloonBlowRules.PlayerCount ||
                root.GetComponentInChildren<CinemachineCamera>(true) == null ||
                root.GetComponentInChildren<AudioSource>(true) == null ||
                FindDescendant(root.transform, "Art Replacement Anchors") == null ||
                root.GetComponentInChildren<Canvas>(true) != null)
            {
                throw new InvalidOperationException(
                    "Generated Balloon Blow scene is missing its network, " +
                    "player, balloon, camera, audio or art contract.");
            }

            if (root.GetComponentInChildren<Camera>(true) != null ||
                root.GetComponentInChildren<AudioListener>(true) != null)
            {
                throw new InvalidOperationException(
                    "Balloon Blow additive scene must reuse Board's output " +
                    "Camera and AudioListener.");
            }
        }

        private static Transform FindDescendant(
            Transform root,
            string childName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == childName)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(root.GetChild(index), childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void EnsureFolders()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(ProjectRoot + "/Art");
            EnsureFolder(ProjectRoot + "/Art/Minigames");
            EnsureFolder(ProjectRoot + "/Art/Minigames/BalloonBlow");
            EnsureFolder(MaterialFolder);
        }

        private static void EnsureBalloonBlowInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            for (var index = scenes.Count - 1; index >= 0; index--)
            {
                if (scenes[index].path == BalloonBlowScenePath)
                {
                    scenes.RemoveAt(index);
                }
            }

            var insertAfter = -1;
            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path == StableFootingScenePath)
                {
                    insertAfter = index;
                    break;
                }
            }
            scenes.Insert(
                insertAfter >= 0 ? insertAfter + 1 : scenes.Count,
                new EditorBuildSettingsScene(BalloonBlowScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            var slash = path.LastIndexOf('/');
            if (slash <= 0)
            {
                throw new InvalidOperationException(
                    "Cannot create project folder '" + path + "'.");
            }
            var parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        private sealed class ArenaReferences
        {
            public Transform[] PlayerAnchors;
            public Transform[] BalloonAnchors;
        }

        private sealed class BalloonBlowMaterials
        {
            public Material Stage;
            public Material Backdrop;
            public Material Trim;
            public Material Plaque;
            public Material[] Balloon;
        }
    }
}

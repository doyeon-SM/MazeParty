using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.Minefield;
using MazeParty.Multiplayer;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    public static class MinefieldProjectSetup
    {
        private const string MenuPath = "MazeParty/Minigames/Rebuild Minefield";
        private const string Root = "Assets/MazeParty";
        private const string ScenesFolder = "Assets/MazeParty/Scenes/Minigames/Minefield";
        private const string UiFolder = "Assets/MazeParty/Prefabs/Minigames/Minefield";
        private const string ArtFolder = Root + "/Art";
        private const string MinigameArtFolder = ArtFolder + "/Minigames";
        private const string MinefieldArtFolder = MinigameArtFolder + "/Minefield";
        private const string MaterialFolder = MinefieldArtFolder + "/Materials";
        private const string BoardScenePath = "Assets/MazeParty/Scenes/Board/Board.unity";
        private const string CrusherPrefabPath =
            Root + "/Prefabs/Minigames/Minefield/Crusher.prefab";
        public const string EnvironmentPrefabPath =
            Root + "/Prefabs/Minigames/Minefield/MinefieldEnvironment.prefab";
        private const string SirenPrefabPath =
            Root + "/Prefabs/Minigames/Minefield/ProximitySiren.prefab";
        private const string SonarPulsePrefabPath =
            Root + "/Prefabs/Minigames/Minefield/SonarPulse.prefab";
        private const string MineMarkerPrefabPath =
            Root + "/Prefabs/Minigames/Minefield/DetectedMine.prefab";
        private const float CrusherStartOffset = 2.5f;

        internal const string MinefieldScenePath =
            "Assets/MazeParty/Scenes/Minigames/Minefield/Minefield.unity";

        [MenuItem(MenuPath)]
        public static void RebuildMinefield()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "Minefield rebuild canceled; open scene changes were left untouched.");
                return;
            }

            BuildMinefieldAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var minefield = SceneManager.GetSceneByPath(MinefieldScenePath);
            if (minefield.IsValid() && minefield.isLoaded)
            {
                SceneManager.SetActiveScene(minefield);
            }
            else
            {
                EditorSceneManager.OpenScene(MinefieldScenePath, OpenSceneMode.Single);
            }
            Debug.Log(
                "Minefield rebuilt: additive-safe top-view arena, network state, " +
                "world presentation, and image-centered board minigame UI.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildMinefield()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildMinefieldAssets()
        {
            EnsureFolders();
            var materials = CreateMaterials();
            var sirenPrefab = LoadOrCreateCorePrefab(
                SirenPrefabPath,
                () => CreateSirenTemplate(materials));
            var sonarPulsePrefab = LoadOrCreateCorePrefab(
                SonarPulsePrefabPath,
                () => CreateSonarPulseTemplate(materials));
            var mineMarkerPrefab = LoadOrCreateCorePrefab(
                MineMarkerPrefabPath,
                () => CreateMineMarkerTemplate(materials));
            BuildMinefieldScene(
                materials,
                sirenPrefab,
                sonarPulsePrefab,
                mineMarkerPrefab);
            AssetDatabase.SaveAssets();
        }

        private static void BuildMinefieldScene(
            MinefieldMaterials materials,
            GameObject sirenPrefab,
            GameObject sonarPulsePrefab,
            GameObject mineMarkerPrefab)
        {
            var previousActive = SceneManager.GetActiveScene();
            var previousActivePath = previousActive.path;
            var scratchScene = default(Scene);
            var loadedMinefield = SceneManager.GetSceneByPath(MinefieldScenePath);
            if (loadedMinefield.IsValid() && loadedMinefield.isLoaded)
            {
                if (loadedMinefield.isDirty)
                {
                    throw new InvalidOperationException(
                        "Minefield.unity has unsaved changes. Save or discard them " +
                        "before rebuilding the generated minigame scene.");
                }

                if (SceneManager.sceneCount == 1)
                {
                    scratchScene = EditorSceneManager.OpenScene(
                        BoardScenePath,
                        OpenSceneMode.Additive);
                    SceneManager.SetActiveScene(scratchScene);
                }

                EditorSceneManager.CloseScene(loadedMinefield, true);
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var root = new GameObject("Minefield Network State");
            var arenaPresentation = new GameObject("Arena Presentation");
            arenaPresentation.transform.SetParent(root.transform, false);
            CreateArena(arenaPresentation.transform, materials);
            CreateLighting(arenaPresentation.transform);
            CreateTopDownCamera(root.transform);

            // Save and enable the scene before adding its NetworkObject so NGO can
            // assign a stable in-scene GlobalObjectIdHash.
            EditorSceneManager.SaveScene(scene, MinefieldScenePath);
            EnsureMinefieldAfterBoardInBuildSettings();

            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkMinefieldState>();
            var networkView = root.AddComponent<MinefieldNetworkView>();
            ConfigureNetworkView(
                networkView,
                sirenPrefab,
                sonarPulsePrefab,
                mineMarkerPrefab);
            ValidateSceneContract(root);
            EditorSceneManager.SaveScene(scene, MinefieldScenePath);

            if (previousActivePath == MinefieldScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() &&
                previousActive.isLoaded &&
                previousActive.path != MinefieldScenePath)
            {
                SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }

            if (scratchScene.IsValid() && scratchScene.isLoaded)
            {
                if (SceneManager.GetActiveScene() == scratchScene)
                {
                    SceneManager.SetActiveScene(scene);
                }
                EditorSceneManager.CloseScene(scratchScene, true);
            }
        }

        private static void CreateArena(
            Transform parent,
            MinefieldMaterials materials)
        {
            var width =
                NetworkMinefieldState.ArenaMaxX -
                NetworkMinefieldState.ArenaMinX;
            CreateAuthorityColliders(parent);
            MinigameCorePrefabUtility.InstantiateOrSeed(
                EnvironmentPrefabPath,
                parent,
                () => CreateEnvironmentTemplate(materials),
                "Minefield Environment");

            var crusher = CreateCube(
                "Crusher Placeholder",
                parent,
                new Vector3(
                    NetworkMinefieldState.ArenaCenterX,
                    1.25f,
                    NetworkMinefieldState.ArenaMinZ - CrusherStartOffset),
                new Vector3(width, 2.5f, 1f),
                materials.Crusher);
            crusher.AddComponent<MinefieldCrusher>();
            crusher = MinigameCorePrefabUtility.Connect(
                crusher,
                CrusherPrefabPath);
            if (crusher.GetComponent<MinefieldCrusher>() == null)
            {
                throw new InvalidOperationException(
                    "Crusher.prefab must retain its MinefieldCrusher component.");
            }
        }

        private static void CreateAuthorityColliders(Transform parent)
        {
            var colliderRoot = new GameObject("Authority Colliders").transform;
            colliderRoot.SetParent(parent, false);
            var centerZ =
                (NetworkMinefieldState.ArenaMinZ +
                 NetworkMinefieldState.ArenaMaxZ) * 0.5f;
            var width =
                NetworkMinefieldState.ArenaMaxX -
                NetworkMinefieldState.ArenaMinX;
            var depth =
                NetworkMinefieldState.ArenaMaxZ -
                NetworkMinefieldState.ArenaMinZ;
            const float wallThickness = 0.5f;
            const float wallHeight = 2.5f;
            MinigameCorePrefabUtility.CreateSceneOwnedBoxCollider(
                "Arena Floor",
                colliderRoot,
                new Vector3(NetworkMinefieldState.ArenaCenterX, -0.1f, centerZ),
                Quaternion.identity,
                new Vector3(width, 0.2f, depth));
            MinigameCorePrefabUtility.CreateSceneOwnedBoxCollider(
                "West Wall",
                colliderRoot,
                new Vector3(
                    NetworkMinefieldState.ArenaMinX - wallThickness * 0.5f,
                    wallHeight * 0.5f,
                    centerZ),
                Quaternion.identity,
                new Vector3(wallThickness, wallHeight, depth));
            MinigameCorePrefabUtility.CreateSceneOwnedBoxCollider(
                "East Wall",
                colliderRoot,
                new Vector3(
                    NetworkMinefieldState.ArenaMaxX + wallThickness * 0.5f,
                    wallHeight * 0.5f,
                    centerZ),
                Quaternion.identity,
                new Vector3(wallThickness, wallHeight, depth));
        }

        private static GameObject CreateEnvironmentTemplate(
            MinefieldMaterials materials)
        {
            var environment = new GameObject("Minefield Environment");
            var parent = environment.transform;
            var centerZ =
                (NetworkMinefieldState.ArenaMinZ +
                 NetworkMinefieldState.ArenaMaxZ) * 0.5f;
            var width =
                NetworkMinefieldState.ArenaMaxX -
                NetworkMinefieldState.ArenaMinX;
            var depth =
                NetworkMinefieldState.ArenaMaxZ -
                NetworkMinefieldState.ArenaMinZ;

            CreateCube(
                "Arena Floor Visual",
                parent,
                new Vector3(NetworkMinefieldState.ArenaCenterX, -0.1f, centerZ),
                new Vector3(width, 0.2f, depth),
                materials.Floor);

            const float wallThickness = 0.5f;
            const float wallHeight = 2.5f;
            CreateCube(
                "West Wall Visual",
                parent,
                new Vector3(
                    NetworkMinefieldState.ArenaMinX - wallThickness * 0.5f,
                    wallHeight * 0.5f,
                    centerZ),
                new Vector3(wallThickness, wallHeight, depth),
                materials.Wall);
            CreateCube(
                "East Wall Visual",
                parent,
                new Vector3(
                    NetworkMinefieldState.ArenaMaxX + wallThickness * 0.5f,
                    wallHeight * 0.5f,
                    centerZ),
                new Vector3(wallThickness, wallHeight, depth),
                materials.Wall);

            CreateGrid(parent, width, depth, materials.Grid);
            CreateLine(
                "Start Line",
                parent,
                NetworkMinefieldState.ArenaMinZ + 0.75f,
                width,
                materials.Start);
            CreateLine(
                "Finish Line",
                parent,
                NetworkMinefieldState.ArenaMaxZ - 0.5f,
                width,
                materials.Finish);
            MinigameCorePrefabUtility.StripColliders(environment);
            return environment;
        }

        private static GameObject LoadOrCreateCorePrefab(
            string path,
            Func<GameObject> createTemplate)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                return prefab;
            }

            var template = createTemplate();
            GameObject connected = null;
            try
            {
                connected = MinigameCorePrefabUtility.Connect(
                    template,
                    path);
            }
            finally
            {
                if (connected != null)
                {
                    UnityEngine.Object.DestroyImmediate(connected);
                }
                else if (template != null)
                {
                    UnityEngine.Object.DestroyImmediate(template);
                }
            }

            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Core prefab could not be loaded: " + path);
            }
            return prefab;
        }

        private static GameObject CreateSirenTemplate(
            MinefieldMaterials materials)
        {
            var siren = new GameObject("Proximity Siren");
            var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseObject.name = "Siren Base";
            baseObject.transform.SetParent(siren.transform, false);
            baseObject.transform.localScale = new Vector3(0.28f, 0.1f, 0.28f);
            baseObject.GetComponent<Renderer>().sharedMaterial =
                materials.SirenBase;
            UnityEngine.Object.DestroyImmediate(baseObject.GetComponent<Collider>());

            var lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lens.name = "Siren Red Lens";
            lens.transform.SetParent(siren.transform, false);
            lens.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            lens.transform.localScale = new Vector3(0.34f, 0.26f, 0.34f);
            lens.GetComponent<Renderer>().sharedMaterial = materials.SirenLens;
            UnityEngine.Object.DestroyImmediate(lens.GetComponent<Collider>());

            var lightObject = new GameObject("Siren Red Light");
            lightObject.transform.SetParent(siren.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.red;
            light.range = 3f;
            light.intensity = 0f;
            light.enabled = false;
            return siren;
        }

        private static GameObject CreateSonarPulseTemplate(
            MinefieldMaterials materials)
        {
            var pulseObject = new GameObject("Sonar Pulse");
            pulseObject.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            var pulse = pulseObject.AddComponent<LineRenderer>();
            pulse.loop = true;
            pulse.useWorldSpace = false;
            pulse.widthMultiplier = 0.09f;
            pulse.positionCount = 65;
            pulse.startColor = new Color(1f, 0.2f, 0.15f, 0.9f);
            pulse.endColor = pulse.startColor;
            pulse.sharedMaterial = materials.SonarPulse;
            for (var point = 0; point < pulse.positionCount; point++)
            {
                var angle = point / 64f * Mathf.PI * 2f;
                pulse.SetPosition(point, new Vector3(
                    Mathf.Cos(angle) * NetworkMinefieldState.SonarRadius,
                    0f,
                    Mathf.Sin(angle) * NetworkMinefieldState.SonarRadius));
            }
            pulse.enabled = false;
            return pulseObject;
        }

        private static GameObject CreateMineMarkerTemplate(
            MinefieldMaterials materials)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Detected Mine";
            marker.transform.localScale = new Vector3(0.7f, 0.14f, 0.7f);
            marker.GetComponent<Renderer>().sharedMaterial =
                materials.MineMarker;
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
            return marker;
        }

        private static void CreateGrid(
            Transform parent,
            float width,
            float depth,
            Material material)
        {
            var cellWidth = width / NetworkMinefieldState.GridWidth;
            var cellDepth = depth / NetworkMinefieldState.GridHeight;
            for (var x = 1; x < NetworkMinefieldState.GridWidth; x++)
            {
                var line = CreateCube(
                    "Grid X " + x,
                    parent,
                    new Vector3(
                        NetworkMinefieldState.ArenaMinX + cellWidth * x,
                        0.015f,
                        0f),
                    new Vector3(0.045f, 0.03f, depth),
                    material);
                UnityEngine.Object.DestroyImmediate(line.GetComponent<Collider>());
            }

            for (var z = 1; z < NetworkMinefieldState.GridHeight; z++)
            {
                var line = CreateCube(
                    "Grid Z " + z,
                    parent,
                    new Vector3(
                        NetworkMinefieldState.ArenaCenterX,
                        0.015f,
                        NetworkMinefieldState.ArenaMinZ + cellDepth * z),
                    new Vector3(width, 0.03f, 0.045f),
                    material);
                UnityEngine.Object.DestroyImmediate(line.GetComponent<Collider>());
            }
        }

        private static void CreateLine(
            string name,
            Transform parent,
            float worldZ,
            float arenaWidth,
            Material material)
        {
            var line = CreateCube(
                name,
                parent,
                new Vector3(NetworkMinefieldState.ArenaCenterX, 0.025f, worldZ),
                new Vector3(arenaWidth, 0.05f, 0.35f),
                material);
            var collider = line.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static GameObject CreateCube(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.SetPositionAndRotation(position, Quaternion.identity);
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        private static MinefieldMaterials CreateMaterials()
        {
            return new MinefieldMaterials
            {
                Floor = CreateOrUpdateMaterial(
                    "MinefieldFloor",
                    new Color(0.075f, 0.11f, 0.15f)),
                Grid = CreateOrUpdateMaterial(
                    "MinefieldGrid",
                    new Color(0.18f, 0.28f, 0.34f)),
                Wall = CreateOrUpdateMaterial(
                    "MinefieldWall",
                    new Color(0.12f, 0.19f, 0.25f)),
                Start = CreateOrUpdateMaterial(
                    "MinefieldStart",
                    new Color(0.18f, 0.86f, 0.42f)),
                Finish = CreateOrUpdateMaterial(
                    "MinefieldFinish",
                    new Color(1f, 0.74f, 0.12f)),
                Crusher = CreateOrUpdateMaterial(
                    "MinefieldCrusherPlaceholder",
                    new Color(0.82f, 0.08f, 0.075f)),
                SirenBase = CreateOrUpdateMaterial(
                    "MinefieldSirenBase",
                    new Color(0.12f, 0.12f, 0.14f)),
                SirenLens = CreateOrUpdateMaterial(
                    "MinefieldSirenLens",
                    new Color(1f, 0.12f, 0.08f)),
                SonarPulse = CreateOrUpdateMaterial(
                    "MinefieldSonarPulse",
                    new Color(1f, 0.2f, 0.15f)),
                MineMarker = CreateOrUpdateMaterial(
                    "MinefieldDetectedMine",
                    new Color(1f, 0.12f, 0.06f))
            };
        }

        private static Material CreateOrUpdateMaterial(string name, Color color)
        {
            var path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                         Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "No supported Lit shader is available for Minefield.");
            }

            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);

            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.2f);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject = new GameObject("Minefield Directional Light");
            lightObject.transform.SetParent(parent);
            lightObject.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
        }

        private static void CreateTopDownCamera(Transform parent)
        {
            var cameraObject = new GameObject("CM_MinefieldTopDown");
            cameraObject.transform.SetParent(parent);
            cameraObject.transform.SetPositionAndRotation(
                MinefieldNetworkView.CalculatePlayerCameraPosition(
                    new Vector3(NetworkMinefieldState.ArenaCenterX, 0f, 0f)),
                MinefieldNetworkView.PlayerCameraRotation);

            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 200;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize =
                MinefieldNetworkView.PlayerCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 100f;
            camera.Lens = lens;
        }

        private static void ConfigureNetworkView(
            MinefieldNetworkView view,
            GameObject sirenPrefab,
            GameObject sonarPulsePrefab,
            GameObject mineMarkerPrefab)
        {
            var serializedView = new SerializedObject(view);
            var sirenProperty = serializedView.FindProperty("sirenPrefab");
            var sonarProperty = serializedView.FindProperty("sonarPulsePrefab");
            var mineProperty = serializedView.FindProperty("mineMarkerPrefab");
            if (sirenProperty == null || sonarProperty == null ||
                mineProperty == null)
            {
                throw new InvalidOperationException(
                    "MinefieldNetworkView no longer exposes its core prefab " +
                    "bindings.");
            }
            sirenProperty.objectReferenceValue = sirenPrefab;
            sonarProperty.objectReferenceValue = sonarPulsePrefab;
            mineProperty.objectReferenceValue = mineMarkerPrefab;
            serializedView.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ValidateSceneContract(GameObject root)
        {
            var networkView = root.GetComponent<MinefieldNetworkView>();
            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkMinefieldState>() == null ||
                networkView == null ||
                FindDescendant(root.transform, "Arena Presentation") == null ||
                root.GetComponentInChildren<CinemachineCamera>(true) == null ||
                FindDescendant(root.transform, "Crusher Placeholder") == null ||
                root.GetComponentInChildren<Canvas>(true) != null)
            {
                throw new InvalidOperationException(
                    "Generated Minefield scene is missing its network or presentation contract.");
            }
            var serializedView = new SerializedObject(networkView);
            if (AssetDatabase.GetAssetPath(
                    serializedView.FindProperty("sirenPrefab")?.objectReferenceValue) !=
                    SirenPrefabPath ||
                AssetDatabase.GetAssetPath(
                    serializedView.FindProperty("sonarPulsePrefab")?.objectReferenceValue) !=
                    SonarPulsePrefabPath ||
                AssetDatabase.GetAssetPath(
                    serializedView.FindProperty("mineMarkerPrefab")?.objectReferenceValue) !=
                    MineMarkerPrefabPath)
            {
                throw new InvalidOperationException(
                    "Minefield scene must bind its authored core prefabs.");
            }

            if (root.GetComponentInChildren<Camera>(true) != null ||
                root.GetComponentInChildren<AudioListener>(true) != null)
            {
                throw new InvalidOperationException(
                    "Minefield must not contain a Unity Camera or AudioListener; " +
                    "the additive Board scene owns the output camera.");
            }
        }

        private static GameObject FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root.gameObject;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var result = FindDescendant(root.GetChild(index), name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static void EnsureMinefieldAfterBoardInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            for (var index = scenes.Count - 1; index >= 0; index--)
            {
                if (scenes[index].path == MinefieldScenePath)
                {
                    scenes.RemoveAt(index);
                }
            }

            var boardIndex = -1;
            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path != BoardScenePath)
                {
                    continue;
                }

                scenes[index] = new EditorBuildSettingsScene(BoardScenePath, true);
                boardIndex = index;
                break;
            }

            if (boardIndex < 0)
            {
                scenes.Add(new EditorBuildSettingsScene(BoardScenePath, true));
                boardIndex = scenes.Count - 1;
            }

            scenes.Insert(
                boardIndex + 1,
                new EditorBuildSettingsScene(MinefieldScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolders()
        {
            EnsureFolder(Root);
            EnsureFolder(ScenesFolder);
            EnsureFolder(UiFolder);
            EnsureFolder(ArtFolder);
            EnsureFolder(MinigameArtFolder);
            EnsureFolder(MinefieldArtFolder);
            EnsureFolder(MaterialFolder);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var separator = path.LastIndexOf('/');
            if (separator <= 0)
            {
                throw new InvalidOperationException(
                    "Cannot create project folder '" + path + "'.");
            }

            var parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }

        private sealed class MinefieldMaterials
        {
            public Material Floor;
            public Material Grid;
            public Material Wall;
            public Material Start;
            public Material Finish;
            public Material Crusher;
            public Material SirenBase;
            public Material SirenLens;
            public Material SonarPulse;
            public Material MineMarker;
        }
    }
}

using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.WrongWay;
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
    /// Generates the additive WrongWay race scene and its lightweight art.
    /// Existing build scenes are preserved; WrongWay is inserted immediately
    /// after Minefield (or Board when Minefield is not registered).
    /// </summary>
    public static class WrongWayProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Rebuild WrongWay";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string ScenesFolder =
            ProjectRoot + "/Scenes";
        private const string ArtFolder =
            ProjectRoot + "/Art";
        private const string MinigameArtFolder =
            ArtFolder + "/Minigames";
        private const string WrongWayArtFolder =
            MinigameArtFolder + "/WrongWay";
        private const string MaterialFolder =
            WrongWayArtFolder + "/Materials";
        private const string BoardScenePath =
            ScenesFolder + "/Board.unity";
        private const string MinefieldScenePath =
            ScenesFolder + "/Minefield.unity";

        public const string WrongWayScenePath =
            "Assets/MazeParty/Scenes/WrongWay.unity";

        [MenuItem(MenuPath)]
        public static void RebuildWrongWay()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "WrongWay rebuild canceled; open scene changes were left untouched.");
                return;
            }

            BuildWrongWayAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var wrongWay =
                SceneManager.GetSceneByPath(WrongWayScenePath);
            if (wrongWay.IsValid() && wrongWay.isLoaded)
            {
                SceneManager.SetActiveScene(wrongWay);
            }
            else
            {
                EditorSceneManager.OpenScene(
                    WrongWayScenePath,
                    OpenSceneMode.Single);
            }

            Debug.Log(
                "WrongWay rebuilt: four 50-step lanes, network state, " +
                "lead-follow race camera and additive-safe presentation.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildWrongWay()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildWrongWayAssets()
        {
            EnsureFolders();
            BuildWrongWayScene(CreateMaterials());
            AssetDatabase.SaveAssets();
        }

        private static void BuildWrongWayScene(
            WrongWayMaterials materials)
        {
            var previousActive = SceneManager.GetActiveScene();
            var previousActivePath = previousActive.path;
            var scratchScene = default(Scene);
            var loadedWrongWay =
                SceneManager.GetSceneByPath(WrongWayScenePath);

            if (loadedWrongWay.IsValid() && loadedWrongWay.isLoaded)
            {
                if (loadedWrongWay.isDirty)
                {
                    throw new InvalidOperationException(
                        "WrongWay.unity has unsaved changes. Save or discard " +
                        "them before rebuilding the generated scene.");
                }

                if (SceneManager.sceneCount == 1)
                {
                    scratchScene = EditorSceneManager.OpenScene(
                        BoardScenePath,
                        OpenSceneMode.Additive);
                    SceneManager.SetActiveScene(scratchScene);
                }

                EditorSceneManager.CloseScene(loadedWrongWay, true);
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var root = new GameObject("WrongWay Network State");
            var arenaPresentation =
                new GameObject("Arena Presentation");
            arenaPresentation.transform.SetParent(
                root.transform,
                false);

            CreateArena(arenaPresentation.transform, materials);
            CreateLighting(arenaPresentation.transform);
            CreateRaceCamera(root.transform);

            // NGO only assigns a stable in-scene hash after the scene is saved
            // and registered as an enabled build scene.
            EditorSceneManager.SaveScene(
                scene,
                WrongWayScenePath);
            EnsureWrongWayInBuildSettings();

            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkWrongWayState>();
            root.AddComponent<WrongWayNetworkView>();

            ValidateSceneContract(root);
            EditorSceneManager.SaveScene(
                scene,
                WrongWayScenePath);

            if (previousActivePath == WrongWayScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() &&
                     previousActive.isLoaded &&
                     previousActive.path != WrongWayScenePath)
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
            WrongWayMaterials materials)
        {
            var stairArena = new GameObject("Stair Arena");
            stairArena.transform.SetParent(parent, false);

            var outerWidth =
                NetworkWrongWayState.LaneSpacing *
                (WrongWayRules.PlayerCount - 1) +
                NetworkWrongWayState.StepWidth +
                4f;
            var groundDepth =
                WrongWayNetworkView.StartPlatformDepth +
                WrongWayNetworkView.CourseLength +
                WrongWayNetworkView.FinishPlatformDepth +
                5f;
            var groundCenterZ =
                (WrongWayNetworkView.CourseLength -
                 WrongWayNetworkView.StartPlatformDepth +
                 WrongWayNetworkView.FinishPlatformDepth) * 0.5f;

            var ground = CreateCube(
                "Backdrop Floor",
                stairArena.transform,
                new Vector3(
                    NetworkWrongWayState.ArenaCenterX,
                    -0.35f,
                    groundCenterZ),
                new Vector3(outerWidth, 0.5f, groundDepth),
                Quaternion.identity,
                materials.Ground);
            RemoveCollider(ground);

            for (var slot = 0;
                 slot < WrongWayRules.PlayerCount;
                 slot++)
            {
                CreateLane(
                    stairArena.transform,
                    slot,
                    materials.Lanes[slot],
                    materials.Finish);
            }

            CreateLaneRails(
                stairArena.transform,
                materials.Rail);
            CreateFinishArch(
                stairArena.transform,
                outerWidth - 2f,
                materials.Finish);
        }

        private static void CreateLane(
            Transform parent,
            int slot,
            Material laneMaterial,
            Material finishMaterial)
        {
            var lane = new GameObject("Lane " + (slot + 1));
            lane.transform.SetParent(parent, false);
            var laneX = WrongWayNetworkView.GetLaneX(slot);

            var startPlatform = CreateCube(
                "Start Platform",
                lane.transform,
                new Vector3(
                    laneX,
                    -0.1f,
                    -WrongWayNetworkView.StartPlatformDepth * 0.5f),
                new Vector3(
                    NetworkWrongWayState.StepWidth,
                    0.2f,
                    WrongWayNetworkView.StartPlatformDepth),
                Quaternion.identity,
                laneMaterial);
            RemoveCollider(startPlatform);

            for (var step = 1;
                 step <= WrongWayRules.StepCount;
                 step++)
            {
                var topHeight =
                    step * NetworkWrongWayState.StepHeight;
                var tread = CreateCube(
                    "Step " + step.ToString("00"),
                    lane.transform,
                    new Vector3(
                        laneX,
                        topHeight * 0.5f,
                        (step - 0.5f) *
                        NetworkWrongWayState.StepDepth),
                    new Vector3(
                        NetworkWrongWayState.StepWidth,
                        topHeight,
                        NetworkWrongWayState.StepDepth - 0.025f),
                    Quaternion.identity,
                    laneMaterial);
                RemoveCollider(tread);
            }

            var finishPlatform = CreateCube(
                "Finish Platform",
                lane.transform,
                new Vector3(
                    laneX,
                    WrongWayNetworkView.CourseHeight * 0.5f,
                    WrongWayNetworkView.CourseLength +
                    WrongWayNetworkView.FinishPlatformDepth * 0.5f),
                new Vector3(
                    NetworkWrongWayState.StepWidth,
                    WrongWayNetworkView.CourseHeight,
                    WrongWayNetworkView.FinishPlatformDepth),
                Quaternion.identity,
                finishMaterial);
            RemoveCollider(finishPlatform);
        }

        private static void CreateLaneRails(
            Transform parent,
            Material railMaterial)
        {
            var slopeDegrees = Mathf.Atan2(
                WrongWayNetworkView.CourseHeight,
                WrongWayNetworkView.CourseLength) * Mathf.Rad2Deg;
            var railLength = Mathf.Sqrt(
                WrongWayNetworkView.CourseLength *
                WrongWayNetworkView.CourseLength +
                WrongWayNetworkView.CourseHeight *
                WrongWayNetworkView.CourseHeight);

            for (var boundary = 0;
                 boundary <= WrongWayRules.PlayerCount;
                 boundary++)
            {
                var leftEdge =
                    WrongWayNetworkView.GetLaneX(0) -
                    NetworkWrongWayState.StepWidth * 0.5f;
                var x = leftEdge +
                        boundary * NetworkWrongWayState.LaneSpacing;
                var rail = CreateCube(
                    "Lane Rail " + (boundary + 1),
                    parent,
                    new Vector3(
                        x,
                        WrongWayNetworkView.CourseHeight * 0.5f + 0.42f,
                        WrongWayNetworkView.CourseLength * 0.5f),
                    new Vector3(0.085f, 0.12f, railLength),
                    Quaternion.Euler(-slopeDegrees, 0f, 0f),
                    railMaterial);
                RemoveCollider(rail);
            }
        }

        private static void CreateFinishArch(
            Transform parent,
            float width,
            Material material)
        {
            var finishRoot = new GameObject("Finish Arch");
            finishRoot.transform.SetParent(parent, false);
            var finishZ =
                WrongWayNetworkView.CourseLength +
                WrongWayNetworkView.FinishPlatformDepth * 0.65f;
            var finishTop =
                WrongWayNetworkView.CourseHeight + 3.2f;
            var halfWidth = width * 0.5f;

            var left = CreateCube(
                "Finish Arch Left",
                finishRoot.transform,
                new Vector3(
                    NetworkWrongWayState.ArenaCenterX - halfWidth,
                    WrongWayNetworkView.CourseHeight + 1.55f,
                    finishZ),
                new Vector3(0.35f, 3.1f, 0.35f),
                Quaternion.identity,
                material);
            var right = CreateCube(
                "Finish Arch Right",
                finishRoot.transform,
                new Vector3(
                    NetworkWrongWayState.ArenaCenterX + halfWidth,
                    WrongWayNetworkView.CourseHeight + 1.55f,
                    finishZ),
                new Vector3(0.35f, 3.1f, 0.35f),
                Quaternion.identity,
                material);
            var header = CreateCube(
                "Finish Arch Header",
                finishRoot.transform,
                new Vector3(
                    NetworkWrongWayState.ArenaCenterX,
                    finishTop,
                    finishZ),
                new Vector3(width + 0.35f, 0.65f, 0.45f),
                Quaternion.identity,
                material);
            RemoveCollider(left);
            RemoveCollider(right);
            RemoveCollider(header);
        }

        private static WrongWayMaterials CreateMaterials()
        {
            return new WrongWayMaterials
            {
                Ground = CreateOrUpdateMaterial(
                    "WrongWayGround",
                    new Color(0.035f, 0.045f, 0.075f)),
                Rail = CreateOrUpdateMaterial(
                    "WrongWayRail",
                    new Color(0.78f, 0.84f, 0.92f)),
                Finish = CreateOrUpdateMaterial(
                    "WrongWayFinish",
                    new Color(1f, 0.72f, 0.08f)),
                Lanes = new[]
                {
                    CreateOrUpdateMaterial(
                        "WrongWayLaneBlue",
                        new Color(0.1f, 0.38f, 0.75f)),
                    CreateOrUpdateMaterial(
                        "WrongWayLaneRed",
                        new Color(0.78f, 0.16f, 0.12f)),
                    CreateOrUpdateMaterial(
                        "WrongWayLaneGreen",
                        new Color(0.12f, 0.58f, 0.24f)),
                    CreateOrUpdateMaterial(
                        "WrongWayLanePurple",
                        new Color(0.48f, 0.2f, 0.72f))
                }
            };
        }

        private static Material CreateOrUpdateMaterial(
            string name,
            Color color)
        {
            var path = MaterialFolder + "/" + name + ".mat";
            var material =
                AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader =
                    Shader.Find("Universal Render Pipeline/Lit") ??
                    Shader.Find("Standard");
                if (shader == null)
                {
                    throw new InvalidOperationException(
                        "No supported Lit shader is available for WrongWay.");
                }

                material = new Material(shader)
                {
                    name = name
                };
                AssetDatabase.CreateAsset(material, path);
            }

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
                material.SetFloat("_Smoothness", 0.18f);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject =
                new GameObject("WrongWay Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation =
                Quaternion.Euler(48f, -32f, 0f);

            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.95f, 0.88f);
        }

        private static void CreateRaceCamera(Transform parent)
        {
            var cameraObject =
                new GameObject("CM_WrongWayRace");
            cameraObject.transform.SetParent(parent, false);

            var focus =
                WrongWayNetworkView.CalculateCameraFocus(0);
            cameraObject.transform.SetPositionAndRotation(
                WrongWayNetworkView.CalculateCameraPosition(focus),
                WrongWayNetworkView.CalculateCameraRotation(focus));

            var camera =
                cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride =
                LensSettings.OverrideModes.Perspective;
            lens.FieldOfView =
                WrongWayNetworkView.CameraFieldOfView;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 150f;
            camera.Lens = lens;
        }

        private static GameObject CreateCube(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Quaternion rotation,
            Material material)
        {
            var cube =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.SetPositionAndRotation(
                position,
                rotation);
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        private static void RemoveCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static void ValidateSceneContract(GameObject root)
        {
            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkWrongWayState>() == null ||
                root.GetComponent<WrongWayNetworkView>() == null ||
                FindDescendant(
                    root.transform,
                    "Arena Presentation") == null ||
                FindDescendant(
                    root.transform,
                    "Stair Arena") == null ||
                root.GetComponentInChildren<CinemachineCamera>(
                    true) == null)
            {
                throw new InvalidOperationException(
                    "Generated WrongWay scene is missing its network or " +
                    "presentation contract.");
            }

            for (var slot = 0;
                 slot < WrongWayRules.PlayerCount;
                 slot++)
            {
                var lane = FindDescendant(
                    root.transform,
                    "Lane " + (slot + 1));
                if (lane == null)
                {
                    throw new InvalidOperationException(
                        "Generated WrongWay scene is missing lane " +
                        (slot + 1) +
                        ".");
                }

                var stepCount = 0;
                for (var child = 0;
                     child < lane.transform.childCount;
                     child++)
                {
                    if (lane.transform.GetChild(child).name
                        .StartsWith(
                            "Step ",
                            StringComparison.Ordinal))
                    {
                        stepCount++;
                    }
                }

                if (stepCount != WrongWayRules.StepCount)
                {
                    throw new InvalidOperationException(
                        "Every WrongWay lane must contain exactly 50 steps.");
                }
            }

            if (root.GetComponentInChildren<Camera>(true) != null ||
                root.GetComponentInChildren<AudioListener>(true) != null)
            {
                throw new InvalidOperationException(
                    "WrongWay must not contain a Unity Camera or AudioListener; " +
                    "the additive Board scene owns the output camera.");
            }

            if (FindDescendant(root.transform, "Crusher") != null)
            {
                throw new InvalidOperationException(
                    "WrongWay must not include a crusher or death device.");
            }
        }

        private static GameObject FindDescendant(
            Transform root,
            string childName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == childName)
            {
                return root.gameObject;
            }

            for (var index = 0;
                 index < root.childCount;
                 index++)
            {
                var result = FindDescendant(
                    root.GetChild(index),
                    childName);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static void EnsureWrongWayInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            for (var index = scenes.Count - 1;
                 index >= 0;
                 index--)
            {
                if (scenes[index].path == WrongWayScenePath)
                {
                    scenes.RemoveAt(index);
                }
            }

            var insertAfter = FindSceneIndex(
                scenes,
                MinefieldScenePath);
            if (insertAfter < 0)
            {
                insertAfter = FindSceneIndex(
                    scenes,
                    BoardScenePath);
            }

            var targetIndex = insertAfter >= 0
                ? insertAfter + 1
                : scenes.Count;
            scenes.Insert(
                targetIndex,
                new EditorBuildSettingsScene(
                    WrongWayScenePath,
                    true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static int FindSceneIndex(
            IReadOnlyList<EditorBuildSettingsScene> scenes,
            string path)
        {
            for (var index = 0;
                 index < scenes.Count;
                 index++)
            {
                if (scenes[index].path == path)
                {
                    return index;
                }
            }

            return -1;
        }

        private static void EnsureFolders()
        {
            EnsureFolder(ProjectRoot);
            EnsureFolder(ScenesFolder);
            EnsureFolder(ArtFolder);
            EnsureFolder(MinigameArtFolder);
            EnsureFolder(WrongWayArtFolder);
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
            AssetDatabase.CreateFolder(
                parent,
                path.Substring(separator + 1));
        }

        private sealed class WrongWayMaterials
        {
            public Material Ground;
            public Material Rail;
            public Material Finish;
            public Material[] Lanes;
        }
    }
}

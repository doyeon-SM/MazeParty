using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.BoardFlowTestbed;
using MazeParty.Multiplayer;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardFlowProjectSetup
    {
        private const string Root = "Assets/MazeParty";
        private const string BoardFolder = Root + "/Board";
        private const string MaterialFolder = BoardFolder + "/Materials";
        private const string BoardPath = Root + "/Scenes/Board.unity";

        private const string DiceArtFolder = Root + "/Art/Dice";
        private const string D12ArtFolder = DiceArtFolder + "/D12";
        private const string D12ModelFolder = D12ArtFolder + "/Models";
        private const string D12TextureFolder = D12ArtFolder + "/Textures";
        private const string D12MaterialFolder = D12ArtFolder + "/Materials";
        private const string D12PrefabFolder = D12ArtFolder + "/Prefabs";
        private const string D12ModelPath = D12ModelFolder + "/Dice_d12.fbx";
        private const string D12AlbedoPath =
            D12TextureFolder + "/D12_White_Albedo.png";
        private const string D12NormalPath =
            D12TextureFolder + "/D12_White_Normal.png";
        private const string D12OcclusionPath = D12TextureFolder + "/D12_AO.png";
        private const string D12MaterialPath =
            D12MaterialFolder + "/D12Tintable.mat";
        private const string D12VisualPrefabPath =
            D12PrefabFolder + "/D12WorldDieVisual.prefab";
        private const float D12VisualScale = 0.3f;

        private static readonly Color[] D12PlayerColors =
        {
            new Color(0.95f, 0.25f, 0.25f),
            new Color(0.25f, 0.55f, 1f),
            new Color(0.25f, 0.85f, 0.4f),
            new Color(1f, 0.75f, 0.2f)
        };

        private const string UiFolder = Root + "/UI";
        private const string UiPrefabFolder = UiFolder + "/Prefabs";
        internal const string BoardCanvasPrefabPath =
            UiPrefabFolder + "/BoardCanvas.prefab";
        private const string TestbedPath =
            Root + "/Dev/BoardFlowTestbed/BoardFlowTestbed.unity";
        private const float RoomSize = BoardTile.RoomSize;

        private static readonly Vector2Int[] MainLoop =
        {
            new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(3, 1),
            new Vector2Int(4, 1), new Vector2Int(5, 1), new Vector2Int(5, 2),
            new Vector2Int(5, 3), new Vector2Int(5, 4), new Vector2Int(5, 5),
            new Vector2Int(4, 5), new Vector2Int(3, 5), new Vector2Int(2, 5),
            new Vector2Int(1, 5), new Vector2Int(1, 4), new Vector2Int(1, 3),
            new Vector2Int(1, 2)
        };

        private static readonly Vector2Int[][] Branches =
        {
            new[]
            {
                new Vector2Int(1, 1), new Vector2Int(1, 0), new Vector2Int(2, 0),
                new Vector2Int(3, 0), new Vector2Int(4, 0), new Vector2Int(4, 1)
            },
            new[]
            {
                new Vector2Int(5, 1), new Vector2Int(6, 1), new Vector2Int(6, 2),
                new Vector2Int(6, 3), new Vector2Int(6, 4), new Vector2Int(5, 4)
            },
            new[]
            {
                new Vector2Int(5, 5), new Vector2Int(5, 6), new Vector2Int(4, 6),
                new Vector2Int(3, 6), new Vector2Int(2, 6), new Vector2Int(2, 5)
            },
            new[]
            {
                new Vector2Int(1, 5), new Vector2Int(0, 5), new Vector2Int(0, 4),
                new Vector2Int(0, 3), new Vector2Int(0, 2), new Vector2Int(1, 2)
            }
        };

        [MenuItem("MazeParty/Gameplay/Rebuild Board Flow Prototype")]
        public static void RebuildBoardFlowPrototype()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("Board flow prototype rebuild canceled; open scene changes were left untouched.");
                return;
            }

            BuildBoardSceneBase();
            BuildLocalTestbedFromBoard();
            EnsureBoardInBuildSettings();
            AddNetworkStateAndSave();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(BoardPath, OpenSceneMode.Single);
            Debug.Log(
                "Board flow prototype rebuilt: 32 rooms, directed branches, Canvas HUD, " +
                "perspective staged cameras, and the network match state.");
        }

        [MenuItem("MazeParty/Gameplay/Rebuild Board Flow Prototype", true)]
        private static bool CanRebuildBoardFlowPrototype()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem("MazeParty/UI/Open Board Canvas Prefab")]
        public static void OpenBoardCanvasPrefab()
        {
            EnsureFolders();
            var prefab = LoadOrCreateBoardCanvasPrefab();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            AssetDatabase.OpenAsset(prefab);
        }

        [MenuItem("MazeParty/Gameplay/Rebuild D12 Dice Assets")]
        public static void RebuildD12DiceAssets()
        {
            EnsureFolders();
            EnsureD12RuntimeAssets();
            CreateOrUpdateD12PlayerMaterials(D12PlayerColors);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "D12 dice assets rebuilt with the tracked 512px texture set, " +
                "URP tint material, convex collider and numbered face mapping.");
        }

        [MenuItem("MazeParty/Gameplay/Rebuild D12 Dice Assets", true)]
        private static bool CanRebuildD12DiceAssets()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        internal static void BuildBoardSceneBase()
        {
            EnsureFolders();
            var previousActive = SceneManager.GetActiveScene();
            var loadedBoard = SceneManager.GetSceneByPath(BoardPath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            if (loadedBoard.IsValid() && loadedBoard.isLoaded)
            {
                if (loadedBoard.isDirty)
                {
                    EditorSceneManager.CloseScene(scene, true);
                    throw new InvalidOperationException(
                        "Board.unity has unsaved changes. Save or discard them before rebuilding the generated board.");
                }
                EditorSceneManager.CloseScene(loadedBoard, true);
            }

            var materials = CreateMaterials();
            CreateLighting();
            CreateBoardBackdrop(materials.Backdrop);
            var topology = CreateTopology(materials);
            var cameras = CreateCameraRig();
            CreateBoardCanvas(cameras);

            var validation = topology.ValidateTopology();
            if (!validation.IsValid)
            {
                var messages = new List<string>();
                for (var i = 0; i < validation.Issues.Count; i++)
                {
                    messages.Add(validation.Issues[i].Code + ": " + validation.Issues[i].Message);
                }
                throw new InvalidOperationException("Generated board topology is invalid:\n" + string.Join("\n", messages));
            }

            EditorSceneManager.SaveScene(scene, BoardPath);
            if (previousActive.IsValid() && previousActive.isLoaded && previousActive.path != BoardPath)
            {
                SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        internal static void AddNetworkStateAndSave()
        {
            var scene = EditorSceneManager.OpenScene(BoardPath, OpenSceneMode.Single);
            var existing = GameObject.Find("Network Match State");
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing);
            }

            // NGO assigns a stable in-scene object hash because Board is already an
            // enabled build scene when this method runs.
            var matchState = new GameObject("Network Match State");
            matchState.AddComponent<NetworkObject>();
            matchState.AddComponent<KeyShopRuntimeState>();
            matchState.AddComponent<NetworkMatchState>();
            var topology = UnityEngine.Object.FindAnyObjectByType<BoardTopology>();
            if (topology == null)
            {
                throw new InvalidOperationException("Board scene is missing its topology.");
            }

            var dice = CreateNetworkWorldDice();
            var diceCoordinator = matchState.AddComponent<NetworkWorldDiceCoordinator>();
            diceCoordinator.ConfigureSceneDice(dice, topology);
            EditorSceneManager.SaveScene(scene, BoardPath);
        }

        internal static void BuildLocalTestbedFromBoard()
        {
            var scene = SceneManager.GetSceneByPath(BoardPath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(BoardPath, OpenSceneMode.Single);
            }
            SceneManager.SetActiveScene(scene);
            var canvas = GameObject.Find("Board Canvas");
            var rig = GameObject.Find("Board Camera Rig");
            var topology = UnityEngine.Object.FindAnyObjectByType<BoardTopology>();
            var director = UnityEngine.Object.FindAnyObjectByType<GameplayCameraDirector>();
            if (canvas == null || rig == null || topology == null || director == null)
            {
                throw new InvalidOperationException("Board scene is missing testbed prerequisites.");
            }

            var networkView = canvas.GetComponent<BoardFlowView>();
            if (networkView != null)
            {
                UnityEngine.Object.DestroyImmediate(networkView);
            }
            var networkPresenter = rig.GetComponent<BoardFlowCameraPresenter>();
            if (networkPresenter != null)
            {
                UnityEngine.Object.DestroyImmediate(networkPresenter);
            }

            var starts = new List<BoardTile>();
            for (var i = 0; i < topology.Tiles.Count; i++)
            {
                var tile = topology.Tiles[i];
                if (tile != null && tile.TileType == BoardTileType.Start)
                {
                    starts.Add(tile);
                }
            }
            starts.Sort((left, right) =>
            {
                var x = left.Coordinate.x.CompareTo(right.Coordinate.x);
                return x != 0 ? x : left.Coordinate.y.CompareTo(right.Coordinate.y);
            });
            if (starts.Count != MultiplayerConstants.MaxPlayers)
            {
                throw new InvalidOperationException("Board flow testbed requires exactly four Start tiles.");
            }

            var localPlayerObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            localPlayerObject.name = "Local Editor Player";
            var capsule = localPlayerObject.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                UnityEngine.Object.DestroyImmediate(capsule);
            }
            localPlayerObject.GetComponent<Renderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/RoomStart.mat");
            localPlayerObject.transform.position = starts[0].GetRecoveryCenter(1f);
            var controller = localPlayerObject.AddComponent<CharacterController>();
            controller.center = Vector3.zero;
            controller.height = 2f;
            controller.radius = 0.5f;
            localPlayerObject.AddComponent<PlayerAvatarVisual>();
            var localBoundaryWalls = localPlayerObject.AddComponent<PlayerBoardBoundaryWalls>();
            localBoundaryWalls.Configure(0, controller, topology);
            localBoundaryWalls.SetPresentationVisible(true);

            var eye = new GameObject("CameraPivot").transform;
            eye.SetParent(localPlayerObject.transform, false);
            eye.localPosition = new Vector3(0f, 0.75f, 0f);

            var markerColors = new[]
            {
                new Color(0.95f, 0.25f, 0.25f), new Color(0.25f, 0.55f, 1f),
                new Color(0.25f, 0.85f, 0.4f), new Color(1f, 0.75f, 0.2f)
            };
            for (var i = 1; i < starts.Count; i++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                marker.name = "Simulated Remote Player " + (i + 1);
                marker.transform.position = starts[i].GetRecoveryCenter(1f);
                var markerCollider = marker.GetComponent<Collider>();
                if (markerCollider != null)
                {
                    UnityEngine.Object.DestroyImmediate(markerCollider);
                }
                var renderer = marker.GetComponent<Renderer>();
                var properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", markerColors[i]);
                properties.SetColor("_Color", markerColors[i]);
                renderer.SetPropertyBlock(properties);
                var remoteController = marker.AddComponent<CharacterController>();
                remoteController.center = Vector3.zero;
                remoteController.height = 2f;
                remoteController.radius = 0.5f;
                marker.AddComponent<PlayerAvatarVisual>();
                remoteController.enabled = false;
                var remoteWalls = marker.AddComponent<PlayerBoardBoundaryWalls>();
                remoteWalls.Configure(i, remoteController, topology);
                remoteWalls.SetPresentationVisible(false);
            }

            CreateEditorTools(canvas.transform,
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var simulator = rig.AddComponent<BoardFlowLocalSimulator>();
            var d12VisualPrefab = EnsureD12RuntimeAssets();
            simulator.Configure(
                controller,
                eye,
                topology,
                director,
                d12VisualPrefab);

            for (var i = 0; i < GameplayInventory.Capacity; i++)
            {
                var choiceButton = FindDescendant(canvas.transform, "ItemChoiceButton" + i);
                if (choiceButton == null)
                {
                    continue;
                }

                var onlineHover = choiceButton.GetComponent<BoardItemChoiceButton>();
                if (onlineHover != null)
                {
                    UnityEngine.Object.DestroyImmediate(onlineHover);
                }

                var localHover = choiceButton.AddComponent<BoardFlowLocalItemChoiceButton>();
                localHover.Configure(simulator, i);
            }

            for (var i = 0; i < ItemShopRules.OfferCount; i++)
            {
                var shopButton = FindDescendant(canvas.transform, "ItemShopOffer" + i);
                if (shopButton == null)
                {
                    continue;
                }

                var onlineHover = shopButton.GetComponent<BoardItemChoiceButton>();
                if (onlineHover != null)
                {
                    UnityEngine.Object.DestroyImmediate(onlineHover);
                }

                var localHover = shopButton.AddComponent<BoardFlowLocalItemChoiceButton>();
                localHover.ConfigureShop(simulator, i);
            }

            EditorSceneManager.SaveScene(scene, TestbedPath);
            ExcludeTestbedFromBuildSettings();
        }

        private static BoardTopology CreateTopology(BoardMaterials materials)
        {
            var root = new GameObject("Board Topology");
            var tileRoot = new GameObject("Rooms").transform;
            tileRoot.SetParent(root.transform);
            var gateRoot = new GameObject("Directed Gates").transform;
            gateRoot.SetParent(root.transform);

            var coordinates = new List<Vector2Int>(MainLoop);
            for (var branchIndex = 0; branchIndex < Branches.Length; branchIndex++)
            {
                var branch = Branches[branchIndex];
                for (var i = 1; i < branch.Length - 1; i++)
                {
                    if (!coordinates.Contains(branch[i]))
                    {
                        coordinates.Add(branch[i]);
                    }
                }
            }

            var tilesByCoordinate = new Dictionary<Vector2Int, BoardTile>();
            var tiles = new List<BoardTile>();
            for (var i = 0; i < coordinates.Count; i++)
            {
                var coordinate = coordinates[i];
                var type = TileTypeFor(coordinate);
                var tile = CreateTile(tileRoot, coordinate, type, materials, i);
                tilesByCoordinate.Add(coordinate, tile);
                tiles.Add(tile);
            }

            var edges = new List<DirectedEdge>();
            for (var i = 0; i < MainLoop.Length; i++)
            {
                edges.Add(new DirectedEdge(MainLoop[i], MainLoop[(i + 1) % MainLoop.Length]));
            }
            for (var branchIndex = 0; branchIndex < Branches.Length; branchIndex++)
            {
                var branch = Branches[branchIndex];
                for (var i = 0; i < branch.Length - 1; i++)
                {
                    edges.Add(new DirectedEdge(branch[i], branch[i + 1]));
                }
            }

            var gates = new List<BoardGate>();
            for (var i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                gates.Add(CreateGate(
                    gateRoot,
                    tilesByCoordinate[edge.Source],
                    tilesByCoordinate[edge.Destination],
                    i));
            }

            var topology = root.AddComponent<BoardTopology>();
            topology.Configure(tiles.ToArray(), gates.ToArray());
            root.AddComponent<KeyShopWorldMarker>();
            root.AddComponent<ItemShopWorldMarker>();
            EditorUtility.SetDirty(topology);
            return topology;
        }

        private static NetworkWorldDie[] CreateNetworkWorldDice()
        {
            var existing = UnityEngine.Object.FindObjectsByType<NetworkWorldDie>(
                FindObjectsInactive.Include);
            for (var i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(existing[i].gameObject);
                }
            }

            var d12VisualPrefab = EnsureD12RuntimeAssets();
            var dice = new NetworkWorldDie[MultiplayerConstants.MaxPlayers];
            var playerMaterials =
                CreateOrUpdateD12PlayerMaterials(D12PlayerColors);

            for (var slot = 0; slot < dice.Length; slot++)
            {
                var dieObject = PrefabUtility.InstantiatePrefab(d12VisualPrefab) as GameObject;
                if (dieObject == null)
                {
                    throw new InvalidOperationException(
                        "Failed to instantiate the generated D12 visual prefab.");
                }

                dieObject.name = "World Die P" + (slot + 1);
                dieObject.transform.position = new Vector3(slot * 1.5f, -20f, 0f);

                var dieRenderer = dieObject.GetComponent<MeshRenderer>();
                if (dieRenderer == null)
                {
                    throw new InvalidOperationException(
                        "The generated D12 visual prefab has no MeshRenderer.");
                }

                // Renderer property blocks are not serialized into the generated
                // scene reliably. A tiny per-slot material asset preserves the tint
                // after closing/reopening Board while sharing the same 512px maps.
                dieRenderer.sharedMaterial = playerMaterials[slot];

                dieObject.AddComponent<NetworkObject>();
                var networkTransform = dieObject.AddComponent<NetworkTransform>();
                networkTransform.Interpolate = true;
                var body = dieObject.AddComponent<Rigidbody>();
                body.mass = 0.8f;
                body.linearDamping = 1.1f;
                body.angularDamping = 1.4f;
                body.maxAngularVelocity = 24f;
                dieObject.AddComponent<NetworkRigidbody>();

                if (WorldDieD12Layout.FaceCount !=
                    WorldDieAuthorityModel.MaximumFace)
                {
                    throw new InvalidOperationException(
                        "The D12 face mapping must contain exactly one normal per roll value.");
                }

                var markers =
                    new WorldDieFaceMarker[WorldDieD12Layout.FaceCount];
                for (var faceIndex = 0; faceIndex < markers.Length; faceIndex++)
                {
                    var faceValue =
                        faceIndex + WorldDieAuthorityModel.MinimumFace;
                    if (!WorldDieD12Layout.TryGetLocalNormal(
                            faceValue,
                            out var normal) ||
                        !WorldDieD12Layout.TryGetLocalMarkerPosition(
                            faceValue,
                            out var markerPosition))
                    {
                        throw new InvalidOperationException(
                            "The shared D12 layout is missing face " +
                            faceValue + ".");
                    }

                    var markerObject = new GameObject("Face " + faceValue);
                    markerObject.transform.SetParent(dieObject.transform, false);
                    markerObject.transform.localPosition =
                        markerPosition;
                    markerObject.transform.localRotation =
                        Quaternion.FromToRotation(Vector3.up, normal);
                    var marker = markerObject.AddComponent<WorldDieFaceMarker>();
                    marker.Configure(faceValue);
                    markers[faceIndex] = marker;
                }

                var resultObject = new GameObject("Public World Result");
                resultObject.transform.SetParent(dieObject.transform, false);
                resultObject.transform.localScale =
                    Vector3.one / D12VisualScale;
                var resultText = resultObject.AddComponent<TextMesh>();
                resultText.text = "?";
                resultText.anchor = TextAnchor.MiddleCenter;
                resultText.alignment = TextAlignment.Center;
                resultText.fontSize = 64;
                resultText.characterSize = 0.045f;
                resultText.color = Color.white;

                var die = dieObject.AddComponent<NetworkWorldDie>();
                var renderers = dieObject.GetComponentsInChildren<Renderer>(true);
                die.ConfigureSceneDie(slot, renderers, markers, resultText);
                for (var i = 0; i < renderers.Length; i++)
                {
                    renderers[i].enabled = false;
                }
                dice[slot] = die;
            }

            return dice;
        }

        private static GameObject EnsureD12RuntimeAssets()
        {
            ConfigureD12ImportSettings();
            var material = CreateOrUpdateD12Material();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(D12ModelPath);
            if (model == null)
            {
                throw new InvalidOperationException(
                    "Tracked D12 model is missing at " + D12ModelPath + ".");
            }

            var sourceFilter = model.GetComponentInChildren<MeshFilter>(true);
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
            {
                throw new InvalidOperationException(
                    "Tracked D12 model does not contain an imported mesh.");
            }

            var temporary = new GameObject("D12 World Die Visual");
            try
            {
                temporary.transform.localScale = Vector3.one * D12VisualScale;
                var filter = temporary.AddComponent<MeshFilter>();
                filter.sharedMesh = sourceFilter.sharedMesh;
                var renderer = temporary.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
                var collider = temporary.AddComponent<MeshCollider>();
                collider.sharedMesh = sourceFilter.sharedMesh;
                collider.convex = true;

                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    temporary,
                    D12VisualPrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        "Failed to save the generated D12 visual prefab.");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temporary);
            }
        }

        private static void ConfigureD12ImportSettings()
        {
            var modelImporter = AssetImporter.GetAtPath(D12ModelPath) as ModelImporter;
            if (modelImporter == null)
            {
                throw new InvalidOperationException(
                    "D12 model importer is unavailable at " + D12ModelPath + ".");
            }

            var modelChanged = false;
            if (modelImporter.importAnimation)
            {
                modelImporter.importAnimation = false;
                modelChanged = true;
            }
            if (modelImporter.isReadable)
            {
                modelImporter.isReadable = false;
                modelChanged = true;
            }
            if (modelImporter.addCollider)
            {
                modelImporter.addCollider = false;
                modelChanged = true;
            }
            if (modelImporter.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                modelImporter.materialImportMode = ModelImporterMaterialImportMode.None;
                modelChanged = true;
            }
            if (modelChanged)
            {
                modelImporter.SaveAndReimport();
            }

            ConfigureD12Texture(
                D12AlbedoPath,
                TextureImporterType.Default,
                true);
            ConfigureD12Texture(
                D12NormalPath,
                TextureImporterType.NormalMap,
                false);
            ConfigureD12Texture(
                D12OcclusionPath,
                TextureImporterType.Default,
                false);
        }

        private static void ConfigureD12Texture(
            string path,
            TextureImporterType textureType,
            bool sRgb)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    "D12 texture importer is unavailable at " + path + ".");
            }

            var changed = false;
            if (importer.textureType != textureType)
            {
                importer.textureType = textureType;
                changed = true;
            }
            if (importer.sRGBTexture != sRgb)
            {
                importer.sRGBTexture = sRgb;
                changed = true;
            }
            if (!importer.mipmapEnabled)
            {
                importer.mipmapEnabled = true;
                changed = true;
            }
            if (importer.maxTextureSize != 512)
            {
                importer.maxTextureSize = 512;
                changed = true;
            }
            if (importer.textureCompression != TextureImporterCompression.Compressed)
            {
                importer.textureCompression = TextureImporterCompression.Compressed;
                changed = true;
            }
            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        private static Material CreateOrUpdateD12Material()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                         Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "No supported Lit shader is available for the D12 die.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(D12MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "D12Tintable" };
                AssetDatabase.CreateAsset(material, D12MaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(D12AlbedoPath);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(D12NormalPath);
            var occlusion = AssetDatabase.LoadAssetAtPath<Texture2D>(D12OcclusionPath);
            if (albedo == null || normal == null || occlusion == null)
            {
                throw new InvalidOperationException(
                    "One or more tracked D12 textures could not be loaded.");
            }

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", albedo);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", albedo);
            }
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }
            if (material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
            }
            if (material.HasProperty("_OcclusionMap"))
            {
                material.SetTexture("_OcclusionMap", occlusion);
                material.SetFloat("_OcclusionStrength", 1f);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.48f);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material[] CreateOrUpdateD12PlayerMaterials(Color[] colors)
        {
            var baseMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(D12MaterialPath);
            if (baseMaterial == null)
            {
                throw new InvalidOperationException(
                    "The generated D12 base material is unavailable.");
            }

            var materials = new Material[colors.Length];
            for (var slot = 0; slot < colors.Length; slot++)
            {
                var name = "D12Player" + (slot + 1);
                var path = D12MaterialFolder + "/" + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(baseMaterial) { name = name };
                    AssetDatabase.CreateAsset(material, path);
                }
                else
                {
                    material.CopyPropertiesFromMaterial(baseMaterial);
                    material.shader = baseMaterial.shader;
                    material.name = name;
                }

                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", colors[slot]);
                }
                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", colors[slot]);
                }
                EditorUtility.SetDirty(material);
                materials[slot] = material;
            }

            return materials;
        }

        private static BoardTile CreateTile(
            Transform parent,
            Vector2Int coordinate,
            BoardTileType type,
            BoardMaterials materials,
            int index)
        {
            var tileObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tileObject.name = "Room " + coordinate.x + "," + coordinate.y + " - " + type;
            tileObject.transform.SetParent(parent);
            tileObject.transform.position = GridToWorld(coordinate);
            tileObject.transform.localScale = new Vector3(7.72f, 0.2f, 7.72f);
            tileObject.GetComponent<Renderer>().sharedMaterial = MaterialFor(type, materials, index);

            var tile = tileObject.AddComponent<BoardTile>();
            tile.Configure(coordinate, type);

            var labelObject = new GameObject("Room Label");
            labelObject.transform.SetParent(tileObject.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 0.56f, 0f);
            labelObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            labelObject.transform.localScale = Vector3.one * 0.05f;
            var label = labelObject.AddComponent<TextMesh>();
            label.text = coordinate.x + "," + coordinate.y + "\n" + TileTypeLabel(type);
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 30;
            label.characterSize = 0.35f;
            label.color = Color.white;
            return tile;
        }

        private static BoardGate CreateGate(
            Transform parent,
            BoardTile source,
            BoardTile destination,
            int index)
        {
            var direction = (destination.WorldCenter - source.WorldCenter).normalized;
            var gateObject = new GameObject(
                "Gate " + index.ToString("00") + " - " + source.Coordinate + " to " + destination.Coordinate);
            gateObject.transform.SetParent(parent);
            gateObject.transform.SetPositionAndRotation(
                (source.WorldCenter + destination.WorldCenter) * 0.5f + Vector3.up * 0.15f,
                Quaternion.LookRotation(direction, Vector3.up));
            var gate = gateObject.AddComponent<BoardGate>();
            // A reused boundary wall represents the whole side of one square room.
            // Blue means this directed side is passable, so traversal validation uses
            // the full room width instead of the earlier narrow prototype gateway.
            gate.Configure(source, destination, BoardTile.RoomSize);
            return gate;
        }

        private static GameplayCameraDirector CreateCameraRig()
        {
            var root = new GameObject("Board Camera Rig");
            var framingObject = new GameObject("Board Framing Anchor");
            framingObject.transform.SetParent(root.transform);
            var framing = framingObject.AddComponent<BoardCameraFramingAnchor>();
            var settings = new BoardCameraFramingSettings(
                new Vector2(56f, 56f), 0.15f, 50f, 20f, 0f);
            framing.Configure(settings);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(root.transform);
            var output = cameraObject.AddComponent<Camera>();
            output.clearFlags = CameraClearFlags.SolidColor;
            output.backgroundColor = new Color(0.018f, 0.026f, 0.045f);
            output.nearClipPlane = 0.05f;
            output.farClipPlane = 500f;
            cameraObject.AddComponent<AudioListener>();
            var brain = cameraObject.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.Cut, 0f);
            brain.IgnoreTimeScale = true;

            var boardPose = settings.Evaluate(Vector3.zero, 16f / 9f);
            var boardCamera = CreateCinemachineCamera(
                root.transform, "CM_BoardWide", boardPose.Position, boardPose.Rotation, boardPose.FieldOfView, 100);
            var firstPerson = CreateCinemachineCamera(
                root.transform, "CM_FirstPerson", new Vector3(0f, 2f, 0f), Quaternion.identity, 70f, 0);
            var minigame = CreateCinemachineCamera(
                root.transform, "CM_MinigamePlaceholder", boardPose.Position, boardPose.Rotation, 55f, 0);

            var director = root.AddComponent<GameplayCameraDirector>();
            director.Configure(output, firstPerson, boardCamera, minigame);
            director.SetBoardFramingAnchor(framing);
            var presenter = root.AddComponent<BoardFlowCameraPresenter>();
            presenter.Configure(director);
            return director;
        }

        private static CinemachineCamera CreateCinemachineCamera(
            Transform parent,
            string name,
            Vector3 position,
            Quaternion rotation,
            float fieldOfView,
            int priority)
        {
            var cameraObject = new GameObject(name);
            cameraObject.transform.SetParent(parent);
            cameraObject.transform.SetPositionAndRotation(position, rotation);
            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = priority;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = fieldOfView;
            lens.NearClipPlane = 0.05f;
            lens.FarClipPlane = 500f;
            camera.Lens = lens;
            return camera;
        }

        private static GameObject CreateBoardCanvasTemplate()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException("Unity LegacyRuntime.ttf was not found.");
            }

            var root = new GameObject(
                "Board Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(BoardEventSystemBootstrap),
                typeof(BoardFlowView));
            root.layer = LayerMask.NameToLayer("UI");
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            CreateHeader(root.transform, font);
            CreatePlayerPanel(root.transform, font);
            CreateInventory(root.transform, font);
            CreateStatus(root.transform, font);
            CreateSelectionPanel(root.transform, font);
            CreateItemShopPanel(root.transform, font);
            CreateReadyPanel(root.transform, font);
            CreateResultPanel(root.transform, font);
            CreateReticle(root.transform, font);
            CreateReconnectOverlay(root.transform, font);
            return root;
        }

        private static void CreateBoardCanvas(GameplayCameraDirector cameraDirector)
        {
            var prefab = LoadOrCreateBoardCanvasPrefab();
            var root = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (root == null)
            {
                throw new InvalidOperationException(
                    "Board Canvas prefab could not be instantiated.");
            }

            root.name = "Board Canvas";
            var view = root.GetComponent<BoardFlowView>();
            if (view == null)
            {
                throw new InvalidOperationException(
                    "Board Canvas prefab is missing BoardFlowView.");
            }

            view.Configure(cameraDirector);
        }

        private static GameObject LoadOrCreateBoardCanvasPrefab()
        {
            EnsureFolder(UiPrefabFolder);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardCanvasPrefabPath);
            if (prefab == null)
            {
                var template = CreateBoardCanvasTemplate();
                try
                {
                    prefab = PrefabUtility.SaveAsPrefabAsset(
                        template,
                        BoardCanvasPrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(template);
                }

                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        "Failed to create the Board Canvas prefab.");
                }

                AssetDatabase.SaveAssets();
            }

            ValidateBoardCanvasPrefab(prefab);
            return prefab;
        }

        private static void ValidateBoardCanvasPrefab(GameObject prefab)
        {
            if (prefab == null ||
                prefab.GetComponent<Canvas>() == null ||
                prefab.GetComponent<CanvasScaler>() == null ||
                prefab.GetComponent<GraphicRaycaster>() == null ||
                prefab.GetComponent<BoardEventSystemBootstrap>() == null ||
                prefab.GetComponent<BoardFlowView>() == null)
            {
                throw new InvalidOperationException(
                    "Board Canvas prefab root contract is incomplete. " +
                    "Keep its Canvas, scaler, raycaster, event bootstrap, and BoardFlowView.");
            }

            var requiredPanels = new[]
            {
                "Header Panel",
                "Player State Panel",
                "Inventory Panel",
                "ItemSelectionPanel",
                "ItemShopPanel",
                "MinigameReadyPanel",
                "SkippedResultPanel",
                "ReconnectOverlay",
                "BoardReticle"
            };
            for (var i = 0; i < requiredPanels.Length; i++)
            {
                if (FindDescendant(prefab.transform, requiredPanels[i]) == null)
                {
                    throw new InvalidOperationException(
                        "Board Canvas prefab is missing UI anchor '" +
                        requiredPanels[i] + "'.");
                }
            }

            var requiredTexts = new[]
            {
                "TurnText",
                "PhaseText",
                "PhaseTimerText",
                "BoardChoiceTimerText",
                "BoardShieldText",
                "DiceText",
                "MovesText",
                "BoardAmmoText",
                "BoardStatusText",
                "BoardTooltipText",
                "ReconnectText",
                "ItemShopTitle",
                "ItemShopTooltip",
                "ItemShopStatus"
            };
            for (var i = 0; i < requiredTexts.Length; i++)
            {
                RequireBoardUiComponent<Text>(prefab, requiredTexts[i]);
            }

            RequireBoardUiComponent<Button>(prefab, "NoItemButton");
            RequireBoardUiComponent<Button>(prefab, "ReadyButton");
            RequireBoardUiComponent<Button>(prefab, "ItemShopCloseButton");

            for (var i = 0; i < GameplayInventory.Capacity; i++)
            {
                RequireBoardUiComponent<Image>(prefab, "BoardInventorySlot" + i);
                RequireBoardUiComponent<Text>(prefab, "BoardInventorySlotLabel" + i);
                RequireBoardUiComponent<Button>(prefab, "ItemChoiceButton" + i);
                RequireBoardUiComponent<Text>(prefab, "ItemChoiceLabel" + i);
                RequireBoardUiComponent<BoardItemChoiceButton>(
                    prefab,
                    "ItemChoiceButton" + i);
            }

            for (var i = 0; i < MultiplayerConstants.MaxPlayers; i++)
            {
                RequireBoardUiComponent<Image>(prefab, "PlayerCard" + i);
                RequireBoardUiComponent<Text>(prefab, "PlayerState" + i);
                RequireBoardUiComponent<Image>(prefab, "PlayerHealthFill" + i);
                RequireBoardUiComponent<Text>(prefab, "PlayerHealthText" + i);
                RequireBoardUiComponent<Text>(prefab, "PlayerCurrency" + i);
                RequireBoardUiComponent<Text>(prefab, "PlayerActionIcon" + i);
                RequireBoardUiComponent<Text>(prefab, "PlayerRank" + i);
            }

            for (var i = 0; i < ItemShopRules.OfferCount; i++)
            {
                RequireBoardUiComponent<Button>(prefab, "ItemShopOffer" + i);
                RequireBoardUiComponent<Text>(prefab, "ItemShopOfferLabel" + i);
                RequireBoardUiComponent<BoardItemChoiceButton>(
                    prefab,
                    "ItemShopOffer" + i);
            }
        }

        private static T RequireBoardUiComponent<T>(
            GameObject prefab,
            string objectName)
            where T : Component
        {
            var target = FindDescendant(prefab.transform, objectName);
            var component = target != null ? target.GetComponent<T>() : null;
            if (component == null)
            {
                throw new InvalidOperationException(
                    "Board Canvas prefab anchor '" + objectName +
                    "' must keep its " + typeof(T).Name + " component.");
            }

            return component;
        }

        private static void CreateHeader(Transform canvas, Font font)
        {
            var panel = CreatePanel("Header Panel", canvas, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(820f, 108f),
                new Vector2(0.5f, 1f), new Color(0.025f, 0.045f, 0.08f, 0.94f));
            CreateText("TurnText", panel.transform, "TURN --", font, 27,
                new Vector2(-300f, -30f), new Vector2(180f, 42f), TextAnchor.MiddleCenter);
            CreateText("PhaseText", panel.transform, "WAITING FOR 4 PLAYERS", font, 25,
                new Vector2(0f, -30f), new Vector2(420f, 42f), TextAnchor.MiddleCenter);
            CreateText("PhaseTimerText", panel.transform, "--:--", font, 30,
                new Vector2(305f, -30f), new Vector2(170f, 42f), TextAnchor.MiddleCenter);
            CreateText("BoardChoiceTimerText", panel.transform, "CHOICE --", font, 17,
                new Vector2(-205f, -74f), new Vector2(250f, 30f), TextAnchor.MiddleCenter);
            CreateText("BoardShieldText", panel.transform, "SHIELD OFF", font, 17,
                new Vector2(90f, -74f), new Vector2(220f, 30f), TextAnchor.MiddleCenter);
        }

        private static void CreatePlayerPanel(Transform canvas, Font font)
        {
            var panel = CreatePanel("Player State Panel", canvas, new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(22f, -22f), new Vector2(370f, 420f),
                new Vector2(0f, 1f), new Color(0.025f, 0.045f, 0.08f, 0.9f));
            CreateText("Players Title", panel.transform, "ONLINE PLAYERS", font, 19,
                new Vector2(185f, -20f), new Vector2(330f, 30f), TextAnchor.MiddleCenter,
                new Vector2(0f, 1f), new Vector2(0f, 1f));
            for (var i = 0; i < MultiplayerConstants.MaxPlayers; i++)
            {
                var card = CreatePanel(
                    "PlayerCard" + i,
                    panel.transform,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(12f, -44f - i * 90f),
                    new Vector2(346f, 82f),
                    new Vector2(0f, 1f),
                    new Color(0.055f, 0.085f, 0.13f, 0.94f));
                CreateText(
                    "PlayerState" + i,
                    card.transform,
                    "P" + (i + 1) + "  WAITING",
                    font,
                    17,
                    new Vector2(88f, -15f),
                    new Vector2(150f, 24f),
                    TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f));
                CreateText(
                    "PlayerRank" + i,
                    card.transform,
                    "RANK 1",
                    font,
                    15,
                    new Vector2(289f, -15f),
                    new Vector2(90f, 24f),
                    TextAnchor.MiddleCenter,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f));

                var healthBar = CreatePanel(
                    "PlayerHealthBar" + i,
                    card.transform,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(12f, -32f),
                    new Vector2(220f, 18f),
                    new Vector2(0f, 1f),
                    new Color(0.12f, 0.14f, 0.18f, 1f));
                var healthFill = CreatePanel(
                    "PlayerHealthFill" + i,
                    healthBar.transform,
                    Vector2.zero,
                    Vector2.one,
                    Vector2.zero,
                    Vector2.zero,
                    new Vector2(0.5f, 0.5f),
                    new Color(0.2f, 0.82f, 0.38f, 1f));
                var fillImage = healthFill.GetComponent<Image>();
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Horizontal;
                fillImage.fillOrigin = 0;
                fillImage.fillAmount = 1f;
                CreateText(
                    "PlayerHealthText" + i,
                    healthBar.transform,
                    "100/100",
                    font,
                    13,
                    Vector2.zero,
                    new Vector2(214f, 18f),
                    TextAnchor.MiddleCenter);
                CreateText(
                    "PlayerCurrency" + i,
                    card.transform,
                    "KEY  0    GOLD  10",
                    font,
                    15,
                    new Vector2(116f, -65f),
                    new Vector2(220f, 22f),
                    TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f));
                CreateText(
                    "PlayerActionIcon" + i,
                    card.transform,
                    string.Empty,
                    font,
                    16,
                    new Vector2(288f, -52f),
                    new Vector2(100f, 38f),
                    TextAnchor.MiddleCenter,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f));
            }
        }

        private static void CreateInventory(Transform canvas, Font font)
        {
            var panel = CreatePanel("Inventory Panel", canvas, new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(690f, 132f),
                new Vector2(0.5f, 0f), new Color(0.025f, 0.045f, 0.08f, 0.92f));
            CreateText("Inventory Title", panel.transform, "ITEM SLOTS", font, 17,
                new Vector2(0f, 105f), new Vector2(300f, 25f), TextAnchor.MiddleCenter);
            for (var i = 0; i < GameplayInventory.Capacity; i++)
            {
                var slot = CreatePanel("BoardInventorySlot" + i, panel.transform, new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f), new Vector2((i - 1) * 210f, 14f), new Vector2(194f, 78f),
                    new Vector2(0.5f, 0f), new Color(0.18f, 0.36f, 0.58f, 0.94f));
                CreateText("BoardInventorySlotLabel" + i, slot.transform, "EMPTY", font, 17,
                    Vector2.zero, new Vector2(180f, 64f), TextAnchor.MiddleCenter);
            }

            CreateText("BoardAmmoText", canvas, "CHARGE --", font, 24,
                new Vector2(-160f, 44f), new Vector2(280f, 74f), TextAnchor.MiddleRight,
                new Vector2(1f, 0f), new Vector2(1f, 0f));
            CreateText("DiceText", canvas, "DICE --", font, 23,
                new Vector2(185f, 120f), new Vector2(330f, 38f), TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(0f, 0f));
            CreateText("MovesText", canvas, "MOVES --", font, 23,
                new Vector2(150f, 80f), new Vector2(260f, 38f), TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(0f, 0f));
        }

        private static void CreateStatus(Transform canvas, Font font)
        {
            CreateText("BoardStatusText", canvas, "Waiting for the board flow.", font, 18,
                new Vector2(0f, 164f), new Vector2(900f, 38f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        }

        private static void CreateSelectionPanel(Transform canvas, Font font)
        {
            var panel = CreatePanel("ItemSelectionPanel", canvas, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 390f),
                new Vector2(0.5f, 0.5f), new Color(0.02f, 0.035f, 0.07f, 0.98f));
            CreateText("Selection Title", panel.transform, "CHOOSE AN ITEM", font, 32,
                new Vector2(0f, 150f), new Vector2(600f, 46f), TextAnchor.MiddleCenter);
            CreateText("Selection Rule", panel.transform,
                "Personal 30-second limit. The shared 3:00 action clock continues.", font, 18,
                new Vector2(0f, 112f), new Vector2(720f, 32f), TextAnchor.MiddleCenter);
            for (var i = 0; i < GameplayInventory.Capacity; i++)
            {
                var button = CreateButton("ItemChoiceButton" + i, panel.transform, "ITEM", font,
                    new Vector2((i - 1) * 240f, 22f), new Vector2(215f, 105f));
                button.gameObject.AddComponent<BoardItemChoiceButton>();
                button.GetComponentInChildren<Text>().gameObject.name = "ItemChoiceLabel" + i;
            }
            CreateText("BoardTooltipText", panel.transform, "Hover an item for details.", font, 17,
                new Vector2(0f, -76f), new Vector2(700f, 58f), TextAnchor.MiddleCenter);
            CreateButton("NoItemButton", panel.transform, "DO NOT USE", font,
                new Vector2(0f, -145f), new Vector2(300f, 52f));
            panel.SetActive(false);
        }

        private static void CreateReadyPanel(Transform canvas, Font font)
        {
            var panel = CreatePanel("MinigameReadyPanel", canvas, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(610f, 245f),
                new Vector2(0.5f, 0.5f), new Color(0.03f, 0.055f, 0.09f, 0.98f));
            CreateText("Ready Title", panel.transform, "MINIGAME INTRO / READY", font, 28,
                new Vector2(0f, 73f), new Vector2(540f, 44f), TextAnchor.MiddleCenter);
            CreateText("Ready Note", panel.transform,
                "Minigame selection is TODO. Ready from all four players triggers the development skip.",
                font, 17, new Vector2(0f, 20f), new Vector2(520f, 62f), TextAnchor.MiddleCenter);
            CreateButton("ReadyButton", panel.transform, "READY / SKIP", font,
                new Vector2(0f, -74f), new Vector2(280f, 58f));
            panel.SetActive(false);
        }

        private static void CreateItemShopPanel(Transform canvas, Font font)
        {
            var panel = CreatePanel("ItemShopPanel", canvas, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1030f, 570f),
                new Vector2(0.5f, 0.5f), new Color(0.025f, 0.035f, 0.075f, 0.985f));
            CreateText("ItemShopTitle", panel.transform, "ITEM SHOP", font, 32,
                new Vector2(0f, 240f), new Vector2(720f, 46f), TextAnchor.MiddleCenter);
            CreateText("ItemShopRule", panel.transform,
                "Each offer has one shared copy. The action clock continues.", font, 17,
                new Vector2(0f, 200f), new Vector2(850f, 30f), TextAnchor.MiddleCenter);

            for (var i = 0; i < ItemShopRules.OfferCount; i++)
            {
                var row = i < 3 ? 0 : 1;
                var column = row == 0 ? i : i - 3;
                var x = row == 0
                    ? (column - 1) * 290f
                    : (column - 0.5f) * 290f;
                var y = row == 0 ? 105f : -25f;
                var button = CreateButton(
                    "ItemShopOffer" + i,
                    panel.transform,
                    "ITEM\n0 GOLD",
                    font,
                    new Vector2(x, y),
                    new Vector2(260f, 105f));
                button.gameObject.AddComponent<BoardItemChoiceButton>();
                button.GetComponentInChildren<Text>().gameObject.name = "ItemShopOfferLabel" + i;
            }

            CreateText("ItemShopTooltip", panel.transform,
                "Hover an item for details.", font, 17,
                new Vector2(0f, -130f), new Vector2(880f, 62f), TextAnchor.MiddleCenter);
            CreateText("ItemShopStatus", panel.transform,
                "Select an available item to buy it immediately.", font, 17,
                new Vector2(0f, -190f), new Vector2(880f, 35f), TextAnchor.MiddleCenter);
            CreateButton("ItemShopCloseButton", panel.transform, "CLOSE", font,
                new Vector2(0f, -245f), new Vector2(260f, 48f));
            panel.SetActive(false);
        }

        private static void CreateResultPanel(Transform canvas, Font font)
        {
            var panel = CreatePanel("SkippedResultPanel", canvas, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(570f, 170f),
                new Vector2(0.5f, 0.5f), new Color(0.08f, 0.045f, 0.1f, 0.98f));
            CreateText("Result Title", panel.transform, "RESULT PLACEHOLDER", font, 30,
                new Vector2(0f, 42f), new Vector2(500f, 44f), TextAnchor.MiddleCenter);
            CreateText("Result Note", panel.transform,
                "No minigame reward or currency transfer. Next turn begins in 3 seconds.", font, 18,
                new Vector2(0f, -25f), new Vector2(490f, 60f), TextAnchor.MiddleCenter);
            panel.SetActive(false);
        }

        private static void CreateReticle(Transform canvas, Font font)
        {
            var reticle = CreateText("BoardReticle", canvas, "+", font, 30,
                Vector2.zero, new Vector2(40f, 40f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            reticle.gameObject.SetActive(false);
        }

        private static void CreateReconnectOverlay(Transform canvas, Font font)
        {
            var overlay = CreatePanel("ReconnectOverlay", canvas, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f),
                new Color(0.015f, 0.02f, 0.035f, 0.9f));
            var rect = overlay.GetComponent<RectTransform>();
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            CreateText("ReconnectText", overlay.transform,
                "PLAYER DISCONNECTED\nMATCH PAUSED\n01:00 remaining", font, 34,
                Vector2.zero, new Vector2(720f, 190f), TextAnchor.MiddleCenter);
            overlay.SetActive(false);
        }

        private static GameObject CreatePanel(
            string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPosition, Vector2 size, Vector2 pivot, Color color)
        {
            var panel = CreateUiObject(name, parent);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            panel.AddComponent<Image>().color = color;
            return panel;
        }

        private static Text CreateText(
            string name, Transform parent, string value, Font font, int fontSize,
            Vector2 position, Vector2 size, TextAnchor alignment,
            Vector2? anchorMin = null, Vector2? anchorMax = null)
        {
            var textObject = CreateUiObject(name, parent);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin ?? new Vector2(0.5f, 0.5f);
            rect.anchorMax = anchorMax ?? rect.anchorMin;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = textObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = new Color(0.93f, 0.96f, 1f);
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(
            string name, Transform parent, string label, Font font,
            Vector2 position, Vector2 size)
        {
            var buttonObject = CreateUiObject(name, parent);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.13f, 0.39f, 0.68f, 1f);
            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            CreateText("Label", buttonObject.transform, label, font, 18,
                Vector2.zero, size - new Vector2(12f, 10f), TextAnchor.MiddleCenter);
            return button;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var result = new GameObject(name, typeof(RectTransform));
            result.layer = LayerMask.NameToLayer("UI");
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Board Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.86f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
        }

        private static void CreateBoardBackdrop(Material material)
        {
            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backdrop.name = "Board Backdrop (No Gameplay Collision)";
            backdrop.transform.position = new Vector3(0f, -0.42f, 0f);
            backdrop.transform.localScale = new Vector3(60f, 0.4f, 60f);
            backdrop.GetComponent<Renderer>().sharedMaterial = material;
            var collider = backdrop.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static BoardMaterials CreateMaterials()
        {
            return new BoardMaterials
            {
                NormalA = CreateOrUpdateMaterial("RoomNormalA", new Color(0.12f, 0.28f, 0.42f)),
                NormalB = CreateOrUpdateMaterial("RoomNormalB", new Color(0.14f, 0.36f, 0.43f)),
                Start = CreateOrUpdateMaterial("RoomStart", new Color(0.16f, 0.58f, 0.3f)),
                KeyShop = CreateOrUpdateMaterial("RoomKeyShop", new Color(0.76f, 0.52f, 0.08f)),
                Respawn = CreateOrUpdateMaterial("RoomRespawn", new Color(0.1f, 0.58f, 0.68f)),
                Backdrop = CreateOrUpdateMaterial("BoardBackdrop", new Color(0.025f, 0.04f, 0.065f))
            };
        }

        private static Material CreateOrUpdateMaterial(string name, Color color)
        {
            var path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null)
                {
                    throw new InvalidOperationException("No supported Lit shader is available.");
                }
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material MaterialFor(
            BoardTileType type, BoardMaterials materials, int index)
        {
            switch (type)
            {
                case BoardTileType.Start: return materials.Start;
                case BoardTileType.KeyShop: return materials.KeyShop;
                case BoardTileType.Respawn: return materials.Respawn;
                default: return index % 2 == 0 ? materials.NormalA : materials.NormalB;
            }
        }

        private static BoardTileType TileTypeFor(Vector2Int coordinate)
        {
            if (coordinate == new Vector2Int(1, 1) || coordinate == new Vector2Int(5, 1) ||
                coordinate == new Vector2Int(5, 5) || coordinate == new Vector2Int(1, 5))
            {
                return BoardTileType.Start;
            }
            // The only Key Shop is a runtime marker selected at turn-two overview.
            // Static shop rooms would violate the unique-shop and turn-one-hidden rules.
            if (coordinate == new Vector2Int(3, 0) || coordinate == new Vector2Int(6, 3) ||
                coordinate == new Vector2Int(3, 6) || coordinate == new Vector2Int(0, 3))
            {
                return BoardTileType.Respawn;
            }
            return BoardTileType.Normal;
        }

        private static string TileTypeLabel(BoardTileType type)
        {
            switch (type)
            {
                case BoardTileType.Start: return "START";
                case BoardTileType.KeyShop: return "KEY SHOP";
                case BoardTileType.Respawn: return "RESPAWN";
                default: return "NORMAL";
            }
        }

        private static Vector3 GridToWorld(Vector2Int coordinate)
        {
            return new Vector3((coordinate.x - 3) * RoomSize, 0f, (coordinate.y - 3) * RoomSize);
        }

        private static void EnsureFolders()
        {
            EnsureFolder(Root);
            EnsureFolder(BoardFolder);

            EnsureFolder(UiFolder);
            EnsureFolder(UiPrefabFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(DiceArtFolder);
            EnsureFolder(D12ArtFolder);
            EnsureFolder(D12ModelFolder);
            EnsureFolder(D12TextureFolder);
            EnsureFolder(D12MaterialFolder);
            EnsureFolder(D12PrefabFolder);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            var separator = path.LastIndexOf('/');
            var parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }

        private static void EnsureBoardInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            var found = false;
            for (var i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == BoardPath)
                {
                    scenes[i] = new EditorBuildSettingsScene(BoardPath, true);
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                scenes.Add(new EditorBuildSettingsScene(BoardPath, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void ExcludeTestbedFromBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (var i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == TestbedPath)
                {
                    scenes[i] = new EditorBuildSettingsScene(TestbedPath, false);
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return;
                }
            }
        }

        private static void CreateEditorTools(Transform canvas, Font font)
        {
            var panel = CreatePanel("Editor Flow Tools", canvas, new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-22f, -22f), new Vector2(310f, 480f),
                new Vector2(1f, 1f), new Color(0.08f, 0.035f, 0.11f, 0.95f));
            CreateText("Editor Tools Title", panel.transform, "EDITOR LOCAL TOOLS", font, 20,
                new Vector2(0f, -28f), new Vector2(270f, 32f), TextAnchor.MiddleCenter);
            CreateText("Editor Tools Help", panel.transform,
                "No network required. Other players are simulated.\nTAB toggles the pointer during action.", font, 14,
                new Vector2(0f, -63f), new Vector2(270f, 46f), TextAnchor.MiddleCenter);
            CreateButton("EditorStageFightButton", panel.transform, "STAGE P1 / P2 FIGHT", font,
                new Vector2(0f, -105f), new Vector2(270f, 42f));
            CreateButton("EditorFinishActionButton", panel.transform, "FINISH ACTION (ALL ARRIVED)", font,
                new Vector2(0f, -154f), new Vector2(270f, 42f));
            CreateButton("EditorSpeedButton", panel.transform, "FLOW SPEED  x1", font,
                new Vector2(0f, -203f), new Vector2(270f, 42f));
            CreateButton("EditorPauseButton", panel.transform, "PAUSE / RESUME MODEL", font,
                new Vector2(0f, -252f), new Vector2(270f, 42f));
            CreateButton("EditorDamagePlayerButton", panel.transform, "DAMAGE P1 / COMBAT HIT", font,
                new Vector2(0f, -301f), new Vector2(270f, 42f));
            CreateButton("EditorAddGoldButton", panel.transform, "ADD P1 GOLD  +10", font,
                new Vector2(0f, -350f), new Vector2(270f, 42f));
            CreateButton("EditorBuyKeyButton", panel.transform, "BUY KEY  -20 GOLD", font,
                new Vector2(0f, -399f), new Vector2(270f, 42f));
        }

        private static GameObject FindDescendant(Transform root, string objectName)
        {
            var descendants = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < descendants.Length; i++)
            {
                if (descendants[i].gameObject.name == objectName)
                {
                    return descendants[i].gameObject;
                }
            }

            return null;
        }

        private readonly struct DirectedEdge
        {
            public DirectedEdge(Vector2Int source, Vector2Int destination)
            {
                Source = source;
                Destination = destination;
            }
            public Vector2Int Source { get; }
            public Vector2Int Destination { get; }
        }

        private sealed class BoardMaterials
        {
            public Material NormalA;
            public Material NormalB;
            public Material Start;
            public Material KeyShop;
            public Material Respawn;
            public Material Backdrop;
        }
    }
}

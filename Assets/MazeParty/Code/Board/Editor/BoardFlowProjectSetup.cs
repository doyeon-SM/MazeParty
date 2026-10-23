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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardFlowProjectSetup
    {
        private const string Root = "Assets/MazeParty";
        private const string BoardFolder = Root + "/Board";
        private const string MaterialFolder = BoardFolder + "/Materials";
        private const string BoardPath = "Assets/MazeParty/Scenes/Board/Board.unity";
        private const string RuntimeTerrainResourcesName =
            "Runtime Terrain Resources (Build Placeholder)";
        internal const string BoardSkyboxPath =
            "Assets/Ignore/Fantasy Skybox FREE/Cubemaps/Classic/FS000_Night_01.mat";
        internal const string ResourceTransferCoinSourcePath =
            "Assets/Ignore/BTM_Assets/BTM_Items_Gems/Prefabs/Coin.prefab";
        internal const string ResourceTransferKeySourcePath =
            "Assets/Ignore/BTM_Assets/BTM_Items_Gems/Prefabs/Key.prefab";
        internal const string ResourceTransferCoinPrefabPath =
            "Assets/MazeParty/Prefabs/Board/World/BoardEventCoin.prefab";
        internal const string ResourceTransferKeyPrefabPath =
            "Assets/MazeParty/Prefabs/Board/World/BoardEventKey.prefab";

        private const string DiceArtFolder = Root + "/Art/Dice";
        private const string D12ArtFolder = DiceArtFolder + "/D12";
        private const string D12ModelFolder = D12ArtFolder + "/Models";
        private const string D12TextureFolder = D12ArtFolder + "/Textures";
        private const string D12MaterialFolder = D12ArtFolder + "/Materials";
        private const string D12PrefabFolder = "Assets/MazeParty/Prefabs/Board/Dice";
        private const string D12ModelPath = D12ModelFolder + "/Dice_d12.fbx";
        private const string D12AlbedoPath =
            D12TextureFolder + "/D12_White_Albedo.png";
        private const string D12NormalPath =
            D12TextureFolder + "/D12_White_Normal.png";
        private const string D12OcclusionPath = D12TextureFolder + "/D12_AO.png";
        private const string D12MaterialPath =
            D12MaterialFolder + "/D12Tintable.mat";
        private const string D12VisualPrefabPath =
            "Assets/MazeParty/Prefabs/Board/Dice/D12WorldDieVisual.prefab";
        internal const string NetworkWorldDiePrefabPath =
            "Assets/MazeParty/Prefabs/Board/Dice/NetworkWorldDie.prefab";
        private const float D12VisualScale = 0.3f;

        private static readonly Color[] D12PlayerColors =
        {
            new Color(0.95f, 0.25f, 0.25f),
            new Color(0.25f, 0.55f, 1f),
            new Color(0.25f, 0.85f, 0.4f),
            new Color(1f, 0.75f, 0.2f)
        };

        private const string UiFolder = "Assets/MazeParty/Prefabs/Board";
        private const string UiPrefabFolder = "Assets/MazeParty/Prefabs/Board/UI";
        private const string BoardCanvasModuleFolder =
            "Assets/MazeParty/Prefabs/Board/UI/Modules";
        internal const string BoardCanvasPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";
        internal const string BoardFlowTestToolsPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/Dev/BoardFlowTestTools.prefab";
        private const string TestbedPath =
            "Assets/MazeParty/Scenes/Board/Dev/BoardFlowTestbed.unity";
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

        [MenuItem("MazeParty/UI/Remove Obsolete Board HUD Texts")]
        public static void RemoveObsoleteBoardHudTexts()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Board HUD migration requires Edit Mode.");
            }

            var prefab = BoardCanvasSplitProjectSetup.EnsureInstalled();
            BoardCanvasSplitProjectSetup.Validate(prefab);
            Debug.Log(
                "The split BoardCanvas contract is valid; obsolete monolithic " +
                "HUD migration is no longer required.");
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
            EnsureBoardResourceTransferPrefabs();
            ConfigureBoardEnvironment();
            CreateLighting();
            CreateBoardBackdrop(materials.Backdrop);
            CreateRuntimeTerrainBuildPlaceholder();
            var topology = CreateTopology(materials);
            var cameras = CreateCameraRig();
            CreateBoardCanvas(cameras);
            BoardHitFlashProjectSetup.EnsureSceneInstance(scene);
            MinigameResultCanvasProjectSetup.EnsureSceneInstance(scene);
            AwardCeremonyProjectSetup.EnsureSceneInstances(scene);

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
            MinigameStartCountdownProjectSetup.EnsureBoardSceneInstance();
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

            // The local flow testbed stops before the network-owned award
            // ceremony. Keep only the UI it can actually drive so its scene
            // remains a focused, prefab-only development surface.
            AwardCeremonyCanvasBindings awardCanvas = null;
            var testbedRoots = scene.GetRootGameObjects();
            for (var rootIndex = 0;
                 rootIndex < testbedRoots.Length && awardCanvas == null;
                 rootIndex++)
            {
                awardCanvas = testbedRoots[rootIndex]
                    .GetComponentInChildren<AwardCeremonyCanvasBindings>(true);
            }
            if (awardCanvas != null)
            {
                var awardRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(
                    awardCanvas.gameObject) ?? awardCanvas.gameObject;
                UnityEngine.Object.DestroyImmediate(awardRoot);
            }

            var networkView = canvas.GetComponent<BoardFlowView>();
            if (networkView == null)
            {
                throw new InvalidOperationException(
                    "Board Canvas prefab instance is missing BoardFlowView.");
            }
            networkView.enabled = false;
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
            controller.radius = PlayerAvatarVisual.MovementControllerRadius;
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
                remoteController.radius =
                    PlayerAvatarVisual.MovementControllerRadius;
                marker.AddComponent<PlayerAvatarVisual>();
                remoteController.enabled = false;
                var remoteWalls = marker.AddComponent<PlayerBoardBoundaryWalls>();
                remoteWalls.Configure(i, remoteController, topology);
                remoteWalls.SetPresentationVisible(false);
            }

            var boardBindings = canvas.GetComponent<BoardCanvasBindings>();
            var resultBindings =
                MinigameResultCanvasProjectSetup.EnsureSceneInstance(scene);
            if (boardBindings == null || !boardBindings.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Board Canvas instance is missing its serialized bindings.");
            }
            var testToolBindings = InstantiateEditorTools(canvas.transform);
            var simulator = rig.AddComponent<BoardFlowLocalSimulator>();
            BoardItemProjectSetup.EnsureAssets();
            var d12VisualPrefab = EnsureD12RuntimeAssets();
            simulator.Configure(
                controller,
                eye,
                topology,
                director,
                d12VisualPrefab,
                boardBindings,
                testToolBindings,
                resultBindings);

            for (var i = 0; i < GameplayInventory.Capacity; i++)
            {
                var choiceButton = FindDescendant(canvas.transform, "ItemChoiceButton" + i);
                if (choiceButton == null)
                {
                    continue;
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
            var worldPrefabs = BoardWorldPrefabProjectSetup.EnsureAssets();
            BoardWorldPrefabProjectSetup.Bind(root.AddComponent<KeyShopWorldMarker>(), worldPrefabs);
            BoardWorldPrefabProjectSetup.Bind(root.AddComponent<ItemShopWorldMarker>(), worldPrefabs);
            BoardKillGoldProjectSetup.EnsureView(root);
            BoardShopRouteProjectSetup.EnsureView(root);
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

            BoardItemProjectSetup.EnsureAssets();
            var d12VisualPrefab = EnsureD12RuntimeAssets();
            var networkWorldDiePrefab =
                LoadOrCreateNetworkWorldDiePrefab(d12VisualPrefab);
            var dice = new NetworkWorldDie[MultiplayerConstants.MaxPlayers * 2];

            for (var index = 0; index < dice.Length; index++)
            {
                var slot = index / 2;
                var dieObject = PrefabUtility.InstantiatePrefab(
                    networkWorldDiePrefab) as GameObject;
                if (dieObject == null)
                {
                    throw new InvalidOperationException(
                        "Failed to instantiate NetworkWorldDie.prefab.");
                }

                dieObject.name = "World Die P" + (slot + 1) + " Die " + (index % 2 + 1);
                dieObject.transform.position = new Vector3(slot * 1.5f, -20f, 0f);
                var die = dieObject.GetComponent<NetworkWorldDie>();
                if (die == null || !die.HasRequiredPresentation)
                {
                    throw new InvalidOperationException(
                        "NetworkWorldDie.prefab is missing its authored contract.");
                }
                die.ConfigureSceneIdentity(slot, index % 2);
                dice[index] = die;
            }

            return dice;
        }

        private static GameObject EnsureD12RuntimeAssets()
        {
            ConfigureD12ImportSettings();
            var authored = AssetDatabase.LoadAssetAtPath<GameObject>(D12VisualPrefabPath);
            if (authored != null) return authored;
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

        private static GameObject LoadOrCreateNetworkWorldDiePrefab(
            GameObject visualPrefab)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                NetworkWorldDiePrefabPath);
            if (prefab == null)
            {
                var temporary = PrefabUtility.InstantiatePrefab(
                    visualPrefab) as GameObject;
                if (temporary == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate D12WorldDieVisual.prefab while " +
                        "seeding NetworkWorldDie.prefab.");
                }

                try
                {
                    temporary.name = "Network World Die";
                    temporary.AddComponent<NetworkObject>();
                    var networkTransform =
                        temporary.AddComponent<NetworkTransform>();
                    networkTransform.Interpolate = true;
                    var body = temporary.AddComponent<Rigidbody>();
                    body.mass = 0.8f;
                    body.linearDamping = 1.1f;
                    body.angularDamping = 1.4f;
                    body.maxAngularVelocity = 24f;
                    temporary.AddComponent<NetworkRigidbody>();

                    if (WorldDieD12Layout.FaceCount !=
                        WorldDieAuthorityModel.MaximumFace)
                    {
                        throw new InvalidOperationException(
                            "The D12 face mapping must contain exactly one " +
                            "normal per roll value.");
                    }

                    var markers = new WorldDieFaceMarker[
                        WorldDieD12Layout.FaceCount];
                    for (var faceIndex = 0;
                         faceIndex < markers.Length;
                         faceIndex++)
                    {
                        var faceValue = faceIndex +
                            WorldDieAuthorityModel.MinimumFace;
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

                        var markerObject = new GameObject(
                            "Face " + faceValue);
                        markerObject.transform.SetParent(
                            temporary.transform,
                            false);
                        markerObject.transform.localPosition = markerPosition;
                        markerObject.transform.localRotation =
                            Quaternion.FromToRotation(Vector3.up, normal);
                        var marker = markerObject.AddComponent<
                            WorldDieFaceMarker>();
                        marker.Configure(faceValue);
                        markers[faceIndex] = marker;
                    }

                    var resultObject = new GameObject("Public World Result");
                    resultObject.transform.SetParent(temporary.transform, false);
                    resultObject.transform.localScale =
                        Vector3.one / D12VisualScale;
                    var resultText = resultObject.AddComponent<TextMesh>();
                    resultText.text = "?";
                    resultText.anchor = TextAnchor.MiddleCenter;
                    resultText.alignment = TextAlignment.Center;
                    resultText.fontSize = 64;
                    resultText.characterSize = 0.045f;
                    resultText.color = Color.white;

                    var die = temporary.AddComponent<NetworkWorldDie>();
                    var tintRenderer = temporary.GetComponent<MeshRenderer>();
                    var renderers = temporary.GetComponentsInChildren<
                        Renderer>(true);
                    die.ConfigurePrefabPresentation(
                        tintRenderer,
                        renderers,
                        markers,
                        resultText,
                        D12PlayerColors);
                    BoardFeedbackAuthoring.EnsureDiceHint(temporary);
                    renderers = temporary.GetComponentsInChildren<Renderer>(true);
                    for (var index = 0; index < renderers.Length; index++)
                    {
                        renderers[index].enabled = false;
                    }
                    temporary.GetComponent<Collider>().enabled = false;

                    prefab = PrefabUtility.SaveAsPrefabAsset(
                        temporary,
                        NetworkWorldDiePrefabPath);
                    if (prefab == null)
                    {
                        throw new InvalidOperationException(
                            "Failed to save NetworkWorldDie.prefab.");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(temporary);
                }
            }

            ValidateNetworkWorldDiePrefab(prefab);
            return prefab;
        }

        private static void ValidateNetworkWorldDiePrefab(GameObject prefab)
        {
            var die = prefab != null
                ? prefab.GetComponent<NetworkWorldDie>()
                : null;
            var collider = prefab != null
                ? prefab.GetComponent<MeshCollider>()
                : null;
            var original = prefab != null
                ? PrefabUtility.GetCorrespondingObjectFromOriginalSource(prefab)
                : null;
            if (prefab == null ||
                PrefabUtility.GetPrefabAssetType(prefab) !=
                PrefabAssetType.Variant ||
                AssetDatabase.GetAssetPath(original) != D12VisualPrefabPath ||
                prefab.GetComponent<NetworkObject>() == null ||
                prefab.GetComponent<NetworkTransform>() == null ||
                prefab.GetComponent<NetworkRigidbody>() == null ||
                prefab.GetComponent<Rigidbody>() == null ||
                collider == null || !collider.convex ||
                prefab.GetComponentsInChildren<NetworkObject>(true).Length != 1 ||
                die == null || !die.HasRequiredPresentation)
            {
                throw new InvalidOperationException(
                    "NetworkWorldDie.prefab must remain a complete variant of " +
                    "D12WorldDieVisual.prefab. Repair the prefab directly; " +
                    "setup will not overwrite authored design.");
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
            if (!modelImporter.HasPreBakeCollisionMesh(isConvex: true))
            {
                // The authored D12 visual uses this imported mesh in a convex
                // MeshCollider. Bake that collision data into player builds so
                // every process does not cook the same mesh at runtime.
                modelImporter.SetPreBakeCollisionMesh(
                    isConvex: true,
                    preBake: true);
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
            var shader = Shader.Find("MazeParty/Toon Shader FX") ??
                         Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "No supported Lit shader is available for the D12 die.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(D12MaterialPath);
            if (material != null) return material;
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
            return BoardWorldPrefabProjectSetup.CreateTile(parent, coordinate, type, index, GridToWorld(coordinate));
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
            output.clearFlags = CameraClearFlags.Skybox;
            output.backgroundColor = new Color(0.018f, 0.026f, 0.045f);
            output.nearClipPlane = 0.05f;
            output.farClipPlane = 500f;
            BoardFeedbackAuthoring.ConfigureNightCamera(output);
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
                root.transform, "CM_MinigameFallback", boardPose.Position, boardPose.Rotation, 55f, 0);

            var director = root.AddComponent<GameplayCameraDirector>();
            director.Configure(output, firstPerson, boardCamera, minigame);
            director.SetBoardFramingAnchor(framing);
            var presenter = root.AddComponent<BoardFlowCameraPresenter>();
            presenter.Configure(director);
            presenter.ConfigureEventSplitView(BoardEventPresentationProjectSetup.EnsureSplitRig());
            presenter.ConfigureLocalPlayerMarker(
                BoardLocalPlayerMarkerProjectSetup.CreateInstance(root.transform));
            presenter.ConfigureResourceTransferAssets(
                RequireBoardResourceTransferPrefab(
                    ResourceTransferCoinPrefabPath),
                RequireBoardResourceTransferPrefab(
                    ResourceTransferKeyPrefabPath));
            presenter.ConfigureLandingEffectFeedbackAssets(
                BoardLandingEffectFeedbackProjectSetup.EnsureInstalled(),
                SharedVfxProjectSetup.EnsureLightningStrikePrefab());
            presenter.ConfigureLandingRewardAssets(
                BoardLandingEffectFeedbackProjectSetup.EnsureGiftPrefab(),
                BoardLandingEffectFeedbackProjectSetup.EnsureHealingShieldPrefab());
            BoardWorldEffectsProjectSetup.EnsureFor(root);
            return director;
        }

        private static GameObject RequireBoardResourceTransferPrefab(
            string assetPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Required board resource-transfer prefab is missing: '" +
                    assetPath + "'.");
            }

            return prefab;
        }

        private static void EnsureBoardResourceTransferPrefabs()
        {
            EnsureFolder("Assets/MazeParty/Prefabs/Board/World");
            EnsureBoardResourceTransferPrefab(
                ResourceTransferCoinPrefabPath,
                ResourceTransferCoinSourcePath,
                "BoardEventCoin",
                0.32f);
            EnsureBoardResourceTransferPrefab(
                ResourceTransferKeyPrefabPath,
                ResourceTransferKeySourcePath,
                "BoardEventKey",
                0.5f);
        }

        private static void EnsureBoardResourceTransferPrefab(
            string assetPath,
            string sourcePath,
            string objectName,
            float visualScale)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) != null)
            {
                return;
            }

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Required BTM resource model is missing: '" +
                    sourcePath + "'.");
            }

            var root = new GameObject(objectName);
            try
            {
                var visual = PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (visual == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate BTM resource model: '" +
                        sourcePath + "'.");
                }

                visual.name = "Visual";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one * visualScale;
                var behaviours = visual.GetComponentsInChildren<MonoBehaviour>(true);
                for (var index = 0; index < behaviours.Length; index++)
                {
                    behaviours[index].enabled = false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureBoardEnvironment()
        {
            var skybox = AssetDatabase.LoadAssetAtPath<Material>(BoardSkyboxPath);
            if (skybox == null)
            {
                throw new InvalidOperationException(
                    $"Required board skybox material is missing: '{BoardSkyboxPath}'.");
            }

            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            BoardFeedbackAuthoring.ConfigureNightAmbient();
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

        private static void CreateBoardCanvas(GameplayCameraDirector cameraDirector)
        {
            var prefab = LoadOrCreateBoardCanvasPrefab();
            var root = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (root == null)
            {
                throw new InvalidOperationException(
                    "Board Canvas prefab could not be instantiated.");
            }

            root.transform.localScale = prefab.transform.localScale;
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
            var prefab = BoardCanvasSplitProjectSetup.EnsureInstalled();
            MinigameResultCanvasProjectSetup.EnsurePrefabMigrated();
            BoardCanvasSplitProjectSetup.Validate(prefab);
            return prefab;
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
            text.fontStyle = FontStyle.Normal;
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
            light.color = new Color(0.57f, 0.69f, 1f);
            light.intensity = 0.10f;
            light.shadows = LightShadows.Soft;
            RenderSettings.sun = light;
            BoardFeedbackAuthoring.EnsureNightVolume();
        }

        private static void CreateBoardBackdrop(Material material)
        {
            var backdrop = (GameObject)PrefabUtility.InstantiatePrefab(BoardWorldPrefabProjectSetup.EnsureBackdrop());
            backdrop.name = "Board Backdrop (No Gameplay Collision)";
            backdrop.transform.position = new Vector3(0f, -0.42f, 0f);
        }

        private static void CreateRuntimeTerrainBuildPlaceholder()
        {
            // Both production terrains are loaded from Resources prefabs at runtime.
            // Unity only packages the engine-side Terrain rendering resources when a
            // Terrain component exists in an enabled build scene, so retain one
            // enabled component on an inactive GameObject as the build-time
            // inclusion anchor. This matches Unity's documented placeholder form.
            var terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(
                ForestGroundAuthoring.TerrainDataPath);
            if (terrainData == null)
            {
                throw new InvalidOperationException(
                    "The runtime Terrain build placeholder requires " +
                    ForestGroundAuthoring.TerrainDataPath + ".");
            }

            var renderPipeline = GraphicsSettings.currentRenderPipeline;
            if (renderPipeline == null ||
                renderPipeline.defaultTerrainMaterial == null)
            {
                throw new InvalidOperationException(
                    "The active render pipeline does not provide a default terrain material.");
            }

            var placeholder = new GameObject(RuntimeTerrainResourcesName);
            var terrain = placeholder.AddComponent<Terrain>();
            terrain.terrainData = terrainData;
            terrain.materialTemplate = ToonShaderProjectSetup.GetTerrainMaterial();
            terrain.drawInstanced = false;
            terrain.drawTreesAndFoliage = false;
            placeholder.SetActive(false);
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
            if (material != null) return material; // Preserve authored material design.
            if (material == null)
            {
                var shader = Shader.Find("MazeParty/Toon Shader FX") ?? Shader.Find("Standard");
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
            EnsureFolder(BoardCanvasModuleFolder);
            EnsureFolder(UiPrefabFolder + "/Dev");
            EnsureFolder(MaterialFolder);
            EnsureFolder(DiceArtFolder);
            EnsureFolder(D12ArtFolder);
            EnsureFolder(D12ModelFolder);
            EnsureFolder(D12TextureFolder);
            EnsureFolder(D12MaterialFolder);
            EnsureFolder(D12PrefabFolder);
            EnsureFolder("Assets/MazeParty/Prefabs/Board/World");
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

        private static BoardFlowTestToolsBindings InstantiateEditorTools(
            Transform canvas)
        {
            var prefab = LoadOrCreateEditorToolsPrefab();
            var instance = PrefabUtility.InstantiatePrefab(
                prefab,
                canvas) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "BoardFlowTestTools.prefab could not be instantiated.");
            }

            var bindings = instance.GetComponent<BoardFlowTestToolsBindings>();
            if (bindings == null || !bindings.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "BoardFlowTestTools prefab instance has incomplete " +
                    "serialized bindings.");
            }
            return bindings;
        }

        private static GameObject LoadOrCreateEditorToolsPrefab()
        {
            EnsureFolder(UiPrefabFolder + "/Dev");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardFlowTestToolsPrefabPath);
            if (prefab != null)
            {
                prefab = MigrateEditorToolsBindingsIfMissing(prefab);
                ValidateEditorToolsPrefab(prefab);
                return prefab;
            }

            var font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Unity built-in LegacyRuntime.ttf font could not be loaded.");
            }

            var template = CreateEditorToolsTemplate(font);
            try
            {
                prefab = PrefabUtility.SaveAsPrefabAsset(
                    template,
                    BoardFlowTestToolsPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(template);
            }
            ValidateEditorToolsPrefab(prefab);
            return prefab;
        }

        private static GameObject MigrateEditorToolsBindingsIfMissing(
            GameObject prefab)
        {
            if (prefab.GetComponent<BoardFlowTestToolsBindings>() != null)
            {
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(
                BoardFlowTestToolsPrefabPath);
            try
            {
                var bindings =
                    contents.AddComponent<BoardFlowTestToolsBindings>();
                ConfigureEditorToolsBindings(contents, bindings);
                PrefabUtility.SaveAsPrefabAsset(
                    contents,
                    BoardFlowTestToolsPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardFlowTestToolsPrefabPath);
        }

        private static void ValidateEditorToolsPrefab(GameObject prefab)
        {
            if (prefab == null || prefab.GetComponent<RectTransform>() == null)
            {
                throw new InvalidOperationException(
                    "BoardFlowTestTools.prefab is missing its UI root. " +
                    "Repair the prefab directly so its design is preserved.");
            }

            var bindings =
                prefab.GetComponent<BoardFlowTestToolsBindings>();
            if (bindings == null || !bindings.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "BoardFlowTestTools.prefab has incomplete serialized " +
                    "bindings. Repair the prefab directly so setup preserves " +
                    "its authored design.");
            }
        }

        private static GameObject CreateEditorToolsTemplate(Font font)
        {
            var root = new GameObject(
                "BoardFlowTestTools",
                typeof(RectTransform));
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var panel = CreatePanel("Editor Flow Tools", root.transform, new Vector2(1f, 1f),
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
            var bindings = root.AddComponent<BoardFlowTestToolsBindings>();
            ConfigureEditorToolsBindings(root, bindings);
            return root;
        }

        private static void ConfigureEditorToolsBindings(
            GameObject root,
            BoardFlowTestToolsBindings bindings)
        {
            var stageFight = RequireEditorToolButton(
                root,
                "EditorStageFightButton");
            var finishAction = RequireEditorToolButton(
                root,
                "EditorFinishActionButton");
            var speed = RequireEditorToolButton(root, "EditorSpeedButton");
            var speedLabel = speed.GetComponentInChildren<Text>(true);
            if (speedLabel == null)
            {
                throw new InvalidOperationException(
                    "EditorSpeedButton requires its prefab-authored label.");
            }

            bindings.Configure(
                stageFight,
                finishAction,
                speed,
                RequireEditorToolButton(root, "EditorPauseButton"),
                RequireEditorToolButton(root, "EditorDamagePlayerButton"),
                RequireEditorToolButton(root, "EditorAddGoldButton"),
                RequireEditorToolButton(root, "EditorBuyKeyButton"),
                speedLabel);
        }

        private static Button RequireEditorToolButton(
            GameObject root,
            string name)
        {
            var target = FindDescendant(root.transform, name);
            var button = target != null ? target.GetComponent<Button>() : null;
            if (button == null)
            {
                throw new InvalidOperationException(
                    "BoardFlowTestTools prefab migration could not find '" +
                    name + "'. Repair the prefab directly.");
            }
            return button;
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

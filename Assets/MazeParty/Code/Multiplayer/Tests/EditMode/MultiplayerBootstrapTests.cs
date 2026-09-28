using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MultiplayerBootstrapTests
    {
        private const string BootstrapScenePath =
            "Assets/MazeParty/Scenes/Multiplayer/OnlineBootstrap.unity";
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";
        private const string MinefieldScenePath =
            "Assets/MazeParty/Scenes/Minigames/Minefield/Minefield.unity";
        private const string WrongWayScenePath =
            "Assets/MazeParty/Scenes/Minigames/WrongWay/WrongWay.unity";
        private const string RedLightGreenLightScenePath =
            "Assets/MazeParty/Scenes/Minigames/RedLightGreenLight/RedLightGreenLight.unity";
        private const string StableFootingScenePath =
            "Assets/MazeParty/Scenes/Minigames/StableFooting/StableFooting.unity";
        private const string BalloonBlowScenePath =
            "Assets/MazeParty/Scenes/Minigames/BalloonBlow/BalloonBlow.unity";
        private const string GiftGrabScenePath =
            "Assets/MazeParty/Scenes/Minigames/GiftGrab/GiftGrab.unity";
        private const string TerritoryPaintScenePath =
            "Assets/MazeParty/Scenes/Minigames/TerritoryPaint/TerritoryPaint.unity";
        private const string TagChaseScenePath =
            "Assets/MazeParty/Scenes/Minigames/TagChase/TagChase.unity";
        private const string RaceScenePath =
            "Assets/MazeParty/Scenes/Minigames/Race/Race.unity";
        private const string SequenceMemoryScenePath =
            "Assets/MazeParty/Scenes/Minigames/SequenceMemory/SequenceMemory.unity";
        private const string BouncingBallsScenePath =
            "Assets/MazeParty/Scenes/Minigames/BouncingBalls/BouncingBalls.unity";
        private const string BombPassingScenePath =
            "Assets/MazeParty/Scenes/Minigames/BombPassing/BombPassing.unity";
        private const string SnowySpinScenePath =
            "Assets/MazeParty/Scenes/Minigames/SnowySpin/SnowySpin.unity";
        private const string ArenaCombatScenePath =
            "Assets/MazeParty/Scenes/Minigames/ArenaCombat/ArenaCombat.unity";
        private const string CliffBarrageScenePath =
            "Assets/MazeParty/Scenes/Minigames/CliffBarrage/CliffBarrage.unity";
        private const string PlayerPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/NetworkPlayer.prefab";
        private const string D12VisualPrefabPath =
            "Assets/MazeParty/Prefabs/Board/Dice/D12WorldDieVisual.prefab";
        private const string NetworkWorldDiePrefabPath =
            "Assets/MazeParty/Prefabs/Board/Dice/NetworkWorldDie.prefab";
        private const string D12ModelPath =
            "Assets/MazeParty/Art/Dice/D12/Models/Dice_d12.fbx";

        [Test]
        public void SessionRules_AssignLowestSeatAndRequireFourUniqueReadyPlayers()
        {
            Assert.That(
                SessionRules.FindLowestAvailableSlot(new[] { 0, 2, 3 }),
                Is.EqualTo(1));
            Assert.That(
                SessionRules.FindLowestAvailableSlot(new[] { 0, 1, 2, 3 }),
                Is.EqualTo(-1));

            var ready = CreateReadyPlayers();
            Assert.That(SessionRules.CanStart(ready), Is.True);
            Assert.That(SessionRules.CanStart(ready.Take(3).ToList()), Is.False);

            var duplicate = CreateReadyPlayers();
            duplicate[3] = new OnlinePlayerSnapshot(
                "p4",
                "Four",
                2,
                true,
                false);
            Assert.That(SessionRules.CanStart(duplicate), Is.False);

            var unready = CreateReadyPlayers();
            unready[1] = new OnlinePlayerSnapshot(
                "p2",
                "Two",
                1,
                false,
                false);
            Assert.That(SessionRules.CanStart(unready), Is.False);
        }

        [Test]
        public void BoardMapStartup_PermanentFailureEndsMatchWithoutReadinessWait()
        {
            var cases = new[]
            {
                new
                {
                    MapReady = false,
                    AllPlayersReady = false,
                    Failure = "Map catalog is invalid.",
                    Expected = BoardMapStartupDisposition.FailMatch
                },
                new
                {
                    MapReady = true,
                    AllPlayersReady = true,
                    Failure = "Saved map version is unavailable.",
                    Expected = BoardMapStartupDisposition.FailMatch
                },
                new
                {
                    MapReady = true,
                    AllPlayersReady = true,
                    Failure = string.Empty,
                    Expected = BoardMapStartupDisposition.Ready
                },
                new
                {
                    MapReady = true,
                    AllPlayersReady = false,
                    Failure = string.Empty,
                    Expected = BoardMapStartupDisposition.WaitForReadiness
                },
                new
                {
                    MapReady = false,
                    AllPlayersReady = true,
                    Failure = string.Empty,
                    Expected = BoardMapStartupDisposition.WaitForReadiness
                }
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    BoardMapStartupPolicy.Evaluate(
                        testCase.MapReady,
                        testCase.AllPlayersReady,
                        testCase.Failure),
                    Is.EqualTo(testCase.Expected),
                    $"mapReady={testCase.MapReady}, " +
                    $"allPlayersReady={testCase.AllPlayersReady}, " +
                    $"failure='{testCase.Failure}'");
            }
        }

        [Test]
        public void OnlineBootstrapScene_HasBuildNetworkPlayerAndUiContracts()
        {
            var enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            Assert.That(enabledScenes.Length, Is.GreaterThanOrEqualTo(17));
            Assert.That(
                enabledScenes.Take(17),
                Is.EqualTo(new[]
                {
                    BootstrapScenePath,
                    BoardScenePath,
                    MinefieldScenePath,
                    WrongWayScenePath,
                    RedLightGreenLightScenePath,
                    StableFootingScenePath,
                    BalloonBlowScenePath,
                    GiftGrabScenePath,
                    TerritoryPaintScenePath,
                    TagChaseScenePath,
                    RaceScenePath,
                    SequenceMemoryScenePath,
                    BouncingBallsScenePath,
                    BombPassingScenePath,
                    SnowySpinScenePath,
                    ArenaCombatScenePath,
                    CliffBarrageScenePath
                }));

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PlayerPrefabPath);
            Assert.That(playerPrefab, Is.Not.Null, PlayerPrefabPath);
            Assert.That(
                playerPrefab.GetComponent<NetworkObject>(),
                Is.Not.Null);
            Assert.That(
                playerPrefab.GetComponent<
                    Unity.Netcode.Components.NetworkTransform>(),
                Is.Not.Null);
            Assert.That(
                playerPrefab.GetComponent<CharacterController>(),
                Is.Not.Null);
            Assert.That(
                playerPrefab.GetComponent<NetworkPlayerAvatar>(),
                Is.Not.Null);
            Assert.That(
                playerPrefab.GetComponent<PlayerAvatarVisual>(),
                Is.Not.Null);
            var boundaryWalls =
                playerPrefab.GetComponent<PlayerBoardBoundaryWalls>();
            Assert.That(boundaryWalls, Is.Not.Null);
            Assert.That(boundaryWalls.WorldPrefabs, Is.Not.Null);
            Assert.That(boundaryWalls.WorldPrefabs.HasRequiredReferences, Is.True);

            var scene = SceneManager.GetSceneByPath(BootstrapScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    BootstrapScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                var manager = roots
                    .SelectMany(root =>
                        root.GetComponentsInChildren<NetworkManager>(true))
                    .SingleOrDefault();
                Assert.That(manager, Is.Not.Null);
                var transport = manager.GetComponent<UnityTransport>();
                Assert.That(transport, Is.Not.Null);
                Assert.That(transport.DisconnectTimeoutMS, Is.EqualTo(15000));
                Assert.That(
                    manager.NetworkConfig.EnableSceneManagement,
                    Is.True);
                Assert.That(
                    manager.NetworkConfig.PlayerPrefab,
                    Is.SameAs(playerPrefab));

                var lobby = roots
                    .SelectMany(root =>
                        root.GetComponentsInChildren<OnlineLobbyView>(true))
                    .SingleOrDefault();
                Assert.That(lobby, Is.Not.Null);
                Assert.That(lobby.HasRequiredReferences, Is.True);
                Assert.That(
                    lobby.PlayerRowCount,
                    Is.EqualTo(MultiplayerConstants.MaxPlayers));

                var tower = roots
                    .SelectMany(root => root.GetComponentsInChildren<
                        MinigameScheduleTowerView>(true))
                    .SingleOrDefault();
                Assert.That(tower, Is.Not.Null);
                Assert.That(tower.HasRequiredReferences, Is.True);
                Assert.That(
                    tower.BlockCount,
                    Is.EqualTo(
                        MinigameScheduleTowerView.MaximumVisibleBlocks));

                var menus = roots
                    .SelectMany(root =>
                        root.GetComponentsInChildren<GameMenuView>(true))
                    .ToArray();
                Assert.That(menus, Has.Length.EqualTo(1));
                Assert.That(menus[0].Bindings, Is.Not.Null);
                Assert.That(menus[0].Bindings.HasRequiredReferences, Is.True);

                var eventSystems = roots
                    .SelectMany(root =>
                        root.GetComponentsInChildren<EventSystem>(true))
                    .ToArray();
                Assert.That(eventSystems, Has.Length.EqualTo(1));
                Assert.That(
                    eventSystems[0]
                        .GetComponent<InputSystemUIInputModule>(),
                    Is.Not.Null);
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void BoardScene_HasStableAuthoritativeDiceAndShopContracts()
        {
            var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                D12VisualPrefabPath);
            Assert.That(visualPrefab, Is.Not.Null, D12VisualPrefabPath);
            var expectedMesh =
                visualPrefab.GetComponent<MeshFilter>()?.sharedMesh;
            Assert.That(expectedMesh, Is.Not.Null);
            var meshCollider = visualPrefab.GetComponent<MeshCollider>();
            Assert.That(meshCollider, Is.Not.Null);
            Assert.That(meshCollider.convex, Is.True);
            var networkDiePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                NetworkWorldDiePrefabPath);
            Assert.That(
                networkDiePrefab,
                Is.Not.Null,
                NetworkWorldDiePrefabPath);
            Assert.That(
                PrefabUtility.GetPrefabAssetType(networkDiePrefab),
                Is.EqualTo(PrefabAssetType.Variant));
            Assert.That(
                AssetDatabase.GetAssetPath(
                    PrefabUtility.GetCorrespondingObjectFromOriginalSource(
                        networkDiePrefab)),
                Is.EqualTo(D12VisualPrefabPath));
            Assert.That(
                networkDiePrefab.GetComponent<NetworkWorldDie>()
                    .HasRequiredPresentation,
                Is.True);
            Assert.That(
                networkDiePrefab.GetComponentsInChildren<NetworkObject>(true),
                Has.Length.EqualTo(1));

            var modelImporter = AssetImporter.GetAtPath(D12ModelPath) as ModelImporter;
            Assert.That(modelImporter, Is.Not.Null, D12ModelPath);
            Assert.That(
                modelImporter.HasPreBakeCollisionMesh(isConvex: true),
                Is.True,
                "The convex D12 collision mesh must be pre-baked for player builds.");
            var expectedFaces = Enumerable.Range(
                    WorldDieAuthorityModel.MinimumFace,
                    WorldDieD12Layout.FaceCount)
                .ToArray();

            var scene = SceneManager.GetSceneByPath(BoardScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    BoardScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                var matchState = roots
                    .SelectMany(root => root.GetComponentsInChildren<
                        NetworkMatchState>(true))
                    .SingleOrDefault();
                Assert.That(matchState, Is.Not.Null);
                AssertStableInSceneNetworkObject(matchState.gameObject);

                var dice = roots
                    .SelectMany(root => root.GetComponentsInChildren<
                        NetworkWorldDie>(true))
                    .OrderBy(die => die.ConfiguredSlot).ThenBy(die => die.DieIndex)
                    .ToArray();
                Assert.That(
                    dice,
                    Has.Length.EqualTo(MultiplayerConstants.MaxPlayers * 2));
                Assert.That(dice.Select(die => die.ConfiguredSlot), Is.EqualTo(new[] { 0, 0, 1, 1, 2, 2, 3, 3 }));
                Assert.That(dice.Select(die => die.DieIndex), Is.EqualTo(new[] { 0, 1, 0, 1, 0, 1, 0, 1 }));
                Assert.That(dice.Select(d => new SerializedObject(d.GetComponent<NetworkObject>()).FindProperty("GlobalObjectIdHash").longValue).Distinct().Count(), Is.EqualTo(8));
                foreach (var die in dice)
                {
                    AssertStableInSceneNetworkObject(die.gameObject);
                    Assert.That(
                        PrefabUtility.GetPrefabInstanceStatus(die.gameObject),
                        Is.EqualTo(PrefabInstanceStatus.Connected));
                    Assert.That(
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                            die.gameObject),
                        Is.EqualTo(NetworkWorldDiePrefabPath));
                    Assert.That(
                        PrefabUtility.GetAddedComponents(die.gameObject),
                        Is.Empty,
                        die.name);
                    Assert.That(
                        PrefabUtility.GetAddedGameObjects(die.gameObject),
                        Is.Empty,
                        die.name);
                    Assert.That(die.HasRequiredPresentation, Is.True, die.name);
                    Assert.That(
                        die.GetComponent<MeshFilter>()?.sharedMesh,
                        Is.SameAs(expectedMesh));

                    var markers = die.GetComponentsInChildren<
                            WorldDieFaceMarker>(true)
                        .OrderBy(marker => marker.Value)
                        .ToArray();
                    Assert.That(
                        markers.Select(marker => marker.Value),
                        Is.EqualTo(expectedFaces));
                    foreach (var marker in markers)
                    {
                        Assert.That(
                            WorldDieD12Layout.TryGetLocalNormal(
                                marker.Value,
                                out var expectedNormal),
                            Is.True);
                        Assert.That(
                            Vector3.Dot(
                                marker.LocalNormal,
                                expectedNormal),
                            Is.GreaterThan(0.99999f));
                    }
                }

                Assert.That(
                    roots.SelectMany(root => root.GetComponentsInChildren<
                        NetworkWorldDiceCoordinator>(true)).ToArray(),
                    Has.Length.EqualTo(1));
                Assert.That(
                    roots.SelectMany(root => root.GetComponentsInChildren<
                        KeyShopWorldMarker>(true)).ToArray(),
                    Has.Length.EqualTo(1));
                Assert.That(
                    roots.SelectMany(root => root.GetComponentsInChildren<
                            BoardTile>(true))
                        .Count(tile =>
                            tile.TileType == BoardTileType.KeyShop),
                    Is.Zero);
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void PlayerInputRules_SanitizeProfileAndChooseAvailableColor()
        {
            var displayName = PlayerProfilePreferences.SanitizeDisplayName(
                "미로파티플레이어이름테스트입니다추가문자");
            Assert.That(displayName, Does.StartWith("미로파티"));
            Assert.That(displayName.Length, Is.EqualTo(16));

            var appearance = PlayerAppearanceState.FromColor(
                Color.cyan,
                5,
                4,
                99,
                8);
            Assert.That(appearance.EyeId, Is.Zero);
            Assert.That(appearance.MouthId, Is.Zero);
            Assert.That(appearance.HatId, Is.Zero);
            Assert.That(appearance.OutfitId, Is.Zero);
            Assert.That(
                (Color32)appearance.BodyColor,
                Is.EqualTo(LobbyColorPalette.GetColor(4)));
            Assert.That(
                LobbyColorPalette.FindFirstAvailable(0b0010_1111),
                Is.EqualTo(4));
        }

        [Test]
        public void TraversalInitialization_DoesNotRebindActiveGateCrossing()
        {
            var avatarObject = new GameObject("Traversal Regression Avatar");
            var tileObject = new GameObject("Logical Source Tile");

            try
            {
                var avatar = avatarObject.AddComponent<NetworkPlayerAvatar>();
                var sourceTile = tileObject.AddComponent<BoardTile>();
                sourceTile.Configure(Vector2Int.zero, BoardTileType.Normal);

                const BindingFlags privateInstance =
                    BindingFlags.Instance | BindingFlags.NonPublic;
                var traversalField = typeof(NetworkPlayerAvatar).GetField(
                    "_traversal",
                    privateInstance);
                var ensureMethod = typeof(NetworkPlayerAvatar).GetMethod(
                    "EnsureTraversalInitialized",
                    privateInstance);
                Assert.That(traversalField, Is.Not.Null);
                Assert.That(ensureMethod, Is.Not.Null);

                var traversal =
                    (BoardTraversalState)traversalField.GetValue(avatar);
                traversal.Begin(sourceTile, 4);
                avatarObject.transform.position = new Vector3(8f, 0f, 0f);
                ensureMethod.Invoke(avatar, null);

                Assert.That(traversal.CurrentTile, Is.SameAs(sourceTile));
                Assert.That(traversal.RemainingMoves, Is.EqualTo(4));
            }
            finally
            {
                Object.DestroyImmediate(avatarObject);
                Object.DestroyImmediate(tileObject);
            }
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void AuthoredBoardStartPose_ClampsControllerInsideSmallFootprint(
            int sides)
        {
            var mapObject = new GameObject("Start Pose Map");
            var topologyObject = new GameObject("Start Pose Topology");
            var tileObject = new GameObject("Assigned Start");
            var anchorObject = new GameObject("Player Spawn 1");
            var controllerObject = new GameObject("Start Pose Controller");
            try
            {
                topologyObject.transform.SetParent(mapObject.transform);
                tileObject.transform.SetParent(topologyObject.transform);
                anchorObject.transform.SetParent(mapObject.transform);

                var mapRoot = mapObject.AddComponent<BoardMapRoot>();
                var topology = topologyObject.AddComponent<BoardTopology>();
                var start = tileObject.AddComponent<BoardTile>();
                start.Configure(Vector2Int.zero, BoardTileType.Start);
                start.transform.SetPositionAndRotation(
                    new Vector3(4f, 0f, -3f),
                    Quaternion.Euler(0f, 31f, 0f));
                var vertices = new Vector2[sides];
                for (var index = 0; index < sides; index++)
                {
                    var angle = Mathf.PI * 2f * index / sides;
                    vertices[index] = new Vector2(
                        Mathf.Cos(angle) * 1.5f,
                        Mathf.Sin(angle) * 1.5f);
                }

                var footprint = tileObject.AddComponent<BoardTileFootprint>();
                footprint.Configure(vertices);
                topology.Configure(
                    new[] { start },
                    System.Array.Empty<BoardGate>());

                anchorObject.transform.SetPositionAndRotation(
                    start.transform.TransformPoint(new Vector3(1.15f, 0f, 0f)) +
                    start.transform.up.normalized,
                    start.transform.rotation * Quaternion.Euler(0f, 17f, 0f));
                var starts = new BoardTile[PlayerSlotRules.Count];
                starts[0] = start;
                var anchors = new Transform[PlayerSlotRules.Count];
                anchors[0] = anchorObject.transform;
                mapRoot.Configure(
                    null,
                    topology,
                    null,
                    null,
                    null,
                    start,
                    starts,
                    anchors);

                var controller = controllerObject.AddComponent<CharacterController>();
                controller.center = Vector3.zero;
                controller.radius = 0.5f;
                controller.height = 2f;

                Assert.That(footprint.CanContainInset(0.52f), Is.True);
                Assert.That(
                    start.ContainsHorizontalPoint(anchorObject.transform.position),
                    Is.True);
                var usedAuthoredPose =
                    NetworkPlayerAvatar.TryResolveAuthoredBoardStartPose(
                        topology,
                        mapRoot,
                        0,
                        start,
                        controller,
                        out var position,
                        out var rotation);
                var expected = start.GetClosestPointInside(
                    anchorObject.transform.position,
                    0.52f);

                Assert.That(usedAuthoredPose, Is.True);
                Assert.That(Vector3.Distance(position, expected), Is.LessThan(0.0001f));
                Assert.That(
                    Vector3.Distance(position, anchorObject.transform.position),
                    Is.GreaterThan(0.01f));
                Assert.That(start.ContainsHorizontalPoint(position), Is.True);
                Assert.That(
                    Vector3.Dot(
                        position - start.WorldCenter,
                        start.transform.up.normalized),
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(
                    Quaternion.Angle(rotation, anchorObject.transform.rotation),
                    Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(mapObject);
            }
        }

        [Test]
        public void AuthoredBoardStartPose_FallsBackForLegacyOutsideMismatchAndHeight()
        {
            var mapObject = new GameObject("Start Pose Validation Map");
            var topologyObject = new GameObject("Start Pose Validation Topology");
            var firstObject = new GameObject("Player One Start");
            var secondObject = new GameObject("Different Start");
            var anchorObject = new GameObject("Player Spawn 1");
            var controllerObject = new GameObject("Start Pose Validation Controller");
            try
            {
                topologyObject.transform.SetParent(mapObject.transform);
                firstObject.transform.SetParent(topologyObject.transform);
                secondObject.transform.SetParent(topologyObject.transform);
                anchorObject.transform.SetParent(mapObject.transform);

                var mapRoot = mapObject.AddComponent<BoardMapRoot>();
                var topology = topologyObject.AddComponent<BoardTopology>();
                var first = firstObject.AddComponent<BoardTile>();
                first.Configure(Vector2Int.zero, BoardTileType.Start);
                var second = secondObject.AddComponent<BoardTile>();
                second.Configure(Vector2Int.right, BoardTileType.Normal);
                second.transform.position = Vector3.right * 12f;
                topology.Configure(
                    new[] { first, second },
                    System.Array.Empty<BoardGate>());

                var starts = new BoardTile[PlayerSlotRules.Count];
                starts[0] = first;
                var anchors = new Transform[PlayerSlotRules.Count];
                anchors[0] = anchorObject.transform;
                mapRoot.Configure(
                    null,
                    topology,
                    null,
                    null,
                    null,
                    first,
                    starts,
                    anchors);
                var controller = controllerObject.AddComponent<CharacterController>();
                controller.center = Vector3.zero;
                controller.radius = 0.5f;
                controller.height = 2f;

                AssertFallback(null, first, first.GetRecoveryCenter(1f));

                anchorObject.transform.position = second.GetRecoveryCenter(1f);
                AssertFallback(mapRoot, first, first.GetRecoveryCenter(1f));

                anchorObject.transform.position = first.GetRecoveryCenter(1f);
                AssertFallback(mapRoot, second, second.GetRecoveryCenter(1f));

                anchorObject.transform.position = first.GetRecoveryCenter(0f);
                AssertFallback(mapRoot, first, first.GetRecoveryCenter(1f));

                var undersizedFootprint = firstObject.AddComponent<BoardTileFootprint>();
                undersizedFootprint.Configure(new[]
                {
                    new Vector2(-0.25f, -0.25f),
                    new Vector2(0.25f, -0.25f),
                    new Vector2(0.25f, 0.25f),
                    new Vector2(-0.25f, 0.25f)
                });
                anchorObject.transform.position = first.GetRecoveryCenter(1f);
                Assert.That(undersizedFootprint.CanContainInset(0.52f), Is.False);
                AssertFallback(mapRoot, first, first.GetRecoveryCenter(1f));

                void AssertFallback(
                    BoardMapRoot candidateRoot,
                    BoardTile expectedStart,
                    Vector3 expectedPosition)
                {
                    var usedAuthoredPose =
                        NetworkPlayerAvatar.TryResolveAuthoredBoardStartPose(
                            topology,
                            candidateRoot,
                            0,
                            expectedStart,
                            controller,
                            out var position,
                            out var rotation);
                    Assert.That(usedAuthoredPose, Is.False);
                    Assert.That(
                        Vector3.Distance(position, expectedPosition),
                        Is.LessThan(0.0001f));
                    Assert.That(
                        Quaternion.Angle(rotation, Quaternion.identity),
                        Is.LessThan(0.0001f));
                }
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(mapObject);
            }
        }

        [Test]
        public void MinigameRuntimeRegistration_AlignsWithCatalog()
        {
            var runtimeIds =
                MinigameRuntimeRegistry.RegisteredIds.ToArray();
            var catalogIds = MinigameCatalog.RegisteredMinigames
                .Select(definition => definition.Id)
                .ToArray();

            Assert.That(runtimeIds, Has.Length.EqualTo(catalogIds.Length));
            Assert.That(runtimeIds, Is.EquivalentTo(catalogIds));
        }

        [Test]
        public void RegisteredMinigames_ExposeTheirOwnInitialCountdown()
        {
            var registeredAdapters = typeof(MinigameRuntimeRegistry).GetField(
                "RegisteredAdapters",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(registeredAdapters, Is.Not.Null);

            var adapters = (System.Array)registeredAdapters.GetValue(null);
            Assert.That(adapters.Length,
                Is.EqualTo(MinigameCatalog.RegisteredMinigames.Count));
            foreach (var adapter in adapters)
            {
                var method = adapter.GetType().GetMethod(
                    "TryGetInitialCountdown",
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.That(method, Is.Not.Null);
                Assert.That(method.DeclaringType,
                    Is.EqualTo(adapter.GetType()),
                    adapter.GetType().Name + " must project its own first countdown.");
            }
        }



        private static List<OnlinePlayerSnapshot> CreateReadyPlayers()
        {
            return new List<OnlinePlayerSnapshot>
            {
                new OnlinePlayerSnapshot("p1", "One", 0, true, true),
                new OnlinePlayerSnapshot("p2", "Two", 1, true, false),
                new OnlinePlayerSnapshot("p3", "Three", 2, true, false),
                new OnlinePlayerSnapshot("p4", "Four", 3, true, false)
            };
        }

        private static void AssertStableInSceneNetworkObject(GameObject target)
        {
            var networkObject = target.GetComponent<NetworkObject>();
            Assert.That(networkObject, Is.Not.Null, target.name);
            Assert.That(networkObject.InScenePlaced, Is.True, target.name);
            Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u), target.name);
        }


    }
}

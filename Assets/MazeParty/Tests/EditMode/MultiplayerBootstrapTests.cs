using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MazeParty.Gameplay;
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
            "Assets/MazeParty/Scenes/OnlineBootstrap.unity";
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board.unity";
        private const string MinefieldScenePath =
            "Assets/MazeParty/Scenes/Minefield.unity";
        private const string WrongWayScenePath =
            "Assets/MazeParty/Scenes/WrongWay.unity";
        private const string RedLightGreenLightScenePath =
            "Assets/MazeParty/Scenes/RedLightGreenLight.unity";
        private const string PlayerPrefabPath =
            "Assets/MazeParty/Prefabs/NetworkPlayer.prefab";
        private const string D12VisualPrefabPath =
            "Assets/MazeParty/Art/Dice/D12/Prefabs/" +
            "D12WorldDieVisual.prefab";

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
        public void OnlineBootstrapScene_HasBuildNetworkPlayerAndUiContracts()
        {
            var enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            Assert.That(enabledScenes.Length, Is.GreaterThanOrEqualTo(5));
            Assert.That(
                enabledScenes.Take(5),
                Is.EqualTo(new[]
                {
                    BootstrapScenePath,
                    BoardScenePath,
                    MinefieldScenePath,
                    WrongWayScenePath,
                    RedLightGreenLightScenePath
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
            Assert.That(boundaryWalls.WallMaterial, Is.Not.Null);

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
                    .OrderBy(die => die.ConfiguredSlot)
                    .ToArray();
                Assert.That(
                    dice,
                    Has.Length.EqualTo(MultiplayerConstants.MaxPlayers));
                Assert.That(
                    dice.Select(die => die.ConfiguredSlot),
                    Is.EqualTo(new[] { 0, 1, 2, 3 }));
                foreach (var die in dice)
                {
                    AssertStableInSceneNetworkObject(die.gameObject);
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

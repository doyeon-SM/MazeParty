using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class SessionRulesTests
    {
        [Test]
        public void FindLowestAvailableSlot_UsesFirstGap()
        {
            Assert.That(
                SessionRules.FindLowestAvailableSlot(new[] { 0, 2, 3 }),
                Is.EqualTo(1));
        }

        [Test]
        public void FindLowestAvailableSlot_ReturnsMinusOneWhenFull()
        {
            Assert.That(
                SessionRules.FindLowestAvailableSlot(new[] { 0, 1, 2, 3 }),
                Is.EqualTo(-1));
        }

        [Test]
        public void CanStart_RequiresExactlyFourReadyUniqueSeats()
        {
            var players = CreateReadyPlayers();

            Assert.That(SessionRules.CanStart(players), Is.True);
            Assert.That(SessionRules.CanStart(players.Take(3).ToList()), Is.False);
        }

        [Test]
        public void CanStart_RejectsDuplicateSeatOrUnreadyPlayer()
        {
            var duplicate = CreateReadyPlayers();
            duplicate[3] = new OnlinePlayerSnapshot("p4", "Four", 2, true, false);

            var unready = CreateReadyPlayers();
            unready[1] = new OnlinePlayerSnapshot("p2", "Two", 1, false, false);

            Assert.That(SessionRules.CanStart(duplicate), Is.False);
            Assert.That(SessionRules.CanStart(unready), Is.False);
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
    }

    public sealed class MultiplayerAssetConfigurationTests
    {
        private const string BootstrapScene = "Assets/MazeParty/Scenes/OnlineBootstrap.unity";
        private const string BoardScene = "Assets/MazeParty/Scenes/Board.unity";
        private const string PlayerPrefab = "Assets/MazeParty/Prefabs/NetworkPlayer.prefab";

        [Test]
        public void OnlineScenes_AreFirstEnabledBuildScenes()
        {
            var enabled = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();

            Assert.That(enabled.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(enabled[0].path, Is.EqualTo(BootstrapScene));
            Assert.That(enabled[1].path, Is.EqualTo(BoardScene));
        }

        [Test]
        public void NetworkPlayerPrefab_HasRequiredNetworkComponents()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Unity.Netcode.Components.NetworkTransform>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<CharacterController>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkPlayerAvatar>(), Is.Not.Null);
            var boundaryWalls = prefab.GetComponent<PlayerBoardBoundaryWalls>();
            Assert.That(boundaryWalls, Is.Not.Null);
            Assert.That(boundaryWalls.WallMaterial, Is.Not.Null);
            Assert.That(boundaryWalls.WallMaterial.shader.name,
                Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(prefab.GetComponent<PlayerAvatarVisual>(), Is.Not.Null);
        }

        [Test]
        public void LobbyArena_RuntimeFactoryCreatesVisibleFourPlayerRoom()
        {
            var cameraObject = new GameObject("Lobby Arena Test Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var existedBeforeTest = Object.FindAnyObjectByType<LobbyArena>(
                FindObjectsInactive.Include) != null;
            var arena = LobbyArena.EnsureRuntimeCreated(camera);

            try
            {
                Assert.That(arena, Is.Not.Null);
                Assert.That(arena.SpawnCount, Is.EqualTo(MultiplayerConstants.MaxPlayers));
                Assert.That(arena.InnerSize.x, Is.GreaterThan(8f));
                Assert.That(arena.InnerSize.y, Is.GreaterThan(6f));
                Assert.That(arena.transform.Find("Floor"), Is.Not.Null);
                Assert.That(camera.fieldOfView, Is.EqualTo(48f));

                for (var left = 0; left < MultiplayerConstants.MaxPlayers; left++)
                {
                    for (var right = left + 1; right < MultiplayerConstants.MaxPlayers; right++)
                    {
                        Assert.That(
                            Vector3.Distance(
                                arena.GetSpawnPosition(left),
                                arena.GetSpawnPosition(right)),
                            Is.GreaterThan(2f));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                if (!existedBeforeTest && arena != null)
                {
                    Object.DestroyImmediate(arena.gameObject);
                }
            }
        }

        [Test]
        public void BoardScene_HasStableInSceneNetworkObjectHash()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                BoardScene,
                UnityEditor.SceneManagement.OpenSceneMode.Additive);

            try
            {
                var matchState = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<NetworkMatchState>(true))
                    .SingleOrDefault();

                Assert.That(matchState, Is.Not.Null);
                var networkObject = matchState.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));
                Assert.That(networkObject.InScenePlaced, Is.True);
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void BoardScene_HasFourServerOwnedWorldDiceAndUniqueDynamicShopPresenter()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                BoardScene,
                UnityEditor.SceneManagement.OpenSceneMode.Additive);

            try
            {
                var roots = scene.GetRootGameObjects();
                var dice = roots
                    .SelectMany(root => root.GetComponentsInChildren<NetworkWorldDie>(true))
                    .OrderBy(die => die.ConfiguredSlot)
                    .ToArray();
                Assert.That(dice, Has.Length.EqualTo(MultiplayerConstants.MaxPlayers));
                Assert.That(
                    dice.Select(die => die.ConfiguredSlot),
                    Is.EqualTo(new[] { 0, 1, 2, 3 }));
                Assert.That(
                    dice.All(die => die.GetComponent<NetworkObject>() != null &&
                                    die.GetComponent<NetworkObject>().InScenePlaced &&
                                    die.GetComponent<NetworkObject>().PrefabIdHash != 0u),
                    Is.True);

                var coordinators = roots
                    .SelectMany(root => root.GetComponentsInChildren<NetworkWorldDiceCoordinator>(true))
                    .ToArray();
                Assert.That(coordinators, Has.Length.EqualTo(1));
                var shopMarkers = roots
                    .SelectMany(root => root.GetComponentsInChildren<KeyShopWorldMarker>(true))
                    .ToArray();
                Assert.That(shopMarkers, Has.Length.EqualTo(1));
                var staticShops = roots
                    .SelectMany(root => root.GetComponentsInChildren<BoardTile>(true))
                    .Count(tile => tile.TileType == BoardTileType.KeyShop);
                Assert.That(staticShops, Is.Zero);
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }


        [Test]
        public void BootstrapScene_HasConfiguredNetworkManagerAndTransport()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                BootstrapScene,
                UnityEditor.SceneManagement.OpenSceneMode.Additive);

            try
            {
                var roots = scene.GetRootGameObjects();
                var manager = roots
                    .Select(root => root.GetComponent<NetworkManager>())
                    .FirstOrDefault(component => component != null);

                Assert.That(manager, Is.Not.Null);
                var transport = manager.GetComponent<UnityTransport>();
                Assert.That(transport, Is.Not.Null);
                Assert.That(transport.DisconnectTimeoutMS, Is.EqualTo(15000));
                Assert.That(manager.NetworkConfig.EnableSceneManagement, Is.True);
                Assert.That(manager.NetworkConfig.PlayerPrefab, Is.Not.Null);
                Assert.That(manager.NetworkConfig.PlayerPrefab, Is.EqualTo(
                    AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab)));
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void BootstrapScene_HasConfiguredCanvasLobby()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                BootstrapScene,
                UnityEditor.SceneManagement.OpenSceneMode.Additive);

            try
            {
                var roots = scene.GetRootGameObjects();
                var canvases = roots
                    .SelectMany(root => root.GetComponentsInChildren<Canvas>(true))
                    .ToArray();
                Assert.That(canvases, Has.Length.EqualTo(1));
                Assert.That(canvases[0].renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                Assert.That(canvases[0].GetComponent<CanvasScaler>(), Is.Not.Null);
                Assert.That(canvases[0].GetComponent<GraphicRaycaster>(), Is.Not.Null);

                var lobbyView = canvases[0].GetComponent<OnlineLobbyView>();
                Assert.That(lobbyView, Is.Not.Null);
                Assert.That(lobbyView.HasRequiredReferences, Is.True);
                Assert.That(
                    lobbyView.PlayerRowCount,
                    Is.EqualTo(MultiplayerConstants.MaxPlayers));

                var eventSystems = roots
                    .SelectMany(root => root.GetComponentsInChildren<EventSystem>(true))
                    .ToArray();
                Assert.That(eventSystems, Has.Length.EqualTo(1));
                Assert.That(
                    eventSystems[0].GetComponent<InputSystemUIInputModule>(),
                    Is.Not.Null);

                var texts = lobbyView.GetComponentsInChildren<Text>(true);
                Assert.That(texts, Is.Not.Empty);
                foreach (var text in texts)
                {
                    Assert.That(
                        text.text,
                        Does.Not.Match("[\\uAC00-\\uD7A3]"),
                        text.gameObject.name + " must use English until a Korean font is added.");
                }
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

    }

    public sealed class PlayerCustomizationRulesTests
    {
        [Test]
        public void KoreanDisplayName_IsPreservedAndLimitedToSixteenCharacters()
        {
            var value = PlayerProfilePreferences.SanitizeDisplayName(
                "미로파티플레이어이름테스트입니다추가문자");

            Assert.That(value, Does.StartWith("미로파티"));
            Assert.That(value.Length, Is.EqualTo(16));
        }

        [Test]
        public void AppearanceSanitizer_AllowsOnlyImplementedTestHat()
        {
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
        }

        [Test]
        public void LobbyPalette_HasEightDistinctRequestedColors()
        {
            Assert.That(LobbyColorPalette.Count, Is.EqualTo(8));
            var colors = Enumerable.Range(0, LobbyColorPalette.Count)
                .Select(LobbyColorPalette.GetColor)
                .ToArray();

            Assert.That(colors.Distinct().Count(), Is.EqualTo(8));
            Assert.That(LobbyColorPalette.GetDisplayName(0), Is.EqualTo("Red"));
            Assert.That(LobbyColorPalette.GetDisplayName(7), Is.EqualTo("Black"));
        }

        [Test]
        public void LobbyPalette_FirstAvailableSkipsOccupiedColors()
        {
            const byte occupied = 0b0010_1111;

            Assert.That(LobbyColorPalette.FindFirstAvailable(occupied), Is.EqualTo(4));
        }
    }

    public sealed class NetworkBoardTraversalRegressionTests
    {
        [Test]
        public void EnsureTraversalInitialized_DoesNotRebindAnActiveGateCrossing()
        {
            var avatarObject = new GameObject("Traversal Regression Avatar");
            var tileObject = new GameObject("Logical Source Tile");

            try
            {
                var avatar = avatarObject.AddComponent<NetworkPlayerAvatar>();
                var sourceTile = tileObject.AddComponent<BoardTile>();
                sourceTile.Configure(Vector2Int.zero, BoardTileType.Normal);

                var traversalField = typeof(NetworkPlayerAvatar).GetField(
                    "_traversal",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var ensureMethod = typeof(NetworkPlayerAvatar).GetMethod(
                    "EnsureTraversalInitialized",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(traversalField, Is.Not.Null);
                Assert.That(ensureMethod, Is.Not.Null);

                var traversal = (BoardTraversalState)traversalField.GetValue(avatar);
                traversal.Begin(sourceTile, 4);

                // This method runs every server physics frame. While a capsule is
                // between rooms it must preserve the logical source until the gate
                // itself commits and decrements RemainingMoves.
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
    }
}

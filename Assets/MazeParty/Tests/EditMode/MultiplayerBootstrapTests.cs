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
        [Test]
        public void BoardArrivalGrace_IsThreeSeconds()
        {
            Assert.That(
                NetworkMatchState.AllPlayersArrivalGraceSeconds,
                Is.EqualTo(3d));
        }

        private const string BootstrapScene = "Assets/MazeParty/Scenes/OnlineBootstrap.unity";
        private const string BoardScene = "Assets/MazeParty/Scenes/Board.unity";
        private const string PlayerPrefab = "Assets/MazeParty/Prefabs/NetworkPlayer.prefab";
        private const string D12Model =
            "Assets/MazeParty/Art/Dice/D12/Models/Dice_d12.fbx";
        private const string D12VisualPrefab =
            "Assets/MazeParty/Art/Dice/D12/Prefabs/D12WorldDieVisual.prefab";
        private const string D12Albedo =
            "Assets/MazeParty/Art/Dice/D12/Textures/D12_White_Albedo.png";

        // Source albedo atlas values, indexed top-to-bottom and left-to-right.
        // The test derives each number's face normal independently from mesh UVs so
        // a matching mistake in the scene-generation constants cannot self-validate.
        private static readonly int[,] D12AtlasValuesByTopRow =
        {
            { 7, 1, 10, 5 },
            { 9, 11, 4, 8 },
            { 3, 6, 2, 12 }
        };

        private static readonly Color[] D12PlayerColors =
        {
            new Color(0.95f, 0.25f, 0.25f),
            new Color(0.25f, 0.55f, 1f),
            new Color(0.25f, 0.85f, 0.4f),
            new Color(1f, 0.75f, 0.2f)
        };

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
        public void BoardScene_EachWorldDieHasCompleteD12MarkerSet()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                BoardScene,
                UnityEditor.SceneManagement.OpenSceneMode.Additive);

            try
            {
                var expectedValues = Enumerable.Range(
                        WorldDieAuthorityModel.MinimumFace,
                        WorldDieAuthorityModel.MaximumFace -
                        WorldDieAuthorityModel.MinimumFace + 1)
                    .ToArray();
                var dice = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<NetworkWorldDie>(true))
                    .ToArray();

                Assert.That(
                    dice,
                    Has.Length.EqualTo(MultiplayerConstants.MaxPlayers));
                foreach (var die in dice)
                {
                    var markers = die.GetComponentsInChildren<
                            WorldDieFaceMarker>(true)
                        .OrderBy(marker => marker.Value)
                        .ToArray();

                    Assert.That(
                        markers,
                        Has.Length.EqualTo(expectedValues.Length),
                        die.name);
                    Assert.That(
                        markers.Select(marker => marker.Value),
                        Is.EqualTo(expectedValues),
                        die.name);
                }
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(
                    scene,
                    true);
            }
        }

        [Test]
        public void BoardScene_WorldDiceUseTrackedD12MeshAndExactNumberedFaces()
        {
            var importedModel = AssetDatabase.LoadAssetAtPath<GameObject>(D12Model);
            var visualPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(D12VisualPrefab);

            Assert.That(importedModel, Is.Not.Null);
            Assert.That(visualPrefab, Is.Not.Null);
            var mesh = importedModel.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.triangles.Length / 3, Is.EqualTo(36));
            Assert.That(
                mesh.normals
                    .Select(normal => new Vector3(
                        Mathf.Round(normal.x * 100000f) / 100000f,
                        Mathf.Round(normal.y * 100000f) / 100000f,
                        Mathf.Round(normal.z * 100000f) / 100000f))
                    .Distinct()
                    .Count(),
                Is.EqualTo(12));

            var faceNormalsByValue = DeriveD12FaceNormalsFromUvAtlas(mesh);
            Assert.That(faceNormalsByValue.Keys.OrderBy(value => value),
                Is.EqualTo(Enumerable.Range(1, 12)));

            for (var value = 1; value <= 12; value++)
            {
                var expected = faceNormalsByValue[value].normalized;
                var opposite = faceNormalsByValue[13 - value].normalized;
                Assert.That(
                    (expected + opposite).sqrMagnitude,
                    Is.LessThan(0.000001f),
                    "Opposite numbered D12 faces must sum to 13.");
            }

            var prefabFilter = visualPrefab.GetComponent<MeshFilter>();
            var prefabCollider = visualPrefab.GetComponent<MeshCollider>();
            var prefabRenderer = visualPrefab.GetComponent<MeshRenderer>();
            Assert.That(prefabFilter, Is.Not.Null);
            Assert.That(prefabFilter.sharedMesh, Is.SameAs(mesh));
            Assert.That(prefabCollider, Is.Not.Null);
            Assert.That(prefabCollider.convex, Is.True);
            Assert.That(prefabCollider.sharedMesh, Is.SameAs(mesh));
            Assert.That(prefabRenderer, Is.Not.Null);
            Assert.That(
                prefabRenderer.sharedMaterial.shader.name,
                Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(
                AssetDatabase.GetAssetPath(
                    prefabRenderer.sharedMaterial.GetTexture("_BaseMap")),
                Is.EqualTo(D12Albedo));

            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                BoardScene,
                UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var dice = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<NetworkWorldDie>(true))
                    .ToArray();
                Assert.That(dice, Has.Length.EqualTo(MultiplayerConstants.MaxPlayers));

                foreach (var die in dice)
                {
                    Assert.That(
                        die.GetComponent<MeshFilter>().sharedMesh,
                        Is.SameAs(mesh));
                    var sceneRenderer = die.GetComponent<MeshRenderer>();
                    Assert.That(sceneRenderer, Is.Not.Null);
                    Assert.That(
                        sceneRenderer.sharedMaterial.name,
                        Is.EqualTo("D12Player" + (die.ConfiguredSlot + 1)));
                    Assert.That(
                        Vector4.Distance(
                            sceneRenderer.sharedMaterial.GetColor("_BaseColor"),
                            D12PlayerColors[die.ConfiguredSlot]),
                        Is.LessThan(0.0001f),
                        "The serialized scene must retain each player's D12 tint.");
                    Assert.That(
                        AssetDatabase.GetAssetPath(
                            sceneRenderer.sharedMaterial.GetTexture("_BaseMap")),
                        Is.EqualTo(D12Albedo));
                    var markers = die.GetComponentsInChildren<WorldDieFaceMarker>(true);
                    Assert.That(markers, Has.Length.EqualTo(12));
                    Assert.That(
                        markers.Select(marker => marker.Value).OrderBy(value => value),
                        Is.EqualTo(Enumerable.Range(1, 12)));

                    for (var value = 1; value <= 12; value++)
                    {
                        var marker = markers.Single(candidate => candidate.Value == value);
                        Assert.That(
                            Vector3.Dot(
                                marker.LocalNormal,
                                faceNormalsByValue[value].normalized),
                            Is.GreaterThan(0.99999f),
                            "Scene marker " + value +
                            " must match its texture-numbered mesh face.");
                        Assert.That(
                            marker.GetComponentInChildren<TextMesh>(true),
                            Is.Null,
                            "The texture supplies face numbers; marker text would overlap it.");
                    }
                }
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Dictionary<int, Vector3> DeriveD12FaceNormalsFromUvAtlas(
            Mesh mesh)
        {
            const float centroidClusterEpsilon = 0.001f;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            Assert.That(uvs, Has.Length.EqualTo(normals.Length));

            var faceGroups = Enumerable.Range(0, normals.Length)
                .GroupBy(index => new Vector3(
                    Mathf.Round(normals[index].x * 100000f) / 100000f,
                    Mathf.Round(normals[index].y * 100000f) / 100000f,
                    Mathf.Round(normals[index].z * 100000f) / 100000f));
            var faceCentroids = new List<KeyValuePair<Vector3, Vector2>>();
            foreach (var faceGroup in faceGroups)
            {
                var vertexIndices = faceGroup.ToArray();
                Assert.That(vertexIndices, Has.Length.EqualTo(5));
                var uvCentroid = vertexIndices
                    .Select(index => uvs[index])
                    .Aggregate(Vector2.zero, (sum, uv) => sum + uv) /
                    vertexIndices.Length;
                faceCentroids.Add(new KeyValuePair<Vector3, Vector2>(
                    faceGroup.Key.normalized,
                    uvCentroid));
            }

            Assert.That(faceCentroids, Has.Count.EqualTo(12));
            var atlasColumns = ClusterD12AtlasCoordinates(
                    faceCentroids.Select(face => face.Value.x),
                    centroidClusterEpsilon)
                .OrderBy(coordinate => coordinate)
                .ToArray();
            var atlasRowsFromTop = ClusterD12AtlasCoordinates(
                    faceCentroids.Select(face => face.Value.y),
                    centroidClusterEpsilon)
                .OrderByDescending(coordinate => coordinate)
                .ToArray();
            Assert.That(atlasColumns, Has.Length.EqualTo(4));
            Assert.That(atlasRowsFromTop, Has.Length.EqualTo(3));

            var result = new Dictionary<int, Vector3>();
            foreach (var face in faceCentroids)
            {
                var atlasColumn = FindNearestD12AtlasCoordinate(
                    atlasColumns,
                    face.Value.x);
                var atlasRowFromTop = FindNearestD12AtlasCoordinate(
                    atlasRowsFromTop,
                    face.Value.y);
                var value = D12AtlasValuesByTopRow[atlasRowFromTop, atlasColumn];
                Assert.That(result.ContainsKey(value), Is.False,
                    "Each numbered atlas cell must belong to exactly one mesh face.");
                result.Add(value, face.Key);
            }

            return result;
        }

        private static IEnumerable<float> ClusterD12AtlasCoordinates(
            IEnumerable<float> coordinates,
            float epsilon)
        {
            var clusters = new List<List<float>>();
            foreach (var coordinate in coordinates.OrderBy(value => value))
            {
                var cluster = clusters.LastOrDefault();
                if (cluster == null ||
                    Mathf.Abs(coordinate - cluster.Average()) > epsilon)
                {
                    cluster = new List<float>();
                    clusters.Add(cluster);
                }

                cluster.Add(coordinate);
            }

            return clusters.Select(cluster => cluster.Average());
        }

        private static int FindNearestD12AtlasCoordinate(
            IReadOnlyList<float> coordinates,
            float value)
        {
            var nearestIndex = 0;
            var nearestDistance = float.PositiveInfinity;
            for (var index = 0; index < coordinates.Count; index++)
            {
                var distance = Mathf.Abs(coordinates[index] - value);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = index;
                }
            }

            return nearestIndex;
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

                var quitButtonTransform = lobbyView.transform.Find(
                    "Lobby Window/Quit Game Button");
                Assert.That(quitButtonTransform, Is.Not.Null);
                var quitButton = quitButtonTransform.GetComponent<Button>();
                Assert.That(quitButton, Is.Not.Null);
                Assert.That(
                    quitButton.GetComponentInChildren<Text>(true).text,
                    Is.EqualTo("Quit Game"));
                var serializedView = new SerializedObject(lobbyView);
                Assert.That(
                    serializedView.FindProperty("quitButton").objectReferenceValue,
                    Is.SameAs(quitButton));

                var lobbyCanvasGroup = lobbyView.GetComponent<CanvasGroup>();
                lobbyView.SetPresentationVisible(false);
                Assert.That(lobbyView.PresentationVisible, Is.False);
                Assert.That(lobbyCanvasGroup.alpha, Is.Zero);
                Assert.That(lobbyCanvasGroup.interactable, Is.False);
                Assert.That(lobbyCanvasGroup.blocksRaycasts, Is.False);
                lobbyView.SetPresentationVisible(true);
                Assert.That(lobbyCanvasGroup.alpha, Is.EqualTo(1f));

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

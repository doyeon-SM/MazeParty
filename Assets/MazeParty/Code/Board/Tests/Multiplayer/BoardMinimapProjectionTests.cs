using Arikan;
using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardMinimapProjectionTests
    {
        [Test]
        public void LandingEffect_StaysVisibleOnMapWhileWorldTileStaysHidden()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab"));
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                board.name = "Map-only landing effect tile";
                var worldRenderer = board.GetComponent<Renderer>();
                var tile = board.AddComponent<BoardTile>();
                tile.Configure(Vector2Int.zero, BoardTileType.Normal);
                tile.ApplyLandingEffectPresentation(BoardLandingEffectType.GoldGain);

                var topology = board.AddComponent<BoardTopology>();
                topology.Configure(new[] { tile }, new BoardGate[0]);
                var view = instance.GetComponent<BoardMinimapView>();
                view.PrepareMap(topology, tile.Coordinate, 0, null);

                var rooms = new SerializedObject(view).FindProperty("rooms");
                var effectIcon = (BoardMapIcon)rooms.GetArrayElementAtIndex(0)
                    .FindPropertyRelative("EffectIcon").objectReferenceValue;
                var iconKind = new SerializedObject(effectIcon)
                    .FindProperty("kind").enumValueIndex;

                Assert.That(tile.LandingEffect,
                    Is.EqualTo(BoardLandingEffectType.GoldGain));
                Assert.That(worldRenderer.enabled, Is.False,
                    "Landing effects must not re-enable the world tile block.");
                Assert.That(effectIcon.enabled, Is.True,
                    "The same landing effect remains visible on the map.");
                Assert.That(iconKind,
                    Is.EqualTo((int)BoardMapIconKind.GoldGain));
            }
            finally
            {
                Object.DestroyImmediate(board);
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void RouteDots_FollowBothMapProjections_AndClearWhenUnavailable()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab"));
            var board = new GameObject("Route board");
            try
            {
                var tiles = new BoardTile[3];
                var gates = new BoardGate[2];
                for (var i = 0; i < tiles.Length; i++)
                {
                    var obj = new GameObject("Tile");
                    obj.transform.SetParent(board.transform);
                    obj.transform.position = new Vector3(31f + i * 8f, 0f, -19f);
                    tiles[i] = obj.AddComponent<BoardTile>();
                    tiles[i].Configure(new Vector2Int(i, 0), BoardTileType.Normal);
                    if (i > 0) { gates[i - 1] = obj.AddComponent<BoardGate>(); gates[i - 1].Configure(tiles[i - 1], tiles[i]); }
                }
                var topology = board.AddComponent<BoardTopology>();
                topology.Configure(tiles, gates);
                foreach (var view in instance.GetComponentsInChildren<BoardMinimapView>(true))
                {
                    var data = new SerializedObject(view);
                    var graphic = (BoardMapRouteGraphic)data.FindProperty("shopRouteGraphic").objectReferenceValue;
                    var projection = (MiniMapView)data.FindProperty("projection").objectReferenceValue;
                    Assert.That(graphic.transform.parent, Is.EqualTo(projection.otherDotCanvas));
                    var mask = (Mask)data.FindProperty("circularMask").objectReferenceValue;
                    if (mask != null) Assert.That(graphic.transform.IsChildOf(mask.transform), Is.True);
                    view.PrepareMap(topology, tiles[0].Coordinate, 0, tiles[2].Coordinate, tiles[0].WorldCenter);
                    Assert.That(
                        graphic.PointCount,
                        Is.GreaterThan(2),
                        "Both the full map and minimap show the dice-independent shortest route to the key shop.");
                    var firstPoint = graphic.ProjectPoint(0);
                    var endPoint = graphic.ProjectPoint(graphic.PointCount - 1);
                    Assert.That(endPoint.x, Is.GreaterThan(firstPoint.x));
                    view.SetHeading(270f);
                    if (mask != null)
                    {
                        Assert.That(firstPoint.sqrMagnitude, Is.LessThan(.001f));
                        var delta = projection.otherDotCanvas.localRotation * (Vector3)(endPoint - firstPoint);
                        Assert.That(delta.y, Is.LessThan(0f), "East must appear below a west-facing player.");
                        view.PrepareMap(topology, tiles[0].Coordinate, 0, tiles[2].Coordinate, tiles[0].WorldCenter + Vector3.right);
                        Assert.That(graphic.ProjectPoint(0).x, Is.LessThan(firstPoint.x), "Dots must move with the actual local map center.");
                    }
                    foreach (var target in new Vector2Int?[] { null, tiles[2].Coordinate, tiles[0].Coordinate })
                    {
                        view.PrepareMap(topology, tiles[2].Coordinate, 0, target);
                        Assert.That(graphic.PointCount, Is.Zero, "Missing shop, same tile and unreachable routes must clear old dots.");
                    }
                }
            }
            finally { Object.DestroyImmediate(instance); Object.DestroyImmediate(board); }
        }
        [Test]
        public void DirectedShopRoute_UsesDistanceAndPreviewsBranchesBeforeArrival()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab"));
            var board = new GameObject("Directed board");
            try
            {
                var tiles = new BoardTile[4];
                var gates = new BoardGate[3];
                for (var i = 0; i < tiles.Length; i++)
                {
                    var obj = new GameObject("Tile " + i);
                    obj.transform.SetParent(board.transform, false);
                    obj.transform.position = i < 3
                        ? Vector3.right * i * BoardTile.RoomSize
                        : Vector3.forward * BoardTile.RoomSize;
                    tiles[i] = obj.AddComponent<BoardTile>();
                    tiles[i].Configure(i < 3
                            ? new Vector2Int(i, 0)
                            : new Vector2Int(0, 1),
                        BoardTileType.Normal);
                    if (i == 0) continue;
                    var gateIndex = i - 1;
                    gates[gateIndex] = obj.AddComponent<BoardGate>();
                    gates[gateIndex].Configure(
                        i < 3 ? tiles[i - 1] : tiles[0],
                        tiles[i],
                        BoardTile.RoomSize);
                }
                var topology = board.AddComponent<BoardTopology>();
                topology.Configure(tiles, gates);
                var view = instance.GetComponent<BoardMinimapView>();
                Assert.That(view.GetMinimumShopDistance(topology, Vector2Int.zero, new Vector2Int(2, 0)), Is.EqualTo(2));
                Assert.That(view.GetMinimumShopDistance(topology, new Vector2Int(2, 0), Vector2Int.zero), Is.EqualTo(-1));
                Assert.That(view.GetMinimumShopDistance(topology, Vector2Int.zero, Vector2Int.zero), Is.Zero);
                Assert.That(view.GetMinimumShopDistance(topology, Vector2Int.zero, null), Is.EqualTo(-1));
                var data = new SerializedObject(view);
                var rooms = data.FindProperty("rooms");
                var north = (BoardMapIcon)rooms.GetArrayElementAtIndex(0).FindPropertyRelative("ProgressArrows").GetArrayElementAtIndex(0).objectReferenceValue;
                var east = (BoardMapIcon)rooms.GetArrayElementAtIndex(0).FindPropertyRelative("ProgressArrows").GetArrayElementAtIndex(1).objectReferenceValue;
                var laterEast = (BoardMapIcon)rooms.GetArrayElementAtIndex(1).FindPropertyRelative("ProgressArrows").GetArrayElementAtIndex(1).objectReferenceValue;
                view.PrepareMap(topology, Vector2Int.zero, 2, new Vector2Int(2, 0));
                Assert.That(east.enabled, Is.True);
                Assert.That(north.enabled, Is.True,
                    "Direction icons appear for each choice at a branch.");
                Assert.That(laterEast.enabled, Is.False,
                    "Single-exit tiles do not display branch arrows.");
                view.PrepareMap(topology, Vector2Int.zero, 0, Vector2Int.right);
                Assert.That(east.enabled, Is.True,
                    "Branch arrows remain visible before movement and without remaining moves.");
                Assert.That(north.enabled, Is.True);
                Assert.That(view.GetMinimumShopDistance(topology, Vector2Int.zero, Vector2Int.right), Is.EqualTo(1));
                view.PrepareMap(topology, Vector2Int.right, 1, Vector2Int.right);
                Assert.That(east.enabled, Is.True,
                    "A branch already shown elsewhere on the map must not depend on the local tile.");
                Assert.That(north.enabled, Is.True);
                Assert.That(laterEast.enabled, Is.False,
                    "A tile with only one exit does not show a direction icon.");
                Assert.That(view.GetMinimumShopDistance(topology,
                    Vector2Int.right, Vector2Int.right), Is.Zero);
                view.PrepareMap(topology, Vector2Int.right, 1, Vector2Int.zero);
                Assert.That(view.GetMinimumShopDistance(topology,
                    Vector2Int.right, Vector2Int.zero), Is.EqualTo(-1));
                view.PrepareMap(topology, Vector2Int.right, 1, null);
                Assert.That(view.GetMinimumShopDistance(topology,
                    Vector2Int.right, null), Is.EqualTo(-1));
            }
            finally { Object.DestroyImmediate(board); Object.DestroyImmediate(instance); }
        }

        [Test]
        public void LocalRadiusAndFullMap_AreBoundToPlayerPositionAndBoardLifecycle()
        {
            UiPopupStack.ClearForTests();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab");
            var instance = Object.Instantiate(prefab);
            var board = new GameObject("Board");
            var player = new GameObject("Local player");
            try
            {
                var tile = board.AddComponent<BoardTile>();
                tile.Configure(Vector2Int.zero, BoardTileType.Normal);
                var topology = board.AddComponent<BoardTopology>();
                topology.Configure(new[] { tile }, new BoardGate[0]);
                var local = instance.GetComponent<BoardMinimapView>();
                var data = new SerializedObject(local);
                var mask = (Mask)data.FindProperty("circularMask").objectReferenceValue;
                Assert.That(mask, Is.Not.Null);
                Assert.That(mask.GetComponent<BoardMapCircleGraphic>(), Is.Not.Null);
                Assert.That(mask.GetComponent<CanvasRenderer>(), Is.Not.Null);
                var dot = (Image)data.FindProperty("players").GetArrayElementAtIndex(0).objectReferenceValue;
                Assert.That(dot.transform.IsChildOf(mask.transform), Is.True);
                foreach (var focus in new[] { new Vector3(2f, 0f, -3f), new Vector3(-7f, 0f, 4f) })
                {
                    player.transform.position = focus;
                    local.PrepareMap(topology, Vector2Int.zero, 2, null, focus);
                    local.PresentPlayer(0, player.transform, Color.white, true);
                    local.SetHeading(270f);
                    Assert.That(dot.rectTransform.localPosition.sqrMagnitude, Is.LessThan(0.001f),
                        "The actual player position, not the tile center, anchors the map.");
                    Assert.That(local.IsPositionVisible(focus + Vector3.right * 16f), Is.True);
                    Assert.That(local.IsPositionVisible(focus + Vector3.right * 16.01f), Is.False);
                    Assert.That(local.IsPositionVisible(focus + new Vector3(12f, 0f, 12f)), Is.False,
                        "The range must be circular, not a four-tile-wide square.");
                    player.transform.position = focus + Vector3.forward * 17f;
                    local.PresentPlayer(0, player.transform, Color.white, false);
                    Assert.That(dot.gameObject.activeSelf, Is.False);
                }
                var controller = instance.GetComponent<BoardMapView>();
                typeof(BoardMapView).GetMethod(
                        "WireFullMapCloseButton",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(controller, new object[] { true });
                var controllerData = new SerializedObject(controller);
                var full = (BoardMinimapView)controllerData.FindProperty("fullMap").objectReferenceValue;
                var panel = (GameObject)controllerData.FindProperty("fullMapPanel").objectReferenceValue;
                var closeButton = (Button)controllerData
                    .FindProperty("fullMapCloseButton").objectReferenceValue;
                Assert.That(full.HasRequiredReferences, Is.True);
                // Check the actual prefab policy for every possible local seat, including ownership changes.
                foreach (var view in new[] { local, full })
                {
                    var viewData = new SerializedObject(view);
                    var markers = viewData.FindProperty("players");
                    var highlights = viewData.FindProperty("localHighlights");
                    player.transform.position = Vector3.zero;
                    view.PrepareMap(topology, Vector2Int.zero, 2, null, player.transform.position);
                    for (var localSlot = 0; localSlot < MultiplayerConstants.MaxPlayers; localSlot++)
                    {
                        for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                        {
                            var isLocal = slot == localSlot;
                            view.PresentPlayer(slot, player.transform, Color.white, isLocal);
                            var marker = (Image)markers.GetArrayElementAtIndex(slot).objectReferenceValue;
                            var highlight = (GameObject)highlights.GetArrayElementAtIndex(slot).objectReferenceValue;
                            Assert.That(marker.gameObject.activeSelf, Is.True,
                                "Knowledge filtering happens before a marker position is presented.");
                            Assert.That(highlight.activeSelf, Is.EqualTo(isLocal));
                        }
                    }
                    for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                    {
                        view.PresentPlayer(slot, null, Color.white, false);
                        Assert.That(((Image)markers.GetArrayElementAtIndex(slot).objectReferenceValue).gameObject.activeSelf, Is.False);
                        Assert.That(((GameObject)highlights.GetArrayElementAtIndex(slot).objectReferenceValue).activeSelf, Is.False);
                    }
                }
                var fullData = new SerializedObject(full);
                var fullMarkers = fullData.FindProperty("players");
                var fullHighlights = fullData.FindProperty("localHighlights");
                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                {
                    var isLocal = slot == 2;
                    full.PresentPlayer(
                        slot,
                        player.transform,
                        Color.white,
                        isLocal,
                        BoardMinimapDisplayContext.TurnOverview);
                    Assert.That(
                        ((Image)fullMarkers.GetArrayElementAtIndex(slot)
                            .objectReferenceValue).gameObject.activeSelf,
                        Is.True,
                        "The turn overview must show every visible or last-known player.");
                    Assert.That(
                        ((GameObject)fullHighlights.GetArrayElementAtIndex(slot)
                            .objectReferenceValue).activeSelf,
                        Is.EqualTo(isLocal));
                }
                full.PresentPlayer(0, player.transform, Color.white, false);
                Assert.That(
                    ((Image)fullMarkers.GetArrayElementAtIndex(0)
                        .objectReferenceValue).gameObject.activeSelf,
                    Is.True,
                    "A supplied last-known position is shown on the full map.");
                full.PrepareMap(topology, Vector2Int.zero, 2, null, player.transform.position);
                full.SetHeading(270f);
                var fullProjection = full.GetComponentInChildren<MiniMapView>(true);
                Assert.That(Quaternion.Angle(fullProjection.otherDotCanvas.localRotation, Quaternion.identity), Is.LessThan(0.01f));
                Assert.That(full.IsPositionVisible(Vector3.one * 1000f), Is.True);
                controller.UpdateFullMapState(true, true);
                Assert.That(controller.FullMapOpen && panel.activeSelf, Is.True);
                Assert.That(UiPopupStack.IsTop(panel), Is.True);
                closeButton.onClick.Invoke();
                Assert.That(controller.FullMapOpen, Is.False,
                    "The authored close button must clear the user-open latch.");
                Assert.That(panel.activeSelf, Is.False,
                    "The authored close button must hide a non-forced full map.");
                Assert.That(UiPopupStack.Count, Is.Zero);
                controller.UpdateFullMapState(true, true);
                controller.UpdateFullMapState(true, false);
                Assert.That(controller.FullMapOpen, Is.True, "Opening does not require holding M.");
                controller.UpdateFullMapState(true, true);
                Assert.That(controller.FullMapOpen || panel.activeSelf, Is.False);
                controller.UpdateFullMapState(true, false, true);
                Assert.That(controller.FullMapOpen, Is.False,
                    "A forced turn overview must not mutate the M-key latch.");
                Assert.That(panel.activeSelf, Is.True);
                Assert.That(UiPopupStack.Count, Is.Zero,
                    "The automatic turn-overview map is not a dismissible popup.");
                controller.UpdateFullMapState(true, false, true);
                Assert.That(panel.activeSelf, Is.True,
                    "Repeated turn-overview refreshes keep the authored panel active.");
                controller.UpdateFullMapState(true, false, false);
                Assert.That(panel.activeSelf, Is.False);
                controller.UpdateFullMapState(true, true);
                controller.UpdateFullMapState(false, false);
                Assert.That(controller.FullMapOpen || panel.activeSelf, Is.False, "Leaving the board closes the map.");
                controller.UpdateFullMapState(false, true);
                Assert.That(controller.FullMapOpen, Is.False, "M cannot reopen it during a minigame.");
            }
            finally
            {
                UiPopupStack.ClearForTests();
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(board);
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void DirectPlayerSight_RequiresViewportAndClearWorldLine()
        {
            var cameraObject = new GameObject("Map sight camera");
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var outputCamera = cameraObject.AddComponent<Camera>();
                outputCamera.enabled = false;
                outputCamera.transform.position =
                    new Vector3(10000f, 10000f, 10000f);
                outputCamera.transform.rotation = Quaternion.identity;
                var target = outputCamera.transform.position +
                             Vector3.forward * 10f;
                var hits = new RaycastHit[8];

                obstacle.SetActive(false);
                Physics.SyncTransforms();
                Assert.That(BoardMapView.IsPointDirectlyVisible(
                    outputCamera, target, hits), Is.True);

                obstacle.transform.position = Vector3.Lerp(
                    outputCamera.transform.position,
                    target,
                    .5f);
                obstacle.transform.localScale = new Vector3(2f, 2f, .5f);
                obstacle.SetActive(true);
                Physics.SyncTransforms();
                Assert.That(BoardMapView.IsPointDirectlyVisible(
                    outputCamera, target, hits), Is.False,
                    "A wall or obstacle must prevent a map observation.");

                obstacle.SetActive(false);
                Physics.SyncTransforms();
                Assert.That(BoardMapView.IsPointDirectlyVisible(
                    outputCamera,
                    outputCamera.transform.position - Vector3.forward * 10f,
                    hits), Is.False,
                    "A player behind the camera is not directly observed.");
            }
            finally
            {
                Object.DestroyImmediate(obstacle);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void AuthoredMinimap_ProjectsOffsetBoardAndKeepsViewHeadingUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab");
            var instance = Object.Instantiate(prefab);
            var board = new GameObject("Offset board");
            var target = new GameObject("Player");
            try
            {
                var tile = board.AddComponent<BoardTile>();
                board.transform.position = new Vector3(31f, 0f, -19f);
                tile.Configure(Vector2Int.zero, BoardTileType.Normal);
                var topology = board.AddComponent<BoardTopology>();
                topology.Configure(new[] { tile }, new BoardGate[0]);
                var view = instance.GetComponent<BoardMinimapView>();
                view.PrepareMap(topology, Vector2Int.zero, 1, null);
                var projection = instance.GetComponentInChildren<MiniMapView>(true);
                var serialized = new SerializedObject(view);
                var dot = (Image)serialized.FindProperty("players").GetArrayElementAtIndex(0).objectReferenceValue;
                target.transform.position = tile.WorldCenter;
                view.PresentPlayer(0, target.transform, Color.white, true);
                Assert.That(dot.rectTransform.localPosition.sqrMagnitude, Is.LessThan(0.001f),
                    "World bounds center must map to UI center even away from world origin.");

                foreach (var yaw in new[] { 0f, 90f, 180f, 270f, 315f })
                {
                    target.transform.position = tile.WorldCenter + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    view.PresentPlayer(0, target.transform, Color.white, true);
                    view.SetHeading(yaw);
                    var displayed = projection.otherDotCanvas.localRotation * dot.rectTransform.localPosition;
                    Assert.That(displayed.x, Is.EqualTo(0f).Within(0.001f), "Heading " + yaw);
                    Assert.That(displayed.y, Is.GreaterThan(0f), "Looking direction must appear above center.");
                    Assert.That(Quaternion.Angle(Quaternion.identity,
                        projection.otherDotCanvas.localRotation * dot.rectTransform.localRotation),
                        Is.LessThan(0.01f), "Player symbol stays upright.");
                }
                view.PresentPlayer(0, null, Color.white, false);
                Assert.That(dot.gameObject.activeSelf, Is.False, "Removed players must not leave stale markers.");

                var neighborObject = new GameObject("East room");
                neighborObject.transform.SetParent(board.transform, false);
                neighborObject.transform.localPosition = Vector3.right * BoardTile.RoomSize;
                var neighbor = neighborObject.AddComponent<BoardTile>();
                neighbor.Configure(Vector2Int.right, BoardTileType.Normal);
                var gateObject = new GameObject("East-only exit");
                gateObject.transform.SetParent(board.transform, false);
                var gate = gateObject.AddComponent<BoardGate>();
                gate.Configure(tile, neighbor, BoardTile.RoomSize);
                topology.Configure(new[] { tile, neighbor }, new[] { gate });
                var room = serialized.FindProperty("rooms").GetArrayElementAtIndex(0);
                var walls = room.FindPropertyRelative("Walls");
                var northWall = (Image)walls.GetArrayElementAtIndex(0).objectReferenceValue;
                var eastWall = (Image)walls.GetArrayElementAtIndex(1).objectReferenceValue;
                foreach (var moves in new[] { 1, 0 })
                {
                    view.PrepareMap(topology, Vector2Int.zero, moves, null);
                    Assert.That(northWall.enabled, Is.True, "No exit must remain a wall.");
                    Assert.That(eastWall.enabled, Is.EqualTo(moves == 0),
                        "The current room's exit must close when movement is exhausted.");
                }
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(board);
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FreeformMap_UsesAuthoredPolygonsAndConnectedPortalOverlay()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab");
            var instance = Object.Instantiate(prefab);
            var board = new GameObject("Freeform board");
            try
            {
                var firstObject = new GameObject("Triangle tile");
                firstObject.transform.SetParent(board.transform, false);
                var first = firstObject.AddComponent<BoardTile>();
                first.Configure(new Vector2Int(-7, 4), BoardTileType.Start);
                firstObject.AddComponent<BoardTileFootprint>().Configure(new[]
                {
                    new Vector2(2f, -2f),
                    new Vector2(8f, -2f),
                    new Vector2(5f, 3f)
                });

                var secondObject = new GameObject("Pentagon tile");
                secondObject.transform.SetParent(board.transform, false);
                secondObject.transform.position = Vector3.right * 9f;
                var second = secondObject.AddComponent<BoardTile>();
                second.Configure(new Vector2Int(20, 31), BoardTileType.Normal);
                secondObject.AddComponent<BoardTileFootprint>().Configure(new[]
                {
                    new Vector2(-2.5f, -2f),
                    new Vector2(2.5f, -2f),
                    new Vector2(3f, 1f),
                    new Vector2(0f, 3f),
                    new Vector2(-3f, 1f)
                });

                var gateObject = new GameObject("Connected portal");
                gateObject.transform.SetParent(board.transform, false);
                gateObject.transform.position = Vector3.right * 4.5f;
                gateObject.transform.rotation = Quaternion.LookRotation(Vector3.right);
                var gate = gateObject.AddComponent<BoardGate>();
                gate.Configure(first, second, 2f);

                var topology = board.AddComponent<BoardTopology>();
                topology.Configure(new[] { first, second }, new[] { gate });

                var views = instance.GetComponentsInChildren<BoardMinimapView>(true);
                Assert.That(views, Has.Length.EqualTo(2));
                foreach (var view in views)
                {
                    view.PrepareMap(
                        topology,
                        first.Coordinate,
                        1,
                        second.Coordinate,
                        first.WorldCenter);
                    var data = new SerializedObject(view);
                    var graphic = data.FindProperty("topologyGraphic")
                        .objectReferenceValue as BoardMapTopologyGraphic;
                    Assert.That(graphic, Is.Not.Null);
                    Assert.That(graphic.PresentedTileCount, Is.EqualTo(2));
                    Assert.That(graphic.PresentedPortalCount, Is.EqualTo(1));

                    var rooms = data.FindProperty("rooms");
                    var firstFloor = (Image)rooms.GetArrayElementAtIndex(0)
                        .FindPropertyRelative("Floor").objectReferenceValue;
                    var secondFloor = (Image)rooms.GetArrayElementAtIndex(1)
                        .FindPropertyRelative("Floor").objectReferenceValue;
                    var firstTypeIcon = (BoardMapIcon)rooms.GetArrayElementAtIndex(0)
                        .FindPropertyRelative("TypeIcon").objectReferenceValue;
                    var secondTypeIcon = (BoardMapIcon)rooms.GetArrayElementAtIndex(1)
                        .FindPropertyRelative("TypeIcon").objectReferenceValue;
                    Assert.That(firstFloor.gameObject.activeSelf, Is.True);
                    Assert.That(secondFloor.gameObject.activeSelf, Is.True);
                    Assert.That(firstFloor.enabled, Is.False,
                        "The aggregate polygon graphic replaces square floor images.");
                    Assert.That(firstTypeIcon.enabled, Is.False,
                        "Normal and start tiles must not draw the meaningless Room square icon.");
                    Assert.That(secondTypeIcon.enabled, Is.False,
                        "Both maps mark a key shop only by coloring its tile.");
                    view.PrepareMap(
                        topology,
                        first.Coordinate,
                        1,
                        null,
                        first.WorldCenter);
                    Assert.That(secondTypeIcon.enabled, Is.False,
                        "A normal tile must not draw the meaningless Room square icon.");

                    var projection = (MiniMapView)data.FindProperty("projection")
                        .objectReferenceValue;
                    var probe = new GameObject(
                        "Expected freeform anchor",
                        typeof(RectTransform));
                    try
                    {
                        probe.transform.position = first.GetRecoveryCenter();
                        projection.Translate(
                            probe.transform,
                            (RectTransform)probe.transform);
                        var expected = ((RectTransform)probe.transform).localPosition;
                        Assert.That(
                            Vector2.Distance(
                                firstFloor.rectTransform.localPosition,
                                expected),
                            Is.LessThan(0.001f),
                            "Freeform icons must use the authored polygon's safe center.");

                        probe.transform.position = first.transform.position;
                        projection.Translate(
                            probe.transform,
                            (RectTransform)probe.transform);
                        Assert.That(
                            Vector2.Distance(
                                firstFloor.rectTransform.localPosition,
                                ((RectTransform)probe.transform).localPosition),
                            Is.GreaterThan(0.1f),
                            "An offset polygon must not anchor icons at its transform origin.");
                    }
                    finally
                    {
                        Object.DestroyImmediate(probe);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(board);
                Object.DestroyImmediate(instance);
            }
        }


    }
}

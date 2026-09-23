using Arikan;
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
        public void RouteDots_FollowLocalProjectionAndClearWhenShopIsUnavailable()
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
                    Assert.That(graphic.PointCount, Is.GreaterThan(2), "The route is independent of the dice result.");
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
        public void TileInformation_UsesDirectedDistanceAndOnlyImmediateAvailableExits()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab"));
            var board = new GameObject("Directed board");
            try
            {
                var tiles = new BoardTile[3];
                var gates = new BoardGate[2];
                for (var i = 0; i < tiles.Length; i++)
                {
                    var obj = new GameObject("Tile " + i);
                    obj.transform.SetParent(board.transform, false);
                    obj.transform.position = Vector3.right * i * BoardTile.RoomSize;
                    tiles[i] = obj.AddComponent<BoardTile>();
                    tiles[i].Configure(new Vector2Int(i, 0), BoardTileType.Normal);
                    if (i == 0) continue;
                    gates[i - 1] = obj.AddComponent<BoardGate>();
                    gates[i - 1].Configure(tiles[i - 1], tiles[i], BoardTile.RoomSize);
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
                var east = (BoardMapIcon)rooms.GetArrayElementAtIndex(0).FindPropertyRelative("ProgressArrows").GetArrayElementAtIndex(1).objectReferenceValue;
                var laterEast = (BoardMapIcon)rooms.GetArrayElementAtIndex(1).FindPropertyRelative("ProgressArrows").GetArrayElementAtIndex(1).objectReferenceValue;
                var label = (Text)data.FindProperty("shopDistanceText").objectReferenceValue;
                view.PrepareMap(topology, Vector2Int.zero, 2, new Vector2Int(2, 0));
                Assert.That(east.enabled, Is.True);
                Assert.That(laterEast.enabled, Is.False, "Future steps must not be advertised as immediate exits.");
                var oldDistanceText = label.text;
                view.PrepareMap(topology, Vector2Int.zero, 0, Vector2Int.right);
                Assert.That(east.enabled, Is.False);
                Assert.That(label.text, Is.Not.EqualTo(oldDistanceText), "Shop relocation must refresh the cached distance.");
                Assert.That(view.GetMinimumShopDistance(topology, Vector2Int.zero, Vector2Int.right), Is.EqualTo(1));
                view.PrepareMap(topology, Vector2Int.right, 1, Vector2Int.right);
                Assert.That(label.text, Is.EqualTo(string.Format(data.FindProperty("shopDistanceFormat").stringValue, 0)));
                view.PrepareMap(topology, Vector2Int.right, 1, Vector2Int.zero);
                Assert.That(label.text, Is.EqualTo(data.FindProperty("shopUnreachableText").stringValue));
                view.PrepareMap(topology, Vector2Int.right, 1, null);
                Assert.That(label.text, Is.EqualTo(data.FindProperty("shopUnavailableText").stringValue));
            }
            finally { Object.DestroyImmediate(board); Object.DestroyImmediate(instance); }
        }

        [Test]
        public void LocalRadiusAndFullMap_AreBoundToPlayerPositionAndBoardLifecycle()
        {
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
                var controllerData = new SerializedObject(controller);
                var full = (BoardMinimapView)controllerData.FindProperty("fullMap").objectReferenceValue;
                var panel = (GameObject)controllerData.FindProperty("fullMapPanel").objectReferenceValue;
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
                            Assert.That(marker.gameObject.activeSelf, Is.EqualTo(view == local || isLocal),
                                "HUD shows nearby players; full map shows only the current local seat.");
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
                full.PrepareMap(topology, Vector2Int.zero, 2, null, player.transform.position);
                full.SetHeading(270f);
                var fullProjection = full.GetComponentInChildren<MiniMapView>(true);
                Assert.That(Quaternion.Angle(fullProjection.otherDotCanvas.localRotation, Quaternion.identity), Is.LessThan(0.01f));
                Assert.That(full.IsPositionVisible(Vector3.one * 1000f), Is.True);
                controller.UpdateFullMapState(true, true);
                Assert.That(controller.FullMapOpen && panel.activeSelf, Is.True);
                controller.UpdateFullMapState(true, false);
                Assert.That(controller.FullMapOpen, Is.True, "Opening does not require holding M.");
                controller.UpdateFullMapState(true, true);
                Assert.That(controller.FullMapOpen || panel.activeSelf, Is.False);
                controller.UpdateFullMapState(true, true);
                controller.UpdateFullMapState(false, false);
                Assert.That(controller.FullMapOpen || panel.activeSelf, Is.False, "Leaving the board closes the map.");
                controller.UpdateFullMapState(false, true);
                Assert.That(controller.FullMapOpen, Is.False, "M cannot reopen it during a minigame.");
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(board);
                Object.DestroyImmediate(instance);
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
    }
}

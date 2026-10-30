using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;

namespace MazeParty.Multiplayer.Tests
{
    public sealed partial class BoardChestAuthorityTests
    {
        private BoardGate PrepareWall()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Resources/MazeParty/Board/Maps/MazeGrayboxMapRoot.prefab");
            var map = Object.Instantiate(prefab, _root.transform);
            _topology = map.GetComponent<BoardMapRoot>().Topology; _topology.RebuildIndex();
            typeof(NetworkMatchState).GetField("_boardTopology", Private).SetValue(_match, _topology);
            typeof(NetworkPlayerAvatar).GetField("_topology", Private).SetValue(_player, _topology);
            var gate = _topology.Gates.First(g => BoardWallRules.IsCanonical(g) && BoardWallRules.CanClose(_topology, g));
            _player.transform.position = gate.Source.WorldCenter;
            _player.EyePivot.position = _player.transform.position + Vector3.up * 1.5f;
            _player.EyePivot.forward = gate.ForwardNormal;
            Set(_player, "_logicalTileCoordinate", gate.Source.Coordinate);
            Set(_match, "_boardMapId", new FixedString64Bytes(BoardWallRules.MapId));
            Physics.SyncTransforms();
            Assert.That(_player.EquipChestItemOnServer(PrototypeItemId.Wall), Is.True);
            return gate;
        }

        [Test]
        public void Wall_RejectsUnauthorizedInvalidAndOccupiedPlacementWithoutConsumption_ThenBlocksBothWaysOnce()
        {
            var gate = PrepareWall();
            var eye = _player.EyePivot.position;
            foreach (var denied in new[] { "map", "client", "pause", "dead", "pending", "remote", "direction" })
            {
                Set(_match, "_boardMapId", new FixedString64Bytes(denied == "map" ? BoardForestGhostRules.MapId : BoardWallRules.MapId));
                Authority(_match, denied != "client");
                Set(_match, "_playerPauseActive", denied == "pause");
                Set(_player, "_currentHealth", denied == "dead" ? 0 : 100);
                Set(_player, "_choiceResolution", (byte)(denied == "pending" ? ItemChoiceResolution.Pending : ItemChoiceResolution.ItemSelected));
                Assert.That(_match.TryUseSelectedItemOnServer(_player,
                    eye + (denied == "remote" ? Vector3.up * 10 : Vector3.zero),
                    denied == "direction" ? -gate.ForwardNormal : gate.ForwardNormal), Is.False, denied);
                Assert.That(Get<int>(_player, "_itemCharges"), Is.EqualTo(1), denied);
            }
            var blocker = Child("Player inside wall").AddComponent<NetworkPlayerAvatar>();
            Authority(blocker, true); Set(blocker, "_slot", 1); Set(blocker, "_boardReady", true);
            Set(blocker, "_currentHealth", 100); blocker.transform.position = BoardWallRules.Position(gate);
            Assert.That(_match.TryGetWallPlacement(_player, eye, gate.ForwardNormal, out _), Is.False);
            blocker.transform.position = Vector3.up * 100;
            Assert.That(_match.TryGetWallPlacement(_player, eye, gate.ForwardNormal, out var target), Is.True);
            Assert.That(target, Is.SameAs(gate));
            var revision = _topology.TraversalRevision;
            LogAssert.Expect(LogType.Error, RpcUnavailable);
            Assert.That(_match.TryUseSelectedItemOnServer(_player, eye, gate.ForwardNormal), Is.True);
            Assert.That(_match.PlacedWallCount, Is.EqualTo(1));
            Assert.That(gate.IsTemporarilyBlocked && BoardWallRules.Reverse(_topology, gate).IsTemporarilyBlocked, Is.True);
            Assert.That(_topology.TraversalRevision, Is.GreaterThan(revision));
            Assert.That(Get<int>(_player, "_itemCharges"), Is.Zero);
            Assert.That(_match.TryUseSelectedItemOnServer(_player, eye, gate.ForwardNormal), Is.False);
            Assert.That(_player.EquipChestItemOnServer(PrototypeItemId.Wall), Is.True);
            Assert.That(_match.TryUseSelectedItemOnServer(_player, eye, gate.ForwardNormal), Is.False, "Duplicate wall.");
            Assert.That(Get<int>(_player, "_itemCharges"), Is.EqualTo(1));
        }

        [Test]
        public void Wall_ReplicationRestoreAndExpiry_KeepFiveTurnsAndReopenAtExactBoundary()
        {
            var gate = PrepareWall();
            LogAssert.Expect(LogType.Error, RpcUnavailable);
            Assert.That(_match.TryUseSelectedItemOnServer(_player, _player.EyePivot.position, gate.ForwardNormal), Is.True);
            var installedAt = _match.Turn;
            var wall = _match.GetPlacedWall(0);
            Assert.That(wall.ExpiresAtTurn, Is.EqualTo(installedAt + 5));
            Invoke("ClearBoardWallViews"); Assert.That(gate.IsTemporarilyBlocked, Is.False);
            Invoke("RefreshBoardWallPresentation"); Assert.That(gate.IsTemporarilyBlocked, Is.True);
            var snapshot = new MatchRecoverySnapshot { boardMapId = BoardWallRules.MapId,
                checkpoint = MatchRecoveryCheckpoint.TurnOverview, turn = installedAt, walls = new[] { wall } };
            var validate = typeof(NetworkMatchState).GetMethod("ValidateRecoveryWalls", Private);
            Assert.That(validate.Invoke(_match, new object[] { snapshot }), Is.True);
            Invoke("ClearBoardWalls"); Assert.That(_match.PlacedWallCount, Is.Zero);
            Invoke("RestoreBoardWalls", (object)snapshot.walls); Invoke("RefreshBoardWallPresentation");
            Assert.That(_match.GetPlacedWall(0), Is.EqualTo(wall));
            snapshot.walls = new[] { new BoardWallSnapshot(new Vector2Int(100, 100), new Vector2Int(101, 100), wall.ExpiresAtTurn) };
            Assert.That(validate.Invoke(_match, new object[] { snapshot }), Is.False);
            Assert.That(_match.GetPlacedWall(0), Is.EqualTo(wall), "Invalid restore must not mutate current walls.");
            snapshot.walls = _topology.Gates.Where(BoardWallRules.IsCanonical)
                .Where(g => BoardWallRules.Reverse(_topology, g) != null)
                .Select(g => new BoardWallSnapshot(g.Source.Coordinate, g.Destination.Coordinate, wall.ExpiresAtTurn)).ToArray();
            Assert.That(validate.Invoke(_match, new object[] { snapshot }), Is.False, "Saved closures cannot isolate rooms.");
            for (var turn = installedAt; turn < installedAt + 5; turn++)
            {
                Set(_match, "_turn", turn); Invoke("ExpireBoardWallsOnServer", turn);
                Assert.That(_match.PlacedWallCount, Is.EqualTo(1)); Assert.That(gate.IsTemporarilyBlocked, Is.True);
            }
            Set(_match, "_turn", installedAt + 5); Invoke("ExpireBoardWallsOnServer", installedAt + 5);
            Assert.That(_match.PlacedWallCount, Is.Zero);
            Assert.That(gate.IsTemporarilyBlocked || BoardWallRules.Reverse(_topology, gate).IsTemporarilyBlocked, Is.False);
            Assert.That(BoardWallRules.CanClose(_topology, gate), Is.True);
        }

        [Test]
        public void Wall_OnlyAppearsInMazeShopRewardAndChestPools()
        {
            var found = new bool[3];
            for (var seed = 0; seed < 200; seed++)
            foreach (var map in new[] { BoardWallRules.MapId, BoardForestGhostRules.MapId })
            {
                var reward = PrototypeItemCatalog.GetRandomId(new System.Random(seed), map);
                var chest = BoardChestRules.DrawItem(PrototypeItemCatalog.All, seed, map);
                var shop = new ItemShopStock(seed, map);
                var offers = Enumerable.Range(0, ItemShopRules.OfferCount).Select(shop.GetOffer).ToArray();
                if (map == BoardWallRules.MapId)
                {
                    found[0] |= reward == PrototypeItemId.Wall; found[1] |= chest == PrototypeItemId.Wall;
                    found[2] |= offers.Contains(PrototypeItemId.Wall);
                    if (found[2] && offers.Contains(PrototypeItemId.Wall))
                        Assert.Throws<System.ArgumentException>(() => ItemShopStock.Restore(offers, 0, mapId: BoardForestGhostRules.MapId));
                }
                else Assert.That(reward != PrototypeItemId.Wall && chest != PrototypeItemId.Wall &&
                    !offers.Contains(PrototypeItemId.Wall), Is.True);
            }
            Assert.That(found, Is.All.True);
        }
    }
}

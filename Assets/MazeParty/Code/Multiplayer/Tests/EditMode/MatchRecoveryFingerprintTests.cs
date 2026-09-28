using System.Collections.Generic;
using System.Reflection;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MatchRecoveryFingerprintTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
            {
                Object.DestroyImmediate(_objects[index]);
            }
            _objects.Clear();
        }

        [Test]
        public void PlayerKey_IsStableWithoutPersistingRawIdentity()
        {
            var first = MatchRecoveryFingerprint.CreatePlayerKey(" account-id ");
            var second = MatchRecoveryFingerprint.CreatePlayerKey("account-id");

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(first, Does.Not.Contain("account-id"));
        }

        [Test]
        public void ContentFingerprint_ChangesWithRecoveryRelevantBoardGeometry()
        {
            var source = CreateTile(new Vector2Int(0, 0));
            var destination = CreateTile(new Vector2Int(1, 0));
            var gate = CreateObject("Gate").AddComponent<BoardGate>();
            gate.Configure(source, destination);
            var topology = CreateObject("Topology").AddComponent<BoardTopology>();
            topology.Configure(new[] { source, destination }, new[] { gate });
            var schedule = HostMinigameSchedule.Create(1234);

            var original = MatchRecoveryFingerprint.CreateContentFingerprint(
                topology,
                schedule);
            destination.transform.position = new Vector3(8f, 0f, 0f);
            var moved = MatchRecoveryFingerprint.CreateContentFingerprint(
                topology,
                schedule);

            Assert.That(moved, Is.Not.EqualTo(original));

            destination.Configure(new Vector2Int(2, 0), BoardTileType.Normal);
            topology.RebuildIndex();
            var changed = MatchRecoveryFingerprint.CreateContentFingerprint(
                topology,
                schedule);

            Assert.That(changed, Is.Not.EqualTo(moved));
        }

        [Test]
        public void ContentFingerprint_CanRecreateTheVersionOneJournalIdentity()
        {
            var tile = CreateTile(Vector2Int.zero);
            var topology = CreateObject("Topology").AddComponent<BoardTopology>();
            topology.Configure(new[] { tile }, System.Array.Empty<BoardGate>());
            var schedule = HostMinigameSchedule.Create(1234);

            var current = MatchRecoveryFingerprint.CreateContentFingerprint(
                topology,
                schedule);
            var legacy = MatchRecoveryFingerprint.CreateContentFingerprint(
                topology,
                schedule,
                MatchRecoverySnapshot.LegacyRecoveryVersionWithoutMines);

            Assert.That(legacy, Is.Not.EqualTo(current));
            Assert.That(
                legacy,
                Is.EqualTo(MatchRecoveryFingerprint.CreateContentFingerprint(
                    topology,
                    schedule,
                    1)));
        }

        [Test]
        public void ContentFingerprint_ChangesWithGateTraversalContractAndOrder()
        {
            var first = CreateTile(new Vector2Int(0, 0));
            var second = CreateTile(new Vector2Int(1, 0));
            var third = CreateTile(new Vector2Int(2, 0));
            var firstGate = CreateObject("First Gate").AddComponent<BoardGate>();
            var secondGate = CreateObject("Second Gate").AddComponent<BoardGate>();
            firstGate.Configure(first, second, 2.5f);
            secondGate.Configure(second, third, 2.5f);
            var topology = CreateObject("Topology").AddComponent<BoardTopology>();
            topology.Configure(
                new[] { first, second, third },
                new[] { firstGate, secondGate });
            var schedule = HostMinigameSchedule.Create(4321);

            var original = MatchRecoveryFingerprint.CreateContentFingerprint(
                topology,
                schedule);
            firstGate.transform.position = new Vector3(0.25f, 0f, 0f);
            var moved = MatchRecoveryFingerprint.CreateContentFingerprint(
                topology,
                schedule);
            Assert.That(moved, Is.Not.EqualTo(original));

            firstGate.transform.rotation = Quaternion.Euler(0f, 15f, 0f);
            var rotated = MatchRecoveryFingerprint.CreateContentFingerprint(
                topology,
                schedule);
            Assert.That(rotated, Is.Not.EqualTo(moved));

            firstGate.Configure(first, second, 3.5f);
            var widened = MatchRecoveryFingerprint.CreateContentFingerprint(
                topology,
                schedule);
            Assert.That(widened, Is.Not.EqualTo(rotated));

            var epsilonField = typeof(BoardGate).GetField(
                "crossingEpsilon",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(epsilonField, Is.Not.Null);
            epsilonField.SetValue(firstGate, 0.05f);
            var epsilonChanged =
                MatchRecoveryFingerprint.CreateContentFingerprint(
                    topology,
                    schedule);
            Assert.That(epsilonChanged, Is.Not.EqualTo(widened));

            topology.Configure(
                new[] { first, second, third },
                new[] { secondGate, firstGate });
            var reordered = MatchRecoveryFingerprint.CreateContentFingerprint(
                topology,
                schedule);
            Assert.That(reordered, Is.Not.EqualTo(epsilonChanged));
        }

        [Test]
        public void ContentFingerprint_ChangesWithAuthoredFootprint()
        {
            var tile = CreateTile(Vector2Int.zero);
            var footprint = tile.gameObject.AddComponent<BoardTileFootprint>();
            footprint.Configure(new[]
            {
                new Vector2(-2f, -2f),
                new Vector2(2f, -2f),
                new Vector2(0f, 2f)
            });
            var topology = CreateObject("Topology").AddComponent<BoardTopology>();
            topology.Configure(new[] { tile }, System.Array.Empty<BoardGate>());
            var schedule = HostMinigameSchedule.Create(3412);

            var original = MatchRecoveryFingerprint.CreateContentFingerprint(
                topology,
                schedule);
            footprint.Configure(new[]
            {
                new Vector2(-3f, -2f),
                new Vector2(2f, -2f),
                new Vector2(0f, 2f)
            });
            var reshaped = MatchRecoveryFingerprint.CreateContentFingerprint(
                topology,
                schedule);

            Assert.That(reshaped, Is.Not.EqualTo(original));
        }

        [Test]
        public void ContentFingerprint_ChangesWithMapIdentityAndContentVersion()
        {
            var root = CreateObject("Map Root");
            var tile = CreateTile(Vector2Int.zero);
            tile.transform.SetParent(root.transform, false);
            var topology = root.AddComponent<BoardTopology>();
            topology.Configure(new[] { tile }, System.Array.Empty<BoardGate>());
            var mapRoot = root.AddComponent<BoardMapRoot>();
            var definition = ScriptableObject.CreateInstance<BoardMapDefinition>();
            var schedule = HostMinigameSchedule.Create(4312);
            try
            {
                definition.Configure("forest", "Forest", 1, null);
                mapRoot.Configure(
                    definition,
                    topology,
                    root.transform,
                    root.transform,
                    root.transform,
                    tile,
                    System.Array.Empty<Transform>());
                var first = MatchRecoveryFingerprint.CreateContentFingerprint(
                    topology,
                    schedule);

                definition.Configure("forest", "Forest", 2, null);
                var versionChanged = MatchRecoveryFingerprint.CreateContentFingerprint(
                    topology,
                    schedule);
                Assert.That(versionChanged, Is.Not.EqualTo(first));

                definition.Configure("forest-night", "Forest Night", 2, null);
                var identityChanged = MatchRecoveryFingerprint.CreateContentFingerprint(
                    topology,
                    schedule);
                Assert.That(identityChanged, Is.Not.EqualTo(versionChanged));
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        private BoardTile CreateTile(Vector2Int coordinate)
        {
            var tile = CreateObject("Tile " + coordinate).AddComponent<BoardTile>();
            tile.Configure(coordinate, BoardTileType.Normal);
            return tile;
        }

        private GameObject CreateObject(string name)
        {
            var value = new GameObject(name);
            _objects.Add(value);
            return value;
        }
    }
}

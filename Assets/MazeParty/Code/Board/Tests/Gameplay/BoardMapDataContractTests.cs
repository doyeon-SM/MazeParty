using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardMapDataContractTests
    {
        [Test]
        public void MapId_IsNormalizedToStableKebabCase()
        {
            var cases = new[]
            {
                (Source: "Forest Map", Expected: "forest-map"),
                (Source: "  FOREST__02  ", Expected: "forest-02"),
                (Source: "forest-map", Expected: "forest-map"),
                (Source: "---forest---map---", Expected: "forest-map")
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    BoardMapDefinition.NormalizeMapId(testCase.Source),
                    Is.EqualTo(testCase.Expected),
                    testCase.Source);
                Assert.That(
                    BoardMapDefinition.IsValidMapId(testCase.Expected),
                    Is.True,
                    testCase.Source);
            }
        }

        [Test]
        public void Catalog_RejectsDuplicateOrInvalidDefinitions()
        {
            var first = ScriptableObject.CreateInstance<BoardMapDefinition>();
            var duplicate = ScriptableObject.CreateInstance<BoardMapDefinition>();
            var catalog = ScriptableObject.CreateInstance<BoardMapCatalog>();
            try
            {
                first.Configure("forest", "Forest", 1, null);
                duplicate.Configure("forest", "Another Forest", 2, null);
                catalog.Configure(new[] { first, duplicate });

                Assert.That(catalog.HasUniqueValidIds(), Is.False);
                Assert.That(catalog.TryGetMap("forest", out var found), Is.True);
                Assert.That(found, Is.SameAs(first));
                Assert.That(catalog.TryGetMap("Forest", out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(duplicate);
                Object.DestroyImmediate(first);
            }
        }

        [Test]
        public void Catalog_ClonesConfiguredArray()
        {
            var definition = ScriptableObject.CreateInstance<BoardMapDefinition>();
            var catalog = ScriptableObject.CreateInstance<BoardMapCatalog>();
            try
            {
                definition.Configure("forest", "Forest", 0, null);
                var source = new[] { definition };
                catalog.Configure(source);
                source[0] = null;

                Assert.That(catalog.Maps[0], Is.SameAs(definition));
                Assert.That(definition.ContentVersion, Is.EqualTo(1));
                Assert.That(catalog.HasUniqueValidIds(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void MapRoot_KeepsEnvironmentSeparateAndClonesSpawnBindings()
        {
            var rootObject = new GameObject("Map Root");
            var topologyObject = new GameObject("Topology");
            var tilesObject = new GameObject("Tiles");
            var connectionsObject = new GameObject("Connections");
            var environmentObject = new GameObject("Environment");
            var spawnObject = new GameObject("Spawn");
            try
            {
                topologyObject.transform.SetParent(rootObject.transform);
                tilesObject.transform.SetParent(topologyObject.transform);
                connectionsObject.transform.SetParent(topologyObject.transform);
                environmentObject.transform.SetParent(rootObject.transform);
                spawnObject.transform.SetParent(rootObject.transform);

                var mapRoot = rootObject.AddComponent<BoardMapRoot>();
                var topology = topologyObject.AddComponent<BoardTopology>();
                var spawns = new[] { spawnObject.transform };
                mapRoot.Configure(
                    null,
                    topology,
                    tilesObject.transform,
                    connectionsObject.transform,
                    environmentObject.transform,
                    null,
                    spawns);
                spawns[0] = null;

                Assert.That(mapRoot.HasAuthoringRoots, Is.True);
                Assert.That(mapRoot.EnvironmentRoot, Is.SameAs(environmentObject.transform));
                Assert.That(mapRoot.PlayerSpawnAnchors[0], Is.SameAs(spawnObject.transform));
                Assert.That(mapRoot.PlayerSpawnAnchors.Count, Is.EqualTo(PlayerSlotRules.Count));
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void MapRoot_PlayerStartFallsBackToSharedStart()
        {
            var rootObject = new GameObject("Map Root");
            var sharedObject = new GameObject("Shared Start");
            var assignedObject = new GameObject("Assigned Start");
            var spawnObject = new GameObject("Spawn");
            try
            {
                sharedObject.transform.SetParent(rootObject.transform);
                assignedObject.transform.SetParent(rootObject.transform);
                spawnObject.transform.SetParent(rootObject.transform);
                var mapRoot = rootObject.AddComponent<BoardMapRoot>();
                var shared = sharedObject.AddComponent<BoardTile>();
                var assigned = assignedObject.AddComponent<BoardTile>();
                var playerStarts = new BoardTile[PlayerSlotRules.Count];
                playerStarts[1] = assigned;
                var spawns = new Transform[PlayerSlotRules.Count];
                spawns[1] = spawnObject.transform;

                mapRoot.Configure(
                    null,
                    null,
                    null,
                    null,
                    null,
                    shared,
                    playerStarts,
                    spawns);
                playerStarts[1] = null;
                spawns[1] = null;

                Assert.That(mapRoot.GetStartTile(0), Is.SameAs(shared));
                Assert.That(mapRoot.GetStartTile(1), Is.SameAs(assigned));
                Assert.That(mapRoot.TryGetStartAssignment(1, out var tile, out var anchor), Is.True);
                Assert.That(tile, Is.SameAs(assigned));
                Assert.That(anchor, Is.SameAs(spawnObject.transform));
                Assert.That(mapRoot.TryGetStartAssignment(-1, out _, out _), Is.False);
                Assert.Throws<System.ArgumentOutOfRangeException>(() => mapRoot.GetStartTile(4));
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }
    }
}

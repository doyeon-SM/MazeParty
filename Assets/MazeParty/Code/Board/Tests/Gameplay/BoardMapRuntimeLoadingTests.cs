using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardMapRuntimeLoadingTests
    {
        [Test]
        public void Selection_RequiresStableIdentityAndVersion()
        {
            (string MapId, int Version, bool Expected)[] cases =
            {
                (null, 0, true),
                (string.Empty, 0, true),
                ("forest-graybox", 1, true),
                ("Forest Graybox", 1, false),
                ("forest-graybox", 0, false),
                (string.Empty, 1, false)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    BoardMapSelection.TryCreate(
                        testCase.MapId,
                        testCase.Version,
                        out _),
                    Is.EqualTo(testCase.Expected),
                    (testCase.MapId ?? "<null>") + " / " +
                    testCase.Version);
            }
        }

        [Test]
        public void Selection_RejectsIdentityThatCannotFitNetworkPayload()
        {
            var tooLong = new string(
                'a',
                BoardMapSelection.MaximumMapIdLength + 1);

            Assert.That(
                BoardMapSelection.TryCreate(tooLong, 1, out _),
                Is.False);
        }

        [Test]
        public void EmptyCatalog_SelectsAndActivatesLegacyBoard()
        {
            var fixture = CreateFixture(includeRuntimeMap: false);
            try
            {
                Assert.That(
                    fixture.Loader.TryResolveFreshSelection(
                        out var selection,
                        out var resolveError),
                    Is.True,
                    resolveError);
                Assert.That(selection, Is.EqualTo(BoardMapSelection.Legacy));
                Assert.That(
                    fixture.Loader.TryActivate(selection, out var loadError),
                    Is.True,
                    loadError);
                Assert.That(fixture.Loader.IsReady, Is.True);
                Assert.That(fixture.LegacyContent.activeSelf, Is.True);
                Assert.That(
                    fixture.Loader.RuntimeTopology.Tiles.Single(),
                    Is.SameAs(fixture.LegacyTile));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void CatalogMap_MirrorsTopologyAndResolvesAllAuthoredStarts()
        {
            var fixture = CreateFixture(includeRuntimeMap: true);
            try
            {
                Assert.That(
                    fixture.Loader.TryResolveFreshSelection(
                        out var selection,
                        out var resolveError),
                    Is.True,
                    resolveError);
                Assert.That(selection.MapId, Is.EqualTo("test-map"));
                Assert.That(selection.ContentVersion, Is.EqualTo(7));
                Assert.That(
                    fixture.Loader.TryActivate(selection, out var loadError),
                    Is.True,
                    loadError);

                var proxy = fixture.Loader.RuntimeTopology;
                var activeRoot =
                    BoardMapRuntimeLoader.ResolveActiveMapRoot(proxy);
                Assert.That(activeRoot, Is.SameAs(fixture.Loader.RuntimeMapRoot));
                Assert.That(fixture.LegacyContent.activeSelf, Is.False);
                Assert.That(proxy.Tiles.Count, Is.EqualTo(4));

                for (var slot = 0; slot < PlayerSlotRules.Count; slot++)
                {
                    var start = activeRoot.GetStartTile(slot);
                    Assert.That(start, Is.Not.Null, $"P{slot + 1} start is missing.");
                    Assert.That(proxy.Tiles.Contains(start), Is.True,
                        $"P{slot + 1} start must belong to the mirrored runtime topology.");
                    Assert.That(activeRoot.GetSpawnAnchor(slot), Is.Not.Null);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void RecoverySelection_RejectsMissingContentVersion()
        {
            var fixture = CreateFixture(includeRuntimeMap: true);
            try
            {
                Assert.That(
                    fixture.Loader.TryResolveExactSelection(
                        new BoardMapSelection("test-map", 8),
                        out _,
                        out var error),
                    Is.False);
                Assert.That(error, Does.Contain("test-map@8"));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private static Fixture CreateFixture(bool includeRuntimeMap)
        {
            var host = new GameObject("Runtime Loader Host");
            var legacyTopologyObject = new GameObject("Legacy Topology");
            legacyTopologyObject.transform.SetParent(host.transform);
            var legacyTopology = legacyTopologyObject.AddComponent<BoardTopology>();
            var legacyContent = new GameObject("Legacy Content");
            legacyContent.transform.SetParent(legacyTopologyObject.transform);
            var legacyTileObject = new GameObject("Legacy Start");
            legacyTileObject.transform.SetParent(legacyContent.transform);
            var legacyTile = legacyTileObject.AddComponent<BoardTile>();
            legacyTile.Configure(Vector2Int.zero, BoardTileType.Start);
            legacyTopology.Configure(new[] { legacyTile }, Array.Empty<BoardGate>());

            var catalog = ScriptableObject.CreateInstance<BoardMapCatalog>();
            BoardMapDefinition definition = null;
            GameObject template = null;
            if (includeRuntimeMap)
            {
                definition = ScriptableObject.CreateInstance<BoardMapDefinition>();
                template = CreateMapTemplate(definition);
                definition.Configure("test-map", "Test Map", 7, template);
                catalog.Configure(new[] { definition });
            }
            else
            {
                catalog.Configure(Array.Empty<BoardMapDefinition>());
            }

            var loader = host.AddComponent<BoardMapRuntimeLoader>();
            loader.Configure(
                catalog,
                legacyTopology,
                new[] { legacyContent },
                host.transform);
            return new Fixture(
                host,
                catalog,
                definition,
                template,
                loader,
                legacyContent,
                legacyTile);
        }

        private static GameObject CreateMapTemplate(BoardMapDefinition definition)
        {
            var root = new GameObject("Test Map Root");
            var mapRoot = root.AddComponent<BoardMapRoot>();
            var topologyObject = new GameObject("Topology");
            topologyObject.transform.SetParent(root.transform);
            var topology = topologyObject.AddComponent<BoardTopology>();
            var tilesRoot = new GameObject("Tiles").transform;
            tilesRoot.SetParent(topologyObject.transform);
            var connectionsRoot = new GameObject("Connections").transform;
            connectionsRoot.SetParent(topologyObject.transform);
            var environmentRoot = new GameObject("Environment").transform;
            environmentRoot.SetParent(root.transform);

            var starts = new BoardTile[PlayerSlotRules.Count];
            var anchors = new Transform[PlayerSlotRules.Count];
            for (var slot = 0; slot < PlayerSlotRules.Count; slot++)
            {
                var tileObject = new GameObject($"Start {slot + 1}");
                tileObject.transform.SetParent(tilesRoot);
                tileObject.transform.localPosition = new Vector3(slot * 8f, 0f, 0f);
                starts[slot] = tileObject.AddComponent<BoardTile>();
                starts[slot].Configure(new Vector2Int(slot, 0), BoardTileType.Start);

                var anchorObject = new GameObject($"Spawn {slot + 1}");
                anchorObject.transform.SetParent(root.transform);
                anchorObject.transform.position =
                    starts[slot].WorldCenter + Vector3.up;
                anchors[slot] = anchorObject.transform;
            }

            topology.Configure(starts, Array.Empty<BoardGate>());
            mapRoot.Configure(
                definition,
                topology,
                tilesRoot,
                connectionsRoot,
                environmentRoot,
                starts[0],
                starts,
                anchors);
            return root;
        }

        private sealed class Fixture : IDisposable
        {
            public Fixture(
                GameObject host,
                BoardMapCatalog catalog,
                BoardMapDefinition definition,
                GameObject template,
                BoardMapRuntimeLoader loader,
                GameObject legacyContent,
                BoardTile legacyTile)
            {
                Host = host;
                Catalog = catalog;
                Definition = definition;
                Template = template;
                Loader = loader;
                LegacyContent = legacyContent;
                LegacyTile = legacyTile;
            }

            public GameObject Host { get; }
            public BoardMapCatalog Catalog { get; }
            public BoardMapDefinition Definition { get; }
            public GameObject Template { get; }
            public BoardMapRuntimeLoader Loader { get; }
            public GameObject LegacyContent { get; }
            public BoardTile LegacyTile { get; }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Host);
                if (Template != null)
                {
                    UnityEngine.Object.DestroyImmediate(Template);
                }
                if (Definition != null)
                {
                    UnityEngine.Object.DestroyImmediate(Definition);
                }
                UnityEngine.Object.DestroyImmediate(Catalog);
            }
        }
    }
}

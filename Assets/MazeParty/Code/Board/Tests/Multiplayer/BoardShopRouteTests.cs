using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardShopRouteTests
    {
        [Test]
        public void LocalGuide_FollowsDirectedCentersAndClearsOldRoutesWithoutNetworkingOrCollision()
        {
            var world = new GameObject("Board fixture");
            var presentation = new GameObject("Client-local guide");
            try
            {
                var coordinates = new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.one };
                var tiles = new BoardTile[3];
                for (var i = 0; i < tiles.Length; i++)
                {
                    var room = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    room.transform.SetParent(world.transform, false);
                    room.transform.position = new Vector3(coordinates[i].x * 8f, 2f, coordinates[i].y * 8f);
                    room.transform.localScale = new Vector3(7.72f, .2f, 7.72f);
                    tiles[i] = room.AddComponent<BoardTile>();
                    tiles[i].Configure(coordinates[i], BoardTileType.Normal);
                }
                var gates = new BoardGate[2];
                for (var i = 0; i < gates.Length; i++)
                {
                    var gate = new GameObject("Directed gate");
                    gate.transform.SetParent(world.transform, false);
                    gates[i] = gate.AddComponent<BoardGate>();
                    gates[i].Configure(tiles[i], tiles[i + 1], 8f);
                }
                var topology = world.AddComponent<BoardTopology>();
                topology.Configure(tiles, gates);
                var view = presentation.AddComponent<BoardShopRouteView>();
                view.Configure(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/MazeParty/Prefabs/Board/World/KeyShopRouteHemisphere.prefab"));
                Assert.That(view.HasRequiredReferences, Is.True);
                view.PresentRoute(topology, tiles[0], tiles[2]);
                var dots = presentation.GetComponentsInChildren<MeshRenderer>();
                Assert.That(dots.Length, Is.GreaterThan(3));
                Assert.That(presentation.GetComponentsInChildren<NetworkObject>(true), Is.Empty);
                Assert.That(presentation.GetComponentsInChildren<Collider>(true), Is.Empty);
                foreach (var dot in dots)
                {
                    var p = dot.transform.position;
                    Assert.That(p.y, Is.GreaterThan(2.1f), "Dots must sit above the floor, not its volume center.");
                    Assert.That(Mathf.Abs(p.z) < .001f || Mathf.Abs(p.x - 8f) < .001f, Is.True,
                        "The guide must turn at the tile center instead of taking a diagonal shortcut.");
                }
                Assert.That(dots.Any(dot => Vector2.Distance(new Vector2(dot.transform.position.x, dot.transform.position.z), new Vector2(8f, 0f)) < .001f), Is.True);
                foreach (var phase in new[] { BoardFlowState.TurnOverview, BoardFlowState.Descending,
                    BoardFlowState.Action, BoardFlowState.MinigameIntroReady, BoardFlowState.TurnOverview })
                {
                    view.PresentRouteForPhase(topology, tiles[0], tiles[2], phase);
                    Assert.That(presentation.GetComponentsInChildren<MeshRenderer>().Length,
                        Is.EqualTo(phase == BoardFlowState.MinigameIntroReady ? 0 : dots.Length),
                        "The local floor route must appear before rolling and return on the next overview.");
                }
                var allocated = presentation.transform.childCount;
                view.SetVisible(false);
                Assert.That(presentation.GetComponentsInChildren<MeshRenderer>(), Is.Empty);
                view.PresentRoute(topology, tiles[0], tiles[2]);
                Assert.That(presentation.GetComponentsInChildren<MeshRenderer>().Length, Is.EqualTo(dots.Length));
                Assert.That(presentation.transform.childCount, Is.EqualTo(allocated), "Unhiding must reuse the pool.");
                view.PresentRoute(topology, tiles[1], tiles[2]);
                Assert.That(presentation.GetComponentsInChildren<MeshRenderer>().All(dot => Mathf.Abs(dot.transform.position.x - 8f) < .001f), Is.True);
                foreach (var source in new[] { tiles[2], tiles[1], tiles[0] })
                {
                    view.PresentRoute(topology, source, source == tiles[0] ? null : tiles[0]);
                    Assert.That(presentation.GetComponentsInChildren<MeshRenderer>(), Is.Empty, "No route or no shop must hide previous dots.");
                }
                view.PresentRoute(topology, tiles[0], tiles[0]);
                Assert.That(presentation.GetComponentsInChildren<MeshRenderer>(), Is.Empty, "Already at the shop has no remaining route.");
            }
            finally { Object.DestroyImmediate(presentation); Object.DestroyImmediate(world); }
        }
    }
}

using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardMapKnowledgePresentationTests
    {
        [Test]
        public void BothMaps_RetainOnlyObservedCoordinatesUntilTheOpponentIsSeenAgain()
        {
            var root = new GameObject("Observed opponent", typeof(NetworkObject));
            try
            {
                var avatar = root.AddComponent<NetworkPlayerAvatar>();
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(avatar, true);
                var knowledge = new BoardPlayerMapKnowledge();
                var query = typeof(BoardMinimapView).GetMethod("TryGetPlayerCoordinate",
                    BindingFlags.Static | BindingFlags.NonPublic);
                var last = new Vector2Int(3, 4);
                var next = new Vector2Int(8, 7);
                foreach (var context in new[] { BoardMinimapDisplayContext.Minimap,
                    BoardMinimapDisplayContext.FullMap, BoardMinimapDisplayContext.TurnOverview })
                {
                    knowledge.Reset();
                    var args = new object[] { avatar, null, 1, context, knowledge, default(Vector2Int) };
                    Assert.That(query.Invoke(null, args), Is.False, "Unknown opponents must not be exposed.");
                    knowledge.BeginObservationFrame();
                    knowledge.Observe(1, last);
                    knowledge.EndObservationFrame();
                    Assert.That(query.Invoke(null, args), Is.True);
                    Assert.That(args[5], Is.EqualTo(last));

                    knowledge.ClearCurrentVisibility();
                    avatar.transform.position = new Vector3(80f, 0f, 70f);
                    Assert.That(query.Invoke(null, args), Is.True, context.ToString());
                    Assert.That(args[5], Is.EqualTo(last), "Unobserved movement cannot move a remembered marker.");
                    knowledge.BeginObservationFrame();
                    knowledge.Observe(1, next);
                    knowledge.EndObservationFrame();
                    Assert.That(query.Invoke(null, args), Is.True);
                    Assert.That(args[5], Is.EqualTo(next));
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}

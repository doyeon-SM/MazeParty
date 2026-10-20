using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardHudRankingTests
    {
        [Test]
        public void AuthoredCards_FollowRanksKeepTiesStableAndRetargetAnActiveSwap()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Board/UI/BoardHudCanvas.prefab"));
            try
            {
                var hud = root.GetComponent<BoardHudCanvasBindings>();
                Assert.That(hud.HasRequiredReferences, Is.True);
                var cards = hud.PlayerCards;
                var parent = cards[0].transform.parent;
                Assert.That(cards.All(card => card.transform.parent == parent), Is.True);
                hud.PresentPlayerRanking(new[] { 1, 2, 3, 4 }, 0d);
                var positions = cards.Select(card => card.rectTransform.anchoredPosition).ToArray();
                Assert.That(positions[0].y, Is.GreaterThan(positions[1].y));

                hud.PresentPlayerRanking(new[] { 4, 3, 2, 1 }, 1d);
                for (var slot = 0; slot < 4; slot++)
                    Assert.That(cards[slot].rectTransform.anchoredPosition, Is.EqualTo(positions[slot]),
                        "Changing rank starts at the currently displayed position.");
                hud.TickPlayerRanking(1.3d);
                var inFlight = cards.Select(card => card.rectTransform.anchoredPosition).ToArray();
                Assert.That(inFlight[0].y, Is.LessThan(positions[0].y));
                Assert.That(inFlight[3].y, Is.GreaterThan(positions[3].y));
                hud.PresentPlayerRanking(new[] { 1, 2, 2, 4 }, 1.3d);
                for (var slot = 0; slot < 4; slot++)
                    Assert.That(cards[slot].rectTransform.anchoredPosition, Is.EqualTo(inFlight[slot]),
                        "An interrupted swap must not teleport to the previous target.");
                hud.TickPlayerRanking(3d);
                var expectedOrder = new[] { 0, 2, 1, 3 };
                for (var row = 0; row < 4; row++)
                    Assert.That(cards[expectedOrder[row]].rectTransform.anchoredPosition,
                        Is.EqualTo(positions[row]));

                hud.PresentPlayerRanking(new[] { 1, 1, 1, 1 }, 4d);
                hud.TickPlayerRanking(5d);
                for (var row = 0; row < 4; row++)
                    Assert.That(cards[expectedOrder[row]].rectTransform.anchoredPosition,
                        Is.EqualTo(positions[row]), "Ties retain their previous display order.");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}

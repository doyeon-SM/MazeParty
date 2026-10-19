using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MazeParty.Gameplay.Minigames.CoinCollect;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class CoinCollectRulesTests
    {
        [Test]
        public void Rain_IsSeededAndSuppliesExactQuantitiesInBothRounds()
        {
            var first = new CoinCollectMatchState(783UL);
            var second = new CoinCollectMatchState(783UL);
            var different = new CoinCollectMatchState(784UL);
            Assert.That(first.GetScheduledItem(0).Position, Is.Not.EqualTo(different.GetScheduledItem(0).Position));
            for (var round = 1; round <= 2; round++)
            {
                var counts = new int[3];
                for (var i = 0; i < CoinCollectRules.RainCount; i++)
                {
                    var item = first.GetScheduledItem(i);
                    var repeat = second.GetScheduledItem(i);
                    Assert.That(item.Position, Is.EqualTo(repeat.Position));
                    Assert.That(item.BornAt, Is.EqualTo(repeat.BornAt));
                    Assert.That(item.Kind, Is.EqualTo(repeat.Kind));
                    Assert.That(item.BornAt, Is.GreaterThanOrEqualTo(0d).And.LessThan(60d));
                    Assert.That(item.HeightAt(59.999d), Is.EqualTo(CoinCollectRules.GroundHeight));
                    counts[(int)item.Kind]++;
                }
                Assert.That(counts, Is.EqualTo(new[] { 50, 10, 10 }));
                first.AdvanceTo(60d); second.AdvanceTo(60d);
                Assert.That(first.RainSpawnedCount, Is.EqualTo(70));
                Assert.That(first.IsRoundComplete, Is.True);
                Assert.That(first.BeginNextRound(), Is.EqualTo(round == 1));
                Assert.That(second.BeginNextRound(), Is.EqualTo(round == 1));
            }
            Assert.That(first.IsComplete, Is.True);
        }

        [Test]
        public void Pickup_RequiresReachableHeightAndConsumesExactlyOnce()
        {
            var match = EmptyRound();
            var position = match.GetPlayer(0).Position;
            var items = Items(match);
            items.Add(new CoinCollectItemSnapshot(CoinCollectItemKind.Coin, position, 0d, 8f));
            var reachesPlayer = (8d - CoinCollectRules.PickupHeight) / CoinCollectRules.FallSpeed;
            match.AdvanceTo(reachesPlayer - .001d);
            Assert.That(match.TryCollect(0, 0), Is.False);
            match.AdvanceTo(reachesPlayer + .001d);
            Assert.That(match.GetPlayer(0).Score, Is.EqualTo(1));
            Assert.That(items[0].HeightAt(match.Elapsed), Is.GreaterThan(CoinCollectRules.GroundHeight), "Airborne collection must work.");
            Assert.That(match.TryCollect(0, 0), Is.False);
            items.Add(new CoinCollectItemSnapshot(CoinCollectItemKind.Money, position, match.Elapsed, .55f));
            Assert.That(match.TryCollect(0, 1), Is.True);
            Assert.That(match.GetPlayer(0).Score, Is.EqualTo(6));
            items.Add(new CoinCollectItemSnapshot(CoinCollectItemKind.Coin, position, match.Elapsed, .55f));
            match.AdvanceTo(60d);
            Assert.That(match.TryCollect(0, 2), Is.False, "No collection after the exact round deadline.");
        }

        [Test]
        public void Bomb_DropsFlooredHalfInFivesThenOnesAndLocksForExactlyOneSecond()
        {
            foreach (var score in new[] { 0, 1, 2, 9, 24, 25, 200 })
            {
                var match = EmptyRound();
                SetPlayer(match, 0, "Score", score);
                var items = Items(match);
                items.Add(new CoinCollectItemSnapshot(CoinCollectItemKind.Bomb, match.GetPlayer(0).Position, 0d, .55f));
                Assert.That(match.TryCollect(0, 0), Is.True);
                var dropped = score / 2;
                Assert.That(match.GetPlayer(0).Score, Is.EqualTo(score - dropped));
                Assert.That(items.Skip(1).Count(i => i.Kind == CoinCollectItemKind.Money), Is.EqualTo(dropped / 5));
                Assert.That(items.Skip(1).Count(i => i.Kind == CoinCollectItemKind.Coin), Is.EqualTo(dropped % 5));
                Assert.That(items.Skip(1).Sum(i => i.Kind == CoinCollectItemKind.Money ? 5 : 1), Is.EqualTo(dropped));
                Assert.That(match.TryPush(0), Is.False);
                var start = match.GetPlayer(0).Position;
                match.SetMovementInput(0, Vector2.right); match.AdvanceTo(.999d);
                Assert.That(match.GetPlayer(0).Position, Is.EqualTo(start));
                Assert.That(match.GetPlayer(0).StunRemaining, Is.GreaterThan(0d));
                match.AdvanceTo(1d);
                Assert.That(match.GetPlayer(0).StunRemaining, Is.Zero);
                if (dropped > 0)
                {
                    SetPlayer(match, 0, "Position", items[1].Position);
                    Assert.That(match.TryCollect(0, 1), Is.True, "The original owner may recollect after recovering.");
                }
            }
        }

        [Test]
        public void BombDrops_BlockEveryPlayerUntilExactUnlockTimeButRainRemainsCollectible()
        {
            foreach (var collector in new[] { 0, 1, 2, 3 })
            {
                var match = EmptyRound(); SetPlayer(match, 0, "Score", 24);
                var items = Items(match);
                match.AdvanceTo(10d);
                items.Add(new CoinCollectItemSnapshot(CoinCollectItemKind.Bomb, match.GetPlayer(0).Position, 10d, .55f));
                Assert.That(match.TryCollect(0, 0), Is.True);
                var protectedItem = items[1];
                var scoreBefore = match.GetPlayer(collector).Score;
                SetPlayer(match, collector, "Position", protectedItem.Position);
                foreach (var time in new[] { 10.1d, 10.5d, 10.999d })
                {
                    match.AdvanceTo(time);
                    Assert.That(match.TryCollect(collector, 1), Is.False);
                    Assert.That(items[1].Active, Is.True);
                    Assert.That(match.GetPlayer(collector).Score, Is.EqualTo(scoreBefore));
                }
                // The lock is per dropped item, not a global pickup cooldown.
                if (collector != 0)
                {
                    items.Add(new CoinCollectItemSnapshot(CoinCollectItemKind.Coin, protectedItem.Position, 10d, .55f));
                    Assert.That(match.TryCollect(collector, items.Count - 1), Is.True);
                    scoreBefore++;
                }
                match.AdvanceTo(11d);
                Assert.That(items[1].Active, Is.False, "Collection becomes available at the exact one-second boundary.");
                Assert.That(match.GetPlayer(collector).Score, Is.EqualTo(scoreBefore + 5));
                Assert.That(match.TryCollect(collector, 1), Is.False, "A collected item cannot be awarded twice.");
            }
        }

        [Test]
        public void Push_UsesCastStunAndCooldownBoundariesWithoutDroppingScore()
        {
            var match = EmptyRound();
            SetPlayer(match, 0, "Position", new Vector2(700, 0)); SetPlayer(match, 0, "Facing", Vector2.right);
            SetPlayer(match, 1, "Position", new Vector2(701, 0)); SetPlayer(match, 1, "Score", 25);
            var attemptedSlots = new List<int>();
            match.PushStarted += attemptedSlots.Add;
            Assert.That(match.TryPush(0), Is.True);
            Assert.That(attemptedSlots, Is.EqualTo(new[] { 0 }), "The attempt starts before hit resolution.");
            Assert.That(match.TryPush(0), Is.False);
            Assert.That(attemptedSlots.Count, Is.EqualTo(1), "Rejected repeats must not replay the animation.");
            match.AdvanceTo(.099d); Assert.That(match.GetPlayer(1).StunRemaining, Is.Zero);
            match.AdvanceTo(.1d); Assert.That(match.GetPlayer(1).StunRemaining, Is.EqualTo(.5d).Within(1e-8));
            Assert.That(match.GetPlayer(1).Position.x, Is.GreaterThan(701f));
            Assert.That(match.GetPlayer(1).Score, Is.EqualTo(25)); Assert.That(match.ItemCount, Is.Zero);
            match.AdvanceTo(.599d); Assert.That(match.GetPlayer(1).StunRemaining, Is.GreaterThan(0d));
            match.AdvanceTo(.6d); Assert.That(match.GetPlayer(1).StunRemaining, Is.Zero);
            match.AdvanceTo(.999d); Assert.That(match.TryPush(0), Is.False);
            match.AdvanceTo(1d); Assert.That(match.TryPush(0), Is.True);
            Assert.That(attemptedSlots, Is.EqualTo(new[] { 0, 0 }), "A miss must still animate when the cooldown allows the attempt.");
            match.InterruptInputs(); match.AdvanceTo(1.2d);
            Assert.That(match.GetPlayer(0).CastRemaining, Is.Zero, "A paused cast must not fire after reconnect.");
        }

        [Test]
        public void RoundTransition_RetainsScoresClearsObjectsAndUsesSharedCompetitionRanks()
        {
            var match = EmptyRound();
            foreach (var pair in new[] { (0, 20), (1, 15), (2, 15), (3, 5) }) SetPlayer(match, pair.Item1, "Score", pair.Item2);
            Items(match).Add(new CoinCollectItemSnapshot(CoinCollectItemKind.Coin, new Vector2(700, 0), 0d, .55f));
            match.AdvanceTo(59.999d);
            Assert.That(match.BeginNextRound(), Is.False);
            match.AdvanceTo(60d);
            Assert.That(match.BeginNextRound(), Is.True);
            Assert.That(match.ItemCount, Is.Zero);
            Assert.That(Enumerable.Range(0, 4).Select(slot => match.GetPlayer(slot).Score), Is.EqualTo(new[] { 20, 15, 15, 5 }));
            Assert.That(Enumerable.Range(0, 4).Select(match.GetRank), Is.EqualTo(new[] { 1, 2, 2, 4 }));
            foreach (var slot in Enumerable.Range(0, 4)) SetPlayer(match, slot, "Score", 0);
            Assert.That(Enumerable.Range(0, 4).Select(match.GetRank), Is.EqualTo(new[] { 1, 1, 1, 1 }));
        }
        private static CoinCollectMatchState EmptyRound()
        {
            var match = new CoinCollectMatchState(123);
            typeof(CoinCollectMatchState).GetField("_spawned", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(match, 70);
            return match;
        }
        private static List<CoinCollectItemSnapshot> Items(CoinCollectMatchState match) =>
            (List<CoinCollectItemSnapshot>)typeof(CoinCollectMatchState).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(match);
        private static void SetPlayer(CoinCollectMatchState match, int slot, string field, object value)
        {
            var players = (Array)typeof(CoinCollectMatchState).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(match);
            var player = players.GetValue(slot); player.GetType().GetField(field).SetValue(player, value);
        }
    }
}

using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.BalloonBlow;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BalloonBlowRulesTests
    {
        [Test]
        public void ReleaseAndNeutralInterrupt_ApplyOnlyDefinedCooldowns()
        {
            var round = new BalloonBlowRoundState(1);

            round.SetInflateHeld(0, true, 0d);
            round.AdvanceTo(1.5d);
            var player = round.GetPlayer(0);

            Assert.That(
                player.ProgressPercent,
                Is.EqualTo(15f).Within(0.0001f));
            Assert.That(
                player.Phase,
                Is.EqualTo(BalloonBlowPlayerPhase.Inflating));

            var released = round.SetInflateHeld(0, false, 1.5d);
            Assert.That(released.Status, Is.EqualTo(
                BalloonBlowInputStatus.Released));
            Assert.That(
                player.CooldownEndsAtSeconds,
                Is.EqualTo(2.5d));

            round.AdvanceTo(2.499d);
            Assert.That(
                player.Phase,
                Is.EqualTo(BalloonBlowPlayerPhase.Cooldown));
            round.AdvanceTo(2.5d);
            Assert.That(
                player.ProgressPercent,
                Is.EqualTo(12f).Within(0.0001f));
            Assert.That(
                player.Phase,
                Is.EqualTo(BalloonBlowPlayerPhase.Ready));

            round.SetInflateHeld(1, true, 2.5d);
            round.AdvanceTo(3d);
            var interruptedPlayer = round.GetPlayer(1);
            round.InterruptHeldInputs(3d);

            Assert.That(
                interruptedPlayer.ProgressPercent,
                Is.EqualTo(5f).Within(0.0001f));
            Assert.That(
                interruptedPlayer.Phase,
                Is.EqualTo(BalloonBlowPlayerPhase.Ready));
            Assert.That(
                interruptedPlayer.CooldownEndsAtSeconds,
                Is.Zero);
            round.AdvanceTo(4d);
            Assert.That(
                interruptedPlayer.ProgressPercent,
                Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void TwoSecondLimit_ForcesPenaltyAndRequiresReleaseToRearm()
        {
            var round = new BalloonBlowRoundState(1);
            round.SetInflateHeld(0, true, 0d);

            round.AdvanceTo(1.999d);
            var player = round.GetPlayer(0);
            Assert.That(
                player.Phase,
                Is.EqualTo(BalloonBlowPlayerPhase.Inflating));

            round.AdvanceTo(2d);
            Assert.That(
                player.ProgressPercent,
                Is.EqualTo(20f).Within(0.0001f));
            Assert.That(
                player.Phase,
                Is.EqualTo(BalloonBlowPlayerPhase.Cooldown));
            Assert.That(player.CooldownEndsAtSeconds, Is.EqualTo(3.5d));
            Assert.That(player.RequiresReleaseToRearm, Is.True);

            round.AdvanceTo(4d);
            Assert.That(
                player.ProgressPercent,
                Is.EqualTo(14f).Within(0.0001f));
            Assert.That(
                player.Phase,
                Is.EqualTo(BalloonBlowPlayerPhase.AwaitingRelease));

            round.SetInflateHeld(0, false, 4d);
            round.SetInflateHeld(0, true, 4d);
            round.AdvanceTo(5d);

            Assert.That(
                player.ProgressPercent,
                Is.EqualTo(24f).Within(0.0001f));
            Assert.That(
                player.Phase,
                Is.EqualTo(BalloonBlowPlayerPhase.Inflating));
        }

        [Test]
        public void OneHundredPercent_PopsAutomaticallyAndAllPoppedEndsEarly()
        {
            var round = new BalloonBlowRoundState(2);
            var elapsed = 0d;

            for (var cycle = 0; cycle < 6; cycle++)
            {
                SetAllHeld(round, true, elapsed);
                elapsed += 1.9d;
                round.AdvanceTo(elapsed);
                SetAllHeld(round, false, elapsed);
                elapsed += 1d;
                round.AdvanceTo(elapsed);
            }

            SetAllHeld(round, true, elapsed);
            elapsed += 0.4d;
            round.AdvanceTo(elapsed);

            Assert.That(round.IsComplete, Is.True);
            Assert.That(
                round.EndReason,
                Is.EqualTo(BalloonBlowRoundEndReason.AllPopped));
            Assert.That(round.ElapsedSeconds, Is.EqualTo(17.8d).Within(0.0001d));
            Assert.That(PlayerSlots(round.Result), Is.EqualTo(
                new[] { 0, 1, 2, 3 }));
            Assert.That(Points(round.Result), Is.EqualTo(
                new[] { 3, 2, 1, 0 }));

            for (var slot = 0; slot < BalloonBlowRules.PlayerCount; slot++)
            {
                Assert.That(round.GetPlayer(slot).IsPopped, Is.True);
                Assert.That(
                    round.GetPlayer(slot).ProgressPercent,
                    Is.EqualTo(100f));
                Assert.That(
                    round.GetPlayer(slot).PopOrder,
                    Is.EqualTo((ulong)slot + 1UL));
            }
        }

        [Test]
        public void TimeoutAndMatchScoring_UsePopThenProgressAndThreeRoundPoints()
        {
            var round = new BalloonBlowRoundState(3);
            round.SetInflateHeld(0, true, 28d);
            round.AdvanceTo(29d);
            round.SetInflateHeld(0, false, 29d);
            round.SetInflateHeld(1, true, 29d);
            round.AdvanceTo(29.5d);
            round.SetInflateHeld(1, false, 29.5d);

            Assert.That(round.TryEndForTimeout(29.999d), Is.False);
            Assert.That(round.TryEndForTimeout(30d), Is.True);
            Assert.That(
                round.EndReason,
                Is.EqualTo(BalloonBlowRoundEndReason.TimeLimit));
            Assert.That(PlayerSlots(round.Result), Is.EqualTo(
                new[] { 0, 1, 2, 3 }));

            var scored = BalloonBlowRoundScoring.Score(new[]
            {
                BalloonBlowRoundOutcome.Popped(2, 10d, 2UL),
                BalloonBlowRoundOutcome.Incomplete(3, 42f),
                BalloonBlowRoundOutcome.Popped(0, 10d, 1UL),
                BalloonBlowRoundOutcome.Incomplete(1, 42f)
            });
            Assert.That(PlayerSlots(scored), Is.EqualTo(
                new[] { 0, 2, 1, 3 }));

            var leaderboard = BalloonBlowMatchScoring.BuildLeaderboard(
                new[] { scored, scored, scored });
            Assert.That(LeaderboardSlots(leaderboard), Is.EqualTo(
                new[] { 0, 2, 1, 3 }));
            Assert.That(LeaderboardPoints(leaderboard), Is.EqualTo(
                new[] { 9, 6, 3, 0 }));
            Assert.Throws<System.ArgumentException>(() =>
                BalloonBlowMatchScoring.BuildLeaderboard(
                    new[] { scored, scored }));
        }

        private static void SetAllHeld(
            BalloonBlowRoundState round,
            bool isHeld,
            double elapsedSeconds)
        {
            for (var slot = 0; slot < BalloonBlowRules.PlayerCount; slot++)
            {
                round.SetInflateHeld(slot, isHeld, elapsedSeconds);
            }
        }

        private static int[] PlayerSlots(BalloonBlowRoundResult result)
        {
            var slots = new int[result.Standings.Count];
            for (var index = 0; index < slots.Length; index++)
            {
                slots[index] = result.Standings[index].PlayerSlot;
            }

            return slots;
        }

        private static int[] Points(BalloonBlowRoundResult result)
        {
            var points = new int[result.Standings.Count];
            for (var index = 0; index < points.Length; index++)
            {
                points[index] = result.Standings[index].Points;
            }

            return points;
        }

        private static int[] LeaderboardSlots(
            IReadOnlyList<BalloonBlowLeaderboardEntry> leaderboard)
        {
            var slots = new int[leaderboard.Count];
            for (var index = 0; index < slots.Length; index++)
            {
                slots[index] = leaderboard[index].PlayerSlot;
            }

            return slots;
        }

        private static int[] LeaderboardPoints(
            IReadOnlyList<BalloonBlowLeaderboardEntry> leaderboard)
        {
            var points = new int[leaderboard.Count];
            for (var index = 0; index < points.Length; index++)
            {
                points[index] = leaderboard[index].TotalPoints;
            }

            return points;
        }
    }
}

using System;
using System.Linq;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.CoinCollect;
using MazeParty.Gameplay.Minigames.Flight;
using MazeParty.Gameplay.Minigames.Race2;
using MazeParty.Gameplay.Minigames.BalloonBlow;
using MazeParty.Gameplay.Minigames.GiftGrab;
using MazeParty.Gameplay.Minigames.StableFooting;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using MazeParty.Gameplay.Minigames.WrongWay;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MatchGameSettingsTests
    {
        [Test]
        public void SelectionEnforcesTurnMinimum_AndScheduleSurvivesSerializationWithoutExcludedGames()
        {
            foreach (var turns in new[] { 5, 10, 15, 20 })
            {
                var settings = MatchGameSettings.Default;
                var allowed = turns != 5;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                allowed = true;
#endif
                Assert.That(settings.TryChangeTurns(turns, out settings), Is.EqualTo(allowed));
                var payload = MatchGameSettings.Default.Encode().Replace("1:15:", "1:" + turns + ":");
                Assert.That(MatchGameSettings.TryDecode(payload, out _), Is.EqualTo(allowed));
                if (!allowed) continue;
                for (var id = 20; id > turns; id--)
                {
                    var game = (ScheduledMinigameId)id;
                    var value = settings.Get(game);
                    Assert.That(settings.TryChangeGame(game, new MinigameSetting(false, value.Rounds, value.Seconds), out settings), Is.True);
                }
                var first = settings.Get(ScheduledMinigameId.Minefield);
                Assert.That(settings.TryChangeGame(ScheduledMinigameId.Minefield,
                    new MinigameSetting(false, first.Rounds, first.Seconds), out _), Is.False);
                Assert.That(MatchGameSettings.TryDecode(settings.Encode(), out var restored), Is.True);
                var schedule = HostMinigameSchedule.Create(417, turns, restored.IncludedGames());
                var again = HostMinigameSchedule.Create(417, turns, restored.IncludedGames());
                var codec = new HostMinigameScheduleJsonCodec();
                Assert.That(codec.TryDecode(codec.Encode("settings-test", schedule), out _, out var saved), Is.True);
                var entries = Enumerable.Range(1, turns).Select(schedule.GetMinigameForTurn).ToArray();
                Assert.That(entries.Distinct().Count(), Is.EqualTo(turns));
                Assert.That(entries.All(id => restored.Get(id).Included), Is.True);
                Assert.That(entries, Is.EqualTo(Enumerable.Range(1, turns).Select(again.GetMinigameForTurn)));
                Assert.That(entries, Is.EqualTo(Enumerable.Range(1, turns).Select(saved.GetMinigameForTurn)));
                if (turns < 20) Assert.That(settings.TryChangeTurns(turns + 5, out _), Is.False);
            }
            foreach (var turns in new[] { 0, 4, 6, 9, 11, 14, 16, 21, 25 })
                Assert.That(MatchGameSettings.Default.TryChangeTurns(turns, out _), Is.False);
        }

        [Test]
        public void SettingsRejectInvalidRangesAndCorruptWirePayloads()
        {
            var settings = MatchGameSettings.Default;
            const string savedDefault = "1:15:AQMDAQIEAQMEAQMEAQMCAQIEAQEEAQQEAQMEAQUAAQIEAQEAAQMEAQEEAQMEAQIEAQUAAQMIAQUAAQEI";
            Assert.That(settings.Get(ScheduledMinigameId.Race2).Rounds, Is.EqualTo(1));
            Assert.That(settings.Get(ScheduledMinigameId.Race2).Seconds, Is.EqualTo(60));
            Assert.That(MatchGameSettings.TryDecode(savedDefault, out var saved), Is.True);
            Assert.That(saved.Encode(), Is.EqualTo(savedDefault));
            Assert.That(saved.Get(ScheduledMinigameId.Race2).Rounds, Is.EqualTo(3));
            Assert.That(saved.Get(ScheduledMinigameId.Race2).Seconds, Is.EqualTo(120));
            foreach (ScheduledMinigameId id in settings.IncludedGames())
            {
                var timed = MatchGameSettings.HasTimeLimit(id);
                var min = timed ? 30 : 0;
                var max = MatchGameSettings.MaximumSeconds(id);
                foreach (var rounds in new[] { 1, 5 })
                    foreach (var seconds in new[] { min, max })
                    {
                        Assert.That(settings.TryChangeGame(id, new MinigameSetting(true, rounds, seconds), out var changed), Is.True);
                        Assert.That(MatchGameSettings.TryDecode(changed.Encode(), out _), Is.True);
                    }
                foreach (var rounds in new[] { 0, 6 })
                    Assert.That(settings.TryChangeGame(id, new MinigameSetting(true, rounds, min), out _), Is.False);
                foreach (var seconds in new[] { -15, 15, timed ? 31 : 30, max + 15 })
                    Assert.That(settings.TryChangeGame(id, new MinigameSetting(true, 1, seconds), out _), Is.False);
            }
            var encoded = settings.Encode();
            foreach (var bad in new[] { "", " ", encoded + " ", encoded.Replace("1:15:", "2:15:"),
                encoded.Replace("1:15:", "1:015:"), encoded.Substring(0, encoded.Length - 4) })
                Assert.That(MatchGameSettings.TryDecode(bad, out _), Is.False);
            var bytes = Convert.FromBase64String(encoded.Split(':')[2]);
            foreach (var mutation in new[] { Tuple.Create(0, (byte)2), Tuple.Create(1, (byte)6), Tuple.Create(2, (byte)255) })
            {
                var copy = (byte[])bytes.Clone(); copy[mutation.Item1] = mutation.Item2;
                Assert.That(MatchGameSettings.TryDecode("1:15:" + Convert.ToBase64String(copy), out _), Is.False);
            }
        }

        [Test]
        public void ConfiguredCoinRoundsKeepRainBudget_AndFinishAtExactCustomDeadline()
        {
            foreach (var rounds in new[] { 1, 5 })
                foreach (var seconds in new[] { 30, 120 })
                {
                    Assert.That(MatchGameSettings.Default.TryChangeGame(ScheduledMinigameId.CoinCollect,
                        new MinigameSetting(true, rounds, seconds), out var settings), Is.True);
                    var match = new CoinCollectMatchState(417, MinigameRulesConfig.FromMatch(ScheduledMinigameId.CoinCollect, settings));
                    for (var round = 1; round <= rounds; round++)
                    {
                        var counts = new int[3];
                        for (var i = 0; i < 70; i++)
                        {
                            var item = match.GetScheduledItem(i);
                            counts[(int)item.Kind]++;
                            Assert.That(item.BornAt, Is.GreaterThanOrEqualTo(0).And.LessThan(seconds));
                        }
                        Assert.That(counts, Is.EqualTo(new[] { 50, 10, 10 }));
                        match.AdvanceTo(seconds - 0.000001);
                        Assert.That(match.IsRoundComplete, Is.False);
                        match.AdvanceTo(seconds);
                        Assert.That(match.IsRoundComplete, Is.True);
                        Assert.That(match.BeginNextRound(), Is.EqualTo(round < rounds));
                    }
                    Assert.That(match.IsComplete, Is.True);
                }
        }

        [Test]
        public void ShortAndLongFlightCoursesRemainReachable_AndSpeedRampUsesConfiguredDeadline()
        {
            foreach (var seconds in new[] { 30, 180 })
            {
                var settings = MatchGameSettings.Default;
                settings.TryChangeGame(ScheduledMinigameId.Flight, new MinigameSetting(true, 1, seconds), out settings);
                settings.TryChangeGame(ScheduledMinigameId.Race2, new MinigameSetting(true, 5, seconds), out settings);
                foreach (var t in new[] { 0d, seconds / 2d, (double)seconds })
                {
                    Assert.That(Race2Rules.TimeAtDistance(Race2Rules.Distance(t, seconds), seconds), Is.EqualTo(t).Within(1e-8));
                    Assert.That(FlightRules.TimeAtDistance(FlightRules.Distance(t, seconds), seconds), Is.EqualTo(t).Within(1e-8));
                }
                Assert.That(Race2Rules.Speed(seconds, seconds), Is.EqualTo(Race2Rules.MaximumSpeed).Within(1e-8));
                var match = new FlightMatchState(417, MinigameRulesConfig.FromMatch(ScheduledMinigameId.Flight, settings));
                for (var tick = 0; tick < seconds * 120; tick++)
                {
                    match.AdvanceTo(3 + tick / 120d);
                    if (match.Course.WitnessFlap(tick))
                        for (var slot = 0; slot < 4; slot++)
                            Assert.That(match.TryFlap(slot), Is.True);
                }
                match.AdvanceTo(3 + seconds - .000001);
                Assert.That(match.Phase, Is.EqualTo(FlightPhase.Running));
                match.AdvanceTo(3 + seconds);
                Assert.That(match.Phase, Is.EqualTo(FlightPhase.Result));
                for (var slot = 0; slot < 4; slot++)
                {
                    Assert.That(match.IsAlive(slot), Is.True);
                    Assert.That(match.Survival(slot), Is.EqualTo(seconds).Within(1e-8));
                }
                match.AdvanceTo(5 + seconds);
                Assert.That(match.Phase, Is.EqualTo(FlightPhase.Complete));
            }
        }

        [Test]
        public void ConcurrentSimulationsKeepTheirOwnDeadlinesAndRoundCounts()
        {
            var shortMatch = new CoinCollectMatchState(417, Rules(ScheduledMinigameId.CoinCollect, 1, 30));
            var longMatch = new CoinCollectMatchState(417, Rules(ScheduledMinigameId.CoinCollect, 5, 120));
            var practice = new CoinCollectMatchState(417);
            shortMatch.AdvanceTo(30);
            longMatch.AdvanceTo(30);
            practice.AdvanceTo(30);
            Assert.That(shortMatch.IsComplete, Is.True);
            Assert.That(shortMatch.BeginNextRound(), Is.False);
            Assert.That(longMatch.IsRoundComplete, Is.False);
            Assert.That(practice.IsRoundComplete, Is.False);
            for (var round = 1; round <= 5; round++)
            {
                longMatch.AdvanceTo(120);
                Assert.That(longMatch.BeginNextRound(), Is.EqualTo(round < 5));
            }
            Assert.That(longMatch.IsComplete, Is.True);
            practice.AdvanceTo(CoinCollectRules.RoundSeconds);
            Assert.That(practice.BeginNextRound(), Is.True);
        }

        [Test]
        public void ConfiguredFifthRoundsEndExactlyAtTheirOwnDeadline()
        {
            foreach (var seconds in new[] { 30, 90 })
            {
                var balloon = new BalloonBlowRoundState(5, Rules(ScheduledMinigameId.BalloonBlow, 5, seconds));
                var gift = new GiftGrabRoundState(5, Rules(ScheduledMinigameId.GiftGrab, 5, seconds));
                var footing = new StableFootingRoundState(417, 5, Rules(ScheduledMinigameId.StableFooting, 5, seconds));
                var redLight = new RedLightGreenLightRoundState(417, 5, Rules(ScheduledMinigameId.RedLightGreenLight, 5, seconds));
                var wrongWay = new WrongWayRoundState(417, 5, Rules(ScheduledMinigameId.WrongWay, 5, seconds));
                foreach (var end in new Func<double, bool>[] { balloon.TryEndForTimeout, gift.TryEndForTimeout,
                    footing.TryEndForTimeout, redLight.TryEndForTimeout, wrongWay.TryEndForTimeout })
                {
                    Assert.That(end(seconds - .000001), Is.False);
                    Assert.That(end(seconds), Is.True);
                    Assert.That(end(seconds), Is.False);
                }
                Assert.That(footing.Result.Standings.All(s => s.Outcome.Survived), Is.True);
            }
        }

        [Test]
        public void ConfiguredFinalMemoryRoundRanksByCompletionTime()
        {
            foreach (var rounds in new[] { 1, 5 })
            {
                var match = new SequenceMemoryMatchState(417, Rules(ScheduledMinigameId.SequenceMemory, rounds, 0));
                match.BeginMatch();
                for (var round = 1; round <= rounds; round++)
                {
                    match.OpenInputWindow();
                    for (var slot = 0; slot < 4; slot++)
                        for (var input = 0; input < match.CurrentProblem.Length; input++)
                            Assert.That(match.SubmitInput(slot, match.CurrentProblem[input], (byte)round,
                                match.InputEpoch, slot + 1d).WasAccepted, Is.True);
                    match.CompleteRevealAndAdvance();
                    Assert.That(match.IsComplete, Is.EqualTo(round == rounds));
                }
                Assert.That(match.Result.Standings.Select(s => s.Rank), Is.EqualTo(new[] { 1, 2, 3, 4 }));
                Assert.That(match.Result.Standings.Select(s => s.PlayerSlot), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            }
        }

        private static MinigameRulesConfig Rules(ScheduledMinigameId id, int rounds, int seconds)
        {
            Assert.That(MatchGameSettings.Default.TryChangeGame(id,
                new MinigameSetting(true, rounds, seconds), out var settings), Is.True, id.ToString());
            return MinigameRulesConfig.FromMatch(id, settings);
        }

        [Test]
        public void RoundSeriesRejectsDuplicateOrOutOfOrderResults_AndSettlesTiedFinalRanksOnce()
        {
            var series = new MinigameRoundSeries(2);
            PlayerPlacementSet.TryCreate(new[] { new PlayerPlacement(0, 1), new PlayerPlacement(1, 2),
                new PlayerPlacement(2, 3), new PlayerPlacement(3, 4) }, out var first, out _);
            PlayerPlacementSet.TryCreate(new[] { new PlayerPlacement(0, 2), new PlayerPlacement(1, 1),
                new PlayerPlacement(2, 3), new PlayerPlacement(3, 4) }, out var second, out _);
            Assert.That(series.TryRecord(2, second), Is.False);
            Assert.That(series.TryRecord(1, first), Is.True);
            Assert.That(series.TryRecord(1, first), Is.False);
            Assert.Throws<InvalidOperationException>(() => series.FinalPlacements());
            Assert.That(series.TryRecord(2, second), Is.True);
            Assert.That(series.TryRecord(2, second), Is.False);
            Assert.That(Enumerable.Range(0, 4).Select(series.FinalPlacements().GetRankForSlot),
                Is.EqualTo(new[] { 1, 1, 3, 4 }));
        }
    }
}

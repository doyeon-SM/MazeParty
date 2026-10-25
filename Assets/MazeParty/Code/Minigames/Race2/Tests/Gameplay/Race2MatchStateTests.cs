using System;
using System.Linq;
using MazeParty.Gameplay.Minigames.Race2;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class Race2MatchStateTests
    {
        [Test]
        public void Stream_SeededAcrossRounds_RecyclesFixedPool_AndNeverRepeatsFourLanes()
        {
            var first = new Race2BoxStream(417, 1); var same = new Race2BoxStream(417, 1); var next = new Race2BoxStream(417, 2);
            var differences = 0;
            for (var i = 0; i < 400; i++)
            {
                Assert.That(first.Lane(i), Is.EqualTo(same.Lane(i)));
                if (first.Lane(i) != next.Lane(i)) differences++;
                if (i >= 3) Assert.That(Enumerable.Range(i - 3, 4).Select(first.Lane).Distinct().Count(), Is.EqualTo(2));
            }
            Assert.That(differences, Is.GreaterThan(0));
            for (var t = 0d; t <= Race2Rules.RoundSeconds; t += .25d)
            {
                var sequences = new int[Race2Rules.BoxCount]; var positions = new double[Race2Rules.BoxCount];
                for (var b = 0; b < Race2Rules.BoxCount; b++)
                {
                    first.Box(b, t, out sequences[b], out positions[b]);
                    Assert.That(positions[b], Is.GreaterThanOrEqualTo(Race2Rules.RecycleZ - 1e-8));
                }
                Assert.That(sequences.Distinct().Count(), Is.EqualTo(Race2Rules.BoxCount));
                Array.Sort(positions);
                for (var b = 1; b < Race2Rules.BoxCount; b++) Assert.That(positions[b] - positions[b - 1], Is.EqualTo(Race2Rules.BoxSpacing).Within(1e-8));
            }
            for (var b = 0; b < Race2Rules.BoxCount; b++)
            {
                var recycleAt = Race2Rules.TimeAtDistance(Race2Rules.FirstBoxDistance +
                    b * Race2Rules.BoxSpacing - Race2Rules.RecycleZ);
                first.Box(b, recycleAt - 1e-6, out var beforeSequence, out var beforeZ);
                first.Box(b, recycleAt + 1e-6, out var afterSequence, out var afterZ);
                Assert.That(beforeSequence, Is.EqualTo(b));
                Assert.That(beforeZ, Is.GreaterThan(Race2Rules.RecycleZ));
                Assert.That(afterSequence, Is.EqualTo(b + Race2Rules.BoxCount));
                Assert.That(afterZ - beforeZ, Is.EqualTo(Race2Rules.BoxCount * Race2Rules.BoxSpacing).Within(.001));
            }
            foreach (var sample in new[]{(0d,10d,0d),(30d,16.25d,393.75d),(60d,22.5d,975d)})
            {
                Assert.That(Race2Rules.Speed(sample.Item1), Is.EqualTo(sample.Item2).Within(1e-8));
                Assert.That(Race2Rules.Distance(sample.Item1), Is.EqualTo(sample.Item3).Within(1e-8));
                Assert.That(Race2Rules.TimeAtDistance(sample.Item3), Is.EqualTo(sample.Item1).Within(1e-8));
            }
        }
        [Test]
        public void CountdownAndSweptCollision_UseExactBoundaries_AndBatchTiesBeforeRanking()
        {
            foreach (var safePlayers in new[]{0,1})
            {
                var game = new Race2MatchState(417, Rules(2, 60));
                Assert.That(game.TrySetLane(0,1), Is.False);
                game.AdvanceTo(2.99999); Assert.That(game.Phase, Is.EqualTo(Race2Phase.Countdown));
                Assert.That(game.RoundElapsed, Is.Zero);
                game.AdvanceTo(3); Assert.That(game.Phase, Is.EqualTo(Race2Phase.Running));
                var bad = game.Boxes.Lane(0);
                for (var i = 0; i < 4; i++) game.TrySetLane(i,i < safePlayers ? (byte)(1-bad) : bad);
                var impact = 3d + Race2Rules.TimeAtDistance(Race2Rules.FirstBoxDistance - Race2Rules.CollisionHalfDepth);
                game.AdvanceTo(impact - 1e-7);
                Assert.That(Enumerable.Range(0,4).All(game.IsAlive), Is.True);
                game.AdvanceTo(impact);
                Assert.That(game.Phase, Is.EqualTo(Race2Phase.RoundResult));
                for (var i = 0; i < 4; i++) Assert.That(game.GetRoundRank(i), Is.EqualTo(safePlayers == 1 && i > 0 ? 2 : 1));
                Assert.That(game.TrySetLane(0, (byte)(1-bad)), Is.False);
                var points = game.GetScore(0); game.AdvanceTo(game.PhaseEndsAt);
                Assert.That(game.Phase, Is.EqualTo(Race2Phase.Countdown)); Assert.That(game.RoundNumber, Is.EqualTo(2));
                Assert.That(game.GetScore(0), Is.EqualTo(points)); Assert.That(game.RoundElapsed, Is.Zero);
                Assert.That(Enumerable.Range(0,4).All(game.IsAlive), Is.True);
            }
            var coarse = new Race2MatchState(417); coarse.AdvanceTo(3);
            for (var i=0;i<4;i++)coarse.TrySetLane(i,coarse.Boxes.Lane(0));
            coarse.AdvanceTo(5); // Box would already be behind the player at this frame.
            Assert.That(Enumerable.Range(0,4).Any(coarse.IsAlive), Is.False);
            Assert.That(coarse.Phase, Is.EqualTo(Race2Phase.RoundResult));
        }
        [Test]
        public void LaneChanges_AreImmediate_RejectInvalidInput_AndCollideWithOverlappingBox()
        {
            var game = new Race2MatchState(417); game.AdvanceTo(3);
            var bad = game.Boxes.Lane(0);var safe=(byte)(1-bad);
            for(var i=0;i<4;i++)game.TrySetLane(i,safe);
            Assert.That(game.TrySetLane(0,2),Is.False);Assert.That(game.TrySetLane(-1,0),Is.False);
            Assert.That(game.TrySetLane(4,0),Is.False);Assert.That(game.TrySetLane(0,safe),Is.False);
            game.AdvanceTo(3+Race2Rules.TimeAtDistance(Race2Rules.FirstBoxDistance));
            Assert.That(Enumerable.Range(0,4).All(game.IsAlive),Is.True);
            Assert.That(game.TrySetLane(0,bad),Is.True);Assert.That(game.IsAlive(0),Is.False);
            Assert.That(game.TrySetLane(0,safe),Is.False);
            Assert.That(game.Phase,Is.EqualTo(Race2Phase.Running));
            var elapsed=game.Elapsed;game.AdvanceTo(double.NaN);game.AdvanceTo(double.PositiveInfinity);game.AdvanceTo(-1);
            Assert.That(game.Elapsed,Is.EqualTo(elapsed));
        }
        [Test]
        public void DefaultAndSavedTimeoutRounds_PreservePoints_AndTieAllSurvivors()
        {
            foreach(var setting in new[]{(1,60),(3,120),(5,180)})
            {
                var game=setting.Item1==1 ? new Race2MatchState(8991) : new Race2MatchState(8991,Rules(setting.Item1,setting.Item2));
                for(var round=1;round<=setting.Item1;round++)
                {
                    game.AdvanceTo(game.PhaseEndsAt);
                    var start=game.Elapsed;
                    while(game.Phase==Race2Phase.Running)
                    {
                        var sequence=-1;var nearest=double.MaxValue;
                        for(var b=0;b<Race2Rules.BoxCount;b++)
                        {
                            game.Boxes.Box(b,game.RoundElapsed,out var current,out var z);
                            if(z >= -Race2Rules.CollisionHalfDepth && z<nearest){nearest=z;sequence=current;}
                        }
                        Assert.That(sequence,Is.GreaterThanOrEqualTo(0));
                        for(var i=0;i<4;i++)game.TrySetLane(i,(byte)(1-game.Boxes.Lane(sequence)));
                        game.AdvanceTo(Math.Min(start+setting.Item2,game.Elapsed+.02));
                    }
                    Assert.That(game.RoundElapsed,Is.EqualTo(setting.Item2).Within(1e-7));
                    for(var i=0;i<4;i++){Assert.That(game.IsAlive(i),Is.True);Assert.That(game.GetRoundRank(i),Is.EqualTo(1));Assert.That(game.GetScore(i),Is.EqualTo(round*3));}
                    game.AdvanceTo(game.PhaseEndsAt);
                }
                Assert.That(game.Phase,Is.EqualTo(Race2Phase.Complete));
                Assert.That(game.Elapsed,Is.EqualTo(setting.Item1*(setting.Item2+7)).Within(1e-7));
                for(var i=0;i<4;i++)Assert.That(game.GetRank(i),Is.EqualTo(1));
            }
        }

        private static MinigameRulesConfig Rules(int rounds, int seconds)
        {
            Assert.That(MatchGameSettings.Default.TryChangeGame(ScheduledMinigameId.Race2,
                new MinigameSetting(true, rounds, seconds), out var settings), Is.True);
            Assert.That(MatchGameSettings.TryDecode(settings.Encode(), out var restored), Is.True);
            return MinigameRulesConfig.FromMatch(ScheduledMinigameId.Race2, restored);
        }
        [Test]
        public void Spectator_CyclesForward_SkipsEliminatedPlayers_AndHandlesNoSurvivors()
        {
            Assert.That(Race2Rules.NextSpectator(0,i=>i==2||i==3),Is.EqualTo(2));
            Assert.That(Race2Rules.NextSpectator(2,i=>i==0||i==3),Is.EqualTo(3));
            Assert.That(Race2Rules.NextSpectator(3,i=>i==0||i==2),Is.EqualTo(0));
            Assert.That(Race2Rules.NextSpectator(0,i=>false),Is.EqualTo(-1));
        }
    }
}

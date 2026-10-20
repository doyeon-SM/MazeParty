using System;
using MazeParty.Gameplay.Minigames.Flight;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class FlightMatchStateTests
    {
        [Test]
        public void CountdownStartsGlidingAtExactBoundary_AndRejectsInvalidOrRepeatedInput()
        {
            var match = new FlightMatchState(417);
            match.AdvanceTo(2.999999);
            Assert.That(match.Phase, Is.EqualTo(FlightPhase.Countdown));
            Assert.That(match.Height(0), Is.Zero);
            Assert.That(match.TryFlap(0), Is.False);
            match.AdvanceTo(3);
            Assert.That(match.Phase, Is.EqualTo(FlightPhase.Running));
            match.AdvanceTo(3.1);
            Assert.That(match.Height(0), Is.EqualTo(-.06).Within(1e-9));
            Assert.That(match.TryFlap(-1), Is.False); Assert.That(match.TryFlap(4), Is.False);
            Assert.That(match.TryFlap(0), Is.True); Assert.That(match.TryFlap(0), Is.False);
            match.AdvanceTo(3.1001);
            Assert.That(match.TryFlap(0), Is.False);
            match.AdvanceTo(3.2);
            Assert.That(match.Height(0), Is.GreaterThan(0));
            Assert.That(match.TryFlap(0), Is.True);
            var before = match.Elapsed;
            foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, before - 1 }) match.AdvanceTo(invalid);
            Assert.That(match.Elapsed, Is.EqualTo(before));
        }
        [Test]
        public void GeneratedCourseHasOneContinuousLegalPathThroughEveryGap_AndAllTimeoutSurvivorsTie()
        {
            foreach (var seed in new ulong[] { 0, 1, 2, 3, 7, 17, 37, 59, 83, 99, 137, 417, 999, 12345, 998877, ulong.MaxValue })
            {
                var match = new FlightMatchState(seed);
                for (var tick = 0; tick < 14400; tick++)
                {
                    match.AdvanceTo(3 + tick / 120d);
                    if (match.Course.WitnessFlap(tick))
                        for (var slot = 0; slot < 4; slot++) Assert.That(match.TryFlap(slot), Is.True, $"seed={seed}, tick={tick}");
                }
                match.AdvanceTo(122.999999);
                Assert.That(match.Phase, Is.EqualTo(FlightPhase.Running));
                match.AdvanceTo(123);
                Assert.That(match.Phase, Is.EqualTo(FlightPhase.Result));
                for (var slot = 0; slot < 4; slot++)
                {
                    Assert.That(match.IsAlive(slot), Is.True, "Constructed path must survive the actual collision simulation: " + seed);
                    Assert.That(match.Survival(slot), Is.EqualTo(120).Within(1e-8));
                    Assert.That(match.Rank(slot), Is.EqualTo(1));
                }
                Assert.That(match.TryFlap(0), Is.False);
                match.AdvanceTo(124.999999); Assert.That(match.Phase, Is.EqualTo(FlightPhase.Result));
                match.AdvanceTo(125); Assert.That(match.Phase, Is.EqualTo(FlightPhase.Complete));
            }
        }
        [Test]
        public void CourseIsSeedDeterministic_AndGapBoundsIncludeThePlayersBody()
        {
            for (ulong seed = 0; seed < 64; seed++)
            {
                var a = new FlightCourse(seed); var b = new FlightCourse(seed);
                for (var i = 0; i < a.Count; i++)
                {
                    Assert.That(a[i].Center, Is.EqualTo(b[i].Center));
                    Assert.That(a[i].Height, Is.EqualTo(b[i].Height));
                    Assert.That(a[i].Height, Is.InRange(3d, 5d));
                    // Independently sample the full horizontal crossing, including both body edges.
                    for (var t = a[i].EntersAt; t <= a[i].ExitsAt; t += 1d / 120)
                    {
                        var y = a.WitnessY((int)Math.Round(t * 120));
                        Assert.That(y - 1, Is.GreaterThan(a[i].Lower));
                        Assert.That(y + 1, Is.LessThan(a[i].Upper));
                    }
                }
            }
        }
        [Test]
        public void SimultaneousDeathsTie_AndLastSurvivorEndsMatchImmediately()
        {
            var allFall = new FlightMatchState(417);
            allFall.AdvanceTo(123);
            for (var slot = 0; slot < 4; slot++)
            {
                Assert.That(allFall.IsAlive(slot), Is.False);
                Assert.That(allFall.Rank(slot), Is.EqualTo(1));
                Assert.That(allFall.Survival(slot), Is.EqualTo(allFall.Survival(0)).Within(1e-8));
            }
            var lastAlive = new FlightMatchState(417);
            for (var tick = 0; tick < 14400; tick++)
            {
                lastAlive.AdvanceTo(3 + tick / 120d);
                if (lastAlive.Phase != FlightPhase.Running) break;
                if (lastAlive.Course.WitnessFlap(tick)) lastAlive.TryFlap(0);
            }
            Assert.That(lastAlive.Phase, Is.EqualTo(FlightPhase.Result));
            Assert.That(lastAlive.IsAlive(0), Is.True);
            Assert.That(lastAlive.Rank(0), Is.EqualTo(1));
            Assert.That(lastAlive.RunElapsed, Is.LessThan(120));
            for (var slot = 1; slot < 4; slot++)
            {
                Assert.That(lastAlive.Rank(slot), Is.EqualTo(2));
                Assert.That(lastAlive.Survival(slot), Is.EqualTo(lastAlive.RunElapsed).Within(1e-8));
                Assert.That(lastAlive.TryFlap(slot), Is.False);
            }
        }
        [Test]
        public void CollisionTimesAndStandingsDoNotDependOnFramePartition()
        {
            var oneFrame = new FlightMatchState(9);
            var manyFrames = new FlightMatchState(9);
            oneFrame.AdvanceTo(30);
            for (var i = 1; i <= 3600; i++) manyFrames.AdvanceTo(i / 120d);
            for (var slot = 0; slot < 4; slot++)
            {
                Assert.That(manyFrames.Survival(slot), Is.EqualTo(oneFrame.Survival(slot)).Within(1e-8));
                Assert.That(manyFrames.Rank(slot), Is.EqualTo(oneFrame.Rank(slot)));
            }
            double y = FlightRules.Ceiling - .01, velocity = FlightRules.FlapVelocity;
            FlightRules.Integrate(ref y, ref velocity, .01);
            Assert.That(y, Is.LessThanOrEqualTo(FlightRules.Ceiling));
            Assert.That(velocity, Is.LessThanOrEqualTo(0), "The upper screen boundary cancels upward velocity.");
        }
    }
}

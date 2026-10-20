using System;
using MazeParty.Gameplay.Minigames.ShellGame;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class ShellGameMatchStateTests
    {
        private static void ChoosePhase(ShellGameMatchState match)
        {
            while(match.Phase!=ShellGamePhase.Choosing) match.AdvanceTo(match.PhaseEndsAt);
        }
        [Test]
        public void EveryChoiceCombination_ScoresOnlyCorrectSelections_AndHidesOtherChoices()
        {
            for(var mask=0;mask<256;mask++)
            {
                var match=new ShellGameMatchState((ulong)mask);
                ChoosePhase(match);
                for(var i=0;i<4;i++)
                {
                    var choice=((mask>>(i*2))&3)-1;
                    if(choice>=0) Assert.That(match.TryChoose(i,choice),Is.True);
                    if(match.Phase==ShellGamePhase.Choosing)
                    {
                        Assert.That(match.RevealedChoices,Is.Zero);
                        Assert.That(match.GetVisibleChoice(i,(i+1)%4),Is.EqualTo(-1));
                    }
                }
                if(match.Phase==ShellGamePhase.Choosing) match.AdvanceTo(match.PhaseEndsAt);
                Assert.That(match.Phase,Is.EqualTo(ShellGamePhase.Reveal));
                for(var i=0;i<4;i++)
                {
                    var selected=((mask>>(i*2))&3)-1;
                    Assert.That(match.GetScore(i),Is.EqualTo(selected==match.Plan.CoinBox ? 1 : 0),mask+"/"+i);
                    Assert.That(match.GetVisibleChoice(i,(i+1)%4),Is.EqualTo(selected));
                }
            }
        }
        [Test]
        public void FirstClickLocks_AllSelectedRevealsImmediately_AndScoresExactlyOnce()
        {
            var match=new ShellGameMatchState(9);
            Assert.That(match.TryChoose(0,0),Is.False);
            ChoosePhase(match); var at=match.Elapsed+.2; match.AdvanceTo(at);
            foreach(var pair in new[]{(-1,0),(4,0),(0,-1),(0,3)})
                Assert.That(match.TryChoose(pair.Item1,pair.Item2),Is.False);
            for(var i=0;i<4;i++)
            {
                Assert.That(match.TryChoose(i,match.Plan.CoinBox),Is.True);
                Assert.That(match.TryChoose(i,(match.Plan.CoinBox+1)%3),Is.False);
            }
            Assert.That(match.Phase,Is.EqualTo(ShellGamePhase.Reveal));
            Assert.That(match.PhaseEndsAt,Is.EqualTo(at+2));
            match.AdvanceTo(at+2-.000001);
            Assert.That(match.RoundNumber,Is.EqualTo(1));
            match.AdvanceTo(at+2);
            Assert.That(match.RoundNumber,Is.EqualTo(2));
            Assert.That(match.GetChoice(0),Is.EqualTo(-1));
            Assert.That(match.GetScore(0),Is.EqualTo(1));
        }
        [Test]
        public void ExactPhaseDeadlines_UseFasterSwapsEachRound_AndTenSecondChoiceWindow()
        {
            var match=new ShellGameMatchState(123);
            var previousSpeed=double.MaxValue;
            for(var round=1;round<=5;round++)
            {
                Assert.That(match.RoundNumber,Is.EqualTo(round));
                var speed=match.Plan.SwapSeconds;
                Assert.That(speed,Is.LessThan(previousSpeed));
                previousSpeed=speed;
                foreach(var phase in new[]{ShellGamePhase.Countdown,ShellGamePhase.Preview,ShellGamePhase.Hiding,ShellGamePhase.Shuffling,ShellGamePhase.Choosing,ShellGamePhase.Reveal})
                {
                    Assert.That(match.Phase,Is.EqualTo(phase));
                    if(phase==ShellGamePhase.Shuffling)
                        Assert.That(match.Remaining,Is.EqualTo(match.Plan.ShuffleCount*(.8-(round-1)*.125)).Within(1e-9));
                    if(phase==ShellGamePhase.Choosing) Assert.That(match.Remaining,Is.EqualTo(10).Within(1e-9));
                    var deadline=match.PhaseEndsAt;
                    match.AdvanceTo(deadline-.000001); Assert.That(match.Phase,Is.EqualTo(phase));
                    match.AdvanceTo(deadline); Assert.That(match.Phase,Is.Not.EqualTo(phase));
                    if(phase==ShellGamePhase.Choosing) Assert.That(match.TryChoose(0,match.Plan.CoinBox),Is.False);
                }
            }
            Assert.That(match.Phase,Is.EqualTo(ShellGamePhase.Complete));
            var end=match.Elapsed;
            match.AdvanceTo(1e6); match.AdvanceTo(double.NaN); match.AdvanceTo(double.PositiveInfinity);
            Assert.That(match.Elapsed,Is.EqualTo(end));
            for(var i=0;i<4;i++) Assert.That(match.GetScore(i),Is.Zero);
        }
        [Test]
        public void SeedReplay_PreservesAllSwapPermutationsAndCoinIdentity()
        {
            var counts=new bool[4];
            for(ulong seed=0;seed<250;seed++)
                for(var round=1;round<=5;round++)
                {
                    var plan=new ShellGameRoundPlan(seed,round); var restored=new ShellGameRoundPlan(seed,round);
                    Assert.That(plan.ShuffleCount,Is.InRange(5,8)); counts[plan.ShuffleCount-5]=true;
                    Assert.That(restored.CoinBox,Is.EqualTo(plan.CoinBox));
                    for(var step=0;step<plan.ShuffleCount;step++)
                    {
                        var a=plan.FirstBox(step); var b=plan.SecondBox(step);
                        Assert.That(a,Is.Not.EqualTo(b));
                        Assert.That(plan.Position(a,step+1),Is.EqualTo(plan.Position(b,step)));
                        for(var box=0;box<3;box++) Assert.That(plan.Position(box,step+1),Is.EqualTo(restored.Position(box,step+1)));
                        CollectionAssert.AreEquivalent(new[]{0,1,2},new[]{plan.Position(0,step+1),plan.Position(1,step+1),plan.Position(2,step+1)});
                    }
                }
            Assert.That(Array.TrueForAll(counts,v=>v),Is.True);
        }
        [Test]
        public void FiveRounds_AccumulateScoresAndCompetitionRanks()
        {
            var match=new ShellGameMatchState(44);
            for(var round=1;round<=5;round++)
            {
                ChoosePhase(match);
                for(var slot=0;slot<4;slot++)
                    match.TryChoose(slot,slot==3 || slot==0 && round>3 ? (match.Plan.CoinBox+1)%3 : match.Plan.CoinBox);
                match.AdvanceTo(match.PhaseEndsAt);
            }
            Assert.That(match.Phase,Is.EqualTo(ShellGamePhase.Complete));
            CollectionAssert.AreEqual(new[]{3,5,5,0},new[]{match.GetScore(0),match.GetScore(1),match.GetScore(2),match.GetScore(3)});
            CollectionAssert.AreEqual(new[]{3,1,1,4},new[]{match.GetRank(0),match.GetRank(1),match.GetRank(2),match.GetRank(3)});
        }
    }
}

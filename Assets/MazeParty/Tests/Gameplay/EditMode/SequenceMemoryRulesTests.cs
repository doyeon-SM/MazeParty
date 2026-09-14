using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class SequenceMemoryRulesTests
    {
        [Test]
        public void Problems_AreSeededUniqueAndNeverContainTripleRuns()
        {
            var first = SequenceMemoryProblemGenerator.GenerateMatch(
                0x123456789ABCDEF0UL);
            var repeated = SequenceMemoryProblemGenerator.GenerateMatch(
                0x123456789ABCDEF0UL);
            var otherSeed = SequenceMemoryProblemGenerator.GenerateMatch(
                0x0FEDCBA987654321UL);
            var signatures = new HashSet<string>();
            var differsFromOtherSeed = false;

            Assert.That(
                first,
                Has.Length.EqualTo(SequenceMemoryRules.RoundCount));
            for (var roundIndex = 0;
                 roundIndex < first.Length;
                 roundIndex++)
            {
                var problem = first[roundIndex];
                Assert.That(
                    problem.Length,
                    Is.EqualTo(
                        SequenceMemoryRules.GetProblemLength(
                            roundIndex + 1)));
                Assert.That(
                    repeated[roundIndex].ToString(),
                    Is.EqualTo(problem.ToString()));
                Assert.That(signatures.Add(problem.ToString()), Is.True);
                AssertNoTripleRun(problem);
                differsFromOtherSeed |=
                    otherSeed[roundIndex].ToString() !=
                    problem.ToString();
            }

            Assert.That(differsFromOtherSeed, Is.True);
            Assert.That(
                SequenceMemoryRules.GetProblemPresentationSeconds(1),
                Is.EqualTo(4d).Within(0.0000001d));
            Assert.That(
                new SequenceMemoryProblem(
                    new[]
                    {
                        SequenceMemoryInput.A,
                        SequenceMemoryInput.S,
                        SequenceMemoryInput.A,
                        SequenceMemoryInput.D,
                        SequenceMemoryInput.A
                    }).ToString(),
                Is.EqualTo("ASADA"),
                "Non-consecutive repeats are valid even three times.");
        }

        [Test]
        public void PrefixMismatch_LocksImmediatelyAndStaleWindowIsRejected()
        {
            var match = BeginInputMatch(77UL);
            var player = match.GetPlayer(0);
            var epoch = match.InputEpoch;
            var round = (byte)match.CurrentRoundNumber;
            var firstInput = match.CurrentProblem[0];

            var stale = match.SubmitInput(
                0,
                firstInput,
                round,
                epoch - 1,
                0d);
            Assert.That(
                stale.Status,
                Is.EqualTo(
                    SequenceMemoryInputStatus.IgnoredStaleInputWindow));
            Assert.That(player.CurrentInput, Is.Empty);

            var accepted = match.SubmitInput(
                0,
                firstInput,
                round,
                epoch,
                0.1d);
            var wrongInput = DifferentInput(match.CurrentProblem[1]);
            var failed = match.SubmitInput(
                0,
                wrongInput,
                round,
                epoch,
                0.2d);
            var ignored = match.SubmitInput(
                0,
                match.CurrentProblem[1],
                round,
                epoch,
                0.3d);

            Assert.That(
                accepted.Status,
                Is.EqualTo(SequenceMemoryInputStatus.AcceptedPrefix));
            Assert.That(
                failed.Status,
                Is.EqualTo(
                    SequenceMemoryInputStatus.FailedPrefixMismatch));
            Assert.That(failed.CorrectPrefixLength, Is.EqualTo(1));
            Assert.That(failed.BecameTorsoLost, Is.True);
            Assert.That(
                ignored.Status,
                Is.EqualTo(
                    SequenceMemoryInputStatus.IgnoredPlayerLocked));
            Assert.That(player.MistakeCount, Is.EqualTo(1));
            Assert.That(
                player.LifeState,
                Is.EqualTo(SequenceMemoryPlayerLifeState.TorsoLost));
            Assert.That(player.ShouldHideTorso, Is.True);
            Assert.That(
                player.CurrentInput,
                Is.EqualTo(new[] { firstInput, wrongInput }));
        }

        [Test]
        public void Timeout_UsesExactBoundaryAndPenalizesEmptyAndPartialOnly()
        {
            var match = BeginInputMatch(78UL);
            SolveProblem(match, 1, 1d);
            SolveProblem(match, 2, 1d);
            SubmitCorrectPrefix(match, 0, 2, 9.999d);

            Assert.That(match.TryEndInputForTimeout(9.999d), Is.False);
            Assert.That(match.TryEndInputForTimeout(10d), Is.True);
            Assert.That(
                match.Phase,
                Is.EqualTo(SequenceMemoryMatchPhase.RevealingAnswer));
            Assert.That(
                match.LastInputCloseResolution.Reason,
                Is.EqualTo(SequenceMemoryInputCloseReason.TimeLimit));
            Assert.That(
                match.LastInputCloseResolution.TimeoutMistakes.Count,
                Is.EqualTo(2));
            Assert.That(match.GetPlayer(0).MistakeCount, Is.EqualTo(1));
            Assert.That(match.GetPlayer(3).MistakeCount, Is.EqualTo(1));
            Assert.That(match.GetPlayer(1).MistakeCount, Is.Zero);
            Assert.That(match.GetPlayer(2).MistakeCount, Is.Zero);

            var exactBoundary = BeginInputMatch(79UL);
            var rejected = exactBoundary.SubmitInput(
                0,
                exactBoundary.CurrentProblem[0],
                (byte)exactBoundary.CurrentRoundNumber,
                exactBoundary.InputEpoch,
                SequenceMemoryRules.InputWindowSeconds);

            Assert.That(
                rejected.Status,
                Is.EqualTo(
                    SequenceMemoryInputStatus.IgnoredAtOrAfterDeadline));
            Assert.That(
                exactBoundary.LastInputCloseResolution.TimeoutMistakes.Count,
                Is.EqualTo(SequenceMemoryRules.PlayerCount));
            Assert.That(
                exactBoundary.GetPlayer(0).CurrentInput,
                Is.Empty,
                "An input at exactly ten seconds is not appended.");
        }

        [Test]
        public void SecondMistake_EliminatesAndThirdEliminationEndsWithOneSurvivor()
        {
            var match = BeginInputMatch(80UL);
            FailImmediately(match, 0, 0d);
            FailImmediately(match, 1, 0d);
            FailImmediately(match, 2, 0d);
            SolveProblem(match, 3, 1d);
            Assert.That(
                match.Phase,
                Is.EqualTo(SequenceMemoryMatchPhase.RevealingAnswer));

            match.CompleteRevealAndAdvance();
            match.OpenInputWindow();
            FailImmediately(match, 0, 0d);
            FailImmediately(match, 1, 0d);
            var terminalFailure = FailImmediately(match, 2, 0d);

            Assert.That(terminalFailure.BecameEliminated, Is.True);
            Assert.That(
                match.LastInputCloseResolution.Reason,
                Is.EqualTo(SequenceMemoryInputCloseReason.LastSurvivor));
            Assert.That(
                match.GetPlayer(3).TurnStatus,
                Is.EqualTo(
                    SequenceMemoryPlayerTurnStatus.LockedForMatchEnd));

            match.CompleteRevealAndAdvance();

            Assert.That(match.IsComplete, Is.True);
            Assert.That(
                match.EndReason,
                Is.EqualTo(
                    SequenceMemoryMatchEndReason.LastSurvivor));
            Assert.That(
                PlayerSlots(match.Result),
                Is.EqualTo(new[] { 3, 0, 1, 2 }));
            AssertUniqueRanks(match.Result);
        }

        [Test]
        public void EliminationRanking_UsesLaterRoundPrefixPreviousSpeedThenOrder()
        {
            var match = BeginInputMatch(81UL);
            for (var slot = 0;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
            {
                FailImmediately(match, slot, 0d);
            }

            match.CompleteRevealAndAdvance();
            match.OpenInputWindow();
            FailImmediately(match, 0, 0d);
            SubmitCorrectPrefix(match, 1, 1, 0d);
            FailImmediately(match, 1, 0d);
            SolveProblem(match, 3, 2d);
            SolveProblem(match, 2, 3d);

            match.CompleteRevealAndAdvance();
            match.OpenInputWindow();
            SubmitCorrectPrefix(match, 2, 2, 0d);
            SubmitCorrectPrefix(match, 3, 2, 0d);
            Assert.That(match.TryEndInputForTimeout(10d), Is.True);
            match.CompleteRevealAndAdvance();

            Assert.That(
                match.EndReason,
                Is.EqualTo(
                    SequenceMemoryMatchEndReason.AllPlayersEliminated));
            Assert.That(
                PlayerSlots(match.Result),
                Is.EqualTo(new[] { 3, 2, 1, 0 }),
                "Round 3 beats round 2; equal round/prefix uses the " +
                "faster previous completion, then prefix separates 1/0.");
            Assert.That(match.Result.Standings[0].Rank, Is.EqualTo(1));
            Assert.That(match.Result.Standings[3].Rank, Is.EqualTo(4));
        }

        [Test]
        public void FinalRound_RanksSolversByCompletionThenFailedSurvivorThenEliminated()
        {
            var match = BeginInputMatch(82UL);
            FailImmediately(match, 3, 0d);
            SolveProblem(match, 0, 1d);
            SolveProblem(match, 1, 1d);
            SolveProblem(match, 2, 1d);

            match.CompleteRevealAndAdvance();
            match.OpenInputWindow();
            FailImmediately(match, 3, 0d);
            SolveProblem(match, 0, 1d);
            SolveProblem(match, 1, 1d);
            SolveProblem(match, 2, 1d);

            for (var roundNumber = 3;
                 roundNumber < SequenceMemoryRules.RoundCount;
                 roundNumber++)
            {
                match.CompleteRevealAndAdvance();
                match.OpenInputWindow();
                SolveProblem(match, 0, 1d);
                SolveProblem(match, 1, 1d);
                SolveProblem(match, 2, 1d);
            }

            match.CompleteRevealAndAdvance();
            match.OpenInputWindow();
            FailImmediately(match, 2, 0d);
            SolveProblem(match, 1, 2d);
            SolveProblem(match, 0, 3d);

            Assert.That(match.CurrentRoundNumber, Is.EqualTo(10));
            Assert.That(
                match.Phase,
                Is.EqualTo(SequenceMemoryMatchPhase.RevealingAnswer));
            match.CompleteRevealAndAdvance();

            Assert.That(
                match.EndReason,
                Is.EqualTo(
                    SequenceMemoryMatchEndReason.AllRoundsCompleted));
            Assert.That(
                PlayerSlots(match.Result),
                Is.EqualTo(new[] { 1, 0, 2, 3 }));
            Assert.That(match.Result.CompletedRoundCount, Is.EqualTo(10));
            AssertUniqueRanks(match.Result);
        }

        private static SequenceMemoryMatchState BeginInputMatch(
            ulong seed)
        {
            var match = new SequenceMemoryMatchState(seed);
            match.BeginMatch();
            match.OpenInputWindow();
            return match;
        }

        private static SequenceMemoryInputResolution FailImmediately(
            SequenceMemoryMatchState match,
            int playerSlot,
            double elapsedSeconds)
        {
            var player = match.GetPlayer(playerSlot);
            return match.SubmitInput(
                playerSlot,
                DifferentInput(
                    match.CurrentProblem[
                        player.CurrentCorrectPrefixLength]),
                (byte)match.CurrentRoundNumber,
                match.InputEpoch,
                elapsedSeconds);
        }

        private static void SolveProblem(
            SequenceMemoryMatchState match,
            int playerSlot,
            double elapsedSeconds)
        {
            SubmitCorrectPrefix(
                match,
                playerSlot,
                match.CurrentProblem.Length,
                elapsedSeconds);
        }

        private static void SubmitCorrectPrefix(
            SequenceMemoryMatchState match,
            int playerSlot,
            int inputCount,
            double elapsedSeconds)
        {
            var startingIndex =
                match.GetPlayer(playerSlot).CurrentCorrectPrefixLength;
            for (var index = startingIndex;
                 index < startingIndex + inputCount;
                 index++)
            {
                var result = match.SubmitInput(
                    playerSlot,
                    match.CurrentProblem[index],
                    (byte)match.CurrentRoundNumber,
                    match.InputEpoch,
                    elapsedSeconds);
                Assert.That(result.WasAccepted, Is.True);
            }
        }

        private static SequenceMemoryInput DifferentInput(
            SequenceMemoryInput input)
        {
            return input == SequenceMemoryInput.A
                ? SequenceMemoryInput.S
                : SequenceMemoryInput.A;
        }

        private static void AssertNoTripleRun(
            SequenceMemoryProblem problem)
        {
            for (var index = 2; index < problem.Length; index++)
            {
                Assert.That(
                    problem[index] == problem[index - 1] &&
                    problem[index] == problem[index - 2],
                    Is.False,
                    problem.ToString());
            }
        }

        private static int[] PlayerSlots(
            SequenceMemoryMatchResult result)
        {
            var slots = new int[result.Standings.Count];
            for (var index = 0; index < slots.Length; index++)
            {
                slots[index] = result.Standings[index].PlayerSlot;
            }

            return slots;
        }

        private static void AssertUniqueRanks(
            SequenceMemoryMatchResult result)
        {
            for (var index = 0; index < result.Standings.Count; index++)
            {
                Assert.That(
                    result.Standings[index].Rank,
                    Is.EqualTo(index + 1));
            }
        }
    }
}

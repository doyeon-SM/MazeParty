using System.Collections.Generic;
using MazeParty.Dev.MinigameSoloTest;
using MazeParty.Gameplay.Minigames.Minefield;
using MazeParty.Gameplay.Minigames.WrongWay;
using MazeParty.Multiplayer;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameSoloTestContractTests
    {
        [Test]
        public void CatalogEntriesHaveUniqueIdsAndValidProductionScenes()
        {
            var ids = new HashSet<MinigameSoloTestId>();
            var descriptors = MinigameSoloTestCatalog.All;

            Assert.That(descriptors.Count, Is.GreaterThan(0));
            for (var index = 0; index < descriptors.Count; index++)
            {
                var descriptor = descriptors[index];
                Assert.That(ids.Add(descriptor.Id), Is.True);
                Assert.That(descriptor.DisplayName, Is.Not.Empty);
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(
                        descriptor.ScenePath),
                    Is.Not.Null,
                    descriptor.ScenePath);
            }
        }

        [Test]
        public void MinefieldSessionUsesProductionDeterministicLayout()
        {
            const int seed = -19770517;
            var session = new MinefieldSoloSession();
            session.Begin(seed);

            var actual = session.CreateCurrentMineLayout();
            var expected =
                NetworkMinefieldState.GenerateMineWorldPositions(
                    unchecked((ulong)(uint)seed),
                    1);

            Assert.That(actual.Length, Is.EqualTo(MinefieldRules.PlayerCount * 5));
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (var index = 0; index < actual.Length; index++)
            {
                Assert.That(actual[index], Is.EqualTo(expected[index]));
            }
        }

        [Test]
        public void MinefieldSessionCompletesThreeSoloRoundsWithoutExtraPlayers()
        {
            var session = new MinefieldSoloSession();
            session.Begin(12345);

            for (var round = 1; round <= MinefieldRules.RoundCount; round++)
            {
                Assert.That(session.RoundNumber, Is.EqualTo(round));
                Assert.That(
                    session.Phase,
                    Is.EqualTo(MinefieldSoloPhase.Countdown));

                session.Tick(
                    (float)NetworkMinefieldState.CountdownSeconds + 0.01f);
                Assert.That(
                    session.Phase,
                    Is.EqualTo(MinefieldSoloPhase.Running));

                var cleared = round != 2;
                Assert.That(
                    session.ResolveCurrentRound(cleared),
                    Is.True);
                Assert.That(
                    session.Phase,
                    Is.EqualTo(MinefieldSoloPhase.RoundResult));
                Assert.That(
                    session.WasRoundCleared(round),
                    Is.EqualTo(cleared));

                session.Tick(
                    (float)NetworkMinefieldState.RoundResultSeconds + 0.01f);
            }

            Assert.That(
                session.Phase,
                Is.EqualTo(MinefieldSoloPhase.Complete));
            Assert.That(session.ClearedRoundCount, Is.EqualTo(2));
        }

        [Test]
        public void RunningTimeoutFailsRoundAndMovesToResult()
        {
            var session = new MinefieldSoloSession();
            session.Begin(7);
            session.Tick(
                (float)NetworkMinefieldState.CountdownSeconds + 0.01f);

            session.Tick(
                (float)NetworkMinefieldState.RunSeconds + 0.01f);

            Assert.That(
                session.Phase,
                Is.EqualTo(MinefieldSoloPhase.RoundResult));
            Assert.That(session.WasRoundCleared(1), Is.False);
            Assert.That(
                session.RemainingSeconds,
                Is.EqualTo(
                    (float)NetworkMinefieldState.RoundResultSeconds));
        }

        [Test]
        public void RestartCurrentRoundClearsItsPreviousOutcome()
        {
            var session = new MinefieldSoloSession();
            session.Begin(99);
            session.Tick(
                (float)NetworkMinefieldState.CountdownSeconds + 0.01f);
            Assert.That(session.ResolveCurrentRound(true), Is.True);
            Assert.That(session.ClearedRoundCount, Is.EqualTo(1));

            session.RestartCurrentRound();

            Assert.That(
                session.Phase,
                Is.EqualTo(MinefieldSoloPhase.Countdown));
            Assert.That(session.RoundNumber, Is.EqualTo(1));
            Assert.That(session.ClearedRoundCount, Is.Zero);
            Assert.That(session.WasRoundCleared(1), Is.False);
        }

        [Test]
        public void WrongWaySessionKeepsPromptWhileFallenAndAcceptsAfterLock()
        {
            var session = new WrongWaySoloSession();
            session.Begin(2468);
            session.Tick((float)WrongWayRules.CountdownSeconds);

            var prompt = session.CurrentPrompt.Value;
            var incorrect = prompt == WrongWayDirection.Up
                ? WrongWayDirection.Down
                : WrongWayDirection.Up;

            Assert.That(
                session.TrySubmitDirection(
                    incorrect,
                    out var wrong),
                Is.True);
            Assert.That(
                wrong.Status,
                Is.EqualTo(WrongWayInputStatus.Incorrect));
            Assert.That(session.CompletedSteps, Is.Zero);
            Assert.That(session.CurrentPrompt, Is.EqualTo(prompt));
            Assert.That(session.IsInputLocked, Is.True);
            Assert.That(
                session.InputLockSecondsRemaining,
                Is.EqualTo(0.5f).Within(0.001f));

            session.Tick(0.49f);
            session.TrySubmitDirection(prompt, out var ignored);
            Assert.That(
                ignored.Status,
                Is.EqualTo(WrongWayInputStatus.IgnoredWhileLocked));
            Assert.That(session.CompletedSteps, Is.Zero);

            session.Tick(0.02f);
            session.TrySubmitDirection(prompt, out var accepted);
            Assert.That(
                accepted.Status,
                Is.EqualTo(WrongWayInputStatus.Correct));
            Assert.That(session.CompletedSteps, Is.EqualTo(1));
        }

        [Test]
        public void WrongWaySessionCompletesTwoFiftyStepRoundsLocally()
        {
            var session = new WrongWaySoloSession();
            session.Begin(97531);

            for (var roundNumber = 1;
                 roundNumber <= WrongWayRules.RoundCount;
                 roundNumber++)
            {
                Assert.That(
                    session.RoundNumber,
                    Is.EqualTo(roundNumber));
                session.Tick(
                    (float)WrongWayRules.CountdownSeconds);
                Assert.That(
                    session.Phase,
                    Is.EqualTo(WrongWaySoloPhase.Running));

                for (var step = 0;
                     step < WrongWayRules.StepCount;
                     step++)
                {
                    Assert.That(
                        session.TrySubmitDirection(
                            session.CurrentPrompt.Value,
                            out _),
                        Is.True);
                }

                Assert.That(
                    session.Phase,
                    Is.EqualTo(WrongWaySoloPhase.RoundResult));
                var standing = session
                    .GetRoundResult(roundNumber)
                    .GetStandingForSlot(
                        WrongWaySoloSession.LocalPlayerSlot);
                Assert.That(
                    standing.CompletedSteps,
                    Is.EqualTo(WrongWayRules.StepCount));
                Assert.That(standing.Rank, Is.EqualTo(1));

                session.Tick(
                    WrongWaySoloSession.RoundResultSeconds);
            }

            Assert.That(
                session.Phase,
                Is.EqualTo(WrongWaySoloPhase.Complete));
            Assert.That(session.Leaderboard, Has.Count.EqualTo(4));
            Assert.That(
                session.Leaderboard[0].PlayerSlot,
                Is.EqualTo(WrongWaySoloSession.LocalPlayerSlot));
            Assert.That(
                session.Leaderboard[0].TotalCompletedSteps,
                Is.EqualTo(WrongWayRules.StepCount * 2));
        }

    }
}

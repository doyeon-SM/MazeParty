using System.Collections.Generic;
using MazeParty.Dev.MinigameSoloTest;
using MazeParty.Gameplay.Minigames.Minefield;
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
    }
}

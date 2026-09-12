using System.Collections.Generic;
using MazeParty.Dev.MinigameSoloTest;
using MazeParty.Gameplay.Minigames.StableFooting;
using NUnit.Framework;
using UnityEditor;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameSoloTestContractTests
    {
        [Test]
        public void CatalogAndStableFootingHarness_PreserveEssentialContracts()
        {
            var ids = new HashSet<MinigameSoloTestId>();
            var descriptors = MinigameSoloTestCatalog.All;

            Assert.That(descriptors, Is.Not.Empty);
            foreach (var descriptor in descriptors)
            {
                Assert.That(
                    ids.Add(descriptor.Id),
                    Is.True,
                    descriptor.Id.ToString());
                Assert.That(descriptor.DisplayName, Is.Not.Empty);
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(
                        descriptor.ScenePath),
                    Is.Not.Null,
                    descriptor.ScenePath);
            }

            Assert.That(
                (byte)MinigameSoloTestId.RedLightGreenLight,
                Is.EqualTo(3),
                "Serialized solo-test IDs must remain stable.");
            Assert.That(
                MinigameSoloTestCatalog.TryGet(
                    MinigameSoloTestId.RedLightGreenLight,
                    out var redLightGreenLight),
                Is.True);
            Assert.That(
                redLightGreenLight.ScenePath,
                Is.EqualTo(
                    MinigameSoloTestCatalog.RedLightGreenLightScenePath));
            Assert.That(
                (byte)MinigameSoloTestId.StableFooting,
                Is.EqualTo(4),
                "Serialized solo-test IDs must remain stable.");
            Assert.That(
                MinigameSoloTestCatalog.TryGet(
                    MinigameSoloTestId.StableFooting,
                    out var stableFooting),
                Is.True);
            Assert.That(
                stableFooting.ScenePath,
                Is.EqualTo(
                    MinigameSoloTestCatalog.StableFootingScenePath));
            Assert.That(
                (byte)MinigameSoloTestId.BalloonBlow,
                Is.EqualTo(5),
                "Serialized solo-test IDs must remain stable.");
            Assert.That(
                MinigameSoloTestCatalog.TryGet(
                    MinigameSoloTestId.BalloonBlow,
                    out var balloonBlow),
                Is.True);
            Assert.That(
                balloonBlow.ScenePath,
                Is.EqualTo(
                    MinigameSoloTestCatalog.BalloonBlowScenePath));
            Assert.That(
                (byte)MinigameSoloTestId.GiftGrab,
                Is.EqualTo(6),
                "Serialized solo-test IDs must remain stable.");
            Assert.That(
                MinigameSoloTestCatalog.TryGet(
                    MinigameSoloTestId.GiftGrab,
                    out var giftGrab),
                Is.True);
            Assert.That(
                giftGrab.ScenePath,
                Is.EqualTo(MinigameSoloTestCatalog.GiftGrabScenePath));

            var session = new StableFootingSoloSession();
            session.Begin(12345);
            session.Tick((float)StableFootingSoloSession.CountdownSeconds);
            var firstCycle = session.CurrentCycle;
            Assert.That(firstCycle, Is.Not.Null);
            var safeTile = -1;
            var unsafeTile = -1;
            foreach (var assignment in firstCycle.Assignments)
            {
                if (firstCycle.IsTileSafe(assignment.TileIndex))
                {
                    safeTile = assignment.TileIndex;
                }
                else
                {
                    unsafeTile = assignment.TileIndex;
                }

                if (safeTile >= 0 && unsafeTile >= 0)
                {
                    break;
                }
            }
            Assert.That(safeTile, Is.GreaterThanOrEqualTo(0));
            Assert.That(unsafeTile, Is.GreaterThanOrEqualTo(0));
            session.SetPlayerTile(0, safeTile);
            for (var slot = 1;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                session.SetPlayerTile(slot, unsafeTile);
            }
            session.Tick((float)firstCycle.MoveEndsAtSeconds + 0.01f);

            Assert.That(
                session.Phase,
                Is.EqualTo(StableFootingSoloPhase.RoundResult));
            Assert.That(session.PresentationCycle, Is.SameAs(firstCycle));
            Assert.That(
                session.PresentationCyclePhase,
                Is.EqualTo(StableFootingCyclePhase.Drop),
                "The decisive collapse must remain visible during results.");
        }
    }
}

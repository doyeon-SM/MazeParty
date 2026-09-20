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
                Assert.That(descriptor.ControlsLabel, Is.Not.Empty);
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
            Assert.That(
                (byte)MinigameSoloTestId.TerritoryPaint,
                Is.EqualTo(7),
                "Serialized solo-test IDs must remain stable.");
            Assert.That(
                MinigameSoloTestCatalog.TryGet(
                    MinigameSoloTestId.TerritoryPaint,
                    out var territoryPaint),
                Is.True);
            Assert.That(
                territoryPaint.ScenePath,
                Is.EqualTo(
                    MinigameSoloTestCatalog.TerritoryPaintScenePath));
            Assert.That(
                (byte)MinigameSoloTestId.TagChase,
                Is.EqualTo(8),
                "Serialized solo-test IDs must remain stable.");
            Assert.That(
                MinigameSoloTestCatalog.TryGet(
                    MinigameSoloTestId.TagChase,
                    out var tagChase),
                Is.True);
            Assert.That(
                tagChase.ScenePath,
                Is.EqualTo(MinigameSoloTestCatalog.TagChaseScenePath));
            Assert.That(
                (byte)MinigameSoloTestId.Race,
                Is.EqualTo(9),
                "Serialized solo-test IDs must remain stable.");
            Assert.That(
                MinigameSoloTestCatalog.TryGet(
                    MinigameSoloTestId.Race,
                    out var race),
                Is.True);
            Assert.That(
                race.ScenePath,
                Is.EqualTo(MinigameSoloTestCatalog.RaceScenePath));
            Assert.That(
                (byte)MinigameSoloTestId.SequenceMemory,
                Is.EqualTo(10),
                "Serialized solo-test IDs must remain stable.");
            Assert.That(
                MinigameSoloTestCatalog.TryGet(
                    MinigameSoloTestId.SequenceMemory,
                    out var sequenceMemory),
                Is.True);
            Assert.That(
                sequenceMemory.ScenePath,
                Is.EqualTo(
                    MinigameSoloTestCatalog.SequenceMemoryScenePath));
            Assert.That(
                sequenceMemory.DisplayName,
                Is.EqualTo("Sequence Memory"));
            Assert.That(
                sequenceMemory.ControlsLabel,
                Does.Contain("A / S / D"));
            Assert.That(
                (byte)MinigameSoloTestId.BouncingBalls,
                Is.EqualTo(11),
                "Serialized solo-test IDs must remain stable.");
            Assert.That(
                MinigameSoloTestCatalog.TryGet(
                    MinigameSoloTestId.BouncingBalls,
                    out var bouncingBalls),
                Is.True);
            Assert.That(
                bouncingBalls.ScenePath,
                Is.EqualTo(
                    MinigameSoloTestCatalog.BouncingBallsScenePath));
            Assert.That(
                bouncingBalls.DisplayName,
                Is.EqualTo("Bouncing Balls"));
            Assert.That(
                bouncingBalls.ControlsLabel,
                Does.Contain("A / D"));
            Assert.That(
                (byte)MinigameSoloTestId.BombPassing,
                Is.EqualTo(12),
                "Serialized solo-test IDs must remain stable.");
            Assert.That(
                MinigameSoloTestCatalog.TryGet(
                    MinigameSoloTestId.BombPassing,
                    out var bombPassing),
                Is.True);
            Assert.That(
                bombPassing.ScenePath,
                Is.EqualTo(
                    MinigameSoloTestCatalog.BombPassingScenePath));
            Assert.That(
                bombPassing.DisplayName,
                Is.EqualTo("Bomb Passing"));
            Assert.That(
                bombPassing.ControlsLabel,
                Does.Contain("LMB"));
            Assert.That(
                (byte)MinigameSoloTestId.SnowySpin,
                Is.EqualTo(13),
                "Serialized solo-test IDs must remain stable.");
            Assert.That(
                MinigameSoloTestCatalog.TryGet(
                    MinigameSoloTestId.SnowySpin,
                    out var snowySpin),
                Is.True);
            Assert.That(
                snowySpin.ScenePath,
                Is.EqualTo(
                    MinigameSoloTestCatalog.SnowySpinScenePath));
            Assert.That(
                snowySpin.DisplayName,
                Is.EqualTo("Snowy Spin"));
            Assert.That(
                snowySpin.ControlsLabel,
                Does.Contain("WASD"));

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

using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class WorldDieAuthorityModelTests
    {
        [Test]
        public void Prepare_RejectsInvalidSlotAndAcceptsFourSeatRange()
        {
            var model = new WorldDieAuthorityModel();

            Assert.That(model.Prepare(-1, Vector2Int.zero), Is.False);
            Assert.That(model.Prepare(
                MultiplayerConstants.MaxPlayers,
                Vector2Int.zero), Is.False);
            Assert.That(model.Prepare(3, new Vector2Int(2, 5)), Is.True);
            Assert.That(model.Slot, Is.EqualTo(3));
            Assert.That(model.Phase, Is.EqualTo(WorldDiePhase.Ready));
            Assert.That(model.TileCoordinate, Is.EqualTo(new Vector2Int(2, 5)));
        }

        [Test]
        public void Push_AllowsOnlyMatchingSlotWithResolvedChoiceAndLiveAction()
        {
            var model = CreateReadyModel(2);
            var accepted = model.TryBeginRoll(
                Context(2),
                10d,
                out var rejectReason);

            Assert.That(accepted, Is.True);
            Assert.That(rejectReason, Is.EqualTo(WorldDiePushRejectReason.None));
            Assert.That(model.Phase, Is.EqualTo(WorldDiePhase.Rolling));
        }

        [TestCase(1, true, true, false, false, true, WorldDiePushRejectReason.WrongPlayer)]
        [TestCase(2, false, true, false, false, true, WorldDiePushRejectReason.ActionUnavailable)]
        [TestCase(2, true, false, false, false, true, WorldDiePushRejectReason.ItemChoicePending)]
        [TestCase(2, true, true, true, false, true, WorldDiePushRejectReason.AlreadyRolled)]
        [TestCase(2, true, true, false, true, true, WorldDiePushRejectReason.ReconnectPaused)]
        [TestCase(2, true, true, false, false, false, WorldDiePushRejectReason.PlayerOutsideAssignedTile)]
        public void Push_RejectsUntrustedOrUnavailableRequest(
            int requesterSlot,
            bool actionAvailable,
            bool choiceResolved,
            bool alreadyRolled,
            bool reconnectPaused,
            bool insideTile,
            WorldDiePushRejectReason expected)
        {
            var model = CreateReadyModel(2);
            var context = new WorldDiePushContext(
                requesterSlot,
                actionAvailable,
                choiceResolved,
                alreadyRolled,
                reconnectPaused,
                insideTile);

            Assert.That(
                model.TryBeginRoll(context, 10d, out var actual),
                Is.False);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(model.Phase, Is.EqualTo(WorldDiePhase.Ready));
        }

        [Test]
        public void Motion_SettlesOnlyAfterContinuousLowVelocityWindow()
        {
            var model = CreateRollingModel();

            Assert.That(
                Observe(model, 1d, 0.01f, 0.01f),
                Is.EqualTo(WorldDieMotionDecision.None));
            Assert.That(
                Observe(model, 1.4d, 0.5f, 0.01f),
                Is.EqualTo(WorldDieMotionDecision.None));
            Assert.That(
                Observe(model, 2d, 0.01f, 0.01f),
                Is.EqualTo(WorldDieMotionDecision.None));
            Assert.That(
                Observe(model, 2.7d, 0.01f, 0.01f),
                Is.EqualTo(WorldDieMotionDecision.Settle));
        }

        [Test]
        public void Motion_ForceSettlesAtMaximumRollDuration()
        {
            var model = CreateRollingModel();

            Assert.That(
                Observe(model, 5.99d, 1f, 1f),
                Is.EqualTo(WorldDieMotionDecision.None));
            Assert.That(
                Observe(model, 6d, 1f, 1f),
                Is.EqualTo(WorldDieMotionDecision.ForceSettle));
        }

        [Test]
        public void Pause_DoesNotConsumeRollOrSettleDeadline()
        {
            var model = CreateRollingModel();
            model.SetPaused(true, 2d);

            Assert.That(
                Observe(model, 50d, 0f, 0f),
                Is.EqualTo(WorldDieMotionDecision.None));

            model.SetPaused(false, 102d);
            Assert.That(
                Observe(model, 105.9d, 1f, 1f),
                Is.EqualTo(WorldDieMotionDecision.None));
            Assert.That(
                Observe(model, 106d, 1f, 1f),
                Is.EqualTo(WorldDieMotionDecision.ForceSettle));
        }

        [Test]
        public void Snapshot_RestoresRollingSlotTileAndPausedTiming()
        {
            var source = CreateRollingModel();
            source.SetPaused(true, 2d);
            var snapshot = source.Capture(50d);
            var restored = new WorldDieAuthorityModel();

            Assert.That(restored.Restore(snapshot, 100d), Is.True);
            Assert.That(restored.Slot, Is.EqualTo(1));
            Assert.That(restored.TileCoordinate, Is.EqualTo(new Vector2Int(4, 7)));
            Assert.That(restored.Phase, Is.EqualTo(WorldDiePhase.Rolling));
            Assert.That(restored.IsPaused, Is.True);

            restored.SetPaused(false, 110d);
            Assert.That(
                Observe(restored, 113.9d, 1f, 1f),
                Is.EqualTo(WorldDieMotionDecision.None));
            Assert.That(
                Observe(restored, 114d, 1f, 1f),
                Is.EqualTo(WorldDieMotionDecision.ForceSettle));
        }

        [Test]
        public void SettledFace_AcceptsBoardD10RangeAndPreventsSecondPush()
        {
            var model = CreateRollingModel();

            Assert.That(model.MarkSettled(10), Is.True);
            Assert.That(model.SettledFace, Is.EqualTo(10));
            Assert.That(model.Phase, Is.EqualTo(WorldDiePhase.Settled));
            Assert.That(
                model.TryBeginRoll(Context(1), 20d, out var reason),
                Is.False);
            Assert.That(reason, Is.EqualTo(WorldDiePushRejectReason.NotReady));
        }

        [Test]
        public void ResultPresentation_HidesAtExactTwoSecondBoundary()
        {
            var hideAt = 10d + WorldDieResultPresentationPolicy.DefaultVisibleSeconds;

            Assert.That(
                WorldDieResultPresentationPolicy.ShouldHide(
                    WorldDiePhase.Settled,
                    false,
                    hideAt - 0.001d,
                    hideAt),
                Is.False);
            Assert.That(
                WorldDieResultPresentationPolicy.ShouldHide(
                    WorldDiePhase.Settled,
                    false,
                    hideAt,
                    hideAt),
                Is.True);
        }

        [Test]
        public void ResultPresentation_DoesNotExpireWhilePausedOrBeforeSettlement()
        {
            Assert.That(
                WorldDieResultPresentationPolicy.ShouldHide(
                    WorldDiePhase.Settled,
                    true,
                    20d,
                    12d),
                Is.False);
            Assert.That(
                WorldDieResultPresentationPolicy.ShouldHide(
                    WorldDiePhase.Rolling,
                    false,
                    20d,
                    12d),
                Is.False);
        }

        [Test]
        public void TileFrame_ClampsCenterAndRemovesOnlyOutwardVelocity()
        {
            var frame = new WorldDieTileFrame(
                Vector3.zero,
                Vector3.right,
                Vector3.forward,
                Vector3.up,
                4f);

            var constrained = frame.Constrain(
                new Vector3(4f, 1f, 0f),
                new Vector3(3f, -2f, 1f),
                0.5f,
                0.5f,
                0f,
                out var position,
                out var velocity);

            Assert.That(constrained, Is.True);
            Assert.That(position.x, Is.EqualTo(3.5f).Within(0.0001f));
            Assert.That(position.y, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(velocity.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(velocity.y, Is.EqualTo(-2f).Within(0.0001f));
            Assert.That(velocity.z, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void FaceResolver_UsesHighestRotatedConfiguredMarker()
        {
            var normals = new[]
            {
                Vector3.up,
                Vector3.right,
                Vector3.down
            };
            var values = new[] { 10, 4, 1 };

            Assert.That(
                WorldDieFaceResolver.ResolveHighestFace(
                    Quaternion.identity,
                    normals,
                    values),
                Is.EqualTo(10));
            Assert.That(
                WorldDieFaceResolver.ResolveHighestFace(
                    Quaternion.Euler(0f, 0f, 90f),
                    normals,
                    values),
                Is.EqualTo(4));
        }

        private static WorldDieAuthorityModel CreateReadyModel(int slot)
        {
            var model = new WorldDieAuthorityModel();
            Assert.That(model.Prepare(slot, new Vector2Int(4, 7)), Is.True);
            return model;
        }

        private static WorldDieAuthorityModel CreateRollingModel()
        {
            var model = CreateReadyModel(1);
            Assert.That(
                model.TryBeginRoll(Context(1), 0d, out _),
                Is.True);
            return model;
        }

        private static WorldDiePushContext Context(int slot)
        {
            return new WorldDiePushContext(
                slot,
                true,
                true,
                false,
                false,
                true);
        }

        private static WorldDieMotionDecision Observe(
            WorldDieAuthorityModel model,
            double now,
            float linearSpeed,
            float angularSpeed)
        {
            return model.ObserveMotion(
                now,
                linearSpeed,
                angularSpeed,
                0.08f,
                0.12f,
                0.65d,
                6d);
        }
    }
}

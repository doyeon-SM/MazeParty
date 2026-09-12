using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class WorldDieAuthorityModelTests
    {
        [Test]
        public void D12Layout_ResolvesEveryFaceAndLandingRotation()
        {
            Assert.That(WorldDieAuthorityModel.MinimumFace, Is.EqualTo(1));
            Assert.That(WorldDieAuthorityModel.MaximumFace, Is.EqualTo(12));
            Assert.That(WorldDieD12Layout.FaceCount, Is.EqualTo(12));

            var normals = new Vector3[WorldDieD12Layout.FaceCount];
            var values = new int[WorldDieD12Layout.FaceCount];
            var tileUp = new Vector3(0.2f, 0.95f, -0.23f).normalized;
            var startRotation = Quaternion.Euler(17f, 83f, 241f);
            for (var index = 0; index < values.Length; index++)
            {
                var face = index + WorldDieAuthorityModel.MinimumFace;
                values[index] = face;
                Assert.That(
                    WorldDieD12Layout.TryGetLocalNormal(
                        face,
                        out normals[index]),
                    Is.True);
                Assert.That(
                    normals[index].magnitude,
                    Is.EqualTo(1f).Within(0.00001f));
                Assert.That(
                    WorldDieD12Layout.TryGetLocalMarkerPosition(
                        face,
                        out var markerPosition),
                    Is.True);
                Assert.That(
                    markerPosition.magnitude,
                    Is.EqualTo(WorldDieD12Layout.FaceMarkerRadius)
                        .Within(0.00001f));
                Assert.That(
                    WorldDieD12Layout.TryGetLocalNormal(
                        13 - face,
                        out var opposite),
                    Is.True);
                Assert.That(
                    (normals[index] + opposite).sqrMagnitude,
                    Is.LessThan(0.000001f));
            }

            for (var index = 0; index < values.Length; index++)
            {
                var landingRotation = WorldDieRollPresentationPolicy
                    .ResolveLandingRotation(
                        startRotation,
                        normals[index],
                        tileUp);
                Assert.That(
                    Vector3.Dot(
                        landingRotation * normals[index],
                        tileUp),
                    Is.GreaterThan(0.99999f));

                var worldUpRotation =
                    Quaternion.FromToRotation(tileUp, Vector3.up) *
                    landingRotation;
                Assert.That(
                    WorldDieFaceResolver.ResolveHighestFace(
                        worldUpRotation,
                        normals,
                        values),
                    Is.EqualTo(values[index]));
            }

            Assert.That(
                WorldDieD12Layout.TryGetLocalNormal(0, out _),
                Is.False);
            Assert.That(
                WorldDieD12Layout.TryGetLocalNormal(13, out _),
                Is.False);
        }

        [Test]
        public void TileConfinement_UsesRotatedBoundsAndRemovesOutwardVelocity()
        {
            var dieCenter = new Vector3(1.25f, -0.4f, 2.1f);
            var bounds = new Bounds(
                dieCenter + new Vector3(0.08f, -0.03f, 0.05f),
                new Vector3(0.9f, 0.7f, 1.1f));
            var sourceRotation = Quaternion.Euler(17f, 83f, 241f);
            var targetRotation = Quaternion.Euler(311f, 129f, 44f);
            var frame = new WorldDieTileFrame(
                dieCenter,
                Vector3.right,
                Vector3.forward,
                Vector3.up,
                4f);
            var rightExtent = WorldDieRollPresentationPolicy
                .GetTargetRotationProjectedExtent(
                    bounds,
                    dieCenter,
                    sourceRotation,
                    targetRotation,
                    frame.Right);
            var forwardExtent = WorldDieRollPresentationPolicy
                .GetTargetRotationProjectedExtent(
                    bounds,
                    dieCenter,
                    sourceRotation,
                    targetRotation,
                    frame.Forward);

            AssertProjectedExtentContainsEveryRotatedCorner(
                bounds,
                dieCenter,
                sourceRotation,
                targetRotation,
                frame.Right,
                rightExtent);
            AssertProjectedExtentContainsEveryRotatedCorner(
                bounds,
                dieCenter,
                sourceRotation,
                targetRotation,
                frame.Forward,
                forwardExtent);
            Assert.That(
                frame.Constrain(
                    dieCenter + frame.Right * 10f + frame.Forward * 10f,
                    frame.Right * 3f + frame.Forward + Vector3.down * 2f,
                    rightExtent,
                    forwardExtent,
                    0f,
                    out var constrained,
                    out var velocity),
                Is.True);
            Assert.That(
                Mathf.Abs(Vector3.Dot(
                    constrained - frame.Center,
                    frame.Right)) + rightExtent,
                Is.LessThanOrEqualTo(frame.HalfExtent + 0.00001f));
            Assert.That(
                Mathf.Abs(Vector3.Dot(
                    constrained - frame.Center,
                    frame.Forward)) + forwardExtent,
                Is.LessThanOrEqualTo(frame.HalfExtent + 0.00001f));
            Assert.That(
                Vector3.Dot(velocity, frame.Right),
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                Vector3.Dot(velocity, frame.Forward),
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(velocity.y, Is.EqualTo(-2f).Within(0.0001f));
        }

        [Test]
        public void Lifecycle_RejectsInvalidSlotsAndInvalidOrRepeatedSettlement()
        {
            var model = new WorldDieAuthorityModel();
            Assert.That(model.Prepare(-1, Vector2Int.zero), Is.False);
            Assert.That(
                model.Prepare(
                    MultiplayerConstants.MaxPlayers,
                    Vector2Int.zero),
                Is.False);
            Assert.That(model.Prepare(3, new Vector2Int(2, 5)), Is.True);
            Assert.That(model.Slot, Is.EqualTo(3));
            Assert.That(model.Phase, Is.EqualTo(WorldDiePhase.Ready));
            Assert.That(
                model.TryBeginRoll(Context(3), 10d, out _),
                Is.True);
            Assert.That(
                model.MarkSettled(WorldDieAuthorityModel.MaximumFace),
                Is.True);
            Assert.That(model.Phase, Is.EqualTo(WorldDiePhase.Settled));
            Assert.That(
                model.TryBeginRoll(Context(3), 20d, out var reason),
                Is.False);
            Assert.That(reason, Is.EqualTo(WorldDiePushRejectReason.NotReady));

            var invalidFace = CreateRollingModel();
            Assert.That(
                invalidFace.MarkSettled(
                    WorldDieAuthorityModel.MaximumFace + 1),
                Is.False);
            Assert.That(invalidFace.Phase, Is.EqualTo(WorldDiePhase.Rolling));
        }

        [Test]
        public void Push_AllowsMatchingSlotWithResolvedChoiceAndLiveAction()
        {
            var model = CreateReadyModel(2);

            Assert.That(
                model.TryBeginRoll(Context(2), 10d, out var reason),
                Is.True);
            Assert.That(reason, Is.EqualTo(WorldDiePushRejectReason.None));
            Assert.That(model.Phase, Is.EqualTo(WorldDiePhase.Rolling));
        }

        [Test]
        public void Push_RejectsEveryUntrustedOrUnavailableContext()
        {
            var contexts = new[]
            {
                new WorldDiePushContext(1, true, true, false, false, true),
                new WorldDiePushContext(2, false, true, false, false, true),
                new WorldDiePushContext(2, true, false, false, false, true),
                new WorldDiePushContext(2, true, true, true, false, true),
                new WorldDiePushContext(2, true, true, false, true, true),
                new WorldDiePushContext(2, true, true, false, false, false)
            };
            var expectedReasons = new[]
            {
                WorldDiePushRejectReason.WrongPlayer,
                WorldDiePushRejectReason.ActionUnavailable,
                WorldDiePushRejectReason.ItemChoicePending,
                WorldDiePushRejectReason.AlreadyRolled,
                WorldDiePushRejectReason.ReconnectPaused,
                WorldDiePushRejectReason.PlayerOutsideAssignedTile
            };

            for (var index = 0; index < contexts.Length; index++)
            {
                var model = CreateReadyModel(2);
                Assert.That(
                    model.TryBeginRoll(
                        contexts[index],
                        10d,
                        out var actualReason),
                    Is.False,
                    expectedReasons[index].ToString());
                Assert.That(actualReason, Is.EqualTo(expectedReasons[index]));
                Assert.That(model.Phase, Is.EqualTo(WorldDiePhase.Ready));
            }
        }

        [Test]
        public void Motion_SettlesAfterLowVelocityOrMaximumDuration()
        {
            var naturallySettling = CreateRollingModel();
            Assert.That(
                Observe(naturallySettling, 1d, 0.01f, 0.01f),
                Is.EqualTo(WorldDieMotionDecision.None));
            Assert.That(
                Observe(naturallySettling, 1.4d, 0.5f, 0.01f),
                Is.EqualTo(WorldDieMotionDecision.None));
            Assert.That(
                Observe(naturallySettling, 2d, 0.01f, 0.01f),
                Is.EqualTo(WorldDieMotionDecision.None));
            Assert.That(
                Observe(naturallySettling, 2.7d, 0.01f, 0.01f),
                Is.EqualTo(WorldDieMotionDecision.Settle));

            var forced = CreateRollingModel();
            Assert.That(
                Observe(forced, 5.99d, 1f, 1f),
                Is.EqualTo(WorldDieMotionDecision.None));
            Assert.That(
                Observe(forced, 6d, 1f, 1f),
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
            Assert.That(
                restored.TileCoordinate,
                Is.EqualTo(new Vector2Int(4, 7)));
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

        private static void AssertProjectedExtentContainsEveryRotatedCorner(
            Bounds bounds,
            Vector3 dieCenter,
            Quaternion sourceRotation,
            Quaternion targetRotation,
            Vector3 axis,
            float extent)
        {
            var rotationDelta =
                targetRotation * Quaternion.Inverse(sourceRotation);
            var normalizedAxis = axis.normalized;
            for (var x = -1; x <= 1; x += 2)
            {
                for (var y = -1; y <= 1; y += 2)
                {
                    for (var z = -1; z <= 1; z += 2)
                    {
                        var corner = bounds.center + Vector3.Scale(
                            bounds.extents,
                            new Vector3(x, y, z));
                        var rotatedOffset =
                            rotationDelta * (corner - dieCenter);
                        Assert.That(
                            Mathf.Abs(Vector3.Dot(
                                rotatedOffset,
                                normalizedAxis)),
                            Is.LessThanOrEqualTo(extent + 0.00001f));
                    }
                }
            }
        }
    }
}

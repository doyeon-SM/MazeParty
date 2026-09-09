using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class WorldDieAuthorityModelTests
    {
        [Test]
        public void FaceBounds_UseBoardD12Range()
        {
            Assert.That(WorldDieAuthorityModel.MinimumFace, Is.EqualTo(1));
            Assert.That(WorldDieAuthorityModel.MaximumFace, Is.EqualTo(12));
            Assert.That(WorldDieD12Layout.FaceCount, Is.EqualTo(12));
        }

        [Test]
        public void D12Layout_ContainsNormalizedMarkersAndOppositeFacesSumToThirteen()
        {
            for (var face = WorldDieAuthorityModel.MinimumFace;
                 face <= WorldDieAuthorityModel.MaximumFace;
                 face++)
            {
                Assert.That(
                    WorldDieD12Layout.TryGetLocalNormal(face, out var normal),
                    Is.True);
                Assert.That(normal.magnitude, Is.EqualTo(1f).Within(0.00001f));
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
                    WorldDieD12Layout.TryGetLocalNormal(13 - face, out var opposite),
                    Is.True);
                Assert.That(
                    (normal + opposite).sqrMagnitude,
                    Is.LessThan(0.000001f));
            }

            Assert.That(
                WorldDieD12Layout.TryGetLocalNormal(0, out _),
                Is.False);
            Assert.That(
                WorldDieD12Layout.TryGetLocalNormal(13, out _),
                Is.False);
        }

        [Test]
        public void RollPresentation_UsesOneSecondBoundaryAndPauseSafeTimestamps()
        {
            Assert.That(
                WorldDieRollPresentationPolicy.TotalSeconds,
                Is.EqualTo(1d));
            Assert.That(
                WorldDieRollPresentationPolicy.PhysicalTumbleSeconds +
                WorldDieRollPresentationPolicy.LandingSeconds,
                Is.EqualTo(WorldDieRollPresentationPolicy.TotalSeconds));
            Assert.That(
                WorldDieRollPresentationPolicy.ShouldBeginLanding(0.649999d),
                Is.False);
            Assert.That(
                WorldDieRollPresentationPolicy.ShouldBeginLanding(0.65d),
                Is.True);
            Assert.That(
                WorldDieRollPresentationPolicy.ShouldCommitResult(0.999999d),
                Is.False);
            Assert.That(
                WorldDieRollPresentationPolicy.ShouldCommitResult(1d),
                Is.True);
            Assert.That(
                WorldDieRollPresentationPolicy.GetLandingProgress(0.65d),
                Is.EqualTo(0f).Within(0.00001f));
            Assert.That(
                WorldDieRollPresentationPolicy.GetLandingProgress(0.825d),
                Is.EqualTo(0.5f).Within(0.00001f));
            Assert.That(
                WorldDieRollPresentationPolicy.GetLandingProgress(1d),
                Is.EqualTo(1f).Within(0.00001f));
            Assert.That(
                WorldDieRollPresentationPolicy.ShiftTimestampForPause(10d, 2.5d),
                Is.EqualTo(12.5d));
            Assert.That(
                WorldDieRollPresentationPolicy.ShiftTimestampForPause(-1d, 2.5d),
                Is.EqualTo(-1d));
            Assert.That(
                WorldDieRollPresentationPolicy.GetLandingCenterHeight(
                    0.1f,
                    0.336f,
                    0.01f),
                Is.EqualTo(0.446f).Within(0.00001f));
        }

        [Test]
        public void TargetRotationProjectedExtent_ContainsRotatedColliderBounds()
        {
            var dieCenter = new Vector3(1.25f, -0.4f, 2.1f);
            var bounds = new Bounds(
                dieCenter + new Vector3(0.08f, -0.03f, 0.05f),
                new Vector3(0.9f, 0.7f, 1.1f));
            var sourceRotation = Quaternion.Euler(17f, 83f, 241f);
            var targetRotation = Quaternion.Euler(311f, 129f, 44f);
            var right = new Vector3(0.94f, 0.2f, -0.27f).normalized;
            var forward = Vector3.Cross(right, new Vector3(0.1f, 0.97f, 0.21f))
                .normalized;

            AssertProjectedExtentContainsEveryRotatedCorner(
                bounds,
                dieCenter,
                sourceRotation,
                targetRotation,
                right);
            AssertProjectedExtentContainsEveryRotatedCorner(
                bounds,
                dieCenter,
                sourceRotation,
                targetRotation,
                forward);

            var frame = new WorldDieTileFrame(
                dieCenter,
                right,
                forward,
                Vector3.Cross(forward, right),
                4f);
            var rightExtent =
                WorldDieRollPresentationPolicy.GetTargetRotationProjectedExtent(
                    bounds,
                    dieCenter,
                    sourceRotation,
                    targetRotation,
                    frame.Right);
            var forwardExtent =
                WorldDieRollPresentationPolicy.GetTargetRotationProjectedExtent(
                    bounds,
                    dieCenter,
                    sourceRotation,
                    targetRotation,
                    frame.Forward);
            Assert.That(
                frame.Constrain(
                    dieCenter + frame.Right * 10f + frame.Forward * 10f,
                    Vector3.zero,
                    rightExtent,
                    forwardExtent,
                    0f,
                    out var constrained,
                    out _),
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
        }

        [Test]
        public void HudRoll_IsVisibleOnlyForOwnersMatchingSettledWorldDie()
        {
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveVisibleRoll(
                    true,
                    12,
                    2,
                    true,
                    2,
                    WorldDiePhase.Settled,
                    12),
                Is.EqualTo(12));
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveVisibleRoll(
                    true,
                    12,
                    2,
                    true,
                    2,
                    WorldDiePhase.Hidden,
                    0),
                Is.Zero);
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveVisibleRoll(
                    true,
                    12,
                    2,
                    true,
                    1,
                    WorldDiePhase.Settled,
                    12),
                Is.Zero);
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveVisibleRoll(
                    false,
                    12,
                    2,
                    true,
                    2,
                    WorldDiePhase.Settled,
                    12),
                Is.Zero);
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveVisibleRoll(
                    true,
                    0,
                    2,
                    true,
                    2,
                    WorldDiePhase.Settled,
                    7,
                    true),
                Is.EqualTo(7),
                "A phase reset must not truncate the settled die's reveal.");
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveVisibleRoll(
                    true,
                    0,
                    2,
                    true,
                    2,
                    WorldDiePhase.Settled,
                    7),
                Is.Zero,
                "An uncommitted Action roll must not use the reset fallback.");
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveVisibleRoll(
                    true,
                    12,
                    2,
                    true,
                    2,
                    WorldDiePhase.Settled,
                    7),
                Is.Zero,
                "A live private roll must still match the public result.");
        }

        [Test]
        public void HudStatus_ShowsCompletionWithoutReofferingRollAfterReveal()
        {
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveDiceStatusLabel(
                    12,
                    true,
                    true,
                    true,
                    true,
                    WorldDiePhase.Settled,
                    12),
                Is.EqualTo("DICE  12"));
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveDiceStatusLabel(
                    0,
                    true,
                    true,
                    true,
                    true,
                    WorldDiePhase.Hidden,
                    0),
                Is.EqualTo(WorldDieHudPresentationPolicy.RollCompleteLabel));
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveDiceStatusLabel(
                    0,
                    false,
                    true,
                    true,
                    true,
                    WorldDiePhase.Ready,
                    0),
                Is.EqualTo("RMB  AIM AT YOUR DIE TO ROLL"));
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveDiceStatusLabel(
                    0,
                    false,
                    false,
                    true,
                    true,
                    WorldDiePhase.Hidden,
                    0),
                Is.EqualTo("DICE  CHOOSE ITEM FIRST"));
        }

        [Test]
        public void HudStatus_ShowsRollingAndClosedActionWindowWithoutRollPrompt()
        {
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveDiceStatusLabel(
                    0,
                    false,
                    true,
                    true,
                    false,
                    WorldDiePhase.Rolling,
                    0),
                Is.EqualTo("DICE  ROLLING..."));
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveDiceStatusLabel(
                    0,
                    false,
                    true,
                    true,
                    false,
                    WorldDiePhase.Ready,
                    0),
                Is.EqualTo("DICE  TIME EXPIRED"));
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveDiceStatusLabel(
                    0,
                    false,
                    true,
                    true,
                    false,
                    WorldDiePhase.Settled,
                    7),
                Is.EqualTo(WorldDieHudPresentationPolicy.RollCompleteLabel));
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveDiceStatusLabel(
                    7,
                    false,
                    true,
                    false,
                    false,
                    WorldDiePhase.Settled,
                    7),
                Is.EqualTo("DICE  7"),
                "The settled result remains visible after Action exits.");
            Assert.That(
                WorldDieHudPresentationPolicy.ResolveDiceStatusLabel(
                    0,
                    true,
                    true,
                    false,
                    false,
                    WorldDiePhase.Hidden,
                    0),
                Is.EqualTo("DICE  --"));
        }

        [Test]
        public void LandingRotation_AlignsEveryD12FaceFromArbitraryRotations()
        {
            var normals = new Vector3[WorldDieD12Layout.FaceCount];
            var values = new int[WorldDieD12Layout.FaceCount];
            for (var index = 0; index < normals.Length; index++)
            {
                values[index] = index + WorldDieAuthorityModel.MinimumFace;
                Assert.That(
                    WorldDieD12Layout.TryGetLocalNormal(
                        values[index],
                        out normals[index]),
                    Is.True);
            }

            var startRotations = new[]
            {
                Quaternion.identity,
                Quaternion.Euler(17f, 83f, 241f),
                Quaternion.Euler(311f, 129f, 44f)
            };
            var tileUps = new[]
            {
                Vector3.up,
                new Vector3(0.2f, 0.95f, -0.23f).normalized
            };

            for (var faceIndex = 0; faceIndex < normals.Length; faceIndex++)
            {
                for (var rotationIndex = 0;
                     rotationIndex < startRotations.Length;
                     rotationIndex++)
                {
                    for (var upIndex = 0; upIndex < tileUps.Length; upIndex++)
                    {
                        var tileUp = tileUps[upIndex];
                        var landingRotation =
                            WorldDieRollPresentationPolicy.ResolveLandingRotation(
                                startRotations[rotationIndex],
                                normals[faceIndex],
                                tileUp);
                        Assert.That(
                            Vector3.Dot(
                                landingRotation * normals[faceIndex],
                                tileUp),
                            Is.GreaterThan(0.99999f),
                            "Face " + values[faceIndex] +
                            " must align with the tile up vector.");

                        var inWorldUpFrame =
                            Quaternion.FromToRotation(tileUp, Vector3.up) *
                            landingRotation;
                        Assert.That(
                            WorldDieFaceResolver.ResolveHighestFace(
                                inWorldUpFrame,
                                normals,
                                values),
                            Is.EqualTo(values[faceIndex]));
                    }
                }
            }
        }

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
        public void SettledFace_AcceptsTwelveRejectsThirteenAndPreventsSecondPush()
        {
            var model = CreateRollingModel();

            Assert.That(
                model.MarkSettled(WorldDieAuthorityModel.MaximumFace),
                Is.True);
            Assert.That(
                model.SettledFace,
                Is.EqualTo(WorldDieAuthorityModel.MaximumFace));
            Assert.That(model.Phase, Is.EqualTo(WorldDiePhase.Settled));
            Assert.That(
                model.TryBeginRoll(Context(1), 20d, out var reason),
                Is.False);
            Assert.That(reason, Is.EqualTo(WorldDiePushRejectReason.NotReady));

            var aboveMaximum = CreateRollingModel();
            Assert.That(
                aboveMaximum.MarkSettled(
                    WorldDieAuthorityModel.MaximumFace + 1),
                Is.False);
            Assert.That(
                aboveMaximum.Phase,
                Is.EqualTo(WorldDiePhase.Rolling));
        }

        [Test]
        public void ResultPresentation_HidesAtExactOneSecondBoundary()
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
                WorldDieResultPresentationPolicy
                    .ShouldPreserveAcrossActionExit(WorldDiePhase.Settled),
                Is.True);
            Assert.That(
                WorldDieResultPresentationPolicy
                    .ShouldPreserveAcrossActionExit(WorldDiePhase.Rolling),
                Is.False);
            Assert.That(
                WorldDieResultPresentationPolicy
                    .ShouldPreserveAcrossActionExit(WorldDiePhase.Ready),
                Is.False);
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
            var values = new[]
            {
                WorldDieAuthorityModel.MaximumFace,
                4,
                WorldDieAuthorityModel.MinimumFace
            };

            Assert.That(
                WorldDieFaceResolver.ResolveHighestFace(
                    Quaternion.identity,
                    normals,
                    values),
                Is.EqualTo(WorldDieAuthorityModel.MaximumFace));
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

        private static void AssertProjectedExtentContainsEveryRotatedCorner(
            Bounds bounds,
            Vector3 dieCenter,
            Quaternion sourceRotation,
            Quaternion targetRotation,
            Vector3 axis)
        {
            var extent =
                WorldDieRollPresentationPolicy.GetTargetRotationProjectedExtent(
                    bounds,
                    dieCenter,
                    sourceRotation,
                    targetRotation,
                    axis);
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
                            Mathf.Abs(Vector3.Dot(rotatedOffset, normalizedAxis)),
                            Is.LessThanOrEqualTo(extent + 0.00001f));
                    }
                }
            }
        }
    }
}

using System;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class FootstepRulesTests
    {
        [Test]
        public void ConfirmedPrototypeValues_AreCentralized()
        {
            Assert.That(FootstepRules.WalkSpeedMultiplier, Is.EqualTo(0.55f));
            Assert.That(FootstepRules.StandardAudibleRadius, Is.EqualTo(24f));
            Assert.That(FootstepRules.QuietWalkAudibleRadius, Is.EqualTo(6f));
            Assert.That(FootstepRules.StandardStepDistance, Is.EqualTo(1.8f));
            Assert.That(FootstepRules.QuietWalkStepDistance, Is.EqualTo(1.35f));
        }

        [Test]
        public void StandardCadence_EmitsAfterActualTravelDistance()
        {
            var cadence = new FootstepCadenceTracker();

            Assert.That(cadence.RecordMovement(1.7f, false), Is.Zero);
            Assert.That(cadence.RecordMovement(0.11f, false), Is.EqualTo(1));
            Assert.That(cadence.DistanceSinceStep, Is.EqualTo(0.01f).Within(0.001f));
        }

        [Test]
        public void QuietCadence_UsesShorterStride()
        {
            var cadence = new FootstepCadenceTracker();

            Assert.That(cadence.RecordMovement(1.34f, true), Is.Zero);
            Assert.That(cadence.RecordMovement(0.02f, true), Is.EqualTo(1));
        }

        [Test]
        public void SwitchingLocomotionMode_StartsFreshStride()
        {
            var cadence = new FootstepCadenceTracker();

            Assert.That(cadence.RecordMovement(1.7f, false), Is.Zero);
            Assert.That(cadence.RecordMovement(0.1f, true), Is.Zero);
            Assert.That(cadence.DistanceSinceStep, Is.EqualTo(0.1f).Within(0.001f));
        }

        [Test]
        public void LongTravel_EmitsEveryCompletedStrideAndKeepsRemainder()
        {
            var cadence = new FootstepCadenceTracker();

            Assert.That(cadence.RecordMovement(3.7f, false), Is.EqualTo(2));
            Assert.That(cadence.DistanceSinceStep, Is.EqualTo(0.1f).Within(0.001f));
        }

        [Test]
        public void InvalidDistance_IsRejected()
        {
            var cadence = new FootstepCadenceTracker();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => cadence.RecordMovement(float.NaN, false));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => cadence.RecordMovement(float.PositiveInfinity, true));
        }

        [Test]
        public void EmptyClipList_StillPublishesPresentationWithoutPlayingAudio()
        {
            var gameObject = new GameObject("Footstep Emitter Test");
            try
            {
                var emitter = gameObject.AddComponent<FootstepAudioEmitter>();
                FootstepPresentation presentation = default;
                emitter.FootstepPresented += value => presentation = value;

                Assert.DoesNotThrow(
                    () => emitter.PresentFootstep(Vector3.one, true));
                Assert.That(emitter.PresentedCount, Is.EqualTo(1));
                Assert.That(emitter.LastAudibleRadius, Is.EqualTo(6f));
                Assert.That(emitter.LastWasQuietWalk, Is.True);
                Assert.That(presentation.WorldPosition, Is.EqualTo(Vector3.one));
                Assert.That(presentation.AudibleRadius, Is.EqualTo(6f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
    }
}

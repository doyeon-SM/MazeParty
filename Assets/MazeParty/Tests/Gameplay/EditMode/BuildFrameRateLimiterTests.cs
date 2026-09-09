using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BuildFrameRateLimiterTests
    {
        private int _previousTarget;
        private int _previousVSync;

        [SetUp]
        public void SetUp()
        {
            _previousTarget = Application.targetFrameRate;
            _previousVSync = QualitySettings.vSyncCount;
        }

        [TearDown]
        public void TearDown()
        {
            Application.targetFrameRate = _previousTarget;
            QualitySettings.vSyncCount = _previousVSync;
        }

        [Test]
        public void Apply_DisablesVSyncAndCapsAtSixtyFrames()
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;

            BuildFrameRateLimiter.Apply();

            Assert.That(QualitySettings.vSyncCount, Is.Zero);
            Assert.That(
                Application.targetFrameRate,
                Is.EqualTo(BuildFrameRateLimiter.TargetFrameRate));
            Assert.That(BuildFrameRateLimiter.TargetFrameRate, Is.EqualTo(60));
        }
    }
}

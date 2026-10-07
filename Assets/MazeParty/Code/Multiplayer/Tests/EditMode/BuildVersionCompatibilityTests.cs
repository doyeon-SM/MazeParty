using System;
using System.Text;
using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BuildVersionCompatibilityTests
    {
        [Test]
        public void ConnectionPayload_AcceptsOnlyExactVersionedPayload()
        {
            const string expected = "0.52";

            Assert.That(
                BuildVersionCompatibility.IsCompatiblePayload(
                    BuildVersionCompatibility.CreateConnectionPayload(expected),
                    expected),
                Is.True);
            Assert.That(
                BuildVersionCompatibility.IsCompatiblePayload(
                    BuildVersionCompatibility.CreateConnectionPayload("0.53"),
                    expected),
                Is.False);
            Assert.That(
                BuildVersionCompatibility.IsCompatiblePayload(
                    BuildVersionCompatibility.CreateConnectionPayload("0.52.0"),
                    expected),
                Is.False);
            Assert.That(
                BuildVersionCompatibility.IsCompatiblePayload(
                    BuildVersionCompatibility.CreateConnectionPayload(" 0.52 "),
                    expected),
                Is.False,
                "Connection compatibility must use an exact ordinal version.");
            Assert.That(
                BuildVersionCompatibility.IsCompatiblePayload(
                    Array.Empty<byte>(),
                    expected),
                Is.False);
            Assert.That(
                BuildVersionCompatibility.IsCompatiblePayload(
                    Encoding.UTF8.GetBytes(expected),
                    expected),
                Is.False,
                "Unversioned legacy connection data must not bypass approval.");
            Assert.That(
                BuildVersionCompatibility.IsCompatiblePayload(
                    new byte[] { 0xff },
                    expected),
                Is.False);
        }

        [Test]
        public void VersionPresentationAndRejectionReason_AreStable()
        {
            Assert.That(
                BuildVersionCompatibility.FormatDisplayText("0.52"),
                Is.EqualTo("v0.52"));
            Assert.That(
                BuildVersionCompatibility.FormatDisplayText("v0.52"),
                Is.EqualTo("v0.52"));
            Assert.That(
                BuildVersionCompatibility.IsMismatchDisconnectReason(
                    "Connection denied: " +
                    BuildVersionCompatibility.RejectionReason),
                Is.True);
            Assert.That(
                BuildVersionCompatibility.IsMismatchDisconnectReason(
                    "Relay timed out."),
                Is.False);
        }

        [Test]
        public void RejectionState_IsAttemptScopedAndConsumesPendingClientsOnce()
        {
            var state = new BuildVersionRejectionState();

            state.RecordServerRejectedClient(42);
            Assert.That(state.ConsumeServerRejectedClient(42), Is.True);
            Assert.That(state.ConsumeServerRejectedClient(42), Is.False);

            state.BeginLocalAttempt();
            state.ObserveLocalDisconnectReason(
                "Connection denied: " +
                BuildVersionCompatibility.RejectionReason);
            Assert.That(state.ConsumeLocalAttemptRejection(), Is.True);
            Assert.That(state.ConsumeLocalAttemptRejection(), Is.False);

            state.ObserveLocalDisconnectReason(
                BuildVersionCompatibility.RejectionReason);
            state.BeginLocalAttempt();
            Assert.That(
                state.ConsumeLocalAttemptRejection(),
                Is.False,
                "A previous rejected attempt must not relabel the next failure.");
        }
    }
}

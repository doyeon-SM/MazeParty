using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class PresentationEventRevisionGateTests
    {
        [Test]
        public void Observe_BaselinesSnapshotAndOnlyPlaysFutureRevisions()
        {
            var gate = new PresentationEventRevisionGate();

            Assert.That(gate.Observe(12U), Is.False,
                "The replicated snapshot must not replay a historical event.");
            Assert.That(gate.HasBaseline, Is.True);
            Assert.That(gate.LastRevision, Is.EqualTo(12U));
            Assert.That(gate.Observe(12U), Is.False,
                "An unchanged snapshot must remain silent.");
            Assert.That(gate.Observe(13U), Is.True,
                "A later replicated event must play once.");
            Assert.That(gate.Observe(13U), Is.False,
                "The same event must not play twice.");
            Assert.That(gate.Observe(0U), Is.False,
                "A round reset is state restoration, not an event.");
            Assert.That(gate.Observe(1U), Is.True,
                "The first event after a replicated round reset must play.");

            gate.Reset();

            Assert.That(gate.HasBaseline, Is.False);
            Assert.That(gate.Observe(21U), Is.False,
                "Re-enabling a scene must baseline its current snapshot.");
        }
    }
}

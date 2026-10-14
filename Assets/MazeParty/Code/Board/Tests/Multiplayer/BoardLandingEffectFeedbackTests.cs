using NUnit.Framework;
using MazeParty.Gameplay;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardLandingEffectFeedbackTests
    {
        [Test]
        public void EventFocus_VisitsEveryAffectedPlayerAtExactBoundaries()
        {
            var snapshot = new BoardLandingEffectFeedbackSnapshot
            {
                Active = true,
                Revision = 1,
                Slot = 2,
                Kind = (byte)BoardLandingEffectFeedbackKind.Event,
                AffectedMask = 0b1111,
                StartedAt = 10d
            };

            Assert.That(BoardLandingEffectFeedbackRules.GetEventFocusSlot(
                snapshot, 10d), Is.EqualTo(0));
            Assert.That(BoardLandingEffectFeedbackRules.GetEventFocusSlot(
                snapshot, 10.75d), Is.EqualTo(1));
            Assert.That(BoardLandingEffectFeedbackRules.GetEventFocusSlot(
                snapshot, 11.5d), Is.EqualTo(2));
            Assert.That(BoardLandingEffectFeedbackRules.GetEventFocusSlot(
                snapshot, 12.25d), Is.EqualTo(3));
            Assert.That(BoardLandingEffectFeedbackRules.GetEventFocusSlot(
                snapshot, 13d), Is.EqualTo(-1));

            snapshot.AffectedMask = 0b1010;
            Assert.That(BoardLandingEffectFeedbackRules.GetEventFocusSlot(
                snapshot, 10d), Is.EqualTo(1));
            Assert.That(BoardLandingEffectFeedbackRules.GetEventFocusSlot(
                snapshot, 11.5d), Is.EqualTo(3));
        }

        [Test]
        public void LandingMessage_ExposesActorSlotForSharedFrontCamera()
        {
            var message = LandingEffectMessage.Encode(
                3, true, 1, 2, BoardLandingEffectType.GoldGain,
                "GOLD GAIN {0} GOLD", "+10");
            Assert.That(LandingEffectMessage.GetSlot(message), Is.EqualTo(3));
            Assert.That(LandingEffectMessage.GetSlot("invalid"), Is.EqualTo(-1));
        }
    }
}

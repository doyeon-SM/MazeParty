using NUnit.Framework;
using MazeParty.Gameplay;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardLandingEffectFeedbackTests
    {
        [Test]
        public void EventTokens_KeepAllTargetsAndResourcesInsideTheNetworkPayload()
        {
            var names = new[] { "도연", "Alex", "ユウ", "小明" };
            for (var mask = 1; mask < 16; mask++)
            {
                foreach (var resource in new[] { BoardSpecialEventResource.Gold, BoardSpecialEventResource.Key })
                {
                    var payload = LandingEffectMessage.Encode(0, true, -100, 100,
                        BoardLandingEffectType.SpecialEvent,
                        "EVENT RESULT: {0} RECEIVES UP TO {1}",
                        LandingEffectMessage.PlayersToken(mask),
                        LandingEffectMessage.ResourceToken(resource, 30));
                    Assert.That(System.Text.Encoding.UTF8.GetByteCount(payload), Is.LessThan(125));
                    var formatted = LandingEffectMessage.FormatEventPopup(payload,
                        slot => "<color=#00FF00>" + names[slot] + "</color>");
                    for (var slot = 0; slot < 4; slot++)
                        if ((mask & (1 << slot)) != 0) Assert.That(formatted, Does.Contain(names[slot]));
                    Assert.That(formatted, Does.Contain(resource == BoardSpecialEventResource.Gold
                        ? "<sprite name=\"gold\"" : "<sprite name=\"key\""));
                    Assert.That(formatted, Does.Not.Contain("@m"));
                    Assert.That(formatted, Does.Not.Contain("@p"));
                }
            }
        }

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

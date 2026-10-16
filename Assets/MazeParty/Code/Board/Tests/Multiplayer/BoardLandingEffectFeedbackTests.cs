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
        public void LandingFeedback_PausePreservesRemainingWindowWithoutReplayingTheRevision()
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var warning = typeof(Unity.Netcode.NetworkVariableBase).GetField("IgnoreInitializeWarning",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var previousWarning = (bool)warning.GetValue(null);
            var previousMatch = NetworkMatchState.Instance;
            var root = new UnityEngine.GameObject("Landing pause fixture", typeof(Unity.Netcode.NetworkObject));
            try
            {
                warning.SetValue(null, true);
                var match = root.AddComponent<NetworkMatchState>();
                typeof(Unity.Netcode.NetworkBehaviour).GetProperty("IsServer").SetValue(match, true);
                var feedback = (Unity.Netcode.NetworkVariable<BoardLandingEffectFeedbackSnapshot>)
                    typeof(NetworkMatchState).GetField("_landingEffectFeedback", flags).GetValue(match);
                foreach (var kind in new[] { BoardLandingEffectFeedbackKind.Gold,
                    BoardLandingEffectFeedbackKind.Item, BoardLandingEffectFeedbackKind.Health,
                    BoardLandingEffectFeedbackKind.Event })
                {
                    feedback.Value = new BoardLandingEffectFeedbackSnapshot {
                        Active = true, Revision = 7, Slot = 0, Kind = (byte)kind, StartedAt = 0d };
                    typeof(NetworkMatchState).GetMethod("PauseResourceTransferPresentationOnServer", flags)
                        .Invoke(match, new object[] { 0.5d });
                    Assert.That(match.LandingFeedbackPresentationNow, Is.EqualTo(0.5d));
                    var remaining = BoardLandingEffectFeedbackRules.GetRemainingSeconds(
                        match.LandingEffectFeedback, match.LandingFeedbackPresentationNow);
                    typeof(NetworkMatchState).GetMethod("ResumeResourceTransferPresentationOnServer", flags)
                        .Invoke(match, new object[] { 100.5d });
                    Assert.That(match.LandingEffectFeedback.Revision, Is.EqualTo(7));
                    Assert.That(match.LandingEffectFeedback.StartedAt, Is.EqualTo(100d));
                    Assert.That(BoardLandingEffectFeedbackRules.GetRemainingSeconds(
                        match.LandingEffectFeedback, 100.5d), Is.EqualTo(remaining));
                    var end = 100d + BoardLandingEffectFeedbackRules.GetDurationSeconds(kind);
                    Assert.That(BoardLandingEffectFeedbackRules.IsVisible(
                        match.LandingEffectFeedback, end - 0.000001d), Is.True);
                    Assert.That(BoardLandingEffectFeedbackRules.IsVisible(
                        match.LandingEffectFeedback, end), Is.False);
                }
            }
            finally
            {
                typeof(NetworkMatchState).GetProperty(nameof(NetworkMatchState.Instance)).SetValue(null, previousMatch);
                UnityEngine.Object.DestroyImmediate(root);
                warning.SetValue(null, previousWarning);
            }
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

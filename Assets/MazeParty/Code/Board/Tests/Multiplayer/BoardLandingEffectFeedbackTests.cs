using NUnit.Framework;
using MazeParty.Gameplay;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardLandingEffectFeedbackTests
    {
        [Test]
        public void ForestGhostFeedback_UsesAppliedAmountAndPauseAwareWindowForIntroAndBothTransferLegs()
        {
            var snapshot = new BoardForestGhostSnapshot { Active = true, IsIntro = true, Slot = -1, StartedAt = 10d };
            Assert.That(BoardLandingEffectFeedbackRules.TryGetForestGhostContent(snapshot, 10d,
                out _, out var intro, out var remaining), Is.True);
            Assert.That(intro, Is.EqualTo(GameText.T("LET THE MISCHIEF BEGIN!")));
            Assert.That(remaining, Is.EqualTo(3f));
            Assert.That(BoardLandingEffectFeedbackRules.TryGetForestGhostContent(snapshot, 13d,
                out _, out _, out _), Is.False);
            snapshot.IsIntro = false; snapshot.Slot = 1;
            foreach (var resource in new[] { ForestGhostResource.Gold, ForestGhostResource.Key, ForestGhostResource.Health })
            foreach (var actual in new[] { -10, 0, 5 })
            foreach (var delivery in new[] { false, true })
            {
                snapshot.Resource = (byte)resource; snapshot.IsDelivery = delivery;
                snapshot.Amount = 50; snapshot.ActualAmount = actual; snapshot.Applied = false;
                Assert.That(BoardLandingEffectFeedbackRules.TryGetForestGhostContent(snapshot, 11.25d,
                    out _, out _, out _), Is.False);
                snapshot.Applied = true;
                Assert.That(BoardLandingEffectFeedbackRules.TryGetForestGhostContent(snapshot, 11.25d,
                    out _, out var label, out remaining), Is.True);
                Assert.That(label, Is.EqualTo(actual > 0 ? "+5" : actual == 0 ? "0" : "-10"));
                Assert.That(remaining, Is.EqualTo(1.75f));
                snapshot.Pause(11.25d);
                Assert.That(BoardLandingEffectFeedbackRules.TryGetForestGhostContent(snapshot, 100d,
                    out _, out _, out remaining), Is.True);
                Assert.That(remaining, Is.EqualTo(1.75f));
                snapshot.Resume(11.25d);
                snapshot.Slot = -1;
                Assert.That(BoardLandingEffectFeedbackRules.TryGetForestGhostContent(snapshot, 11.25d,
                    out _, out _, out _), Is.False);
                snapshot.Slot = 1;
            }
        }

        [Test]
        public void DamageImpact_UsesSharedSeedAndPlaysOnceInsideTheUnpausedDamageWindow()
        {
            var snapshot = new BoardLandingEffectFeedbackSnapshot { Active = true, Revision = 4,
                Slot = 2, Kind = (byte)BoardLandingEffectFeedbackKind.Health, SignedAmount = -20, StartedAt = 10d };
            var choices = new System.Collections.Generic.HashSet<int>();
            for (var seed = 1; seed <= 90; seed++)
            {
                var first = new BoardDamageImpactPlayback();
                var second = new BoardDamageImpactPlayback();
                Assert.That(first.TryTake(snapshot, BoardLandingEffectType.Damage20,
                    BoardFlowState.LandingEffectResolve, false, seed, 10.499999d, out _), Is.False);
                Assert.That(first.TryTake(snapshot, BoardLandingEffectType.Damage20,
                    BoardFlowState.LandingEffectResolve, true, seed, 10.5d, out _), Is.False);
                Assert.That(first.TryTake(snapshot, BoardLandingEffectType.Damage20,
                    BoardFlowState.LandingEffectResolve, false, seed, 10.5d, out var selected), Is.True);
                Assert.That(second.TryTake(snapshot, BoardLandingEffectType.Damage20,
                    BoardFlowState.LandingEffectResolve, false, seed, 10.7d, out var other), Is.True);
                Assert.That(selected, Is.EqualTo(other)); choices.Add(selected);
                Assert.That(first.TryTake(snapshot, BoardLandingEffectType.Damage20,
                    BoardFlowState.LandingEffectResolve, false, seed, 11d, out _), Is.False);
                first.Reset();
                Assert.That(first.TryTake(snapshot, BoardLandingEffectType.Damage20,
                    BoardFlowState.LandingEffectResolve, false, seed, 11.5d, out _), Is.False);
            }
            Assert.That(choices, Is.EquivalentTo(new[] { 0, 1, 2 }));
            var playback = new BoardDamageImpactPlayback();
            foreach (var effect in new[] { BoardLandingEffectType.Healing10, BoardLandingEffectType.Healing20,
                BoardLandingEffectType.GoldLoss, BoardLandingEffectType.SpecialEvent })
                Assert.That(playback.TryTake(snapshot, effect, BoardFlowState.LandingEffectResolve,
                    false, 1, 10.5d, out _), Is.False);
            Assert.That(playback.TryTake(snapshot, BoardLandingEffectType.Damage40,
                BoardFlowState.Action, false, 1, 10.5d, out _), Is.False);
            Assert.That(playback.TryTake(snapshot, BoardLandingEffectType.Damage40,
                BoardFlowState.LandingEffectResolve, false, 1, 10.5d, out _), Is.True);
            snapshot.Revision++; snapshot.StartedAt = 20d;
            Assert.That(playback.TryTake(snapshot, BoardLandingEffectType.Damage40,
                BoardFlowState.LandingEffectResolve, false, 1, 20.5d, out _), Is.True);
        }

        [Test]
        public void EventTokens_KeepCompactGroupsAndResourcesInsideTheNetworkPayload()
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
                    var count = 0;
                    for (var slot = 0; slot < 4; slot++) if ((mask & (1 << slot)) != 0) count++;
                    if (count >= 3)
                    {
                        Assert.That(formatted, Does.Contain("<color=#FFFFFF>"));
                        Assert.That(formatted, Does.Contain(GameText.T(mask == 15 ? "EVERYONE" : "EVERYONE EXCEPT ME")));
                        var group = LandingEffectMessage.FormatArgument(
                            LandingEffectMessage.PlayersToken(mask), slot => names[slot]);
                        foreach (var name in names) Assert.That(group, Does.Not.Contain(name));
                    }
                    else for (var slot = 0; slot < 4; slot++)
                        if ((mask & (1 << slot)) != 0) Assert.That(formatted, Does.Contain(names[slot]));
                    Assert.That(formatted, Does.Contain(resource == BoardSpecialEventResource.Gold
                        ? "<sprite name=\"gold\"" : "<sprite name=\"key\""));
                    Assert.That(formatted, Does.Not.Contain("@m"));
                    Assert.That(formatted, Does.Not.Contain("@p"));
                }
            }
        }

        [Test]
        public void EventFocus_ShowsAllTargetsTogetherAfterResultAtExactBoundaries()
        {
            for (var mask = 1; mask < 16; mask++)
            {
                var snapshot = new BoardLandingEffectFeedbackSnapshot
                {
                    Active = true, Revision = 1, Slot = 2,
                    Kind = (byte)BoardLandingEffectFeedbackKind.Event,
                    AffectedMask = (byte)mask, StartedAt = 10d
                };
                Assert.That(BoardLandingEffectFeedbackRules.GetEventFocusMask(snapshot, 12.999d), Is.Zero);
                Assert.That(BoardLandingEffectFeedbackRules.GetEventFocusMask(snapshot, 13d), Is.EqualTo(mask));
                Assert.That(BoardLandingEffectFeedbackRules.IsEffectVisible(snapshot, 13.499d), Is.False);
                Assert.That(BoardLandingEffectFeedbackRules.IsEffectVisible(snapshot, 13.5d), Is.True);
                Assert.That(BoardLandingEffectFeedbackRules.IsEffectVisible(snapshot, 14.499d), Is.True);
                Assert.That(BoardLandingEffectFeedbackRules.IsEffectVisible(snapshot, 14.5d), Is.False);
                Assert.That(BoardLandingEffectFeedbackRules.GetEventFocusMask(snapshot, 14.999d), Is.EqualTo(mask));
                Assert.That(BoardLandingEffectFeedbackRules.GetEventFocusMask(snapshot, 15d), Is.Zero);
                Assert.That(BoardLandingEffectFeedbackRules.GetDurationSeconds(snapshot), Is.EqualTo(5d));
            }
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
                    BoardLandingEffectFeedbackKind.Event, BoardLandingEffectFeedbackKind.EventIntro })
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
        public void HealthTrail_InterruptsFromCurrentValueAndRestartsItsOneSecondWindow()
        {
            var state = new BoardHudHealthAnimation();
            state.Reset(1f);
            state.Change(100, 70, 100, 10d);
            Assert.That(state.Trail(10d), Is.EqualTo(1f));
            Assert.That(state.Trail(10.5d), Is.EqualTo(0.85f).Within(0.0001f));
            state.Change(70, 90, 100, 10.5d);
            Assert.That(state.Trail(10.5d), Is.EqualTo(0.85f).Within(0.0001f));
            Assert.That(state.AccumulatedAmount, Is.EqualTo(50));
            Assert.That(state.IsActive(11.49999d), Is.True);
            Assert.That(state.IsActive(11.5d), Is.False);
            Assert.That(state.Trail(11.5d), Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(state.Scale(11.5d), Is.EqualTo(1f));
            state.Change(90, 80, 100, 11.5d);
            Assert.That(state.AccumulatedAmount, Is.EqualTo(10), "Exact expiry starts a new pulse.");
            state.Change(80, 60, 100, 11.75d);
            Assert.That(state.AccumulatedAmount, Is.EqualTo(30));
            Assert.That(state.Trail(12.75d), Is.EqualTo(0.6f).Within(0.0001f));
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

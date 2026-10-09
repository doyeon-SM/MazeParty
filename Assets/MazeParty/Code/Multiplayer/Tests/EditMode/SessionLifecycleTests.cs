using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class SessionLifecycleTests
    {
        [TestCase(true, MultiplayerConstants.LobbyPhase, true)]
        [TestCase(false, MultiplayerConstants.LobbyPhase, false)]
        [TestCase(true, MultiplayerConstants.PlayingPhase, false)]
        public void LobbyWorldIdentity_RequiresVisibleLobbyPresentation(
            bool presentationVisible,
            string phase,
            bool expected)
        {
            var snapshot = new SessionSnapshot(
                "ABCD",
                true,
                phase,
                "host",
                Array.Empty<OnlinePlayerSnapshot>());

            Assert.That(
                OnlineSessionController.ShouldShowLobbyWorldIdentity(
                    snapshot,
                    presentationVisible),
                Is.EqualTo(expected));
        }

        [Test]
        public void LobbyWorldIdentity_MapsByAssignedSlotAndClearsUnmatchedState()
        {
            var snapshot = new SessionSnapshot(
                "ABCD",
                true,
                MultiplayerConstants.LobbyPhase,
                "host",
                new[]
                {
                    new OnlinePlayerSnapshot(
                        "guest",
                        "Guest",
                        2,
                        true,
                        false),
                    new OnlinePlayerSnapshot(
                        "host",
                        "Host",
                        0,
                        false,
                        true)
                });

            Assert.That(
                OnlineSessionController.TryResolveLobbyWorldIdentity(
                    snapshot,
                    true,
                    2,
                    out var ready,
                    out var host),
                Is.True);
            Assert.That(ready, Is.True);
            Assert.That(host, Is.False);

            Assert.That(
                OnlineSessionController.TryResolveLobbyWorldIdentity(
                    snapshot,
                    true,
                    1,
                    out ready,
                    out host),
                Is.False);
            Assert.That(ready, Is.False);
            Assert.That(host, Is.False);

            Assert.That(
                OnlineSessionController.TryResolveLobbyWorldIdentity(
                    snapshot,
                    true,
                    -1,
                    out ready,
                    out host),
                Is.False);
            Assert.That(ready, Is.False);
            Assert.That(host, Is.False);

            Assert.That(
                OnlineSessionController.TryResolveLobbyWorldIdentity(
                    snapshot,
                    false,
                    0,
                    out ready,
                    out host),
                Is.False);
            Assert.That(ready, Is.False);
            Assert.That(host, Is.False);
        }

        [Test]
        public void Rules_AllowExpectedStableAndTerminalTransitions()
        {
            Assert.That(
                SessionLifecycleRules.CanTransition(
                    SessionLifecycleState.Offline,
                    SessionLifecycleState.Connecting),
                Is.True);
            Assert.That(
                SessionLifecycleRules.CanTransition(
                    SessionLifecycleState.Connecting,
                    SessionLifecycleState.Lobby),
                Is.True);
            Assert.That(
                SessionLifecycleRules.CanTransition(
                    SessionLifecycleState.Lobby,
                    SessionLifecycleState.StartingMatch),
                Is.True);
            Assert.That(
                SessionLifecycleRules.CanTransition(
                    SessionLifecycleState.StartingMatch,
                    SessionLifecycleState.Playing),
                Is.True);
            Assert.That(
                SessionLifecycleRules.CanTransition(
                    SessionLifecycleState.Playing,
                    SessionLifecycleState.ReturningToLobby),
                Is.True);
            Assert.That(
                SessionLifecycleRules.CanTransition(
                    SessionLifecycleState.ReturningToLobby,
                    SessionLifecycleState.Lobby),
                Is.True);
            Assert.That(
                SessionLifecycleRules.CanTransition(
                    SessionLifecycleState.Playing,
                    SessionLifecycleState.Terminating),
                Is.True);
            Assert.That(
                SessionLifecycleRules.CanTransition(
                    SessionLifecycleState.Disposing,
                    SessionLifecycleState.Disposed),
                Is.True);
            Assert.That(
                SessionLifecycleRules.CanTransition(
                    SessionLifecycleState.Disposed,
                    SessionLifecycleState.Offline),
                Is.False);
        }

        [Test]
        public async Task Coordinator_RejectsConcurrentUserOperationAndRunsQueuedSystemWork()
        {
            var coordinator = new SessionOperationCoordinator();
            var release = new TaskCompletionSource<bool>();
            var order = new List<int>();
            var transitions = new List<string>();
            var observedIdle = false;
            coordinator.BecameIdle += () =>
                observedIdle = !coordinator.IsBusy;
            coordinator.StateChanged += (previous, current) =>
                transitions.Add(previous + ">" + current);

            Assert.That(
                coordinator.TryStart(
                    SessionLifecycleState.Connecting,
                    async _ =>
                    {
                        order.Add(1);
                        await release.Task;
                        order.Add(2);
                    }),
                Is.True);
            Assert.That(
                coordinator.TryStart(
                    SessionLifecycleState.Connecting,
                    _ => Task.CompletedTask),
                Is.False);
            Assert.That(
                coordinator.TryEnqueue(
                    SessionLifecycleState.Terminating,
                    _ =>
                    {
                        order.Add(3);
                        return Task.CompletedTask;
                    }),
                Is.True);

            release.SetResult(true);
            await coordinator.ActiveOperation;

            Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(coordinator.IsBusy, Is.False);
            Assert.That(observedIdle, Is.True);
            Assert.That(
                coordinator.State,
                Is.EqualTo(SessionLifecycleState.Terminating));
            await coordinator.BeginShutdown(null);
            Assert.That(
                transitions,
                Is.EqualTo(new[]
                {
                    "Offline>Connecting",
                    "Connecting>Terminating",
                    "Terminating>Disposing",
                    "Disposing>Disposed"
                }));
        }

        [Test]
        public async Task Coordinator_ShutdownCancelsWaitAndDisposesResourceOnce()
        {
            var coordinator = new SessionOperationCoordinator();
            var resource = new CountingDisposable();

            Assert.That(
                coordinator.TryStart(
                    SessionLifecycleState.Connecting,
                    token => Task.Delay(Timeout.Infinite, token)),
                Is.True);

            var first = coordinator.BeginShutdown(resource);
            var second = coordinator.BeginShutdown(resource);
            Assert.That(second, Is.SameAs(first));
            await first;

            Assert.That(resource.DisposeCount, Is.EqualTo(1));
            Assert.That(
                coordinator.State,
                Is.EqualTo(SessionLifecycleState.Disposed));
            Assert.That(coordinator.IsBusy, Is.False);
        }

        [Test]
        public async Task ApplicationQuitCoordinator_IsSingleFlightAndApprovesAfterCleanup()
        {
            var coordinator = new ApplicationQuitCoordinator();
            var cleanupRelease = new TaskCompletionSource<bool>();
            var deadline = new TaskCompletionSource<bool>();
            var cleanupCalls = 0;

            Assert.That(
                coordinator.TryBegin(
                    () =>
                    {
                        cleanupCalls++;
                        return cleanupRelease.Task;
                    },
                    deadline.Task),
                Is.True);
            Assert.That(
                coordinator.TryBegin(
                    () => Task.CompletedTask,
                    deadline.Task),
                Is.False);
            Assert.That(coordinator.IsApproved, Is.False);

            cleanupRelease.SetResult(true);
            var result = await coordinator.PreparationTask;

            Assert.That(
                result,
                Is.EqualTo(ApplicationQuitPreparationResult.Completed));
            Assert.That(coordinator.IsApproved, Is.True);
            Assert.That(cleanupCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task ApplicationQuitCoordinator_DeadlineStillApprovesQuit()
        {
            var coordinator = new ApplicationQuitCoordinator();
            var cleanup = new TaskCompletionSource<bool>();
            var deadline = new TaskCompletionSource<bool>();

            Assert.That(
                coordinator.TryBegin(() => cleanup.Task, deadline.Task),
                Is.True);
            deadline.SetResult(true);

            var result = await coordinator.PreparationTask;
            Assert.That(
                result,
                Is.EqualTo(ApplicationQuitPreparationResult.TimedOut));
            Assert.That(coordinator.IsApproved, Is.True);
        }

        [Test]
        public async Task ApplicationQuitCoordinator_FailureStillApprovesQuit()
        {
            var coordinator = new ApplicationQuitCoordinator();
            var expected = new InvalidOperationException("cleanup failed");

            Assert.That(
                coordinator.TryBegin(
                    () => Task.FromException(expected),
                    new TaskCompletionSource<bool>().Task),
                Is.True);

            var result = await coordinator.PreparationTask;
            Assert.That(
                result,
                Is.EqualTo(ApplicationQuitPreparationResult.Failed));
            Assert.That(coordinator.IsApproved, Is.True);
            Assert.That(coordinator.Failure, Is.SameAs(expected));
        }

        private sealed class CountingDisposable : IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}

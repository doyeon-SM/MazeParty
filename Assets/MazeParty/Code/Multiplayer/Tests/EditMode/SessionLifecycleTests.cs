using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class SessionLifecycleTests
    {
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

using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class SessionJoinAttemptTests
    {
        [Test]
        public async Task TimedOutJoin_ReleasesLobbyOperationWhileSdkCleanupContinues()
        {
            using (var coordinator = new SessionOperationCoordinator())
            {
                var guard = new SessionJoinAttempt<object>();
                var reply = new TaskCompletionSource<object>();
                var deadline = new TaskCompletionSource<bool>();
                Assert.That(coordinator.TryStart(SessionLifecycleState.Connecting,
                    async _ => await guard.RunAsync(() => reply.Task, result => Task.CompletedTask, deadline.Task)), Is.True);
                Assert.That(coordinator.IsBusy, Is.True);
                deadline.SetResult(true);
                await coordinator.ActiveOperation;
                Assert.That(coordinator.IsBusy, Is.False);
                Assert.That(coordinator.LastException, Is.TypeOf<TimeoutException>());
                Assert.That(guard.IsPending, Is.True);
                reply.SetResult(new object());
                await guard.Settlement;
            }
        }
        [Test]
        public async Task Timeout_ReleasesCaller_BlocksDuplicateJoin_AndDiscardsLateSuccessBeforeRetry()
        {
            var guard = new SessionJoinAttempt<object>();
            var reply = new TaskCompletionSource<object>();
            var deadline = new TaskCompletionSource<bool>();
            var cleanup = new TaskCompletionSource<bool>();
            var session = new object();
            object discarded = null;
            var attempt = guard.RunAsync(() => reply.Task, value => { discarded = value; return cleanup.Task; }, deadline.Task);
            deadline.SetResult(true);
            try { await attempt; Assert.Fail("Expected timeout"); } catch (TimeoutException) { }
            Assert.That(guard.IsPending, Is.True);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await guard.RunAsync(
                () => throw new AssertionException("Duplicate SDK join"), _ => Task.CompletedTask, Task.CompletedTask));
            reply.SetResult(session);
            Assert.That(discarded, Is.SameAs(session));
            Assert.That(guard.IsPending, Is.True);
            cleanup.SetResult(true);
            await guard.Settlement;
            Assert.That(guard.IsPending, Is.False);
            Assert.That(await guard.RunAsync(() => Task.FromResult(session), _ => throw new AssertionException("Unexpected cleanup"), Task.CompletedTask), Is.SameAs(session));
        }

        [Test]
        public async Task LateFailure_AllowsRetry_ButFailedCleanupKeepsOwnership()
        {
            foreach (var cleanupFails in new[] { false, true })
            {
                var guard = new SessionJoinAttempt<object>();
                var reply = new TaskCompletionSource<object>();
                Assert.ThrowsAsync<TimeoutException>(async () => await guard.RunAsync(() => reply.Task,
                    _ => Task.FromException(new InvalidOperationException("cleanup")), Task.CompletedTask));
                if (cleanupFails) reply.SetResult(new object());
                else reply.SetException(new InvalidOperationException("join"));
                await guard.Settlement;
                Assert.That(guard.IsPending, Is.EqualTo(cleanupFails));
                Assert.That(guard.CleanupError != null, Is.EqualTo(cleanupFails));
            }
        }
    }
}

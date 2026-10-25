using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class PrivacyAccountDeletionTests
    {
        [TestCase(false, true, "player", "profile")]
        [TestCase(true, false, "player", "profile")]
        [TestCase(true, true, "someone-else", "profile")]
        [TestCase(true, true, "player", "other-profile")]
        [TestCase(true, true, "", "profile")]
        public void RejectsActiveRoomUnauthenticatedOrStaleConfirmation(bool offline, bool authorized, string id, string profile)
        {
            var backend = new Backend { IsAuthorized = authorized };
            var holds = new Holds();
            var flow = new PrivacyAccountDeletion(backend, holds);
            Assert.Throws<InvalidOperationException>(() => flow.DeleteAsync(id, profile, () => offline).GetAwaiter().GetResult());
            Assert.That(backend.Calls, Is.Zero);
            Assert.That(holds.Read("profile"), Is.Zero);
            Assert.That(flow.IsBusy, Is.False);
        }
        [Test]
        public void SerializesDeletionAndStoresHoldBeforeNetworkRequest()
        {
            var holds = new Holds(); var pending = new TaskCompletionSource<bool>();
            var backend = new Backend { Completion = pending.Task };
            var flow = new PrivacyAccountDeletion(backend, holds);
            var first = flow.DeleteAsync("player", "profile", () => true);
            Assert.That(flow.IsBusy, Is.True);
            Assert.That(holds.Read("profile"), Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => flow.DeleteAsync("player", "profile", () => true).GetAwaiter().GetResult());
            pending.SetResult(true); first.GetAwaiter().GetResult();
            Assert.That(backend.Calls, Is.EqualTo(1));
            Assert.That(flow.Deleted, Is.True);
            Assert.That(flow.IsBusy, Is.False);
            Assert.That(flow.LastPlayerId, Is.EqualTo("player"));
            Assert.That(holds.Read("profile"), Is.EqualTo(2));
            Assert.That(holds.Read("other-profile"), Is.Zero);
            Assert.Throws<InvalidOperationException>(() => flow.DeleteAsync("player", "profile", () => true).GetAwaiter().GetResult());
        }
        [Test]
        public void UnknownNetworkOutcomeRetainsHoldAndAllowsAuthenticatedRetry()
        {
            var holds = new Holds();
            var backend = new Backend { Completion = Task.FromException(new TimeoutException()) };
            var flow = new PrivacyAccountDeletion(backend, holds);
            Assert.Throws<TimeoutException>(() => flow.DeleteAsync("player", "profile", () => true).GetAwaiter().GetResult());
            Assert.That(flow.IsBusy, Is.False); Assert.That(flow.Deleted, Is.False);
            Assert.That(holds.Read("profile"), Is.EqualTo(1));
            Assert.That(backend.IsAuthorized, Is.True, "No credential clearing after an uncertain result.");
            backend.Completion = Task.CompletedTask;
            flow.DeleteAsync("player", "profile", () => true).GetAwaiter().GetResult();
            Assert.That(backend.Calls, Is.EqualTo(2)); Assert.That(flow.Deleted, Is.True);
        }
        [Test]
        public void PersistenceFailurePreventsRemoteDeletion()
        {
            var backend = new Backend();
            var flow = new PrivacyAccountDeletion(backend, new Holds { FailWrites = true });
            Assert.Throws<InvalidOperationException>(() => flow.DeleteAsync("player", "profile", () => true).GetAwaiter().GetResult());
            Assert.That(backend.Calls, Is.Zero); Assert.That(flow.IsBusy, Is.False);
        }
        private sealed class Backend : IPrivacyAccountBackend
        {
            public bool IsAuthorized { get; set; } = true;
            public string PlayerId => "player";
            public string Profile => "profile";
            public int Calls;
            public Task Completion = Task.CompletedTask;
            public Task DeleteAsync() { Calls++; return Completion; }
        }
        private sealed class Holds : IPrivacyDeletionHoldStore
        {
            private readonly Dictionary<string, int> _states = new Dictionary<string, int>();
            public bool FailWrites;
            public int Read(string profile) => _states.TryGetValue(profile, out var state) ? state : 0;
            public void Write(string profile, int state)
            { if (FailWrites) throw new InvalidOperationException(); _states[profile] = state; }
            public void Clear(string profile) { _states.Remove(profile); }
        }
    }
}

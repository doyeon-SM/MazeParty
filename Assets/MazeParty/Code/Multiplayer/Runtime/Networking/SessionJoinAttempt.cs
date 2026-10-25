using System;
using System.Threading.Tasks;

namespace MazeParty.Multiplayer
{
    /// <summary>Bounds UI waiting without abandoning ownership of an uncancellable SDK join.</summary>
    public sealed class SessionJoinAttempt<T> where T : class
    {
        private bool _pending;
        public bool IsPending => _pending;
        public Exception CleanupError { get; private set; }
        public Task Settlement { get; private set; } = Task.CompletedTask;

        public async Task<T> RunAsync(Func<Task<T>> join, Func<T, Task> discard, Task deadline)
        {
            if (_pending) throw new InvalidOperationException("A previous join is still settling.");
            _pending = true;
            Task<T> request;
            try { request = join(); }
            catch { _pending = false; throw; }

            await Task.WhenAny(request, deadline);
            // Prefer a completed request at the deadline boundary.
            if (request.IsCompleted)
            {
                try { return await request; }
                finally { _pending = false; }
            }

            Settlement = DiscardLateResultAsync(request, discard);
            throw new TimeoutException("The connection attempt timed out.");
        }

        private async Task DiscardLateResultAsync(Task<T> request, Func<T, Task> discard)
        {
            T result;
            try { result = await request; }
            catch { _pending = false; return; } // Observe a late SDK failure, then permit retry.
            try
            {
                if (result != null) await discard(result);
                _pending = false;
            }
            catch (Exception exception)
            {
                // Do not permit a new join to race a session whose cleanup failed.
                CleanupError = exception;
            }
        }
    }
}

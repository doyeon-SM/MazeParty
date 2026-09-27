using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Owns the one serialized lifecycle-operation tail and the lifetime token.
    /// It contains no Unity or NGO state, so transition and shutdown behavior can
    /// be verified without starting a network session.
    /// </summary>
    public sealed class SessionOperationCoordinator : IDisposable
    {
        private readonly object _gate = new object();
        private readonly CancellationTokenSource _lifetimeSource =
            new CancellationTokenSource();
        private readonly CancellationToken _lifetimeToken;
        private Task _activeOperation = Task.CompletedTask;
        private Task _shutdownTask = Task.CompletedTask;
        private int _pendingOperationCount;
        private bool _shutdownStarted;
        private bool _disposed;
        private Exception _lastException;
        private SessionLifecycleState _state = SessionLifecycleState.Offline;

        public event Action BecameIdle;
        public event Action<SessionLifecycleState, SessionLifecycleState> StateChanged;

        public SessionOperationCoordinator()
        {
            _lifetimeToken = _lifetimeSource.Token;
        }

        public SessionLifecycleState State
        {
            get
            {
                lock (_gate)
                {
                    return _state;
                }
            }
        }

        public bool IsBusy
        {
            get
            {
                lock (_gate)
                {
                    return _pendingOperationCount > 0;
                }
            }
        }

        public Task ActiveOperation
        {
            get
            {
                lock (_gate)
                {
                    return _activeOperation;
                }
            }
        }

        public Task ShutdownTask
        {
            get
            {
                lock (_gate)
                {
                    return _shutdownTask;
                }
            }
        }

        public CancellationToken LifetimeToken => _lifetimeToken;

        public Exception LastException
        {
            get
            {
                lock (_gate)
                {
                    return _lastException;
                }
            }
        }

        /// <summary>Starts an exclusive user-facing lifecycle operation.</summary>
        public bool TryStart(
            SessionLifecycleState operationState,
            Func<CancellationToken, Task> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            SessionLifecycleState previousState;
            var startSignal = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                if (_shutdownStarted || !_activeOperation.IsCompleted ||
                    !SessionLifecycleRules.CanTransition(_state, operationState))
                {
                    return false;
                }

                previousState = _state;
                _state = operationState;
                _pendingOperationCount++;
                _activeOperation = RunOperationAsync(
                    operation,
                    _lifetimeToken,
                    startSignal.Task);
            }

            RaiseStateChanged(previousState, operationState);
            startSignal.TrySetResult(true);
            return true;
        }

        /// <summary>
        /// Queues a system lifecycle operation behind the current one. This is
        /// used by NGO/session callbacks which must not race a button operation.
        /// </summary>
        public bool TryEnqueue(
            SessionLifecycleState operationState,
            Func<CancellationToken, Task> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            lock (_gate)
            {
                if (_shutdownStarted)
                {
                    return false;
                }

                var predecessor = _activeOperation;
                _pendingOperationCount++;
                _activeOperation = RunQueuedOperationAsync(
                    predecessor,
                    operationState,
                    operation,
                    _lifetimeToken);
                return true;
            }
        }

        /// <summary>Queues maintenance work without changing lifecycle state.</summary>
        public bool TryEnqueue(Func<CancellationToken, Task> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            lock (_gate)
            {
                if (_shutdownStarted)
                {
                    return false;
                }

                var predecessor = _activeOperation;
                _pendingOperationCount++;
                _activeOperation = RunQueuedOperationAsync(
                    predecessor,
                    null,
                    operation,
                    _lifetimeToken);
                return true;
            }
        }

        public bool TryTransition(SessionLifecycleState next)
        {
            SessionLifecycleState previousState;
            lock (_gate)
            {
                if (_shutdownStarted &&
                    next != SessionLifecycleState.Disposing &&
                    next != SessionLifecycleState.Disposed)
                {
                    return false;
                }

                if (!SessionLifecycleRules.CanTransition(_state, next))
                {
                    return false;
                }

                previousState = _state;
                _state = next;
            }

            RaiseStateChanged(previousState, next);
            return true;
        }

        /// <summary>
        /// Cancels owned waits, waits for tracked work, then disposes the provider.
        /// Repeated calls return the same tracked shutdown task.
        /// </summary>
        public Task BeginShutdown(
            IDisposable resource,
            IEnumerable<Task> additionalTasks = null)
        {
            SessionLifecycleState previousState;
            Task shutdownTask;
            lock (_gate)
            {
                if (_shutdownStarted)
                {
                    return _shutdownTask;
                }

                _shutdownStarted = true;
                previousState = _state;
                _state = SessionLifecycleState.Disposing;
                _lifetimeSource.Cancel();

                var waits = new List<Task> { _activeOperation };
                if (additionalTasks != null)
                {
                    foreach (var task in additionalTasks)
                    {
                        if (task != null)
                        {
                            waits.Add(task);
                        }
                    }
                }

                _shutdownTask = ShutdownAsync(waits, resource);
                shutdownTask = _shutdownTask;
            }

            RaiseStateChanged(previousState, SessionLifecycleState.Disposing);
            return shutdownTask;
        }

        public void Dispose()
        {
            BeginShutdown(null);
        }

        private async Task RunQueuedOperationAsync(
            Task predecessor,
            SessionLifecycleState? operationState,
            Func<CancellationToken, Task> operation,
            CancellationToken token)
        {
            try
            {
                await predecessor;
                token.ThrowIfCancellationRequested();

                if (operationState.HasValue &&
                    !TryTransition(operationState.Value))
                {
                    return;
                }

                await operation(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Lifetime shutdown is the expected cancellation path.
            }
            catch (Exception exception)
            {
                RecordException(exception);
            }
            finally
            {
                CompleteOperation();
            }
        }

        private async Task RunOperationAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken token,
            Task startSignal)
        {
            try
            {
                await startSignal;
                token.ThrowIfCancellationRequested();
                await operation(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Lifetime shutdown is the expected cancellation path.
            }
            catch (Exception exception)
            {
                RecordException(exception);
            }
            finally
            {
                CompleteOperation();
            }
        }

        private async Task ShutdownAsync(
            IReadOnlyCollection<Task> waits,
            IDisposable resource)
        {
            // BeginShutdown publishes Disposing before this continuation may
            // publish Disposed, even when every tracked task is already done.
            await Task.Yield();
            try
            {
                await Task.WhenAll(waits);
            }
            catch (Exception exception)
            {
                RecordException(exception);
            }

            try
            {
                resource?.Dispose();
            }
            catch (Exception exception)
            {
                RecordException(exception);
            }
            finally
            {
                SessionLifecycleState previousState;
                lock (_gate)
                {
                    if (!_disposed)
                    {
                        _lifetimeSource.Dispose();
                        _disposed = true;
                    }

                    previousState = _state;
                    _state = SessionLifecycleState.Disposed;
                }

                RaiseStateChanged(previousState, SessionLifecycleState.Disposed);
            }
        }

        private void RaiseStateChanged(
            SessionLifecycleState previous,
            SessionLifecycleState current)
        {
            if (previous == current)
            {
                return;
            }

            try
            {
                StateChanged?.Invoke(previous, current);
            }
            catch (Exception exception)
            {
                RecordException(exception);
            }
        }

        private void RecordException(Exception exception)
        {
            lock (_gate)
            {
                _lastException = exception;
            }
        }

        private void CompleteOperation()
        {
            Action becameIdle = null;
            lock (_gate)
            {
                if (_pendingOperationCount > 0)
                {
                    _pendingOperationCount--;
                }

                if (_pendingOperationCount == 0)
                {
                    becameIdle = BecameIdle;
                }
            }

            if (becameIdle == null)
            {
                return;
            }

            try
            {
                becameIdle.Invoke();
            }
            catch (Exception exception)
            {
                RecordException(exception);
            }
        }
    }
}

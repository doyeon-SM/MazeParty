using System;
using System.Threading;
using System.Threading.Tasks;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
        private void RunAsync(
            Func<Task> operation,
            string progressSource,
            SessionLifecycleState operationState)
        {
            if (_destroyed)
            {
                return;
            }

            if (_sessionOperations.TryStart(
                    operationState,
                    cancellationToken => ExecuteSessionOperationAsync(
                        operation,
                        progressSource,
                        cancellationToken)))
            {
                // Render once more so the busy state is visible before the
                // provider operation reaches its first await.
                RenderLobby();
            }
        }

        private void OnSessionOperationsBecameIdle()
        {
            if (!_destroyed)
            {
                RenderLobby();
            }
        }

        private async Task ExecuteSessionOperationAsync(
            Func<Task> operation,
            string progressSource,
            CancellationToken cancellationToken)
        {
            SetLocalizedStatus(progressSource);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await operation();
                cancellationToken.ThrowIfCancellationRequested();
                if (_destroyed)
                {
                    return;
                }

                SynchronizeLifecycleStateFromSession(force: true);
                if (_sessions != null && _sessions.IsInSession)
                {
                    SetLocalizedStatus(
                        _sessions.Current.Phase == MultiplayerConstants.PlayingPhase
                            ? GameText.N("Connected to the online game.")
                            : GameText.N("Connected to the online lobby."));
                }
                else
                {
                    SetLocalizedStatus(
                        GameText.N("Online session disconnected."));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // OnDestroy owns cancellation; no destroyed UI may be updated.
            }
            catch (Exception exception)
            {
                if (_destroyed)
                {
                    return;
                }

                SynchronizeLifecycleStateFromSession(force: true);
                SetStatus(exception.Message);
                Debug.LogException(exception);
                GameSound.Play(SoundKeys.UiError);
            }
            finally
            {
                if (!_destroyed)
                {
                    RenderLobby();
                }
            }
        }

        private void SynchronizeLifecycleStateFromSession(bool force = false)
        {
            if (_destroyed ||
                LifecycleState == SessionLifecycleState.Disposing ||
                LifecycleState == SessionLifecycleState.Disposed)
            {
                return;
            }

            if (!force && IsBusy && SessionLifecycleRules.IsTransient(LifecycleState))
            {
                return;
            }

            SessionLifecycleState next;
            if (_completedMatchLobbyReturnInProgress)
            {
                next = SessionLifecycleState.ReturningToLobby;
            }
            else if (_sessions == null || !_sessions.IsInSession)
            {
                next = SessionLifecycleState.Offline;
            }
            else
            {
                next = _sessions.Current.Phase == MultiplayerConstants.PlayingPhase
                    ? SessionLifecycleState.Playing
                    : SessionLifecycleState.Lobby;
            }

            // While a remote termination is settling, do not project its stale
            // provider snapshot back to Lobby/Playing. The provider's final
            // Changed callback moves it to Offline.
            if (!force &&
                LifecycleState == SessionLifecycleState.Terminating &&
                next != SessionLifecycleState.Offline)
            {
                return;
            }

            _sessionOperations.TryTransition(next);
        }

        private static void TraceSessionTransition(
            SessionLifecycleState previous,
            SessionLifecycleState current)
        {
            if (previous == current)
            {
                return;
            }

            ServerEventTrace.Record(
                ServerEventCode.SessionTransition,
                state: (int)current,
                value0: (int)previous);
        }

        private void EndSessionAfterNetworkFailure(string reason)
        {
            if (_networkTerminationRequested ||
                _sessions == null ||
                !_sessions.IsInSession)
            {
                return;
            }

            _networkTerminationRequested = true;
            if (!_sessionOperations.TryEnqueue(
                    SessionLifecycleState.Terminating,
                    cancellationToken => EndSessionAfterNetworkFailureAsync(
                        reason,
                        cancellationToken)))
            {
                _networkTerminationRequested = false;
            }
        }

        private async Task EndSessionAfterNetworkFailureAsync(
            string reason,
            CancellationToken cancellationToken)
        {
            ClearPlayingReconnectTicket();
            SetStatus(reason);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // MPS owns NGO lifecycle. Never call NetworkManager.Shutdown directly.
                if (_sessions != null && _sessions.IsInSession)
                {
                    await _sessions.LeaveAsync();
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The provider is disposed after the tracked operation settles.
            }
            catch (Exception exception)
            {
                if (!_destroyed)
                {
                    SetLocalizedStatus(
                        "{0} Cleanup needs a retry: {1}",
                        reason,
                        exception.Message);
                    Debug.LogWarning(_status);
                }
            }
            finally
            {
                _networkTerminationRequested = false;
                SynchronizeLifecycleStateFromSession(force: true);
            }
        }
    }
}

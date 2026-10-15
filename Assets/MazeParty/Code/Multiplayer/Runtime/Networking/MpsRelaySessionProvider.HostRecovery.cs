using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class MpsRelaySessionProvider
    {
        // MPS 2.3.1 NetworkModule's session metadata key. Clearing stale Relay
        // metadata is necessary when the original host restarts with no network
        // handler: StopNetworkAsync only accepts the Started state.
        private const string NetworkMetadataProperty = "_session_network";
        private bool _localTransportLost;
        private CancellationTokenSource _sessionPollingSource;
        private CancellationTokenSource _hostRecoverySource;
        private Task _hostRecoveryTask;
        public Func<Task> PrepareHostRecoveryAsync { get; set; }
        public event Action HostNetworkRestored;

        private async Task PollSessionMembershipAsync(ISession session, CancellationToken token)
        {
            try
            {
                while (OwnsSession(session))
                {
                    await Task.Delay(5000, token);
                    if (!OwnsSession(session)) return;
                    if (IsWaitingForOriginalHost(session)) continue;
                    try
                    {
                        await session.RefreshAsync();
                        token.ThrowIfCancellationRequested();
                        if (!OwnsSession(session)) return;
                        RebuildSnapshot();
                        StartHostRecoveryIfNeeded();
                        if (session.IsHost && GetSessionPhase(session) == MultiplayerConstants.LobbyPhase &&
                            session.Players.Any(player => !HasReservedSeat(session, player.Id)))
                            RunSeatAssignment();
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("Session membership refresh will retry: " + exception.Message);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }

        public void NotifyTransportStopped()
        {
            if (_session == null || _ending) return;
            _localTransportLost = true;
            RebuildSnapshot();
            StartHostRecoveryIfNeeded();
        }

        public void NotifyTransportConnected()
        {
            _localTransportLost = false;
            if (_session != null) RebuildSnapshot();
        }

        private bool IsWaitingForOriginalHost(ISession session) =>
            session != null && (_localTransportLost || session.Host != _originalHostId ||
                GetSessionPhase(session) == MultiplayerConstants.HostRecoveryPhase);

        private void StartHostRecoveryIfNeeded()
        {
            if (_ending || _session == null || !IsWaitingForOriginalHost(_session) ||
                (_hostRecoveryTask != null && !_hostRecoveryTask.IsCompleted)) return;
            _hostRecoverySource = new CancellationTokenSource();
            _hostRecoveryTask = MaintainOriginalHostRecoveryAsync(_session, _hostRecoverySource.Token);
        }

        private void CancelHostRecovery()
        {
            _hostRecoverySource?.Cancel();
            _hostRecoverySource?.Dispose();
            _hostRecoverySource = null;
            _hostRecoveryTask = null;
            _localTransportLost = false;
        }

        private bool OwnsSession(ISession session) => ReferenceEquals(session, _session) && !_ending;

        private async Task MaintainOriginalHostRecoveryAsync(ISession session, CancellationToken token)
        {
            // Assign the tracked task before synchronous Changed callbacks can re-enter.
            await Task.Yield();
            string preparedEpoch = null;
            try
            {
                while (OwnsSession(session))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        await session.RefreshAsync();
                        if (!OwnsSession(session)) return;
                        if (!IsWaitingForOriginalHost(session))
                        {
                            if (preparedEpoch != null && session.IsHost && session.Host == _originalHostId &&
                                session.AsHost().Network.State == NetworkState.Started)
                                HostNetworkRestored?.Invoke();
                            return;
                        }

                        if (session.IsHost && GetSessionPhase(session) != MultiplayerConstants.HostRecoveryPhase)
                            await PrepareWaitingRoomAsync(session, token);

                        var epoch = GetSessionProperty(session, MultiplayerConstants.HostRecoveryEpochProperty, "");
                        if (GetSessionPhase(session) == MultiplayerConstants.HostRecoveryPhase && epoch.Length > 0)
                        {
                            // All peers finish NGO shutdown and local scene cleanup before
                            // the original host publishes a replacement Relay connection.
                            if (preparedEpoch != epoch)
                            {
                                if (PrepareHostRecoveryAsync != null) await PrepareHostRecoveryAsync();
                                token.ThrowIfCancellationRequested();
                                if (!OwnsSession(session)) return;
                                if (_identity.PlayerId == _originalHostId)
                                    session.CurrentPlayer.SetProperty(MultiplayerConstants.HostReturnRequestProperty,
                                        new PlayerProperty(epoch, VisibilityPropertyOptions.Member));
                                session.CurrentPlayer.SetProperty(MultiplayerConstants.ReadyProperty,
                                    new PlayerProperty("0", VisibilityPropertyOptions.Member));
                                session.CurrentPlayer.SetProperty(MultiplayerConstants.NetworkClientIdProperty, null);
                                session.CurrentPlayer.SetProperty(MultiplayerConstants.HostRecoveryReadyProperty,
                                    new PlayerProperty(epoch, VisibilityPropertyOptions.Member));
                                await SaveCurrentPlayerDataWithRetryAsync(session);
                                preparedEpoch = epoch;
                            }

                            if (session.IsHost && session.Host != _originalHostId)
                            {
                                var original = session.Players.FirstOrDefault(p => p.Id == _originalHostId);
                                if (original != null && SessionRecoveryRules.CanReturnHostAuthority(
                                        _originalHostId, original.Id, epoch, GetPlayerProperty(original,
                                            MultiplayerConstants.HostReturnRequestProperty, "")))
                                {
                                    await _hostMutationGate.WaitAsync(token);
                                    try
                                    {
                                        if (!OwnsSession(session) || !session.IsHost) continue;
                                        var caretaker = session.AsHost();
                                        caretaker.Host = _originalHostId;
                                        await caretaker.SavePropertiesAsync();
                                    }
                                    finally { _hostMutationGate.Release(); }
                                }
                            }
                            else if (session.IsHost && session.Host == _originalHostId &&
                                     session.Players.All(p => GetPlayerProperty(p,
                                         MultiplayerConstants.HostRecoveryReadyProperty, "") == epoch))
                            {
                                await RestoreOriginalHostNetworkAsync(session, token);
                                return;
                            }
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        // A temporary service failure must not delete the room or journal.
                        Debug.LogWarning("Waiting for original-host recovery: " + exception.Message);
                    }
                    await Task.Delay(2000, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }

        private async Task PrepareWaitingRoomAsync(ISession session, CancellationToken token)
        {
            await _hostMutationGate.WaitAsync(token);
            try
            {
                if (!OwnsSession(session) || !session.IsHost) return;
                var host = session.AsHost();
                if (host.Network.State == NetworkState.Started) await host.Network.StopNetworkAsync();
                token.ThrowIfCancellationRequested();
                if (!OwnsSession(session) || !session.IsHost) return;
                host.SetProperty(NetworkMetadataProperty, null);
                host.SetProperty(MultiplayerConstants.PhaseProperty,
                    new SessionProperty(MultiplayerConstants.HostRecoveryPhase, VisibilityPropertyOptions.Member));
                host.SetProperty(MultiplayerConstants.HostRecoveryEpochProperty,
                    new SessionProperty(Guid.NewGuid().ToString("N"), VisibilityPropertyOptions.Member));
                host.IsLocked = false;
                await host.SavePropertiesAsync();
                RebuildSnapshot();
            }
            finally { _hostMutationGate.Release(); }
        }

        private async Task RestoreOriginalHostNetworkAsync(ISession session, CancellationToken token)
        {
            await _hostMutationGate.WaitAsync(token);
            try
            {
                if (!OwnsSession(session) || !session.IsHost || session.Host != _originalHostId) return;
                var host = session.AsHost();
                if (host.Network.State != NetworkState.Started)
                    await host.Network.StartRelayNetworkAsync(new RelayNetworkOptions());
                token.ThrowIfCancellationRequested();
                if (!OwnsSession(session) || !session.IsHost) return;
                host.SetProperty(MultiplayerConstants.PhaseProperty,
                    new SessionProperty(MultiplayerConstants.LobbyPhase, VisibilityPropertyOptions.Member));
                host.SetProperty(MultiplayerConstants.HostRecoveryEpochProperty, null);
                host.IsLocked = false;
                await host.SavePropertiesAsync();
                RebuildSnapshot();
                HostNetworkRestored?.Invoke();
            }
            finally { _hostMutationGate.Release(); }
        }

        private static bool HasReservedSeat(ISession session, string playerId)
        {
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                if (GetSessionProperty(session, MultiplayerConstants.SlotProperty(slot), "") == playerId)
                    return true;
            return false;
        }

        private async Task OpenReservedSeatForReconnectAsync(ISession session)
        {
            await _hostMutationGate.WaitAsync();
            try
            {
                if (!OwnsSession(session) || !session.IsHost ||
                    GetSessionPhase(session) != MultiplayerConstants.PlayingPhase) return;
                session.AsHost().IsLocked = false;
                await session.AsHost().SavePropertiesAsync();
            }
            catch (Exception exception) { Debug.LogWarning("Could not open the reserved reconnect seat: " + exception.Message); }
            finally { _hostMutationGate.Release(); }
        }

        private async Task RemoveUnreservedPlayerAsync(ISession session, string playerId)
        {
            try
            {
                if (OwnsSession(session) && session.IsHost && !HasReservedSeat(session, playerId))
                    await session.AsHost().RemovePlayerAsync(playerId);
            }
            catch (Exception exception) { Debug.LogWarning("Could not remove an unreserved member: " + exception.Message); }
        }
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using MazeParty.Gameplay;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
        private bool _creatingFreshRoom;
        private void OnFreshCreateConfirmed()
        {
            if (IsInSession || IsBusy) return;
            RunAsync(CreateFreshAfterConfirmationAsync, GameText.N("Closing the previous room and creating a new room..."),
                SessionLifecycleState.Connecting);
        }

        private async Task CreateFreshAfterConfirmationAsync()
        {
            _creatingFreshRoom = true;
            try
            {
                var previousId = TryGetPlayingReconnectTicket(out var ticket) ? ticket : _savedEntry?.sessionId;
                if (!string.IsNullOrWhiteSpace(previousId))
                {
                    try
                    {
                        await _sessions.ReconnectToSessionAsync(previousId, _localProfile.DisplayName);
                        var deadline = Time.realtimeSinceStartupAsDouble + 60d;
                        while (IsInSession && !CurrentSession.IsHost)
                        {
                            if (Time.realtimeSinceStartupAsDouble >= deadline)
                                throw new TimeoutException(GameText.T("Waiting for the original host authority. Retry creating the room."));
                            await Task.Delay(200, _sessionOperations.LifetimeToken);
                        }
                        if (IsInSession) await _sessions.LeaveAsync();
                    }
                    catch (SessionException exception) when (!IsInSession &&
                        (exception.Error == SessionError.SessionNotFound || exception.Error == SessionError.SessionDeleted)) { }
                }
                await CreateAndPublishAsync(_localProfile.DisplayName);
            }
            finally { _creatingFreshRoom = false; }
        }

        private MatchRecoverySnapshot _savedEntry;
        private DateTime _savedEntryUtc;
        public string RecoverySessionId => _sessions?.CurrentSessionId ?? string.Empty;
        public bool CanRestartSavedGame => _savedEntry != null &&
            DateTime.UtcNow < _savedEntryUtc + MatchRecoveryJournal.DefaultRetention &&
            MatchRecoveryResumeRules.IsOriginalHost(_savedEntry, _identity?.PlayerId);
        public bool IsWaitingForReconnect => IsInSession && CurrentSession.Phase == MultiplayerConstants.PlayingPhase &&
            ((NetworkMatchState.Instance != null && NetworkMatchState.Instance.IsReconnectPaused) ||
             (_pendingBoardLoadReconnect && _reconnectGraceDisconnectedClients.Count > 0));
        public double ReconnectWaitRemaining => NetworkMatchState.Instance != null && NetworkMatchState.Instance.IsReconnectPaused
            ? NetworkMatchState.Instance.ReconnectRemaining : Math.Max(0d, _pendingBoardLoadReconnectEndsAt - Time.realtimeSinceStartupAsDouble);
        public bool CanReturnToLobbyDuringReconnect => IsWaitingForReconnect && CurrentSession.IsHost &&
            _networkManager != null && _networkManager.IsServer && !IsBusy;

        public bool ReturnToLobbyDuringReconnect()
        {
            if (!CanReturnToLobbyDuringReconnect) return false;
            return VoidActiveMatchToLobbyOnServer(GameText.T("The host ended the reconnect wait. The saved game is preserved."), true);
        }

        private async Task InitializeRecoveryEntryAsync()
        {
            await _identity.SignInAsync(_localProfile.DisplayName);
            _playingReconnectTicketKey = BuildPlayingReconnectTicketKey(_identity.PlayerId);
            RefreshSavedEntry();
        }

        private void RefreshSavedEntry()
        {
            var saved = new HostMinigameScheduleSession();
            _savedEntry = null;
            if (saved.TryPeekRecovery(out var record) == MatchRecoveryLoadStatus.Loaded &&
                saved.TryPeekRecoverySnapshot(out var snapshot) == MatchRecoveryLoadStatus.Loaded)
            {
                _savedEntry = snapshot;
                _savedEntryUtc = record.SavedUtc;
            }
        }

        public void NotifySavedRecovery(MatchRecoverySnapshot snapshot, DateTime savedUtc)
        {
            _savedEntry = snapshot;
            _savedEntryUtc = savedUtc;
        }

        private void RenderRecoveryEntry(bool inSession)
        {
            var details = string.Empty;
            if (CanRestartSavedGame)
            {
                details = _savedEntryUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "\n" +
                    string.Join("\n", _savedEntry.players.Select((p, i) => string.IsNullOrWhiteSpace(p.displayName)
                        ? GameText.F("Player {0}", i + 1) : p.displayName));
            }
            lobbyView.RecoveryView.Render(inSession, IsBusy, TryGetPlayingReconnectTicket(out _),
                CanRestartSavedGame, details, _status);
        }

        private void OnReconnectRequested()
        {
            if (IsInSession || !TryGetPlayingReconnectTicket(out var id)) return;
            RunAsync(() => ReconnectAndPublishAsync(id, _localProfile.DisplayName),
                GameText.N("Reconnecting to the interrupted online game..."), SessionLifecycleState.Reconnecting);
        }

        private void OnRestartRequested()
        {
            if (IsInSession) return;
            RunAsync(RestartSavedRoomAsync, GameText.N("Restoring the saved room..."), SessionLifecycleState.Reconnecting);
        }

        private async Task RestartSavedRoomAsync()
        {
            await _identity.SignInAsync(_localProfile.DisplayName);
            RefreshSavedEntry();
            if (!CanRestartSavedGame)
                throw new InvalidOperationException(GameText.T("Only the original host can restart this saved game."));
            var saved = _savedEntry;
            var provider = (MpsRelaySessionProvider)_sessions;
            provider.ConfigureRecoveryRoster(Array.ConvertAll(saved.players, p => p.playerKey), Array.ConvertAll(saved.players, p => p.displayName));
            var id = TryGetPlayingReconnectTicket(out var ticket) ? ticket : saved.sessionId;
            if (!string.IsNullOrWhiteSpace(id))
            {
                try
                {
                    await _sessions.ReconnectToSessionAsync(id, _localProfile.DisplayName);
                    if (CurrentSession.Phase != MultiplayerConstants.HostRecoveryPhase)
                    {
                        await PublishLocalNetworkClientIdWhenReadyAsync();
                        await SelectSavedMatchAsync();
                    }
                    return;
                }
                catch (SessionException exception) when (!IsInSession &&
                    (exception.Error == SessionError.SessionNotFound || exception.Error == SessionError.SessionDeleted))
                {
                    // Only authoritative absence permits replacement. A transient
                    // network or permission failure must not split the group.
                }
            }
            if (!BoardMapSelection.TryCreate(saved.boardMapId, saved.boardMapContentVersion, out var selection))
                throw new InvalidOperationException(GameText.T("The saved match does not contain a valid board map selection."));
            await _sessions.CreateAsync("MazeParty Saved Room", _localProfile.DisplayName, selection);
            await PublishLocalNetworkClientIdWhenReadyAsync();
            await SelectSavedMatchAsync();
        }
    }
}

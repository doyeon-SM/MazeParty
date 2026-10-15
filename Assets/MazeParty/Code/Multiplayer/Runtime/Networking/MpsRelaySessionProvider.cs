using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MazeParty.Gameplay;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class MpsRelaySessionProvider : IOnlineSessionProvider
    {
        private readonly IPlayerIdentityProvider _identity;
        private readonly SemaphoreSlim _hostMutationGate = new SemaphoreSlim(1, 1);

        private ISession _session;
        private string _originalHostId = string.Empty;
        private bool _ending;

        public MpsRelaySessionProvider(IPlayerIdentityProvider identity)
        {
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        }

        public event Action Changed;
        public event Action<string> Ended;

        public bool IsInSession => _session != null;
        public string CurrentSessionId => _session?.Id ?? string.Empty;
        public SessionSnapshot Current { get; private set; } = SessionSnapshot.Empty;
        public Func<bool> KeepRoomOnPlayingDeparture { get; set; }

        public async Task CreateAsync(
            string roomName,
            string displayName,
            BoardMapSelection initialBoardMapSelection)
        {
            ThrowIfAlreadyInSession();
            var boardMapSelection = ValidateBoardMapSelection(
                initialBoardMapSelection);
            await _identity.SignInAsync(displayName);

            var sessionProperties = new Dictionary<string, SessionProperty>
            {
                [MultiplayerConstants.OriginalHostProperty] =
                    new SessionProperty(_identity.PlayerId, VisibilityPropertyOptions.Member),
                [MultiplayerConstants.SlotProperty(0)] =
                    new SessionProperty(_identity.PlayerId, VisibilityPropertyOptions.Member),
                [MultiplayerConstants.PhaseProperty] =
                    new SessionProperty(MultiplayerConstants.LobbyPhase, VisibilityPropertyOptions.Member),
                [MultiplayerConstants.BuildVersionProperty] =
                    new SessionProperty(
                        BuildVersionCompatibility.CurrentVersion,
                        VisibilityPropertyOptions.Member),
                [MultiplayerConstants.BoardMapIdProperty] =
                    new SessionProperty(
                        boardMapSelection.MapId,
                        VisibilityPropertyOptions.Member),
                [MultiplayerConstants.BoardMapVersionProperty] =
                    new SessionProperty(
                        boardMapSelection.ContentVersion.ToString(
                            CultureInfo.InvariantCulture),
                        VisibilityPropertyOptions.Member)
            };

            if (_recoveryRosterKeys != null)
                for (var slot = 0; slot < _recoveryRosterKeys.Length; slot++)
                    sessionProperties[RecoveryPlayerKeyProperty(slot)] =
                        new SessionProperty(_recoveryRosterKeys[slot], VisibilityPropertyOptions.Member);
            if (_recoveryRosterNames != null)
                for (var slot = 0; slot < _recoveryRosterNames.Length; slot++)
                    sessionProperties[RecoveryPlayerNameProperty(slot)] = new SessionProperty(
                        _recoveryRosterNames[slot] ?? "Player " + (slot + 1), VisibilityPropertyOptions.Member);
            var options = new SessionOptions
            {
                Type = MultiplayerConstants.SessionType,
                Name = string.IsNullOrWhiteSpace(roomName) ? "MazeParty Room" : roomName.Trim(),
                MaxPlayers = MultiplayerConstants.MaxPlayers,
                IsPrivate = true,
                PlayerProperties = BuildPlayerProperties(_identity.DisplayName),
                SessionProperties = sessionProperties
            }
            .WithRelayNetwork()
            .WithNetworkOptions(new NetworkOptions
            {
                RelayProtocol = RelayProtocol.DTLS
            });

            // MPS owns NGO lifecycle here. CreateSessionAsync configures Relay and calls
            // NetworkManager.StartHost internally; do not call StartHost or Shutdown here.
            var createdSession = await MultiplayerService.Instance.CreateSessionAsync(options);
            AttachSession(createdSession);
            await EnsureSeatAssignmentsAsync();
        }

        public async Task JoinByCodeAsync(string code, string displayName)
        {
            ThrowIfAlreadyInSession();

            var normalizedCode = string.IsNullOrWhiteSpace(code)
                ? string.Empty
                : code.Trim().ToUpperInvariant();
            if (normalizedCode.Length == 0)
            {
                throw new ArgumentException(GameText.T("Enter an invite code."), nameof(code));
            }

            await _identity.SignInAsync(displayName);

            var options = new JoinSessionOptions
            {
                Type = MultiplayerConstants.SessionType,
                PlayerProperties = BuildPlayerProperties(_identity.DisplayName)
            }
            .WithNetworkOptions(new NetworkOptions
            {
                RelayProtocol = RelayProtocol.DTLS
            });

            // MPS applies Relay connection data and calls NetworkManager.StartClient.
            var joinedSession = await RetryRateLimitedSessionOperationAsync(() =>
                MultiplayerService.Instance.JoinSessionByCodeAsync(normalizedCode, options));

            if (!HasCompatibleBuild(joinedSession))
            {
                // Take ownership before cleanup. If service/network cleanup still fails
                // after bounded retries, the UI keeps a handle and can retry LeaveAsync
                // instead of becoming stuck behind MPS SessionTypeAlreadyExists.
                AttachSession(joinedSession);
                try
                {
                    await LeaveAsync();
                }
                catch (Exception cleanupException)
                {
                    throw new InvalidOperationException(
                        GameText.T(
                            "The host uses a different game build and automatic cleanup failed. " +
                            "Use Leave Session to retry cleanup."),
                        cleanupException);
                }

                throw new InvalidOperationException(
                    GameText.T("The host uses a different game build."));
            }

            AttachSession(joinedSession);
            if ((HasRecoveryRoster(joinedSession) || GetSessionPhase(joinedSession) != MultiplayerConstants.LobbyPhase) &&
                !HasReservedSeat(joinedSession, _identity.PlayerId))
            {
                await LeaveAsync();
                throw new InvalidOperationException("This room is reserved for the interrupted game's players.");
            }
        }

        public async Task ReconnectToSessionAsync(string sessionId, string displayName)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await ReconnectToSessionCoreAsync(sessionId, displayName);
                    return;
                }
                catch (SessionException exception) when (!IsInSession && attempt < 5 &&
                    exception.Error == SessionError.Unknown &&
                    exception.InnerException is Unity.Services.Relay.RelayServiceException)
                {
                    // The room survives while the original host replaces an
                    // expired Relay allocation. The SDK cleans failed joins;
                    // refresh membership and retry the same room, never split it.
                    await Task.Delay(2000 + attempt * 1000);
                }
            }
        }

        private async Task ReconnectToSessionCoreAsync(string sessionId, string displayName)
        {
            ThrowIfAlreadyInSession();
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("A reconnect session ID is required.");
            await _identity.SignInAsync(displayName);
            var joinedIds = await RetryRateLimitedSessionOperationAsync(() =>
                MultiplayerService.Instance.GetJoinedSessionIdsAsync());
            ISession recovered;
            if (joinedIds != null && joinedIds.Contains(sessionId, StringComparer.Ordinal))
            {
                recovered = await RetryRateLimitedSessionOperationAsync(() =>
                    MultiplayerService.Instance.ReconnectToSessionAsync(sessionId,
                        new ReconnectSessionOptions { Type = MultiplayerConstants.SessionType }));
            }
            else
            {
                // A backend disconnect timeout may have removed membership. The host
                // retains authenticated seat owners and opens only their recovery path.
                recovered = await RetryRateLimitedSessionOperationAsync(() =>
                    MultiplayerService.Instance.JoinSessionByIdAsync(sessionId,
                    new JoinSessionOptions {
                        Type = MultiplayerConstants.SessionType,
                        PlayerProperties = BuildPlayerProperties(_identity.DisplayName)
                    }.WithNetworkOptions(new NetworkOptions { RelayProtocol = RelayProtocol.DTLS })));
            }
            AttachSession(recovered);
            if (!HasCompatibleBuild(recovered) ||
                ((HasRecoveryRoster(recovered) || GetSessionPhase(recovered) != MultiplayerConstants.LobbyPhase) &&
                 !HasReservedSeat(recovered, _identity.PlayerId)))
            {
                await LeaveAsync();
                throw new InvalidOperationException("The saved room does not reserve a seat for this account and game build.");
            }
            if (_identity.PlayerId == _originalHostId)
            {
                // Also covers restart before backend host election has completed.
                if (recovered.IsHost && GetSessionPhase(recovered) != MultiplayerConstants.HostRecoveryPhase)
                    await PrepareWaitingRoomAsync(recovered, CancellationToken.None);
                StartHostRecoveryIfNeeded();
            }
        }

        public async Task PublishLocalNetworkClientIdAsync(ulong clientId)
        {
            var session = RequireSession();
            if (session.CurrentPlayer == null) await session.RefreshAsync();
            if (!ReferenceEquals(session, _session) || _ending) return;
            if (session.CurrentPlayer == null)
                throw new InvalidOperationException("The local session member is not available yet.");
            session.CurrentPlayer.SetProperty(
                MultiplayerConstants.NetworkClientIdProperty,
                new PlayerProperty(
                    clientId.ToString(CultureInfo.InvariantCulture),
                    VisibilityPropertyOptions.Member));
            session.CurrentPlayer.SetProperty(
                MultiplayerConstants.LeavingProperty,
                new PlayerProperty("0", VisibilityPropertyOptions.Member));

            await SaveCurrentPlayerDataWithRetryAsync(session);
            if (ReferenceEquals(session, _session))
            {
                RebuildSnapshot();
            }
        }

        public bool TryGetAuthoritativeSlot(ulong clientId, out int slot)
        {
            slot = -1;
            var session = _session;
            if (session == null)
            {
                return false;
            }

            var clientIdText = clientId.ToString(CultureInfo.InvariantCulture);
            var matches = session.Players
                .Where(player =>
                    GetPlayerProperty(
                        player,
                        MultiplayerConstants.NetworkClientIdProperty,
                        string.Empty) == clientIdText)
                .ToArray();
            if (matches.Length != 1)
            {
                return false;
            }

            var playerId = matches[0].Id;
            for (var candidate = 0; candidate < MultiplayerConstants.MaxPlayers; candidate++)
            {
                if (session.Properties.TryGetValue(
                        MultiplayerConstants.SlotProperty(candidate),
                        out var property) &&
                    property != null &&
                    string.Equals(property.Value, playerId, StringComparison.Ordinal))
                {
                    slot = candidate;
                    return true;
                }
            }

            return false;
        }

        public async Task<bool> RemoveDisconnectedLobbyPlayerAsync(
            string expectedSessionId,
            ulong clientId,
            bool honorLeaveMarker = true)
        {
            await _hostMutationGate.WaitAsync();
            try
            {
                var session = _session;
                if (session == null ||
                    _ending ||
                    string.IsNullOrWhiteSpace(expectedSessionId) ||
                    !string.Equals(session.Id, expectedSessionId, StringComparison.Ordinal) ||
                    !session.IsHost ||
                    GetSessionPhase(session) != MultiplayerConstants.LobbyPhase)
                {
                    return false;
                }

                await session.RefreshAsync();

                if (!ReferenceEquals(session, _session) ||
                    _ending ||
                    !string.Equals(session.Id, expectedSessionId, StringComparison.Ordinal) ||
                    !session.IsHost ||
                    GetSessionPhase(session) != MultiplayerConstants.LobbyPhase)
                {
                    return false;
                }

                var clientIdText = clientId.ToString(CultureInfo.InvariantCulture);
                var matches = session.Players
                    .Where(player =>
                        player.Id != session.CurrentPlayer.Id &&
                        GetPlayerProperty(
                            player,
                            MultiplayerConstants.NetworkClientIdProperty,
                            string.Empty) == clientIdText)
                    .ToArray();

                if (matches.Length == 0)
                {
                    return false;
                }

                if (matches.Length > 1)
                {
                    Debug.LogWarning(
                        "Multiple MPS players claimed NGO client " + clientIdText +
                        "; no lobby member was removed.");
                    return false;
                }

                var player = matches[0];
                if (honorLeaveMarker &&
                    GetPlayerProperty(
                        player,
                        MultiplayerConstants.LeavingProperty,
                        "0") == "1")
                {
                    return false;
                }

                // MPS disconnect removal time is environment-wide, so the host overrides
                // it only in the lobby. Gameplay keeps the configured reconnect window.
                // TODO(STEAM-SESSION): replace this member-property mapping with an
                // authenticated Steam lobby member / transport identity binding.
                await session.AsHost().RemovePlayerAsync(player.Id);
                return true;
            }
            catch (SessionException exception) when (IsTerminalSessionError(exception))
            {
                return false;
            }
            finally
            {
                _hostMutationGate.Release();
            }
        }

        public async Task SetReadyAsync(bool ready)
        {
            var session = RequireSession();
            var value = ready ? "1" : "0";
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (session.CurrentPlayer == null) await session.RefreshAsync();
                if (!ReferenceEquals(session, _session) || _ending) return;
                var player = session.CurrentPlayer;
                if (player != null)
                {
                    player.SetProperty(MultiplayerConstants.ReadyProperty,
                        new PlayerProperty(value, VisibilityPropertyOptions.Member));
                    await SaveCurrentPlayerDataWithRetryAsync(session);
                    if (session.CurrentPlayer != null && GetPlayerProperty(
                            session.CurrentPlayer, MultiplayerConstants.ReadyProperty, "") == value)
                    {
                        RebuildSnapshot();
                        return;
                    }
                }
                await Task.Delay(300);
                await session.RefreshAsync();
            }
            throw new InvalidOperationException("The local session member is still refreshing. Please retry Ready.");
        }

        public async Task SetBoardMapAsync(BoardMapSelection selection)
        {
            var validatedSelection = ValidateBoardMapSelection(selection);
            await _hostMutationGate.WaitAsync();
            try
            {
                var session = RequireSession();
                var phase = GetSessionPhase(session);
                if (!SessionRules.CanChangeBoardMap(
                        session.IsHost,
                        phase))
                {
                    throw new InvalidOperationException(!session.IsHost
                        ? GameText.T("Only the host can select the board map.")
                        : GameText.T(
                            "The board map can only be changed in the lobby."));
                }

                var host = session.AsHost();
                host.SetProperty(
                    MultiplayerConstants.BoardMapIdProperty,
                    new SessionProperty(
                        validatedSelection.MapId,
                        VisibilityPropertyOptions.Member));
                host.SetProperty(
                    MultiplayerConstants.BoardMapVersionProperty,
                    new SessionProperty(
                        validatedSelection.ContentVersion.ToString(
                            CultureInfo.InvariantCulture),
                        VisibilityPropertyOptions.Member));

                await SaveHostPropertiesWithRetryAsync(host, session);
                if (_ending ||
                    !ReferenceEquals(session, _session) ||
                    !session.IsHost)
                {
                    throw new InvalidOperationException(
                        GameText.T(
                            "The session changed before the board map selection was saved."));
                }

                RebuildSnapshot();
            }
            finally
            {
                _hostMutationGate.Release();
            }
        }

        public Task SetPlayingAsync(bool playing) => SetPlayingAsync(playing, true);

        public async Task SetPlayingAsync(bool playing, bool preserveRecoveryRoster)
        {
            await _hostMutationGate.WaitAsync();
            try
            {
                var session = RequireSession();
                if (!session.IsHost)
                {
                    throw new InvalidOperationException("Only the host can change the game state.");
                }

                if (playing)
                {
                    await EnsureSeatAssignmentsLockedAsync(session);
                    if (!ReferenceEquals(session, _session) || _ending)
                    {
                        throw new InvalidOperationException(
                            GameText.T("The session changed before the game could start."));
                    }

                    RebuildSnapshot();
                    if (!Current.CanStart)
                    {
                        throw new InvalidOperationException(
                            GameText.T("Exactly four ready players are required."));
                    }
                }

                var host = session.AsHost();
                host.IsLocked = playing;
                if (!playing && !preserveRecoveryRoster)
                {
                    // A completed match releases reserved seats. Interrupted
                    // games retain them until resumed or explicitly discarded.
                    for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                    {
                        host.SetProperty(RecoveryPlayerKeyProperty(slot), null);
                        host.SetProperty(RecoveryPlayerNameProperty(slot), null);
                    }
                    ConfigureRecoveryRoster(null);
                }
                if (playing)
                {
                    foreach (var player in Current.Players)
                    {
                        host.SetProperty(RecoveryPlayerKeyProperty(player.Slot), new SessionProperty(
                            MatchRecoveryFingerprint.CreatePlayerKey(player.PlayerId), VisibilityPropertyOptions.Member));
                        host.SetProperty(RecoveryPlayerNameProperty(player.Slot), new SessionProperty(
                            player.DisplayName, VisibilityPropertyOptions.Member));
                    }
                }
                host.SetProperty(
                    MultiplayerConstants.PhaseProperty,
                    new SessionProperty(
                        playing ? MultiplayerConstants.PlayingPhase : MultiplayerConstants.LobbyPhase,
                        VisibilityPropertyOptions.Member));

                await SaveHostPropertiesWithRetryAsync(host, session);
                if (_ending ||
                    !ReferenceEquals(session, _session) ||
                    !session.IsHost)
                {
                    throw new InvalidOperationException(
                        GameText.T("The session changed before the game state was saved."));
                }

                RebuildSnapshot();
            }
            finally
            {
                _hostMutationGate.Release();
            }
        }

        public async Task LeaveAsync()
        {
            var session = _session;
            if (session == null || _ending)
            {
                return;
            }

            _ending = true;
            var completed = false;
            await _hostMutationGate.WaitAsync();
            try
            {
                if (!session.IsHost)
                {
                    try
                    {
                        session.CurrentPlayer.SetProperty(
                            MultiplayerConstants.LeavingProperty,
                            new PlayerProperty("1", VisibilityPropertyOptions.Member));
                        await SaveCurrentPlayerDataWithRetryAsync(session);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            "Could not publish the normal-leave marker; continuing cleanup: " +
                            exception.Message);
                    }
                }

                await EndSessionWithRetryAsync(session);
                await WaitForSessionUnregistrationAsync(session);
                completed = true;
            }
            finally
            {
                _hostMutationGate.Release();

                if (completed && ReferenceEquals(session, _session))
                {
                    ClearSession();
                }

                _ending = false;
            }
        }

        public void Dispose()
        {
            CancelHostRecovery();

            if (_session != null)
            {
                DetachSession(_session);
            }
        }

        private static Dictionary<string, PlayerProperty> BuildPlayerProperties(string displayName)
        {
            return new Dictionary<string, PlayerProperty>
            {
                [MultiplayerConstants.DisplayNameProperty] =
                    new PlayerProperty(displayName, VisibilityPropertyOptions.Member),
                [MultiplayerConstants.ReadyProperty] =
                    new PlayerProperty("0", VisibilityPropertyOptions.Member),
                [MultiplayerConstants.LeavingProperty] =
                    new PlayerProperty("0", VisibilityPropertyOptions.Member)
            };
        }

        private void AttachSession(ISession session)
        {
            CancelHostRecovery();

            _session = session ?? throw new ArgumentNullException(nameof(session));
            _originalHostId = GetSessionProperty(
                session, MultiplayerConstants.OriginalHostProperty, session.Host);
            _ending = false;
            _sessionPollingSource = new CancellationTokenSource();
            _ = PollSessionMembershipAsync(session, _sessionPollingSource.Token);

            session.Changed += OnSessionChanged;
            session.PlayerJoined += OnPlayerJoined;
            session.PlayerHasLeft += OnPlayerHasLeft;
            session.PlayerPropertiesChanged += OnSessionChanged;
            session.SessionPropertiesChanged += OnSessionChanged;
            session.SessionHostChanged += OnSessionHostChanged;
            session.Deleted += OnSessionDeleted;
            session.RemovedFromSession += OnRemovedFromSession;

            RebuildSnapshot();
            if (session.IsHost && GetSessionPhase(session) == MultiplayerConstants.LobbyPhase)
            {
                RunSeatAssignment();
            }
            StartHostRecoveryIfNeeded();
        }

        private void DetachSession(ISession session)
        {
            _sessionPollingSource?.Cancel();
            _sessionPollingSource?.Dispose();
            _sessionPollingSource = null;
            session.Changed -= OnSessionChanged;
            session.PlayerJoined -= OnPlayerJoined;
            session.PlayerHasLeft -= OnPlayerHasLeft;
            session.PlayerPropertiesChanged -= OnSessionChanged;
            session.SessionPropertiesChanged -= OnSessionChanged;
            session.SessionHostChanged -= OnSessionHostChanged;
            session.Deleted -= OnSessionDeleted;
            session.RemovedFromSession -= OnRemovedFromSession;
        }

        private void OnSessionChanged()
        {
            RebuildSnapshot();
            StartHostRecoveryIfNeeded();
        }

        private void OnPlayerJoined(string playerId)
        {
            RebuildSnapshot();
            if (_session != null && GetSessionPhase(_session) != MultiplayerConstants.LobbyPhase)
            {
                if (_session.IsHost && !HasReservedSeat(_session, playerId))
                    _ = RemoveUnreservedPlayerAsync(_session, playerId);
                return;
            }
            if (_session != null && _session.IsHost)
            {
                RunSeatAssignment();
            }
        }

        private void OnPlayerHasLeft(string playerId)
        {
            var session = _session;
            RebuildSnapshot();

            if (session == null || _ending || !session.IsHost)
            {
                return;
            }

            if (Current.Phase == MultiplayerConstants.PlayingPhase)
            {
                var keepRoom = KeepRoomOnPlayingDeparture;
                if (keepRoom != null && keepRoom())
                {
                    // The match is already over for everyone. The room stays open;
                    // the next join after the lobby return reassigns the free seat.
                    return;
                }

                // Backend removal can also follow a crash or a connection timeout.
                // The server's reconnect timer owns match disposition; never delete
                // the shared room because one member disappeared.
                _ = OpenReservedSeatForReconnectAsync(session);
                return;
            }

            if (Current.Phase == MultiplayerConstants.LobbyPhase) RunSeatAssignment();
        }

        private void OnSessionHostChanged(string newHostId)
        {
            RebuildSnapshot();
            StartHostRecoveryIfNeeded();
        }

        private void OnSessionDeleted()
        {
            if (!_ending)
            {
                ObserveCompletedRemoteEnd(GameText.T("The host ended the session."));
            }
        }

        private void OnRemovedFromSession()
        {
            if (!_ending)
            {
                ObserveCompletedRemoteEnd(GameText.T("You were removed from the session."));
            }
        }

        private void EndFromRemote(string reason)
        {
            if (_ending)
            {
                return;
            }

            _ending = true;
            Ended?.Invoke(reason);
            _ = FinishRemoteEndAsync();
        }

        private async Task FinishRemoteEndAsync()
        {
            var session = _session;
            var completed = session == null;
            await _hostMutationGate.WaitAsync();
            try
            {
                if (session != null)
                {
                    await EndSessionWithRetryAsync(session);
                    await WaitForSessionUnregistrationAsync(session);
                    completed = true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Session cleanup did not finish. Use Leave Session to retry: " +
                    exception.Message);
            }
            finally
            {
                _hostMutationGate.Release();

                if (completed && ReferenceEquals(session, _session))
                {
                    ClearSession();
                }

                _ending = false;
            }
        }

        private void ObserveCompletedRemoteEnd(string reason)
        {
            if (_ending)
            {
                return;
            }

            _ending = true;
            Ended?.Invoke(reason);
            _ = FinishObservedRemoteEndAsync();
        }

        private async Task FinishObservedRemoteEndAsync()
        {
            var session = _session;
            var completed = session == null;
            await _hostMutationGate.WaitAsync();
            try
            {
                if (session != null)
                {
                    // MPS SessionManager receives Deleted/Removed first and owns cleanup.
                    // Re-entering LeaveAsync here caused LobbyNotFound and duplicate Stop.
                    await WaitForSessionUnregistrationAsync(session);
                    completed = true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "MPS did not unregister the completed session yet: " +
                    exception.Message);
            }
            finally
            {
                _hostMutationGate.Release();

                if (completed && ReferenceEquals(session, _session))
                {
                    ClearSession();
                }

                _ending = false;
            }
        }

        private async void RunSeatAssignment()
        {
            try
            {
                await EnsureSeatAssignmentsAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private async Task EnsureSeatAssignmentsAsync()
        {
            await _hostMutationGate.WaitAsync();
            try
            {
                await EnsureSeatAssignmentsLockedAsync(_session);
            }
            finally
            {
                _hostMutationGate.Release();
            }
        }

        private async Task EnsureSeatAssignmentsLockedAsync(ISession observedSession)
        {
            if (observedSession == null ||
                !ReferenceEquals(observedSession, _session) ||
                !observedSession.IsHost ||
                _ending ||
                GetSessionPhase(observedSession) != MultiplayerConstants.LobbyPhase)
            {
                return;
            }

            var host = observedSession.AsHost();
            if (HasRecoveryRoster(observedSession))
            {
                await AssignRecoverySeatsLockedAsync(observedSession);
                return;
            }
            var presentPlayerIds = new HashSet<string>(
                host.Players.Select(player => player.Id));
            var seatOwners = new string[MultiplayerConstants.MaxPlayers];

            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var key = MultiplayerConstants.SlotProperty(slot);
                if (!host.Properties.TryGetValue(key, out var property) ||
                    property == null ||
                    string.IsNullOrWhiteSpace(property.Value))
                {
                    continue;
                }

                if (presentPlayerIds.Contains(property.Value))
                {
                    seatOwners[slot] = property.Value;
                }
                else
                {
                    host.SetProperty(key, null);
                }
            }

            foreach (var player in host.Players)
            {
                if (seatOwners.Contains(player.Id))
                {
                    continue;
                }

                var occupied = seatOwners
                    .Select((owner, index) => string.IsNullOrEmpty(owner) ? -1 : index)
                    .Where(slot => slot >= 0);
                var freeSlot = SessionRules.FindLowestAvailableSlot(occupied);
                if (freeSlot < 0)
                {
                    break;
                }

                seatOwners[freeSlot] = player.Id;
                host.SetProperty(
                    MultiplayerConstants.SlotProperty(freeSlot),
                    new SessionProperty(player.Id, VisibilityPropertyOptions.Member));
            }

            // Save even when no new SetProperty call was needed. A previous failed save
            // leaves MPS' local Modified flag set and this call retries that same snapshot.
            await SaveHostPropertiesWithRetryAsync(host, observedSession);

            if (ReferenceEquals(observedSession, _session))
            {
                RebuildSnapshot();
            }
        }

        private static async Task<T> RetryRateLimitedSessionOperationAsync<T>(Func<Task<T>> operation)
        {
            for (var attempt = 0; ; attempt++)
            {
                try { return await operation(); }
                catch (SessionException exception) when (
                    exception.Error == SessionError.RateLimitExceeded && attempt < 3)
                {
                    await Task.Delay(1500 << attempt);
                }
            }
        }

        private static async Task SaveCurrentPlayerDataWithRetryAsync(ISession session)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    await session.SaveCurrentPlayerDataAsync();
                    return;
                }
                catch when (attempt < 2)
                {
                    await Task.Delay(250 << attempt);
                }
            }
        }

        private async Task SaveHostPropertiesWithRetryAsync(
            IHostSession host,
            ISession observedSession)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (!ReferenceEquals(observedSession, _session) ||
                    !observedSession.IsHost ||
                    _ending)
                {
                    return;
                }

                try
                {
                    await host.SavePropertiesAsync();
                    return;
                }
                catch when (attempt < 2)
                {
                    await Task.Delay(250 << attempt);
                }
            }
        }

        private void RebuildSnapshot()
        {
            var session = _session;
            if (session == null)
            {
                Current = SessionSnapshot.Empty;
                Changed?.Invoke();
                return;
            }

            var seatByPlayer = new Dictionary<string, int>();
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if (session.Properties.TryGetValue(
                        MultiplayerConstants.SlotProperty(slot),
                        out var property) &&
                    property != null &&
                    !string.IsNullOrWhiteSpace(property.Value))
                {
                    seatByPlayer[property.Value] = slot;
                }
            }

            var players = new List<OnlinePlayerSnapshot>(session.Players.Count);
            foreach (var player in session.Players)
            {
                var displayName = GetPlayerProperty(
                    player,
                    MultiplayerConstants.DisplayNameProperty,
                    "Player");
                var ready = GetPlayerProperty(
                    player,
                    MultiplayerConstants.ReadyProperty,
                    "0") == "1";
                var slot = seatByPlayer.TryGetValue(player.Id, out var assignedSlot)
                    ? assignedSlot
                    : -1;

                players.Add(new OnlinePlayerSnapshot(
                    player.Id,
                    displayName,
                    slot,
                    ready,
                    player.Id == session.Host));
            }

            players.Sort((left, right) =>
            {
                var leftSlot = left.Slot < 0 ? int.MaxValue : left.Slot;
                var rightSlot = right.Slot < 0 ? int.MaxValue : right.Slot;
                var comparison = leftSlot.CompareTo(rightSlot);
                return comparison != 0
                    ? comparison
                    : string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
            });

            var phase = session.Properties.TryGetValue(
                    MultiplayerConstants.PhaseProperty,
                    out var phaseProperty) &&
                phaseProperty != null
                ? phaseProperty.Value
                : MultiplayerConstants.LobbyPhase;
            var boardMapSelection = GetSessionBoardMapSelection(session);

            var waitingNames = new List<string>();
            if (HasRecoveryRoster(session))
                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                {
                    var member = players.FirstOrDefault(p => p.Slot == slot);
                    if (member == null || !member.IsReady)
                        waitingNames.Add(member?.DisplayName ?? GetSessionProperty(session,
                            RecoveryPlayerNameProperty(slot), "Player " + (slot + 1)));
                }
            Current = new SessionSnapshot(
                session.Code,
                session.IsHost && session.Host == _originalHostId,
                IsWaitingForOriginalHost(session) ? MultiplayerConstants.HostRecoveryPhase : phase,
                _identity.PlayerId,
                players,
                boardMapSelection,
                waitingNames);
            Changed?.Invoke();
        }

        private static BoardMapSelection GetSessionBoardMapSelection(
            ISession session)
        {
            var mapId = GetSessionProperty(
                session,
                MultiplayerConstants.BoardMapIdProperty,
                string.Empty);
            var versionText = GetSessionProperty(
                session,
                MultiplayerConstants.BoardMapVersionProperty,
                "0");
            if (int.TryParse(
                    versionText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var contentVersion) &&
                BoardMapSelection.TryCreate(
                    mapId,
                    contentVersion,
                    out var selection))
            {
                return selection;
            }

            // Preserve invalid service data so the host's pre-start exact-map
            // validation fails closed instead of silently selecting legacy content.
            return new BoardMapSelection(mapId, -1);
        }

        private static string GetSessionPhase(ISession session)
        {
            return session.Properties.TryGetValue(
                       MultiplayerConstants.PhaseProperty,
                       out var phaseProperty) &&
                   phaseProperty != null
                ? phaseProperty.Value
                : MultiplayerConstants.LobbyPhase;
        }

        private static string GetSessionProperty(
            ISession session,
            string key,
            string fallback)
        {
            return session.Properties.TryGetValue(key, out var property) &&
                   property != null &&
                   property.Value != null
                ? property.Value
                : fallback;
        }

        private static string GetPlayerProperty(
            IReadOnlyPlayer player,
            string key,
            string fallback)
        {
            return player.Properties.TryGetValue(key, out var property) &&
                   property != null &&
                   !string.IsNullOrWhiteSpace(property.Value)
                ? property.Value
                : fallback;
        }

        private ISession RequireSession()
        {
            return _session ?? throw new InvalidOperationException(
                GameText.T("You are not connected to an online session."));
        }

        private static BoardMapSelection ValidateBoardMapSelection(
            BoardMapSelection selection)
        {
            if (!BoardMapSelection.TryCreate(
                    selection.MapId,
                    selection.ContentVersion,
                    out var validated) ||
                validated != selection)
            {
                throw new ArgumentException(
                    "A valid board map selection is required.",
                    nameof(selection));
            }

            var catalog = Resources.Load<BoardMapCatalog>(
                BoardMapRuntimeLoader.CatalogResourcesPath);
            if (!BoardMapRuntimeLoader.TryResolveExactSelection(
                    catalog,
                    validated,
                    out var exact,
                    out var error))
            {
                throw new ArgumentException(
                    GameText.T(
                        "The selected board map could not be loaded safely."),
                    nameof(selection));
            }

            return exact;
        }

        private void ThrowIfAlreadyInSession()
        {
            if (_session != null)
            {
                throw new InvalidOperationException(
                    GameText.T("You are already connected to an online session."));
            }
        }

        private void ClearSession()
        {
            CancelHostRecovery();

            var session = _session;
            if (session != null)
            {
                DetachSession(session);
            }

            _session = null;
            _originalHostId = string.Empty;
            Current = SessionSnapshot.Empty;
            Changed?.Invoke();
        }

        private static bool HasCompatibleBuild(ISession session)
        {
            return session.Properties.TryGetValue(
                       MultiplayerConstants.BuildVersionProperty,
                       out var buildProperty) &&
                   buildProperty != null &&
                   BuildVersionCompatibility.IsCompatibleVersion(
                       buildProperty.Value);
        }

        private async Task EndSessionWithRetryAsync(ISession session)
        {
            // A backend-elected caretaker must leave without deleting the room.
            var deleteAsHost = SessionRecoveryRules.ShouldDeleteRoomOnExplicitLeave(
                session.IsHost, _identity.PlayerId,
                GetSessionProperty(session, MultiplayerConstants.OriginalHostProperty, session.Host));
            var terminal = session.State == SessionState.Deleted ||
                           (!deleteAsHost &&
                            session.State == SessionState.Disconnected);
            if (terminal)
            {
                return;
            }

            try
            {
                if (deleteAsHost)
                {
                    await session.AsHost().DeleteAsync();
                }
                else
                {
                    await session.LeaveAsync();
                }
            }
            catch (SessionException exception) when (IsTerminalSessionError(exception))
            {
                // A concurrent host delete may win. Do not repeat the entire MPS leave
                // pipeline because its network handler may already have stopped.
            }
        }

        private static bool IsTerminalSessionError(SessionException exception)
        {
            return exception.Error == SessionError.SessionNotFound ||
                   exception.Error == SessionError.SessionDeleted ||
                   exception.Error == SessionError.NotInLobby;
        }

        private static async Task WaitForSessionUnregistrationAsync(ISession session)
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                var sessions = MultiplayerService.Instance.Sessions;
                if (!sessions.TryGetValue(session.Type, out var registered) ||
                    !ReferenceEquals(registered, session))
                {
                    return;
                }

                await Task.Delay(50);
            }

            throw new InvalidOperationException(
                GameText.T("MPS has not unregistered the session yet. Try cleanup again."));
        }
    }
}

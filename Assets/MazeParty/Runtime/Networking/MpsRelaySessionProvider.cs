using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed class MpsRelaySessionProvider : IOnlineSessionProvider
    {
        private readonly IPlayerIdentityProvider _identity;
        private readonly SemaphoreSlim _hostMutationGate = new SemaphoreSlim(1, 1);

        private const int HostMigrationDeleteWaitMilliseconds = 8000;

        private ISession _session;
        private string _originalHostId = string.Empty;
        private CancellationTokenSource _hostMigrationFallbackSource;
        private bool _waitingForHostDeletion;
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

        public async Task CreateAsync(string roomName, string displayName)
        {
            ThrowIfAlreadyInSession();
            await _identity.SignInAsync(displayName);

            var sessionProperties = new Dictionary<string, SessionProperty>
            {
                [MultiplayerConstants.SlotProperty(0)] =
                    new SessionProperty(_identity.PlayerId, VisibilityPropertyOptions.Member),
                [MultiplayerConstants.PhaseProperty] =
                    new SessionProperty(MultiplayerConstants.LobbyPhase, VisibilityPropertyOptions.Member),
                [MultiplayerConstants.BuildVersionProperty] =
                    new SessionProperty(Application.version, VisibilityPropertyOptions.Member)
            };

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
                throw new ArgumentException("Enter an invite code.", nameof(code));
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
            var joinedSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(
                normalizedCode,
                options);

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
                        "The host uses a different game build and automatic cleanup failed. " +
                        "Use Leave Session to retry cleanup.",
                        cleanupException);
                }

                throw new InvalidOperationException(
                    "The host uses a different game build.");
            }

            AttachSession(joinedSession);
        }

        public async Task PublishLocalNetworkClientIdAsync(ulong clientId)
        {
            var session = RequireSession();
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
            session.CurrentPlayer.SetProperty(
                MultiplayerConstants.ReadyProperty,
                new PlayerProperty(ready ? "1" : "0", VisibilityPropertyOptions.Member));

            await SaveCurrentPlayerDataWithRetryAsync(session);
            RebuildSnapshot();
        }

        public async Task SetPlayingAsync(bool playing)
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
                            "The session changed before the game could start.");
                    }

                    RebuildSnapshot();
                    if (!Current.CanStart)
                    {
                        throw new InvalidOperationException(
                            "Exactly four ready players are required.");
                    }
                }

                var host = session.AsHost();
                host.IsLocked = playing;
                host.SetProperty(
                    MultiplayerConstants.PhaseProperty,
                    new SessionProperty(
                        playing ? MultiplayerConstants.PlayingPhase : MultiplayerConstants.LobbyPhase,
                        VisibilityPropertyOptions.Member));

                await SaveHostPropertiesWithRetryAsync(host, session);
                if (_ending || !ReferenceEquals(session, _session))
                {
                    throw new InvalidOperationException(
                        "The session changed before the game state was saved.");
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
            CancelHostMigrationFallback();

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
            CancelHostMigrationFallback();

            _session = session ?? throw new ArgumentNullException(nameof(session));
            _originalHostId = session.Host;
            _ending = false;

            session.Changed += OnSessionChanged;
            session.PlayerJoined += OnPlayerJoined;
            session.PlayerHasLeft += OnPlayerHasLeft;
            session.PlayerPropertiesChanged += OnSessionChanged;
            session.SessionPropertiesChanged += OnSessionChanged;
            session.SessionHostChanged += OnSessionHostChanged;
            session.Deleted += OnSessionDeleted;
            session.RemovedFromSession += OnRemovedFromSession;

            RebuildSnapshot();
            if (session.IsHost)
            {
                RunSeatAssignment();
            }
        }

        private void DetachSession(ISession session)
        {
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
        }

        private void OnPlayerJoined(string _)
        {
            RebuildSnapshot();
            if (_session != null && _session.IsHost)
            {
                RunSeatAssignment();
            }
        }

        private void OnPlayerHasLeft(string _)
        {
            var session = _session;
            RebuildSnapshot();

            if (session == null || _ending || !session.IsHost)
            {
                return;
            }

            if (Current.Phase == MultiplayerConstants.PlayingPhase)
            {
                // A normal Leave reaches this event after the departing client has
                // completed its backend removal, so deleting here cannot race that leave.
                EndFromRemote("A player left. The active four-player session is ending.");
                return;
            }

            RunSeatAssignment();
        }

        private void OnSessionHostChanged(string newHostId)
        {
            var session = _session;
            if (_ending ||
                session == null ||
                string.IsNullOrEmpty(_originalHostId) ||
                string.Equals(newHostId, _originalHostId, StringComparison.Ordinal))
            {
                return;
            }

            var currentPlayerId = session.CurrentPlayer?.Id ?? string.Empty;
            if (session.IsHost &&
                string.Equals(newHostId, currentPlayerId, StringComparison.Ordinal))
            {
                CancelHostMigrationFallback();
                EndFromRemote("The host disconnected. The session is closing.");
                return;
            }

            if (_waitingForHostDeletion)
            {
                return;
            }

            _waitingForHostDeletion = true;
            _hostMigrationFallbackSource = new CancellationTokenSource();
            Ended?.Invoke("The host disconnected. Waiting for the session to close.");
            _ = WaitForHostDeletionOrLeaveAsync(
                session,
                _hostMigrationFallbackSource);
        }

        private async Task WaitForHostDeletionOrLeaveAsync(
            ISession observedSession,
            CancellationTokenSource fallbackSource)
        {
            try
            {
                await Task.Delay(
                    HostMigrationDeleteWaitMilliseconds,
                    fallbackSource.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!ReferenceEquals(fallbackSource, _hostMigrationFallbackSource) ||
                !ReferenceEquals(observedSession, _session) ||
                _ending ||
                !_waitingForHostDeletion)
            {
                return;
            }

            _hostMigrationFallbackSource = null;
            _waitingForHostDeletion = false;
            fallbackSource.Dispose();
            EndFromRemote(
                "The replacement host did not close the session. Leaving locally.");
        }

        private void CancelHostMigrationFallback()
        {
            var fallbackSource = _hostMigrationFallbackSource;
            _hostMigrationFallbackSource = null;
            _waitingForHostDeletion = false;

            if (fallbackSource == null)
            {
                return;
            }

            fallbackSource.Cancel();
            fallbackSource.Dispose();
        }



        private void OnSessionDeleted()
        {
            if (!_ending)
            {
                ObserveCompletedRemoteEnd("The host ended the session.");
            }
        }

        private void OnRemovedFromSession()
        {
            if (!_ending)
            {
                ObserveCompletedRemoteEnd("You were removed from the session.");
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
                _ending)
            {
                return;
            }

            var host = observedSession.AsHost();
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

            Current = new SessionSnapshot(
                session.Code,
                session.IsHost,
                phase,
                session.CurrentPlayer.Id,
                players);
            Changed?.Invoke();
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
                "You are not connected to an online session.");
        }

        private void ThrowIfAlreadyInSession()
        {
            if (_session != null)
            {
                throw new InvalidOperationException(
                    "You are already connected to an online session.");
            }
        }

        private void ClearSession()
        {
            CancelHostMigrationFallback();

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
                   buildProperty.Value == Application.version;
        }

        private static async Task EndSessionWithRetryAsync(ISession session)
        {
            var deleteAsHost = session.IsHost;
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
                "MPS has not unregistered the session yet. Try cleanup again.");
        }
    }
}

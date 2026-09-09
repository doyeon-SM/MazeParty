using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    [RequireComponent(typeof(NetworkManager))]
    public sealed class OnlineSessionController : MonoBehaviour
    {
        private const int LobbyDisconnectSettleMilliseconds = 1500;
        private const int LobbyDisconnectForcedCleanupGraceMilliseconds = 5000;
        private const int NetworkIdentityPublishAttempts = 3;
        private const int NetworkIdentityPublishRetryBaseMilliseconds = 250;
        private const string PlayingReconnectTicketPrefix =
            "MazeParty.PlayingReconnectSession.";

        [SerializeField] private Camera lobbyCamera;
        [SerializeField] private Light lobbyLight;
        [SerializeField] private OnlineLobbyView lobbyView;

        private IPlayerIdentityProvider _identity;
        private IOnlineSessionProvider _sessions;
        private NetworkManager _networkManager;
        private NetworkSceneManager _networkSceneManager;
        private string _status = "Create a private session or join with an invite code.";
        private bool _busy;
        private bool _networkTerminationQueued;
        private bool _explicitLeaveQueued;
        private bool _networkIdentityPublished;
        private Task _networkIdentityPublishTask;
        private string _networkIdentityPublishSessionId = string.Empty;
        private readonly HashSet<string> _pendingLobbyCleanupKeys =
            new HashSet<string>();
        private bool _applicationQuitting;
        private bool _destroyed;
        private string _playingReconnectTicketKey;
        private PlayerLocalProfile _localProfile;

        public static OnlineSessionController Instance { get; private set; }

        public int LocalSlot => _sessions != null ? _sessions.Current.LocalSlot : -1;
        public SessionSnapshot CurrentSession =>
            _sessions != null ? _sessions.Current : SessionSnapshot.Empty;
        public PlayerAppearanceState LocalAppearance => _localProfile.Appearance;

        public bool TryResolveAuthoritativeSlot(ulong clientId, out int slot)
        {
            slot = -1;
            return _sessions != null &&
                   _sessions.IsInSession &&
                   _sessions.TryGetAuthoritativeSlot(clientId, out slot);
        }

        public bool TryResolveAuthoritativeDisplayName(ulong clientId, out string displayName)
        {
            displayName = string.Empty;
            if (!TryResolveAuthoritativeSlot(clientId, out var slot))
            {
                return false;
            }

            var players = CurrentSession.Players;
            for (var index = 0; index < players.Count; index++)
            {
                if (players[index].Slot == slot)
                {
                    displayName = PlayerProfilePreferences.SanitizeDisplayName(
                        players[index].DisplayName);
                    return true;
                }
            }
            return false;
        }

        public void ConfigureSceneReferences(
            Camera camera,
            Light light,
            OnlineLobbyView view)
        {
            lobbyCamera = camera;
            lobbyLight = light;
            lobbyView = view;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Application.quitting += OnApplicationQuitting;

            _playingReconnectTicketKey = BuildPlayingReconnectTicketKey();
            _localProfile = PlayerProfilePreferences.Load();
            var displayName = _localProfile.DisplayName;
            if (lobbyView == null)
            {
                lobbyView = FindAnyObjectByType<OnlineLobbyView>();
            }

            lobbyView?.SetDisplayName(displayName);
            lobbyView?.SetAppearance(_localProfile.Appearance);

            _identity = new UnityAnonymousIdentityProvider();
            _sessions = new MpsRelaySessionProvider(_identity);
            _sessions.Changed += OnSessionChanged;
            _sessions.Ended += OnSessionEnded;

            _networkManager = GetComponent<NetworkManager>();
            if (_networkManager == null)
            {
                Debug.LogError("OnlineSessionController requires a NetworkManager.");
                enabled = false;
                RenderLobby();
                return;
            }

            _networkManager.OnClientConnectedCallback += OnClientConnected;
            _networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            _networkManager.OnClientStopped += OnClientStopped;
            ObserveNetworkSceneManager(_networkManager.SceneManager);

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            BindLobbyView();
            RenderLobby();

            if (TryGetPlayingReconnectTicket(out var reconnectSessionId))
            {
                RunAsync(
                    () => ReconnectAndPublishAsync(reconnectSessionId, displayName),
                    "Reconnecting to the interrupted online game...");
            }
        }

        private void OnDestroy()
        {
            _destroyed = true;
            Application.quitting -= OnApplicationQuitting;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            UnbindLobbyView();

            if (_networkSceneManager != null)
            {
                _networkSceneManager.OnLoadEventCompleted -= OnNetworkLoadEventCompleted;
                _networkSceneManager = null;
            }

            if (_networkManager != null)
            {
                _networkManager.OnClientConnectedCallback -= OnClientConnected;
                _networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
                _networkManager.OnClientStopped -= OnClientStopped;
            }

            if (_sessions != null)
            {
                _sessions.Changed -= OnSessionChanged;
                _sessions.Ended -= OnSessionEnded;
                _sessions.Dispose();
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void BindLobbyView()
        {
            if (lobbyView == null)
            {
                Debug.LogError("OnlineSessionController requires an OnlineLobbyView.");
                return;
            }

            lobbyView.CreateRequested += OnCreateRequested;
            lobbyView.JoinRequested += OnJoinRequested;
            lobbyView.CopyRequested += OnCopyRequested;
            lobbyView.ReadyRequested += OnReadyRequested;
            lobbyView.StartRequested += OnStartRequested;
            lobbyView.LeaveRequested += OnLeaveRequested;
            lobbyView.AppearanceChanged += OnAppearanceChanged;
        }

        private void UnbindLobbyView()
        {
            if (lobbyView == null)
            {
                return;
            }

            lobbyView.CreateRequested -= OnCreateRequested;
            lobbyView.JoinRequested -= OnJoinRequested;
            lobbyView.CopyRequested -= OnCopyRequested;
            lobbyView.ReadyRequested -= OnReadyRequested;
            lobbyView.StartRequested -= OnStartRequested;
            lobbyView.LeaveRequested -= OnLeaveRequested;
            lobbyView.AppearanceChanged -= OnAppearanceChanged;
        }

        private void OnCreateRequested(string displayName)
        {
            SaveLocalProfile(displayName, _localProfile.Appearance);
            RunAsync(
                () => CreateAndPublishAsync(displayName),
                "Creating a private Relay session...");
        }

        private void OnJoinRequested(string code, string displayName)
        {
            SaveLocalProfile(displayName, _localProfile.Appearance);
            RunAsync(
                () => JoinAndPublishAsync(code, displayName),
                "Joining the Relay session...");
        }

        private void OnAppearanceChanged(PlayerAppearanceState appearance)
        {
            SaveLocalProfile(_localProfile.DisplayName, appearance.Sanitized());
            var playerObject = _networkManager != null &&
                               _networkManager.SpawnManager != null
                ? _networkManager.SpawnManager.GetLocalPlayerObject()
                : null;
            playerObject?.GetComponent<NetworkPlayerAvatar>()?
                .RequestLocalAppearance(_localProfile.Appearance);
        }

        private void SaveLocalProfile(string displayName, PlayerAppearanceState appearance)
        {
            var safeName = PlayerProfilePreferences.SanitizeDisplayName(displayName);
            _localProfile = new PlayerLocalProfile(safeName, appearance.Sanitized());
            PlayerProfilePreferences.Save(_localProfile.DisplayName, _localProfile.Appearance);
        }

        private void OnCopyRequested()
        {
            if (_sessions == null || !_sessions.IsInSession)
            {
                return;
            }

            GUIUtility.systemCopyBuffer = _sessions.Current.Code;
            SetStatus("Invite code copied to the clipboard.");
        }

        private void OnReadyRequested()
        {
            if (_sessions == null || !_sessions.IsInSession)
            {
                return;
            }

            var ready = !_sessions.Current.LocalReady;
            RunAsync(
                () => _sessions.SetReadyAsync(ready),
                "Saving ready state...");
        }

        private void OnStartRequested()
        {
            RunAsync(StartGameAsync, "Synchronizing the Board scene...");
        }

        private void OnLeaveRequested()
        {
            RunAsync(LeaveSessionAsync, "Leaving the session...");
        }

        private async Task CreateAndPublishAsync(string displayName)
        {
            ClearPlayingReconnectTicket();
            _networkIdentityPublished = false;
            await _sessions.CreateAsync("MazeParty Room", displayName);
            await PublishLocalNetworkClientIdWhenReadyAsync();
        }

        private async Task JoinAndPublishAsync(string code, string displayName)
        {
            ClearPlayingReconnectTicket();
            _networkIdentityPublished = false;
            await _sessions.JoinByCodeAsync(code, displayName);
            await PublishLocalNetworkClientIdWhenReadyAsync();
        }

        private async Task ReconnectAndPublishAsync(string sessionId, string displayName)
        {
            _networkIdentityPublished = false;
            try
            {
                await _sessions.ReconnectToSessionAsync(sessionId, displayName);
                var snapshot = _sessions.Current;
                if (snapshot.IsHost || snapshot.Phase != MultiplayerConstants.PlayingPhase)
                {
                    await _sessions.LeaveAsync();
                    throw new InvalidOperationException(
                        "The saved reconnect target is no longer an active client game.");
                }

                await PublishLocalNetworkClientIdWhenReadyAsync();
            }
            catch
            {
                ClearPlayingReconnectTicket();
                throw;
            }
        }

        private Task PublishLocalNetworkClientIdWhenReadyAsync()
        {
            if (_networkIdentityPublished ||
                _sessions == null ||
                !_sessions.IsInSession ||
                _networkManager == null)
            {
                return Task.CompletedTask;
            }

            var expectedSessionId = _sessions.CurrentSessionId;
            if (string.IsNullOrWhiteSpace(expectedSessionId))
            {
                throw new InvalidOperationException(
                    "The active session ID is not available for network identity publishing.");
            }

            // Create/Join completion and NGO connection callbacks can overlap. Share one
            // per-session task so they never race the same MPS player-property save.
            if (_networkIdentityPublishTask != null &&
                _networkIdentityPublishSessionId == expectedSessionId)
            {
                return _networkIdentityPublishTask;
            }

            _networkIdentityPublishSessionId = expectedSessionId;
            _networkIdentityPublishTask =
                PublishLocalNetworkClientIdTrackedAsync(expectedSessionId);
            return _networkIdentityPublishTask;
        }

        private async Task PublishLocalNetworkClientIdTrackedAsync(
            string expectedSessionId)
        {
            // Yield once so the shared task field is assigned before this method can
            // complete synchronously and clear it.
            await Task.Yield();
            try
            {
                await PublishLocalNetworkClientIdCoreAsync(expectedSessionId);
            }
            finally
            {
                if (_networkIdentityPublishSessionId == expectedSessionId)
                {
                    _networkIdentityPublishTask = null;
                    _networkIdentityPublishSessionId = string.Empty;
                }
            }
        }

        private async Task PublishLocalNetworkClientIdCoreAsync(
            string expectedSessionId)
        {
            for (var readinessAttempt = 0; readinessAttempt < 40; readinessAttempt++)
            {
                if (!IsExpectedSession(expectedSessionId))
                {
                    return;
                }

                if (_networkManager.IsListening &&
                    (_networkManager.IsClient || _networkManager.IsHost))
                {
                    // This is an outer retry around the provider save. It complements
                    // provider-level conflict handling and reduces missing NGO mappings
                    // when the service has a transient write failure.
                    for (var saveAttempt = 0;
                         saveAttempt < NetworkIdentityPublishAttempts;
                         saveAttempt++)
                    {
                        if (!IsExpectedSession(expectedSessionId))
                        {
                            return;
                        }

                        try
                        {
                            await _sessions.PublishLocalNetworkClientIdAsync(
                                _networkManager.LocalClientId);
                            if (IsExpectedSession(expectedSessionId))
                            {
                                _networkIdentityPublished = true;
                            }

                            return;
                        }
                        catch (Exception) when (
                            saveAttempt < NetworkIdentityPublishAttempts - 1)
                        {
                            await Task.Delay(
                                NetworkIdentityPublishRetryBaseMilliseconds *
                                (saveAttempt + 1));
                        }
                    }
                }

                await Task.Delay(50);
            }

            if (IsExpectedSession(expectedSessionId))
            {
                throw new InvalidOperationException(
                    "The local network player ID was not ready in time.");
            }
        }

        private async Task LeaveSessionAsync()
        {
            ClearPlayingReconnectTicket();
            _explicitLeaveQueued = true;
            try
            {
                await _sessions.LeaveAsync();
            }
            finally
            {
                _explicitLeaveQueued = false;
                ClearPlayingReconnectTicket();
            }
        }

        private async Task StartGameAsync()
        {
            var snapshot = _sessions.Current;
            if (!snapshot.IsHost || !snapshot.CanStart)
            {
                throw new InvalidOperationException(
                    "All four players must be ready.");
            }

            var manager = _networkManager != null
                ? _networkManager
                : NetworkManager.Singleton;
            if (manager == null || !manager.IsHost || manager.SceneManager == null)
            {
                throw new InvalidOperationException(
                    "The host NetworkManager is not ready.");
            }

            ObserveNetworkSceneManager(manager.SceneManager);
            if (!HasExactlyFourAssignedNetworkPlayers(manager))
            {
                throw new InvalidOperationException(
                    "Four connected players with server-assigned seats are required.");
            }

            await _sessions.SetPlayingAsync(true);

            var result = manager.SceneManager.LoadScene(
                MultiplayerConstants.BoardScene,
                LoadSceneMode.Additive);

            if (result != SceneEventProgressStatus.Started)
            {
                await _sessions.SetPlayingAsync(false);
                throw new InvalidOperationException(
                    "Could not start Board scene synchronization: " + result);
            }

            SetStatus("Waiting for every player to finish loading the Board scene.");
        }

        private async void RunAsync(Func<Task> operation, string progress)
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            SetStatus(progress);
            try
            {
                await operation();
                if (_sessions.IsInSession)
                {
                    _status = _sessions.Current.Phase == MultiplayerConstants.PlayingPhase
                        ? "Connected to the online game."
                        : "Connected to the online lobby.";
                }
                else
                {
                    _status = "Online session disconnected.";
                }
            }
            catch (Exception exception)
            {
                _status = exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                _busy = false;
                RenderLobby();
            }
        }

        private void OnSessionChanged()
        {
            UpdatePlayingReconnectTicket();
            if (_networkManager != null)
            {
                ObserveNetworkSceneManager(_networkManager.SceneManager);
            }

            if (!_sessions.IsInSession)
            {
                _networkIdentityPublished = false;
                UnloadBoardLocally();
            }

            RenderLobby();
        }

        private void OnSessionEnded(string reason)
        {
            ClearPlayingReconnectTicket();
            SetStatus(reason);
        }

        private static bool HasExactlyFourAssignedNetworkPlayers(NetworkManager manager)
        {
            if (manager.SpawnManager == null ||
                manager.ConnectedClientsIds.Count != MultiplayerConstants.MaxPlayers)
            {
                return false;
            }

            var assignedMask = 0;
            foreach (var clientId in manager.ConnectedClientsIds)
            {
                var playerObject = manager.SpawnManager.GetPlayerNetworkObject(clientId);
                var avatar = playerObject != null
                    ? playerObject.GetComponent<NetworkPlayerAvatar>()
                    : null;
                var slot = avatar != null ? avatar.AssignedSlot : -1;
                if (slot < 0 || slot >= MultiplayerConstants.MaxPlayers)
                {
                    return false;
                }

                var slotBit = 1 << slot;
                if ((assignedMask & slotBit) != 0)
                {
                    return false;
                }

                assignedMask |= slotBit;
            }

            return assignedMask == (1 << MultiplayerConstants.MaxPlayers) - 1;
        }

        private void ObserveNetworkSceneManager(NetworkSceneManager sceneManager)
        {
            if (ReferenceEquals(_networkSceneManager, sceneManager))
            {
                return;
            }

            if (_networkSceneManager != null)
            {
                _networkSceneManager.OnLoadEventCompleted -= OnNetworkLoadEventCompleted;
            }

            _networkSceneManager = sceneManager;
            if (_networkSceneManager != null)
            {
                _networkSceneManager.OnLoadEventCompleted += OnNetworkLoadEventCompleted;
            }
        }

        private void OnNetworkLoadEventCompleted(
            string sceneName,
            LoadSceneMode _,
            List<ulong> clientsCompleted,
            List<ulong> clientsTimedOut)
        {
            if (sceneName != MultiplayerConstants.BoardScene ||
                _networkManager == null ||
                !_networkManager.IsHost ||
                _sessions == null ||
                !_sessions.IsInSession)
            {
                return;
            }

            var completedCount = clientsCompleted != null ? clientsCompleted.Count : 0;
            var timedOutCount = clientsTimedOut != null ? clientsTimedOut.Count : 0;
            if (timedOutCount > 0 ||
                completedCount != MultiplayerConstants.MaxPlayers ||
                !HasExactlyFourAssignedNetworkPlayers(_networkManager))
            {
                EndSessionAfterNetworkFailure(
                    "Four-player Board synchronization failed. The session is ending.");
                return;
            }

            var matchState = NetworkMatchState.Instance;
            if (matchState == null || !matchState.IsSpawned)
            {
                EndSessionAfterNetworkFailure(
                    "The Board network state was not created. The session is ending.");
                return;
            }

            matchState.EnableGameplayOnServer();
            SetStatus("All four players loaded the Board. Gameplay input is enabled.");
        }

        private void OnClientConnected(ulong clientId)
        {
            if (_applicationQuitting ||
                _sessions == null ||
                !_sessions.IsInSession ||
                _networkManager == null ||
                clientId != _networkManager.LocalClientId)
            {
                return;
            }

            _ = PublishLocalNetworkClientIdSafelyAsync();
        }

        private async Task PublishLocalNetworkClientIdSafelyAsync()
        {
            try
            {
                await PublishLocalNetworkClientIdWhenReadyAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not publish the NGO client ID to the MPS member: " +
                    exception.Message);
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (_applicationQuitting ||
                _explicitLeaveQueued ||
                _networkManager == null ||
                _sessions == null ||
                !_sessions.IsInSession)
            {
                return;
            }

            var remoteClientLost =
                _networkManager.IsServer &&
                clientId != NetworkManager.ServerClientId;
            var localClientLost =
                !_networkManager.IsServer &&
                clientId == _networkManager.LocalClientId;

            if (remoteClientLost)
            {
                if (_sessions.Current.Phase == MultiplayerConstants.LobbyPhase)
                {
                    // Capture the service session identity now. The delayed cleanup must
                    // never target a replacement session that reuses the NGO client ID.
                    var expectedSessionId = _sessions.CurrentSessionId;
                    QueueDisconnectedLobbyPlayerCleanup(
                        expectedSessionId,
                        clientId);
                }
                else
                {
                    NetworkMatchState.Instance?.PauseForReconnectOnServer(clientId);
                    SetStatus(
                        "A player disconnected. Gameplay is paused for the 60-second reconnect window.");
                }
            }
            else if (localClientLost)
            {
                SetStatus(
                    "Relay connection lost. Waiting for the host or session service.");
            }
        }

        private void OnClientStopped(bool _)
        {
            if (_applicationQuitting ||
                _explicitLeaveQueued ||
                _networkManager == null ||
                _networkManager.IsServer ||
                _sessions == null ||
                !_sessions.IsInSession)
            {
                return;
            }

            SetStatus(
                "Relay connection stopped. Waiting for the host or session service.");
        }

        private void QueueDisconnectedLobbyPlayerCleanup(
            string expectedSessionId,
            ulong clientId)
        {
            if (!IsExpectedLobbySession(expectedSessionId) ||
                _networkManager == null ||
                !_networkManager.IsServer)
            {
                return;
            }

            var cleanupKey = expectedSessionId + "|" + clientId;
            if (!_pendingLobbyCleanupKeys.Add(cleanupKey))
            {
                return;
            }

            _ = RemoveDisconnectedLobbyPlayerAfterSettleAsync(
                expectedSessionId,
                clientId,
                cleanupKey);
        }


        private async Task RemoveDisconnectedLobbyPlayerAfterSettleAsync(
            string expectedSessionId,
            ulong clientId,
            string cleanupKey)
        {
            try
            {
                await Task.Delay(LobbyDisconnectSettleMilliseconds);
                if (!CanRemoveDisconnectedLobbyPlayer(
                        expectedSessionId,
                        clientId))
                {
                    return;
                }

                var removed = false;
                try
                {
                    removed = await _sessions.RemoveDisconnectedLobbyPlayerAsync(
                        expectedSessionId,
                        clientId,
                        honorLeaveMarker: true);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "Initial disconnected lobby cleanup failed; " +
                        "the bounded grace retry remains scheduled: " +
                        exception.Message);
                }

                if (removed)
                {
                    if (IsExpectedLobbySession(expectedSessionId))
                    {
                        SetStatus(
                            "A disconnected lobby player was removed and the seat is open.");
                    }

                    return;
                }

                // A normal Leave may have published its marker but not completed yet.
                // Wait outside the provider mutation gate, then perform one bounded
                // fallback that ignores a stale marker.
                await Task.Delay(LobbyDisconnectForcedCleanupGraceMilliseconds);
                if (!CanRemoveDisconnectedLobbyPlayer(
                        expectedSessionId,
                        clientId))
                {
                    return;
                }

                try
                {
                    removed = await _sessions.RemoveDisconnectedLobbyPlayerAsync(
                        expectedSessionId,
                        clientId,
                        honorLeaveMarker: false);
                    if (removed && IsExpectedLobbySession(expectedSessionId))
                    {
                        SetStatus(
                            "A stale disconnected lobby player was removed after the grace period.");
                    }
                }
                catch (Exception exception)
                {
                    if (IsExpectedLobbySession(expectedSessionId))
                    {
                        SetStatus(
                            "Could not remove the disconnected lobby player: " +
                            exception.Message);
                        Debug.LogWarning(_status);
                    }
                }
            }
            finally
            {
                _pendingLobbyCleanupKeys.Remove(cleanupKey);
            }
        }

        private bool IsExpectedSession(string expectedSessionId)
        {
            return
                !_destroyed &&
                !_applicationQuitting &&
                !_explicitLeaveQueued &&
                !string.IsNullOrWhiteSpace(expectedSessionId) &&
                _sessions != null &&
                _sessions.IsInSession &&
                string.Equals(
                    _sessions.CurrentSessionId,
                    expectedSessionId,
                    StringComparison.Ordinal);
        }

        private bool IsExpectedLobbySession(string expectedSessionId)
        {
            return
                IsExpectedSession(expectedSessionId) &&
                _sessions.Current.IsHost &&
                _sessions.Current.Phase == MultiplayerConstants.LobbyPhase;
        }

        private bool CanRemoveDisconnectedLobbyPlayer(
            string expectedSessionId,
            ulong clientId)
        {
            return
                IsExpectedLobbySession(expectedSessionId) &&
                _networkManager != null &&
                _networkManager.IsServer &&
                clientId != NetworkManager.ServerClientId &&
                !IsNetworkClientConnected(clientId);
        }

        private bool IsNetworkClientConnected(ulong clientId)
        {
            if (_networkManager == null)
            {
                return false;
            }

            foreach (var connectedClientId in _networkManager.ConnectedClientsIds)
            {
                if (connectedClientId == clientId)
                {
                    return true;
                }
            }

            return false;
        }

        public void EndActiveMatchForNetworkFailure(string reason)
        {
            EndSessionAfterNetworkFailure(reason);
        }

        private async void EndSessionAfterNetworkFailure(string reason)
        {
            if (_networkTerminationQueued ||
                _sessions == null ||
                !_sessions.IsInSession)
            {
                return;
            }

            _networkTerminationQueued = true;
            ClearPlayingReconnectTicket();
            SetStatus(reason);
            try
            {
                // MPS owns NGO lifecycle. Never call NetworkManager.Shutdown directly.
                await _sessions.LeaveAsync();
            }
            catch (Exception exception)
            {
                SetStatus(reason + " Cleanup needs a retry: " + exception.Message);
                Debug.LogWarning(_status);
            }
            finally
            {
                _networkTerminationQueued = false;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == MultiplayerConstants.BoardScene)
            {
                SetLobbyRendering(false);
            }
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (scene.name == MultiplayerConstants.BoardScene)
            {
                SetLobbyRendering(true);
            }
        }

        private void SetLobbyRendering(bool enabled)
        {
            if (lobbyCamera != null)
            {
                lobbyCamera.enabled = enabled;

                var audioListener = lobbyCamera.GetComponent<AudioListener>();
                if (audioListener != null)
                {
                    audioListener.enabled = enabled;
                }
            }

            if (lobbyLight != null)
            {
                lobbyLight.enabled = enabled;
            }
        }

        private void UnloadBoardLocally()
        {
            var minefield = SceneManager.GetSceneByName(MultiplayerConstants.MinefieldScene);
            if (minefield.IsValid() && minefield.isLoaded)
            {
                SceneManager.UnloadSceneAsync(minefield);
            }

            var board = SceneManager.GetSceneByName(MultiplayerConstants.BoardScene);
            if (board.IsValid() && board.isLoaded)
            {
                SceneManager.UnloadSceneAsync(board);
            }
            else
            {
                SetLobbyRendering(true);
            }
        }

        private void SetStatus(string status)
        {
            _status = status ?? string.Empty;
            RenderLobby();
        }

        private void RenderLobby()
        {
            if (lobbyView == null)
            {
                return;
            }

            var inSession = _sessions != null && _sessions.IsInSession;
            var snapshot = inSession ? _sessions.Current : SessionSnapshot.Empty;
            lobbyView.Render(snapshot, inSession, _busy, _status);
        }

        private void OnApplicationQuitting()
        {
            // MPS owns its Application.quitting LeaveAsync. Suppress controller callbacks
            // so a second project-side Leave cannot stop the same network session twice.
            _applicationQuitting = true;
            // MPS treats graceful application quit as a normal Leave. Keep the
            // reconnect ticket only for a process/network loss that skips this callback.
            ClearPlayingReconnectTicket();
        }

        private void UpdatePlayingReconnectTicket()
        {
            if (!_applicationQuitting &&
                !_explicitLeaveQueued &&
                !_networkTerminationQueued &&
                _sessions != null &&
                _sessions.IsInSession &&
                !_sessions.Current.IsHost &&
                _sessions.Current.Phase == MultiplayerConstants.PlayingPhase &&
                !string.IsNullOrWhiteSpace(_sessions.CurrentSessionId))
            {
                PlayerPrefs.SetString(
                    _playingReconnectTicketKey,
                    _sessions.CurrentSessionId);
                PlayerPrefs.Save();
                return;
            }

            ClearPlayingReconnectTicket();
        }

        private bool TryGetPlayingReconnectTicket(out string sessionId)
        {
            sessionId = string.IsNullOrWhiteSpace(_playingReconnectTicketKey)
                ? string.Empty
                : PlayerPrefs.GetString(_playingReconnectTicketKey, string.Empty).Trim();
            return sessionId.Length > 0;
        }

        private void ClearPlayingReconnectTicket()
        {
            if (string.IsNullOrWhiteSpace(_playingReconnectTicketKey) ||
                !PlayerPrefs.HasKey(_playingReconnectTicketKey))
            {
                return;
            }

            PlayerPrefs.DeleteKey(_playingReconnectTicketKey);
            PlayerPrefs.Save();
        }

        private static string BuildPlayingReconnectTicketKey()
        {
            var profile = "default";
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(
                        arguments[index],
                        "-auth-profile",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = arguments[index + 1].Trim();
                if (candidate.Length > 0)
                {
                    profile = candidate;
                }
                break;
            }

            return PlayingReconnectTicketPrefix + Hash128.Compute(profile);
        }

        // TODO(STEAM-LIFECYCLE): Steam lobby callbacks should forward host departure to
        // IOnlineSessionProvider.Ended. The fixed-four match ends instead of migrating
        // hosts. A Steam transport must preserve the same client/member identity mapping.
    }
}


using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    internal enum BoardMapStartupDisposition : byte
    {
        WaitForReadiness = 0,
        Ready = 1,
        FailMatch = 2
    }

    internal static class BoardMapStartupPolicy
    {
        public static BoardMapStartupDisposition Evaluate(
            bool mapReady,
            bool allPlayersReady,
            string permanentFailure)
        {
            if (!string.IsNullOrWhiteSpace(permanentFailure))
            {
                return BoardMapStartupDisposition.FailMatch;
            }

            return mapReady && allPlayersReady
                ? BoardMapStartupDisposition.Ready
                : BoardMapStartupDisposition.WaitForReadiness;
        }
    }

    [RequireComponent(typeof(NetworkManager))]
    public sealed partial class OnlineSessionController : MonoBehaviour
    {
        private const int LobbyDisconnectSettleMilliseconds = 1500;
        private const int LobbyDisconnectForcedCleanupGraceMilliseconds = 5000;
        private const int NetworkIdentityPublishAttempts = 3;
        private const int NetworkIdentityPublishRetryBaseMilliseconds = 250;
        private const double CompletedMatchUnloadClientGraceSeconds = 15d;
        private const int CompletedMatchPhaseSaveFailureLimit = 3;
        private const int CompletedMatchPhaseSaveRetryMilliseconds = 1000;
        private const double CompletedMatchFailClosedRetrySeconds = 1d;
        private const int CompletedMatchFailClosedTerminationAttemptLimit = 3;
        private const int CompletedMatchFailClosedLeaveTimeoutMilliseconds = 10000;
        private const double CompletedMatchSceneOperationFallbackTimeoutSeconds = 120d;

        private const double VoluntaryLeaveAckTimeoutSeconds = 3d;
        private const double VoluntaryLeaveAnnouncementFlushSeconds = 0.35d;
        private const int VoluntaryLeaveSessionEndDelayMilliseconds = 350;
        private const int LocalReadyResetAttemptsPerBatch = 3;
        private const int LocalReadyResetRetryBaseMilliseconds = 500;
        private const double LocalReadyResetBatchCooldownSeconds = 5d;
        private const string PlayingReconnectTicketPrefix =
            "MazeParty.PlayingReconnectSession.";

        [SerializeField] private Camera lobbyCamera;
        [SerializeField] private Light lobbyLight;
        [SerializeField] private OnlineLobbyView lobbyView;

        private IPlayerIdentityProvider _identity;
        private IOnlineSessionProvider _sessions;
        private NetworkManager _networkManager;
        private NetworkSceneManager _networkSceneManager;
        private string _status = string.Empty;
        private LocalizedMessage _localizedStatus = new LocalizedMessage(string.Empty);
        private bool _statusUsesLocalization;
        private readonly SessionOperationCoordinator _sessionOperations =
            new SessionOperationCoordinator();
        private bool _networkTerminationRequested;
        private bool _networkIdentityPublished;
        private Task _networkIdentityPublishTask;
        private string _networkIdentityPublishSessionId = string.Empty;
        private readonly HashSet<string> _pendingLobbyCleanupKeys =
            new HashSet<string>();
        private readonly Dictionary<string, Task> _pendingLobbyCleanupTasks =
            new Dictionary<string, Task>();
        private bool _applicationQuitting;
        private bool _destroyed;
        private string _playingReconnectTicketKey;
        private PlayerLocalProfile _localProfile;
        private readonly Queue<string> _completedMatchSceneUnloadQueue =
            new Queue<string>();
        private readonly HashSet<ulong> _completedMatchPendingUnloadClients =
            new HashSet<ulong>();
        private readonly HashSet<ulong> _completedMatchDisconnectedClients =
            new HashSet<ulong>();
        private readonly HashSet<ulong> _reconnectGraceDisconnectedClients =
            new HashSet<ulong>();
        private readonly Dictionary<int, ulong> _reconnectGraceClientBySlot =
            new Dictionary<int, ulong>();
        private bool _completedMatchLobbyReturnInProgress;
        private bool _voidedMatchLobbyReturnPending;
        private string _voidedMatchLobbyReturnReason = string.Empty;
        private bool _completedMatchAvatarsPrepared;
        private bool _completedMatchUnloadNextFrame;
        private int _completedMatchUnloadEarliestFrame;
        private string _completedMatchSceneBeingUnloaded = string.Empty;
        private string _completedMatchPendingUnloadScene = string.Empty;
        private double _completedMatchPendingUnloadDeadline;
        private int _completedMatchPhaseSaveFailureCount;
        private double _completedMatchSceneOperationDeadline;
        private bool _completedMatchFailClosedTerminationPending;
        private string _completedMatchFailClosedReason = string.Empty;
        private double _completedMatchFailClosedRetryAt;
        private int _completedMatchFailClosedTerminationAttemptCount;
        private bool _completedMatchFailClosedCallTimedOut;
        private bool _completedMatchFailClosedExhaustedStatusShown;

        private bool _voluntaryLeavePending;
        private double _voluntaryLeaveDeadline;
        private double _voluntaryLeaveAcknowledgedAt = -1d;
        private bool _observedPlayingPhase;
        private bool _localReadyResetQueued;
        private bool _localReadyResetRequired;
        private double _localReadyResetRetryAt;
        private bool _pendingBoardLoadReconnect;
        private bool _pendingBoardLoadVoidRequested;
        private double _pendingBoardLoadReconnectEndsAt;
        private readonly Queue<string> _pendingNotices = new Queue<string>();
        private bool _ceremonyWaitingRoomShown;
        private bool _ceremonyWaitingRoomStatusShown;

        public static OnlineSessionController Instance { get; private set; }

        /// <summary>Raised when a message for the common notice popup is queued.</summary>
        public event Action NoticeQueued;

        public int LocalSlot => _sessions != null ? _sessions.Current.LocalSlot : -1;
        public SessionSnapshot CurrentSession =>
            _sessions != null ? _sessions.Current : SessionSnapshot.Empty;
        public PlayerAppearanceState LocalAppearance => _localProfile.Appearance;
        public bool IsInSession => _sessions != null && _sessions.IsInSession;
        public bool IsBusy => _sessionOperations.IsBusy;
        public SessionLifecycleState LifecycleState => _sessionOperations.State;
        public bool IsVoluntaryLeavePending => _voluntaryLeavePending;
        public bool IsBoardMapSelectionLocked =>
            _matchRecoveryChoiceVisible || ShouldResumeSavedMatch;

        public bool TryGetSelectedBoardMap(out BoardMapSelection selection)
        {
            selection = BoardMapSelection.Legacy;
            if (_sessions == null || !_sessions.IsInSession)
            {
                return false;
            }

            var requested = _sessions.Current.BoardMapSelection;
            if (!BoardMapSelection.TryCreate(
                    requested.MapId,
                    requested.ContentVersion,
                    out selection))
            {
                selection = BoardMapSelection.Legacy;
                return false;
            }

            return true;
        }

        /// <summary>
        /// True after the local player pressed "clean up board" while other
        /// players may still be at the award ceremony. The player sees the
        /// waiting room and may leave the room at any time. The Board scene
        /// itself stays loaded until the whole room returns, because NGO
        /// synchronizes scene unloads for every client at once.
        /// </summary>
        public bool IsBackInWaitingRoomDuringCeremony
        {
            get
            {
                if (!IsInSession)
                {
                    return false;
                }

                var match = NetworkMatchState.Instance;
                if (match == null || !match.IsSpawned)
                {
                    return false;
                }

                var avatar = GetLocalAvatar();
                return avatar != null &&
                       match.IsBackInWaitingRoomDuringCeremony(avatar.AssignedSlot);
            }
        }

        /// <summary>Where the local player is; drives the common menu.</summary>
        public GameMenuContext MenuContext
        {
            get
            {
                if (!IsInSession)
                {
                    return GameMenuContext.Lobby;
                }

                if (IsBackInWaitingRoomDuringCeremony)
                {
                    return GameMenuContext.WaitingRoom;
                }

                var match = NetworkMatchState.Instance;
                if ((match != null && match.IsSpawned) ||
                    _sessions.Current.Phase == MultiplayerConstants.PlayingPhase)
                {
                    return GameMenuContext.InGame;
                }

                return GameMenuContext.WaitingRoom;
            }
        }

        public bool TryDequeueNotice(out string notice)
        {
            if (_pendingNotices.Count > 0)
            {
                notice = _pendingNotices.Dequeue();
                return true;
            }

            notice = string.Empty;
            return false;
        }

        /// <summary>
        /// Fingerprint of the account IDs in the current room, used to reuse a
        /// saved minigame schedule only when exactly the same players restart.
        /// </summary>
        public string GetMatchRosterKey()
        {
            if (_sessions == null || !_sessions.IsInSession)
            {
                return string.Empty;
            }

            var players = _sessions.Current.Players;
            var playerIds = new List<string>(players.Count);
            for (var index = 0; index < players.Count; index++)
            {
                playerIds.Add(players[index].PlayerId);
            }

            return MinigameScheduleRoster.CreateKey(playerIds);
        }

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
            _sessionOperations.BecameIdle += OnSessionOperationsBecameIdle;
            _sessionOperations.StateChanged += TraceSessionTransition;
            Application.quitting += OnApplicationQuitting;

            _playingReconnectTicketKey = BuildPlayingReconnectTicketKey();
            _localProfile = PlayerProfilePreferences.Load();
            var displayName = _localProfile.DisplayName;
            LobbyArena.EnsureRuntimeCreated(lobbyCamera);

            lobbyView?.SetDisplayName(displayName);
            lobbyView?.SetAppearance(_localProfile.Appearance);

            _identity = new UnityAnonymousIdentityProvider();
            _sessions = new MpsRelaySessionProvider(_identity);
            _sessions.KeepRoomOnPlayingDeparture = ShouldKeepRoomOnPlayingDeparture;
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
                    GameText.N("Reconnecting to the interrupted online game..."),
                    SessionLifecycleState.Reconnecting);
            }
        }

        private void Update()
        {
            if (_sessions != null &&
                _sessions.IsInSession &&
                _sessions.Current.Phase == MultiplayerConstants.PlayingPhase)
            {
                _observedPlayingPhase = true;
            }

            AdvanceVoluntaryMatchLeave();
            RefreshCeremonyWaitingRoomPresentation();
            ResolveReconnectedGraceClients();
            AdvanceLocalReadyReset();
            AdvancePendingBoardLoadReconnect();
            TryAdvanceVoidedMatchLobbyReturn();
            TryAdvanceCompletedMatchFailClosedTermination();
            if (_completedMatchFailClosedTerminationPending)
            {
                return;
            }

            var now = Time.realtimeSinceStartupAsDouble;
            if (_completedMatchLobbyReturnInProgress &&
                CompletedMatchReturnRules.HasSceneUnloadTimedOut(
                    _completedMatchSceneOperationDeadline,
                    now))
            {
                BeginCompletedMatchFailClosedTermination(GameText.F(
                    "Could not return the completed match to the lobby: {0}",
                    "Scene synchronization timed out."));
                return;
            }

            if (_completedMatchLobbyReturnInProgress &&
                _completedMatchPendingUnloadClients.Count > 0 &&
                _completedMatchPendingUnloadDeadline > 0d &&
                now >= _completedMatchPendingUnloadDeadline)
            {
                ResolveCompletedMatchUnloadTimeout();
            }

            if (!_completedMatchLobbyReturnInProgress ||
                !_completedMatchUnloadNextFrame ||
                Time.frameCount < _completedMatchUnloadEarliestFrame)
            {
                return;
            }

            _completedMatchUnloadNextFrame = false;
            if (!_completedMatchAvatarsPrepared)
            {
                var manager = _networkManager != null
                    ? _networkManager
                    : NetworkManager.Singleton;
                EnsureCompletedMatchSceneOperationDeadline(manager);
                if (!TryPrepareConnectedAvatarsForLobbyOnServer(manager))
                {
                    SetLocalizedStatus(
                        "Waiting for every connected player avatar before lobby return.");
                    ScheduleCompletedMatchUnloadForNextFrame();
                    return;
                }

                _completedMatchSceneOperationDeadline = 0d;
                _completedMatchAvatarsPrepared = true;
            }

            TryBeginNextCompletedMatchSceneUnload();
        }

        private void OnDestroy()
        {
            _destroyed = true;
            _sessionOperations.BecameIdle -= OnSessionOperationsBecameIdle;
            ResetCompletedMatchLobbyReturnState();
            Application.quitting -= OnApplicationQuitting;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            UnbindLobbyView();

            if (_networkSceneManager != null)
            {
                _networkSceneManager.OnLoadEventCompleted -= OnNetworkLoadEventCompleted;
                _networkSceneManager.OnUnloadEventCompleted -=
                    OnNetworkUnloadEventCompleted;
                _networkSceneManager.OnUnloadComplete -=
                    OnNetworkUnloadComplete;
                _networkSceneManager = null;
            }

            if (_networkManager != null)
            {
                _networkManager.OnClientConnectedCallback -= OnClientConnected;
                _networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
                _networkManager.OnClientStopped -= OnClientStopped;
            }

            var shutdownTasks = new List<Task>(_pendingLobbyCleanupTasks.Values);
            if (_networkIdentityPublishTask != null)
            {
                shutdownTasks.Add(_networkIdentityPublishTask);
            }

            if (_sessions != null)
            {
                _sessions.Changed -= OnSessionChanged;
                _sessions.Ended -= OnSessionEnded;
            }

            _sessionOperations.BeginShutdown(_sessions, shutdownTasks);
            _sessionOperations.StateChanged -= TraceSessionTransition;

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
            lobbyView.MapSelectionDeltaRequested +=
                OnMapSelectionDeltaRequested;
            lobbyView.RecoveryContinueRequested += OnRecoveryContinueRequested;
            lobbyView.RecoveryDiscardRequested += OnRecoveryDiscardRequested;
            lobbyView.AppearanceChanged += OnAppearanceChanged;
            GameText.LanguageChanged += RenderLobby;
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
            lobbyView.MapSelectionDeltaRequested -=
                OnMapSelectionDeltaRequested;
            lobbyView.RecoveryContinueRequested -= OnRecoveryContinueRequested;
            lobbyView.RecoveryDiscardRequested -= OnRecoveryDiscardRequested;
            lobbyView.AppearanceChanged -= OnAppearanceChanged;
            GameText.LanguageChanged -= RenderLobby;
        }

        private void OnCreateRequested(string displayName)
        {
            SaveLocalProfile(displayName, _localProfile.Appearance);
            RunAsync(
                () => CreateAndPublishAsync(displayName),
                GameText.N("Creating a private Relay session..."),
                SessionLifecycleState.Connecting);
        }

        private void OnJoinRequested(string code, string displayName)
        {
            SaveLocalProfile(displayName, _localProfile.Appearance);
            RunAsync(
                () => JoinAndPublishAsync(code, displayName),
                GameText.N("Joining the Relay session..."),
                SessionLifecycleState.Connecting);
        }

        private void OnAppearanceChanged(PlayerAppearanceState appearance)
        {
            var playerObject = _networkManager != null &&
                               _networkManager.SpawnManager != null
                ? _networkManager.SpawnManager.GetLocalPlayerObject()
                : null;
            var avatar = playerObject != null
                ? playerObject.GetComponent<NetworkPlayerAvatar>()
                : null;
            if (avatar != null)
            {
                avatar.RequestLocalAppearance(appearance);
            }
            else
            {
                AcceptAuthoritativeAppearance(appearance);
            }
        }

        public void AcceptAuthoritativeAppearance(PlayerAppearanceState appearance)
        {
            SaveLocalProfile(_localProfile.DisplayName, appearance.Sanitized());
            lobbyView?.SetAppearance(_localProfile.Appearance);
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
            SetLocalizedStatus("Invite code copied to the clipboard.");
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
                GameText.N("Saving ready state..."),
                SessionLifecycleState.Lobby);
        }

        private void OnMapSelectionDeltaRequested(int delta)
        {
            if (delta == 0 || !CanChangeBoardMapSelection())
            {
                return;
            }

            RunAsync(
                () => ChangeBoardMapSelectionAsync(delta),
                GameText.N("Saving board map selection..."),
                SessionLifecycleState.Lobby);
        }

        public void RequestCompletedMatchReturn()
        {
            RunAsync(
                RequestCompletedMatchReturnAsync,
                GameText.N("Saving the return-to-lobby request..."),
                SessionLifecycleState.ReturningToLobby);
        }

        private void OnStartRequested()
        {
            RunAsync(
                StartGameAsync,
                GameText.N("Synchronizing the Board scene..."),
                SessionLifecycleState.StartingMatch);
        }

        private void OnLeaveRequested()
        {
            RunAsync(
                LeaveSessionAsync,
                GameText.N("Leaving the session..."),
                SessionLifecycleState.Leaving);
        }

        private void OnQuitRequested()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Common menu, lobby context: quits the application.</summary>
        public void RequestQuitGame()
        {
            OnQuitRequested();
        }

        /// <summary>Common menu, waiting-room context: leaves the room.</summary>
        public void RequestLeaveWaitingRoom()
        {
            OnLeaveRequested();
        }

        /// <summary>
        /// Common menu, in-game context (after confirmation). The server
        /// announces who ended the match and returns the other players to the
        /// ready screen; this client leaves the session after the announcement
        /// is acknowledged or a short timeout. Once the final ranking is locked
        /// the server only acknowledges this player and the others keep their
        /// ceremony.
        /// </summary>
        public void RequestVoluntaryMatchLeave()
        {
            if (_voluntaryLeavePending)
            {
                return;
            }

            if (MenuContext != GameMenuContext.InGame)
            {
                OnLeaveRequested();
                return;
            }

            _voluntaryLeavePending = true;
            _voluntaryLeaveAcknowledgedAt = -1d;
            _voluntaryLeaveDeadline =
                Time.realtimeSinceStartupAsDouble + VoluntaryLeaveAckTimeoutSeconds;
            SetLocalizedStatus("Leaving the game...");
            GetLocalAvatar()?.RequestVoluntaryMatchLeave();
        }

        public bool RequestPlayerPause()
        {
            var avatar = GetLocalAvatar();
            if (avatar == null)
            {
                return false;
            }

            avatar.RequestPlayerPause();
            return true;
        }

        public bool RequestPlayerPauseRelease()
        {
            var avatar = GetLocalAvatar();
            if (avatar == null)
            {
                return false;
            }

            avatar.RequestPlayerPauseRelease();
            return true;
        }

        public NetworkPlayerAvatar GetLocalAvatar()
        {
            var manager = _networkManager != null
                ? _networkManager
                : NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || manager.SpawnManager == null)
            {
                return null;
            }

            var playerObject = manager.SpawnManager.GetLocalPlayerObject();
            var avatar = playerObject != null
                ? playerObject.GetComponent<NetworkPlayerAvatar>()
                : null;
            return avatar != null && avatar.IsSpawned && avatar.IsOwner ? avatar : null;
        }

        /// <summary>Server side of <see cref="RequestVoluntaryMatchLeave"/>.</summary>
        public void HandleVoluntaryMatchLeaveOnServer(NetworkPlayerAvatar avatar)
        {
            var manager = _networkManager != null
                ? _networkManager
                : NetworkManager.Singleton;
            if (_destroyed || avatar == null || !avatar.IsSpawned ||
                manager == null || !manager.IsServer ||
                _sessions == null || !_sessions.IsInSession)
            {
                return;
            }

            var match = NetworkMatchState.Instance;
            var matchInProgress =
                (match != null && match.IsSpawned) ||
                _sessions.Current.Phase == MultiplayerConstants.PlayingPhase;
            var disposition = VoluntaryLeaveRules.Resolve(
                matchInProgress,
                avatar.OwnerClientId == NetworkManager.ServerClientId,
                IsMatchLobbyReturnOwned,
                IsFinalRankingLocked());
            switch (disposition)
            {
                case VoluntaryLeaveDisposition.AcknowledgeOnly:
                    avatar.AnnounceMatchEndedByPlayerOnServer();
                    break;
                case VoluntaryLeaveDisposition.LeaveCompletedMatch:
                    // The disconnect that follows removes the seat from the
                    // ceremony (LeaveCompletedMatch disconnect disposition).
                    avatar.AcknowledgeCompletedMatchLeaveOnServer();
                    break;
                case VoluntaryLeaveDisposition.ReturnRemainingPlayersToLobby:
                    // Start the return before the announcement so the leaving
                    // player's disconnect is deferred to lobby cleanup instead of
                    // opening the 60-second reconnect pause.
                    var returning = VoidActiveMatchToLobbyOnServer(string.Empty);
                    avatar.AnnounceMatchEndedByPlayerOnServer();
                    if (!returning)
                    {
                        QueueEndSessionAfterVoluntaryLeave();
                    }
                    break;
            }
        }

        /// <summary>
        /// Every client receives who ended the match. The leaving player's own
        /// client treats it as the acknowledgement to leave; everyone else sees a
        /// notice and resets their ready state.
        /// </summary>
        public void ReceiveMatchEndedByPlayer(NetworkPlayerAvatar avatar, string displayName)
        {
            if (avatar != null && avatar.IsOwner)
            {
                AcknowledgeVoluntaryLeave();
                return;
            }

            var name = string.IsNullOrWhiteSpace(displayName)
                ? GameText.T("A player")
                : displayName;
            QueueNotice(GameText.F("The game was ended by {0}.", name));
            QueueLocalReadyResetAfterMatch();
        }

        /// <summary>
        /// The server accepted this client's in-game leave; the client leaves
        /// the session after a short flush (see <see cref="AdvanceVoluntaryMatchLeave"/>).
        /// </summary>
        public void AcknowledgeVoluntaryLeave()
        {
            if (_voluntaryLeavePending && _voluntaryLeaveAcknowledgedAt < 0d)
            {
                _voluntaryLeaveAcknowledgedAt = Time.realtimeSinceStartupAsDouble;
            }
        }

        private void QueueNotice(string notice)
        {
            if (string.IsNullOrWhiteSpace(notice))
            {
                return;
            }

            _pendingNotices.Enqueue(notice);
            NoticeQueued?.Invoke();
        }

        private void AdvanceVoluntaryMatchLeave()
        {
            if (!_voluntaryLeavePending)
            {
                return;
            }

            var now = Time.realtimeSinceStartupAsDouble;
            var acknowledged =
                _voluntaryLeaveAcknowledgedAt >= 0d &&
                now >= _voluntaryLeaveAcknowledgedAt + VoluntaryLeaveAnnouncementFlushSeconds;
            if (!acknowledged && now < _voluntaryLeaveDeadline)
            {
                return;
            }

            _voluntaryLeavePending = false;
            _voluntaryLeaveAcknowledgedAt = -1d;
            CompleteVoluntaryMatchLeave();
        }

        /// <summary>
        /// Shows the waiting-room panel to a player who cleaned up the board
        /// while the others are still at the ceremony. The full waiting room
        /// (3D room, ready button) follows when the whole room returns, or the
        /// lobby screen when this player leaves the room.
        /// </summary>
        private void RefreshCeremonyWaitingRoomPresentation()
        {
            var shown = IsBackInWaitingRoomDuringCeremony;
            if (shown == _ceremonyWaitingRoomShown)
            {
                return;
            }

            _ceremonyWaitingRoomShown = shown;
            if (!shown)
            {
                // The room-wide return, leaving, or a lost connection follows;
                // their own callbacks restore the screen and the status line.
                return;
            }

            lobbyView?.SetPresentationVisible(true);
            SetLocalizedStatus(
                "Back in the waiting room. Other players are still at the award ceremony; you can leave the room from the menu.");
            _ceremonyWaitingRoomStatusShown = true;
        }

        private bool IsFinalRankingLocked()
        {
            var match = NetworkMatchState.Instance;
            return match != null && match.IsSpawned && match.IsFinalRankingLocked;
        }

        public bool IsMatchLobbyReturnOwned =>
            _completedMatchLobbyReturnInProgress ||
            _voidedMatchLobbyReturnPending;

        /// <summary>Host decision for the session service; see <see cref="IOnlineSessionProvider.KeepRoomOnPlayingDeparture"/>.</summary>
        private bool ShouldKeepRoomOnPlayingDeparture()
        {
            return CompletedMatchReturnRules.KeepsRoomOnPlayingDeparture(
                IsFinalRankingLocked(),
                IsMatchLobbyReturnOwned);
        }

        private void CompleteVoluntaryMatchLeave()
        {
            _sessionOperations.TryEnqueue(
                SessionLifecycleState.Leaving,
                CompleteVoluntaryMatchLeaveAsync);
        }

        private async Task CompleteVoluntaryMatchLeaveAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_sessions != null && _sessions.IsInSession)
                {
                    await LeaveSessionAsync();
                }

                cancellationToken.ThrowIfCancellationRequested();
                SetLocalizedStatus("You left the game.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The component lifetime ended while the provider operation settled.
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message);
                Debug.LogException(exception);
            }
            finally
            {
                SynchronizeLifecycleStateFromSession(force: true);
                if (!_destroyed)
                {
                    RenderLobby();
                }
            }
        }

        private void QueueEndSessionAfterVoluntaryLeave()
        {
            if (_networkTerminationRequested)
            {
                return;
            }

            _networkTerminationRequested = true;
            if (!_sessionOperations.TryEnqueue(
                    SessionLifecycleState.Terminating,
                    EndSessionAfterVoluntaryLeaveAsync))
            {
                _networkTerminationRequested = false;
            }
        }

        private async Task EndSessionAfterVoluntaryLeaveAsync(
            CancellationToken cancellationToken)
        {
            // Give the match-ended announcement time to reach every client
            // before the host session closes.
            await Task.Delay(
                VoluntaryLeaveSessionEndDelayMilliseconds,
                cancellationToken);
            await EndSessionAfterNetworkFailureAsync(
                GameText.T("A player left, so the match ended."),
                cancellationToken);
            if (_sessions == null || !_sessions.IsInSession)
            {
                // This is an explicit match abandonment, not a transient
                // network failure. Delete only after the provider confirms
                // that the session has ended, so a live board cannot write a
                // new orphan checkpoint after cleanup.
                new HostMinigameScheduleSession().CompleteActive();
            }
        }

        private void QueueLocalReadyResetAfterMatch()
        {
            if (_sessions == null || !_sessions.IsInSession)
            {
                ClearLocalReadyResetRequirement();
                return;
            }

            _localReadyResetRequired = _sessions.Current.LocalReady;
            if (!_localReadyResetRequired || _localReadyResetQueued)
            {
                return;
            }

            _localReadyResetRetryAt = 0d;
            _localReadyResetQueued = _sessionOperations.TryEnqueue(
                ResetLocalReadyAfterEndedMatchAsync);
        }

        private void AdvanceLocalReadyReset()
        {
            if (!CompletedMatchReturnRules.ShouldKeepLocalReadyResetRequired(
                    _localReadyResetRequired,
                    _sessions != null && _sessions.IsInSession,
                    _sessions != null &&
                    _sessions.IsInSession &&
                    _sessions.Current.LocalReady))
            {
                if (_localReadyResetRequired)
                {
                    ClearLocalReadyResetRequirement();
                }
                return;
            }

            if (_localReadyResetQueued ||
                Time.realtimeSinceStartupAsDouble < _localReadyResetRetryAt)
            {
                return;
            }

            _localReadyResetQueued = _sessionOperations.TryEnqueue(
                ResetLocalReadyAfterEndedMatchAsync);
        }

        private async Task ResetLocalReadyAfterEndedMatchAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                for (var attempt = 0;
                     attempt < LocalReadyResetAttemptsPerBatch;
                     attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!CompletedMatchReturnRules.
                            ShouldKeepLocalReadyResetRequired(
                                _localReadyResetRequired,
                                _sessions != null && _sessions.IsInSession,
                                _sessions != null &&
                                _sessions.IsInSession &&
                                _sessions.Current.LocalReady))
                    {
                        ClearLocalReadyResetRequirement();
                        return;
                    }

                    try
                    {
                        await _sessions.SetReadyAsync(false);
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            "Could not reset the ready state after the match ended " +
                            "(attempt " + (attempt + 1) + "): " +
                            exception.Message);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    if (_sessions == null ||
                        !_sessions.IsInSession ||
                        !_sessions.Current.LocalReady)
                    {
                        ClearLocalReadyResetRequirement();
                        return;
                    }

                    if (attempt + 1 < LocalReadyResetAttemptsPerBatch)
                    {
                        await Task.Delay(
                            LocalReadyResetRetryBaseMilliseconds << attempt,
                            cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The component lifetime ended while the provider operation settled.
            }
            finally
            {
                _localReadyResetQueued = false;
                if (_localReadyResetRequired)
                {
                    _localReadyResetRetryAt =
                        Time.realtimeSinceStartupAsDouble +
                        LocalReadyResetBatchCooldownSeconds;
                }
            }
        }

        private void ClearLocalReadyResetRequirement()
        {
            _localReadyResetRequired = false;
            _localReadyResetRetryAt = 0d;
        }

        private async Task CreateAndPublishAsync(string displayName)
        {
            ClearPlayingReconnectTicket();
            ResetHostMatchRecoveryChoice();
            _networkIdentityPublished = false;
            var initialBoardMapSelection = ResolveInitialBoardMapSelection();
            await _sessions.CreateAsync(
                "MazeParty Room",
                displayName,
                initialBoardMapSelection);
            PrepareHostMatchRecoveryChoice();
            await PublishLocalNetworkClientIdWhenReadyAsync();
        }

        private async Task ChangeBoardMapSelectionAsync(int delta)
        {
            if (!CanChangeBoardMapSelection())
            {
                throw new InvalidOperationException(
                    GameText.T(
                        "The board map can only be changed in the lobby."));
            }

            var catalog = LoadBoardMapCatalog();
            if (!BoardMapRuntimeLoader.TryResolveAdjacentSelection(
                    catalog,
                    _sessions.Current.BoardMapSelection,
                    delta,
                    out var selection,
                    out var error))
            {
                throw new InvalidOperationException(
                    GameText.T(
                        "The selected board map could not be loaded safely."),
                    new InvalidOperationException(error));
            }

            if (selection == _sessions.Current.BoardMapSelection)
            {
                return;
            }

            await _sessions.SetBoardMapAsync(selection);
        }

        private bool CanChangeBoardMapSelection()
        {
            return _sessions != null &&
                   _sessions.IsInSession &&
                   SessionRules.CanChangeBoardMap(
                       _sessions.Current.IsHost,
                       _sessions.Current.Phase) &&
                   !_matchRecoveryChoiceVisible &&
                   !ShouldResumeSavedMatch;
        }

        private static BoardMapSelection ResolveInitialBoardMapSelection()
        {
            if (BoardMapRuntimeLoader.TryResolveFreshSelection(
                    LoadBoardMapCatalog(),
                    out var selection,
                    out var error))
            {
                return selection;
            }

            throw new InvalidOperationException(
                GameText.T(
                    "The selected board map could not be loaded safely."),
                new InvalidOperationException(error));
        }

        private static BoardMapCatalog LoadBoardMapCatalog()
        {
            return Resources.Load<BoardMapCatalog>(
                BoardMapRuntimeLoader.CatalogResourcesPath);
        }

        private static void ValidateBoardMapSelectionBeforeStart(
            BoardMapSelection requested,
            bool allowLegacy)
        {
            var catalog = LoadBoardMapCatalog();
            if (!allowLegacy && requested.IsLegacy &&
                catalog != null && catalog.Maps.Count > 0)
            {
                throw new InvalidOperationException(
                    GameText.T(
                        "The selected board map could not be loaded safely."));
            }

            if (!BoardMapRuntimeLoader.TryResolveExactSelection(
                    catalog,
                    requested,
                    out _,
                    out var error))
            {
                throw new InvalidOperationException(
                    GameText.T(
                        "The selected board map could not be loaded safely."),
                    new InvalidOperationException(error));
            }
        }

        private async Task JoinAndPublishAsync(string code, string displayName)
        {
            ClearPlayingReconnectTicket();
            ResetHostMatchRecoveryChoice();
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
                        GameText.T("The saved reconnect target is no longer an active client game."));
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
            return PublishLocalNetworkClientIdWhenReadyAsync(
                _sessionOperations.LifetimeToken);
        }

        private Task PublishLocalNetworkClientIdWhenReadyAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                    GameText.T("The active session ID is not available for network identity publishing."));
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
                PublishLocalNetworkClientIdTrackedAsync(
                    expectedSessionId,
                    cancellationToken);
            return _networkIdentityPublishTask;
        }

        private async Task PublishLocalNetworkClientIdTrackedAsync(
            string expectedSessionId,
            CancellationToken cancellationToken)
        {
            // Yield once so the shared task field is assigned before this method can
            // complete synchronously and clear it.
            await Task.Yield();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await PublishLocalNetworkClientIdCoreAsync(
                    expectedSessionId,
                    cancellationToken);
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
            string expectedSessionId,
            CancellationToken cancellationToken)
        {
            for (var readinessAttempt = 0; readinessAttempt < 40; readinessAttempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                            cancellationToken.ThrowIfCancellationRequested();
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
                                (saveAttempt + 1),
                                cancellationToken);
                        }
                    }
                }

                await Task.Delay(50, cancellationToken);
            }

            if (IsExpectedSession(expectedSessionId))
            {
                throw new InvalidOperationException(
                    GameText.T("The local network player ID was not ready in time."));
            }
        }

        private async Task LeaveSessionAsync()
        {
            ClearPlayingReconnectTicket();
            var abandonHostSchedule =
                _sessions != null &&
                _sessions.IsInSession &&
                _sessions.Current.IsHost;
            try
            {
                await _sessions.LeaveAsync();
                if (abandonHostSchedule)
                {
                    new HostMinigameScheduleSession().CompleteActive();
                }
            }
            finally
            {
                ResetHostMatchRecoveryChoice();
                ClearPlayingReconnectTicket();
            }
        }

        private async Task StartGameAsync()
        {
            if (_matchRecoveryChoiceVisible)
            {
                throw new InvalidOperationException(
                    GameText.T("Choose whether to continue or discard the saved match first."));
            }

            var snapshot = _sessions.Current;
            if (!snapshot.IsHost || !snapshot.CanStart)
            {
                throw new InvalidOperationException(
                    GameText.T("All four players must be ready."));
            }

            ValidateSelectedMatchRecoveryBeforeStart();
            ValidateBoardMapSelectionBeforeStart(
                snapshot.BoardMapSelection,
                ShouldResumeSavedMatch);

            var manager = _networkManager != null
                ? _networkManager
                : NetworkManager.Singleton;
            if (manager == null || !manager.IsHost || manager.SceneManager == null)
            {
                throw new InvalidOperationException(
                    GameText.T("The host NetworkManager is not ready."));
            }

            ObserveNetworkSceneManager(manager.SceneManager);
            if (!HasExactlyFourAssignedNetworkPlayers(manager))
            {
                throw new InvalidOperationException(
                    GameText.T("Four connected players with server-assigned seats are required."));
            }

            await _sessions.SetPlayingAsync(true);

            var result = manager.SceneManager.LoadScene(
                MultiplayerConstants.BoardScene,
                LoadSceneMode.Additive);

            if (result != SceneEventProgressStatus.Started)
            {
                await _sessions.SetPlayingAsync(false);
                throw new InvalidOperationException(
                    GameText.F("Could not start Board scene synchronization: {0}", result));
            }

            SetLocalizedStatus(
                "Waiting for every player to finish loading the Board scene.");
        }

        private async Task RequestCompletedMatchReturnAsync()
        {
            if (_sessions == null ||
                !_sessions.IsInSession ||
                _sessions.Current.Phase != MultiplayerConstants.PlayingPhase)
            {
                throw new InvalidOperationException(
                    GameText.T("A completed online match is required before returning to the lobby."));
            }

            var match = NetworkMatchState.Instance;
            if (match == null || !match.CanSubmitCeremonyReturn)
            {
                throw new InvalidOperationException(
                    GameText.T("The award ceremony is not accepting return requests yet."));
            }

            await _sessions.SetReadyAsync(false);

            var manager = _networkManager != null
                ? _networkManager
                : NetworkManager.Singleton;
            var playerObject = manager != null && manager.SpawnManager != null
                ? manager.SpawnManager.GetLocalPlayerObject()
                : null;
            var avatar = playerObject != null
                ? playerObject.GetComponent<NetworkPlayerAvatar>()
                : null;
            if (avatar == null || !avatar.IsSpawned || !avatar.IsOwner)
            {
                throw new InvalidOperationException(
                    GameText.T("The local network player is not ready to return to the lobby."));
            }

            avatar.RequestCompletedMatchReturn();
        }

        /// <summary>
        /// Called once every player still in the room cleaned up the board.
        /// Players who already left the room after the final ranking are not
        /// waited for.
        /// </summary>
        public bool BeginCompletedMatchLobbyReturnOnServer()
        {
            // The ceremony owns its retry latch. Requiring an immediate start
            // prevents a queued operation from being accepted and later skipped
            // after the lifecycle state changes underneath it.
            return BeginMatchLobbyReturnOnServer(
                requireImmediateOperationStart: true);
        }

        /// <summary>
        /// Server-only invalidation path for a fixed-four match that can no
        /// longer continue. It deliberately reuses the completed-match scene
        /// cleanup so the room, connected roster and invite code survive.
        /// Repeated calls are safe while the return is already in progress.
        /// </summary>
        public bool VoidActiveMatchToLobbyOnServer(string reason)
        {
            var manager = _networkManager != null
                ? _networkManager
                : NetworkManager.Singleton;
            if (_destroyed ||
                manager == null ||
                !manager.IsServer ||
                _sessions == null ||
                !_sessions.IsInSession ||
                !_sessions.Current.IsHost ||
                (!IsMatchLobbyReturnOwned &&
                 _sessions.Current.Phase != MultiplayerConstants.PlayingPhase))
            {
                return false;
            }

            if (_completedMatchLobbyReturnInProgress)
            {
                return BeginMatchLobbyReturnOnServer(
                    includeReconnectGraceDisconnects: true);
            }

            if (_voidedMatchLobbyReturnPending)
            {
                TryAdvanceVoidedMatchLobbyReturn();
                return true;
            }

            if (manager.SceneManager == null)
            {
                SetLocalizedStatus(
                    "Waiting for the server scene manager during lobby return.");
                return false;
            }

            ObserveNetworkSceneManager(manager.SceneManager);
            if (!TryQueueCompletedMatchSceneUnloads(
                    manager.SceneManager,
                    out var sceneValidationError))
            {
                SetStatus(sceneValidationError);
                return false;
            }

            var match = NetworkMatchState.Instance;
            if (match != null && match.IsSpawned)
            {
                try
                {
                    if (!match.VoidActiveMatchOnServer(lobbyReturnPrepared: true))
                    {
                        _completedMatchSceneUnloadQueue.Clear();
                        return false;
                    }
                }
                catch (Exception exception)
                {
                    if (!match.IsMatchVoided)
                    {
                        _completedMatchSceneUnloadQueue.Clear();
                        Debug.LogException(exception);
                        return false;
                    }

                    // The irreversible gate was already closed. Keep going so
                    // the Board is unloaded even if best-effort teardown failed.
                    Debug.LogWarning(
                        "The match was voided, but part of Board teardown failed: " +
                        exception.Message);
                }
            }

            _voidedMatchLobbyReturnPending = true;
            _voidedMatchLobbyReturnReason = reason ?? string.Empty;

            // The persistent session is invalidated before starting any async
            // phase or scene work. A crash during lobby return must never offer
            // this voided match as recoverable on the next launch.
            try
            {
                new HostMinigameScheduleSession().CompleteActive();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not discard the voided host match state: " +
                    exception.Message);
            }
            ResetHostMatchRecoveryChoice();

            TryAdvanceVoidedMatchLobbyReturn(sceneUnloadPlanPrepared: true);
            // Once the irreversible gate closes, the request is owned by this
            // controller and remains accepted while an exclusive operation is
            // busy. Update retries without reopening gameplay.
            return true;
        }

        private bool TryAdvanceVoidedMatchLobbyReturn(
            bool sceneUnloadPlanPrepared = true)
        {
            if (!_voidedMatchLobbyReturnPending)
            {
                return false;
            }

            if (!BeginMatchLobbyReturnOnServer(
                    includeReconnectGraceDisconnects: true,
                    sceneUnloadPlanPrepared: sceneUnloadPlanPrepared,
                    requireImmediateOperationStart: true))
            {
                return false;
            }

            var reason = _voidedMatchLobbyReturnReason;
            _voidedMatchLobbyReturnPending = false;
            _voidedMatchLobbyReturnReason = string.Empty;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                // Starting the operation can synchronously publish its generic
                // status. Keep the concrete invalidation reason visible.
                SetStatus(reason);
            }

            return true;
        }

        /// <summary>
        /// Returns the room to the player ready screen by unloading the match
        /// scenes for whoever is still connected: after a completed match, or
        /// after a player left an in-progress match through the menu.
        /// </summary>
        private bool BeginMatchLobbyReturnOnServer(
            bool includeReconnectGraceDisconnects = false,
            bool sceneUnloadPlanPrepared = false,
            bool requireImmediateOperationStart = false)
        {
            if (_destroyed)
            {
                return false;
            }

            if (_completedMatchLobbyReturnInProgress)
            {
                if (includeReconnectGraceDisconnects)
                {
                    PromoteReconnectGraceDisconnectsForLobbyCleanup();
                    ClearReconnectGraceDisconnectTracking();
                    QueueCompletedMatchDisconnectedLobbyCleanups();
                }

                return true;
            }

            var manager = _networkManager != null
                ? _networkManager
                : NetworkManager.Singleton;
            if (manager == null ||
                !manager.IsServer ||
                manager.SceneManager == null ||
                _sessions == null ||
                !_sessions.IsInSession ||
                !_sessions.Current.IsHost ||
                _sessions.Current.Phase != MultiplayerConstants.PlayingPhase)
            {
                Debug.LogWarning(
                    "Only the active session host can return a completed match to the lobby.");
                return false;
            }

            ObserveNetworkSceneManager(manager.SceneManager);
            if (!sceneUnloadPlanPrepared &&
                !TryQueueCompletedMatchSceneUnloads(
                    manager.SceneManager,
                    out var sceneValidationError))
            {
                SetStatus(sceneValidationError);
                return false;
            }

            if (includeReconnectGraceDisconnects)
            {
                PromoteReconnectGraceDisconnectsForLobbyCleanup();
            }

            _completedMatchLobbyReturnInProgress = true;
            Func<CancellationToken, Task> operation = cancellationToken =>
                BeginCompletedMatchLobbyReturnAsync(manager, cancellationToken);
            var accepted = requireImmediateOperationStart
                ? _sessionOperations.TryStart(
                    SessionLifecycleState.ReturningToLobby,
                    operation)
                : _sessionOperations.TryEnqueue(
                    SessionLifecycleState.ReturningToLobby,
                    operation);
            if (!accepted)
            {
                if (requireImmediateOperationStart)
                {
                    // Preserve the validated unload plan. The caller owns retry
                    // and may immediately submit again once the coordinator is idle.
                    _completedMatchLobbyReturnInProgress = false;
                }
                else
                {
                    ResetCompletedMatchLobbyReturnState();
                }

                return false;
            }

            if (includeReconnectGraceDisconnects)
            {
                ClearReconnectGraceDisconnectTracking();
            }

            return true;
        }

        private async Task BeginCompletedMatchLobbyReturnAsync(
            NetworkManager manager,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    SetLocalizedStatus(
                        "Returning the match to the player ready screen...");
                    await _sessions.SetPlayingAsync(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    _completedMatchPhaseSaveFailureCount = 0;
                    QueueCompletedMatchDisconnectedLobbyCleanups();

                    if (_destroyed || !_completedMatchLobbyReturnInProgress)
                    {
                        return;
                    }

                    if (manager != null &&
                        manager.IsServer &&
                        manager.SceneManager != null)
                    {
                        ObserveNetworkSceneManager(manager.SceneManager);
                    }
                    ScheduleCompletedMatchUnloadForNextFrame();
                    return;
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _completedMatchPhaseSaveFailureCount++;
                    var snapshot = _sessions != null && _sessions.IsInSession
                        ? _sessions.Current
                        : SessionSnapshot.Empty;
                    var disposition = CompletedMatchReturnRules.
                        GetPhaseSaveFailureDisposition(
                            !_destroyed &&
                            _completedMatchLobbyReturnInProgress,
                            _sessions != null && _sessions.IsInSession,
                            snapshot.IsHost,
                            snapshot.Phase,
                            _completedMatchPhaseSaveFailureCount,
                            CompletedMatchPhaseSaveFailureLimit);
                    switch (disposition)
                    {
                        case CompletedMatchPhaseSaveFailureDisposition.
                            ContinueSceneCleanup:
                            SetLocalizedStatus(
                                "The lobby phase was saved; retrying local match cleanup: {0}",
                                exception.Message);
                            Debug.LogWarning(exception);
                            _completedMatchPhaseSaveFailureCount = 0;
                            ScheduleCompletedMatchUnloadForNextFrame();
                            return;
                        case CompletedMatchPhaseSaveFailureDisposition.Retry:
                            SetLocalizedStatus(
                                "Could not save the lobby phase; retrying: {0}",
                                exception.Message);
                            Debug.LogWarning(exception);
                            await Task.Delay(
                                CompletedMatchPhaseSaveRetryMilliseconds,
                                cancellationToken);
                            continue;
                        default:
                            Debug.LogWarning(exception);
                            BeginCompletedMatchFailClosedTermination(GameText.F(
                                "Could not return the completed match to the lobby: {0}",
                                "Lobby phase save failed after " +
                                _completedMatchPhaseSaveFailureCount +
                                " attempts."));
                            return;
                    }
                }
            }
        }

        private static bool TryPrepareConnectedAvatarsForLobbyOnServer(
            NetworkManager manager)
        {
            if (manager == null || !manager.IsServer || manager.SpawnManager == null)
            {
                return false;
            }

            var avatars = new List<NetworkPlayerAvatar>(
                manager.ConnectedClientsIds.Count);
            foreach (var clientId in manager.ConnectedClientsIds)
            {
                var playerObject = manager.SpawnManager.GetPlayerNetworkObject(clientId);
                var avatar = playerObject != null
                    ? playerObject.GetComponent<NetworkPlayerAvatar>()
                    : null;
                if (avatar == null || !avatar.IsSpawned)
                {
                    return false;
                }

                avatars.Add(avatar);
            }

            for (var index = 0; index < avatars.Count; index++)
            {
                var avatar = avatars[index];
                avatar.PrepareForLobbyOnServer();
            }

            return true;
        }

        private bool TryQueueCompletedMatchSceneUnloads(
            NetworkSceneManager sceneManager,
            out string error)
        {
            error = string.Empty;
            _completedMatchSceneUnloadQueue.Clear();
            _completedMatchSceneBeingUnloaded = string.Empty;
            _completedMatchSceneOperationDeadline = 0d;
            var queuedSceneNames = new HashSet<string>(StringComparer.Ordinal);
            var synchronizedSceneHandles = new HashSet<SceneHandle>();
            var synchronizedScenes = sceneManager.GetSynchronizedScenes();
            for (var index = 0; index < synchronizedScenes.Count; index++)
            {
                synchronizedSceneHandles.Add(synchronizedScenes[index].handle);
            }

            foreach (var definition in MinigameCatalog.RegisteredMinigames)
            {
                var sceneName = definition.SceneName;
                var scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                if (!synchronizedSceneHandles.Contains(scene.handle))
                {
                    _completedMatchSceneUnloadQueue.Clear();
                    error = GameText.F(
                        "The loaded minigame scene is not synchronized: {0}",
                        sceneName);
                    return false;
                }

                if (queuedSceneNames.Add(sceneName))
                {
                    _completedMatchSceneUnloadQueue.Enqueue(sceneName);
                }
            }

            var board = SceneManager.GetSceneByName(MultiplayerConstants.BoardScene);
            if (!board.IsValid() ||
                !board.isLoaded ||
                !synchronizedSceneHandles.Contains(board.handle))
            {
                _completedMatchSceneUnloadQueue.Clear();
                error = GameText.T("The synchronized Board scene is required for lobby return.");
                return false;
            }

            if (queuedSceneNames.Add(MultiplayerConstants.BoardScene))
            {
                _completedMatchSceneUnloadQueue.Enqueue(
                    MultiplayerConstants.BoardScene);
            }

            return true;
        }

        private void TryBeginNextCompletedMatchSceneUnload()
        {
            if (!_completedMatchLobbyReturnInProgress ||
                _completedMatchFailClosedTerminationPending ||
                !string.IsNullOrEmpty(_completedMatchSceneBeingUnloaded) ||
                _completedMatchPendingUnloadClients.Count > 0)
            {
                return;
            }

            var manager = _networkManager != null
                ? _networkManager
                : NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || manager.SceneManager == null)
            {
                EnsureCompletedMatchSceneOperationDeadline(manager);
                SetLocalizedStatus(
                    "Waiting for the server scene manager during lobby return.");
                ScheduleCompletedMatchUnloadForNextFrame();
                return;
            }

            ObserveNetworkSceneManager(manager.SceneManager);
            while (_completedMatchSceneUnloadQueue.Count > 0)
            {
                var sceneName = _completedMatchSceneUnloadQueue.Peek();
                var scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    _completedMatchSceneUnloadQueue.Dequeue();
                    _completedMatchSceneOperationDeadline = 0d;
                    continue;
                }

                EnsureCompletedMatchSceneOperationDeadline(manager);
                var result = manager.SceneManager.UnloadScene(scene);
                switch (CompletedMatchReturnRules.
                    GetSceneUnloadDisposition(result))
                {
                    case CompletedMatchSceneUnloadDisposition.WaitForCompletion:
                        _completedMatchSceneUnloadQueue.Dequeue();
                        _completedMatchSceneBeingUnloaded = sceneName;
                        // A preceding SceneEventInProgress wait belongs to a
                        // different operation. The accepted unload receives its
                        // own full NGO timeout window.
                        _completedMatchSceneOperationDeadline = 0d;
                        EnsureCompletedMatchSceneOperationDeadline(manager);
                        SetLocalizedStatus(
                            "Synchronizing scene unload: {0}",
                            sceneName);
                        return;
                    case CompletedMatchSceneUnloadDisposition.AdvanceQueue:
                        _completedMatchSceneUnloadQueue.Dequeue();
                        _completedMatchSceneOperationDeadline = 0d;
                        continue;
                    case CompletedMatchSceneUnloadDisposition.Retry:
                        SetLocalizedStatus(
                            "Waiting to retry synchronized scene unload for {0}: {1}",
                            sceneName,
                            result);
                        ScheduleCompletedMatchUnloadForNextFrame();
                        return;
                    default:
                        BeginCompletedMatchFailClosedTermination(GameText.F(
                            "Could not return the completed match to the lobby: {0}",
                            "UnloadScene(" + sceneName + ") returned " +
                            result + "."));
                        return;
                }
            }

            _completedMatchSceneOperationDeadline = 0d;
            CompleteCompletedMatchLobbyReturn();
        }

        private void EnsureCompletedMatchSceneOperationDeadline(
            NetworkManager manager)
        {
            if (_completedMatchSceneOperationDeadline > 0d)
            {
                return;
            }

            var timeoutSeconds =
                CompletedMatchSceneOperationFallbackTimeoutSeconds;
            if (manager != null && manager.NetworkConfig != null)
            {
                timeoutSeconds = Math.Max(
                    1d,
                    manager.NetworkConfig.LoadSceneTimeOut +
                    CompletedMatchUnloadClientGraceSeconds);
            }

            _completedMatchSceneOperationDeadline =
                CompletedMatchReturnRules.GetSceneOperationDeadline(
                    Time.realtimeSinceStartupAsDouble,
                    timeoutSeconds);
        }

        private void BeginCompletedMatchFailClosedTermination(
            string reason)
        {
            if (!_completedMatchFailClosedTerminationPending)
            {
                _completedMatchFailClosedTerminationPending = true;
                _completedMatchFailClosedReason = reason ?? string.Empty;
                _completedMatchFailClosedRetryAt = 0d;
                _completedMatchFailClosedTerminationAttemptCount = 0;
                _completedMatchFailClosedCallTimedOut = false;
                _completedMatchFailClosedExhaustedStatusShown = false;
                _completedMatchUnloadNextFrame = false;
                _completedMatchSceneOperationDeadline = 0d;
                SetStatus(_completedMatchFailClosedReason);
            }

            TryAdvanceCompletedMatchFailClosedTermination();
        }

        private void TryAdvanceCompletedMatchFailClosedTermination()
        {
            if (!_completedMatchFailClosedTerminationPending || _destroyed)
            {
                return;
            }

            if (_sessions == null || !_sessions.IsInSession)
            {
                // A failed return is not a successful completion, explicit
                // discard, or voluntary leave. Preserve the recovery journal
                // so the host can resume it on the next session.
                ResetCompletedMatchLobbyReturnState();
                UnloadBoardLocally();
                return;
            }

            var now = Time.realtimeSinceStartupAsDouble;
            var disposition = CompletedMatchReturnRules.
                GetTerminationAttemptDisposition(
                    _completedMatchFailClosedTerminationPending,
                    _networkTerminationRequested,
                    _completedMatchFailClosedCallTimedOut,
                    _completedMatchFailClosedTerminationAttemptCount,
                    CompletedMatchFailClosedTerminationAttemptLimit,
                    _completedMatchFailClosedRetryAt,
                    now);
            if (disposition ==
                CompletedMatchTerminationAttemptDisposition.Wait)
            {
                return;
            }

            if (disposition ==
                CompletedMatchTerminationAttemptDisposition.Exhausted)
            {
                if (!_completedMatchFailClosedExhaustedStatusShown)
                {
                    _completedMatchFailClosedExhaustedStatusShown = true;
                    var detail = _completedMatchFailClosedCallTimedOut
                        ? "Session cleanup timed out. Retry Leave Session or restart the game."
                        : "Session cleanup failed after " +
                          _completedMatchFailClosedTerminationAttemptCount +
                          " attempts. Retry Leave Session or restart the game.";
                    SetLocalizedStatus(
                        "Could not return the completed match to the lobby: {0}",
                        detail);
                    Debug.LogError(_status);
                }
                return;
            }

            _completedMatchFailClosedTerminationAttemptCount++;
            _networkTerminationRequested = true;
            if (!_sessionOperations.TryEnqueue(
                    SessionLifecycleState.Terminating,
                    EndCompletedMatchFailClosedSessionAsync))
            {
                _networkTerminationRequested = false;
                _completedMatchFailClosedTerminationAttemptCount--;
                _completedMatchFailClosedRetryAt =
                    now + CompletedMatchFailClosedRetrySeconds;
            }
        }

        private async Task EndCompletedMatchFailClosedSessionAsync(
            CancellationToken cancellationToken)
        {
            ClearPlayingReconnectTicket();
            SetStatus(_completedMatchFailClosedReason);
            var callTimedOut = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_sessions != null && _sessions.IsInSession)
                {
                    var leaveTask = _sessions.LeaveAsync();
                    var timeoutTask = Task.Delay(
                        CompletedMatchFailClosedLeaveTimeoutMilliseconds,
                        cancellationToken);
                    var completedTask = await Task.WhenAny(
                        leaveTask,
                        timeoutTask);
                    if (!ReferenceEquals(completedTask, leaveTask))
                    {
                        // Observe the provider call before propagating lifetime
                        // cancellation so it cannot become an unobserved task or
                        // race provider disposal without an owner.
                        _ = ObserveTimedOutCompletedMatchLeaveAsync(leaveTask);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!leaveTask.IsCompleted)
                        {
                            callTimedOut = true;
                            _completedMatchFailClosedCallTimedOut = true;
                            Debug.LogWarning(
                                "Completed-match fail-closed session cleanup timed out.");
                            return;
                        }
                    }

                    await leaveTask;
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                // Component lifetime shutdown owns cancellation.
            }
            catch (Exception exception)
            {
                if (!_destroyed)
                {
                    SetLocalizedStatus(
                        "Could not return the completed match to the lobby: {0}",
                        "Session cleanup attempt " +
                        _completedMatchFailClosedTerminationAttemptCount +
                        " failed: " + exception.Message);
                    Debug.LogWarning(_status);
                }
            }
            finally
            {
                _networkTerminationRequested = false;
                if (!_destroyed &&
                    _sessions != null &&
                    _sessions.IsInSession &&
                    !callTimedOut &&
                    !_completedMatchFailClosedCallTimedOut &&
                    _completedMatchFailClosedTerminationAttemptCount <
                    CompletedMatchFailClosedTerminationAttemptLimit)
                {
                    _completedMatchFailClosedRetryAt =
                        Time.realtimeSinceStartupAsDouble +
                        CompletedMatchFailClosedRetrySeconds;
                }

                SynchronizeLifecycleStateFromSession(force: true);
            }
        }

        private static async Task ObserveTimedOutCompletedMatchLeaveAsync(
            Task leaveTask)
        {
            try
            {
                await leaveTask;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Timed-out completed-match session cleanup later failed: " +
                    exception.Message);
            }
        }

        private void CompleteCompletedMatchLobbyReturn()
        {
            new HostMinigameScheduleSession().CompleteActive();
            ResetHostMatchRecoveryChoice();
            ResetCompletedMatchLobbyReturnState();
            _sessionOperations.TryTransition(SessionLifecycleState.Lobby);
            SetLobbyRendering(true);
            SetLocalizedStatus("Returned to the player ready screen.");
        }

        private void ResetCompletedMatchLobbyReturnState()
        {
            _completedMatchSceneUnloadQueue.Clear();
            _completedMatchPendingUnloadClients.Clear();
            _completedMatchDisconnectedClients.Clear();
            _completedMatchSceneBeingUnloaded = string.Empty;
            _completedMatchPendingUnloadScene = string.Empty;
            _completedMatchPendingUnloadDeadline = 0d;
            _completedMatchPhaseSaveFailureCount = 0;
            _completedMatchSceneOperationDeadline = 0d;
            _completedMatchFailClosedTerminationPending = false;
            _completedMatchFailClosedReason = string.Empty;
            _completedMatchFailClosedRetryAt = 0d;
            _completedMatchFailClosedTerminationAttemptCount = 0;
            _completedMatchFailClosedCallTimedOut = false;
            _completedMatchFailClosedExhaustedStatusShown = false;
            _completedMatchAvatarsPrepared = false;
            _completedMatchUnloadNextFrame = false;
            _completedMatchUnloadEarliestFrame = 0;
            _completedMatchLobbyReturnInProgress = false;
            _voidedMatchLobbyReturnPending = false;
            _voidedMatchLobbyReturnReason = string.Empty;
        }

        private void ScheduleCompletedMatchUnloadForNextFrame()
        {
            _completedMatchUnloadNextFrame = true;
            _completedMatchUnloadEarliestFrame = Math.Max(
                _completedMatchUnloadEarliestFrame,
                Time.frameCount + 1);
        }

        private void OnSessionChanged()
        {
            UpdatePlayingReconnectTicket();
            QueueCompletedMatchDisconnectedLobbyCleanups();
            if (_networkManager != null)
            {
                ObserveNetworkSceneManager(_networkManager.SceneManager);
            }

            var isInSession = _sessions.IsInSession;
            var isPlayingPhase = isInSession &&
                _sessions.Current.Phase == MultiplayerConstants.PlayingPhase;
            var isLobbyPhase = isInSession &&
                _sessions.Current.Phase == MultiplayerConstants.LobbyPhase;
            if (CompletedMatchReturnRules.
                ShouldResetLocalReadyAfterLobbyReturn(
                    _observedPlayingPhase,
                    isInSession,
                    isLobbyPhase,
                    isInSession && _sessions.Current.LocalReady))
            {
                QueueLocalReadyResetAfterMatch();
            }

            if (isPlayingPhase)
            {
                _observedPlayingPhase = true;
            }
            else if (!isInSession || isLobbyPhase)
            {
                _observedPlayingPhase = false;
                ResetPendingBoardLoadReconnect();
            }

            if (!isInSession)
            {
                ClearLocalReadyResetRequirement();
                ClearReconnectGraceDisconnectTracking();
                ResetHostMatchRecoveryChoice();
                ResetCompletedMatchLobbyReturnState();
                _networkIdentityPublished = false;
                UnloadBoardLocally();
            }

            SynchronizeLifecycleStateFromSession();
            RenderLobby();
        }

        private void OnSessionEnded(string reason)
        {
            ClearPlayingReconnectTicket();
            _observedPlayingPhase = false;
            ClearLocalReadyResetRequirement();
            ClearReconnectGraceDisconnectTracking();
            ResetPendingBoardLoadReconnect();
            _sessionOperations.TryTransition(SessionLifecycleState.Terminating);
            SetStatus(reason);
        }

        private void BeginPendingBoardLoadReconnect()
        {
            if (!_pendingBoardLoadReconnect)
            {
                _pendingBoardLoadReconnect = true;
                _pendingBoardLoadVoidRequested = false;
                _pendingBoardLoadReconnectEndsAt =
                    CompletedMatchReturnRules.GetReconnectGraceEndsAt(
                        Time.realtimeSinceStartupAsDouble);
            }

            SetLocalizedStatus(
                "A player disconnected. Gameplay is paused for the 60-second reconnect window.");
        }

        private void BeginPendingBoardMapReadinessWait()
        {
            if (!_pendingBoardLoadReconnect)
            {
                _pendingBoardLoadReconnect = true;
                _pendingBoardLoadVoidRequested = false;
                _pendingBoardLoadReconnectEndsAt =
                    CompletedMatchReturnRules.GetReconnectGraceEndsAt(
                        Time.realtimeSinceStartupAsDouble);
            }

            SetLocalizedStatus(
                "Waiting for every player to finish loading the Board scene.");
        }

        private void AdvancePendingBoardLoadReconnect()
        {
            if (!_pendingBoardLoadReconnect)
            {
                return;
            }

            var manager = _networkManager != null
                ? _networkManager
                : NetworkManager.Singleton;
            if (_destroyed ||
                manager == null ||
                !manager.IsServer ||
                _sessions == null ||
                !_sessions.IsInSession ||
                _sessions.Current.Phase != MultiplayerConstants.PlayingPhase)
            {
                ResetPendingBoardLoadReconnect();
                return;
            }

            var match = NetworkMatchState.Instance;
            var now = Time.realtimeSinceStartupAsDouble;
            var allPlayersReady =
                HasExactlyFourBoardReadyNetworkPlayers(manager);
            var startupDisposition = BoardMapStartupPolicy.Evaluate(
                match != null && match.IsSpawned && match.IsBoardMapReady,
                allPlayersReady,
                match != null && match.IsSpawned
                    ? match.BoardMapLoadFailure
                    : string.Empty);
            if (startupDisposition == BoardMapStartupDisposition.FailMatch)
            {
                ResetPendingBoardLoadReconnect();
                EndActiveMatchForNetworkFailure(match.BoardMapLoadFailure);
                return;
            }

            if (!_pendingBoardLoadVoidRequested &&
                match != null &&
                match.IsSpawned &&
                startupDisposition == BoardMapStartupDisposition.Ready &&
                CompletedMatchReturnRules.ShouldResumeReconnect(
                    allPlayersReady,
                    _pendingBoardLoadReconnectEndsAt,
                    now))
            {
                ResetPendingBoardLoadReconnect();
                if (match.EnableGameplayOnServer())
                {
                    SetLocalizedStatus(
                        "All four players loaded the Board. Gameplay input is enabled.");
                }
                return;
            }

            if (!_pendingBoardLoadVoidRequested &&
                !CompletedMatchReturnRules.HasReconnectGraceExpired(
                    _pendingBoardLoadReconnectEndsAt,
                    now))
            {
                return;
            }

            _pendingBoardLoadVoidRequested = true;
            if (VoidActiveMatchToLobbyOnServer(GameText.T(
                    "A player did not reconnect within 60 seconds. The match was voided and everyone is returning to the waiting room.")))
            {
                ResetPendingBoardLoadReconnect();
            }
        }

        private void ResetPendingBoardLoadReconnect()
        {
            _pendingBoardLoadReconnect = false;
            _pendingBoardLoadVoidRequested = false;
            _pendingBoardLoadReconnectEndsAt = 0d;
        }

        private void TrackReconnectGraceDisconnect(ulong clientId)
        {
            _reconnectGraceDisconnectedClients.Add(clientId);
            if (_sessions == null ||
                !_sessions.IsInSession ||
                !_sessions.TryGetAuthoritativeSlot(clientId, out var slot) ||
                slot < 0 ||
                slot >= MultiplayerConstants.MaxPlayers)
            {
                return;
            }

            if (_reconnectGraceClientBySlot.TryGetValue(
                    slot,
                    out var previousClientId) &&
                previousClientId != clientId)
            {
                _reconnectGraceDisconnectedClients.Remove(previousClientId);
            }

            _reconnectGraceClientBySlot[slot] = clientId;
        }

        private void ResolveReconnectedGraceClients()
        {
            if (_reconnectGraceDisconnectedClients.Count == 0 ||
                _sessions == null ||
                !_sessions.IsInSession)
            {
                return;
            }

            var manager = _networkManager != null
                ? _networkManager
                : NetworkManager.Singleton;
            if (manager == null || !manager.IsServer)
            {
                return;
            }

            var connectedBySlot = new Dictionary<int, ulong>();
            foreach (var connectedClientId in manager.ConnectedClientsIds)
            {
                _reconnectGraceDisconnectedClients.Remove(connectedClientId);
                if (_sessions.TryGetAuthoritativeSlot(
                        connectedClientId,
                        out var connectedSlot))
                {
                    connectedBySlot[connectedSlot] = connectedClientId;
                }
            }

            if (_reconnectGraceClientBySlot.Count == 0)
            {
                return;
            }

            var resolvedSlots = new List<int>();
            foreach (var tracked in _reconnectGraceClientBySlot)
            {
                if (!connectedBySlot.TryGetValue(
                        tracked.Key,
                        out var connectedClientId) ||
                    !CompletedMatchReturnRules.IsReconnectForTrackedSeat(
                        tracked.Value,
                        tracked.Key,
                        connectedClientId,
                        tracked.Key))
                {
                    continue;
                }

                _reconnectGraceDisconnectedClients.Remove(tracked.Value);
                resolvedSlots.Add(tracked.Key);
            }

            for (var index = 0; index < resolvedSlots.Count; index++)
            {
                _reconnectGraceClientBySlot.Remove(resolvedSlots[index]);
            }
        }

        private void PromoteReconnectGraceDisconnectsForLobbyCleanup()
        {
            ResolveReconnectedGraceClients();
            foreach (var clientId in _reconnectGraceDisconnectedClients)
            {
                _completedMatchDisconnectedClients.Add(clientId);
            }
        }

        private void ClearReconnectGraceDisconnectTracking()
        {
            _reconnectGraceDisconnectedClients.Clear();
            _reconnectGraceClientBySlot.Clear();
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

        private static bool HasExactlyFourBoardReadyNetworkPlayers(
            NetworkManager manager)
        {
            if (!HasExactlyFourAssignedNetworkPlayers(manager) ||
                manager.SpawnManager == null)
            {
                return false;
            }

            foreach (var clientId in manager.ConnectedClientsIds)
            {
                var playerObject =
                    manager.SpawnManager.GetPlayerNetworkObject(clientId);
                var avatar = playerObject != null
                    ? playerObject.GetComponent<NetworkPlayerAvatar>()
                    : null;
                if (avatar == null || !avatar.IsSpawned || !avatar.IsBoardReady)
                {
                    return false;
                }
            }

            return true;
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
                _networkSceneManager.OnUnloadEventCompleted -=
                    OnNetworkUnloadEventCompleted;
                _networkSceneManager.OnUnloadComplete -=
                    OnNetworkUnloadComplete;
            }

            _networkSceneManager = sceneManager;
            if (_networkSceneManager != null)
            {
                _networkSceneManager.OnLoadEventCompleted += OnNetworkLoadEventCompleted;
                _networkSceneManager.OnUnloadEventCompleted +=
                    OnNetworkUnloadEventCompleted;
                _networkSceneManager.OnUnloadComplete +=
                    OnNetworkUnloadComplete;
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

            // A reconnect timeout may switch the session back to Lobby while
            // NGO is still completing the original Board load. The queued
            // return owns cleanup now; never turn that late callback into a
            // second, room-closing network failure.
            if (IsMatchLobbyReturnOwned)
            {
                ScheduleCompletedMatchUnloadForNextFrame();
                return;
            }

            var activeMatch = NetworkMatchState.Instance;
            if (activeMatch != null &&
                activeMatch.IsSpawned &&
                activeMatch.GameplayEnabled)
            {
                return;
            }

            if (_pendingBoardLoadReconnect)
            {
                AdvancePendingBoardLoadReconnect();
                return;
            }

            var completedCount = clientsCompleted != null ? clientsCompleted.Count : 0;
            var timedOutCount = clientsTimedOut != null ? clientsTimedOut.Count : 0;
            if (timedOutCount > 0 ||
                completedCount != MultiplayerConstants.MaxPlayers ||
                !HasExactlyFourAssignedNetworkPlayers(_networkManager))
            {
                EndSessionAfterNetworkFailure(GameText.T(
                    "Four-player Board synchronization failed. The session is ending."));
                return;
            }

            var matchState = NetworkMatchState.Instance;
            if (matchState == null || !matchState.IsSpawned)
            {
                EndSessionAfterNetworkFailure(GameText.T(
                    "The Board network state was not created. The session is ending."));
                return;
            }

            var allPlayersReady =
                HasExactlyFourBoardReadyNetworkPlayers(_networkManager);
            var startupDisposition = BoardMapStartupPolicy.Evaluate(
                matchState.IsBoardMapReady,
                allPlayersReady,
                matchState.BoardMapLoadFailure);
            if (startupDisposition == BoardMapStartupDisposition.FailMatch)
            {
                EndActiveMatchForNetworkFailure(
                    matchState.BoardMapLoadFailure);
                return;
            }

            if (startupDisposition != BoardMapStartupDisposition.Ready)
            {
                BeginPendingBoardMapReadinessWait();
                return;
            }

            if (matchState.EnableGameplayOnServer())
            {
                SetLocalizedStatus(
                    "All four players loaded the Board. Gameplay input is enabled.");
            }
        }

        private void OnNetworkUnloadEventCompleted(
            string sceneName,
            LoadSceneMode _,
            List<ulong> clientsCompleted,
            List<ulong> clientsTimedOut)
        {
            if (!_completedMatchLobbyReturnInProgress ||
                !string.Equals(
                    sceneName,
                    _completedMatchSceneBeingUnloaded,
                    StringComparison.Ordinal))
            {
                return;
            }

            _completedMatchSceneBeingUnloaded = string.Empty;
            _completedMatchSceneOperationDeadline = 0d;
            _completedMatchPendingUnloadClients.Clear();
            _completedMatchPendingUnloadScene = sceneName;
            if (_networkManager != null)
            {
                foreach (var clientId in _networkManager.ConnectedClientsIds)
                {
                    if (clientsCompleted == null ||
                        !clientsCompleted.Contains(clientId))
                    {
                        _completedMatchPendingUnloadClients.Add(clientId);
                    }
                }
            }

            if (_completedMatchPendingUnloadClients.Count > 0)
            {
                _completedMatchPendingUnloadDeadline =
                    Time.realtimeSinceStartupAsDouble +
                    CompletedMatchUnloadClientGraceSeconds;
                SetLocalizedStatus(
                    "Waiting for {0} player(s) to finish unloading {1}.",
                    _completedMatchPendingUnloadClients.Count,
                    sceneName);
                return;
            }

            _completedMatchPendingUnloadScene = string.Empty;
            _completedMatchPendingUnloadDeadline = 0d;
            // NGO does not permit another scene event from inside its completion
            // notification. The next unload starts from Update on the next frame.
            ScheduleCompletedMatchUnloadForNextFrame();
        }

        private void OnNetworkUnloadComplete(ulong clientId, string sceneName)
        {
            if (!_completedMatchLobbyReturnInProgress ||
                !string.Equals(
                    sceneName,
                    _completedMatchPendingUnloadScene,
                    StringComparison.Ordinal) ||
                !_completedMatchPendingUnloadClients.Remove(clientId))
            {
                return;
            }

            if (_completedMatchPendingUnloadClients.Count == 0)
            {
                _completedMatchPendingUnloadScene = string.Empty;
                _completedMatchPendingUnloadDeadline = 0d;
                ScheduleCompletedMatchUnloadForNextFrame();
            }
        }

        private void ResolveCompletedMatchUnloadTimeout()
        {
            if (_completedMatchPendingUnloadClients.Count == 0)
            {
                _completedMatchPendingUnloadDeadline = 0d;
                return;
            }

            var timedOutClients = new List<ulong>(
                _completedMatchPendingUnloadClients);
            var timedOutScene = _completedMatchPendingUnloadScene;
            _completedMatchPendingUnloadClients.Clear();
            _completedMatchPendingUnloadScene = string.Empty;
            _completedMatchPendingUnloadDeadline = 0d;

            var manager = _networkManager != null
                ? _networkManager
                : NetworkManager.Singleton;
            if (manager != null && manager.IsServer)
            {
                for (var index = 0; index < timedOutClients.Count; index++)
                {
                    var clientId = timedOutClients[index];
                    if (clientId != NetworkManager.ServerClientId &&
                        manager.ConnectedClients.ContainsKey(clientId))
                    {
                        manager.DisconnectClient(clientId);
                    }
                }
            }

            Debug.LogWarning(
                "Stopped waiting for " + timedOutClients.Count +
                " client(s) that did not finish unloading " +
                timedOutScene + " within " +
                CompletedMatchUnloadClientGraceSeconds + " seconds.");
            SetLocalizedStatus(
                "Continuing lobby return after disconnecting clients that " +
                "could not finish the scene unload.");
            ScheduleCompletedMatchUnloadForNextFrame();
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

            _sessionOperations.TryEnqueue(PublishLocalNetworkClientIdSafelyAsync);
        }

        private async Task PublishLocalNetworkClientIdSafelyAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                await PublishLocalNetworkClientIdWhenReadyAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The controller lifetime ended while the shared publish settled.
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
            ResolveCompletedMatchPendingUnloadClient(clientId);
            if (_applicationQuitting ||
                LifecycleState == SessionLifecycleState.Leaving ||
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
                var disposition =
                    CompletedMatchReturnRules.GetRemoteDisconnectDisposition(
                        remoteClientLost,
                        _sessions.Current.Phase ==
                        MultiplayerConstants.LobbyPhase,
                        IsMatchLobbyReturnOwned,
                        IsFinalRankingLocked());
                if (disposition ==
                    RemoteDisconnectDisposition.QueueLobbyCleanup)
                {
                    // Capture the service session identity now. The delayed cleanup must
                    // never target a replacement session that reuses the NGO client ID.
                    var expectedSessionId = _sessions.CurrentSessionId;
                    QueueDisconnectedLobbyPlayerCleanup(
                        expectedSessionId,
                        clientId);
                }
                else if (disposition ==
                         RemoteDisconnectDisposition.DeferCleanupUntilLobby)
                {
                    _completedMatchDisconnectedClients.Add(clientId);
                    SetLocalizedStatus(
                        "A player disconnected while the completed match was returning to the lobby.");
                }
                else if (disposition ==
                         RemoteDisconnectDisposition.LeaveCompletedMatch)
                {
                    // The seat is removed from the session service once the
                    // room is back in the lobby phase.
                    _completedMatchDisconnectedClients.Add(clientId);
                    NetworkMatchState.Instance?.MarkCeremonyDepartureOnServer();
                    SetLocalizedStatus(
                        "A player left the room after the award ceremony.");
                }
                else if (disposition ==
                         RemoteDisconnectDisposition.PauseForReconnect)
                {
                    TrackReconnectGraceDisconnect(clientId);
                    var match = NetworkMatchState.Instance;
                    if (match != null && match.IsSpawned && match.GameplayEnabled)
                    {
                        match.PauseForReconnectOnServer(clientId);
                        SetLocalizedStatus(
                            "A player disconnected. Gameplay is paused for the 60-second reconnect window.");
                    }
                    else
                    {
                        BeginPendingBoardLoadReconnect();
                    }
                }
            }
            else if (localClientLost)
            {
                SetLocalizedStatus(
                    "Relay connection lost. Waiting for the host or session service.");
            }
        }

        private void QueueCompletedMatchDisconnectedLobbyCleanups()
        {
            if (_completedMatchDisconnectedClients.Count == 0 ||
                !_completedMatchLobbyReturnInProgress ||
                _sessions == null ||
                !_sessions.IsInSession ||
                !_sessions.Current.IsHost ||
                _sessions.Current.Phase != MultiplayerConstants.LobbyPhase)
            {
                return;
            }

            var expectedSessionId = _sessions.CurrentSessionId;
            var disconnectedClients = new List<ulong>(
                _completedMatchDisconnectedClients);
            _completedMatchDisconnectedClients.Clear();
            for (var index = 0; index < disconnectedClients.Count; index++)
            {
                QueueDisconnectedLobbyPlayerCleanup(
                    expectedSessionId,
                    disconnectedClients[index]);
            }
        }

        private void ResolveCompletedMatchPendingUnloadClient(ulong clientId)
        {
            if (!_completedMatchLobbyReturnInProgress ||
                !_completedMatchPendingUnloadClients.Remove(clientId) ||
                _completedMatchPendingUnloadClients.Count > 0)
            {
                return;
            }

            _completedMatchPendingUnloadScene = string.Empty;
            _completedMatchPendingUnloadDeadline = 0d;
            ScheduleCompletedMatchUnloadForNextFrame();
        }

        private void OnClientStopped(bool _)
        {
            if (_applicationQuitting ||
                LifecycleState == SessionLifecycleState.Leaving ||
                _networkManager == null ||
                _networkManager.IsServer ||
                _sessions == null ||
                !_sessions.IsInSession)
            {
                return;
            }

            SetLocalizedStatus(
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

            _pendingLobbyCleanupTasks[cleanupKey] =
                RemoveDisconnectedLobbyPlayerAfterSettleAsync(
                    expectedSessionId,
                    clientId,
                    cleanupKey,
                    _sessionOperations.LifetimeToken);
        }


        private async Task RemoveDisconnectedLobbyPlayerAfterSettleAsync(
            string expectedSessionId,
            ulong clientId,
            string cleanupKey,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(
                    LobbyDisconnectSettleMilliseconds,
                    cancellationToken);
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
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    throw;
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
                        SetLocalizedStatus(
                            "A disconnected lobby player was removed and the seat is open.");
                    }

                    return;
                }

                // A normal Leave may have published its marker but not completed yet.
                // Wait outside the provider mutation gate, then perform one bounded
                // fallback that ignores a stale marker.
                await Task.Delay(
                    LobbyDisconnectForcedCleanupGraceMilliseconds,
                    cancellationToken);
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
                    cancellationToken.ThrowIfCancellationRequested();
                    if (removed && IsExpectedLobbySession(expectedSessionId))
                    {
                        SetLocalizedStatus(
                            "A stale disconnected lobby player was removed after the grace period.");
                    }
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    if (IsExpectedLobbySession(expectedSessionId))
                    {
                        SetLocalizedStatus(
                            "Could not remove the disconnected lobby player: {0}",
                            exception.Message);
                        Debug.LogWarning(_status);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown cancels the bounded cleanup waits.
            }
            finally
            {
                _pendingLobbyCleanupKeys.Remove(cleanupKey);
                _pendingLobbyCleanupTasks.Remove(cleanupKey);
            }
        }

        private bool IsExpectedSession(string expectedSessionId)
        {
            return
                !_destroyed &&
                !_applicationQuitting &&
                LifecycleState != SessionLifecycleState.Leaving &&
                !_networkTerminationRequested &&
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
            ServerEventTrace.DumpToLog();
            EndSessionAfterNetworkFailure(reason);
        }

        // TODO(STEAM-LIFECYCLE): Steam lobby callbacks should forward host departure to
        // IOnlineSessionProvider.Ended. The fixed-four match ends instead of migrating
        // hosts. A Steam transport must preserve the same client/member identity mapping.
    }
}


using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
        private bool _lobbyRenderingEnabled = true;

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
                if (_ceremonyWaitingRoomStatusShown && IsInSession)
                {
                    // The "others are still at the ceremony" line is stale now.
                    SetLocalizedStatus("Returned to the player ready screen.");
                }
            }
        }

        private void SetLobbyRendering(bool enabled)
        {
            _lobbyRenderingEnabled = enabled;
            lobbyView?.SetPresentationVisible(enabled);
            if (enabled)
            {
                RefreshLobbyWorldNameplates();
            }
            else
            {
                RefreshLobbyWorldNameplates(SessionSnapshot.Empty, false);
            }

            var arena = LobbyArena.Instance;
            if (arena == null)
            {
                arena = FindAnyObjectByType<LobbyArena>(
                    FindObjectsInactive.Include);
            }

            arena?.SetPresentationVisible(enabled);

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
            foreach (var definition in MinigameCatalog.RegisteredMinigames)
            {
                var minigameScene = SceneManager.GetSceneByName(
                    definition.SceneName);
                if (minigameScene.IsValid() && minigameScene.isLoaded)
                {
                    SceneManager.UnloadSceneAsync(minigameScene);
                }
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
            _ceremonyWaitingRoomStatusShown = false;
            _statusUsesLocalization = false;
            _localizedStatus = default;
            _status = status ?? string.Empty;
            RenderLobby();
        }

        private void SetLocalizedStatus(string source, params object[] arguments)
        {
            _ceremonyWaitingRoomStatusShown = false;
            _statusUsesLocalization = true;
            _localizedStatus = new LocalizedMessage(source, arguments);
            _status = _localizedStatus.Resolve();
            RenderLobby();
        }

        private void RenderLobby()
        {
            if (_statusUsesLocalization)
            {
                _status = _localizedStatus.Resolve();
            }

            var inSession = _sessions != null && _sessions.IsInSession;
            var snapshot = inSession ? _sessions.Current : SessionSnapshot.Empty;
            RefreshLobbyWorldNameplates(
                snapshot,
                inSession && _lobbyRenderingEnabled);
            if (lobbyView == null)
            {
                return;
            }

            ApplyMatchRecoveryPresentation();
            lobbyView.Render(snapshot, inSession, IsBusy, _status);
        }

        public void RefreshLobbyWorldNameplates()
        {
            var inSession = _sessions != null && _sessions.IsInSession;
            RefreshLobbyWorldNameplates(
                inSession ? _sessions.Current : SessionSnapshot.Empty,
                inSession && _lobbyRenderingEnabled);
        }

        private static void RefreshLobbyWorldNameplates(
            SessionSnapshot snapshot,
            bool lobbyPresentationEnabled)
        {
            var showLobbyState = ShouldShowLobbyWorldIdentity(
                snapshot,
                lobbyPresentationEnabled);
            var avatars = FindObjectsByType<NetworkPlayerAvatar>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var avatarIndex = 0; avatarIndex < avatars.Length; avatarIndex++)
            {
                var avatar = avatars[avatarIndex];
                var ready = false;
                var host = false;
                if (avatar != null)
                {
                    TryResolveLobbyWorldIdentity(
                        snapshot,
                        showLobbyState,
                        avatar.AssignedSlot,
                        out ready,
                        out host);
                }

                avatar?.AvatarVisual?.SetLobbyIdentityState(ready, host);
            }
        }

        internal static bool ShouldShowLobbyWorldIdentity(
            SessionSnapshot snapshot,
            bool lobbyPresentationEnabled)
        {
            return lobbyPresentationEnabled &&
                   snapshot != null &&
                   snapshot.Phase == MultiplayerConstants.LobbyPhase;
        }

        internal static bool TryResolveLobbyWorldIdentity(
            SessionSnapshot snapshot,
            bool showLobbyState,
            int assignedSlot,
            out bool ready,
            out bool host)
        {
            ready = false;
            host = false;
            if (!showLobbyState || snapshot == null || assignedSlot < 0)
            {
                return false;
            }

            for (var playerIndex = 0;
                 playerIndex < snapshot.Players.Count;
                 playerIndex++)
            {
                var player = snapshot.Players[playerIndex];
                if (player.Slot != assignedSlot)
                {
                    continue;
                }

                ready = player.IsReady;
                host = player.IsHost;
                return true;
            }

            return false;
        }
    }
}

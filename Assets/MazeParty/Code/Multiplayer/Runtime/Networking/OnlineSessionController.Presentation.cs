using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
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
            lobbyView?.SetPresentationVisible(enabled);

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
            if (lobbyView == null)
            {
                return;
            }

            if (_statusUsesLocalization)
            {
                _status = _localizedStatus.Resolve();
            }

            var inSession = _sessions != null && _sessions.IsInSession;
            var snapshot = inSession ? _sessions.Current : SessionSnapshot.Empty;
            ApplyMatchRecoveryPresentation();
            lobbyView.Render(snapshot, inSession, IsBusy, _status);
        }
    }
}

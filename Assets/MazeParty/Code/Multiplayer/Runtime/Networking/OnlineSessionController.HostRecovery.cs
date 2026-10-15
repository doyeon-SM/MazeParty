using System;
using System.Threading.Tasks;
using MazeParty.Gameplay.Minigames;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
        private async Task PrepareOriginalHostRecoveryAsync()
        {
            var token = _sessionOperations.LifetimeToken;
            var deadline = Time.realtimeSinceStartupAsDouble + 20d;
            while (_networkManager != null &&
                   (_networkManager.IsListening || _networkManager.ShutdownInProgress))
            {
                token.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    throw new TimeoutException("Waiting for the old Relay connection to stop.");
                await Task.Delay(100, token);
            }
            token.ThrowIfCancellationRequested();
            ResetPendingBoardLoadReconnect();
            ClearReconnectGraceDisconnectTracking();
            ResetCompletedMatchLobbyReturnState();
            _networkIdentityPublished = false;
            UnloadBoardLocally();
            while (HasLoadedRecoveryGameScene())
            {
                token.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    throw new TimeoutException("Waiting for the interrupted game scenes to unload.");
                await Task.Delay(100, token);
            }
            SetLobbyRendering(true);
            SetLocalizedStatus("Waiting for the original host. The room and saved match are preserved.");
        }

        private static bool HasLoadedRecoveryGameScene()
        {
            var board = SceneManager.GetSceneByName(MultiplayerConstants.BoardScene);
            if (board.IsValid() && board.isLoaded) return true;
            foreach (var game in MinigameCatalog.RegisteredMinigames)
            {
                var scene = SceneManager.GetSceneByName(game.SceneName);
                if (scene.IsValid() && scene.isLoaded) return true;
            }
            return false;
        }

        private void OnOriginalHostNetworkRestored()
        {
            if (_destroyed || _applicationQuitting) return;
            PrepareHostMatchRecoveryChoice();
            _sessionOperations.TryEnqueue(PublishLocalNetworkClientIdSafelyAsync);
        }
    }
}

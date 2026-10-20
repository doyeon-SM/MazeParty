using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    internal static class OnlineLoadingPolicy
    {
        internal static bool ShouldShow(
            SessionLifecycleState state, bool busy, bool inSession, string phase,
            bool boardSceneLoaded, bool boardReady, bool hasPriorityPresentation)
        {
            if (hasPriorityPresentation ||
                state == SessionLifecycleState.ReturningToLobby ||
                state == SessionLifecycleState.Leaving ||
                state == SessionLifecycleState.Terminating ||
                state == SessionLifecycleState.Disposing ||
                state == SessionLifecycleState.Disposed ||
                (inSession && phase == MultiplayerConstants.HostRecoveryPhase))
                return false;

            if (busy && (state == SessionLifecycleState.Connecting ||
                         state == SessionLifecycleState.Reconnecting ||
                         state == SessionLifecycleState.StartingMatch))
                return true;

            // Scene activation and the service's phase update can arrive in
            // either order. Keep covering both until NGO and the map are ready.
            return inSession && !boardReady &&
                (phase == MultiplayerConstants.PlayingPhase || boardSceneLoaded);
        }
    }

    public sealed partial class OnlineSessionController
    {
        [SerializeField] private OnlineLoadingView loadingView;

        public void ConfigureLoadingView(OnlineLoadingView view) => loadingView = view;

        private void RefreshLoadingPresentation()
        {
            if (loadingView == null) return;
            var match = NetworkMatchState.Instance;
            var player = _networkManager != null && _networkManager.SpawnManager != null
                ? _networkManager.SpawnManager.GetLocalPlayerObject() : null;
            var avatar = player != null ? player.GetComponent<NetworkPlayerAvatar>() : null;
            var boardReady = match != null && match.IsSpawned &&
                match.IsBoardMapReady && match.GameplayEnabled &&
                avatar != null && avatar.IsSpawned && avatar.IsBoardReady;
            var priorityPresentation = _destroyed || _applicationQuitting ||
                IsMatchLobbyReturnOwned || IsBackInWaitingRoomDuringCeremony ||
                IsWaitingForReconnect ||
                (match != null && (!string.IsNullOrEmpty(match.BoardMapLoadFailure) ||
                                   match.IsFinalRankingLocked));
            loadingView.SetVisible(OnlineLoadingPolicy.ShouldShow(
                LifecycleState, IsBusy, IsInSession, CurrentSession.Phase,
                SceneManager.GetSceneByName(MultiplayerConstants.BoardScene).isLoaded,
                boardReady, priorityPresentation));
        }
    }
}

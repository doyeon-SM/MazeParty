using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private void RequestSelectedMinigameLoadOnServer()
        {
            if (!IsServer ||
                !_activeMatchVoidGate.AllowsGameplayMutation ||
                !GameplayEnabled ||
                NetworkManager == null ||
                NetworkManager.SceneManager == null)
            {
                return;
            }

            var sceneName = GetSelectedMinigameSceneName();
            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                _selectedMinigameNetworkLoadCompleted = true;
                return;
            }

            _selectedMinigameNetworkLoadCompleted = false;
            var status = NetworkManager.SceneManager.LoadScene(
                sceneName,
                LoadSceneMode.Additive);
            if (status != SceneEventProgressStatus.Started)
            {
                OnlineSessionController.Instance?.EndActiveMatchForNetworkFailure(
                    GameText.F(
                        "Could not synchronize the {0} minigame scene: {1}",
                        CurrentMinigame,
                        status));
            }
        }

        private void OnNetworkLoadEventCompleted(
            string sceneName,
            LoadSceneMode _,
            List<ulong> clientsCompleted,
            List<ulong> clientsTimedOut)
        {
            var selectedSceneName = GetSelectedMinigameSceneName();
            if (!IsServer ||
                !_activeMatchVoidGate.AllowsGameplayMutation ||
                !GameplayEnabled ||
                string.IsNullOrEmpty(selectedSceneName) ||
                sceneName != selectedSceneName)
            {
                return;
            }

            var scene = SceneManager.GetSceneByName(selectedSceneName);
            _selectedMinigameNetworkLoadCompleted =
                scene.IsValid() && scene.isLoaded;
            if (!_selectedMinigameNetworkLoadCompleted)
            {
                OnlineSessionController.Instance?.EndActiveMatchForNetworkFailure(
                    GameText.F(
                        "{0} scene synchronization completed without a loaded scene.",
                        CurrentMinigame));
                return;
            }

            // A disconnected client may appear in clientsTimedOut while the global
            // reconnect pause is active. The replacement client is synchronized to
            // every server-loaded scene before its Board-ready handshake completes.
            if (!_reconnectPaused.Value && clientsTimedOut != null &&
                clientsTimedOut.Count > 0)
            {
                OnlineSessionController.Instance?.EndActiveMatchForNetworkFailure(
                    GameText.F(
                        "{0} scene synchronization timed out for a player.",
                        CurrentMinigame));
            }
        }

        private void TryStartLoadedMinigameOnServer(double now)
        {
            if (!IsServer ||
                !_activeMatchVoidGate.AllowsGameplayMutation ||
                !GameplayEnabled ||
                _flow == null || _flow.State != BoardFlowState.MinigameLoading ||
                _flow.IsPaused || IsSimulationSuspended ||
                !_selectedMinigameNetworkLoadCompleted ||
                !HasFourBoardReadyPlayers())
            {
                return;
            }

            if (!TryGetMinigameRuntime(CurrentMinigame, out var runtime) ||
                runtime == null ||
                !runtime.IsSpawned)
            {
                return;
            }

            if (_flow.TryBeginMinigame(now))
            {
                runtime.BeginMatchOnServer(_currentMinigameSeed.Value);
            }
        }

        private bool TryInitializeMinigameScheduleOnServer()
        {
            if (!_activeMatchVoidGate.AllowsGameplayMutation)
            {
                return false;
            }

            try
            {
                _minigameScheduleSession ??=
                    new HostMinigameScheduleSession();
                // The saved order is reused only when exactly the same players
                // restart; any other group gets a new schedule.
                var controller = OnlineSessionController.Instance;
                var rosterKey = controller != null
                    ? controller.GetMatchRosterKey()
                    : string.Empty;
                if (controller != null && controller.ShouldResumeSavedMatch)
                {
                    if (!_minigameScheduleSession.TryGetSavedMatch(
                            out _,
                            out var savedRosterKey) ||
                        !MinigameScheduleRoster.CanReuse(
                            savedRosterKey,
                            rosterKey))
                    {
                        OnlineSessionController.Instance?.EndActiveMatchForNetworkFailure(
                            GameText.T(
                                "The saved match requires the same four accounts."));
                        return false;
                    }
                }
                _minigameSchedule =
                    _minigameScheduleSession.LoadOrCreateActive(
                        MinigameScheduleRules.DefaultTurnCount,
                        rosterKey);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                // Never silently reroll this match, but do not let an unreadable
                // or outdated saved order block every restart of these players.
                try
                {
                    if (OnlineSessionController.Instance == null ||
                        !OnlineSessionController.Instance.ShouldResumeSavedMatch)
                    {
                        _minigameScheduleSession?.CompleteActive();
                    }
                }
                catch (Exception cleanupException)
                {
                    Debug.LogWarning(
                        "Could not discard the unreadable minigame schedule: " +
                        cleanupException.Message);
                }

                OnlineSessionController.Instance?.EndActiveMatchForNetworkFailure(
                    GameText.T("The persisted minigame schedule could not be restored."));
                return false;
            }
        }

        private void RevealScheduledMinigameOnServer(int turn)
        {
            if (!_activeMatchVoidGate.AllowsGameplayMutation)
            {
                return;
            }

            if (_minigameSchedule == null &&
                !TryInitializeMinigameScheduleOnServer())
            {
                return;
            }

            var selected =
                _minigameSchedule.GetMinigameForTurn(turn);
            _currentMinigame.Value = (byte)selected;
            _remainingMinigameSlots.Value =
                Math.Max(0, _minigameSchedule.TurnCount - turn + 1);
            _currentMinigameSeed.Value = DeriveMinigameSeed(
                _minigameSchedule.Seed,
                turn);
            _selectedMinigameNetworkLoadCompleted = false;
            _scheduledSkipPaused = false;
            _pausedScheduledSkipRemaining = 0d;
            _scheduledSkipAt =
                selected == ScheduledMinigameId.Skip
                    ? ServerNow + SkipRevealSeconds
                    : 0d;
            _minigameRevealRevision.Value++;
        }

        private string GetSelectedMinigameSceneName()
        {
            return CurrentMinigame == ScheduledMinigameId.Skip
                ? string.Empty
                : MinigameCatalog.GetSceneName(CurrentMinigame);
        }

        private static ulong DeriveMinigameSeed(int scheduleSeed, int turn)
        {
            var seed = unchecked(
                ((ulong)(uint)scheduleSeed << 32) |
                (uint)turn);
            seed ^= 0x9E3779B97F4A7C15UL;
            seed ^= seed >> 30;
            seed *= 0xBF58476D1CE4E5B9UL;
            seed ^= seed >> 27;
            seed *= 0x94D049BB133111EBUL;
            return seed ^ (seed >> 31);
        }
    }
}

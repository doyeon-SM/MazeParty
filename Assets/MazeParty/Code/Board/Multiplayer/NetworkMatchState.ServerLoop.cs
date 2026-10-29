using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        // The order is part of the gameplay contract: reconnect, player pause,
        // then shop reveal. A resumed frame still waits until the next Update.
        private bool TickGameplayPauseOnServer(double now)
        {
            if (_reconnectPaused.Value)
            {
                StopAllAvatarInputOnServer();
                if (CompletedMatchReturnRules.ShouldResumeReconnect(
                        HasFourBoardReadyPlayers(),
                        _reconnectGraceEndsAt.Value,
                        now))
                {
                    ResumeAfterReconnectOnServer(now);
                }
                else if (!_endingForReconnectTimeout &&
                         CompletedMatchReturnRules.HasReconnectGraceExpired(
                             _reconnectGraceEndsAt.Value,
                             now))
                {
                    var controller = OnlineSessionController.Instance;
                    if (controller != null &&
                        controller.VoidActiveMatchToLobbyOnServer(
                            GameText.T(
                                "A player did not reconnect within 3 minutes. Returning to the waiting room with the saved match preserved."), preserveRecovery: true))
                    {
                        _endingForReconnectTimeout = true;
                    }
                }

                return true;
            }

            if (_playerPauseActive.Value)
            {
                StopAllAvatarInputOnServer();
                if (PlayerPauseRules.HasExpired(_playerPauseEndsAt.Value, now))
                {
                    EndPlayerPauseOnServer(now);
                }

                return true;
            }

            if (_keyShopRevealActive.Value)
            {
                StopAllAvatarInputOnServer();
                AdvanceKeyShopRevealOnServer(now);

                return true;
            }

            return false;
        }

        private void TickBoardFlowOnServer(double now)
        {
            EnsureFlowModel();
            TickConfiguredSeries();
            var deferActionTimeout =
                ShouldDeferActionTimeoutForWorldDie();
            AdvanceDeathGoldPresentationOnServer(now);
            ForEachAvatar(avatar => avatar.AdvanceBoardDeathOnServer(now));
            if (_flow.State == BoardFlowState.LandingEffectResolve)
            {
                // Settle every due landing step before the flow checks its exact
                // boundary. Damage can begin a board death on this same frame.
                AdvanceLandingEffectResolutionOnServer(now);
            }
            var boardDeathInProgress = HasBoardDeathInProgressOnServer();
            AdvanceMagicMazeOnServer(now);
            _flow.Tick(
                now,
                deferActionTimeout,
                boardDeathInProgress,
                ShouldDeferLandingEffectResolutionOnServer(
                    boardDeathInProgress));
            if (_flow.State == BoardFlowState.MatchComplete)
            {
                // Ceremony simulation gates movement per seat; clearing input here
                // would cancel walking every server frame after final results.
                AdvanceAwardCeremonyOnServer(now);
                return;
            }
            if (deferActionTimeout &&
                _flow.State == BoardFlowState.Action &&
                _flow.GetActionRemaining(now) <= 0d)
            {
                StopAllAvatarInputOnServer();
                ResolveExpiredPersonalChoicesOnServer(now);
                return;
            }

            if (_flow.State == BoardFlowState.Action)
            {
                AdvanceArrivalGraceOnServer(now);
            }
            if (_flow.State == BoardFlowState.MinigameIntroReady &&
                CurrentMinigame == ScheduledMinigameId.Skip &&
                _scheduledSkipAt > 0d &&
                now >= _scheduledSkipAt)
            {
                _scheduledSkipAt = 0d;
                _scheduledSkipPaused = false;
                _pausedScheduledSkipRemaining = 0d;
                _flow.TrySkipMinigame(now);
            }
            if (_flow.State == BoardFlowState.MinigameIntroReady &&
                CurrentMinigame != ScheduledMinigameId.Skip &&
                _flow.GetStateRemaining(now) <= 0d)
            {
                _readyMask.Value = (byte)AllPlayersMask;
                _flow.TryBeginMinigameLoading(now);
            }
            TryStartLoadedMinigameOnServer(now);
            if (_flow.State == BoardFlowState.MinigameLoading &&
                !_endingForMinigameLoadingTimeout &&
                _flow.GetStateRemaining(now) <= 0d)
            {
                _endingForMinigameLoadingTimeout = true;
                OnlineSessionController.Instance?.EndActiveMatchForNetworkFailure(
                    GameText.F(
                        "{0} minigame did not finish loading within 60 seconds.",
                        CurrentMinigame));
            }
            AdvanceCombatOnServer(now);
            AdvanceLandingEffectResolutionOnServer(now);
            ResolveExpiredPersonalChoicesOnServer(now);
            AdvanceKeyShopLifecycleOnServer(now);
        }
    }
}

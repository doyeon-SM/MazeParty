using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Host decision for a valid crash-recovery journal found after creating a
    /// new room. Fresh is also the safe default when no journal exists.
    /// </summary>
    public enum HostMatchRecoveryChoice : byte
    {
        Fresh = 0,
        Resume = 1
    }

    public sealed partial class OnlineSessionController
    {
        private HostMatchRecoveryChoice _hostMatchRecoveryChoice =
            HostMatchRecoveryChoice.Fresh;
        private bool _matchRecoveryChoiceVisible;

        public bool ShouldResumeSavedMatch =>
            _hostMatchRecoveryChoice == HostMatchRecoveryChoice.Resume;

        public bool IsMatchRecoveryChoiceVisible =>
            _matchRecoveryChoiceVisible;

        /// <summary>
        /// Resolves the current room's authenticated account identity for a
        /// server-assigned seat. The raw id is used only in memory; recovery
        /// persistence stores its one-way player key.
        /// </summary>
        public bool TryGetMatchPlayerIdForSlot(int slot, out string playerId)
        {
            playerId = string.Empty;
            if (_sessions == null || !_sessions.IsInSession ||
                slot < 0 || slot >= MultiplayerConstants.MaxPlayers)
            {
                return false;
            }

            var players = _sessions.Current.Players;
            for (var index = 0; index < players.Count; index++)
            {
                var player = players[index];
                if (player.Slot != slot ||
                    string.IsNullOrWhiteSpace(player.PlayerId))
                {
                    continue;
                }

                playerId = player.PlayerId;
                return true;
            }

            return false;
        }

        private void PrepareHostMatchRecoveryChoice()
        {
            ResetHostMatchRecoveryChoice();
            if (_sessions == null || !_sessions.IsInSession ||
                !_sessions.Current.IsHost ||
                _sessions.Current.Phase != MultiplayerConstants.LobbyPhase)
            {
                return;
            }

            var scheduleSession = new HostMinigameScheduleSession();
            if (!scheduleSession.TryGetSavedMatch(out _, out _))
            {
                return;
            }

            MatchRecoveryLoadStatus status;
            try
            {
                status = scheduleSession.TryPeekRecovery(out _);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not inspect the saved match recovery journal: " +
                    exception.Message);
                // A sharing violation or other transient I/O failure does not
                // prove that the only recovery copy is corrupt. Keep it and
                // let the host retry Continue or explicitly discard it.
                _matchRecoveryChoiceVisible = true;
                ApplyMatchRecoveryPresentation();
                SetLocalizedStatus(
                    "The saved match could not be inspected. Continue to retry, or discard it.");
                return;
            }

            if (status == MatchRecoveryLoadStatus.Loaded)
            {
                _matchRecoveryChoiceVisible = true;
                ApplyMatchRecoveryPresentation();
                SetLocalizedStatus(
                    "A saved match is available. Continue it, or discard it and start a new match.");
                return;
            }

            // A schedule without a valid stable checkpoint cannot be resumed.
            // Discard it explicitly here and tell the host instead of silently
            // reusing its queue for a fresh board.
            scheduleSession.CompleteActive();
            ApplyMatchRecoveryPresentation();
            if (status == MatchRecoveryLoadStatus.Expired)
            {
                SetLocalizedStatus(
                    "The saved match expired after 72 hours and was discarded.");
            }
            else
            {
                SetLocalizedStatus(
                    "The saved match was incomplete or damaged and was discarded.");
            }
        }

        private void OnRecoveryContinueRequested()
        {
            if (!_matchRecoveryChoiceVisible ||
                _sessions == null || !_sessions.IsInSession ||
                !_sessions.Current.IsHost)
            {
                return;
            }

            _hostMatchRecoveryChoice = HostMatchRecoveryChoice.Resume;
            _matchRecoveryChoiceVisible = false;
            ApplyMatchRecoveryPresentation();
            SetLocalizedStatus(
                "Saved match selected. The same four accounts must be ready before continuing.");
        }

        private void ValidateSelectedMatchRecoveryBeforeStart()
        {
            if (!ShouldResumeSavedMatch)
            {
                return;
            }

            var scheduleSession = new HostMinigameScheduleSession();
            if (!scheduleSession.TryGetSavedMatch(
                    out _,
                    out var savedRosterKey) ||
                !MinigameScheduleRoster.CanReuse(
                    savedRosterKey,
                    GetMatchRosterKey()))
            {
                RestoreMatchRecoveryChoiceForRetry();
                throw new InvalidOperationException(
                    GameText.T("The saved match requires the same four accounts."));
            }

            MatchRecoveryLoadStatus status;
            try
            {
                status = scheduleSession.TryPeekRecovery(out _);
            }
            catch (Exception exception)
            {
                RestoreMatchRecoveryChoiceForRetry();
                throw new InvalidOperationException(
                    GameText.T(
                        "The saved match could not be inspected. Continue to retry, or discard it."),
                    exception);
            }

            if (status == MatchRecoveryLoadStatus.Loaded)
            {
                return;
            }

            RestoreMatchRecoveryChoiceForRetry();
            var source = status == MatchRecoveryLoadStatus.Expired
                ? "The saved match expired after 72 hours."
                : status == MatchRecoveryLoadStatus.Corrupt
                    ? "The saved match is incomplete or damaged."
                    : "No recoverable saved match was found.";
            throw new InvalidOperationException(GameText.T(source));
        }

        private void RestoreMatchRecoveryChoiceForRetry()
        {
            _hostMatchRecoveryChoice = HostMatchRecoveryChoice.Fresh;
            _matchRecoveryChoiceVisible = true;
            ApplyMatchRecoveryPresentation();
        }

        private void OnRecoveryDiscardRequested()
        {
            if (!_matchRecoveryChoiceVisible ||
                _sessions == null || !_sessions.IsInSession ||
                !_sessions.Current.IsHost)
            {
                return;
            }

            new HostMinigameScheduleSession().CompleteActive();
            ResetHostMatchRecoveryChoice();
            SetLocalizedStatus(
                "The saved match was discarded. This room will start a new match.");
        }

        private void ResetHostMatchRecoveryChoice()
        {
            _hostMatchRecoveryChoice = HostMatchRecoveryChoice.Fresh;
            _matchRecoveryChoiceVisible = false;
            ApplyMatchRecoveryPresentation();
        }

        private void ApplyMatchRecoveryPresentation()
        {
            lobbyView?.SetRecoveryChoice(
                _matchRecoveryChoiceVisible,
                !IsBusy);
        }
    }
}

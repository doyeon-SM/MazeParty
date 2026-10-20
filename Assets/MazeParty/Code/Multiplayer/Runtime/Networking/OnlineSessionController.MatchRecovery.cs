using System;
using System.Threading.Tasks;
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
        private bool _savedMatchSelectionPending;

        public bool ShouldResumeSavedMatch =>
            _hostMatchRecoveryChoice == HostMatchRecoveryChoice.Resume;

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

        private bool _recoverySelectionQueued;

        private void PrepareHostMatchRecoveryChoice()
        {
            if (_creatingFreshRoom || ShouldResumeSavedMatch || _recoverySelectionQueued || _sessions == null || !_sessions.IsInSession ||
                !_sessions.Current.IsHost || _sessions.Current.Phase != MultiplayerConstants.LobbyPhase) return;
            RefreshSavedEntry();
            if (!CanRestartSavedGame) return;
            _savedMatchSelectionPending = true;
            _recoverySelectionQueued = true;
            _sessionOperations.TryEnqueue(async cancellationToken =>
            {
                try { await SelectSavedMatchAsync(); }
                finally { _recoverySelectionQueued = false; RenderLobby(); }
            });
        }

        private async Task SelectSavedMatchAsync()
        {
            if (_sessions == null || !_sessions.IsInSession ||
                !_sessions.Current.IsHost ||
                _sessions.Current.Phase != MultiplayerConstants.LobbyPhase)
            {
                throw new InvalidOperationException(
                    GameText.T("Only the host can select the board map."));
            }

            var scheduleSession = new HostMinigameScheduleSession();
            var status = scheduleSession.TryPeekRecoverySnapshot(
                out var recoverySnapshot);
            if (status != MatchRecoveryLoadStatus.Loaded ||
                recoverySnapshot == null ||
                !BoardMapSelection.TryCreate(
                    recoverySnapshot.boardMapId,
                    recoverySnapshot.boardMapContentVersion,
                    out var savedSelection))
            {
                throw new InvalidOperationException(
                    GameText.T(
                        "The saved match does not contain a valid board map selection."));
            }

            if (!BoardMapRuntimeLoader.TryResolveExactSelection(
                    LoadBoardMapCatalog(),
                    savedSelection,
                    out _,
                    out var error))
            {
                throw new InvalidOperationException(
                    GameText.T(
                        "The selected board map could not be loaded safely."),
                    new InvalidOperationException(error));
            }

            if (!MazeParty.Gameplay.Minigames.MatchGameSettings.TryDecode(recoverySnapshot.gameSettings, out var savedSettings))
                throw new InvalidOperationException("Saved match settings are invalid.");
            if (_sessions.Current.GameSettings?.Encode() != savedSettings.Encode())
                await _sessions.SetGameSettingsAsync(savedSettings);

            if (_sessions.Current.BoardMapSelection != savedSelection)
            {
                await _sessions.SetBoardMapAsync(savedSelection);
            }

            if (_sessions.Current.BoardMapSelection != savedSelection)
            {
                throw new InvalidOperationException(
                    GameText.T(
                        "The saved match board map was not synchronized to the lobby."));
            }

            if (!MatchRecoveryResumeRules.IsOriginalHost(recoverySnapshot, _identity.PlayerId))
                throw new InvalidOperationException(GameText.T("Only the original host can restart this saved game."));
            await ((MpsRelaySessionProvider)_sessions).ApplyRecoveryRosterAsync(
                System.Array.ConvertAll(recoverySnapshot.players, player => player.playerKey),
                System.Array.ConvertAll(recoverySnapshot.players, player => player.displayName));
            _hostMatchRecoveryChoice = HostMatchRecoveryChoice.Resume;
            _savedMatchSelectionPending = false;
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
            MatchRecoverySnapshot recoverySnapshot;
            try
            {
                status = scheduleSession.TryPeekRecoverySnapshot(
                    out recoverySnapshot);
            }
            catch (Exception exception)
            {
                RestoreMatchRecoveryChoiceForRetry();
                throw new InvalidOperationException(
                    GameText.T(
                        "The saved match could not be inspected. Continue to retry, or discard it."),
                    exception);
            }

            if (status == MatchRecoveryLoadStatus.Loaded &&
                (recoverySnapshot == null ||
                 !BoardMapSelection.TryCreate(
                     recoverySnapshot.boardMapId,
                     recoverySnapshot.boardMapContentVersion,
                     out var savedSelection) ||
                 _sessions.Current.BoardMapSelection != savedSelection))
            {
                RestoreMatchRecoveryChoiceForRetry();
                throw new InvalidOperationException(
                    GameText.T(
                        "The saved match does not contain a valid board map selection."));
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
            // Preserve the restart intent so joining the missing original accounts
            // lets the host retry Start without an obsolete choice popup.
            _hostMatchRecoveryChoice = HostMatchRecoveryChoice.Resume;
            _savedMatchSelectionPending = false;
            ApplyMatchRecoveryPresentation();
        }

        private void ResetHostMatchRecoveryChoice()
        {
            _hostMatchRecoveryChoice = HostMatchRecoveryChoice.Fresh;
            _savedMatchSelectionPending = false;
            ApplyMatchRecoveryPresentation();
        }

        private void ApplyMatchRecoveryPresentation()
        {
            lobbyView?.SetBoardMapSelectionLocked(
                IsBoardMapSelectionLocked);
        }
    }
}

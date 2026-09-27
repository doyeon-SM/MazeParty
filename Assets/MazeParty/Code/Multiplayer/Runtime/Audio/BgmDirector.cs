using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Picks the music from the online flow: lobby → waiting room → board →
    /// minigame (its own track when it has clips, else the shared one) →
    /// award ceremony → waiting room. A player who cleaned up the board
    /// hears the waiting-room music. A game pause switches the mixer to the
    /// Paused snapshot. Lives on the SoundSystem prefab and idles outside
    /// online play (solo testers, testbeds).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BgmDirector : MonoBehaviour
    {
        private const float RefreshIntervalSeconds = 0.25f;

        private float _nextRefreshAt;

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshAt)
            {
                return;
            }

            _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
            var controller = OnlineSessionController.Instance;
            if (controller == null)
            {
                return;
            }

            var match = NetworkMatchState.Instance;
            var matchActive = match != null && match.IsSpawned && match.GameplayEnabled;
            var minigame = matchActive ? match.CurrentMinigame : ScheduledMinigameId.Skip;
            var scene = BgmTrackRules.Resolve(
                controller.IsInSession,
                matchActive,
                controller.IsBackInWaitingRoomDuringCeremony,
                matchActive ? match.FlowState : BoardFlowState.TurnOverview,
                matchActive && match.IsAwardCeremonyActive,
                minigame);
            GameSound.PlayBgm(BgmTrackRules.Pick(
                BgmTrackRules.Candidates(scene, minigame),
                GameSound.HasClips));
            GameSound.SetSimulationPaused(matchActive && match.IsSimulationSuspended);
        }
    }
}

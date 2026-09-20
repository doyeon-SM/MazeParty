using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.ArenaCombat;
using MazeParty.Gameplay.Minigames.BouncingBalls;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// The Board-scene Canvas owns the shared countdown, clock and round
    /// label for every registered minigame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameCommonHudView : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private MinigameTimerDial timerDial;
        [SerializeField] private GameObject roundRoot;
        [SerializeField] private Text roundText;

        public Canvas RootCanvas => rootCanvas;
        public MinigameTimerDial TimerDial => timerDial;
        public Text RoundText => roundText;
        public bool HasRequiredReferences =>
            rootCanvas != null && timerDial != null &&
            timerDial.HasRequiredReferences && roundRoot != null &&
            roundText != null &&
            timerDial.transform.IsChildOf(transform) &&
            roundRoot.transform.IsChildOf(transform) &&
            roundText.transform.IsChildOf(roundRoot.transform);

        public void Configure(Canvas canvas, MinigameTimerDial timer,
            GameObject round, Text label)
        {
            rootCanvas = canvas;
            timerDial = timer;
            roundRoot = round;
            roundText = label;
        }

        private void Awake()
        {
            HideClock();
        }

        private void Update()
        {
            var match = NetworkMatchState.Instance;
            if (match == null ||
                match.FlowState != BoardFlowState.MinigamePlaying ||
                match.IsMinigameStartCountdown ||
                !MinigameCatalog.TryGetDefinition(
                    match.CurrentMinigame, out var definition))
            {
                HideClock();
                return;
            }

            if (match.IsReconnectPaused)
            {
                ShowClock(match.ReconnectRemaining,
                    NetworkMatchState.ReconnectGraceSeconds,
                    "RECONNECT", true);
                return;
            }

            if (!TryGetClock(match.CurrentMinigame,
                    definition.PhaseDurationSeconds,
                    out var remaining, out var total,
                    out var round))
            {
                HideClock();
                return;
            }

            var hasRounds = definition.RoundCount > 1;
            var label = match.CurrentMinigame ==
                    ScheduledMinigameId.SequenceMemory
                ? "PROBLEM " + round + " / " + definition.RoundCount
                : "ROUND " + round + " / " + definition.RoundCount;
            ShowClock(remaining, total, label, hasRounds);
        }

        private void ShowClock(double remaining, double total,
            string label, bool showRound)
        {
            if (!HasRequiredReferences)
            {
                return;
            }
            if (!timerDial.gameObject.activeSelf)
            {
                timerDial.gameObject.SetActive(true);
            }
            timerDial.SetTime(remaining, total);
            if (roundRoot.activeSelf != showRound)
            {
                roundRoot.SetActive(showRound);
            }
            if (showRound && roundText.text != label)
            {
                roundText.text = label;
            }
        }

        private void HideClock()
        {
            if (timerDial != null)
            {
                timerDial.gameObject.SetActive(false);
            }
            if (roundRoot != null)
            {
                roundRoot.SetActive(false);
            }
        }

        private static bool TryGetClock(ScheduledMinigameId id,
            double runningSeconds, out double remaining,
            out double total, out int round)
        {
            remaining = 0d;
            total = runningSeconds;
            round = 1;
            switch (id)
            {
                case ScheduledMinigameId.Minefield:
                {
                    var state = NetworkMinefieldState.Instance;
                    if (state == null ||
                        state.Phase == NetworkMinefieldPhase.Inactive ||
                        state.Phase == NetworkMinefieldPhase.Complete)
                        return false;
                    remaining = state.Remaining;
                    round = state.RoundNumber;
                    total = state.Phase == NetworkMinefieldPhase.Countdown
                        ? NetworkMinefieldState.CountdownSeconds
                        : state.Phase == NetworkMinefieldPhase.RoundResult
                            ? NetworkMinefieldState.RoundResultSeconds
                            : NetworkMinefieldState.RunSeconds;
                    return true;
                }
                case ScheduledMinigameId.WrongWay:
                {
                    var state = NetworkWrongWayState.Instance;
                    if (state == null ||
                        state.Phase == NetworkWrongWayPhase.Inactive ||
                        state.Phase == NetworkWrongWayPhase.Complete)
                        return false;
                    remaining = state.Remaining;
                    round = state.RoundNumber;
                    total = state.Phase == NetworkWrongWayPhase.RoundResult
                        ? NetworkWrongWayState.RoundResultSeconds
                        : state.Phase == NetworkWrongWayPhase.Countdown
                            ? 3d : runningSeconds;
                    return true;
                }
                case ScheduledMinigameId.RedLightGreenLight:
                {
                    var state = NetworkRedLightGreenLightState.Instance;
                    if (state == null ||
                        state.Phase == NetworkRedLightGreenLightPhase.Inactive ||
                        state.Phase == NetworkRedLightGreenLightPhase.Complete)
                        return false;
                    remaining = state.Remaining;
                    round = state.RoundNumber;
                    total = state.Phase == NetworkRedLightGreenLightPhase.Countdown
                        ? 3d : state.Phase ==
                            NetworkRedLightGreenLightPhase.RoundResult
                            ? 4d : runningSeconds;
                    return true;
                }
                case ScheduledMinigameId.StableFooting:
                {
                    var state = NetworkStableFootingState.Instance;
                    if (state == null ||
                        state.Phase == NetworkStableFootingPhase.Inactive ||
                        state.Phase == NetworkStableFootingPhase.Complete)
                        return false;
                    remaining = state.Remaining;
                    round = state.RoundNumber;
                    total = state.Phase == NetworkStableFootingPhase.Countdown
                        ? NetworkStableFootingState.CountdownSeconds
                        : state.Phase == NetworkStableFootingPhase.RoundResult
                            ? NetworkStableFootingState.RoundResultSeconds
                            : runningSeconds;
                    return true;
                }
                case ScheduledMinigameId.BalloonBlow:
                {
                    var state = NetworkBalloonBlowState.Instance;
                    if (state == null ||
                        state.Phase == NetworkBalloonBlowPhase.Inactive ||
                        state.Phase == NetworkBalloonBlowPhase.Complete)
                        return false;
                    remaining = state.Remaining;
                    round = state.RoundNumber;
                    total = state.Phase == NetworkBalloonBlowPhase.Countdown
                        ? NetworkBalloonBlowState.CountdownSeconds
                        : state.Phase == NetworkBalloonBlowPhase.RoundResult
                            ? NetworkBalloonBlowState.RoundResultSeconds
                            : runningSeconds;
                    return true;
                }
                case ScheduledMinigameId.GiftGrab:
                {
                    var state = NetworkGiftGrabState.Instance;
                    if (state == null ||
                        state.Phase == NetworkGiftGrabPhase.Inactive ||
                        state.Phase == NetworkGiftGrabPhase.Complete)
                        return false;
                    remaining = state.Remaining;
                    round = state.RoundNumber;
                    total = state.Phase == NetworkGiftGrabPhase.Countdown
                        ? NetworkGiftGrabState.CountdownSeconds
                        : state.Phase == NetworkGiftGrabPhase.RoundResult
                            ? NetworkGiftGrabState.RoundResultSeconds
                            : runningSeconds;
                    return true;
                }
                case ScheduledMinigameId.TerritoryPaint:
                {
                    var state = NetworkTerritoryPaintState.Instance;
                    if (state == null ||
                        state.Phase == NetworkTerritoryPaintPhase.Inactive ||
                        state.Phase == NetworkTerritoryPaintPhase.Complete)
                        return false;
                    remaining = state.Remaining;
                    total = state.Phase == NetworkTerritoryPaintPhase.Countdown
                        ? NetworkTerritoryPaintState.CountdownSeconds
                        : state.Phase == NetworkTerritoryPaintPhase.RoundResult
                            ? NetworkTerritoryPaintState.ResultSeconds
                            : runningSeconds;
                    return true;
                }
                case ScheduledMinigameId.TagChase:
                {
                    var state = NetworkTagChaseState.Instance;
                    if (state == null ||
                        state.Phase == NetworkTagChasePhase.Inactive ||
                        state.Phase == NetworkTagChasePhase.Complete)
                        return false;
                    remaining = state.Remaining;
                    round = state.RoundNumber;
                    total = state.Phase == NetworkTagChasePhase.Countdown
                        ? NetworkTagChaseState.CountdownSeconds
                        : state.Phase == NetworkTagChasePhase.RoundResult
                            ? NetworkTagChaseState.RoundResultSeconds
                            : runningSeconds;
                    return true;
                }
                case ScheduledMinigameId.Race:
                {
                    var state = NetworkRaceState.Instance;
                    if (state == null ||
                        state.Phase == NetworkRacePhase.Inactive ||
                        state.Phase == NetworkRacePhase.Complete)
                        return false;
                    remaining = state.Remaining;
                    round = state.RoundNumber;
                    total = state.Phase == NetworkRacePhase.Countdown
                        ? NetworkRaceState.CountdownSeconds
                        : state.Phase == NetworkRacePhase.RoundResult
                            ? NetworkRaceState.RoundResultSeconds
                            : runningSeconds;
                    return true;
                }
                case ScheduledMinigameId.SequenceMemory:
                {
                    var state = NetworkSequenceMemoryState.Instance;
                    if (state == null ||
                        state.Phase == NetworkSequenceMemoryPhase.Inactive)
                        return false;
                    remaining = state.Remaining;
                    round = state.RoundNumber;
                    total = state.Phase == NetworkSequenceMemoryPhase.Countdown
                        ? SequenceMemoryRules.CountdownSeconds
                        : state.Phase ==
                            NetworkSequenceMemoryPhase.PresentingProblem
                            ? SequenceMemoryRules.GetProblemPresentationSeconds(
                                Mathf.Clamp(round, 1,
                                    SequenceMemoryRules.RoundCount))
                            : state.Phase ==
                                NetworkSequenceMemoryPhase.RevealingAnswer
                                ? SequenceMemoryRules.AnswerRevealSeconds
                                : state.Phase ==
                                    NetworkSequenceMemoryPhase.Complete
                                    ? SequenceMemoryRules.ResultSeconds
                                : SequenceMemoryRules.InputWindowSeconds;
                    return true;
                }
                case ScheduledMinigameId.BouncingBalls:
                {
                    var state = NetworkBouncingBallsState.Instance;
                    if (state == null ||
                        state.Phase == NetworkBouncingBallsPhase.Inactive)
                        return false;
                    remaining = state.Remaining;
                    round = state.RoundNumber;
                    total = state.Phase == NetworkBouncingBallsPhase.Countdown
                        ? 3d : state.Phase == NetworkBouncingBallsPhase.RoundBreak
                            ? NetworkBouncingBallsState.RoundBreakSeconds
                            : state.Phase == NetworkBouncingBallsPhase.Complete
                                ? BouncingBallsRules.ResultSeconds
                            : runningSeconds;
                    return true;
                }
                case ScheduledMinigameId.BombPassing:
                {
                    var state = NetworkBombPassingState.Instance;
                    if (state == null ||
                        state.Phase == NetworkBombPassingPhase.Inactive ||
                        state.Phase == NetworkBombPassingPhase.Complete)
                        return false;
                    remaining = state.Phase == NetworkBombPassingPhase.Playing
                        ? state.BombRemainingSeconds : state.Remaining;
                    total = state.Phase == NetworkBombPassingPhase.Playing
                        ? state.BombFuseSeconds
                        : state.Phase == NetworkBombPassingPhase.Countdown
                            ? 3d : 4d;
                    return true;
                }
                case ScheduledMinigameId.SnowySpin:
                {
                    var state = NetworkSnowySpinState.Instance;
                    if (state == null ||
                        state.Phase == NetworkSnowySpinPhase.Inactive ||
                        state.Phase == NetworkSnowySpinPhase.Complete)
                        return false;
                    remaining = state.Remaining;
                    round = state.RoundNumber;
                    total = state.Phase == NetworkSnowySpinPhase.Countdown
                        ? NetworkSnowySpinState.CountdownSeconds
                        : state.Phase == NetworkSnowySpinPhase.RoundResult
                            ? NetworkSnowySpinState.ResultSeconds
                            : runningSeconds;
                    return true;
                }
                case ScheduledMinigameId.ArenaCombat:
                {
                    var state = NetworkArenaCombatState.Instance;
                    if (state == null ||
                        state.Phase == NetworkArenaCombatPhase.Inactive)
                        return false;
                    remaining = state.Remaining;
                    total = state.Phase == NetworkArenaCombatPhase.Countdown
                        ? 3d : state.Phase == NetworkArenaCombatPhase.Complete
                            ? ArenaCombatRules.ResultSeconds
                            : runningSeconds;
                    return true;
                }
                case ScheduledMinigameId.CliffBarrage:
                {
                    var state = NetworkCliffBarrageState.Instance;
                    if (state == null ||
                        state.Phase == NetworkCliffBarragePhase.Inactive ||
                        state.Phase == NetworkCliffBarragePhase.Complete)
                        return false;
                    remaining = state.Remaining;
                    round = state.RoundNumber;
                    total = state.Phase == NetworkCliffBarragePhase.Countdown
                        ? 3d : state.Phase == NetworkCliffBarragePhase.RoundResult
                            ? 4d : runningSeconds;
                    return true;
                }
                default:
                    return false;
            }
        }
    }
}

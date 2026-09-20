using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.CliffBarrage;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// The prefab-authored, shared round clock for Cliff Barrage. It never
    /// constructs or styles Canvas elements at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CliffBarrageHudView : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private MinigameTimerDial timerDial;
        [SerializeField] private Text roundText;

        public Canvas RootCanvas => rootCanvas;
        public MinigameTimerDial TimerDial => timerDial;
        public Text RoundText => roundText;
        public bool HasRequiredReferences =>
            rootCanvas != null && timerDial != null &&
            timerDial.HasRequiredReferences && roundText != null;

        public void Configure(Canvas canvas, MinigameTimerDial timer,
            Text round)
        {
            rootCanvas = canvas;
            timerDial = timer;
            roundText = round;
        }

        public void SetDisplay(int round, double remaining,
            double total, bool visible)
        {
            if (!HasRequiredReferences)
            {
                return;
            }
            rootCanvas.enabled = visible;
            if (!visible)
            {
                return;
            }
            timerDial.SetTime(remaining, total);
            roundText.text = "ROUND " +
                Mathf.Clamp(round, 1, CliffBarrageRules.RoundCount) +
                " / " + CliffBarrageRules.RoundCount;
        }

        private void Awake()
        {
            if (rootCanvas != null)
            {
                rootCanvas.enabled = false;
            }
        }

        private void Update()
        {
            var match = NetworkMatchState.Instance;
            var state = NetworkCliffBarrageState.Instance;
            var visible = match != null &&
                match.IsCliffBarragePhase &&
                match.FlowState == BoardFlowState.MinigamePlaying &&
                state != null && state.IsSpawned &&
                state.Phase == NetworkCliffBarragePhase.Playing;
            if (!visible)
            {
                SetDisplay(1, 0d,
                    CliffBarrageRules.RoundDurationSeconds, false);
                return;
            }
            var remaining = match.IsReconnectPaused
                ? match.ReconnectRemaining : state.Remaining;
            var total = match.IsReconnectPaused
                ? NetworkMatchState.ReconnectGraceSeconds
                : CliffBarrageRules.RoundDurationSeconds;
            SetDisplay(state.RoundNumber, remaining, total, true);
        }
    }
}

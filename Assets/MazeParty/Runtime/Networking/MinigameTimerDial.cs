using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Shared serialized timer binding used by every minigame HUD prefab.
    /// Runtime code only supplies remaining and total time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameTimerDial : MonoBehaviour
    {
        [SerializeField] private MinigameTimerRingGraphic ring;
        [SerializeField] private Text timeText;

        public MinigameTimerRingGraphic Ring => ring;
        public Text TimeText => timeText;
        public bool HasRequiredReferences =>
            ring != null && timeText != null;

        public void Configure(
            MinigameTimerRingGraphic timerRing,
            Text timerText)
        {
            ring = timerRing;
            timeText = timerText;
        }

        public void SetTime(
            double remainingSeconds,
            double totalSeconds)
        {
            if (!HasRequiredReferences)
            {
                return;
            }

            var safeRemaining =
                double.IsNaN(remainingSeconds) ||
                double.IsInfinity(remainingSeconds)
                    ? 0d
                    : System.Math.Max(0d, remainingSeconds);
            var safeTotal =
                double.IsNaN(totalSeconds) ||
                double.IsInfinity(totalSeconds)
                    ? 0d
                    : System.Math.Max(0d, totalSeconds);
            timeText.text =
                MinigameDisplayFormatter.FormatClock(safeRemaining);
            ring.SetFillAmount(
                safeTotal > 0.000001d
                    ? (float)System.Math.Min(
                        1d,
                        safeRemaining / safeTotal)
                    : 0f);
        }
    }
}

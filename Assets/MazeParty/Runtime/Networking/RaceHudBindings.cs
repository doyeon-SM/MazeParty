using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Production Race HUD contract. Only the shared timer is player-visible;
    /// progress, ranks and scores are written to the log.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaceHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private MinigameTimerDial timerDial;

        public Canvas RootCanvas => rootCanvas;
        public MinigameTimerDial TimerDial => timerDial;
        public bool HasRequiredReferences =>
            rootCanvas != null && timerDial != null &&
            timerDial.HasRequiredReferences;

        public void Configure(Canvas canvas, MinigameTimerDial timer)
        {
            rootCanvas = canvas;
            timerDial = timer;
        }
    }
}

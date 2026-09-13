using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Production Tag Chase HUD contract. Gameplay exposes only the shared
    /// circular timer; role, catches and scores are logged by the server.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TagChaseHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private MinigameTimerDial timerDial;

        public Canvas RootCanvas => rootCanvas;
        public MinigameTimerDial TimerDial => timerDial;
        public bool HasRequiredReferences =>
            rootCanvas != null &&
            timerDial != null &&
            timerDial.HasRequiredReferences;

        public void Configure(
            Canvas canvas,
            MinigameTimerDial timer)
        {
            rootCanvas = canvas;
            timerDial = timer;
        }
    }
}

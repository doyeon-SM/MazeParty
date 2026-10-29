using MazeParty.Gameplay;
using UnityEngine.InputSystem;

namespace MazeParty.Multiplayer
{
    internal static class MinigameSpectatorInput
    {
        public static bool SwitchPressed(NetworkMatchState match, bool practice = false) =>
            (practice || (match != null && match.FlowState == BoardFlowState.MinigamePlaying &&
                          !match.IsGlobalSimulationPaused && !match.IsMinigameRoundCountdown)) &&
            !LocalInputGate.BlocksGameplayInput && !LocalInputGate.IsPausePointerRequested &&
            Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }
}

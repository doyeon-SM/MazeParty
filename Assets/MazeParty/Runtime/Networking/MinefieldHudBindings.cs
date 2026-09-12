using MazeParty.Gameplay.Minigames.Minefield;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Stable references owned by MinefieldHud.prefab. Runtime presentation
    /// code may bind game state to these controls, but it must not construct
    /// or restyle them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinefieldHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;
        [SerializeField] private Text phaseText;
        [SerializeField] private Text instructionText;
        [SerializeField] private Text[] scoreRows;

        public Canvas Canvas => canvas;
        public Text PhaseText => phaseText;
        public Text InstructionText => instructionText;
        public Text[] ScoreRows => scoreRows;

        public bool HasRequiredReferences =>
            canvas != null &&
            phaseText != null &&
            instructionText != null &&
            scoreRows != null &&
            scoreRows.Length == MinefieldRules.PlayerCount &&
            AllAssigned(scoreRows);

        public void Configure(
            Canvas targetCanvas,
            Text targetPhaseText,
            Text targetInstructionText,
            Text[] targetScoreRows)
        {
            canvas = targetCanvas;
            phaseText = targetPhaseText;
            instructionText = targetInstructionText;
            scoreRows = targetScoreRows;
        }

        private static bool AllAssigned(Text[] texts)
        {
            for (var index = 0; index < texts.Length; index++)
            {
                if (texts[index] == null)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

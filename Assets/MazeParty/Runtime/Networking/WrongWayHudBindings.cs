using System;
using MazeParty.Gameplay.Minigames.WrongWay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Stable references owned by WrongWayHud.prefab. Runtime presentation
    /// code only binds replicated state to these prefab-authored controls.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WrongWayHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;
        [SerializeField] private Text phaseText;
        [SerializeField] private Text instructionText;
        [SerializeField] private Text promptText;
        [SerializeField] private Text[] progressRows;

        private Color[] _defaultProgressRowColors;

        public Canvas Canvas => canvas;
        public Text PhaseText => phaseText;
        public Text InstructionText => instructionText;
        public Text PromptText => promptText;
        public Text[] ProgressRows => progressRows;

        public bool HasRequiredReferences =>
            canvas != null &&
            phaseText != null &&
            instructionText != null &&
            promptText != null &&
            progressRows != null &&
            progressRows.Length == WrongWayRules.PlayerCount &&
            AllAssigned(progressRows);

        public void Configure(
            Canvas targetCanvas,
            Text targetPhaseText,
            Text targetInstructionText,
            Text targetPromptText,
            Text[] targetProgressRows)
        {
            canvas = targetCanvas;
            phaseText = targetPhaseText;
            instructionText = targetInstructionText;
            promptText = targetPromptText;
            progressRows = targetProgressRows;
        }

        public Color GetDefaultProgressRowColor(int index)
        {
            if (progressRows == null ||
                index < 0 ||
                index >= progressRows.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            CaptureDefaultProgressRowColors();
            return _defaultProgressRowColors[index];
        }

        private void Awake()
        {
            CaptureDefaultProgressRowColors();
        }

        private void CaptureDefaultProgressRowColors()
        {
            if (_defaultProgressRowColors != null &&
                progressRows != null &&
                _defaultProgressRowColors.Length == progressRows.Length)
            {
                return;
            }

            var count = progressRows != null ? progressRows.Length : 0;
            _defaultProgressRowColors = new Color[count];
            for (var index = 0; index < count; index++)
            {
                if (progressRows[index] != null)
                {
                    _defaultProgressRowColors[index] =
                        progressRows[index].color;
                }
            }
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

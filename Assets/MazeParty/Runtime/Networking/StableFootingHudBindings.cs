using System;
using MazeParty.Gameplay.Minigames.StableFooting;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized design contract for the Stable Footing HUD prefab. Runtime
    /// code only updates values and panel visibility through these bindings;
    /// layout and styling remain owned by the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StableFootingHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private Text phaseText;
        [SerializeField] private Text timerText;
        [SerializeField] private Text roundText;
        [SerializeField] private Text instructionText;
        [SerializeField] private Text[] playerRows =
            new Text[StableFootingRules.PlayerCount];
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private GameObject controlsPanel;
        [SerializeField] private GameObject resultPanel;

        private Color[] _defaultPlayerRowColors;

        public Canvas RootCanvas => rootCanvas;
        public Text PhaseText => phaseText;
        public Text TimerText => timerText;
        public Text RoundText => roundText;
        public Text InstructionText => instructionText;
        public Text[] PlayerRows => playerRows;
        public GameObject PausePanel => pausePanel;
        public GameObject ControlsPanel => controlsPanel;
        public GameObject ResultPanel => resultPanel;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            phaseText != null &&
            timerText != null &&
            roundText != null &&
            instructionText != null &&
            playerRows != null &&
            playerRows.Length == StableFootingRules.PlayerCount &&
            Array.TrueForAll(playerRows, row => row != null) &&
            pausePanel != null &&
            controlsPanel != null &&
            resultPanel != null;

        public void Configure(
            Canvas canvas,
            Text phase,
            Text timer,
            Text round,
            Text instructions,
            Text[] rows,
            GameObject pause,
            GameObject controls,
            GameObject result)
        {
            rootCanvas = canvas;
            phaseText = phase;
            timerText = timer;
            roundText = round;
            instructionText = instructions;
            playerRows = rows;
            pausePanel = pause;
            controlsPanel = controls;
            resultPanel = result;
            _defaultPlayerRowColors = null;
            CaptureDefaultPlayerRowColors();
        }

        public Color GetDefaultPlayerRowColor(int index)
        {
            if (playerRows == null ||
                index < 0 || index >= playerRows.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            CaptureDefaultPlayerRowColors();
            return _defaultPlayerRowColors[index];
        }

        private void Awake()
        {
            CaptureDefaultPlayerRowColors();
        }

        private void CaptureDefaultPlayerRowColors()
        {
            if (_defaultPlayerRowColors != null &&
                playerRows != null &&
                _defaultPlayerRowColors.Length == playerRows.Length)
            {
                return;
            }

            var count = playerRows != null ? playerRows.Length : 0;
            _defaultPlayerRowColors = new Color[count];
            for (var index = 0; index < count; index++)
            {
                if (playerRows[index] != null)
                {
                    _defaultPlayerRowColors[index] =
                        playerRows[index].color;
                }
            }
        }
    }
}

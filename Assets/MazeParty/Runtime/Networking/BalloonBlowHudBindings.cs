using System;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized design contract for the Balloon Blow HUD prefab. Runtime
    /// presentation updates values and visibility only; layout, typography,
    /// colors and progress-bar styling remain owned by the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BalloonBlowHudBindings : MonoBehaviour
    {
        public const int PlayerCount = 4;

        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private Text phaseText;
        [SerializeField] private Text timerText;
        [SerializeField] private Text roundText;
        [SerializeField] private Text instructionText;
        [SerializeField] private Text[] playerRows =
            new Text[PlayerCount];
        [SerializeField] private Image[] playerProgressFills =
            new Image[PlayerCount];
        [SerializeField] private Text resultText;
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private GameObject controlsPanel;
        [SerializeField] private GameObject resultPanel;

        private Color[] _defaultRowColors;
        private Color[] _defaultFillColors;

        public Canvas RootCanvas => rootCanvas;
        public Text PhaseText => phaseText;
        public Text TimerText => timerText;
        public Text RoundText => roundText;
        public Text InstructionText => instructionText;
        public Text[] PlayerRows => playerRows;
        public Image[] PlayerProgressFills => playerProgressFills;
        public Text ResultText => resultText;
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
            playerRows.Length == PlayerCount &&
            Array.TrueForAll(playerRows, row => row != null) &&
            playerProgressFills != null &&
            playerProgressFills.Length == PlayerCount &&
            Array.TrueForAll(playerProgressFills, fill => fill != null) &&
            resultText != null &&
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
            Image[] progressFills,
            Text resultMessage,
            GameObject pause,
            GameObject controls,
            GameObject resultPanelObject)
        {
            rootCanvas = canvas;
            phaseText = phase;
            timerText = timer;
            roundText = round;
            instructionText = instructions;
            playerRows = rows;
            playerProgressFills = progressFills;
            resultText = resultMessage;
            pausePanel = pause;
            controlsPanel = controls;
            resultPanel = resultPanelObject;
            _defaultRowColors = null;
            _defaultFillColors = null;
            CaptureDefaults();
        }

        public Color GetDefaultPlayerRowColor(int index)
        {
            ValidatePlayerIndex(index);
            CaptureDefaults();
            return _defaultRowColors[index];
        }

        public Color GetDefaultProgressFillColor(int index)
        {
            ValidatePlayerIndex(index);
            CaptureDefaults();
            return _defaultFillColors[index];
        }

        private void Awake()
        {
            CaptureDefaults();
        }

        private void CaptureDefaults()
        {
            if (_defaultRowColors != null &&
                _defaultFillColors != null &&
                _defaultRowColors.Length == PlayerCount &&
                _defaultFillColors.Length == PlayerCount)
            {
                return;
            }

            _defaultRowColors = new Color[PlayerCount];
            _defaultFillColors = new Color[PlayerCount];
            for (var index = 0; index < PlayerCount; index++)
            {
                if (playerRows != null && index < playerRows.Length &&
                    playerRows[index] != null)
                {
                    _defaultRowColors[index] = playerRows[index].color;
                }
                if (playerProgressFills != null &&
                    index < playerProgressFills.Length &&
                    playerProgressFills[index] != null)
                {
                    _defaultFillColors[index] =
                        playerProgressFills[index].color;
                }
            }
        }

        private static void ValidatePlayerIndex(int index)
        {
            if (index < 0 || index >= PlayerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}

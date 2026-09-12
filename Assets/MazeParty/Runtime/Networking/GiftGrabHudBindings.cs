using System;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized design contract for the Gift Grab HUD prefab. Runtime code
    /// updates copy and visibility only; layout, typography and colors remain
    /// owned by the prefab asset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiftGrabHudBindings : MonoBehaviour
    {
        public const int PlayerCount = 4;

        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private Text phaseText;
        [SerializeField] private Text timerText;
        [SerializeField] private Text roundText;
        [SerializeField] private Text instructionText;
        [SerializeField] private Text localStatusText;
        [SerializeField] private Text neutralGiftText;
        [SerializeField] private Text[] playerRows = new Text[PlayerCount];
        [SerializeField] private Text resultText;
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private GameObject controlsPanel;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Color localPlayerRowColor =
            new Color(1f, 0.88f, 0.25f, 1f);

        private Color[] _defaultRowColors;

        public Canvas RootCanvas => rootCanvas;
        public Text PhaseText => phaseText;
        public Text TimerText => timerText;
        public Text RoundText => roundText;
        public Text InstructionText => instructionText;
        public Text LocalStatusText => localStatusText;
        public Text NeutralGiftText => neutralGiftText;
        public Text[] PlayerRows => playerRows;
        public Text ResultText => resultText;
        public GameObject PausePanel => pausePanel;
        public GameObject ControlsPanel => controlsPanel;
        public GameObject ResultPanel => resultPanel;
        public Color LocalPlayerRowColor => localPlayerRowColor;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            phaseText != null &&
            timerText != null &&
            roundText != null &&
            instructionText != null &&
            localStatusText != null &&
            neutralGiftText != null &&
            playerRows != null &&
            playerRows.Length == PlayerCount &&
            Array.TrueForAll(playerRows, row => row != null) &&
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
            Text localStatus,
            Text neutralGifts,
            Text[] rows,
            Text resultMessage,
            GameObject pause,
            GameObject controls,
            GameObject resultPanelObject,
            Color localRowColor)
        {
            rootCanvas = canvas;
            phaseText = phase;
            timerText = timer;
            roundText = round;
            instructionText = instructions;
            localStatusText = localStatus;
            neutralGiftText = neutralGifts;
            playerRows = rows;
            resultText = resultMessage;
            pausePanel = pause;
            controlsPanel = controls;
            resultPanel = resultPanelObject;
            localPlayerRowColor = localRowColor;
            _defaultRowColors = null;
            CaptureDefaults();
        }

        public Color GetDefaultPlayerRowColor(int index)
        {
            if (index < 0 || index >= PlayerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            CaptureDefaults();
            return _defaultRowColors[index];
        }

        private void Awake()
        {
            CaptureDefaults();
        }

        private void CaptureDefaults()
        {
            if (_defaultRowColors != null &&
                _defaultRowColors.Length == PlayerCount)
            {
                return;
            }

            _defaultRowColors = new Color[PlayerCount];
            for (var index = 0; index < PlayerCount; index++)
            {
                if (playerRows != null && index < playerRows.Length &&
                    playerRows[index] != null)
                {
                    _defaultRowColors[index] = playerRows[index].color;
                }
            }
        }
    }
}

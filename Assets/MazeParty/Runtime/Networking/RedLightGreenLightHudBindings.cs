using System;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public enum RedLightGreenLightHudSignalStyle
    {
        Neutral,
        Green,
        TurnWarning,
        Red
    }

    /// <summary>
    /// Serialized design contract for the Red Light / Green Light HUD prefab.
    /// Runtime presentation code only writes values through these references;
    /// layout, typography and colors remain owned by the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RedLightGreenLightHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private Text phaseText;
        [SerializeField] private Text signalText;
        [SerializeField] private Text instructionText;
        [SerializeField] private Text[] playerRows =
            new Text[RedLightGreenLightRules.PlayerCount];

        [Header("Signal Palette")]
        [SerializeField] private Color neutralSignalColor = Color.white;
        [SerializeField] private Color greenSignalColor =
            new Color(0.22f, 1f, 0.35f, 1f);
        [SerializeField] private Color turnWarningSignalColor =
            new Color(1f, 0.74f, 0.12f, 1f);
        [SerializeField] private Color redSignalColor =
            new Color(1f, 0.16f, 0.1f, 1f);

        private Color[] _defaultPlayerRowColors;

        public Canvas RootCanvas => rootCanvas;
        public Text PhaseText => phaseText;
        public Text SignalText => signalText;
        public Text InstructionText => instructionText;
        public Text[] PlayerRows => playerRows;
        public Color NeutralSignalColor => neutralSignalColor;
        public Color GreenSignalColor => greenSignalColor;
        public Color TurnWarningSignalColor => turnWarningSignalColor;
        public Color RedSignalColor => redSignalColor;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            phaseText != null &&
            signalText != null &&
            instructionText != null &&
            playerRows != null &&
            playerRows.Length == RedLightGreenLightRules.PlayerCount &&
            System.Array.TrueForAll(playerRows, row => row != null);

        public void Configure(
            Canvas canvas,
            Text phase,
            Text signal,
            Text instructions,
            Text[] rows,
            Color neutral,
            Color green,
            Color turnWarning,
            Color red)
        {
            rootCanvas = canvas;
            phaseText = phase;
            signalText = signal;
            instructionText = instructions;
            playerRows = rows;
            neutralSignalColor = neutral;
            greenSignalColor = green;
            turnWarningSignalColor = turnWarning;
            redSignalColor = red;
        }

        public Color GetDefaultPlayerRowColor(int index)
        {
            if (playerRows == null ||
                index < 0 ||
                index >= playerRows.Length)
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

        public void SetSignal(
            string label,
            RedLightGreenLightHudSignalStyle style)
        {
            if (signalText == null)
            {
                return;
            }

            signalText.text = label ?? string.Empty;
            signalText.color = GetSignalColor(style);
        }

        public Color GetSignalColor(RedLightGreenLightHudSignalStyle style)
        {
            switch (style)
            {
                case RedLightGreenLightHudSignalStyle.Green:
                    return greenSignalColor;
                case RedLightGreenLightHudSignalStyle.TurnWarning:
                    return turnWarningSignalColor;
                case RedLightGreenLightHudSignalStyle.Red:
                    return redSignalColor;
                default:
                    return neutralSignalColor;
            }
        }
    }
}

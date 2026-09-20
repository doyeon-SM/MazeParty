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
        [SerializeField] private Text localStatusText;
        [SerializeField] private Text[] playerRows = new Text[PlayerCount];
        [SerializeField] private Text resultText;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Color localPlayerRowColor =
            new Color(1f, 0.88f, 0.25f, 1f);

        private Color[] _defaultRowColors;

        public Canvas RootCanvas => rootCanvas;
        public Text LocalStatusText => localStatusText;
        public Text[] PlayerRows => playerRows;
        public Text ResultText => resultText;
        public GameObject ResultPanel => resultPanel;
        public Color LocalPlayerRowColor => localPlayerRowColor;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            localStatusText != null &&
            playerRows != null &&
            playerRows.Length == PlayerCount &&
            Array.TrueForAll(playerRows, row => row != null) &&
            resultText != null &&
            resultPanel != null &&
            resultPanel.GetComponent<Canvas>() != null;

        public void Configure(
            Canvas canvas,
            Text localStatus,
            Text[] rows,
            Text resultMessage,
            GameObject resultPanelObject,
            Color localRowColor)
        {
            rootCanvas = canvas;
            localStatusText = localStatus;
            playerRows = rows;
            resultText = resultMessage;
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

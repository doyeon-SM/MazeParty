using System;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized design contract for the Sequence Memory HUD prefab. The
    /// runtime view may update text, color and visibility, while hierarchy and
    /// layout remain authored in the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SequenceMemoryHudBindings : MonoBehaviour
    {
        public const int PlayerCount = 4;

        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private GameObject root;
        [SerializeField] private Text npcSequenceText;
        [SerializeField] private Image[] playerRows =
            new Image[PlayerCount];
        [SerializeField] private Text[] playerNameTexts =
            new Text[PlayerCount];
        [SerializeField] private Text[] playerInputTexts =
            new Text[PlayerCount];
        [SerializeField] private Text[] playerStatusTexts =
            new Text[PlayerCount];

        private Color[] _defaultNameColors;
        private Color[] _defaultInputColors;
        private Color[] _defaultStatusColors;
        private Color[] _defaultRowColors;

        public Canvas RootCanvas => rootCanvas;
        public GameObject Root => root;
        public Text NpcSequenceText => npcSequenceText;
        public Image[] PlayerRows => playerRows;
        public Text[] PlayerNameTexts => playerNameTexts;
        public Text[] PlayerInputTexts => playerInputTexts;
        public Text[] PlayerStatusTexts => playerStatusTexts;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            root != null &&
            npcSequenceText != null &&
            HasFourNonNull(playerRows) &&
            HasFourNonNull(playerNameTexts) &&
            HasFourNonNull(playerInputTexts) &&
            HasFourNonNull(playerStatusTexts);

        public void Configure(
            Canvas canvas,
            GameObject visibleRoot,
            Text npcSequence,
            Image[] rows,
            Text[] names,
            Text[] inputs,
            Text[] statuses)
        {
            rootCanvas = canvas;
            root = visibleRoot;
            npcSequenceText = npcSequence;
            playerRows = rows;
            playerNameTexts = names;
            playerInputTexts = inputs;
            playerStatusTexts = statuses;
            ClearDefaultColorCache();
            CaptureDefaultColors();
        }

        public Color GetDefaultNameColor(int playerSlot)
        {
            ValidatePlayerSlot(playerSlot);
            CaptureDefaultColors();
            return _defaultNameColors[playerSlot];
        }

        public Color GetDefaultPlayerRowColor(int playerSlot)
        {
            ValidatePlayerSlot(playerSlot);
            CaptureDefaultColors();
            return _defaultRowColors[playerSlot];
        }

        public Color GetDefaultInputColor(int playerSlot)
        {
            ValidatePlayerSlot(playerSlot);
            CaptureDefaultColors();
            return _defaultInputColors[playerSlot];
        }

        public Color GetDefaultStatusColor(int playerSlot)
        {
            ValidatePlayerSlot(playerSlot);
            CaptureDefaultColors();
            return _defaultStatusColors[playerSlot];
        }

        private void Awake()
        {
            CaptureDefaultColors();
        }

        private void CaptureDefaultColors()
        {
            if (_defaultNameColors != null &&
                _defaultInputColors != null &&
                _defaultStatusColors != null &&
                _defaultRowColors != null)
            {
                return;
            }

            _defaultNameColors = new Color[PlayerCount];
            _defaultInputColors = new Color[PlayerCount];
            _defaultStatusColors = new Color[PlayerCount];
            _defaultRowColors = new Color[PlayerCount];
            for (var slot = 0; slot < PlayerCount; slot++)
            {
                if (playerRows != null &&
                    slot < playerRows.Length &&
                    playerRows[slot] != null)
                {
                    _defaultRowColors[slot] = playerRows[slot].color;
                }
                if (playerNameTexts != null &&
                    slot < playerNameTexts.Length &&
                    playerNameTexts[slot] != null)
                {
                    _defaultNameColors[slot] = playerNameTexts[slot].color;
                }
                if (playerInputTexts != null &&
                    slot < playerInputTexts.Length &&
                    playerInputTexts[slot] != null)
                {
                    _defaultInputColors[slot] = playerInputTexts[slot].color;
                }
                if (playerStatusTexts != null &&
                    slot < playerStatusTexts.Length &&
                    playerStatusTexts[slot] != null)
                {
                    _defaultStatusColors[slot] =
                        playerStatusTexts[slot].color;
                }
            }
        }

        private void ClearDefaultColorCache()
        {
            _defaultNameColors = null;
            _defaultInputColors = null;
            _defaultStatusColors = null;
            _defaultRowColors = null;
        }

        private static bool HasFourNonNull<T>(T[] values)
            where T : UnityEngine.Object
        {
            return values != null &&
                values.Length == PlayerCount &&
                Array.TrueForAll(values, value => value != null);
        }

        private static void ValidatePlayerSlot(int playerSlot)
        {
            if (playerSlot < 0 || playerSlot >= PlayerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(playerSlot));
            }
        }
    }
}

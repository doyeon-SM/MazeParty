using System;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>Serialized design contract for the authored Sequence Memory HUD.</summary>
    [DisallowMultipleComponent]
    public sealed class SequenceMemoryHudBindings : MonoBehaviour
    {
        public const int PlayerCount = 4;

        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private GameObject root;
        [SerializeField] private Text npcSequenceText;
        [SerializeField] private Image[] playerRows = new Image[PlayerCount];
        [SerializeField] private Text[] playerNameTexts = new Text[PlayerCount];
        [SerializeField] private Text[] playerInputTexts = new Text[PlayerCount];

        public Canvas RootCanvas => rootCanvas;
        public GameObject Root => root;
        public Text NpcSequenceText => npcSequenceText;
        public Image[] PlayerRows => playerRows;
        public Text[] PlayerNameTexts => playerNameTexts;
        public Text[] PlayerInputTexts => playerInputTexts;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            root != null &&
            npcSequenceText != null &&
            HasFourNonNull(playerRows) &&
            HasFourNonNull(playerNameTexts) &&
            HasFourNonNull(playerInputTexts);

        public void Configure(
            Canvas canvas,
            GameObject visibleRoot,
            Text npcSequence,
            Image[] rows,
            Text[] names,
            Text[] inputs)
        {
            rootCanvas = canvas;
            root = visibleRoot;
            npcSequenceText = npcSequence;
            playerRows = rows;
            playerNameTexts = names;
            playerInputTexts = inputs;
        }

        private static bool HasFourNonNull<T>(T[] values)
            where T : UnityEngine.Object
        {
            return values != null &&
                values.Length == PlayerCount &&
                Array.TrueForAll(values, value => value != null);
        }
    }
}

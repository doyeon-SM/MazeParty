using System;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Shared bindings for the authored four-player score HUD. Runtime code
    /// updates these references but never builds Canvas elements.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BouncingBallsHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private GameObject root;
        [SerializeField] private Text[] playerNameTexts =
            new Text[MultiplayerConstants.MaxPlayers];
        [SerializeField] private Text[] scoreTexts =
            new Text[MultiplayerConstants.MaxPlayers];

        public Canvas RootCanvas => rootCanvas;
        public GameObject Root => root;
        public Text[] PlayerNameTexts => playerNameTexts;
        public Text[] PlayerScoreTexts => scoreTexts;
        public Text[] ScoreTexts => scoreTexts;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            root != null &&
            HasFourNonNull(playerNameTexts) &&
            HasFourNonNull(scoreTexts);

        public void Configure(
            Canvas canvas,
            GameObject visibleRoot,
            Text[] names,
            Text[] scores)
        {
            rootCanvas = canvas;
            root = visibleRoot;
            playerNameTexts = names;
            scoreTexts = scores;
        }

        public void SetPlayer(
            int slot,
            string playerName,
            int score,
            Color playerColor)
        {
            if (slot < 0 || slot >= MultiplayerConstants.MaxPlayers ||
                !HasRequiredReferences)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            playerNameTexts[slot].text = playerName;
            playerNameTexts[slot].color = playerColor;
            scoreTexts[slot].text = score.ToString();
            scoreTexts[slot].color = playerColor;
        }


        private static bool HasFourNonNull(Text[] values)
        {
            return values != null &&
                values.Length == MultiplayerConstants.MaxPlayers &&
                Array.TrueForAll(values, value => value != null);
        }

    }
}

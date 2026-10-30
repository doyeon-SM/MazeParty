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

        [SerializeField] private Outline[] leaderOutlines = new Outline[MultiplayerConstants.MaxPlayers];
        private readonly int[] _scores = new int[MultiplayerConstants.MaxPlayers];
        private readonly bool[] _presented = new bool[MultiplayerConstants.MaxPlayers];

        public Canvas RootCanvas => rootCanvas;
        public GameObject Root => root;
        public Text[] PlayerNameTexts => playerNameTexts;
        public Text[] PlayerScoreTexts => scoreTexts;
        public Text[] ScoreTexts => scoreTexts;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            root != null &&
            HasFourNonNull(playerNameTexts) &&
            HasFourNonNull(scoreTexts) && leaderOutlines != null &&
            leaderOutlines.Length == MultiplayerConstants.MaxPlayers &&
            Array.TrueForAll(leaderOutlines, value => value != null);

        public void Configure(
            Canvas canvas,
            GameObject visibleRoot,
            Text[] names,
            Text[] scores, Outline[] outlines = null)
        {
            rootCanvas = canvas;
            root = visibleRoot;
            playerNameTexts = names;
            scoreTexts = scores;
            if (outlines != null) leaderOutlines = outlines;
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
            _scores[slot] = score;
            _presented[slot] = true;
            var maximum = int.MinValue;
            for (var i = 0; i < _scores.Length; i++)
                if (_presented[i]) maximum = Math.Max(maximum, _scores[i]);
            for (var i = 0; i < leaderOutlines.Length; i++)
                leaderOutlines[i].enabled = _presented[i] && _scores[i] == maximum;
        }


        private void OnDisable()
        {
            Array.Clear(_presented, 0, _presented.Length);
            if (leaderOutlines == null) return;
            foreach (var outline in leaderOutlines)
                if (outline != null) outline.enabled = false;
        }

        private static bool HasFourNonNull(Text[] values)
        {
            return values != null &&
                values.Length == MultiplayerConstants.MaxPlayers &&
                Array.TrueForAll(values, value => value != null);
        }

    }
}

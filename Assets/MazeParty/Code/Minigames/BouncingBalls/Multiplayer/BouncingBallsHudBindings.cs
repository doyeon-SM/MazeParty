using System;
using MazeParty.Gameplay.Minigames.BouncingBalls;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// References to the authored Bouncing Balls HUD prefab. Runtime code
    /// updates these bindings but never builds Canvas elements.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BouncingBallsHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private GameObject root;
        [SerializeField] private Text[] playerNameTexts =
            new Text[BouncingBallsRules.PlayerCount];
        [SerializeField] private Text[] scoreTexts =
            new Text[BouncingBallsRules.PlayerCount];

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

        private static bool HasFourNonNull(Text[] values)
        {
            return values != null &&
                values.Length == BouncingBallsRules.PlayerCount &&
                Array.TrueForAll(values, value => value != null);
        }
    }
}

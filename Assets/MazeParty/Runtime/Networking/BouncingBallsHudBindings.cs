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
        [SerializeField] private MinigameTimerDial timerDial;
        [SerializeField] private Text roundText;
        [SerializeField] private Text phaseText;
        [SerializeField] private Text instructionText;
        [SerializeField] private Text[] playerNameTexts =
            new Text[BouncingBallsRules.PlayerCount];
        [SerializeField] private Text[] scoreTexts =
            new Text[BouncingBallsRules.PlayerCount];
        [SerializeField] private Text[] concededTexts =
            new Text[BouncingBallsRules.PlayerCount];

        public Canvas RootCanvas => rootCanvas;
        public GameObject Root => root;
        public MinigameTimerDial TimerDial => timerDial;
        public Text RoundText => roundText;
        public Text PhaseText => phaseText;
        public Text InstructionText => instructionText;
        public Text[] PlayerNameTexts => playerNameTexts;
        public Text[] PlayerScoreTexts => scoreTexts;
        public Text[] PlayerConcededTexts => concededTexts;
        public Text[] ScoreTexts => scoreTexts;
        public Text[] ConcededTexts => concededTexts;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            root != null &&
            timerDial != null &&
            timerDial.HasRequiredReferences &&
            roundText != null &&
            phaseText != null &&
            instructionText != null &&
            HasFourNonNull(playerNameTexts) &&
            HasFourNonNull(scoreTexts) &&
            HasFourNonNull(concededTexts);

        public void Configure(
            Canvas canvas,
            GameObject visibleRoot,
            MinigameTimerDial timer,
            Text round,
            Text phase,
            Text instructions,
            Text[] names,
            Text[] scores,
            Text[] conceded)
        {
            rootCanvas = canvas;
            root = visibleRoot;
            timerDial = timer;
            roundText = round;
            phaseText = phase;
            instructionText = instructions;
            playerNameTexts = names;
            scoreTexts = scores;
            concededTexts = conceded;
        }

        private static bool HasFourNonNull(Text[] values)
        {
            return values != null &&
                values.Length == BouncingBallsRules.PlayerCount &&
                Array.TrueForAll(values, value => value != null);
        }
    }
}

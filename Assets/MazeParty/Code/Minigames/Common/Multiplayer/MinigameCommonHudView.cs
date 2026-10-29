using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.ArenaCombat;
using MazeParty.Gameplay.Minigames.BouncingBalls;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// The Board-scene Canvas owns the shared countdown, clock and round
    /// label for every registered minigame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameCommonHudView : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private MinigameTimerDial timerDial;
        [SerializeField] private GameObject roundRoot;
        [SerializeField] private Text roundText;
        [SerializeField] private GameObject taggerAimRoot;
        [SerializeField] private Text spectatorText;

        [SerializeField] private GameObject spectatorRoot;
        [SerializeField] private RawImage spectatorSwitchIcon;
        [SerializeField] private Text spectatorSwitchText;
        public GameObject SpectatorRoot => spectatorRoot;
        public RawImage SpectatorSwitchIcon => spectatorSwitchIcon;
        public Text SpectatorSwitchText => spectatorSwitchText;
        public bool HasSpectatorControls => spectatorRoot != null && spectatorSwitchIcon != null &&
            spectatorSwitchIcon.texture != null && spectatorSwitchText != null &&
            spectatorRoot.transform.IsChildOf(transform) &&
            spectatorText != null && spectatorText.transform.IsChildOf(spectatorRoot.transform) &&
            spectatorSwitchIcon.transform.IsChildOf(spectatorRoot.transform) &&
            spectatorSwitchText.transform.IsChildOf(spectatorRoot.transform);

        public void ConfigureSpectatorControls(GameObject root, RawImage icon, Text label)
        {
            spectatorRoot = root; spectatorSwitchIcon = icon; spectatorSwitchText = label;
        }

        public Canvas RootCanvas => rootCanvas;
        public MinigameTimerDial TimerDial => timerDial;
        public Text RoundText => roundText;
        public GameObject TaggerAimRoot => taggerAimRoot;
        public Text SpectatorText => spectatorText;
        public bool HasRequiredReferences =>
            rootCanvas != null && timerDial != null &&
            timerDial.HasRequiredReferences && roundRoot != null &&
            roundText != null && taggerAimRoot != null && HasSpectatorControls &&
            spectatorText != null &&
            timerDial.transform.IsChildOf(transform) &&
            roundRoot.transform.IsChildOf(transform) &&
            roundText.transform.IsChildOf(roundRoot.transform) &&
            taggerAimRoot.transform.IsChildOf(transform) &&
            spectatorText.transform.IsChildOf(transform);

        public void Configure(Canvas canvas, MinigameTimerDial timer,
            GameObject round, Text label, GameObject taggerAim,
            Text spectatorLabel)
        {
            rootCanvas = canvas;
            timerDial = timer;
            roundRoot = round;
            roundText = label;
            taggerAimRoot = taggerAim;
            spectatorText = spectatorLabel;
        }

        private void Awake()
        {
            HideClock();
            SetTaggerAimVisible(false);
            if (spectatorRoot != null) spectatorRoot.SetActive(false);
            if (spectatorText != null)
            {
                spectatorText.gameObject.SetActive(false);
            }
        }

        public void SetTaggerAimVisible(bool visible)
        {
            if (taggerAimRoot != null &&
                taggerAimRoot.activeSelf != visible)
            {
                taggerAimRoot.SetActive(visible);
            }
        }

        private void Update()
        {
            var match = NetworkMatchState.Instance;
            RefreshSpectatorLabel(match);
            if (match == null ||
                match.FlowState != BoardFlowState.MinigamePlaying ||
                (match.IsMinigameRoundCountdown && match.CurrentMinigame != ScheduledMinigameId.OddOneOut && match.CurrentMinigame != ScheduledMinigameId.Race2 && match.CurrentMinigame != ScheduledMinigameId.ShellGame && match.CurrentMinigame != ScheduledMinigameId.Flight) ||
                !MinigameCatalog.TryGetDefinition(
                    match.CurrentMinigame, out var definition))
            {
                HideClock();
                return;
            }

            if (match.CurrentMinigame == ScheduledMinigameId.BombPassing)
            {
                var bomb = NetworkBombPassingState.Instance;
                if (bomb == null ||
                    bomb.Phase == NetworkBombPassingPhase.Inactive ||
                    bomb.Phase == NetworkBombPassingPhase.Complete)
                {
                    HideClock();
                    return;
                }

                ShowRoundOnly(GameText.F("ROUND {0} / {1}",
                    match.ConfiguredSeriesRound, match.GameSettings.Get(match.CurrentMinigame).Rounds));
                return;
            }

            if (match.IsReconnectPaused)
            {
                ShowClock(match.ReconnectRemaining,
                    NetworkMatchState.ReconnectGraceSeconds,
                    GameText.T("RECONNECT"), true);
                return;
            }

            if (match.CurrentMinigame == ScheduledMinigameId.OddOneOut)
            {
                var odd = NetworkOddOneOutState.Instance;
                if (odd == null || odd.Phase == MazeParty.Gameplay.Minigames.OddOneOut.OddOneOutPhase.Inactive ||
                    odd.Phase == MazeParty.Gameplay.Minigames.OddOneOut.OddOneOutPhase.Complete) HideClock();
                else ShowRoundOnly(GameText.F("ROUND {0} / {1}", odd.RoundNumber, match.GameSettings.Get(match.CurrentMinigame).Rounds));
                return;
            }

            if (match.CurrentMinigame == ScheduledMinigameId.ShellGame)
            {
                var shell = NetworkShellGameState.Instance;
                if (shell == null) HideClock();
                else ShowRoundOnly(GameText.F("ROUND {0} / {1}", shell.RoundNumber, match.GameSettings.Get(match.CurrentMinigame).Rounds));
                return;
            }

            if (!TryGetClock(match.CurrentMinigame,
                    MinigameRulesConfig.FromMatch(match.CurrentMinigame, match.GameSettings).Seconds,
                    out var remaining, out var total,
                    out var round))
            {
                HideClock();
                return;
            }

            if (match.UsesConfiguredRoundSeries) round = match.ConfiguredSeriesRound;
            var hasRounds = match.GameSettings.Get(match.CurrentMinigame).Rounds > 1;
            var label = match.CurrentMinigame ==
                    ScheduledMinigameId.SequenceMemory
                ? GameText.F("PROBLEM {0} / {1}", round, match.GameSettings.Get(match.CurrentMinigame).Rounds)
                : GameText.F("ROUND {0} / {1}", round, match.GameSettings.Get(match.CurrentMinigame).Rounds);
            ShowClock(remaining, total, label, hasRounds);
        }

        private MinefieldNetworkView _minefieldSpectator;
        private RedLightGreenLightNetworkView _redLightSpectator;
        private Race2NetworkView _race2Spectator;

        private void RefreshSpectatorLabel(NetworkMatchState match)
        {
            if (spectatorText == null)
            {
                return;
            }

            var slot = -1;
            if (match != null &&
                match.FlowState == BoardFlowState.MinigamePlaying)
            {
                if (match.CurrentMinigame == ScheduledMinigameId.Minefield)
                {
                    if (_minefieldSpectator == null)
                        _minefieldSpectator = FindFirstObjectByType<MinefieldNetworkView>();
                    var view = _minefieldSpectator;
                    slot = view != null ? view.SpectatedSlot : -1;
                }
                else if (match.CurrentMinigame ==
                         ScheduledMinigameId.RedLightGreenLight)
                {
                    if (_redLightSpectator == null)
                        _redLightSpectator = FindFirstObjectByType<RedLightGreenLightNetworkView>();
                    var view = _redLightSpectator;
                    slot = view != null ? view.SpectatedSlot : -1;
                }
            }

            if (match != null && match.FlowState == BoardFlowState.MinigamePlaying &&
                match.CurrentMinigame == ScheduledMinigameId.Race2)
            {
                if (_race2Spectator == null) _race2Spectator = FindFirstObjectByType<Race2NetworkView>();
                slot = _race2Spectator != null ? _race2Spectator.SpectatedSlot : -1;
            }
            var avatar = slot >= 0 ? match.GetAvatarForSlot(slot) : null;
            ShowSpectator(avatar != null ? avatar.DisplayName : null,
                avatar != null ? avatar.Appearance.BodyColor : Color.white);
        }

        public void ShowSpectator(string displayName, Color bodyColor)
        {
            if (!HasSpectatorControls) return;
            var visible = !string.IsNullOrEmpty(displayName);
            if (spectatorRoot.activeSelf != visible) spectatorRoot.SetActive(visible);
            if (spectatorText.gameObject.activeSelf != visible)
            {
                spectatorText.gameObject.SetActive(visible);
            }
            if (!visible)
            {
                return;
            }

            var safeName = displayName.Replace("<", "‹")
                .Replace(">", "›");
            var color = ColorUtility.ToHtmlStringRGB(
                bodyColor);
            var label = "<color=#" + color + ">" + safeName +
                        "</color> " + GameText.T("SPECTATING");
            spectatorSwitchText.text = GameText.T("SWITCH");
            if (spectatorText.text != label)
            {
                spectatorText.text = label;
            }
        }

        public void ShowPracticeRoundOnly(int round, int roundCount)
        {
            ShowRoundOnly(GameText.F("ROUND {0} / {1}", round, roundCount));
        }

        public void ShowPracticeTimeOnly(double remaining, double total) => ShowClock(remaining, total, string.Empty, false);

        public void ShowPracticeClock(double remaining, double total, int round, int roundCount)
        {
            ShowClock(remaining, total, GameText.F("ROUND {0} / {1}", round, roundCount), true);
        }

        private void ShowClock(double remaining, double total,
            string label, bool showRound)
        {
            if (!HasRequiredReferences)
            {
                return;
            }
            if (!timerDial.gameObject.activeSelf)
            {
                timerDial.gameObject.SetActive(true);
            }
            timerDial.SetTime(remaining, total);
            if (roundRoot.activeSelf != showRound)
            {
                roundRoot.SetActive(showRound);
            }
            if (showRound && roundText.text != label)
            {
                roundText.text = label;
            }
        }

        private void HideClock()
        {
            if (timerDial != null)
            {
                timerDial.gameObject.SetActive(false);
            }
            if (roundRoot != null)
            {
                roundRoot.SetActive(false);
            }
        }

        private void ShowRoundOnly(string label)
        {
            if (!HasRequiredReferences)
            {
                return;
            }

            timerDial.gameObject.SetActive(false);
            roundRoot.SetActive(true);
            if (roundText.text != label)
            {
                roundText.text = label;
            }
        }

        private static bool TryGetClock(ScheduledMinigameId id,
            double runningSeconds, out double remaining,
            out double total, out int round)
        {
            remaining = 0d;
            total = runningSeconds;
            round = 1;
            return MinigameRuntimeRegistry.TryGet(id, out var runtime) &&
                   runtime.TryGetClock(runningSeconds, out remaining, out total, out round);
        }
    }
}

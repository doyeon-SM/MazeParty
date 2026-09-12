using System;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public enum MinigameSoloFeedbackStyle
    {
        Neutral,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Presentation-only binding surface for the developer minigame solo HUD.
    /// Its visual hierarchy lives in MinigameSoloHud.prefab; controllers only
    /// provide text, semantic feedback styles, and button actions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameSoloHudView : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private Text headerText;
        [SerializeField] private Text primaryStatusText;
        [SerializeField] private Text secondaryStatusText;
        [SerializeField] private Text featureText;
        [SerializeField] private Text helpText;
        [SerializeField] private Text feedbackText;

        [Header("Actions")]
        [SerializeField] private Button restartButton;
        [SerializeField] private Button nextSeedButton;
        [SerializeField] private Button stopButton;

        [Header("Feedback Palette")]
        [SerializeField] private Color neutralFeedbackColor = Color.white;
        [SerializeField] private Color successFeedbackColor =
            new Color(0.35f, 1f, 0.55f, 1f);
        [SerializeField] private Color warningFeedbackColor =
            new Color(1f, 0.72f, 0.15f, 1f);
        [SerializeField] private Color errorFeedbackColor =
            new Color(1f, 0.36f, 0.28f, 1f);

        private Action _restartRequested;
        private Action _nextSeedRequested;
        private Action _stopRequested;

        public bool HasRequiredReferences =>
            headerText != null &&
            primaryStatusText != null &&
            secondaryStatusText != null &&
            featureText != null &&
            helpText != null &&
            feedbackText != null &&
            restartButton != null &&
            nextSeedButton != null &&
            stopButton != null;

        public Text HeaderText => headerText;
        public Text PrimaryStatusText => primaryStatusText;
        public Text SecondaryStatusText => secondaryStatusText;
        public Text FeatureText => featureText;
        public Text HelpText => helpText;
        public Text FeedbackText => feedbackText;
        public Button RestartButton => restartButton;
        public Button NextSeedButton => nextSeedButton;
        public Button StopButton => stopButton;
        public Color NeutralFeedbackColor => neutralFeedbackColor;
        public Color SuccessFeedbackColor => successFeedbackColor;
        public Color WarningFeedbackColor => warningFeedbackColor;
        public Color ErrorFeedbackColor => errorFeedbackColor;

        public void Configure(
            Text header,
            Text primaryStatus,
            Text secondaryStatus,
            Text feature,
            Text help,
            Text feedback,
            Button restart,
            Button nextSeed,
            Button stop,
            Color neutral,
            Color success,
            Color warning,
            Color error)
        {
            headerText = header;
            primaryStatusText = primaryStatus;
            secondaryStatusText = secondaryStatus;
            featureText = feature;
            helpText = help;
            feedbackText = feedback;
            restartButton = restart;
            nextSeedButton = nextSeed;
            stopButton = stop;
            neutralFeedbackColor = neutral;
            successFeedbackColor = success;
            warningFeedbackColor = warning;
            errorFeedbackColor = error;
        }

        public void BindActions(
            Action restart,
            Action nextSeed,
            Action stop)
        {
            _restartRequested = restart;
            _nextSeedRequested = nextSeed;
            _stopRequested = stop;
        }

        public void SetContent(
            string header,
            string primaryStatus,
            string secondaryStatus,
            string feature,
            string help,
            string feedback,
            MinigameSoloFeedbackStyle feedbackStyle)
        {
            if (!HasRequiredReferences)
            {
                return;
            }

            headerText.text = header ?? string.Empty;
            primaryStatusText.text = primaryStatus ?? string.Empty;
            secondaryStatusText.text = secondaryStatus ?? string.Empty;
            featureText.text = feature ?? string.Empty;
            helpText.text = help ?? string.Empty;
            feedbackText.text = feedback ?? string.Empty;
            feedbackText.color = GetFeedbackColor(feedbackStyle);
        }

        public Color GetFeedbackColor(MinigameSoloFeedbackStyle style)
        {
            switch (style)
            {
                case MinigameSoloFeedbackStyle.Success:
                    return successFeedbackColor;
                case MinigameSoloFeedbackStyle.Warning:
                    return warningFeedbackColor;
                case MinigameSoloFeedbackStyle.Error:
                    return errorFeedbackColor;
                default:
                    return neutralFeedbackColor;
            }
        }

        private void Awake()
        {
            if (!HasRequiredReferences)
            {
                Debug.LogError(
                    "MinigameSoloHudView prefab references are incomplete.",
                    this);
                enabled = false;
                return;
            }

            restartButton.onClick.AddListener(HandleRestartClicked);
            nextSeedButton.onClick.AddListener(HandleNextSeedClicked);
            stopButton.onClick.AddListener(HandleStopClicked);
        }

        private void OnDestroy()
        {
            if (restartButton != null)
            {
                restartButton.onClick.RemoveListener(HandleRestartClicked);
            }
            if (nextSeedButton != null)
            {
                nextSeedButton.onClick.RemoveListener(HandleNextSeedClicked);
            }
            if (stopButton != null)
            {
                stopButton.onClick.RemoveListener(HandleStopClicked);
            }
        }

        private void HandleRestartClicked()
        {
            _restartRequested?.Invoke();
        }

        private void HandleNextSeedClicked()
        {
            _nextSeedRequested?.Invoke();
        }

        private void HandleStopClicked()
        {
            _stopRequested?.Invoke();
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class OnlineLobbyView : MonoBehaviour
    {
        [Header("Containers")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private GameObject connectionPanel;
        [SerializeField] private GameObject sessionPanel;

        [Header("Connection")]
        [SerializeField] private InputField displayNameInput;
        [SerializeField] private InputField joinCodeInput;
        [SerializeField] private Button createButton;
        [SerializeField] private Button joinButton;

        [Header("Session")]
        [SerializeField] private Button copyButton;
        [SerializeField] private Button readyButton;
        [SerializeField] private Button startButton;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Text inviteCodeText;
        [SerializeField] private Text sessionSummaryText;
        [SerializeField] private Text readyButtonText;
        [SerializeField] private Text startButtonText;
        [SerializeField] private Text[] playerRows = Array.Empty<Text>();
        [SerializeField] private GameObject startHint;
        [SerializeField] private GameObject runningMessage;

        [Header("Feedback")]
        [SerializeField] private Text statusText;

        private bool _buttonEventsBound;

        public event Action<string> CreateRequested;
        public event Action<string, string> JoinRequested;
        public event Action CopyRequested;
        public event Action ReadyRequested;
        public event Action StartRequested;
        public event Action LeaveRequested;

        public bool HasRequiredReferences =>
            canvasGroup != null &&
            connectionPanel != null &&
            sessionPanel != null &&
            displayNameInput != null &&
            joinCodeInput != null &&
            createButton != null &&
            joinButton != null &&
            copyButton != null &&
            readyButton != null &&
            startButton != null &&
            leaveButton != null &&
            inviteCodeText != null &&
            sessionSummaryText != null &&
            readyButtonText != null &&
            startButtonText != null &&
            playerRows != null &&
            playerRows.Length == MultiplayerConstants.MaxPlayers &&
            Array.TrueForAll(playerRows, row => row != null) &&
            startHint != null &&
            runningMessage != null &&
            statusText != null;

        public int PlayerRowCount => playerRows != null ? playerRows.Length : 0;

        public void Configure(
            CanvasGroup configuredCanvasGroup,
            GameObject configuredConnectionPanel,
            GameObject configuredSessionPanel,
            InputField configuredDisplayNameInput,
            InputField configuredJoinCodeInput,
            Button configuredCreateButton,
            Button configuredJoinButton,
            Button configuredCopyButton,
            Button configuredReadyButton,
            Button configuredStartButton,
            Button configuredLeaveButton,
            Text configuredInviteCodeText,
            Text configuredSessionSummaryText,
            Text configuredReadyButtonText,
            Text configuredStartButtonText,
            Text[] configuredPlayerRows,
            GameObject configuredStartHint,
            GameObject configuredRunningMessage,
            Text configuredStatusText)
        {
            canvasGroup = configuredCanvasGroup;
            connectionPanel = configuredConnectionPanel;
            sessionPanel = configuredSessionPanel;
            displayNameInput = configuredDisplayNameInput;
            joinCodeInput = configuredJoinCodeInput;
            createButton = configuredCreateButton;
            joinButton = configuredJoinButton;
            copyButton = configuredCopyButton;
            readyButton = configuredReadyButton;
            startButton = configuredStartButton;
            leaveButton = configuredLeaveButton;
            inviteCodeText = configuredInviteCodeText;
            sessionSummaryText = configuredSessionSummaryText;
            readyButtonText = configuredReadyButtonText;
            startButtonText = configuredStartButtonText;
            playerRows = configuredPlayerRows ?? Array.Empty<Text>();
            startHint = configuredStartHint;
            runningMessage = configuredRunningMessage;
            statusText = configuredStatusText;
        }

        public void SetDisplayName(string displayName)
        {
            if (displayNameInput != null)
            {
                displayNameInput.SetTextWithoutNotify(displayName ?? string.Empty);
            }
        }

        public void Render(
            SessionSnapshot snapshot,
            bool isInSession,
            bool busy,
            string status)
        {
            if (!HasRequiredReferences)
            {
                return;
            }

            snapshot = snapshot ?? SessionSnapshot.Empty;
            canvasGroup.interactable = !busy;

            connectionPanel.SetActive(!isInSession);
            sessionPanel.SetActive(isInSession);
            statusText.text = busy ? "Working..." : status ?? string.Empty;

            if (!isInSession)
            {
                return;
            }

            inviteCodeText.text = "Invite Code: " +
                (string.IsNullOrWhiteSpace(snapshot.Code) ? "-" : snapshot.Code);
            sessionSummaryText.text =
                "Players: " + snapshot.Players.Count + "/" + MultiplayerConstants.MaxPlayers +
                "   Phase: " + FormatPhase(snapshot.Phase);

            for (var index = 0; index < playerRows.Length; index++)
            {
                if (index >= snapshot.Players.Count)
                {
                    playerRows[index].text = "- Waiting for player...";
                    continue;
                }

                var player = snapshot.Players[index];
                var readiness = player.IsReady ? "READY" : "WAITING";
                var host = player.IsHost ? " | HOST" : string.Empty;
                var assignment = player.Slot >= 0 ? string.Empty : " | SYNCING SEAT";
                playerRows[index].text =
                    "- " + player.DisplayName + " [" + readiness + "]" + host + assignment;
            }

            var isLobby = snapshot.Phase == MultiplayerConstants.LobbyPhase;
            readyButton.gameObject.SetActive(isLobby);
            readyButtonText.text = snapshot.LocalReady ? "Cancel Ready" : "Ready";

            startButton.gameObject.SetActive(isLobby && snapshot.IsHost);
            startButton.interactable = !busy && snapshot.CanStart;
            startButtonText.text = "Start 4-Player Game";

            startHint.SetActive(isLobby && snapshot.IsHost && !snapshot.CanStart);
            runningMessage.SetActive(!isLobby);
        }

        private void Awake()
        {
            if (!HasRequiredReferences)
            {
                Debug.LogError(
                    "OnlineLobbyView is missing one or more required uGUI references.",
                    this);
                enabled = false;
                return;
            }

            BindButtonEvents();
        }

        private void OnDestroy()
        {
            UnbindButtonEvents();
        }

        private void BindButtonEvents()
        {
            if (_buttonEventsBound)
            {
                return;
            }

            createButton.onClick.AddListener(OnCreateClicked);
            joinButton.onClick.AddListener(OnJoinClicked);
            copyButton.onClick.AddListener(OnCopyClicked);
            readyButton.onClick.AddListener(OnReadyClicked);
            startButton.onClick.AddListener(OnStartClicked);
            leaveButton.onClick.AddListener(OnLeaveClicked);
            _buttonEventsBound = true;
        }

        private void UnbindButtonEvents()
        {
            if (!_buttonEventsBound)
            {
                return;
            }

            createButton.onClick.RemoveListener(OnCreateClicked);
            joinButton.onClick.RemoveListener(OnJoinClicked);
            copyButton.onClick.RemoveListener(OnCopyClicked);
            readyButton.onClick.RemoveListener(OnReadyClicked);
            startButton.onClick.RemoveListener(OnStartClicked);
            leaveButton.onClick.RemoveListener(OnLeaveClicked);
            _buttonEventsBound = false;
        }

        private void OnCreateClicked()
        {
            CreateRequested?.Invoke(NormalizeDisplayName());
        }

        private void OnJoinClicked()
        {
            var code = joinCodeInput.text.Trim().ToUpperInvariant();
            joinCodeInput.SetTextWithoutNotify(code);
            JoinRequested?.Invoke(code, NormalizeDisplayName());
        }

        private void OnCopyClicked()
        {
            CopyRequested?.Invoke();
        }

        private void OnReadyClicked()
        {
            ReadyRequested?.Invoke();
        }

        private void OnStartClicked()
        {
            StartRequested?.Invoke();
        }

        private void OnLeaveClicked()
        {
            LeaveRequested?.Invoke();
        }

        private string NormalizeDisplayName()
        {
            var displayName = displayNameInput.text.Trim();
            displayNameInput.SetTextWithoutNotify(displayName);
            return displayName;
        }

        private static string FormatPhase(string phase)
        {
            if (string.IsNullOrWhiteSpace(phase))
            {
                return "Unknown";
            }

            return char.ToUpperInvariant(phase[0]) + phase.Substring(1);
        }
    }
}

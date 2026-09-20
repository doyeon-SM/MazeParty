using System;
using UnityEngine;
using UnityEngine.Events;
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
        [SerializeField] private Button quitButton;
        [SerializeField] private Text inviteCodeText;
        [SerializeField] private Text sessionSummaryText;
        [SerializeField] private Text readyButtonText;
        [SerializeField] private Text startButtonText;
        [SerializeField] private Text[] playerRows = Array.Empty<Text>();
        [SerializeField] private GameObject startHint;
        [SerializeField] private GameObject runningMessage;

        [Header("Feedback")]
        [SerializeField] private Text statusText;

        [Header("Character Customization")]
        [SerializeField] private GameObject customizationPanel;
        [SerializeField] private Button[] paletteButtons = Array.Empty<Button>();
        [SerializeField] private Outline[] paletteOutlines = Array.Empty<Outline>();
        [SerializeField] private Toggle testHatToggle;

        private bool _buttonEventsBound;
        private UnityAction[] _paletteButtonActions = Array.Empty<UnityAction>();
        private int _selectedPaletteIndex;
        private float _nextPaletteAvailabilityRefresh;
        private bool _suppressAppearanceEvents;
        private bool _presentationVisible = true;
        private bool _lastBusy;

        public event Action<string> CreateRequested;
        public event Action<string, string> JoinRequested;
        public event Action CopyRequested;
        public event Action ReadyRequested;
        public event Action StartRequested;
        public event Action LeaveRequested;
        public event Action QuitRequested;
        public event Action<PlayerAppearanceState> AppearanceChanged;

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
            quitButton != null &&
            inviteCodeText != null &&
            sessionSummaryText != null &&
            readyButtonText != null &&
            startButtonText != null &&
            playerRows != null &&
            playerRows.Length == MultiplayerConstants.MaxPlayers &&
            Array.TrueForAll(playerRows, row => row != null) &&
            startHint != null &&
            runningMessage != null &&
            statusText != null &&
            customizationPanel != null &&
            paletteButtons != null &&
            paletteButtons.Length == LobbyColorPalette.Count &&
            Array.TrueForAll(paletteButtons, button => button != null) &&
            paletteOutlines != null &&
            paletteOutlines.Length == LobbyColorPalette.Count &&
            Array.TrueForAll(paletteOutlines, outline => outline != null) &&
            testHatToggle != null;

        public int PlayerRowCount => playerRows != null ? playerRows.Length : 0;
        public bool PresentationVisible => _presentationVisible;

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
            Button configuredQuitButton,
            Text configuredInviteCodeText,
            Text configuredSessionSummaryText,
            Text configuredReadyButtonText,
            Text configuredStartButtonText,
            Text[] configuredPlayerRows,
            GameObject configuredStartHint,
            GameObject configuredRunningMessage,
            Text configuredStatusText,
            GameObject configuredCustomizationPanel,
            Button[] configuredPaletteButtons,
            Outline[] configuredPaletteOutlines,
            Toggle configuredTestHatToggle)
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
            quitButton = configuredQuitButton;
            inviteCodeText = configuredInviteCodeText;
            sessionSummaryText = configuredSessionSummaryText;
            readyButtonText = configuredReadyButtonText;
            startButtonText = configuredStartButtonText;
            playerRows = configuredPlayerRows ?? Array.Empty<Text>();
            startHint = configuredStartHint;
            runningMessage = configuredRunningMessage;
            statusText = configuredStatusText;
            customizationPanel = configuredCustomizationPanel;
            paletteButtons = configuredPaletteButtons ?? Array.Empty<Button>();
            paletteOutlines = configuredPaletteOutlines ?? Array.Empty<Outline>();
            testHatToggle = configuredTestHatToggle;
        }

        public void SetDisplayName(string displayName)
        {
            if (displayNameInput != null)
            {
                displayNameInput.SetTextWithoutNotify(displayName ?? string.Empty);
            }
        }

        public void SetAppearance(PlayerAppearanceState appearance)
        {
            if (paletteButtons.Length == 0)
            {
                return;
            }

            _suppressAppearanceEvents = true;
            _selectedPaletteIndex = LobbyColorPalette.FindClosestIndex(
                (Color32)appearance.BodyColor);
            testHatToggle.SetIsOnWithoutNotify(appearance.HatId == 1);
            _suppressAppearanceEvents = false;
            RefreshPaletteAvailability();
        }

        public void SetPresentationVisible(bool visible)
        {
            _presentationVisible = visible;
            ApplyPresentationState();
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
            _lastBusy = busy;
            ApplyPresentationState();

            connectionPanel.SetActive(!isInSession);
            sessionPanel.SetActive(isInSession);
            quitButton.gameObject.SetActive(
                !isInSession || snapshot.Phase == MultiplayerConstants.LobbyPhase);
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

            startHint.SetActive(isLobby && snapshot.IsHost && !snapshot.CanStart);
            runningMessage.SetActive(!isLobby);
            customizationPanel.SetActive(isLobby);
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
            RefreshPaletteAvailability();
            ApplyPresentationState();
        }

        private void Update()
        {
            if (!customizationPanel.activeInHierarchy ||
                Time.unscaledTime < _nextPaletteAvailabilityRefresh)
            {
                return;
            }

            _nextPaletteAvailabilityRefresh = Time.unscaledTime + 0.15f;
            RefreshPaletteAvailability();
        }

        private void OnDestroy()
        {
            UnbindButtonEvents();
        }

        private void ApplyPresentationState()
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = _presentationVisible ? 1f : 0f;
            canvasGroup.interactable = _presentationVisible && !_lastBusy;
            canvasGroup.blocksRaycasts = _presentationVisible;
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
            quitButton.onClick.AddListener(OnQuitClicked);
            _paletteButtonActions = new UnityAction[paletteButtons.Length];
            for (var index = 0; index < paletteButtons.Length; index++)
            {
                var capturedIndex = index;
                UnityAction action = () => OnPaletteColorClicked(capturedIndex);
                _paletteButtonActions[index] = action;
                paletteButtons[index].onClick.AddListener(action);
            }

            testHatToggle.onValueChanged.AddListener(OnHatControlChanged);
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
            quitButton.onClick.RemoveListener(OnQuitClicked);
            for (var index = 0; index < paletteButtons.Length; index++)
            {
                if (index < _paletteButtonActions.Length &&
                    _paletteButtonActions[index] != null)
                {
                    paletteButtons[index].onClick.RemoveListener(
                        _paletteButtonActions[index]);
                }
            }

            testHatToggle.onValueChanged.RemoveListener(OnHatControlChanged);
            _paletteButtonActions = Array.Empty<UnityAction>();
            _buttonEventsBound = false;
        }

        private void OnPaletteColorClicked(int paletteIndex)
        {
            PublishAppearance(paletteIndex);
        }

        private void OnHatControlChanged(bool _)
        {
            PublishAppearance(_selectedPaletteIndex);
        }

        private void PublishAppearance(int paletteIndex)
        {
            if (_suppressAppearanceEvents || paletteButtons.Length == 0 ||
                paletteIndex < 0 || paletteIndex >= LobbyColorPalette.Count)
            {
                return;
            }

            AppearanceChanged?.Invoke(PlayerAppearanceState.FromColor(
                LobbyColorPalette.GetColor(paletteIndex),
                0,
                0,
                (byte)(testHatToggle.isOn ? 1 : 0),
                0));
        }

        private void RefreshPaletteAvailability()
        {
            if (paletteButtons.Length == 0)
            {
                return;
            }

            byte occupiedMask = 0;
            var localIndex = _selectedPaletteIndex;
            var avatars = FindObjectsByType<NetworkPlayerAvatar>();
            for (var avatarIndex = 0; avatarIndex < avatars.Length; avatarIndex++)
            {
                var avatar = avatars[avatarIndex];
                if (avatar == null || !avatar.IsSpawned || avatar.AssignedSlot < 0 ||
                    !LobbyColorPalette.TryGetIndex(
                        (Color32)avatar.Appearance.BodyColor,
                        out var paletteIndex))
                {
                    continue;
                }

                if (avatar.IsOwner)
                {
                    localIndex = paletteIndex;
                }
                else
                {
                    occupiedMask = (byte)(occupiedMask | (1 << paletteIndex));
                }
            }

            _selectedPaletteIndex = Mathf.Clamp(
                localIndex,
                0,
                LobbyColorPalette.Count - 1);
            for (var index = 0; index < paletteButtons.Length; index++)
            {
                var selected = index == _selectedPaletteIndex;
                paletteButtons[index].interactable =
                    selected || (occupiedMask & (1 << index)) == 0;
                paletteOutlines[index].enabled = selected;
            }
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

        private void OnQuitClicked()
        {
            QuitRequested?.Invoke();
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

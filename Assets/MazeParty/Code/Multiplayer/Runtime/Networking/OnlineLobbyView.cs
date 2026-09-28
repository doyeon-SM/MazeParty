using System;
using MazeParty.Gameplay;
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
        [SerializeField] private GameObject joinCodePopup;
        [SerializeField] private GameObject sessionPanel;

        [Header("Connection")]
        [SerializeField] private InputField displayNameInput;
        [SerializeField] private InputField joinCodeInput;
        [SerializeField] private Button createButton;
        [SerializeField] private Button openJoinPopupButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button cancelJoinButton;

        [Header("Session")]
        [SerializeField] private Button copyButton;
        [SerializeField] private Button readyButton;
        [SerializeField] private Button startButton;
        [SerializeField] private Text inviteCodeText;
        [SerializeField] private Text sessionSummaryText;
        [SerializeField] private Text readyButtonText;
        [SerializeField] private Text startButtonText;
        [SerializeField] private Text[] playerRows = Array.Empty<Text>();
        [SerializeField] private GameObject startHint;
        [SerializeField] private GameObject runningMessage;
        [SerializeField] private HoldToRevealButton inviteCodeRevealButton;

        [Header("Feedback")]
        [SerializeField] private Text statusText;

        [Header("Character Customization")]
        [SerializeField] private GameObject customizationPanel;
        [SerializeField] private Button customizationButton;
        [SerializeField] private Button[] paletteButtons = Array.Empty<Button>();
        [SerializeField] private Outline[] paletteOutlines = Array.Empty<Outline>();
        [SerializeField] private Toggle testHatToggle;

        private bool _buttonEventsBound;
        private UnityAction[] _paletteButtonActions = Array.Empty<UnityAction>();
        private int _selectedPaletteIndex;
        private bool _joinPopupOpen;
        private bool _customizationOpen;
        private bool _lastRenderedInSession;
        private bool _inviteCodeRevealed;
        private string _currentInviteCode = string.Empty;
        public byte SelectedExpression { get; private set; }
        public void SelectExpression(byte id)
        { SelectedExpression = MazeParty.Gameplay.PlayerExpressionCatalog.SanitizeFace(id); PublishAppearance(_selectedPaletteIndex); }
        private float _nextPaletteAvailabilityRefresh;
        private bool _suppressAppearanceEvents;
        private bool _presentationVisible = true;
        private bool _lastBusy;
        private bool _recoveryChoiceVisible;
        private bool _recoveryChoiceCanChoose = true;
        private bool _recoveryChoicePresented;
        private bool _hasCapturedActionLabels;
        private string _readyButtonTextBeforeRecovery = string.Empty;
        private string _startButtonTextBeforeRecovery = string.Empty;

        public event Action<string> CreateRequested;
        public event Action<string, string> JoinRequested;
        public event Action CopyRequested;
        public event Action ReadyRequested;
        public event Action StartRequested;
        public event Action RecoveryContinueRequested;
        public event Action RecoveryDiscardRequested;
        public event Action<PlayerAppearanceState> AppearanceChanged;

        public bool HasRequiredReferences =>
            canvasGroup != null &&
            connectionPanel != null &&
            joinCodePopup != null &&
            sessionPanel != null &&
            displayNameInput != null &&
            joinCodeInput != null &&
            createButton != null &&
            openJoinPopupButton != null &&
            joinButton != null &&
            cancelJoinButton != null &&
            copyButton != null &&
            readyButton != null &&
            startButton != null &&
            inviteCodeText != null &&
            sessionSummaryText != null &&
            readyButtonText != null &&
            startButtonText != null &&
            playerRows != null &&
            playerRows.Length == MultiplayerConstants.MaxPlayers &&
            Array.TrueForAll(playerRows, row => row != null) &&
            startHint != null &&
            runningMessage != null &&
            inviteCodeRevealButton != null &&
            inviteCodeRevealButton.HasRequiredReferences &&
            statusText != null &&
            customizationPanel != null &&
            customizationButton != null &&
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

        public void ConfigureInteractionPanels(
            GameObject configuredJoinCodePopup,
            Button configuredOpenJoinPopupButton,
            Button configuredCancelJoinButton,
            HoldToRevealButton configuredInviteCodeRevealButton,
            Button configuredCustomizationButton)
        {
            joinCodePopup = configuredJoinCodePopup;
            openJoinPopupButton = configuredOpenJoinPopupButton;
            cancelJoinButton = configuredCancelJoinButton;
            inviteCodeRevealButton = configuredInviteCodeRevealButton;
            customizationButton = configuredCustomizationButton;
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

            SelectedExpression = appearance.ExpressionId;
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
            if (!visible)
            {
                SetInviteCodeRevealed(false);
            }
            ApplyPresentationState();
        }

        public void SetRecoveryChoice(bool visible, bool canChoose = true)
        {
            if (visible && !_recoveryChoiceVisible)
            {
                CaptureActionLabelsBeforeRecovery();
            }

            _recoveryChoiceVisible = visible;
            _recoveryChoiceCanChoose = canChoose;
            if (!visible)
            {
                _recoveryChoicePresented = false;
                RestoreActionLabelsAfterRecovery();
                _hasCapturedActionLabels = false;
                return;
            }

            if (_recoveryChoicePresented)
            {
                var interactable = !_lastBusy && _recoveryChoiceCanChoose;
                readyButton.interactable = interactable;
                startButton.interactable = interactable;
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
            _lastBusy = busy;
            ApplyPresentationState();

            if (isInSession != _lastRenderedInSession)
            {
                if (isInSession)
                {
                    CloseJoinPopup(true);
                }
                else
                {
                    _customizationOpen = false;
                    SetInviteCodeRevealed(false);
                    _currentInviteCode = string.Empty;
                    joinCodeInput.SetTextWithoutNotify(string.Empty);
                }

                _lastRenderedInSession = isInSession;
            }

            connectionPanel.SetActive(!isInSession);
            joinCodePopup.SetActive(!isInSession && _joinPopupOpen);
            sessionPanel.SetActive(isInSession);
            statusText.text = busy ? GameText.T("Working...") : status ?? string.Empty;

            if (!isInSession)
            {
                _recoveryChoicePresented = false;
                RestoreActionLabelsAfterRecovery();
                customizationButton.gameObject.SetActive(false);
                customizationPanel.SetActive(false);
                return;
            }

            _currentInviteCode = string.IsNullOrWhiteSpace(snapshot.Code)
                ? string.Empty
                : snapshot.Code.Trim();
            RefreshInviteCodeText();
            sessionSummaryText.text = GameText.F(
                "Players: {0}/{1}   Phase: {2}",
                snapshot.Players.Count,
                MultiplayerConstants.MaxPlayers,
                FormatPhase(snapshot.Phase));

            for (var index = 0; index < playerRows.Length; index++)
            {
                if (index >= snapshot.Players.Count)
                {
                    playerRows[index].text = GameText.T("- Waiting for player...");
                    continue;
                }

                var player = snapshot.Players[index];
                var readiness = player.IsReady ? GameText.T("READY") : GameText.T("WAITING");
                var host = player.IsHost ? " | " + GameText.T("HOST") : string.Empty;
                var assignment = player.Slot >= 0 ? string.Empty : " | " + GameText.T("SYNCING SEAT");
                playerRows[index].text =
                    "- " + player.DisplayName + " [" + readiness + "]" + host + assignment;
            }

            var isLobby = snapshot.Phase == MultiplayerConstants.LobbyPhase;
            var showRecoveryChoice =
                _recoveryChoiceVisible && isLobby && snapshot.IsHost;
            _recoveryChoicePresented = showRecoveryChoice;
            if (showRecoveryChoice)
            {
                CaptureActionLabelsBeforeRecovery();
                readyButton.gameObject.SetActive(true);
                readyButton.interactable = !busy && _recoveryChoiceCanChoose;
                readyButtonText.text = GameText.T("Discard Saved Match");

                startButton.gameObject.SetActive(true);
                startButton.interactable = !busy && _recoveryChoiceCanChoose;
                startButtonText.text = GameText.T("Continue Saved Match");

                startHint.SetActive(false);
                runningMessage.SetActive(false);
                _customizationOpen = false;
                customizationButton.gameObject.SetActive(false);
                customizationPanel.SetActive(false);
                return;
            }

            RestoreActionLabelsAfterRecovery();
            readyButton.gameObject.SetActive(isLobby);
            readyButton.interactable = !busy;
            readyButtonText.text = snapshot.LocalReady ? GameText.T("Cancel Ready") : GameText.T("Ready");

            startButton.gameObject.SetActive(isLobby && snapshot.IsHost);
            startButton.interactable = !busy && snapshot.CanStart;

            startHint.SetActive(isLobby && snapshot.IsHost && !snapshot.CanStart);
            runningMessage.SetActive(!isLobby);
            if (!isLobby)
            {
                _customizationOpen = false;
            }

            customizationButton.gameObject.SetActive(isLobby);
            customizationPanel.SetActive(isLobby && _customizationOpen);
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
            joinCodeInput.contentType = InputField.ContentType.Custom;
            joinCodeInput.inputType = InputField.InputType.Password;
            joinCodeInput.characterValidation = InputField.CharacterValidation.Alphanumeric;
            joinCodeInput.asteriskChar = '*';
            joinCodeInput.ForceLabelUpdate();
            joinCodePopup.SetActive(false);
            customizationPanel.SetActive(false);
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
            openJoinPopupButton.onClick.AddListener(OnOpenJoinPopupClicked);
            joinButton.onClick.AddListener(OnJoinClicked);
            cancelJoinButton.onClick.AddListener(OnCancelJoinClicked);
            copyButton.onClick.AddListener(OnCopyClicked);
            readyButton.onClick.AddListener(OnReadyClicked);
            startButton.onClick.AddListener(OnStartClicked);
            customizationButton.onClick.AddListener(OnCustomizationClicked);
            inviteCodeRevealButton.HoldChanged += OnInviteCodeRevealChanged;
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
            openJoinPopupButton.onClick.RemoveListener(OnOpenJoinPopupClicked);
            joinButton.onClick.RemoveListener(OnJoinClicked);
            cancelJoinButton.onClick.RemoveListener(OnCancelJoinClicked);
            copyButton.onClick.RemoveListener(OnCopyClicked);
            readyButton.onClick.RemoveListener(OnReadyClicked);
            startButton.onClick.RemoveListener(OnStartClicked);
            customizationButton.onClick.RemoveListener(OnCustomizationClicked);
            inviteCodeRevealButton.HoldChanged -= OnInviteCodeRevealChanged;
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
                0, SelectedExpression));
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

        private void OnOpenJoinPopupClicked()
        {
            _joinPopupOpen = true;
            joinCodePopup.SetActive(true);
            joinCodeInput.Select();
            joinCodeInput.ActivateInputField();
        }

        private void OnJoinClicked()
        {
            var code = joinCodeInput.text.Trim().ToUpperInvariant();
            joinCodeInput.SetTextWithoutNotify(code);
            JoinRequested?.Invoke(code, NormalizeDisplayName());
        }

        private void OnCancelJoinClicked()
        {
            CloseJoinPopup(true);
        }

        private void OnCopyClicked()
        {
            CopyRequested?.Invoke();
        }

        private void OnReadyClicked()
        {
            if (_recoveryChoicePresented)
            {
                RecoveryDiscardRequested?.Invoke();
                return;
            }

            ReadyRequested?.Invoke();
        }

        private void OnStartClicked()
        {
            if (_recoveryChoicePresented)
            {
                RecoveryContinueRequested?.Invoke();
                return;
            }

            StartRequested?.Invoke();
        }

        private void OnCustomizationClicked()
        {
            if (!customizationButton.gameObject.activeInHierarchy)
            {
                return;
            }

            _customizationOpen = !_customizationOpen;
            customizationPanel.SetActive(_customizationOpen);
        }

        private void OnInviteCodeRevealChanged(bool held)
        {
            SetInviteCodeRevealed(
                held && sessionPanel.activeInHierarchy &&
                !string.IsNullOrEmpty(_currentInviteCode));
        }

        private void SetInviteCodeRevealed(bool revealed)
        {
            _inviteCodeRevealed = revealed;
            RefreshInviteCodeText();
        }

        private void RefreshInviteCodeText()
        {
            if (inviteCodeText == null)
            {
                return;
            }

            var displayedCode = string.IsNullOrEmpty(_currentInviteCode)
                ? "-"
                : _inviteCodeRevealed
                    ? _currentInviteCode
                    : new string('*', _currentInviteCode.Length);
            inviteCodeText.text = GameText.F("Invite Code: {0}", displayedCode);
        }

        private void CloseJoinPopup(bool clearInput)
        {
            _joinPopupOpen = false;
            joinCodePopup.SetActive(false);
            if (clearInput)
            {
                joinCodeInput.SetTextWithoutNotify(string.Empty);
            }
        }

        private void CaptureActionLabelsBeforeRecovery()
        {
            if (_hasCapturedActionLabels ||
                readyButtonText == null || startButtonText == null)
            {
                return;
            }

            _readyButtonTextBeforeRecovery = readyButtonText.text;
            _startButtonTextBeforeRecovery = startButtonText.text;
            _hasCapturedActionLabels = true;
        }

        private void RestoreActionLabelsAfterRecovery()
        {
            if (!_hasCapturedActionLabels)
            {
                return;
            }

            if (readyButtonText != null)
            {
                readyButtonText.text = _readyButtonTextBeforeRecovery;
            }

            if (startButtonText != null)
            {
                startButtonText.text = _startButtonTextBeforeRecovery;
            }
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
                return GameText.T("Unknown");
            }

            // Known session phases get a translated label; the phase string itself is unchanged.
            if (phase == MultiplayerConstants.LobbyPhase)
            {
                return GameText.T("Lobby");
            }

            if (phase == MultiplayerConstants.PlayingPhase)
            {
                return GameText.T("Playing");
            }

            return char.ToUpperInvariant(phase[0]) + phase.Substring(1);
        }
    }
}

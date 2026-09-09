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
        private GameObject _customizationPanel;
        private Button[] _paletteButtons = Array.Empty<Button>();
        private Outline[] _paletteOutlines = Array.Empty<Outline>();
        private Toggle _testHatToggle;
        private int _selectedPaletteIndex;
        private float _nextPaletteAvailabilityRefresh;
        private bool _suppressAppearanceEvents;

        public event Action<string> CreateRequested;
        public event Action<string, string> JoinRequested;
        public event Action CopyRequested;
        public event Action ReadyRequested;
        public event Action StartRequested;
        public event Action LeaveRequested;
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

        public void SetAppearance(PlayerAppearanceState appearance)
        {
            EnsureCustomizationUi();
            if (_paletteButtons.Length == 0)
            {
                return;
            }

            _suppressAppearanceEvents = true;
            _selectedPaletteIndex = LobbyColorPalette.FindClosestIndex(
                (Color32)appearance.BodyColor);
            _testHatToggle.SetIsOnWithoutNotify(appearance.HatId == 1);
            _suppressAppearanceEvents = false;
            RefreshPaletteAvailability();
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
            if (_customizationPanel != null)
            {
                _customizationPanel.SetActive(isLobby);
            }
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

            EnsureCustomizationUi();
            ApplyPlayerNameFont();
            BindButtonEvents();
        }

        private void Update()
        {
            if (_customizationPanel == null || !_customizationPanel.activeInHierarchy ||
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

        private void EnsureCustomizationUi()
        {
            if (_customizationPanel != null || sessionPanel == null)
            {
                return;
            }

            var font = Resources.Load<Font>("MazeParty/Fonts/PlayerNameFont");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            var sessionSize = sessionPanel.GetComponent<LayoutElement>();
            if (sessionSize != null)
            {
                sessionSize.preferredHeight = Mathf.Max(sessionSize.preferredHeight, 700f);
            }

            _customizationPanel = CreateUiObject("Player Customization", sessionPanel.transform);
            var layout = _customizationPanel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var panelSize = _customizationPanel.AddComponent<LayoutElement>();
            panelSize.preferredHeight = 164f;

            CreateRuntimeText(
                "Customization Label",
                _customizationPanel.transform,
                "Character - Unique Body Color / Test Hat",
                font,
                17,
                26f);

            var paletteGrid = CreateUiObject("Body Color Palette", _customizationPanel.transform);
            var grid = paletteGrid.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(102f, 36f);
            grid.spacing = new Vector2(8f, 8f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.MiddleLeft;
            var gridSize = paletteGrid.AddComponent<LayoutElement>();
            gridSize.preferredHeight = 80f;

            _paletteButtons = new Button[LobbyColorPalette.Count];
            _paletteOutlines = new Outline[LobbyColorPalette.Count];
            for (var index = 0; index < LobbyColorPalette.Count; index++)
            {
                var capturedIndex = index;
                var buttonObject = CreateUiObject(
                    LobbyColorPalette.GetDisplayName(index) + " Color Button",
                    paletteGrid.transform);
                var image = buttonObject.AddComponent<Image>();
                image.color = LobbyColorPalette.GetColor(index);
                var outline = buttonObject.AddComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(3f, -3f);
                var button = buttonObject.AddComponent<Button>();
                button.targetGraphic = image;
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, 1f, 1f, 0.82f);
                colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.32f);
                button.colors = colors;

                var label = CreateRuntimeText(
                    "Label",
                    buttonObject.transform,
                    LobbyColorPalette.GetDisplayName(index),
                    font,
                    14,
                    36f);
                Destroy(label.GetComponent<LayoutElement>());
                Stretch(label.rectTransform, 0f, 0f, 1f, 1f, 4f, 2f, -4f, -2f);
                label.alignment = TextAnchor.MiddleCenter;
                label.raycastTarget = false;
                label.color = index == 2 ? Color.black : Color.white;

                button.onClick.AddListener(() => OnPaletteColorClicked(capturedIndex));
                _paletteButtons[index] = button;
                _paletteOutlines[index] = outline;
            }

            var footer = CreateUiObject("Customization Footer", _customizationPanel.transform);
            var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 10f;
            footerLayout.childAlignment = TextAnchor.MiddleLeft;
            footerLayout.childControlWidth = false;
            footerLayout.childControlHeight = true;
            var footerSize = footer.AddComponent<LayoutElement>();
            footerSize.preferredHeight = 32f;

            _testHatToggle = CreateToggle("Test Hat", footer.transform, font);

            _testHatToggle.onValueChanged.AddListener(OnHatControlChanged);
            RefreshPaletteAvailability();
        }

        private void ApplyPlayerNameFont()
        {
            var font = Resources.Load<Font>("MazeParty/Fonts/PlayerNameFont");
            if (font == null)
            {
                return;
            }

            var texts = GetComponentsInChildren<Text>(true);
            for (var index = 0; index < texts.Length; index++)
            {
                texts[index].font = font;
            }
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
            if (_suppressAppearanceEvents || _paletteButtons.Length == 0 ||
                paletteIndex < 0 || paletteIndex >= LobbyColorPalette.Count)
            {
                return;
            }

            AppearanceChanged?.Invoke(PlayerAppearanceState.FromColor(
                LobbyColorPalette.GetColor(paletteIndex),
                0,
                0,
                (byte)(_testHatToggle.isOn ? 1 : 0),
                0));
        }

        private void RefreshPaletteAvailability()
        {
            if (_paletteButtons.Length == 0)
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
            for (var index = 0; index < _paletteButtons.Length; index++)
            {
                var selected = index == _selectedPaletteIndex;
                _paletteButtons[index].interactable =
                    selected || (occupiedMask & (1 << index)) == 0;
                if (_paletteOutlines[index] != null)
                {
                    _paletteOutlines[index].enabled = selected;
                }
            }
        }

        private static Toggle CreateToggle(string label, Transform parent, Font font)
        {
            var root = CreateUiObject(label + " Toggle", parent);
            var size = root.AddComponent<LayoutElement>();
            size.preferredWidth = 180f;
            size.preferredHeight = 28f;
            var background = CreateUiObject("Background", root.transform);
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(0f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(24f, 24f);
            backgroundRect.anchoredPosition = new Vector2(12f, 0f);
            var backgroundImage = background.AddComponent<Image>();
            backgroundImage.color = new Color(0.12f, 0.15f, 0.2f, 1f);
            var checkmark = CreateUiObject("Checkmark", background.transform);
            Stretch(checkmark.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f, 4f, 4f, -4f, -4f);
            var checkmarkImage = checkmark.AddComponent<Image>();
            checkmarkImage.color = new Color(0.3f, 0.75f, 1f, 1f);
            var text = CreateRuntimeText("Label", root.transform, label, font, 15, 28f);
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(34f, 0f);
            textRect.offsetMax = Vector2.zero;
            var toggle = root.AddComponent<Toggle>();
            toggle.targetGraphic = backgroundImage;
            toggle.graphic = checkmarkImage;
            return toggle;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var value = new GameObject(name, typeof(RectTransform));
            value.layer = parent.gameObject.layer;
            value.transform.SetParent(parent, false);
            return value;
        }

        private static Text CreateRuntimeText(
            string name,
            Transform parent,
            string value,
            Font font,
            int size,
            float height)
        {
            var text = CreateUiObject(name, parent).AddComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            var layout = text.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            return text;
        }

        private static void Stretch(
            RectTransform rect,
            float minX,
            float minY,
            float maxX,
            float maxY,
            float left,
            float bottom,
            float right,
            float top)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
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

using System;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Runtime uGUI/Canvas presentation for the online board flow. All visible copy
    /// intentionally remains English until the project's Korean font is installed.
    /// </summary>
    public sealed class BoardFlowView : MonoBehaviour
    {
        [SerializeField] private GameplayCameraDirector cameraDirector;

        private readonly Image[] _slotBackgrounds = new Image[GameplayInventory.Capacity];
        private readonly Text[] _slotLabels = new Text[GameplayInventory.Capacity];
        private readonly Button[] _choiceButtons = new Button[GameplayInventory.Capacity];
        private readonly Text[] _choiceLabels = new Text[GameplayInventory.Capacity];
        private readonly Text[] _playerRows = new Text[MultiplayerConstants.MaxPlayers];

        private GameObject _selectionPanel;
        private GameObject _readyPanel;
        private GameObject _resultPanel;
        private GameObject _reconnectOverlay;
        private GameObject _reticle;
        private Text _turnText;
        private Text _phaseText;
        private Text _phaseTimerText;
        private Text _choiceTimerText;
        private Text _shieldText;
        private Text _diceText;
        private Text _movesText;
        private Text _ammoText;
        private Text _statusText;
        private Text _tooltipText;
        private Text _reconnectText;
        private Button _noItemButton;
        private Button _readyButton;
        private NetworkPlayerAvatar _localAvatar;
        private KeyShopWorldMarker _keyShopMarker;
        private BoardTopology _topology;
        private int _lastRevision = -1;
        private ItemChoiceResolution _lastChoiceResolution = ItemChoiceResolution.NotStarted;
        private bool _wired;

        public void Configure(GameplayCameraDirector director)
        {
            cameraDirector = director;
            BindUi();
        }

        private void Awake()
        {
            BindUi();
        }

        private void OnDestroy()
        {
            UnwireButtons();
        }

        private void Update()
        {
            ResolveLocalAvatar();
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsSpawned || !match.GameplayEnabled)
            {
                SetWaitingState();
                return;
            }

            RefreshPanels(match);
            RefreshHeader(match);
            RefreshLocalPlayer(match);
            RefreshInventory();
            RefreshPlayerRows(match);
            RefreshKeyShop(match);
            RefreshCursor(match);
            RefreshStatusOnStateChange(match);
        }

        public void SelectItem(int slotIndex)
        {
            _localAvatar?.ChooseItem(slotIndex);
            HideItemTooltip();
        }

        public void ChooseNoItem()
        {
            _localAvatar?.ChooseNoItem();
            HideItemTooltip();
        }

        public void SetReady()
        {
            _localAvatar?.SetMinigameReady();
        }

        public void ShowItemTooltip(int slotIndex)
        {
            if (_tooltipText == null || _localAvatar == null)
            {
                return;
            }

            _tooltipText.text = _localAvatar.GetLocalItemName(slotIndex) + "\n" +
                                _localAvatar.GetLocalItemDescription(slotIndex);
            _tooltipText.gameObject.SetActive(true);
        }

        public void HideItemTooltip()
        {
            if (_tooltipText != null)
            {
                _tooltipText.gameObject.SetActive(false);
            }
        }

        private void BindUi()
        {
            _selectionPanel = FindNamed("ItemSelectionPanel");
            _readyPanel = FindNamed("MinigameReadyPanel");
            _resultPanel = FindNamed("SkippedResultPanel");
            _reconnectOverlay = FindNamed("ReconnectOverlay");
            _reticle = FindNamed("BoardReticle");
            _turnText = FindNamedComponent<Text>("TurnText");
            _phaseText = FindNamedComponent<Text>("PhaseText");
            _phaseTimerText = FindNamedComponent<Text>("PhaseTimerText");
            _choiceTimerText = FindNamedComponent<Text>("BoardChoiceTimerText");
            _shieldText = FindNamedComponent<Text>("BoardShieldText");
            _diceText = FindNamedComponent<Text>("DiceText");
            _movesText = FindNamedComponent<Text>("MovesText");
            _ammoText = FindNamedComponent<Text>("BoardAmmoText");
            _statusText = FindNamedComponent<Text>("BoardStatusText");
            _tooltipText = FindNamedComponent<Text>("BoardTooltipText");
            _reconnectText = FindNamedComponent<Text>("ReconnectText");
            _noItemButton = FindNamedComponent<Button>("NoItemButton");
            _readyButton = FindNamedComponent<Button>("ReadyButton");

            for (var i = 0; i < GameplayInventory.Capacity; i++)
            {
                _slotBackgrounds[i] = FindNamedComponent<Image>("BoardInventorySlot" + i);
                _slotLabels[i] = FindNamedComponent<Text>("BoardInventorySlotLabel" + i);
                _choiceButtons[i] = FindNamedComponent<Button>("ItemChoiceButton" + i);
                _choiceLabels[i] = FindNamedComponent<Text>("ItemChoiceLabel" + i);
                var hover = FindNamedComponent<BoardItemChoiceButton>("ItemChoiceButton" + i);
                hover?.Configure(this, i);
            }

            for (var i = 0; i < MultiplayerConstants.MaxPlayers; i++)
            {
                _playerRows[i] = FindNamedComponent<Text>("PlayerState" + i);
            }

            WireButtons();
        }

        private void WireButtons()
        {
            if (_wired)
            {
                return;
            }

            for (var i = 0; i < _choiceButtons.Length; i++)
            {
                var slot = i;
                _choiceButtons[i]?.onClick.AddListener(() => SelectItem(slot));
            }

            _noItemButton?.onClick.AddListener(ChooseNoItem);
            _readyButton?.onClick.AddListener(SetReady);
            _wired = true;
        }

        private void UnwireButtons()
        {
            if (!_wired)
            {
                return;
            }

            for (var i = 0; i < _choiceButtons.Length; i++)
            {
                _choiceButtons[i]?.onClick.RemoveAllListeners();
            }
            _noItemButton?.onClick.RemoveListener(ChooseNoItem);
            _readyButton?.onClick.RemoveListener(SetReady);
            _wired = false;
        }

        private void ResolveLocalAvatar()
        {
            if (_localAvatar != null && _localAvatar.IsSpawned)
            {
                return;
            }

            var manager = Unity.Netcode.NetworkManager.Singleton;
            var playerObject = manager != null && manager.SpawnManager != null
                ? manager.SpawnManager.GetLocalPlayerObject()
                : null;
            _localAvatar = playerObject != null
                ? playerObject.GetComponent<NetworkPlayerAvatar>()
                : null;
        }

        private void RefreshPanels(NetworkMatchState match)
        {
            var choicePending = match.FlowState == BoardFlowState.Action &&
                                _localAvatar != null &&
                                _localAvatar.LocalChoiceResolution == ItemChoiceResolution.Pending &&
                                !match.IsReconnectPaused;
            SetActive(_selectionPanel, choicePending);
            SetActive(_readyPanel,
                match.FlowState == BoardFlowState.MinigameIntroReady && !match.IsReconnectPaused);
            SetActive(_resultPanel,
                match.FlowState == BoardFlowState.SkippedResult && !match.IsReconnectPaused);
            SetActive(_reconnectOverlay, match.IsReconnectPaused);
            SetActive(_reticle,
                match.FlowState == BoardFlowState.Action && !choicePending && !match.IsReconnectPaused);

            if (_reconnectText != null)
            {
                _reconnectText.text = "PLAYER DISCONNECTED\nMATCH PAUSED\n" +
                                      FormatClock(match.ReconnectRemaining) + " remaining";
            }
        }

        private void RefreshHeader(NetworkMatchState match)
        {
            SetText(_turnText, "TURN " + match.Turn);
            SetText(_phaseText, PhaseLabel(match.FlowState));

            var remaining = match.FlowState == BoardFlowState.Action
                ? match.ActionRemaining
                : match.StateRemaining;
            SetText(_phaseTimerText,
                HasCountdown(match.FlowState) ? FormatClock(remaining) : "--:--");

            SetText(_choiceTimerText,
                match.FlowState == BoardFlowState.Action && _localAvatar != null &&
                _localAvatar.LocalChoiceResolution == ItemChoiceResolution.Pending
                    ? "CHOOSE  " + FormatClock(match.ChoiceRemaining)
                    : "CHOICE  " + ChoiceLabel(_localAvatar));

            if (_shieldText != null)
            {
                _shieldText.text = match.ShieldRemaining > 0d
                    ? "SHIELD  " + match.ShieldRemaining.ToString("0.0") + "s"
                    : "SHIELD  OFF";
                _shieldText.color = match.ShieldRemaining > 0d
                    ? new Color(0.25f, 1f, 0.75f)
                    : new Color(1f, 0.45f, 0.45f);
            }
        }

        private void RefreshLocalPlayer(NetworkMatchState match)
        {
            if (_localAvatar == null)
            {
                SetText(_diceText, "DICE  WAITING FOR PLAYER");
                SetText(_movesText, "MOVES  --");
                return;
            }

            if (match.FlowState != BoardFlowState.Action)
            {
                SetText(_diceText, "DICE  --");
                SetText(_movesText, "MOVES  --");
                return;
            }

            SetText(_diceText, _localAvatar.LocalVisibleRoll > 0
                ? "DICE  " + _localAvatar.LocalVisibleRoll
                : _localAvatar.HasResolvedItemChoice
                        ? "RMB  AIM AT YOUR DIE TO ROLL"
                    : "DICE  CHOOSE ITEM FIRST");
            SetText(_movesText, _localAvatar.HasRolled && _localAvatar.LocalRemainingMoves == 0
                ? "MOVES  0  /  FREE MOVE IN ROOM"
                : "MOVES  " + _localAvatar.LocalRemainingMoves);
        }

        private void RefreshInventory()
        {
            for (var i = 0; i < GameplayInventory.Capacity; i++)
            {
                var occupied = _localAvatar != null && _localAvatar.IsLocalItemOccupied(i);
                var selected = _localAvatar != null && _localAvatar.LocalSelectedItemSlot == i;
                var label = occupied ? _localAvatar.GetLocalItemName(i) : "EMPTY";
                SetText(_slotLabels[i], label);
                SetText(_choiceLabels[i], label);

                if (_slotBackgrounds[i] != null)
                {
                    _slotBackgrounds[i].color = selected
                        ? new Color(1f, 0.72f, 0.15f, 0.97f)
                        : occupied
                            ? new Color(0.18f, 0.36f, 0.58f, 0.94f)
                            : new Color(0.1f, 0.12f, 0.16f, 0.88f);
                }

                if (_choiceButtons[i] != null)
                {
                    _choiceButtons[i].interactable = occupied;
                }
            }

            SetText(_ammoText,
                _localAvatar != null && _localAvatar.LocalSelectedItemSlot >= 0
                    ? "CHARGE  1\nLMB  USE ITEM"
                    : "CHARGE  --");
        }

        private void RefreshPlayerRows(NetworkMatchState match)
        {
            for (var slot = 0; slot < _playerRows.Length; slot++)
            {
                if (_playerRows[slot] == null)
                {
                    continue;
                }

                var state = !match.IsPlayerPresent(slot)
                    ? "RECONNECTING"
                    : match.FlowState == BoardFlowState.MinigameIntroReady
                        ? (match.IsMinigameReady(slot) ? "READY" : "WAITING")
                        : match.FlowState == BoardFlowState.Action
                            ? (match.HasArrived(slot) ? "ARRIVED" : match.HasRolled(slot) ? "MOVING" : "CHOOSING")
                            : "ON BOARD";
                _playerRows[slot].text = "P" + (slot + 1) + "  " + state;
                _playerRows[slot].color = match.IsPlayerPresent(slot)
                    ? PlayerColor(slot)
                    : new Color(1f, 0.45f, 0.35f);
            }
        }

        private void RefreshKeyShop(NetworkMatchState match)
        {
            if (_keyShopMarker == null)
            {
                _keyShopMarker = FindAnyObjectByType<KeyShopWorldMarker>();
            }
            if (_topology == null)
            {
                _topology = FindAnyObjectByType<BoardTopology>();
            }

            _keyShopMarker?.ApplyReplicatedState(
                match.KeyShopLifecycle,
                match.KeyShopHasLocation,
                match.KeyShopLocation,
                _topology,
                Mathf.Max(0, match.KeyShopRevision));
        }

        private void RefreshCursor(NetworkMatchState match)
        {
            if (cameraDirector == null)
            {
                cameraDirector = FindAnyObjectByType<GameplayCameraDirector>();
            }

            var choicePending = _localAvatar != null &&
                                _localAvatar.LocalChoiceResolution == ItemChoiceResolution.Pending;
            var pointerVisible = match.IsReconnectPaused ||
                                 match.FlowState != BoardFlowState.Action ||
                                 choicePending;
            cameraDirector?.SetUiPointerVisible(pointerVisible);
        }

        private void RefreshStatusOnStateChange(NetworkMatchState match)
        {
            var choice = _localAvatar != null
                ? _localAvatar.LocalChoiceResolution
                : ItemChoiceResolution.NotStarted;
            if (_lastRevision == match.StateRevision && _lastChoiceResolution == choice)
            {
                return;
            }

            _lastRevision = match.StateRevision;
            _lastChoiceResolution = choice;
            if (choice == ItemChoiceResolution.TimedOut)
            {
                SetText(_statusText, "Choice timed out. DO NOT USE selected automatically.");
                return;
            }

            switch (match.FlowState)
            {
                case BoardFlowState.TurnOverview:
                    SetText(_statusText, "Board overview: inspect every player and tile.");
                    break;
                case BoardFlowState.Descending:
                    SetText(_statusText, "Camera descending to your first-person view.");
                    break;
                case BoardFlowState.Action:
                    SetText(_statusText, choice == ItemChoiceResolution.Pending
                        ? "Choose an item or DO NOT USE. Your personal limit is 30 seconds."
                        : "WASD moves inside the room. Aim at your world die: RMB rolls, LMB nudges. LMB elsewhere uses the active item.");
                    break;
                case BoardFlowState.AscendingResolve:
                    SetText(_statusText, "Input closed. Camera rising while pending effects settle.");
                    break;
                case BoardFlowState.MinigameIntroReady:
                    SetText(_statusText, "Minigame implementation is pending. All four players press READY to skip.");
                    break;
                case BoardFlowState.SkippedResult:
                    SetText(_statusText, "RESULT PLACEHOLDER: no reward or currency change was applied.");
                    break;
            }
        }

        private void SetWaitingState()
        {
            SetActive(_selectionPanel, false);
            SetActive(_readyPanel, false);
            SetActive(_resultPanel, false);
            SetActive(_reconnectOverlay, false);
            SetActive(_reticle, false);
            SetText(_turnText, "TURN --");
            SetText(_phaseText, "WAITING FOR 4 PLAYERS");
            SetText(_phaseTimerText, "--:--");
            cameraDirector?.SetUiPointerVisible(true);
        }

        private T FindNamedComponent<T>(string objectName) where T : Component
        {
            var components = GetComponentsInChildren<T>(true);
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i].gameObject.name == objectName)
                {
                    return components[i];
                }
            }
            return null;
        }

        private GameObject FindNamed(string objectName)
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].gameObject.name == objectName)
                {
                    return transforms[i].gameObject;
                }
            }
            return null;
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        private static bool HasCountdown(BoardFlowState state)
        {
            return state != BoardFlowState.MinigameIntroReady;
        }

        private static string PhaseLabel(BoardFlowState state)
        {
            switch (state)
            {
                case BoardFlowState.TurnOverview: return "BOARD OVERVIEW";
                case BoardFlowState.Descending: return "DESCENDING";
                case BoardFlowState.Action: return "FIRST-PERSON ACTION";
                case BoardFlowState.AscendingResolve: return "RESOLVING / ASCENDING";
                case BoardFlowState.MinigameIntroReady: return "MINIGAME READY (DEV SKIP)";
                case BoardFlowState.SkippedResult: return "RESULT PLACEHOLDER";
                default: return state.ToString().ToUpperInvariant();
            }
        }

        private static string ChoiceLabel(NetworkPlayerAvatar avatar)
        {
            if (avatar == null)
            {
                return "--";
            }

            switch (avatar.LocalChoiceResolution)
            {
                case ItemChoiceResolution.ItemSelected: return "ITEM ACTIVE";
                case ItemChoiceResolution.DoNotUse: return "DO NOT USE";
                case ItemChoiceResolution.TimedOut: return "TIMEOUT / NO ITEM";
                case ItemChoiceResolution.Pending: return "PENDING";
                default: return "--";
            }
        }

        private static string FormatClock(double seconds)
        {
            var whole = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return (whole / 60).ToString("00") + ":" + (whole % 60).ToString("00");
        }

        private static Color PlayerColor(int slot)
        {
            switch (slot)
            {
                case 0: return new Color(1f, 0.42f, 0.42f);
                case 1: return new Color(0.42f, 0.7f, 1f);
                case 2: return new Color(0.42f, 1f, 0.58f);
                default: return new Color(1f, 0.82f, 0.35f);
            }
        }
    }
}

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
        private readonly Image[] _playerCards = new Image[MultiplayerConstants.MaxPlayers];
        private readonly Image[] _playerHealthFills = new Image[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerHealthTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerCurrencyTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerActionIcons = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerRankTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Button[] _shopOfferButtons = new Button[ItemShopRules.OfferCount];
        private readonly Text[] _shopOfferLabels = new Text[ItemShopRules.OfferCount];

        private GameObject _selectionPanel;
        private GameObject _readyPanel;
        private GameObject _resultPanel;
        private GameObject _reconnectOverlay;
        private GameObject _reticle;
        private GameObject _itemShopPanel;
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
        private Text _itemShopTitle;
        private Text _itemShopTooltip;
        private Text _itemShopStatus;
        private Button _noItemButton;
        private Button _readyButton;
        private Button _itemShopCloseButton;
        private NetworkPlayerAvatar _localAvatar;
        private KeyShopWorldMarker _keyShopMarker;
        private ItemShopWorldMarker _itemShopMarker;
        private BoardTopology _topology;
        private int _lastRevision = -1;
        private int _lastBoardEffectRevision = -1;
        private ItemChoiceResolution _lastChoiceResolution = ItemChoiceResolution.NotStarted;
        private bool _wired;
        private int _openItemShopIndex = -1;

        public static BoardFlowView Instance { get; private set; }
        public static bool IsItemShopOpen =>
            Instance != null && Instance._openItemShopIndex >= 0;

        public void Configure(GameplayCameraDirector director)
        {
            cameraDirector = director;
            BindUi();
        }

        private void Awake()
        {
            Instance = this;
            BindUi();
        }

        private void OnDestroy()
        {
            UnwireButtons();
            if (Instance == this)
            {
                Instance = null;
            }
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
            RefreshBoardEffects(match);
            RefreshKeyShop(match);
            RefreshItemShops(match);
            RefreshItemShopPanel(match);
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

        public void OpenItemShop(int shopIndex)
        {
            var match = NetworkMatchState.Instance;
            if (_localAvatar == null || match == null ||
                !match.CanLocalAvatarAccessItemShop(_localAvatar, shopIndex))
            {
                return;
            }

            _openItemShopIndex = shopIndex;
            SetActive(_itemShopPanel, true);
            HideShopItemTooltip();
            SetText(_itemShopStatus, _localAvatar.HasFreeItemSlot
                ? "Select an available item to buy it immediately."
                : "INVENTORY FULL - Browse only; purchases are disabled.");
        }

        public void CloseItemShop()
        {
            _openItemShopIndex = -1;
            SetActive(_itemShopPanel, false);
            HideShopItemTooltip();
        }

        public void PurchaseShopItem(int offerIndex)
        {
            var match = NetworkMatchState.Instance;
            if (_localAvatar == null || match == null || _openItemShopIndex < 0)
            {
                return;
            }

            var snapshot = match.GetItemShopSnapshot(_openItemShopIndex);
            var itemId = snapshot.GetOffer(offerIndex);
            if (snapshot.IsSold(offerIndex) || !PrototypeItemCatalog.IsValid(itemId))
            {
                SetText(_itemShopStatus, "That item is already sold.");
                return;
            }

            var definition = PrototypeItemCatalog.Get(itemId);
            if (!_localAvatar.HasFreeItemSlot)
            {
                SetText(_itemShopStatus, "INVENTORY FULL - No gold was spent.");
                return;
            }
            if (_localAvatar.Gold < definition.Price)
            {
                SetText(_itemShopStatus, "NOT ENOUGH GOLD - No gold was spent.");
                return;
            }

            _localAvatar.PurchaseItemFromShop(
                _openItemShopIndex,
                offerIndex,
                snapshot.Revision);
            SetText(_itemShopStatus, "Purchase requested. Server stock decides the winner.");
        }

        public void ShowShopItemTooltip(int offerIndex)
        {
            var match = NetworkMatchState.Instance;
            if (_itemShopTooltip == null || match == null || _openItemShopIndex < 0)
            {
                return;
            }

            var snapshot = match.GetItemShopSnapshot(_openItemShopIndex);
            var itemId = snapshot.GetOffer(offerIndex);
            if (!PrototypeItemCatalog.IsValid(itemId))
            {
                return;
            }

            var definition = PrototypeItemCatalog.Get(itemId);
            _itemShopTooltip.text = definition.DisplayName + "  /  " +
                                    definition.Price + " GOLD\n" +
                                    definition.Description;
            _itemShopTooltip.gameObject.SetActive(true);
        }

        public void HideShopItemTooltip()
        {
            if (_itemShopTooltip != null)
            {
                _itemShopTooltip.gameObject.SetActive(false);
            }
        }

        private void BindUi()
        {
            _selectionPanel = FindNamed("ItemSelectionPanel");
            _readyPanel = FindNamed("MinigameReadyPanel");
            _resultPanel = FindNamed("SkippedResultPanel");
            _reconnectOverlay = FindNamed("ReconnectOverlay");
            _reticle = FindNamed("BoardReticle");
            _itemShopPanel = FindNamed("ItemShopPanel");
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
            _itemShopTitle = FindNamedComponent<Text>("ItemShopTitle");
            _itemShopTooltip = FindNamedComponent<Text>("ItemShopTooltip");
            _itemShopStatus = FindNamedComponent<Text>("ItemShopStatus");
            _noItemButton = FindNamedComponent<Button>("NoItemButton");
            _readyButton = FindNamedComponent<Button>("ReadyButton");
            _itemShopCloseButton = FindNamedComponent<Button>("ItemShopCloseButton");

            for (var i = 0; i < GameplayInventory.Capacity; i++)
            {
                _slotBackgrounds[i] = FindNamedComponent<Image>("BoardInventorySlot" + i);
                _slotLabels[i] = FindNamedComponent<Text>("BoardInventorySlotLabel" + i);
                _choiceButtons[i] = FindNamedComponent<Button>("ItemChoiceButton" + i);
                _choiceLabels[i] = FindNamedComponent<Text>("ItemChoiceLabel" + i);
                var hover = FindNamedComponent<BoardItemChoiceButton>("ItemChoiceButton" + i);
                hover?.Configure(this, i);
            }

            for (var i = 0; i < ItemShopRules.OfferCount; i++)
            {
                _shopOfferButtons[i] = FindNamedComponent<Button>("ItemShopOffer" + i);
                _shopOfferLabels[i] = FindNamedComponent<Text>("ItemShopOfferLabel" + i);
                var hover = FindNamedComponent<BoardItemChoiceButton>("ItemShopOffer" + i);
                hover?.ConfigureShop(this, i);
            }

            for (var i = 0; i < MultiplayerConstants.MaxPlayers; i++)
            {
                _playerRows[i] = FindNamedComponent<Text>("PlayerState" + i);
                _playerCards[i] = FindNamedComponent<Image>("PlayerCard" + i);
                _playerHealthFills[i] = FindNamedComponent<Image>("PlayerHealthFill" + i);
                _playerHealthTexts[i] = FindNamedComponent<Text>("PlayerHealthText" + i);
                _playerCurrencyTexts[i] = FindNamedComponent<Text>("PlayerCurrency" + i);
                _playerActionIcons[i] = FindNamedComponent<Text>("PlayerActionIcon" + i);
                _playerRankTexts[i] = FindNamedComponent<Text>("PlayerRank" + i);
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
            for (var i = 0; i < _shopOfferButtons.Length; i++)
            {
                var offerIndex = i;
                _shopOfferButtons[i]?.onClick.AddListener(() => PurchaseShopItem(offerIndex));
            }
            _itemShopCloseButton?.onClick.AddListener(CloseItemShop);
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
            for (var i = 0; i < _shopOfferButtons.Length; i++)
            {
                _shopOfferButtons[i]?.onClick.RemoveAllListeners();
            }
            _itemShopCloseButton?.onClick.RemoveListener(CloseItemShop);
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
                                !match.IsGlobalSimulationPaused;
            SetActive(_selectionPanel, choicePending);
            SetActive(_readyPanel,
                match.FlowState == BoardFlowState.MinigameIntroReady && !match.IsGlobalSimulationPaused);
            SetActive(_resultPanel,
                match.FlowState == BoardFlowState.SkippedResult && !match.IsGlobalSimulationPaused);
            SetActive(_reconnectOverlay, match.IsReconnectPaused);
            SetActive(_reticle,
                ((match.FlowState == BoardFlowState.Action && !choicePending &&
                  !IsItemShopOpen) ||
                 (match.IsCombatPhase && _localAvatar != null &&
                  match.IsCombatParticipant(_localAvatar.AssignedSlot) &&
                  match.IsCombatAlive(_localAvatar.AssignedSlot))) &&
                !match.IsGlobalSimulationPaused);

            if (_reconnectText != null)
            {
                _reconnectText.text = "PLAYER DISCONNECTED\nMATCH PAUSED\n" +
                                      FormatClock(match.ReconnectRemaining) + " remaining";
            }
        }

        private void RefreshHeader(NetworkMatchState match)
        {
            SetText(_turnText, "TURN " + match.Turn);
            SetText(_phaseText, match.IsKeyShopRevealActive
                ? "KEY SHOP MOVING"
                : match.IsCombatPhase && match.IsCombatActive
                    ? "FIGHT " + match.CombatSequenceIndex +
                      "  /  " + (match.CombatSequenceIndex + match.CombatQueueCount)
                    : PhaseLabel(match.FlowState));

            var remaining = match.IsKeyShopRevealActive
                ? match.KeyShopRevealRemaining
                : match.FlowState == BoardFlowState.Action
                    ? match.ActionRemaining
                    : match.IsCombatPhase
                        ? match.CombatRemaining
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
                var shieldRemaining = Math.Max(
                    match.ShieldRemaining,
                    _localAvatar != null
                        ? _localAvatar.PersonalItemProtectionRemaining
                        : 0d);
                _shieldText.text = shieldRemaining > 0d
                    ? "SHIELD  " + shieldRemaining.ToString("0.0") + "s"
                    : "SHIELD  OFF";
                _shieldText.color = shieldRemaining > 0d
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

            if (match.IsCombatPhase)
            {
                var isFighting = match.IsCombatActive &&
                                 match.IsCombatParticipant(_localAvatar.AssignedSlot) &&
                                 match.IsCombatAlive(_localAvatar.AssignedSlot);
                SetText(_diceText, isFighting ? "LMB  PUNCH" : "FIGHT  SPECTATING");
                SetText(_movesText, isFighting
                    ? _localAvatar.IsQuietWalking
                        ? "QUIET WALK  6m"
                        : "WASD  MOVE / LCTRL QUIET 6m"
                    : "INPUT  LOCKED");
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
            var movementLabel = _localAvatar.HasRolled &&
                                _localAvatar.LocalRemainingMoves == 0
                ? "MOVES  0 / FREE IN ROOM"
                : "MOVES  " + _localAvatar.LocalRemainingMoves;
            SetText(_movesText, movementLabel +
                (_localAvatar.IsQuietWalking
                    ? "  /  QUIET WALK 6m"
                    : "  /  LCTRL QUIET 6m"));
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
            var rankingStats = new PlayerRankingStats[MultiplayerConstants.MaxPlayers];
            for (var slot = 0; slot < rankingStats.Length; slot++)
            {
                var rankedAvatar = match.GetAvatarForSlot(slot);
                rankingStats[slot] = rankedAvatar != null
                    ? new PlayerRankingStats(
                        rankedAvatar.KeyCount,
                        rankedAvatar.Gold,
                        rankedAvatar.MinigameWins)
                    : default;
            }
            var ranks = PlayerRankingRules.Calculate(rankingStats);

            for (var slot = 0; slot < _playerRows.Length; slot++)
            {
                var isPresent = match.IsPlayerPresent(slot);
                var avatar = match.GetAvatarForSlot(slot);
                var isLocal = _localAvatar != null && _localAvatar.AssignedSlot == slot;
                var connectionLabel = !isPresent
                    ? "RECONNECTING"
                    : isLocal ? "LOCAL" : "ONLINE";
                SetText(_playerRows[slot], "P" + (slot + 1) + "  " + connectionLabel);
                if (_playerRows[slot] != null)
                {
                    _playerRows[slot].color = isPresent
                    ? PlayerColor(slot)
                    : new Color(1f, 0.45f, 0.35f);
                }

                if (_playerCards[slot] != null)
                {
                    _playerCards[slot].color = isLocal
                        ? new Color(0.16f, 0.3f, 0.5f, 0.98f)
                        : new Color(0.055f, 0.085f, 0.13f, 0.94f);
                }

                var showCombatHealth = avatar != null && match.IsCombatPhase &&
                                       match.IsCombatParticipant(slot);
                var maxHealth = showCombatHealth
                    ? BoardCombatRules.TemporaryHealth
                    : avatar != null ? Mathf.Max(1, avatar.MaxHealth) : 1;
                var currentHealth = showCombatHealth
                    ? avatar.CombatHealth
                    : avatar != null ? avatar.CurrentHealth : 0;
                var healthRatio = avatar != null
                    ? Mathf.Clamp01(currentHealth / (float)maxHealth)
                    : 0f;
                if (_playerHealthFills[slot] != null)
                {
                    _playerHealthFills[slot].fillAmount = healthRatio;
                    _playerHealthFills[slot].color = healthRatio > 0.5f
                        ? new Color(0.2f, 0.82f, 0.38f, 1f)
                        : healthRatio > 0.25f
                            ? new Color(1f, 0.7f, 0.16f, 1f)
                            : new Color(0.95f, 0.2f, 0.2f, 1f);
                }
                SetText(_playerHealthTexts[slot], avatar != null
                    ? currentHealth + "/" + maxHealth
                    : "--/--");
                SetText(_playerCurrencyTexts[slot], avatar != null
                    ? "KEY  " + avatar.KeyCount + "    GOLD  " + avatar.Gold
                    : "KEY  --    GOLD  --");
                SetText(_playerRankTexts[slot], avatar != null
                    ? "RANK " + ranks[slot]
                    : "RANK --");

                var actionState = avatar != null && isPresent && !match.IsGlobalSimulationPaused
                    ? avatar.ActionState
                    : PlayerBoardActionState.Hidden;
                var isCombatOut = showCombatHealth && !match.IsCombatAlive(slot);
                SetText(_playerActionIcons[slot], isCombatOut
                    ? "OUT"
                    : ActionIconLabel(actionState));
                if (_playerActionIcons[slot] != null)
                {
                    _playerActionIcons[slot].color = isCombatOut
                        ? new Color(1f, 0.25f, 0.2f)
                        : ActionIconColor(actionState);
                }
            }
        }

        private void RefreshBoardEffects(NetworkMatchState match)
        {
            if (match.BoardEffectRevision <= 0 ||
                _lastBoardEffectRevision == match.BoardEffectRevision)
            {
                return;
            }

            if (_topology == null)
            {
                _topology = FindAnyObjectByType<BoardTopology>();
            }
            if (_topology == null)
            {
                return;
            }

            for (var i = 0; i < _topology.Tiles.Count; i++)
            {
                var tile = _topology.Tiles[i];
                if (tile == null)
                {
                    continue;
                }

                var effect = match.TryGetBoardLandingEffect(tile.Coordinate, out var assigned)
                    ? assigned
                    : BoardLandingEffectType.None;
                tile.ApplyLandingEffectPresentation(effect);
            }

            _lastBoardEffectRevision = match.BoardEffectRevision;
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

        private void RefreshItemShops(NetworkMatchState match)
        {
            if (_itemShopMarker == null)
            {
                _itemShopMarker = FindAnyObjectByType<ItemShopWorldMarker>();
            }
            if (_topology == null)
            {
                _topology = FindAnyObjectByType<BoardTopology>();
            }

            for (var shopIndex = 0; shopIndex < ItemShopRules.ShopCount; shopIndex++)
            {
                var snapshot = match.GetItemShopSnapshot(shopIndex);
                _itemShopMarker?.ApplyReplicatedState(
                    shopIndex,
                    snapshot.Active,
                    snapshot.Location,
                    snapshot.IsSoldOut,
                    _topology,
                    Mathf.Max(0, snapshot.Revision));
            }
        }

        private void RefreshItemShopPanel(NetworkMatchState match)
        {
            if (_openItemShopIndex < 0)
            {
                SetActive(_itemShopPanel, false);
                return;
            }

            if (_localAvatar == null ||
                !match.CanLocalAvatarAccessItemShop(_localAvatar, _openItemShopIndex))
            {
                CloseItemShop();
                return;
            }

            var snapshot = match.GetItemShopSnapshot(_openItemShopIndex);
            SetActive(_itemShopPanel, true);
            SetText(_itemShopTitle, "ITEM SHOP " + (_openItemShopIndex + 1));
            for (var offerIndex = 0; offerIndex < ItemShopRules.OfferCount; offerIndex++)
            {
                var itemId = snapshot.GetOffer(offerIndex);
                var sold = snapshot.IsSold(offerIndex);
                var valid = PrototypeItemCatalog.IsValid(itemId);
                var definition = valid ? PrototypeItemCatalog.Get(itemId) : default;
                SetText(_shopOfferLabels[offerIndex], sold
                    ? "SOLD\n" + (valid ? definition.DisplayName : "ITEM")
                    : valid
                        ? definition.DisplayName + "\n" + definition.Price + " GOLD"
                        : "UNAVAILABLE");
                if (_shopOfferButtons[offerIndex] != null)
                {
                    _shopOfferButtons[offerIndex].interactable =
                        !sold && valid && _localAvatar.HasFreeItemSlot &&
                        _localAvatar.Gold >= definition.Price;
                }
            }

            if (snapshot.IsSoldOut)
            {
                SetText(_itemShopStatus,
                    "SOLD OUT - This shop moves and restocks next overview.");
            }
            else if (!_localAvatar.HasFreeItemSlot)
            {
                SetText(_itemShopStatus, "INVENTORY FULL - Browse only; purchases are disabled.");
            }
        }

        private void RefreshCursor(NetworkMatchState match)
        {
            if (cameraDirector == null)
            {
                cameraDirector = FindAnyObjectByType<GameplayCameraDirector>();
            }

            var choicePending = _localAvatar != null &&
                                _localAvatar.LocalChoiceResolution == ItemChoiceResolution.Pending;
            var localCombatActive = match.IsCombatPhase && match.IsCombatActive &&
                                    _localAvatar != null &&
                                    match.IsCombatParticipant(_localAvatar.AssignedSlot) &&
                                    match.IsCombatAlive(_localAvatar.AssignedSlot);
            var pointerVisible = match.IsReconnectPaused ||
                                 match.IsKeyShopRevealActive ||
                                 (match.FlowState != BoardFlowState.Action &&
                                  !localCombatActive) ||
                                 choicePending || IsItemShopOpen;
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
            if (match.IsKeyShopRevealActive)
            {
                SetText(_statusText,
                    "Key purchased. Match paused while every player confirms the new shop location.");
                return;
            }
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
                case BoardFlowState.CombatResolve:
                    var isFighting = match.IsCombatActive && _localAvatar != null &&
                                     match.IsCombatParticipant(_localAvatar.AssignedSlot) &&
                                     match.IsCombatAlive(_localAvatar.AssignedSlot);
                    SetText(_statusText, isFighting
                        ? "FIGHT: WASD moves inside the room. LMB punches for 5 temporary HP damage."
                        : "SPECTATING: the camera follows the current fight room. Input is locked.");
                    break;
                case BoardFlowState.LandingEffectResolve:
                    SetText(_statusText, "All regular and chain fights resolved. Applying final landing effects in player order.");
                    break;
                case BoardFlowState.MinigameIntroReady:
                    SetText(_statusText, "Minigame implementation is pending. All four players press READY to skip.");
                    break;
                case BoardFlowState.SkippedResult:
                    SetText(_statusText, "RESULT PLACEHOLDER: no minigame reward was applied.");
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
            CloseItemShop();
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
                case BoardFlowState.CombatResolve: return "COMBAT QUEUE";
                case BoardFlowState.LandingEffectResolve: return "LANDING EFFECTS";
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

        private static string ActionIconLabel(PlayerBoardActionState state)
        {
            switch (state)
            {
                case PlayerBoardActionState.Dice: return "DICE";
                case PlayerBoardActionState.Moving: return "MOVE";
                case PlayerBoardActionState.Arrived: return "ARRIVED";
                case PlayerBoardActionState.Fighting: return "FIGHT";
                default: return string.Empty;
            }
        }

        private static Color ActionIconColor(PlayerBoardActionState state)
        {
            switch (state)
            {
                case PlayerBoardActionState.Dice: return new Color(0.4f, 0.75f, 1f);
                case PlayerBoardActionState.Moving: return new Color(0.35f, 1f, 0.55f);
                case PlayerBoardActionState.Arrived: return new Color(1f, 0.82f, 0.3f);
                case PlayerBoardActionState.Fighting: return new Color(1f, 0.3f, 0.25f);
                default: return Color.clear;
            }
        }
    }
}

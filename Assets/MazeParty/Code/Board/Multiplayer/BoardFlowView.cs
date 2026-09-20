using System;
using System.Text;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.BalloonBlow;
using MazeParty.Gameplay.Minigames.BouncingBalls;
using MazeParty.Gameplay.Minigames.GiftGrab;
using MazeParty.Gameplay.Minigames.Minefield;
using MazeParty.Gameplay.Minigames.Race;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using MazeParty.Gameplay.Minigames.StableFooting;
using MazeParty.Gameplay.Minigames.TagChase;
using MazeParty.Gameplay.Minigames.TerritoryPaint;
using MazeParty.Gameplay.Minigames.WrongWay;
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
        [SerializeField] private BoardCanvasBindings uiBindings;
        [SerializeField] private MinigameResultCanvasBindings resultUiBindings;

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
        private readonly Text[] _minigameReadyPlayerStates =
            new Text[MultiplayerConstants.MaxPlayers];
        private readonly Button[] _shopOfferButtons = new Button[ItemShopRules.OfferCount];
        private readonly Text[] _shopOfferLabels = new Text[ItemShopRules.OfferCount];

        private GameObject _selectionPanel;
        private GameObject _readyPanel;
        private GameObject _resultPanel;
        private GameObject _reconnectOverlay;
        private GameObject _reticle;
        private GameObject _itemShopPanel;
        private Canvas _boardCanvas;
        private GraphicRaycaster _boardRaycaster;
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
        private Text _minigameReadyTitle;
        private Text _minigameReadyNote;
        private Text _minigameReadyStatus;
        private Text _minigameRulePlaceholder;
        private Text _minefieldResultTitle;
        private Text _minefieldResultNote;
        private Text _minefieldResultSummary;
        private Text _readyButtonLabel;
        private Image _minigameRuleImage;
        private Button _noItemButton;
        private Button _readyButton;
        private Button _itemShopCloseButton;
        private NetworkPlayerAvatar _localAvatar;
        private KeyShopWorldMarker _keyShopMarker;
        private ItemShopWorldMarker _itemShopMarker;
        private BoardTopology _topology;
        private int _lastRevision = -1;
        private int _lastBoardEffectRevision = -1;
        private int _lastLandingEffectRevision = -1;
        private ItemChoiceResolution _lastChoiceResolution = ItemChoiceResolution.NotStarted;
        private NetworkMinefieldPhase _lastMinefieldPhase = NetworkMinefieldPhase.Inactive;
        private int _lastMinefieldRound = -1;
        private NetworkWrongWayPhase _lastWrongWayPhase =
            NetworkWrongWayPhase.Inactive;
        private int _lastWrongWayRound = -1;
        private NetworkRedLightGreenLightPhase
            _lastRedLightGreenLightPhase =
                NetworkRedLightGreenLightPhase.Inactive;
        private RedLightGreenLightSignalPhase
            _lastRedLightGreenLightSignal =
                RedLightGreenLightSignalPhase.Green;
        private int _lastRedLightGreenLightRound = -1;
        private NetworkStableFootingPhase _lastStableFootingPhase =
            NetworkStableFootingPhase.Inactive;
        private StableFootingCyclePhase _lastStableFootingCyclePhase =
            StableFootingCyclePhase.RoundComplete;
        private int _lastStableFootingRound = -1;
        private NetworkBalloonBlowPhase _lastBalloonBlowPhase =
            NetworkBalloonBlowPhase.Inactive;
        private int _lastBalloonBlowRound = -1;
        private NetworkGiftGrabPhase _lastGiftGrabPhase =
            NetworkGiftGrabPhase.Inactive;
        private int _lastGiftGrabRound = -1;
        private NetworkTagChasePhase _lastTagChasePhase =
            NetworkTagChasePhase.Inactive;
        private int _lastTagChaseRound = -1;
        private NetworkRacePhase _lastRacePhase =
            NetworkRacePhase.Inactive;
        private int _lastRaceRound = -1;
        private NetworkSequenceMemoryPhase _lastSequenceMemoryPhase =
            NetworkSequenceMemoryPhase.Inactive;
        private int _lastSequenceMemoryRound = -1;
        private NetworkBouncingBallsPhase _lastBouncingBallsPhase =
            NetworkBouncingBallsPhase.Inactive;
        private int _lastBouncingBallsRound = -1;
        private int _observedMinigameRevealRevision = -1;
        private float _minigameRevealObservedAt;
        private bool _lastMinigameRevealPending;
        private bool _wired;
        private int _openItemShopIndex = -1;
        private bool _topViewShopHighlightsVisible;

        public static BoardFlowView Instance { get; private set; }
        public static bool IsItemShopOpen =>
            Instance != null && Instance._openItemShopIndex >= 0;
        public bool BoardUiVisible => _boardCanvas == null || _boardCanvas.enabled;
        public BoardCanvasBindings UiBindings => uiBindings;
        public MinigameResultCanvasBindings ResultUiBindings =>
            resultUiBindings;
        public bool HasRequiredUiReferences =>
            uiBindings != null && uiBindings.HasRequiredReferences;

        public void SetTopViewShopHighlights(bool visible)
        {
            _topViewShopHighlightsVisible = visible;
            _keyShopMarker?.SetTopViewHighlight(visible);
            _itemShopMarker?.SetTopViewHighlight(visible);
        }

        public void Configure(GameplayCameraDirector director)
        {
            cameraDirector = director;
            BindUi();
        }

        public void ConfigureUiBindings(BoardCanvasBindings bindings)
        {
            uiBindings = bindings;
        }

        public void ConfigureResultUiBindings(
            MinigameResultCanvasBindings bindings)
        {
            resultUiBindings = bindings;
            BindResultUi();
        }

        public void SetBoardUiVisible(bool visible)
        {
            if (_boardCanvas == null && uiBindings != null)
            {
                _boardCanvas = uiBindings.RootCanvas;
            }
            if (_boardRaycaster == null && uiBindings != null)
            {
                _boardRaycaster = uiBindings.RootRaycaster;
            }

            if (_boardCanvas != null)
            {
                _boardCanvas.enabled = visible;
            }
            if (_boardRaycaster != null)
            {
                _boardRaycaster.enabled = visible;
            }
        }

        private void Awake()
        {
            Instance = this;
            BindUi();
            MinigameLocalPlayerHighlight.EnsureInstalled(gameObject);
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

            SetBoardUiVisible(match.FlowState != BoardFlowState.MinigamePlaying);
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

        private void BindResultUi()
        {
            _resultPanel = resultUiBindings != null
                ? resultUiBindings.ResultPanel
                : null;
            _minefieldResultTitle = resultUiBindings != null
                ? resultUiBindings.ResultTitle
                : null;
            _minefieldResultNote = resultUiBindings != null
                ? resultUiBindings.ResultNote
                : null;
            _minefieldResultSummary = resultUiBindings != null
                ? resultUiBindings.ResultSummary
                : null;
        }

        private void BindUi()
        {
            if (!HasRequiredUiReferences)
            {
                Debug.LogError(
                    "BoardFlowView requires the serialized BoardCanvasBindings " +
                    "contract from BoardCanvas.prefab.",
                    this);
                enabled = false;
                return;
            }

            _boardCanvas = uiBindings.RootCanvas;
            _boardRaycaster = uiBindings.RootRaycaster;
            _selectionPanel = uiBindings.ItemSelectionPanel;
            _readyPanel = uiBindings.MinigameReadyPanel;
            _reconnectOverlay = uiBindings.ReconnectOverlay;
            _reticle = uiBindings.Reticle;
            _itemShopPanel = uiBindings.ItemShopPanel;
            _turnText = uiBindings.TurnText;
            _phaseText = uiBindings.PhaseText;
            _phaseTimerText = uiBindings.PhaseTimerText;
            _choiceTimerText = uiBindings.ChoiceTimerText;
            _shieldText = uiBindings.ShieldText;
            _diceText = uiBindings.DiceText;
            _movesText = uiBindings.MovesText;
            _ammoText = uiBindings.AmmoText;
            _statusText = uiBindings.StatusText;
            _tooltipText = uiBindings.TooltipText;
            _reconnectText = uiBindings.ReconnectText;
            _itemShopTitle = uiBindings.ItemShopTitle;
            _itemShopTooltip = uiBindings.ItemShopTooltip;
            _itemShopStatus = uiBindings.ItemShopStatus;
            _minigameReadyTitle = uiBindings.MinigameReadyTitle;
            _minigameReadyNote = uiBindings.MinigameReadyNote;
            _minigameReadyStatus = uiBindings.MinigameReadyStatus;
            _minigameRulePlaceholder = uiBindings.MinigameRulePlaceholder;
            _minigameRuleImage = uiBindings.MinigameRuleImage;
            _noItemButton = uiBindings.NoItemButton;
            _readyButton = uiBindings.ReadyButton;
            _readyButtonLabel = uiBindings.ReadyButtonLabel;
            _itemShopCloseButton = uiBindings.ItemShopCloseButton;
            BindResultUi();

            CopyReferences(
                uiBindings.InventorySlotBackgrounds,
                _slotBackgrounds);
            CopyReferences(uiBindings.InventorySlotLabels, _slotLabels);
            CopyReferences(uiBindings.ItemChoiceButtons, _choiceButtons);
            CopyReferences(uiBindings.ItemChoiceLabels, _choiceLabels);
            CopyReferences(uiBindings.ShopOfferButtons, _shopOfferButtons);
            CopyReferences(uiBindings.ShopOfferLabels, _shopOfferLabels);
            CopyReferences(uiBindings.PlayerRows, _playerRows);
            CopyReferences(uiBindings.PlayerCards, _playerCards);
            CopyReferences(uiBindings.PlayerHealthFills, _playerHealthFills);
            CopyReferences(uiBindings.PlayerHealthTexts, _playerHealthTexts);
            CopyReferences(
                uiBindings.PlayerCurrencyTexts,
                _playerCurrencyTexts);
            CopyReferences(uiBindings.PlayerActionIcons, _playerActionIcons);
            CopyReferences(uiBindings.PlayerRankTexts, _playerRankTexts);
            CopyReferences(
                uiBindings.MinigameReadyPlayerStates,
                _minigameReadyPlayerStates);

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
            var showMinefieldReady =
                (match.FlowState == BoardFlowState.MinigameIntroReady ||
                 match.FlowState == BoardFlowState.MinigameLoading) &&
                !match.IsGlobalSimulationPaused;
            SetActive(_selectionPanel, choicePending);
            SetActive(_readyPanel, showMinefieldReady);
            SetActive(_resultPanel,
                match.FlowState == BoardFlowState.SkippedResult &&
                !match.IsGlobalSimulationPaused);
            SetActive(_reconnectOverlay, match.IsReconnectPaused);
            SetActive(_reticle,
                ((match.FlowState == BoardFlowState.Action && !choicePending &&
                  !IsItemShopOpen) ||
                 (match.IsCombatPhase && _localAvatar != null &&
                  match.IsCombatParticipant(_localAvatar.AssignedSlot) &&
                  match.IsCombatAlive(_localAvatar.AssignedSlot))) &&
                !match.IsGlobalSimulationPaused);

            RefreshMinefieldPanelContent(match);

            if (_reconnectText != null)
            {
                _reconnectText.text = "PLAYER DISCONNECTED\nMATCH PAUSED\n" +
                                      MinigameDisplayFormatter.FormatClock(
                                          match.ReconnectRemaining) + " remaining";
            }
        }

        private void RefreshHeader(NetworkMatchState match)
        {
            var minefield = NetworkMinefieldState.Instance;
            var wrongWay = NetworkWrongWayState.Instance;
            var redLightGreenLight =
                NetworkRedLightGreenLightState.Instance;
            var stableFooting = NetworkStableFootingState.Instance;
            var balloonBlow = NetworkBalloonBlowState.Instance;
            var giftGrab = NetworkGiftGrabState.Instance;
            var territoryPaint =
                NetworkTerritoryPaintState.Instance;
            var tagChase = NetworkTagChaseState.Instance;
            var race = NetworkRaceState.Instance;
            var sequenceMemory = NetworkSequenceMemoryState.Instance;
            var bouncingBalls = NetworkBouncingBallsState.Instance;
            var revealPending = IsMinigameRevealPending(match);
            SetText(_turnText, "TURN " + match.Turn);
            SetText(_phaseText, match.IsArrivalGraceActive
                ? "ARRIVAL COMPLETE"
                : match.IsKeyShopRevealActive
                ? "KEY SHOP MOVING"
                : match.IsCombatPhase && match.IsCombatActive
                    ? "FIGHT " + match.CombatSequenceIndex +
                      "  /  " + (match.CombatSequenceIndex + match.CombatQueueCount)
                    : match.FlowState == BoardFlowState.MinigamePlaying
                        ? match.CurrentMinigame == ScheduledMinigameId.WrongWay
                            ? WrongWayPhaseLabel(wrongWay)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.RedLightGreenLight
                                ? RedLightGreenLightPhaseLabel(
                                    redLightGreenLight)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.StableFooting
                                ? StableFootingPhaseLabel(stableFooting)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.BalloonBlow
                                ? BalloonBlowPhaseLabel(balloonBlow)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.GiftGrab
                                ? GiftGrabPhaseLabel(giftGrab)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.TerritoryPaint
                                ? TerritoryPaintPhaseLabel(
                                    territoryPaint)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.TagChase
                                ? TagChasePhaseLabel(tagChase)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.Race
                                ? RacePhaseLabel(race)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.SequenceMemory
                                ? SequenceMemoryPhaseLabel(sequenceMemory)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.BouncingBalls
                                ? BouncingBallsPhaseLabel(bouncingBalls)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.BombPassing
                                ? "BOMB PASSING"
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.SnowySpin
                                ? "SNOWY SPIN"
                            : MinefieldPhaseLabel(minefield)
                        : match.FlowState == BoardFlowState.MinigameIntroReady
                            ? revealPending
                                ? "???"
                                : MinigameName(match.CurrentMinigame) + " READY"
                        : match.FlowState == BoardFlowState.MinigameLoading
                            ? "LOADING " + MinigameName(match.CurrentMinigame)
                        : PhaseLabel(match.FlowState));

            string timerLabel;
            if (match.IsArrivalGraceActive)
            {
                timerLabel = "TOP VIEW  " +
                             match.ArrivalGraceRemaining.ToString("0.0") + "s";
            }
            else if (match.IsKeyShopRevealActive)
            {
                timerLabel = MinigameDisplayFormatter.FormatClock(
                    match.KeyShopRevealRemaining);
            }
            else if (match.FlowState == BoardFlowState.MinigameIntroReady ||
                     match.FlowState == BoardFlowState.MinigameLoading)
            {
                timerLabel =
                    match.CurrentMinigame == ScheduledMinigameId.Skip
                        ? "--:--"
                        : MinigameDisplayFormatter.FormatClock(
                            match.StateRemaining);
            }
            else if (match.FlowState == BoardFlowState.MinigamePlaying)
            {
                timerLabel =
                    match.CurrentMinigame == ScheduledMinigameId.WrongWay
                        ? wrongWay != null
                            ? MinigameDisplayFormatter.FormatClock(
                                wrongWay.Remaining)
                            : "--:--"
                        : match.CurrentMinigame ==
                          ScheduledMinigameId.RedLightGreenLight
                            ? redLightGreenLight != null
                                ? MinigameDisplayFormatter.FormatClock(
                                    redLightGreenLight.Remaining)
                                : "--:--"
                        : match.CurrentMinigame ==
                          ScheduledMinigameId.StableFooting
                            ? stableFooting != null
                                ? MinigameDisplayFormatter.FormatClock(
                                    stableFooting.Remaining)
                                : "--:--"
                        : match.CurrentMinigame ==
                          ScheduledMinigameId.BalloonBlow
                            ? balloonBlow != null
                                ? MinigameDisplayFormatter.FormatClock(
                                    balloonBlow.RemainingSeconds)
                                : "--:--"
                        : match.CurrentMinigame ==
                          ScheduledMinigameId.GiftGrab
                            ? giftGrab != null
                                ? MinigameDisplayFormatter.FormatClock(
                                    giftGrab.RemainingSeconds)
                                : "--:--"
                        : match.CurrentMinigame ==
                          ScheduledMinigameId.TerritoryPaint
                            ? territoryPaint != null
                                ? MinigameDisplayFormatter.FormatClock(
                                    territoryPaint.Remaining)
                                : "--:--"
                        : match.CurrentMinigame ==
                          ScheduledMinigameId.TagChase
                            ? tagChase != null
                                ? MinigameDisplayFormatter.FormatClock(
                                    tagChase.Remaining)
                                : "--:--"
                        : match.CurrentMinigame ==
                          ScheduledMinigameId.Race
                            ? race != null
                                ? MinigameDisplayFormatter.FormatClock(
                                    race.Remaining)
                                : "--:--"
                        : match.CurrentMinigame ==
                          ScheduledMinigameId.SequenceMemory
                            ? sequenceMemory != null
                                ? MinigameDisplayFormatter.FormatClock(
                                    sequenceMemory.Remaining)
                                : "--:--"
                        : match.CurrentMinigame ==
                          ScheduledMinigameId.BouncingBalls
                            ? bouncingBalls != null
                                ? MinigameDisplayFormatter.FormatClock(
                                    bouncingBalls.Remaining)
                                : "--:--"
                        : match.CurrentMinigame ==
                          ScheduledMinigameId.BombPassing
                            ? "--:--"
                        : match.CurrentMinigame ==
                          ScheduledMinigameId.SnowySpin
                            ? "--:--"
                            : minefield != null
                            ? MinigameDisplayFormatter.FormatClock(
                                minefield.Remaining)
                            : "--:--";
            }
            else if (match.FlowState == BoardFlowState.MatchComplete)
            {
                timerLabel = "--:--";
            }
            else
            {
                var remaining = match.FlowState == BoardFlowState.Action
                    ? match.ActionRemaining
                    : match.IsCombatPhase
                        ? match.CombatRemaining
                        : match.StateRemaining;
                timerLabel = MinigameDisplayFormatter.FormatClock(remaining);
            }
            SetText(_phaseTimerText, timerLabel);

            SetText(_choiceTimerText,
                match.FlowState == BoardFlowState.Action && _localAvatar != null &&
                _localAvatar.LocalChoiceResolution == ItemChoiceResolution.Pending
                    ? "CHOOSE  " +
                      MinigameDisplayFormatter.FormatClock(
                          match.ChoiceRemaining)
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
                    ? uiBindings.ShieldActiveColor
                    : uiBindings.ShieldInactiveColor;
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

            var visibleRoll = _localAvatar.LocalVisibleRoll;
            var diePhase = _localAvatar.LocalWorldDiePhase;
            var publicFace = _localAvatar.LocalWorldDiePublicFace;
            var isActionPhase = match.FlowState == BoardFlowState.Action;
            SetText(
                _diceText,
                WorldDieHudPresentationPolicy.ResolveDiceStatusLabel(
                    visibleRoll,
                    _localAvatar.HasRolled,
                    _localAvatar.HasResolvedItemChoice,
                    isActionPhase,
                    match.ActionRemaining > 0d,
                    diePhase,
                    publicFace));
            if (!isActionPhase)
            {
                SetText(_movesText, "MOVES  --");
                return;
            }

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
                        ? uiBindings.InventorySelectedColor
                        : occupied
                            ? uiBindings.InventoryOccupiedColor
                            : uiBindings.InventoryEmptyColor;
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
                        ? uiBindings.GetPlayerColor(slot)
                        : uiBindings.DisconnectedPlayerColor;
                }

                if (_playerCards[slot] != null)
                {
                    _playerCards[slot].color = isLocal
                        ? uiBindings.LocalPlayerCardColor
                        : uiBindings.RemotePlayerCardColor;
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
                    _playerHealthFills[slot].gameObject.SetActive(
                        !match.IsArenaCombatPhase);
                    _playerHealthFills[slot].fillAmount = healthRatio;
                    _playerHealthFills[slot].color = healthRatio > 0.5f
                        ? uiBindings.HealthyHealthColor
                        : healthRatio > 0.25f
                            ? uiBindings.WoundedHealthColor
                            : uiBindings.CriticalHealthColor;
                }
                SetText(_playerHealthTexts[slot], avatar != null
                    ? currentHealth + "/" + maxHealth
                    : "--/--");
                SetActive(
                    _playerHealthTexts[slot] != null
                        ? _playerHealthTexts[slot].gameObject
                        : null,
                    !match.IsArenaCombatPhase);
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
                        ? uiBindings.CombatOutColor
                        : uiBindings.GetActionIconColor(actionState);
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
                _keyShopMarker?.SetTopViewHighlight(
                    _topViewShopHighlightsVisible);
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
                _itemShopMarker?.SetTopViewHighlight(
                    _topViewShopHighlightsVisible);
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
            var minefield = NetworkMinefieldState.Instance;
            var minefieldPhase = minefield != null
                ? minefield.Phase
                : NetworkMinefieldPhase.Inactive;
            var minefieldRound = minefield != null ? minefield.RoundNumber : -1;
            var wrongWay = NetworkWrongWayState.Instance;
            var wrongWayPhase = wrongWay != null
                ? wrongWay.Phase
                : NetworkWrongWayPhase.Inactive;
            var wrongWayRound = wrongWay != null
                ? wrongWay.RoundNumber
                : -1;
            var redLightGreenLight =
                NetworkRedLightGreenLightState.Instance;
            var redLightGreenLightPhase = redLightGreenLight != null
                ? redLightGreenLight.Phase
                : NetworkRedLightGreenLightPhase.Inactive;
            var redLightGreenLightSignal = redLightGreenLight != null
                ? redLightGreenLight.SignalPhase
                : RedLightGreenLightSignalPhase.Green;
            var redLightGreenLightRound = redLightGreenLight != null
                ? redLightGreenLight.RoundNumber
                : -1;
            var stableFooting = NetworkStableFootingState.Instance;
            var stableFootingPhase = stableFooting != null
                ? stableFooting.Phase
                : NetworkStableFootingPhase.Inactive;
            var stableFootingCyclePhase = stableFooting != null
                ? stableFooting.CyclePhase
                : StableFootingCyclePhase.RoundComplete;
            var stableFootingRound = stableFooting != null
                ? stableFooting.RoundNumber
                : -1;
            var balloonBlow = NetworkBalloonBlowState.Instance;
            var balloonBlowPhase = balloonBlow != null
                ? balloonBlow.Phase
                : NetworkBalloonBlowPhase.Inactive;
            var balloonBlowRound = balloonBlow != null
                ? balloonBlow.RoundNumber
                : -1;
            var giftGrab = NetworkGiftGrabState.Instance;
            var giftGrabPhase = giftGrab != null
                ? giftGrab.Phase
                : NetworkGiftGrabPhase.Inactive;
            var giftGrabRound = giftGrab != null
                ? giftGrab.RoundNumber
                : -1;
            var tagChase = NetworkTagChaseState.Instance;
            var tagChasePhase = tagChase != null
                ? tagChase.Phase
                : NetworkTagChasePhase.Inactive;
            var tagChaseRound = tagChase != null
                ? tagChase.RoundNumber
                : -1;
            var race = NetworkRaceState.Instance;
            var racePhase = race != null
                ? race.Phase
                : NetworkRacePhase.Inactive;
            var raceRound = race != null
                ? race.RoundNumber
                : -1;
            var sequenceMemory = NetworkSequenceMemoryState.Instance;
            var sequenceMemoryPhase = sequenceMemory != null
                ? sequenceMemory.Phase
                : NetworkSequenceMemoryPhase.Inactive;
            var sequenceMemoryRound = sequenceMemory != null
                ? sequenceMemory.RoundNumber
                : -1;
            var bouncingBalls = NetworkBouncingBallsState.Instance;
            var bouncingBallsPhase = bouncingBalls != null
                ? bouncingBalls.Phase
                : NetworkBouncingBallsPhase.Inactive;
            var bouncingBallsRound = bouncingBalls != null
                ? bouncingBalls.RoundNumber
                : -1;
            var revealPending = IsMinigameRevealPending(match);
            var landingEffectRevision = match.LastLandingEffectRevision;
            if (_lastRevision == match.StateRevision &&
                _lastChoiceResolution == choice &&
                _lastMinefieldPhase == minefieldPhase &&
                _lastMinefieldRound == minefieldRound &&
                _lastWrongWayPhase == wrongWayPhase &&
                _lastWrongWayRound == wrongWayRound &&
                _lastRedLightGreenLightPhase ==
                redLightGreenLightPhase &&
                _lastRedLightGreenLightSignal ==
                redLightGreenLightSignal &&
                _lastRedLightGreenLightRound ==
                redLightGreenLightRound &&
                _lastStableFootingPhase == stableFootingPhase &&
                _lastStableFootingCyclePhase == stableFootingCyclePhase &&
                _lastStableFootingRound == stableFootingRound &&
                _lastBalloonBlowPhase == balloonBlowPhase &&
                _lastBalloonBlowRound == balloonBlowRound &&
                _lastGiftGrabPhase == giftGrabPhase &&
                _lastGiftGrabRound == giftGrabRound &&
                _lastTagChasePhase == tagChasePhase &&
                _lastTagChaseRound == tagChaseRound &&
                _lastRacePhase == racePhase &&
                _lastRaceRound == raceRound &&
                _lastSequenceMemoryPhase == sequenceMemoryPhase &&
                _lastSequenceMemoryRound == sequenceMemoryRound &&
                _lastBouncingBallsPhase == bouncingBallsPhase &&
                _lastBouncingBallsRound == bouncingBallsRound &&
                _lastMinigameRevealPending == revealPending &&
                _lastLandingEffectRevision == landingEffectRevision)
            {
                return;
            }

            _lastRevision = match.StateRevision;
            _lastChoiceResolution = choice;
            _lastMinefieldPhase = minefieldPhase;
            _lastMinefieldRound = minefieldRound;
            _lastWrongWayPhase = wrongWayPhase;
            _lastWrongWayRound = wrongWayRound;
            _lastRedLightGreenLightPhase = redLightGreenLightPhase;
            _lastRedLightGreenLightSignal = redLightGreenLightSignal;
            _lastRedLightGreenLightRound = redLightGreenLightRound;
            _lastStableFootingPhase = stableFootingPhase;
            _lastStableFootingCyclePhase = stableFootingCyclePhase;
            _lastStableFootingRound = stableFootingRound;
            _lastBalloonBlowPhase = balloonBlowPhase;
            _lastBalloonBlowRound = balloonBlowRound;
            _lastGiftGrabPhase = giftGrabPhase;
            _lastGiftGrabRound = giftGrabRound;
            _lastTagChasePhase = tagChasePhase;
            _lastTagChaseRound = tagChaseRound;
            _lastRacePhase = racePhase;
            _lastRaceRound = raceRound;
            _lastSequenceMemoryPhase = sequenceMemoryPhase;
            _lastSequenceMemoryRound = sequenceMemoryRound;
            _lastBouncingBallsPhase = bouncingBallsPhase;
            _lastBouncingBallsRound = bouncingBallsRound;
            _lastMinigameRevealPending = revealPending;
            _lastLandingEffectRevision = landingEffectRevision;
            if (match.IsKeyShopRevealActive)
            {
                SetText(_statusText,
                    "Key purchased. The new shop location is shown; play resumes when the countdown ends.");
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
                    SetText(_statusText, match.IsArrivalGraceActive
                        ? "All players arrived. Top view opens when the countdown ends."
                        : choice == ItemChoiceResolution.Pending
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
                    SetText(_statusText,
                        string.IsNullOrEmpty(match.LastLandingEffectMessage)
                            ? "Applying final landing effects in player order."
                            : match.LastLandingEffectMessage);
                    break;
                case BoardFlowState.MinigameIntroReady:
                    SetText(
                        _statusText,
                        revealPending
                            ? "Opening the top block in the minigame tower..."
                            : match.CurrentMinigame == ScheduledMinigameId.Skip
                            ? "No minigame is available for this queue slot. It will advance automatically."
                            : MinigameName(match.CurrentMinigame) +
                              ": review the rules. Press READY to start early; the minigame starts automatically when the countdown ends.");
                    break;
                case BoardFlowState.MinigameLoading:
                    SetText(_statusText,
                        "Loading the synchronized " +
                        MinigameName(match.CurrentMinigame) +
                        " scene. Board movement is locked; the match ends if loading reaches zero.");
                    break;
                case BoardFlowState.MinigamePlaying:
                    SetText(
                        _statusText,
                        match.CurrentMinigame == ScheduledMinigameId.WrongWay
                            ? WrongWayStatus(wrongWay)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.RedLightGreenLight
                                ? RedLightGreenLightStatus(
                                    redLightGreenLight)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.StableFooting
                                ? StableFootingStatus(stableFooting)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.BalloonBlow
                                ? BalloonBlowStatus(balloonBlow)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.GiftGrab
                                ? GiftGrabStatus(giftGrab)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.TerritoryPaint
                                ? TerritoryPaintStatus(
                                    NetworkTerritoryPaintState.Instance)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.TagChase
                                ? TagChaseStatus(tagChase)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.Race
                                ? RaceStatus(race)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.SequenceMemory
                                ? SequenceMemoryStatus(sequenceMemory)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.BouncingBalls
                                ? BouncingBallsStatus(bouncingBalls)
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.BombPassing
                                ? "Keep the bomb away. Its light flashes " +
                                  "faster as detonation approaches."
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.SnowySpin
                                ? "Roll and push opponents off the ice."
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.ArenaCombat
                                ? "Fight in first person. Eliminated players spectate."
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.CliffBarrage
                                ? "Dodge shells and lasers. Push rivals off the cliff."
                            : MinefieldStatus(minefield));
                    break;
                case BoardFlowState.SkippedResult:
                    SetText(
                        _statusText,
                        match.CurrentMinigame == ScheduledMinigameId.Skip
                            ? "SKIPPED: moving to the next block in the minigame tower."
                            : MinigameName(match.CurrentMinigame) +
                              " COMPLETE: final standings and " +
                              MinigameRewardRules.FinalPlacementGoldSchedule +
                              " gold rewards are shown.");
                    break;
                case BoardFlowState.MatchComplete:
                    SetText(
                        _statusText,
                        "MATCH COMPLETE: all 15 turns have finished.");
                    break;
            }
        }

        private void RefreshMinefieldPanelContent(NetworkMatchState match)
        {
            if (_observedMinigameRevealRevision !=
                match.MinigameRevealRevision)
            {
                _observedMinigameRevealRevision =
                    match.MinigameRevealRevision;
                _minigameRevealObservedAt = Time.unscaledTime;
            }

            var revealPending =
                match.FlowState == BoardFlowState.MinigameIntroReady &&
                Time.unscaledTime - _minigameRevealObservedAt <
                MinigameScheduleTowerView.RevealDelaySeconds;
            var readyCount = CountReadyPlayers(match);
            var selected = match.CurrentMinigame;
            var isMinefield = selected == ScheduledMinigameId.Minefield;
            var isWrongWay = selected == ScheduledMinigameId.WrongWay;
            var isRedLightGreenLight =
                selected == ScheduledMinigameId.RedLightGreenLight;
            var isStableFooting =
                selected == ScheduledMinigameId.StableFooting;
            var isBalloonBlow =
                selected == ScheduledMinigameId.BalloonBlow;
            var isGiftGrab = selected == ScheduledMinigameId.GiftGrab;
            var isTerritoryPaint =
                selected == ScheduledMinigameId.TerritoryPaint;
            var isTagChase = selected == ScheduledMinigameId.TagChase;
            var isRace = selected == ScheduledMinigameId.Race;
            var isSequenceMemory =
                selected == ScheduledMinigameId.SequenceMemory;
            var isBouncingBalls =
                selected == ScheduledMinigameId.BouncingBalls;
            var isBombPassing =
                selected == ScheduledMinigameId.BombPassing;
            var isSnowySpin =
                selected == ScheduledMinigameId.SnowySpin;
            var isArenaCombat =
                selected == ScheduledMinigameId.ArenaCombat;
            var isCliffBarrage =
                selected == ScheduledMinigameId.CliffBarrage;
            var isSkip = selected == ScheduledMinigameId.Skip;
            var ruleCard = uiBindings.GetMinigameRuleCard(selected);
            var hasRuleImage = _minigameRuleImage != null &&
                               ruleCard != null &&
                               !isSkip;
            SetText(
                _minigameReadyTitle,
                revealPending
                    ? "???"
                    : isWrongWay
                    ? "WRONG WAY / STAIR RACE"
                    : isRedLightGreenLight
                        ? "RED LIGHT / GREEN LIGHT"
                    : isStableFooting
                        ? "STABLE FOOTING"
                    : isBalloonBlow
                        ? "BALLOON BLOW"
                    : isGiftGrab
                        ? "GIFT GRAB"
                    : isTerritoryPaint
                        ? "TERRITORY PAINT"
                    : isTagChase
                        ? "TAG CHASE"
                    : isRace
                        ? "RACE"
                    : isSequenceMemory
                        ? "SEQUENCE MEMORY"
                    : isBouncingBalls
                        ? "BOUNCING BALLS"
                    : isBombPassing
                        ? "BOMB PASSING"
                    : isSnowySpin
                        ? "SNOWY SPIN"
                    : isArenaCombat
                        ? "ARENA COMBAT"
                    : isCliffBarrage
                        ? "CLIFF BARRAGE"
                    : isSkip
                        ? "NO MINIGAME / SKIP"
                        : "MINEFIELD / TOP-DOWN");
            SetText(
                _minigameReadyNote,
                revealPending
                    ? "Opening the top block in the minigame tower..."
                    : isWrongWay
                    ? "Press the shown WASD direction to climb. A wrong key knocks " +
                      "you down for 0.5 seconds. First to step 50 ends the round. " +
                      "Two rounds, 60 seconds each.\nREADY " +
                      readyCount + " / 4"
                    : isRedLightGreenLight
                        ? "Move with WASD during GREEN and freeze when RED begins. " +
                          "The first violation injures you and slows you to walking " +
                          "speed; the second eliminates you. First finisher ends the " +
                          "round. Three rounds, 60 seconds each.\n" +
                          "READY " + readyCount + " / 4"
                    : isStableFooting
                        ? "Move with WASD and press LMB to push the nearest player " +
                          "in front of you. Reach the announced X, circle or square " +
                          "before unsafe platforms drop. Two dropped platforms are " +
                          "removed each cycle. Last survivor wins each of three " +
                          "60-second rounds.\nREADY " +
                           readyCount + " / 4"
                    : isBalloonBlow
                        ? "Hold LMB to inflate at 10% per second. Release before " +
                          "two seconds for a 1-second cooldown; reaching two " +
                          "seconds forces a stop and a 1.5-second cooldown, then " +
                          "requires a fresh click. Progress decays 3% per second " +
                          "while not inflating. First to pop ranks first. Three " +
                          "30-second rounds.\nREADY " +
                          readyCount + " / 4"
                    : isGiftGrab
                        ? "Ten gifts start; three more drop at 15, 30 and 45 " +
                          "seconds. Move with WASD. Touch a gift to carry one, " +
                          "then return it to your base or press LMB to throw it. " +
                          "Without a gift, LMB pushes and makes opponents drop " +
                          "theirs. Steal stored gifts from rival bases. Two " +
                          "60-second rounds; most stored gifts wins.\n" +
                          "READY " +
                          readyCount + " / 4"
                    : isTerritoryPaint
                        ? "Move with WASD. Your circular trail paints the " +
                          "arena and can overwrite rival colors. The full " +
                          "arena is worth 1000 points. One 60-second round; " +
                          "highest current area wins.\nREADY " +
                          readyCount + " / 4"
                    : isTagChase
                        ? "Each round assigns one player as the tagger. Runners " +
                          "move with WASD using a shared camera. The tagger moves " +
                          "with WASD in first person and presses LMB to catch. " +
                          "Every player tags once across four 60-second rounds.\n" +
                          "READY " + readyCount + " / 4"
                    : isRace
                        ? "Alternate A and D to advance. Pressing the same key " +
                          "twice does not count. The first player to reach 500 " +
                          "steps ends the round. Three rounds, 60 seconds each.\n" +
                          "READY " + readyCount + " / 4"
                    : isSequenceMemory
                        ? "Watch and listen to the shared A/S/D sequence, then " +
                          "repeat it after it is hidden. A is high, S is middle " +
                          "and D is low. A wrong key locks the current problem. " +
                          "Your first mistake removes your torso; your second " +
                          "eliminates you. Ten problems, one final placement, " +
                          "no per-problem score.\nREADY " +
                          readyCount + " / 4"
                    : isBouncingBalls
                        ? "Move your goal shield with A and D. Three neutral balls " +
                          "launch from the center. Touching one claims your color; " +
                          "when it passes a shield into any goal, its color owner " +
                          "scores. A scored ball relaunches from the conceding " +
                          "player's shield in their color. Two 60-second rounds; " +
                          "highest combined score wins.\nREADY " +
                          readyCount + " / 4"
                    : isBombPassing
                        ? "Move with WASD. Touch the center bomb to pick it up. " +
                          "Click a nearby player in front of you to pass it; " +
                          "the receiver is stunned for 0.5 seconds. Empty-hand " +
                          "click stuns a nearby player for 0.5 seconds. The " +
                          "fuse starts at spawn and lasts 20–25 seconds. " +
                          "At half time, an unheld bomb chases the nearest " +
                          "survivor. Only the carrier is eliminated when it " +
                          "explodes. Last survivor wins.\nREADY " +
                          readyCount + " / 4"
                    : isSnowySpin
                        ? "Roll your colored ball with WASD. Holding a direction " +
                          "accelerates; colliding with other balls pushes them " +
                          "toward the edge. A fall eliminates you for that round. " +
                          "Three rounds, up to 60 seconds each. If time runs " +
                          "out, surviving balls nearer the center rank higher. " +
                          "Round placement points are combined; final placement " +
                          "awards gold once.\nREADY " +
                          readyCount + " / 4"
                    : isArenaCombat
                        ? "Fight with WASD movement, mouse look and LMB punches. " +
                          "Health is hidden. Defeated players spectate. " +
                          "Last survivor wins, or remaining health decides " +
                          "survivors after 60 seconds.\nREADY " +
                          readyCount + " / 4"
                    : isCliffBarrage
                        ? "Move with WASD and click to push the nearest rival " +
                          "in your last movement direction. Falling eliminates " +
                          "you immediately. A shell or laser hit first removes " +
                          "your torso; a second hit eliminates you, with one " +
                          "second of safety after a hit. Survive three 60-second " +
                          "rounds.\nREADY " +
                          readyCount + " / 4"
                    : isSkip
                        ? "This queue slot has no available minigame. " +
                          "The next turn starts automatically."
                        : (hasRuleImage ? string.Empty : "RULE IMAGE PLACEHOLDER\n") +
                          "Stop and RMB to scan. First mine cripples; second eliminates. " +
                          "Reach the finish before the crusher.\n" +
                          "READY " + readyCount + " / 4");
            SetText(
                _minigameReadyStatus,
                revealPending
                    ? "REVEALING..."
                    : isSkip
                        ? "AUTO SKIP"
                        : match.FlowState == BoardFlowState.MinigameLoading
                            ? "LOADING  " +
                              MinigameDisplayFormatter.FormatClock(match.StateRemaining)
                            : "READY " + readyCount + " / 4  AUTO START " +
                              MinigameDisplayFormatter.FormatClock(match.StateRemaining));

            for (var slot = 0; slot < _minigameReadyPlayerStates.Length; slot++)
            {
                var playerState = _minigameReadyPlayerStates[slot];
                if (playerState == null)
                {
                    continue;
                }

                playerState.gameObject.SetActive(!isSkip);
                if (isSkip)
                {
                    continue;
                }

                var isReady = match.IsMinigameReady(slot);
                SetText(
                    playerState,
                    "P" + (slot + 1) + (isReady ? "  READY" : "  WAITING"));
                playerState.color = isReady
                    ? uiBindings.ReadyPlayerCompleteColor
                    : uiBindings.ReadyPlayerWaitingColor;
            }

            var localReady = _localAvatar != null &&
                             MinefieldRules.IsValidPlayerSlot(_localAvatar.AssignedSlot) &&
                             match.IsMinigameReady(_localAvatar.AssignedSlot);
            if (_readyButton != null)
            {
                _readyButton.interactable =
                    match.FlowState == BoardFlowState.MinigameIntroReady &&
                    !match.IsGlobalSimulationPaused &&
                    _localAvatar != null &&
                    !isSkip &&
                    !revealPending &&
                    !localReady;
            }
            SetText(
                _readyButtonLabel,
                revealPending
                    ? "WAIT..."
                    : isSkip
                        ? "SKIPPING..."
                        : "READY");

            SetText(
                _minefieldResultTitle,
                isWrongWay
                    ? "WRONG WAY RESULTS"
                    : isRedLightGreenLight
                        ? "RED LIGHT / GREEN LIGHT RESULTS"
                    : isStableFooting
                        ? "STABLE FOOTING RESULTS"
                    : isBalloonBlow
                        ? "BALLOON BLOW RESULTS"
                    : isGiftGrab
                        ? "GIFT GRAB RESULTS"
                    : isTerritoryPaint
                        ? "TERRITORY PAINT RESULTS"
                    : isTagChase
                        ? "TAG CHASE RESULTS"
                    : isRace
                        ? "RACE RESULTS"
                    : isSequenceMemory
                        ? "SEQUENCE MEMORY RESULTS"
                    : isBouncingBalls
                        ? "BOUNCING BALLS RESULTS"
                    : isBombPassing
                        ? "BOMB PASSING RESULTS"
                    : isSnowySpin
                        ? "SNOWY SPIN RESULTS"
                    : isArenaCombat
                        ? "ARENA COMBAT RESULTS"
                    : isCliffBarrage
                        ? "CLIFF BARRAGE RESULTS"
                    : isSkip
                        ? "TURN SKIPPED"
                        : "MINEFIELD RESULTS");
            var resultSummary = isWrongWay
                ? BuildWrongWayResultSummary(NetworkWrongWayState.Instance)
                : isRedLightGreenLight
                    ? BuildRedLightGreenLightResultSummary(
                        NetworkRedLightGreenLightState.Instance)
                : isStableFooting
                    ? BuildStableFootingResultSummary(
                        NetworkStableFootingState.Instance)
                : isBalloonBlow
                    ? BuildBalloonBlowResultSummary(
                        NetworkBalloonBlowState.Instance)
                : isGiftGrab
                    ? BuildGiftGrabResultSummary(
                        NetworkGiftGrabState.Instance)
                : isTerritoryPaint
                    ? BuildTerritoryPaintResultSummary(
                        NetworkTerritoryPaintState.Instance)
                : isTagChase
                    ? BuildTagChaseResultSummary(
                        NetworkTagChaseState.Instance)
                : isRace
                    ? BuildRaceResultSummary(NetworkRaceState.Instance)
                : isSequenceMemory
                    ? BuildSequenceMemoryResultSummary(
                        NetworkSequenceMemoryState.Instance)
                : isBouncingBalls
                    ? BuildBouncingBallsResultSummary(
                        NetworkBouncingBallsState.Instance)
                : isBombPassing
                    ? BuildBombPassingResultSummary(
                        NetworkBombPassingState.Instance)
                : isSnowySpin
                    ? BuildSnowySpinResultSummary(
                        NetworkSnowySpinState.Instance)
                : isArenaCombat
                    ? BuildArenaCombatResultSummary(
                        NetworkArenaCombatState.Instance)
                : isCliffBarrage
                    ? BuildCliffBarrageResultSummary(
                        NetworkCliffBarrageState.Instance)
                : isSkip
                    ? "No minigame was scheduled for this turn."
                    : BuildMinefieldResultSummary(NetworkMinefieldState.Instance);
            if (_minefieldResultSummary != null)
            {
                SetText(
                    _minefieldResultNote,
                    isSkip
                        ? "No rewards are awarded for an empty queue slot."
                        : "Final placement awards " +
                          MinigameRewardRules.FinalPlacementGoldSchedule +
                          " gold.");
                SetText(_minefieldResultSummary, resultSummary);
            }
            else
            {
                SetText(_minefieldResultNote, resultSummary);
            }

            if (_minigameRuleImage != null)
            {
                // Clear the previous card during the tower reveal so a new
                // minigame cannot be inferred from a retained sprite.
                _minigameRuleImage.sprite = revealPending ? null : ruleCard;
                _minigameRuleImage.color = hasRuleImage
                    ? uiBindings.RuleImageContentColor
                    : uiBindings.RuleImagePlaceholderColor;
                _minigameRuleImage.gameObject.SetActive(
                    hasRuleImage && !revealPending);
            }
            SetText(
                _minigameRulePlaceholder,
                revealPending
                    ? "RULES REVEALING..."
                    : isWrongWay
                    ? "W  A  S  D\n50 STEPS"
                    : isRedLightGreenLight
                        ? "GREEN: MOVE\nRED: FREEZE"
                    : isStableFooting
                        ? "WASD: MOVE\nLMB: PUSH\nX  O  □"
                    : isBalloonBlow
                        ? "HOLD LMB\nPOP FIRST"
                    : isGiftGrab
                        ? "WASD: MOVE\nLMB: THROW / PUSH\nSTEAL GIFTS"
                    : isTerritoryPaint
                        ? "WASD: MOVE\nPAINT THE ARENA"
                    : isTagChase
                        ? "RUNNERS: WASD\nTAGGER: WASD + LMB"
                    : isRace
                        ? "ALTERNATE A / D\n500 STEPS"
                    : isSequenceMemory
                        ? "A: HIGH\nS: MIDDLE\nD: LOW"
                    : isBouncingBalls
                        ? "A / D: MOVE SHIELD\nCLAIM BALLS · SCORE GOALS"
                    : isBombPassing
                        ? "WASD: MOVE\nLMB: PASS / STUN\nSURVIVE THE BOMB"
                    : isSnowySpin
                        ? "WASD: ROLL\nBUILD SPEED · PUSH BALLS OFF"
                    : isArenaCombat
                        ? "WASD: MOVE\nMOUSE: LOOK\nLMB: PUNCH"
                    : isCliffBarrage
                        ? "WASD: DODGE\nLMB: PUSH\nAVOID SHELLS / LASERS"
                        : "RULE IMAGE");
            SetActive(
                _minigameRulePlaceholder != null
                    ? _minigameRulePlaceholder.gameObject
                    : null,
                revealPending || (!isSkip && !hasRuleImage));
        }

        private static int CountReadyPlayers(NetworkMatchState match)
        {
            var count = 0;
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if (match.IsMinigameReady(slot))
                {
                    count++;
                }
            }

            return count;
        }

        private static string BuildMinefieldResultSummary(
            NetworkMinefieldState minefield)
        {
            if (minefield == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1; rank <= MinefieldRules.PlayerCount; rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0; slot < MinefieldRules.PlayerCount; slot++)
                {
                    if (minefield.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null ? match.GetAvatarForSlot(rankedSlot) : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(avatar != null ? avatar.DisplayName : "P" + (rankedSlot + 1))
                    .Append("  SCORE ")
                    .Append(minefield.GetScore(rankedSlot))
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildWrongWayResultSummary(
            NetworkWrongWayState wrongWay)
        {
            if (wrongWay == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1; rank <= WrongWayRules.PlayerCount; rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0; slot < WrongWayRules.PlayerCount; slot++)
                {
                    if (wrongWay.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar =
                    match != null ? match.GetAvatarForSlot(rankedSlot) : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(
                        avatar != null
                            ? avatar.DisplayName
                            : "P" + (rankedSlot + 1))
                    .Append("  SCORE ")
                    .Append(wrongWay.GetScore(rankedSlot))
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildRedLightGreenLightResultSummary(
            NetworkRedLightGreenLightState redLightGreenLight)
        {
            if (redLightGreenLight == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1;
                 rank <= RedLightGreenLightRules.PlayerCount;
                 rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0;
                     slot < RedLightGreenLightRules.PlayerCount;
                     slot++)
                {
                    if (redLightGreenLight.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar =
                    match != null ? match.GetAvatarForSlot(rankedSlot) : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(
                        avatar != null
                            ? avatar.DisplayName
                            : "P" + (rankedSlot + 1))
                    .Append("  SCORE ")
                    .Append(redLightGreenLight.GetScore(rankedSlot))
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildStableFootingResultSummary(
            NetworkStableFootingState stableFooting)
        {
            if (stableFooting == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1;
                 rank <= StableFootingRules.PlayerCount;
                 rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0;
                     slot < StableFootingRules.PlayerCount;
                     slot++)
                {
                    if (stableFooting.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar =
                    match != null ? match.GetAvatarForSlot(rankedSlot) : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(
                        avatar != null
                            ? avatar.DisplayName
                            : "P" + (rankedSlot + 1))
                    .Append("  SCORE ")
                    .Append(stableFooting.GetScore(rankedSlot))
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildBalloonBlowResultSummary(
            NetworkBalloonBlowState balloonBlow)
        {
            if (balloonBlow == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1;
                 rank <= BalloonBlowRules.PlayerCount;
                 rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0;
                     slot < BalloonBlowRules.PlayerCount;
                     slot++)
                {
                    if (balloonBlow.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar =
                    match != null ? match.GetAvatarForSlot(rankedSlot) : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(
                        avatar != null
                            ? avatar.DisplayName
                            : "P" + (rankedSlot + 1))
                    .Append("  SCORE ")
                    .Append(balloonBlow.GetScore(rankedSlot))
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildGiftGrabResultSummary(
            NetworkGiftGrabState giftGrab)
        {
            if (giftGrab == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1; rank <= GiftGrabRules.PlayerCount; rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
                {
                    if (giftGrab.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(
                        avatar != null
                            ? avatar.DisplayName
                            : "P" + (rankedSlot + 1))
                    .Append("  SCORE ")
                    .Append(giftGrab.GetScore(rankedSlot))
                    .Append("  GIFTS ")
                    .Append(giftGrab.GetTotalStoredGiftCount(rankedSlot))
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildTerritoryPaintResultSummary(
            NetworkTerritoryPaintState territoryPaint)
        {
            if (territoryPaint == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1;
                 rank <= TerritoryPaintRules.PlayerCount;
                 rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0;
                     slot < TerritoryPaintRules.PlayerCount;
                     slot++)
                {
                    if (territoryPaint.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(
                        avatar != null
                            ? avatar.DisplayName
                            : "P" + (rankedSlot + 1))
                    .Append("  ")
                    .Append(territoryPaint.GetScore(rankedSlot))
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildTagChaseResultSummary(
            NetworkTagChaseState tagChase)
        {
            if (tagChase == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1; rank <= TagChaseRules.PlayerCount; rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0; slot < TagChaseRules.PlayerCount; slot++)
                {
                    if (tagChase.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(
                        avatar != null
                            ? avatar.DisplayName
                            : "P" + (rankedSlot + 1))
                    .Append("  SCORE ")
                    .Append(tagChase.GetTotalScore(rankedSlot))
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildRaceResultSummary(
            NetworkRaceState race)
        {
            if (race == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1; rank <= RaceRules.PlayerCount; rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0; slot < RaceRules.PlayerCount; slot++)
                {
                    if (race.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(
                        avatar != null
                            ? avatar.DisplayName
                            : "P" + (rankedSlot + 1))
                    .Append("  SCORE ")
                    .Append(race.GetTotalScore(rankedSlot))
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildSnowySpinResultSummary(
            NetworkSnowySpinState snowySpin)
        {
            if (snowySpin == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1;
                 rank <= MultiplayerConstants.MaxPlayers;
                 rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0;
                     slot < MultiplayerConstants.MaxPlayers;
                     slot++)
                {
                    if (snowySpin.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1))
                    .Append("  SCORE ")
                    .Append(snowySpin.GetScore(rankedSlot))
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildArenaCombatResultSummary(
            NetworkArenaCombatState arenaCombat)
        {
            if (arenaCombat == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1;
                 rank <= MultiplayerConstants.MaxPlayers;
                 rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0;
                     slot < MultiplayerConstants.MaxPlayers;
                     slot++)
                {
                    if (arenaCombat.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1))
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildCliffBarrageResultSummary(
            NetworkCliffBarrageState cliffBarrage)
        {
            if (cliffBarrage == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1;
                 rank <= MultiplayerConstants.MaxPlayers;
                 rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0;
                     slot < MultiplayerConstants.MaxPlayers;
                     slot++)
                {
                    if (cliffBarrage.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1))
                    .Append("  SCORE ")
                    .Append(cliffBarrage.GetScore(rankedSlot))
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildBombPassingResultSummary(
            NetworkBombPassingState bombPassing)
        {
            if (bombPassing == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1;
                 rank <= MultiplayerConstants.MaxPlayers;
                 rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0;
                     slot < MultiplayerConstants.MaxPlayers;
                     slot++)
                {
                    if (bombPassing.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1))
                    .Append(bombPassing.IsEliminated(rankedSlot)
                        ? "  OUT"
                        : "  SURVIVED")
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildBouncingBallsResultSummary(
            NetworkBouncingBallsState bouncingBalls)
        {
            if (bouncingBalls == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1;
                 rank <= MultiplayerConstants.MaxPlayers;
                 rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0;
                     slot < MultiplayerConstants.MaxPlayers;
                     slot++)
                {
                    if (bouncingBalls.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1))
                    .Append("  GOALS ")
                    .Append(bouncingBalls.GetScore(rankedSlot))
                    .Append("  CONCEDED ")
                    .Append(bouncingBalls.GetConceded(rankedSlot))
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string BuildSequenceMemoryResultSummary(
            NetworkSequenceMemoryState sequenceMemory)
        {
            if (sequenceMemory == null)
            {
                return "Final standings are synchronizing...";
            }

            var builder = new StringBuilder();
            for (var rank = 1;
                 rank <= SequenceMemoryRules.PlayerCount;
                 rank++)
            {
                var rankedSlot = -1;
                for (var slot = 0;
                     slot < SequenceMemoryRules.PlayerCount;
                     slot++)
                {
                    if (sequenceMemory.GetFinalRank(slot) == rank)
                    {
                        rankedSlot = slot;
                        break;
                    }
                }

                if (rankedSlot < 0)
                {
                    return "Final standings are synchronizing...";
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(rank)
                    .Append(".  ")
                    .Append(
                        avatar != null
                            ? avatar.DisplayName
                            : "P" + (rankedSlot + 1))
                    .Append(sequenceMemory.IsPlayerEliminated(rankedSlot)
                        ? "  OUT"
                        : "  SURVIVED")
                    .Append("  GOLD +")
                    .Append(
                        MinigameRewardRules.GetFinalPlacementGold(rank));
            }

            return builder.ToString();
        }

        private static string MinefieldPhaseLabel(
            NetworkMinefieldState minefield)
        {
            if (minefield == null)
            {
                return "MINEFIELD";
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(ScheduledMinigameId.Minefield);
            var round = Mathf.Clamp(
                minefield.RoundNumber,
                1,
                totalRounds);
            switch (minefield.Phase)
            {
                case NetworkMinefieldPhase.Countdown:
                    return "MINEFIELD  ROUND " + round + " / " +
                           totalRounds + "  -  COUNTDOWN";
                case NetworkMinefieldPhase.Running:
                    return "MINEFIELD  ROUND " + round + " / " +
                           totalRounds + "  -  RUN";
                case NetworkMinefieldPhase.RoundResult:
                    return "MINEFIELD  ROUND " + round + " / " +
                           totalRounds + "  -  RESULT";
                case NetworkMinefieldPhase.Complete:
                    return "MINEFIELD COMPLETE";
                default:
                    return "MINEFIELD";
            }
        }

        private static string MinefieldStatus(NetworkMinefieldState minefield)
        {
            if (minefield == null)
            {
                return "Synchronizing the Minefield simulation...";
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(ScheduledMinigameId.Minefield);
            switch (minefield.Phase)
            {
                case NetworkMinefieldPhase.Countdown:
                    return "Get ready. Every player respawns and the mine layout changes each round.";
                case NetworkMinefieldPhase.Running:
                    return "WASD moves in top view. Stop moving and press RMB to scan nearby mines.";
                case NetworkMinefieldPhase.RoundResult:
                    return "Round points: 3 / 2 / 1 / 0. Finishers rank first; others rank by earliest elimination.";
                case NetworkMinefieldPhase.Complete:
                    return "All " + totalRounds +
                           " rounds complete. Final points determine rank; placement awards gold.";
                default:
                    return "Preparing Minefield...";
            }
        }

        private static string WrongWayPhaseLabel(
            NetworkWrongWayState wrongWay)
        {
            if (wrongWay == null)
            {
                return "WRONG WAY";
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(ScheduledMinigameId.WrongWay);
            var round = Mathf.Clamp(
                wrongWay.RoundNumber,
                1,
                totalRounds);
            switch (wrongWay.Phase)
            {
                case NetworkWrongWayPhase.Countdown:
                    return "WRONG WAY  ROUND " + round + " / " +
                           totalRounds + "  -  COUNTDOWN";
                case NetworkWrongWayPhase.Running:
                    return "WRONG WAY  ROUND " + round + " / " +
                           totalRounds + "  -  CLIMB";
                case NetworkWrongWayPhase.RoundResult:
                    return "WRONG WAY  ROUND " + round + " / " +
                           totalRounds + "  -  RESULT";
                case NetworkWrongWayPhase.Complete:
                    return "WRONG WAY COMPLETE";
                default:
                    return "WRONG WAY";
            }
        }

        private static string WrongWayStatus(
            NetworkWrongWayState wrongWay)
        {
            if (wrongWay == null)
            {
                return "Synchronizing the WrongWay race...";
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(ScheduledMinigameId.WrongWay);
            switch (wrongWay.Phase)
            {
                case NetworkWrongWayPhase.Countdown:
                    return "Get ready. Every player receives the same direction sequence.";
                case NetworkWrongWayPhase.Running:
                    return "Press the shown WASD direction. Wrong input locks you for 0.5 seconds.";
                case NetworkWrongWayPhase.RoundResult:
                    return "Round points: 3 / 2 / 1 / 0. More stairs and earlier arrivals rank higher.";
                case NetworkWrongWayPhase.Complete:
                    return totalRounds +
                           " rounds complete. Final points determine rank; placement awards gold.";
                default:
                    return "Preparing WrongWay...";
            }
        }

        private static string RedLightGreenLightPhaseLabel(
            NetworkRedLightGreenLightState redLightGreenLight)
        {
            if (redLightGreenLight == null)
            {
                return "RED LIGHT / GREEN LIGHT";
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(
                    ScheduledMinigameId.RedLightGreenLight);
            var round = Mathf.Clamp(
                redLightGreenLight.RoundNumber,
                1,
                totalRounds);
            switch (redLightGreenLight.Phase)
            {
                case NetworkRedLightGreenLightPhase.Countdown:
                    return "RED LIGHT / GREEN LIGHT  ROUND " + round +
                           " / " + totalRounds + "  -  COUNTDOWN";
                case NetworkRedLightGreenLightPhase.Running:
                    return "RED LIGHT / GREEN LIGHT  ROUND " + round +
                           " / " + totalRounds + "  -  " +
                           RedLightGreenLightSignalLabel(
                               redLightGreenLight.SignalPhase);
                case NetworkRedLightGreenLightPhase.RoundResult:
                    return "RED LIGHT / GREEN LIGHT  ROUND " + round +
                           " / " + totalRounds + "  -  RESULT";
                case NetworkRedLightGreenLightPhase.Complete:
                    return "RED LIGHT / GREEN LIGHT COMPLETE";
                default:
                    return "RED LIGHT / GREEN LIGHT";
            }
        }

        private static string RedLightGreenLightStatus(
            NetworkRedLightGreenLightState redLightGreenLight)
        {
            if (redLightGreenLight == null)
            {
                return "Synchronizing the Red Light / Green Light race...";
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(
                    ScheduledMinigameId.RedLightGreenLight);
            switch (redLightGreenLight.Phase)
            {
                case NetworkRedLightGreenLightPhase.Countdown:
                    return "Get ready at the shared start line.";
                case NetworkRedLightGreenLightPhase.Running:
                    var remaining =
                        redLightGreenLight.SignalRemaining.ToString("0.0") +
                        "s";
                    switch (redLightGreenLight.SignalPhase)
                    {
                        case RedLightGreenLightSignalPhase.Green:
                            return "GREEN  " + remaining +
                                   ": move toward the finish.";
                        case RedLightGreenLightSignalPhase.TurnWarning:
                            return "TURNING  " + remaining +
                                   ": movement remains legal until RED begins.";
                        case RedLightGreenLightSignalPhase.Red:
                            return "RED  " + remaining +
                                   ": freeze; voluntary movement is a violation.";
                        default:
                            return "Follow the synchronized signal.";
                    }
                case NetworkRedLightGreenLightPhase.RoundResult:
                    return "Finishers rank first, then survivors by forward " +
                           "progress, with eliminated players placed last.";
                case NetworkRedLightGreenLightPhase.Complete:
                    return "All " + totalRounds +
                           " rounds complete. Final points determine " +
                           "rank; placement awards gold.";
                default:
                    return "Preparing Red Light / Green Light...";
            }
        }

        private static string RedLightGreenLightSignalLabel(
            RedLightGreenLightSignalPhase signal)
        {
            switch (signal)
            {
                case RedLightGreenLightSignalPhase.Green:
                    return "GREEN";
                case RedLightGreenLightSignalPhase.TurnWarning:
                    return "TURNING";
                case RedLightGreenLightSignalPhase.Red:
                    return "RED";
                default:
                    return signal.ToString().ToUpperInvariant();
            }
        }

        private static string StableFootingPhaseLabel(
            NetworkStableFootingState stableFooting)
        {
            if (stableFooting == null)
            {
                return "STABLE FOOTING";
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(
                    ScheduledMinigameId.StableFooting);
            var round = Mathf.Clamp(
                stableFooting.RoundNumber,
                1,
                totalRounds);
            switch (stableFooting.Phase)
            {
                case NetworkStableFootingPhase.Countdown:
                    return "STABLE FOOTING  ROUND " + round +
                           " / " + totalRounds + "  -  COUNTDOWN";
                case NetworkStableFootingPhase.Running:
                    return "STABLE FOOTING  ROUND " + round +
                           " / " + totalRounds + "  -  " +
                           stableFooting.CyclePhase.ToString().ToUpperInvariant();
                case NetworkStableFootingPhase.RoundResult:
                    return "STABLE FOOTING  ROUND " + round +
                           " / " + totalRounds + "  -  RESULT";
                case NetworkStableFootingPhase.Complete:
                    return "STABLE FOOTING COMPLETE";
                default:
                    return "STABLE FOOTING";
            }
        }

        private static string StableFootingStatus(
            NetworkStableFootingState stableFooting)
        {
            if (stableFooting == null)
            {
                return "Synchronizing the Stable Footing arena...";
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(
                    ScheduledMinigameId.StableFooting);
            switch (stableFooting.Phase)
            {
                case NetworkStableFootingPhase.Countdown:
                    return "Get ready on the shared 6 x 8 platform arena.";
                case NetworkStableFootingPhase.Running:
                    switch (stableFooting.CyclePhase)
                    {
                        case StableFootingCyclePhase.ShuffleReveal:
                            return "Symbols are shuffling. Watch the shared safe-symbol display.";
                        case StableFootingCyclePhase.Move:
                            return "WASD moves. LMB pushes the nearest player in front of you.";
                        case StableFootingCyclePhase.Drop:
                            return "Unsafe platforms are dropping. Falling eliminates immediately.";
                        case StableFootingCyclePhase.Restore:
                            return "Platforms are returning; two remain permanently removed.";
                        default:
                            return "Stay on the announced safe symbol.";
                    }
                case NetworkStableFootingPhase.RoundResult:
                    return "The last survivor ranks first; later falls rank above earlier falls.";
                case NetworkStableFootingPhase.Complete:
                    return "All " + totalRounds +
                           " rounds complete. Final points determine rank; placement awards gold.";
                default:
                    return "Preparing Stable Footing...";
            }
        }

        private static string BalloonBlowPhaseLabel(
            NetworkBalloonBlowState balloonBlow)
        {
            if (balloonBlow == null)
            {
                return "BALLOON BLOW";
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(
                    ScheduledMinigameId.BalloonBlow);
            var round = Mathf.Clamp(
                balloonBlow.RoundNumber,
                1,
                totalRounds);
            switch (balloonBlow.Phase)
            {
                case NetworkBalloonBlowPhase.Countdown:
                    return "BALLOON BLOW  ROUND " + round +
                           " / " + totalRounds + "  -  COUNTDOWN";
                case NetworkBalloonBlowPhase.Running:
                    return "BALLOON BLOW  ROUND " + round +
                           " / " + totalRounds + "  -  INFLATE";
                case NetworkBalloonBlowPhase.RoundResult:
                    return "BALLOON BLOW  ROUND " + round +
                           " / " + totalRounds + "  -  RESULT";
                case NetworkBalloonBlowPhase.Complete:
                    return "BALLOON BLOW COMPLETE";
                default:
                    return "BALLOON BLOW";
            }
        }

        private static string BalloonBlowStatus(
            NetworkBalloonBlowState balloonBlow)
        {
            if (balloonBlow == null)
            {
                return "Synchronizing the Balloon Blow arena...";
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(
                    ScheduledMinigameId.BalloonBlow);
            switch (balloonBlow.Phase)
            {
                case NetworkBalloonBlowPhase.Countdown:
                    return "Get ready. Hold LMB after the countdown to inflate.";
                case NetworkBalloonBlowPhase.Running:
                    return "Hold LMB to inflate. Release before two seconds; " +
                           "idle and cooldown time slowly deflate your balloon.";
                case NetworkBalloonBlowPhase.RoundResult:
                    return "Popped balloons rank by pop order; remaining balloons " +
                           "rank by progress, then server player order.";
                case NetworkBalloonBlowPhase.Complete:
                    return "All " + totalRounds +
                           " rounds complete. Final points determine rank; placement awards gold.";
                default:
                    return "Preparing Balloon Blow...";
            }
        }

        private static string GiftGrabPhaseLabel(
            NetworkGiftGrabState giftGrab)
        {
            if (giftGrab == null)
            {
                return "GIFT GRAB";
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(ScheduledMinigameId.GiftGrab);
            var round = Mathf.Clamp(
                giftGrab.RoundNumber,
                1,
                totalRounds);
            switch (giftGrab.Phase)
            {
                case NetworkGiftGrabPhase.Countdown:
                    return "GIFT GRAB  ROUND " + round +
                           " / " + totalRounds + "  -  COUNTDOWN";
                case NetworkGiftGrabPhase.Running:
                    return "GIFT GRAB  ROUND " + round +
                           " / " + totalRounds + "  -  STEAL";
                case NetworkGiftGrabPhase.RoundResult:
                    return "GIFT GRAB  ROUND " + round +
                           " / " + totalRounds + "  -  RESULT";
                case NetworkGiftGrabPhase.Complete:
                    return "GIFT GRAB COMPLETE";
                default:
                    return "GIFT GRAB";
            }
        }

        private static string TerritoryPaintPhaseLabel(
            NetworkTerritoryPaintState territoryPaint)
        {
            if (territoryPaint == null)
            {
                return "TERRITORY PAINT";
            }

            switch (territoryPaint.Phase)
            {
                case NetworkTerritoryPaintPhase.Countdown:
                    return "TERRITORY PAINT  -  COUNTDOWN";
                case NetworkTerritoryPaintPhase.Running:
                    return "TERRITORY PAINT";
                case NetworkTerritoryPaintPhase.RoundResult:
                    return "TERRITORY PAINT  -  RESULT";
                case NetworkTerritoryPaintPhase.Complete:
                    return "TERRITORY PAINT COMPLETE";
                default:
                    return "TERRITORY PAINT";
            }
        }

        private static string TagChasePhaseLabel(
            NetworkTagChaseState tagChase)
        {
            if (tagChase == null)
            {
                return "TAG CHASE";
            }

            var round = Mathf.Clamp(
                tagChase.RoundNumber,
                1,
                TagChaseRules.RoundCount);
            switch (tagChase.Phase)
            {
                case NetworkTagChasePhase.Countdown:
                    return "TAG CHASE  ROUND " + round + " / " +
                           TagChaseRules.RoundCount + "  -  COUNTDOWN";
                case NetworkTagChasePhase.Running:
                    return "TAG CHASE  ROUND " + round + " / " +
                           TagChaseRules.RoundCount + "  -  CHASE";
                case NetworkTagChasePhase.RoundResult:
                    return "TAG CHASE  ROUND " + round + " / " +
                           TagChaseRules.RoundCount + "  -  RESULT";
                case NetworkTagChasePhase.Complete:
                    return "TAG CHASE COMPLETE";
                default:
                    return "TAG CHASE";
            }
        }

        private static string TagChaseStatus(
            NetworkTagChaseState tagChase)
        {
            if (tagChase == null)
            {
                return "Synchronizing the Tag Chase arena...";
            }

            var taggerLabel = tagChase.TaggerSlot >= 0
                ? "P" + (tagChase.TaggerSlot + 1)
                : "The selected player";
            switch (tagChase.Phase)
            {
                case NetworkTagChasePhase.Countdown:
                    return taggerLabel +
                           " is the tagger. Get ready for the chase.";
                case NetworkTagChasePhase.Running:
                    return "Runners escape with WASD on the shared camera. " +
                           "The tagger uses WASD and LMB in first person.";
                case NetworkTagChasePhase.RoundResult:
                    return "The tagger earns 3 points only after catching every " +
                           "runner; surviving and caught runners score separately.";
                case NetworkTagChasePhase.Complete:
                    return "All " + TagChaseRules.RoundCount +
                           " rounds complete. Total points determine rank; " +
                           "placement awards gold.";
                default:
                    return "Preparing Tag Chase...";
            }
        }

        private static string RacePhaseLabel(NetworkRaceState race)
        {
            if (race == null)
            {
                return "RACE";
            }

            var round = Mathf.Clamp(
                race.RoundNumber,
                1,
                RaceRules.RoundCount);
            switch (race.Phase)
            {
                case NetworkRacePhase.Countdown:
                    return "RACE  ROUND " + round + " / " +
                           RaceRules.RoundCount + "  -  COUNTDOWN";
                case NetworkRacePhase.Running:
                    return "RACE  ROUND " + round + " / " +
                           RaceRules.RoundCount + "  -  RUN";
                case NetworkRacePhase.RoundResult:
                    return "RACE  ROUND " + round + " / " +
                           RaceRules.RoundCount + "  -  RESULT";
                case NetworkRacePhase.Complete:
                    return "RACE COMPLETE";
                default:
                    return "RACE";
            }
        }

        private static string RaceStatus(NetworkRaceState race)
        {
            if (race == null)
            {
                return "Synchronizing the Race arena...";
            }

            switch (race.Phase)
            {
                case NetworkRacePhase.Countdown:
                    return "Get ready to alternate A and D.";
                case NetworkRacePhase.Running:
                    return "Alternate A and D. Repeating the same key does not " +
                           "advance; first to 500 steps ends the round.";
                case NetworkRacePhase.RoundResult:
                    return "More steps rank higher; server input order breaks " +
                           "equal-progress ties.";
                case NetworkRacePhase.Complete:
                    return "All " + RaceRules.RoundCount +
                           " rounds complete. Total points determine rank; " +
                           "placement awards gold.";
                default:
                    return "Preparing Race...";
            }
        }

        private static string BouncingBallsPhaseLabel(
            NetworkBouncingBallsState bouncingBalls)
        {
            if (bouncingBalls == null)
            {
                return "BOUNCING BALLS";
            }

            var round = Mathf.Clamp(
                bouncingBalls.RoundNumber,
                1,
                BouncingBallsRules.RoundCount);
            switch (bouncingBalls.Phase)
            {
                case NetworkBouncingBallsPhase.Countdown:
                    return "BOUNCING BALLS  ROUND " + round + " / " +
                           BouncingBallsRules.RoundCount + "  -  COUNTDOWN";
                case NetworkBouncingBallsPhase.Playing:
                    return "BOUNCING BALLS  ROUND " + round + " / " +
                           BouncingBallsRules.RoundCount + "  -  PLAY";
                case NetworkBouncingBallsPhase.RoundBreak:
                    return "BOUNCING BALLS  ROUND " + round + " / " +
                           BouncingBallsRules.RoundCount + "  -  RESULT";
                case NetworkBouncingBallsPhase.Complete:
                    return "BOUNCING BALLS COMPLETE";
                default:
                    return "BOUNCING BALLS";
            }
        }

        private static string BouncingBallsStatus(
            NetworkBouncingBallsState bouncingBalls)
        {
            if (bouncingBalls == null)
            {
                return "Synchronizing the Bouncing Balls arena...";
            }

            switch (bouncingBalls.Phase)
            {
                case NetworkBouncingBallsPhase.Countdown:
                    return "Three neutral balls will launch from the center.";
                case NetworkBouncingBallsPhase.Playing:
                    return "Hold A or D to slide your shield. A touched ball " +
                           "takes your color; a goal scores for its color owner.";
                case NetworkBouncingBallsPhase.RoundBreak:
                    return "Round over. Combined goals across both rounds " +
                           "determine final placement.";
                case NetworkBouncingBallsPhase.Complete:
                    return "Two rounds complete. Final placement awards " +
                           MinigameRewardRules.FinalPlacementGoldSchedule +
                           " gold.";
                default:
                    return "Preparing Bouncing Balls...";
            }
        }

        private static string SequenceMemoryPhaseLabel(
            NetworkSequenceMemoryState sequenceMemory)
        {
            if (sequenceMemory == null)
            {
                return "SEQUENCE MEMORY";
            }

            var round = Mathf.Clamp(
                sequenceMemory.RoundNumber,
                1,
                SequenceMemoryRules.RoundCount);
            switch (sequenceMemory.Phase)
            {
                case NetworkSequenceMemoryPhase.Countdown:
                    return "SEQUENCE MEMORY  -  COUNTDOWN";
                case NetworkSequenceMemoryPhase.PresentingProblem:
                    return "SEQUENCE MEMORY  PROBLEM " + round +
                           " / " + SequenceMemoryRules.RoundCount +
                           "  -  WATCH";
                case NetworkSequenceMemoryPhase.AcceptingInput:
                    return "SEQUENCE MEMORY  PROBLEM " + round +
                           " / " + SequenceMemoryRules.RoundCount +
                           "  -  INPUT";
                case NetworkSequenceMemoryPhase.RevealingAnswer:
                    return "SEQUENCE MEMORY  PROBLEM " + round +
                           " / " + SequenceMemoryRules.RoundCount +
                           "  -  ANSWER";
                case NetworkSequenceMemoryPhase.Complete:
                    return "SEQUENCE MEMORY COMPLETE";
                default:
                    return "SEQUENCE MEMORY";
            }
        }

        private static string SequenceMemoryStatus(
            NetworkSequenceMemoryState sequenceMemory)
        {
            if (sequenceMemory == null)
            {
                return "Synchronizing the Sequence Memory game...";
            }

            switch (sequenceMemory.Phase)
            {
                case NetworkSequenceMemoryPhase.Countdown:
                    return "Get ready. The NPC will play one shared A/S/D sequence.";
                case NetworkSequenceMemoryPhase.PresentingProblem:
                    return "Watch and listen: A is high, S is middle and D is low.";
                case NetworkSequenceMemoryPhase.AcceptingInput:
                    return "Repeat the hidden sequence with A, S and D. A wrong key locks this problem immediately.";
                case NetworkSequenceMemoryPhase.RevealingAnswer:
                    return "The answer is visible. One mistake loses the torso; the second eliminates.";
                case NetworkSequenceMemoryPhase.Complete:
                    return "The single match is complete. Placement awards 10 / 6 / 3 / 0 gold.";
                default:
                    return "Preparing Sequence Memory...";
            }
        }

        private static string TerritoryPaintStatus(
            NetworkTerritoryPaintState territoryPaint)
        {
            if (territoryPaint == null)
            {
                return "Synchronizing the Territory Paint arena...";
            }

            switch (territoryPaint.Phase)
            {
                case NetworkTerritoryPaintPhase.Countdown:
                    return "Get ready at your corner.";
                case NetworkTerritoryPaintPhase.Running:
                    return "WASD moves and continuously paints a circular trail.";
                case NetworkTerritoryPaintPhase.RoundResult:
                    return "Current owned area decides the final score.";
                case NetworkTerritoryPaintPhase.Complete:
                    return "Territory Paint complete.";
                default:
                    return "Preparing Territory Paint...";
            }
        }

        private static string GiftGrabStatus(NetworkGiftGrabState giftGrab)
        {
            if (giftGrab == null)
            {
                return "Synchronizing the Gift Grab arena...";
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(ScheduledMinigameId.GiftGrab);
            switch (giftGrab.Phase)
            {
                case NetworkGiftGrabPhase.Countdown:
                    return "Get ready. Ten gifts begin in the shared arena.";
                case NetworkGiftGrabPhase.Running:
                    return "WASD moves. Carry gifts home, throw while carrying, " +
                           "or push while empty-handed. Three gifts drop every 15 seconds.";
                case NetworkGiftGrabPhase.RoundResult:
                    return "Stored gifts decide the round; gift ownership time breaks ties.";
                case NetworkGiftGrabPhase.Complete:
                    return totalRounds +
                           " rounds complete. Points, total stored gifts, " +
                           "final-round rank, then server player order " +
                           "determine placement.";
                default:
                    return "Preparing Gift Grab...";
            }
        }





        private void SetWaitingState()
        {
            SetBoardUiVisible(true);
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

        private static void CopyReferences<T>(T[] source, T[] destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                destination[index] = source[index];
            }
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
                case BoardFlowState.MinigameIntroReady: return "MINIGAME READY";
                case BoardFlowState.MinigameLoading: return "LOADING MINIGAME";
                case BoardFlowState.MinigamePlaying: return "MINIGAME";
                case BoardFlowState.SkippedResult: return "MINIGAME RESULTS";
                case BoardFlowState.MatchComplete: return "MATCH COMPLETE";
                default: return state.ToString().ToUpperInvariant();
            }
        }

        private static string MinigameName(ScheduledMinigameId minigame)
        {
            return MinigameCatalog.GetDisplayName(minigame);
        }

        private bool IsMinigameRevealPending(NetworkMatchState match)
        {
            return match != null &&
                   match.FlowState == BoardFlowState.MinigameIntroReady &&
                   Time.unscaledTime - _minigameRevealObservedAt <
                   MinigameScheduleTowerView.RevealDelaySeconds;
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

    }
}

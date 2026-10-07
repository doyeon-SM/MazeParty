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
        private static readonly Color AvailableItemIconColor = Color.white;
        private static readonly Color SoldItemIconColor =
            new Color(0.45f, 0.45f, 0.45f, 0.45f);

        [SerializeField] private GameplayCameraDirector cameraDirector;
        [SerializeField] private BoardCanvasBindings uiBindings;
        [SerializeField] private MinigameResultCanvasBindings resultUiBindings;

        private readonly Image[] _slotBackgrounds = new Image[GameplayInventory.Capacity];
        private readonly Image[] _slotIcons = new Image[GameplayInventory.Capacity];
        private readonly Button[] _choiceButtons = new Button[GameplayInventory.Capacity];
        private readonly Text[] _choiceLabels = new Text[GameplayInventory.Capacity];
        private readonly Text[] _playerRows = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Image[] _playerCards = new Image[MultiplayerConstants.MaxPlayers];
        private readonly Image[] _playerHealthFills = new Image[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerHealthTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerKeyTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerGoldTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerActionIcons = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerRankTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _minigameReadyPlayerStates =
            new Text[MultiplayerConstants.MaxPlayers];
        private readonly Button[] _shopOfferButtons = new Button[ItemShopRules.OfferCount];
        private readonly Image[] _shopOfferIcons = new Image[ItemShopRules.OfferCount];
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
        private Text _reticleText;
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
        private readonly BoardSoundFeedback _soundFeedback = new BoardSoundFeedback();
        private KeyShopWorldMarker _keyShopMarker;
        private ItemShopWorldMarker _itemShopMarker;
        private BoardTopology _topology;
        private int _lastRevision = -1;
        private bool _lastLocalBoardDeath;
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
                _soundFeedback.Reset();
                SetWaitingState();
                return;
            }

            _soundFeedback.Observe(match, _localAvatar);
            SetBoardUiVisible(
                match.FlowState != BoardFlowState.MinigamePlaying &&
                match.FlowState != BoardFlowState.MatchComplete);
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

            _tooltipText.text = GameText.T(_localAvatar.GetLocalItemName(slotIndex)) + "\n" +
                                GameText.T(_localAvatar.GetLocalItemDescription(slotIndex));
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
                ? GameText.T("Select an available item to buy it immediately.")
                : GameText.T("INVENTORY FULL - Browse only; purchases are disabled."));
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
                SetText(_itemShopStatus, GameText.T("That item is already sold."));
                GameSound.Play(SoundKeys.BoardShopFail);
                return;
            }

            var definition = PrototypeItemCatalog.Get(itemId);
            if (!_localAvatar.HasFreeItemSlot)
            {
                SetText(_itemShopStatus, GameText.T("INVENTORY FULL - No gold was spent."));
                GameSound.Play(SoundKeys.BoardShopFail);
                return;
            }
            if (_localAvatar.Gold < definition.Price)
            {
                SetText(_itemShopStatus, GameText.T("NOT ENOUGH GOLD - No gold was spent."));
                GameSound.Play(SoundKeys.BoardShopFail);
                return;
            }

            _localAvatar.PurchaseItemFromShop(
                _openItemShopIndex,
                offerIndex,
                snapshot.Revision);
            SetText(_itemShopStatus, GameText.T("Purchase requested. Server stock decides the winner."));
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
            _itemShopTooltip.text = GameText.T(definition.DisplayName) + "  /  " +
                                    GameText.F("{0} GOLD", definition.Price) + "\n" +
                                    GameText.T(definition.Description);
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
            _reticleText = uiBindings.ReticleText;
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
            CopyReferences(uiBindings.InventorySlotIcons, _slotIcons);
            CopyReferences(uiBindings.ItemChoiceButtons, _choiceButtons);
            CopyReferences(uiBindings.ItemChoiceLabels, _choiceLabels);
            CopyReferences(uiBindings.ShopOfferButtons, _shopOfferButtons);
            CopyReferences(uiBindings.ShopOfferIcons, _shopOfferIcons);
            CopyReferences(uiBindings.ShopOfferLabels, _shopOfferLabels);
            CopyReferences(uiBindings.PlayerRows, _playerRows);
            CopyReferences(uiBindings.PlayerCards, _playerCards);
            CopyReferences(uiBindings.PlayerHealthFills, _playerHealthFills);
            CopyReferences(uiBindings.PlayerHealthTexts, _playerHealthTexts);
            CopyReferences(uiBindings.PlayerKeyTexts, _playerKeyTexts);
            CopyReferences(uiBindings.PlayerGoldTexts, _playerGoldTexts);
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
                match.FlowState == BoardFlowState.MinigameResult &&
                !match.IsGlobalSimulationPaused);
            SetActive(_reconnectOverlay, match.IsReconnectPaused);
            var showReticle =
                ((match.FlowState == BoardFlowState.Action && !choicePending &&
                  !IsItemShopOpen) ||
                 (match.IsCombatPhase && _localAvatar != null &&
                  match.IsCombatParticipant(_localAvatar.AssignedSlot) &&
                  match.IsCombatAlive(_localAvatar.AssignedSlot))) &&
                !match.IsGlobalSimulationPaused;
            SetActive(_reticle, showReticle);
            RefreshReticleColor(showReticle);

            RefreshMinefieldPanelContent(match);

            if (_reconnectText != null)
            {
                _reconnectText.text = GameText.F(
                    "PLAYER DISCONNECTED\nMATCH PAUSED\n{0} remaining",
                    MinigameDisplayFormatter.FormatClock(
                        match.ReconnectRemaining));
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
            SetText(_turnText, GameText.F("TURN {0}", match.Turn));
            SetText(_phaseText, match.IsArrivalGraceActive
                ? GameText.T("ARRIVAL COMPLETE")
                : match.IsKeyShopRevealActive
                ? GameText.T("KEY SHOP MOVING")
                : match.IsCombatPhase && match.IsCombatActive
                    ? GameText.F("FIGHT {0}  /  {1}", match.CombatSequenceIndex,
                      match.CombatSequenceIndex + match.CombatQueueCount)
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
                                ? GameText.T("BOMB PASSING")
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.SnowySpin
                                ? GameText.T("SNOWY SPIN")
                            : MinefieldPhaseLabel(minefield)
                        : match.FlowState == BoardFlowState.MinigameIntroReady
                            ? revealPending
                                ? "???"
                                : GameText.F("{0} READY", MinigameName(match.CurrentMinigame))
                        : match.FlowState == BoardFlowState.MinigameLoading
                            ? GameText.F("LOADING {0}", MinigameName(match.CurrentMinigame))
                        : PhaseLabel(match.FlowState));

            string timerLabel;
            if (match.IsArrivalGraceActive)
            {
                timerLabel = GameText.F("TOP VIEW  {0:0.0}s",
                             match.ArrivalGraceRemaining);
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
                    ? GameText.F("CHOOSE  {0}",
                      MinigameDisplayFormatter.FormatClock(
                          match.ChoiceRemaining))
                    : GameText.F("CHOICE  {0}", ChoiceLabel(_localAvatar)));

            if (_shieldText != null)
            {
                var shieldRemaining = Math.Max(
                    match.ShieldRemaining,
                    _localAvatar != null
                        ? _localAvatar.PersonalItemProtectionRemaining
                        : 0d);
                _shieldText.text = shieldRemaining > 0d
                    ? GameText.F("SHIELD  {0:0.0}s", shieldRemaining)
                    : GameText.T("SHIELD  OFF");
                _shieldText.color = shieldRemaining > 0d
                    ? uiBindings.ShieldActiveColor
                    : uiBindings.ShieldInactiveColor;
            }
        }

        private void RefreshLocalPlayer(NetworkMatchState match)
        {
            if (_localAvatar == null)
            {
                SetText(_diceText, GameText.T("DICE  WAITING FOR PLAYER"));
                SetText(_movesText, GameText.T("MOVES  --"));
                return;
            }

            if (match.IsCombatPhase)
            {
                var isFighting = match.IsCombatActive &&
                                 match.IsCombatParticipant(_localAvatar.AssignedSlot) &&
                                 match.IsCombatAlive(_localAvatar.AssignedSlot);
                SetText(_diceText, isFighting ? GameText.T("LMB  PUNCH") : GameText.T("FIGHT  SPECTATING"));
                SetText(_movesText, isFighting
                    ? _localAvatar.IsQuietWalking
                        ? GameText.T("QUIET WALK  6m")
                        : GameText.T("WASD  MOVE / LCTRL QUIET 6m")
                    : GameText.T("INPUT  LOCKED"));
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
            if (isActionPhase && _localAvatar.UsesDoubleDice)
                SetText(_diceText, _localAvatar.LocalDiceSummary);
            if (!isActionPhase)
            {
                SetText(_movesText, GameText.T("MOVES  --"));
                return;
            }

            var movementLabel = _localAvatar.HasRolled &&
                                _localAvatar.LocalRemainingMoves == 0
                ? GameText.T("MOVES  0 / FREE IN ROOM")
                : GameText.F("MOVES  {0}", _localAvatar.LocalRemainingMoves);
            SetText(_movesText, movementLabel + "  /  " +
                (_localAvatar.IsQuietWalking
                    ? GameText.T("QUIET WALK 6m")
                    : GameText.T("LCTRL QUIET 6m")));
        }

        private void RefreshInventory()
        {
            for (var i = 0; i < GameplayInventory.Capacity; i++)
            {
                var itemId = _localAvatar != null
                    ? _localAvatar.GetLocalItemId(i)
                    : PrototypeItemId.None;
                var occupied = PrototypeItemCatalog.IsValid(itemId);
                var definition = occupied
                    ? PrototypeItemCatalog.Get(itemId)
                    : null;
                var selected = _localAvatar != null && _localAvatar.LocalSelectedItemSlot == i;
                var label = occupied
                    ? GameText.T(definition.DisplayName)
                    : GameText.T("EMPTY");
                SetText(_choiceLabels[i], label);
                SetItemIcon(
                    _slotIcons[i],
                    definition != null ? definition.Icon : null,
                    AvailableItemIconColor);

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
                    ? _localAvatar.UsesDoubleDice ? GameText.T("RMB  ROLL EACH DIE") :
                        GameText.F("AMMO  {0}\nLMB USE / RMB INTERACT", _localAvatar.LocalItemCharges)
                    : GameText.T("CHARGE  --"));
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
                    ? GameText.T("RECONNECTING")
                    : isLocal ? GameText.T("LOCAL") : GameText.T("ONLINE");
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
                SetText(_playerKeyTexts[slot], avatar != null
                    ? avatar.KeyCount.ToString()
                    : "--");
                SetText(_playerGoldTexts[slot], avatar != null
                    ? avatar.Gold.ToString()
                    : "--");
                SetText(_playerRankTexts[slot], avatar != null
                    ? GameText.F("RANK {0}", ranks[slot])
                    : GameText.T("RANK --"));

                var actionState = avatar != null && isPresent && !match.IsGlobalSimulationPaused
                    ? avatar.ActionState
                    : PlayerBoardActionState.Hidden;
                var isCombatOut = showCombatHealth && !match.IsCombatAlive(slot);
                SetText(_playerActionIcons[slot], isCombatOut
                    ? GameText.T("OUT")
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
            SetText(_itemShopTitle, GameText.F("ITEM SHOP {0}", _openItemShopIndex + 1));
            for (var offerIndex = 0; offerIndex < ItemShopRules.OfferCount; offerIndex++)
            {
                var itemId = snapshot.GetOffer(offerIndex);
                var sold = snapshot.IsSold(offerIndex);
                var valid = PrototypeItemCatalog.IsValid(itemId);
                var definition = valid ? PrototypeItemCatalog.Get(itemId) : default;
                SetItemIcon(
                    _shopOfferIcons[offerIndex],
                    valid ? definition.Icon : null,
                    sold ? SoldItemIconColor : AvailableItemIconColor);
                SetText(_shopOfferLabels[offerIndex], sold
                    ? GameText.F("SOLD\n{0}", valid ? GameText.T(definition.DisplayName) : GameText.T("ITEM"))
                    : valid
                        ? GameText.T(definition.DisplayName) + "\n" + GameText.F("{0} GOLD", definition.Price)
                        : GameText.T("UNAVAILABLE"));
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
                    GameText.T("SOLD OUT - This shop moves and restocks next overview."));
            }
            else if (!_localAvatar.HasFreeItemSlot)
            {
                SetText(_itemShopStatus, GameText.T("INVENTORY FULL - Browse only; purchases are disabled."));
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
                                 choicePending || IsItemShopOpen || BoardUtilityItemView.IsTargetPickerOpen;
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
            var localBoardDeath = _localAvatar != null &&
                                  _localAvatar.CurrentHealth <= 0;
            if (_lastRevision == match.StateRevision &&
                _lastLocalBoardDeath == localBoardDeath &&
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
            _lastLocalBoardDeath = localBoardDeath;
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
                    GameText.T("Key purchased. The new shop location is shown; play resumes when the countdown ends."));
                return;
            }
            if (_localAvatar != null && _localAvatar.CurrentHealth <= 0 &&
                (match.FlowState == BoardFlowState.Action ||
                 match.FlowState == BoardFlowState.AscendingResolve))
            {
                SetText(_statusText,
                    GameText.T("KNOCKED OUT: respawning at the nearest marked room. Input is locked."));
                return;
            }
            if (choice == ItemChoiceResolution.TimedOut)
            {
                SetText(_statusText, GameText.T("Choice timed out. DO NOT USE selected automatically."));
                return;
            }

            switch (match.FlowState)
            {
                case BoardFlowState.TurnOverview:
                    SetText(_statusText, GameText.T("Board overview: inspect every player and tile."));
                    break;
                case BoardFlowState.Descending:
                    SetText(_statusText, GameText.T("Camera descending to your first-person view."));
                    break;
                case BoardFlowState.Action:
                    SetText(_statusText, match.IsArrivalGraceActive
                        ? GameText.T("All players arrived. Top view opens when the countdown ends.")
                        : choice == ItemChoiceResolution.Pending
                        ? GameText.T("Choose an item or DO NOT USE. Your personal limit is 30 seconds.")
                        : GameText.T("WASD moves inside the room. Aim at your world die: RMB rolls, LMB nudges. LMB elsewhere uses the active item."));
                    break;
                case BoardFlowState.AscendingResolve:
                    SetText(_statusText, GameText.T("Input closed. Camera rising while pending effects settle."));
                    break;
                case BoardFlowState.CombatResolve:
                    var isFighting = match.IsCombatActive && _localAvatar != null &&
                                     match.IsCombatParticipant(_localAvatar.AssignedSlot) &&
                                     match.IsCombatAlive(_localAvatar.AssignedSlot);
                    SetText(_statusText, isFighting
                        ? GameText.T("FIGHT: WASD moves inside the room. LMB punches for 5 temporary HP damage.")
                        : GameText.T("SPECTATING: the camera follows the current fight room. Input is locked."));
                    break;
                case BoardFlowState.LandingEffectResolve:
                    SetText(_statusText,
                        string.IsNullOrEmpty(match.LastLandingEffectMessage)
                            ? GameText.T("Applying final landing effects in player order.")
                            : match.LastLandingEffectMessage);
                    break;
                case BoardFlowState.MinigameIntroReady:
                    SetText(
                        _statusText,
                        revealPending
                            ? GameText.T("Opening the top block in the minigame tower...")
                            : match.CurrentMinigame == ScheduledMinigameId.Skip
                            ? GameText.T("No minigame is available for this queue slot. It will advance automatically.")
                            : GameText.F("{0}: review the rules. Press READY to start early; the minigame starts automatically when the countdown ends.",
                              MinigameName(match.CurrentMinigame)));
                    break;
                case BoardFlowState.MinigameLoading:
                    SetText(_statusText,
                        GameText.F("Loading the synchronized {0} scene. Board movement is locked; the match ends if loading reaches zero.",
                        MinigameName(match.CurrentMinigame)));
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
                                ? GameText.T("Keep the bomb away. Its light flashes " +
                                  "faster as detonation approaches.")
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.SnowySpin
                                ? GameText.T("Roll and push opponents off the ice.")
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.ArenaCombat
                                ? GameText.T("Fight in first person. Eliminated players spectate.")
                            : match.CurrentMinigame ==
                              ScheduledMinigameId.CliffBarrage
                                ? GameText.T("Dodge shells and lasers. Push rivals off the cliff.")
                            : MinefieldStatus(minefield));
                    break;
                case BoardFlowState.MinigameResult:
                    SetText(
                        _statusText,
                        match.CurrentMinigame == ScheduledMinigameId.Skip
                            ? GameText.T("SKIPPED: moving to the next block in the minigame tower.")
                            : GameText.F("{0} COMPLETE: final standings and {1} gold rewards are shown.",
                              MinigameName(match.CurrentMinigame),
                              MinigameRewardRules.FinalPlacementGoldSchedule));
                    break;
                case BoardFlowState.MatchComplete:
                    SetText(
                        _statusText,
                        GameText.T("MATCH COMPLETE: all 15 turns have finished."));
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
                    ? GameText.T("WRONG WAY / STAIR RACE")
                    : isRedLightGreenLight
                        ? GameText.T("RED LIGHT / GREEN LIGHT")
                    : isStableFooting
                        ? GameText.T("STABLE FOOTING")
                    : isBalloonBlow
                        ? GameText.T("BALLOON BLOW")
                    : isGiftGrab
                        ? GameText.T("GIFT GRAB")
                    : isTerritoryPaint
                        ? GameText.T("TERRITORY PAINT")
                    : isTagChase
                        ? GameText.T("TAG CHASE")
                    : isRace
                        ? GameText.T("RACE")
                    : isSequenceMemory
                        ? GameText.T("SEQUENCE MEMORY")
                    : isBouncingBalls
                        ? GameText.T("BOUNCING BALLS")
                    : isBombPassing
                        ? GameText.T("BOMB PASSING")
                    : isSnowySpin
                        ? GameText.T("SNOWY SPIN")
                    : isArenaCombat
                        ? GameText.T("ARENA COMBAT")
                    : isCliffBarrage
                        ? GameText.T("CLIFF BARRAGE")
                    : isSkip
                        ? GameText.T("NO MINIGAME / SKIP")
                        : GameText.T("MINEFIELD / TOP-DOWN"));
            SetText(
                _minigameReadyNote,
                revealPending
                    ? GameText.T("Opening the top block in the minigame tower...")
                    : isWrongWay
                    ? GameText.F("Press the shown WASD direction to climb. A wrong key knocks " +
                      "you down for 0.5 seconds. First to step 50 ends the round. " +
                      "Two rounds, 60 seconds each.\nREADY {0} / 4",
                      readyCount)
                    : isRedLightGreenLight
                        ? GameText.F("Move with WASD during GREEN and freeze when RED begins. " +
                          "The first violation injures you and slows you to walking " +
                          "speed; the second eliminates you. First finisher ends the " +
                          "round. Three rounds, 60 seconds each.\n" +
                          "READY {0} / 4", readyCount)
                    : isStableFooting
                        ? GameText.F("Move with WASD and press LMB to push the nearest player " +
                          "in front of you. Reach the announced X, circle or square " +
                          "before unsafe platforms drop. Two dropped platforms are " +
                          "removed each cycle. Last survivor wins each of three " +
                          "60-second rounds.\nREADY {0} / 4",
                           readyCount)
                    : isBalloonBlow
                        ? GameText.F("Hold LMB to inflate at 10% per second. Release before " +
                          "two seconds for a 1-second cooldown; reaching two " +
                          "seconds forces a stop and a 1.5-second cooldown, then " +
                          "requires a fresh click. Progress decays 3% per second " +
                          "while not inflating. First to pop ranks first. Three " +
                          "30-second rounds.\nREADY {0} / 4",
                          readyCount)
                    : isGiftGrab
                        ? GameText.F("Ten gifts start; three more drop at 15, 30 and 45 " +
                          "seconds. Move with WASD. Touch a gift to carry one, " +
                          "then return it to your base or press LMB to throw it. " +
                          "Without a gift, LMB pushes and makes opponents drop " +
                          "theirs. Steal stored gifts from rival bases. Two " +
                          "60-second rounds; most stored gifts wins.\n" +
                          "READY {0} / 4",
                          readyCount)
                    : isTerritoryPaint
                        ? GameText.F("Move with WASD. Your circular trail paints the " +
                          "arena and can overwrite rival colors. The full " +
                          "arena is worth 1000 points. One 60-second round; " +
                          "highest current area wins.\nREADY {0} / 4",
                          readyCount)
                    : isTagChase
                        ? GameText.F("Each round assigns one player as the tagger. Runners " +
                          "move with WASD using a shared camera. The tagger moves " +
                          "with WASD in first person and presses LMB to catch. " +
                          "Every player tags once across four 60-second rounds.\n" +
                          "READY {0} / 4", readyCount)
                    : isRace
                        ? GameText.F("Alternate A and D to advance. Pressing the same key " +
                          "twice does not count. The first player to reach 500 " +
                          "steps ends the round. Three rounds, 60 seconds each.\n" +
                          "READY {0} / 4", readyCount)
                    : isSequenceMemory
                        ? GameText.F("Watch and listen to the shared A/S/D sequence, then " +
                          "repeat it after it is hidden. A is high, S is middle " +
                          "and D is low. A wrong key locks the current problem. " +
                          "Your first mistake removes your torso; your second " +
                          "eliminates you. Ten problems, one final placement, " +
                          "no per-problem score.\nREADY {0} / 4",
                          readyCount)
                    : isBouncingBalls
                        ? GameText.F("Move your goal shield with A and D. Three neutral balls " +
                          "launch from the center. Touching one claims your color; " +
                          "when it passes a shield into any goal, its color owner " +
                          "scores. A scored ball relaunches from the conceding " +
                          "player's shield in their color. Two 60-second rounds; " +
                          "highest combined score wins.\nREADY {0} / 4",
                          readyCount)
                    : isBombPassing
                        ? GameText.F("Move with WASD. Touch the center bomb to pick it up. " +
                          "Click a nearby player in front of you to pass it; " +
                          "the receiver is stunned for 0.5 seconds. Empty-hand " +
                          "click stuns a nearby player for 0.5 seconds. The " +
                          "fuse starts at spawn and lasts 20–25 seconds. " +
                          "At half time, an unheld bomb chases the nearest " +
                          "survivor. Only the carrier is eliminated when it " +
                          "explodes. Last survivor wins.\nREADY {0} / 4",
                          readyCount)
                    : isSnowySpin
                        ? GameText.F("Roll your colored ball with WASD. Holding a direction " +
                          "accelerates; colliding with other balls pushes them " +
                          "toward the edge. A fall eliminates you for that round. " +
                          "Three rounds, up to 60 seconds each. If time runs " +
                          "out, surviving balls nearer the center rank higher. " +
                          "Round placement points are combined; final placement " +
                          "awards gold once.\nREADY {0} / 4",
                          readyCount)
                    : isArenaCombat
                        ? GameText.F("Fight with WASD movement, mouse look and LMB punches. " +
                          "Health is hidden. Defeated players spectate. " +
                          "Last survivor wins, or remaining health decides " +
                          "survivors after 60 seconds.\nREADY {0} / 4",
                          readyCount)
                    : isCliffBarrage
                        ? GameText.F("Move with WASD and click to push the nearest rival " +
                          "in your last movement direction. Falling eliminates " +
                          "you immediately. A shell or laser hit first removes " +
                          "your torso; a second hit eliminates you, with one " +
                          "second of safety after a hit. Survive three 60-second " +
                          "rounds.\nREADY {0} / 4",
                          readyCount)
                    : isSkip
                        ? GameText.T("This queue slot has no available minigame. " +
                          "The next turn starts automatically.")
                        : (hasRuleImage ? string.Empty : GameText.T("RULE IMAGE PLACEHOLDER") + "\n") +
                          GameText.F("Stop and RMB to scan. First mine cripples; second eliminates. " +
                          "Reach the finish before the crusher.\n" +
                          "READY {0} / 4", readyCount));
            SetText(
                _minigameReadyStatus,
                revealPending
                    ? GameText.T("REVEALING...")
                    : isSkip
                        ? GameText.T("AUTO SKIP")
                        : match.FlowState == BoardFlowState.MinigameLoading
                            ? GameText.F("LOADING  {0}",
                              MinigameDisplayFormatter.FormatClock(match.StateRemaining))
                            : GameText.F("READY {0} / 4  AUTO START {1}", readyCount,
                              MinigameDisplayFormatter.FormatClock(match.StateRemaining)));

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
                    isReady
                        ? GameText.F("P{0}  READY", slot + 1)
                        : GameText.F("P{0}  WAITING", slot + 1));
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
                    ? GameText.T("WAIT...")
                    : isSkip
                        ? GameText.T("SKIPPING...")
                        : GameText.T("READY"));

            SetText(
                _minefieldResultTitle,
                isWrongWay
                    ? GameText.T("WRONG WAY RESULTS")
                    : isRedLightGreenLight
                        ? GameText.T("RED LIGHT / GREEN LIGHT RESULTS")
                    : isStableFooting
                        ? GameText.T("STABLE FOOTING RESULTS")
                    : isBalloonBlow
                        ? GameText.T("BALLOON BLOW RESULTS")
                    : isGiftGrab
                        ? GameText.T("GIFT GRAB RESULTS")
                    : isTerritoryPaint
                        ? GameText.T("TERRITORY PAINT RESULTS")
                    : isTagChase
                        ? GameText.T("TAG CHASE RESULTS")
                    : isRace
                        ? GameText.T("RACE RESULTS")
                    : isSequenceMemory
                        ? GameText.T("SEQUENCE MEMORY RESULTS")
                    : isBouncingBalls
                        ? GameText.T("BOUNCING BALLS RESULTS")
                    : isBombPassing
                        ? GameText.T("BOMB PASSING RESULTS")
                    : isSnowySpin
                        ? GameText.T("SNOWY SPIN RESULTS")
                    : isArenaCombat
                        ? GameText.T("ARENA COMBAT RESULTS")
                    : isCliffBarrage
                        ? GameText.T("CLIFF BARRAGE RESULTS")
                    : isSkip
                        ? GameText.T("TURN SKIPPED")
                        : GameText.T("MINEFIELD RESULTS"));
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
                    ? GameText.T("No minigame was scheduled for this turn.")
                    : BuildMinefieldResultSummary(NetworkMinefieldState.Instance);
            if (_minefieldResultSummary != null)
            {
                SetText(
                    _minefieldResultNote,
                    isSkip
                        ? GameText.T("No rewards are awarded for an empty queue slot.")
                        : GameText.F("Final placement awards {0} gold.",
                          MinigameRewardRules.FinalPlacementGoldSchedule));
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
                    ? GameText.T("RULES REVEALING...")
                    : isWrongWay
                    ? GameText.T("W  A  S  D\n50 STEPS")
                    : isRedLightGreenLight
                        ? GameText.T("GREEN: MOVE\nRED: FREEZE")
                    : isStableFooting
                        ? GameText.T("WASD: MOVE\nLMB: PUSH\nX  O  □")
                    : isBalloonBlow
                        ? GameText.T("HOLD LMB\nPOP FIRST")
                    : isGiftGrab
                        ? GameText.T("WASD: MOVE\nLMB: THROW / PUSH\nSTEAL GIFTS")
                    : isTerritoryPaint
                        ? GameText.T("WASD: MOVE\nPAINT THE ARENA")
                    : isTagChase
                        ? GameText.T("RUNNERS: WASD\nTAGGER: WASD + LMB")
                    : isRace
                        ? GameText.T("ALTERNATE A / D\n500 STEPS")
                    : isSequenceMemory
                        ? GameText.T("A: HIGH\nS: MIDDLE\nD: LOW")
                    : isBouncingBalls
                        ? GameText.T("A / D: MOVE SHIELD\nCLAIM BALLS · SCORE GOALS")
                    : isBombPassing
                        ? GameText.T("WASD: MOVE\nLMB: PASS / STUN\nSURVIVE THE BOMB")
                    : isSnowySpin
                        ? GameText.T("WASD: ROLL\nBUILD SPEED · PUSH BALLS OFF")
                    : isArenaCombat
                        ? GameText.T("WASD: MOVE\nMOUSE: LOOK\nLMB: PUNCH")
                    : isCliffBarrage
                        ? GameText.T("WASD: DODGE\nLMB: PUSH\nAVOID SHELLS / LASERS")
                        : GameText.T("RULE IMAGE"));
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
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null ? match.GetAvatarForSlot(rankedSlot) : null;
                builder.Append(GameText.F(
                    "{0}.  {1}  SCORE {2}  GOLD +{3}",
                    rank,
                    avatar != null ? avatar.DisplayName : "P" + (rankedSlot + 1),
                    minefield.GetScore(rankedSlot),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildWrongWayResultSummary(
            NetworkWrongWayState wrongWay)
        {
            if (wrongWay == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar =
                    match != null ? match.GetAvatarForSlot(rankedSlot) : null;
                builder.Append(GameText.F(
                    "{0}.  {1}  SCORE {2}  GOLD +{3}",
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    wrongWay.GetScore(rankedSlot),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildRedLightGreenLightResultSummary(
            NetworkRedLightGreenLightState redLightGreenLight)
        {
            if (redLightGreenLight == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar =
                    match != null ? match.GetAvatarForSlot(rankedSlot) : null;
                builder.Append(GameText.F(
                    "{0}.  {1}  SCORE {2}  GOLD +{3}",
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    redLightGreenLight.GetScore(rankedSlot),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildStableFootingResultSummary(
            NetworkStableFootingState stableFooting)
        {
            if (stableFooting == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar =
                    match != null ? match.GetAvatarForSlot(rankedSlot) : null;
                builder.Append(GameText.F(
                    "{0}.  {1}  SCORE {2}  GOLD +{3}",
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    stableFooting.GetScore(rankedSlot),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildBalloonBlowResultSummary(
            NetworkBalloonBlowState balloonBlow)
        {
            if (balloonBlow == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar =
                    match != null ? match.GetAvatarForSlot(rankedSlot) : null;
                builder.Append(GameText.F(
                    "{0}.  {1}  SCORE {2}  GOLD +{3}",
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    balloonBlow.GetScore(rankedSlot),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildGiftGrabResultSummary(
            NetworkGiftGrabState giftGrab)
        {
            if (giftGrab == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(GameText.F(
                    "{0}.  {1}  SCORE {2}  GIFTS {3}  GOLD +{4}",
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    giftGrab.GetScore(rankedSlot),
                    giftGrab.GetTotalStoredGiftCount(rankedSlot),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildTerritoryPaintResultSummary(
            NetworkTerritoryPaintState territoryPaint)
        {
            if (territoryPaint == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(GameText.F(
                    "{0}.  {1}  {2}  GOLD +{3}",
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    territoryPaint.GetScore(rankedSlot),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildTagChaseResultSummary(
            NetworkTagChaseState tagChase)
        {
            if (tagChase == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(GameText.F(
                    "{0}.  {1}  SCORE {2}  GOLD +{3}",
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    tagChase.GetTotalScore(rankedSlot),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildRaceResultSummary(
            NetworkRaceState race)
        {
            if (race == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(GameText.F(
                    "{0}.  {1}  SCORE {2}  GOLD +{3}",
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    race.GetTotalScore(rankedSlot),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildSnowySpinResultSummary(
            NetworkSnowySpinState snowySpin)
        {
            if (snowySpin == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(GameText.F(
                    "{0}.  {1}  SCORE {2}  GOLD +{3}",
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    snowySpin.GetScore(rankedSlot),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildArenaCombatResultSummary(
            NetworkArenaCombatState arenaCombat)
        {
            if (arenaCombat == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(GameText.F(
                    "{0}.  {1}  GOLD +{2}",
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildCliffBarrageResultSummary(
            NetworkCliffBarrageState cliffBarrage)
        {
            if (cliffBarrage == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(GameText.F(
                    "{0}.  {1}  SCORE {2}  GOLD +{3}",
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    cliffBarrage.GetScore(rankedSlot),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildBombPassingResultSummary(
            NetworkBombPassingState bombPassing)
        {
            if (bombPassing == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(GameText.F(
                    bombPassing.IsEliminated(rankedSlot)
                        ? GameText.N("{0}.  {1}  OUT  GOLD +{2}")
                        : GameText.N("{0}.  {1}  SURVIVED  GOLD +{2}"),
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildBouncingBallsResultSummary(
            NetworkBouncingBallsState bouncingBalls)
        {
            if (bouncingBalls == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(GameText.F(
                    "{0}.  {1}  GOALS {2}  CONCEDED {3}  GOLD +{4}",
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    bouncingBalls.GetScore(rankedSlot),
                    bouncingBalls.GetConceded(rankedSlot),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string BuildSequenceMemoryResultSummary(
            NetworkSequenceMemoryState sequenceMemory)
        {
            if (sequenceMemory == null)
            {
                return GameText.T("Final standings are synchronizing...");
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
                    return GameText.T("Final standings are synchronizing...");
                }
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var match = NetworkMatchState.Instance;
                var avatar = match != null
                    ? match.GetAvatarForSlot(rankedSlot)
                    : null;
                builder.Append(GameText.F(
                    sequenceMemory.IsPlayerEliminated(rankedSlot)
                        ? GameText.N("{0}.  {1}  OUT  GOLD +{2}")
                        : GameText.N("{0}.  {1}  SURVIVED  GOLD +{2}"),
                    rank,
                    avatar != null
                        ? avatar.DisplayName
                        : "P" + (rankedSlot + 1),
                    MinigameRewardRules.GetFinalPlacementGold(rank)));
            }

            return builder.ToString();
        }

        private static string MinefieldPhaseLabel(
            NetworkMinefieldState minefield)
        {
            if (minefield == null)
            {
                return GameText.T("MINEFIELD");
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
                    return GameText.F("MINEFIELD  ROUND {0} / {1}  -  COUNTDOWN",
                           round, totalRounds);
                case NetworkMinefieldPhase.Running:
                    return GameText.F("MINEFIELD  ROUND {0} / {1}  -  RUN",
                           round, totalRounds);
                case NetworkMinefieldPhase.RoundResult:
                    return GameText.F("MINEFIELD  ROUND {0} / {1}  -  RESULT",
                           round, totalRounds);
                case NetworkMinefieldPhase.Complete:
                    return GameText.T("MINEFIELD COMPLETE");
                default:
                    return GameText.T("MINEFIELD");
            }
        }

        private static string MinefieldStatus(NetworkMinefieldState minefield)
        {
            if (minefield == null)
            {
                return GameText.T("Synchronizing the Minefield simulation...");
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(ScheduledMinigameId.Minefield);
            switch (minefield.Phase)
            {
                case NetworkMinefieldPhase.Countdown:
                    return GameText.T("Get ready. Every player respawns and the mine layout changes each round.");
                case NetworkMinefieldPhase.Running:
                    return GameText.T("WASD moves in top view. Stop moving and press RMB to scan nearby mines.");
                case NetworkMinefieldPhase.RoundResult:
                    return GameText.T("Round points: 3 / 2 / 1 / 0. Finishers rank first; others rank by earliest elimination.");
                case NetworkMinefieldPhase.Complete:
                    return GameText.F("All {0} rounds complete. Final points determine rank; placement awards gold.",
                           totalRounds);
                default:
                    return GameText.T("Preparing Minefield...");
            }
        }

        private static string WrongWayPhaseLabel(
            NetworkWrongWayState wrongWay)
        {
            if (wrongWay == null)
            {
                return GameText.T("WRONG WAY");
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
                    return GameText.F("WRONG WAY  ROUND {0} / {1}  -  COUNTDOWN",
                           round, totalRounds);
                case NetworkWrongWayPhase.Running:
                    return GameText.F("WRONG WAY  ROUND {0} / {1}  -  CLIMB",
                           round, totalRounds);
                case NetworkWrongWayPhase.RoundResult:
                    return GameText.F("WRONG WAY  ROUND {0} / {1}  -  RESULT",
                           round, totalRounds);
                case NetworkWrongWayPhase.Complete:
                    return GameText.T("WRONG WAY COMPLETE");
                default:
                    return GameText.T("WRONG WAY");
            }
        }

        private static string WrongWayStatus(
            NetworkWrongWayState wrongWay)
        {
            if (wrongWay == null)
            {
                return GameText.T("Synchronizing the WrongWay race...");
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(ScheduledMinigameId.WrongWay);
            switch (wrongWay.Phase)
            {
                case NetworkWrongWayPhase.Countdown:
                    return GameText.T("Get ready. Every player receives the same direction sequence.");
                case NetworkWrongWayPhase.Running:
                    return GameText.T("Press the shown WASD direction. Wrong input locks you for 0.5 seconds.");
                case NetworkWrongWayPhase.RoundResult:
                    return GameText.T("Round points: 3 / 2 / 1 / 0. More stairs and earlier arrivals rank higher.");
                case NetworkWrongWayPhase.Complete:
                    return GameText.F("{0} rounds complete. Final points determine rank; placement awards gold.",
                           totalRounds);
                default:
                    return GameText.T("Preparing WrongWay...");
            }
        }

        private static string RedLightGreenLightPhaseLabel(
            NetworkRedLightGreenLightState redLightGreenLight)
        {
            if (redLightGreenLight == null)
            {
                return GameText.T("RED LIGHT / GREEN LIGHT");
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
                    return GameText.F("RED LIGHT / GREEN LIGHT  ROUND {0} / {1}  -  COUNTDOWN",
                           round, totalRounds);
                case NetworkRedLightGreenLightPhase.Running:
                    return GameText.F("RED LIGHT / GREEN LIGHT  ROUND {0} / {1}  -  {2}",
                           round, totalRounds,
                           RedLightGreenLightSignalLabel(
                               redLightGreenLight.SignalPhase));
                case NetworkRedLightGreenLightPhase.RoundResult:
                    return GameText.F("RED LIGHT / GREEN LIGHT  ROUND {0} / {1}  -  RESULT",
                           round, totalRounds);
                case NetworkRedLightGreenLightPhase.Complete:
                    return GameText.T("RED LIGHT / GREEN LIGHT COMPLETE");
                default:
                    return GameText.T("RED LIGHT / GREEN LIGHT");
            }
        }

        private static string RedLightGreenLightStatus(
            NetworkRedLightGreenLightState redLightGreenLight)
        {
            if (redLightGreenLight == null)
            {
                return GameText.T("Synchronizing the Red Light / Green Light race...");
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(
                    ScheduledMinigameId.RedLightGreenLight);
            switch (redLightGreenLight.Phase)
            {
                case NetworkRedLightGreenLightPhase.Countdown:
                    return GameText.T("Get ready at the shared start line.");
                case NetworkRedLightGreenLightPhase.Running:
                    var remaining =
                        redLightGreenLight.SignalRemaining;
                    switch (redLightGreenLight.SignalPhase)
                    {
                        case RedLightGreenLightSignalPhase.Green:
                            return GameText.F("GREEN  {0:0.0}s: move toward the finish.",
                                   remaining);
                        case RedLightGreenLightSignalPhase.OneRed:
                            return GameText.F("1 RED / 2 GREEN  {0:0.0}s: movement is still legal.",
                                   remaining);
                        case RedLightGreenLightSignalPhase.TwoRed:
                            return GameText.F("2 RED / 1 GREEN  {0:0.0}s: movement is still legal.",
                                   remaining);
                        case RedLightGreenLightSignalPhase.Red:
                            return GameText.F("RED  {0:0.0}s: freeze; voluntary movement is a violation.",
                                   remaining);
                        default:
                            return GameText.T("Follow the synchronized signal.");
                    }
                case NetworkRedLightGreenLightPhase.RoundResult:
                    return GameText.T("Finishers rank first, then survivors by forward " +
                           "progress, with eliminated players placed last.");
                case NetworkRedLightGreenLightPhase.Complete:
                    return GameText.F("All {0} rounds complete. Final points determine " +
                           "rank; placement awards gold.", totalRounds);
                default:
                    return GameText.T("Preparing Red Light / Green Light...");
            }
        }

        private static string RedLightGreenLightSignalLabel(
            RedLightGreenLightSignalPhase signal)
        {
            switch (signal)
            {
                case RedLightGreenLightSignalPhase.Green:
                    return GameText.T("GREEN");
                case RedLightGreenLightSignalPhase.OneRed:
                    return GameText.T("1 RED");
                case RedLightGreenLightSignalPhase.TwoRed:
                    return GameText.T("2 RED");
                case RedLightGreenLightSignalPhase.Red:
                    return GameText.T("RED");
                default:
                    return signal.ToString().ToUpperInvariant();
            }
        }

        private static string StableFootingPhaseLabel(
            NetworkStableFootingState stableFooting)
        {
            if (stableFooting == null)
            {
                return GameText.T("STABLE FOOTING");
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
                    return GameText.F("STABLE FOOTING  ROUND {0} / {1}  -  COUNTDOWN",
                           round, totalRounds);
                case NetworkStableFootingPhase.Running:
                    return GameText.F("STABLE FOOTING  ROUND {0} / {1}  -  {2}",
                           round, totalRounds,
                           GameText.T(stableFooting.CyclePhase.ToString().ToUpperInvariant()));
                case NetworkStableFootingPhase.RoundResult:
                    return GameText.F("STABLE FOOTING  ROUND {0} / {1}  -  RESULT",
                           round, totalRounds);
                case NetworkStableFootingPhase.Complete:
                    return GameText.T("STABLE FOOTING COMPLETE");
                default:
                    return GameText.T("STABLE FOOTING");
            }
        }

        private static string StableFootingStatus(
            NetworkStableFootingState stableFooting)
        {
            if (stableFooting == null)
            {
                return GameText.T("Synchronizing the Stable Footing arena...");
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(
                    ScheduledMinigameId.StableFooting);
            switch (stableFooting.Phase)
            {
                case NetworkStableFootingPhase.Countdown:
                    return GameText.T("Get ready on the shared 6 x 8 platform arena.");
                case NetworkStableFootingPhase.Running:
                    switch (stableFooting.CyclePhase)
                    {
                        case StableFootingCyclePhase.ShuffleReveal:
                            return GameText.T("Symbols are shuffling. Watch the shared safe-symbol display.");
                        case StableFootingCyclePhase.Move:
                            return GameText.T("WASD moves. LMB pushes the nearest player in front of you.");
                        case StableFootingCyclePhase.Drop:
                            return GameText.T("Unsafe platforms are dropping. Falling eliminates immediately.");
                        case StableFootingCyclePhase.Restore:
                            return GameText.T("Platforms are returning; two remain permanently removed.");
                        default:
                            return GameText.T("Stay on the announced safe symbol.");
                    }
                case NetworkStableFootingPhase.RoundResult:
                    return GameText.T("The last survivor ranks first; later falls rank above earlier falls.");
                case NetworkStableFootingPhase.Complete:
                    return GameText.F("All {0} rounds complete. Final points determine rank; placement awards gold.",
                           totalRounds);
                default:
                    return GameText.T("Preparing Stable Footing...");
            }
        }

        private static string BalloonBlowPhaseLabel(
            NetworkBalloonBlowState balloonBlow)
        {
            if (balloonBlow == null)
            {
                return GameText.T("BALLOON BLOW");
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
                    return GameText.F("BALLOON BLOW  ROUND {0} / {1}  -  COUNTDOWN",
                           round, totalRounds);
                case NetworkBalloonBlowPhase.Running:
                    return GameText.F("BALLOON BLOW  ROUND {0} / {1}  -  INFLATE",
                           round, totalRounds);
                case NetworkBalloonBlowPhase.RoundResult:
                    return GameText.F("BALLOON BLOW  ROUND {0} / {1}  -  RESULT",
                           round, totalRounds);
                case NetworkBalloonBlowPhase.Complete:
                    return GameText.T("BALLOON BLOW COMPLETE");
                default:
                    return GameText.T("BALLOON BLOW");
            }
        }

        private static string BalloonBlowStatus(
            NetworkBalloonBlowState balloonBlow)
        {
            if (balloonBlow == null)
            {
                return GameText.T("Synchronizing the Balloon Blow arena...");
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(
                    ScheduledMinigameId.BalloonBlow);
            switch (balloonBlow.Phase)
            {
                case NetworkBalloonBlowPhase.Countdown:
                    return GameText.T("Get ready. Hold LMB after the countdown to inflate.");
                case NetworkBalloonBlowPhase.Running:
                    return GameText.T("Hold LMB to inflate. Release before two seconds; " +
                           "idle and cooldown time slowly deflate your balloon.");
                case NetworkBalloonBlowPhase.RoundResult:
                    return GameText.T("Popped balloons rank by pop order; remaining balloons " +
                           "rank by progress, then server player order.");
                case NetworkBalloonBlowPhase.Complete:
                    return GameText.F("All {0} rounds complete. Final points determine rank; placement awards gold.",
                           totalRounds);
                default:
                    return GameText.T("Preparing Balloon Blow...");
            }
        }

        private static string GiftGrabPhaseLabel(
            NetworkGiftGrabState giftGrab)
        {
            if (giftGrab == null)
            {
                return GameText.T("GIFT GRAB");
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
                    return GameText.F("GIFT GRAB  ROUND {0} / {1}  -  COUNTDOWN",
                           round, totalRounds);
                case NetworkGiftGrabPhase.Running:
                    return GameText.F("GIFT GRAB  ROUND {0} / {1}  -  STEAL",
                           round, totalRounds);
                case NetworkGiftGrabPhase.RoundResult:
                    return GameText.F("GIFT GRAB  ROUND {0} / {1}  -  RESULT",
                           round, totalRounds);
                case NetworkGiftGrabPhase.Complete:
                    return GameText.T("GIFT GRAB COMPLETE");
                default:
                    return GameText.T("GIFT GRAB");
            }
        }

        private static string TerritoryPaintPhaseLabel(
            NetworkTerritoryPaintState territoryPaint)
        {
            if (territoryPaint == null)
            {
                return GameText.T("TERRITORY PAINT");
            }

            switch (territoryPaint.Phase)
            {
                case NetworkTerritoryPaintPhase.Countdown:
                    return GameText.T("TERRITORY PAINT  -  COUNTDOWN");
                case NetworkTerritoryPaintPhase.Running:
                    return GameText.T("TERRITORY PAINT");
                case NetworkTerritoryPaintPhase.RoundResult:
                    return GameText.T("TERRITORY PAINT  -  RESULT");
                case NetworkTerritoryPaintPhase.Complete:
                    return GameText.T("TERRITORY PAINT COMPLETE");
                default:
                    return GameText.T("TERRITORY PAINT");
            }
        }

        private static string TagChasePhaseLabel(
            NetworkTagChaseState tagChase)
        {
            if (tagChase == null)
            {
                return GameText.T("TAG CHASE");
            }

            var round = Mathf.Clamp(
                tagChase.RoundNumber,
                1,
                TagChaseRules.RoundCount);
            switch (tagChase.Phase)
            {
                case NetworkTagChasePhase.Countdown:
                    return GameText.F("TAG CHASE  ROUND {0} / {1}  -  COUNTDOWN",
                           round, TagChaseRules.RoundCount);
                case NetworkTagChasePhase.Running:
                    return GameText.F("TAG CHASE  ROUND {0} / {1}  -  CHASE",
                           round, TagChaseRules.RoundCount);
                case NetworkTagChasePhase.RoundResult:
                    return GameText.F("TAG CHASE  ROUND {0} / {1}  -  RESULT",
                           round, TagChaseRules.RoundCount);
                case NetworkTagChasePhase.Complete:
                    return GameText.T("TAG CHASE COMPLETE");
                default:
                    return GameText.T("TAG CHASE");
            }
        }

        private static string TagChaseStatus(
            NetworkTagChaseState tagChase)
        {
            if (tagChase == null)
            {
                return GameText.T("Synchronizing the Tag Chase arena...");
            }

            var taggerLabel = tagChase.TaggerSlot >= 0
                ? "P" + (tagChase.TaggerSlot + 1)
                : GameText.T("The selected player");
            switch (tagChase.Phase)
            {
                case NetworkTagChasePhase.Countdown:
                    return GameText.F("{0} is the tagger. Get ready for the chase.",
                           taggerLabel);
                case NetworkTagChasePhase.Running:
                    return GameText.T("Runners escape with WASD on the shared camera. " +
                           "The tagger uses WASD and LMB in first person.");
                case NetworkTagChasePhase.RoundResult:
                    return GameText.T("The tagger earns 3 points only after catching every " +
                           "runner; surviving and caught runners score separately.");
                case NetworkTagChasePhase.Complete:
                    return GameText.F("All {0} rounds complete. Total points determine rank; " +
                           "placement awards gold.", TagChaseRules.RoundCount);
                default:
                    return GameText.T("Preparing Tag Chase...");
            }
        }

        private static string RacePhaseLabel(NetworkRaceState race)
        {
            if (race == null)
            {
                return GameText.T("RACE");
            }

            var round = Mathf.Clamp(
                race.RoundNumber,
                1,
                RaceRules.RoundCount);
            switch (race.Phase)
            {
                case NetworkRacePhase.Countdown:
                    return GameText.F("RACE  ROUND {0} / {1}  -  COUNTDOWN",
                           round, RaceRules.RoundCount);
                case NetworkRacePhase.Running:
                    return GameText.F("RACE  ROUND {0} / {1}  -  RUN",
                           round, RaceRules.RoundCount);
                case NetworkRacePhase.RoundResult:
                    return GameText.F("RACE  ROUND {0} / {1}  -  RESULT",
                           round, RaceRules.RoundCount);
                case NetworkRacePhase.Complete:
                    return GameText.T("RACE COMPLETE");
                default:
                    return GameText.T("RACE");
            }
        }

        private static string RaceStatus(NetworkRaceState race)
        {
            if (race == null)
            {
                return GameText.T("Synchronizing the Race arena...");
            }

            switch (race.Phase)
            {
                case NetworkRacePhase.Countdown:
                    return GameText.T("Get ready to alternate A and D.");
                case NetworkRacePhase.Running:
                    return GameText.T("Alternate A and D. Repeating the same key does not " +
                           "advance; first to 500 steps ends the round.");
                case NetworkRacePhase.RoundResult:
                    return GameText.T("More steps rank higher; server input order breaks " +
                           "equal-progress ties.");
                case NetworkRacePhase.Complete:
                    return GameText.F("All {0} rounds complete. Total points determine rank; " +
                           "placement awards gold.", RaceRules.RoundCount);
                default:
                    return GameText.T("Preparing Race...");
            }
        }

        private static string BouncingBallsPhaseLabel(
            NetworkBouncingBallsState bouncingBalls)
        {
            if (bouncingBalls == null)
            {
                return GameText.T("BOUNCING BALLS");
            }

            var round = Mathf.Clamp(
                bouncingBalls.RoundNumber,
                1,
                BouncingBallsRules.RoundCount);
            switch (bouncingBalls.Phase)
            {
                case NetworkBouncingBallsPhase.Countdown:
                    return GameText.F("BOUNCING BALLS  ROUND {0} / {1}  -  COUNTDOWN",
                           round, BouncingBallsRules.RoundCount);
                case NetworkBouncingBallsPhase.Playing:
                    return GameText.F("BOUNCING BALLS  ROUND {0} / {1}  -  PLAY",
                           round, BouncingBallsRules.RoundCount);
                case NetworkBouncingBallsPhase.RoundBreak:
                    return GameText.F("BOUNCING BALLS  ROUND {0} / {1}  -  RESULT",
                           round, BouncingBallsRules.RoundCount);
                case NetworkBouncingBallsPhase.Complete:
                    return GameText.T("BOUNCING BALLS COMPLETE");
                default:
                    return GameText.T("BOUNCING BALLS");
            }
        }

        private static string BouncingBallsStatus(
            NetworkBouncingBallsState bouncingBalls)
        {
            if (bouncingBalls == null)
            {
                return GameText.T("Synchronizing the Bouncing Balls arena...");
            }

            switch (bouncingBalls.Phase)
            {
                case NetworkBouncingBallsPhase.Countdown:
                    return GameText.T("Three neutral balls will launch from the center.");
                case NetworkBouncingBallsPhase.Playing:
                    return GameText.T("Hold A or D to slide your shield. A touched ball " +
                           "takes your color; a goal scores for its color owner.");
                case NetworkBouncingBallsPhase.RoundBreak:
                    return GameText.T("Round over. Combined goals across both rounds " +
                           "determine final placement.");
                case NetworkBouncingBallsPhase.Complete:
                    return GameText.F("Two rounds complete. Final placement awards {0} gold.",
                           MinigameRewardRules.FinalPlacementGoldSchedule);
                default:
                    return GameText.T("Preparing Bouncing Balls...");
            }
        }

        private static string SequenceMemoryPhaseLabel(
            NetworkSequenceMemoryState sequenceMemory)
        {
            if (sequenceMemory == null)
            {
                return GameText.T("SEQUENCE MEMORY");
            }

            var round = Mathf.Clamp(
                sequenceMemory.RoundNumber,
                1,
                SequenceMemoryRules.RoundCount);
            switch (sequenceMemory.Phase)
            {
                case NetworkSequenceMemoryPhase.Countdown:
                    return GameText.T("SEQUENCE MEMORY  -  COUNTDOWN");
                case NetworkSequenceMemoryPhase.PresentingProblem:
                    return GameText.F("SEQUENCE MEMORY  PROBLEM {0} / {1}  -  WATCH",
                           round, SequenceMemoryRules.RoundCount);
                case NetworkSequenceMemoryPhase.AcceptingInput:
                    return GameText.F("SEQUENCE MEMORY  PROBLEM {0} / {1}  -  INPUT",
                           round, SequenceMemoryRules.RoundCount);
                case NetworkSequenceMemoryPhase.RevealingAnswer:
                    return GameText.F("SEQUENCE MEMORY  PROBLEM {0} / {1}  -  ANSWER",
                           round, SequenceMemoryRules.RoundCount);
                case NetworkSequenceMemoryPhase.Complete:
                    return GameText.T("SEQUENCE MEMORY COMPLETE");
                default:
                    return GameText.T("SEQUENCE MEMORY");
            }
        }

        private static string SequenceMemoryStatus(
            NetworkSequenceMemoryState sequenceMemory)
        {
            if (sequenceMemory == null)
            {
                return GameText.T("Synchronizing the Sequence Memory game...");
            }

            switch (sequenceMemory.Phase)
            {
                case NetworkSequenceMemoryPhase.Countdown:
                    return GameText.T("Get ready. The NPC will play one shared A/S/D sequence.");
                case NetworkSequenceMemoryPhase.PresentingProblem:
                    return GameText.T("Watch and listen: A is high, S is middle and D is low.");
                case NetworkSequenceMemoryPhase.AcceptingInput:
                    return GameText.T("Repeat the hidden sequence with A, S and D. A wrong key locks this problem immediately.");
                case NetworkSequenceMemoryPhase.RevealingAnswer:
                    return GameText.T("The answer is visible. One mistake loses the torso; the second eliminates.");
                case NetworkSequenceMemoryPhase.Complete:
                    return GameText.T("The single match is complete. Placement awards 10 / 6 / 3 / 0 gold.");
                default:
                    return GameText.T("Preparing Sequence Memory...");
            }
        }

        private static string TerritoryPaintStatus(
            NetworkTerritoryPaintState territoryPaint)
        {
            if (territoryPaint == null)
            {
                return GameText.T("Synchronizing the Territory Paint arena...");
            }

            switch (territoryPaint.Phase)
            {
                case NetworkTerritoryPaintPhase.Countdown:
                    return GameText.T("Get ready at your corner.");
                case NetworkTerritoryPaintPhase.Running:
                    return GameText.T("WASD moves and continuously paints a circular trail.");
                case NetworkTerritoryPaintPhase.RoundResult:
                    return GameText.T("Current owned area decides the final score.");
                case NetworkTerritoryPaintPhase.Complete:
                    return GameText.T("Territory Paint complete.");
                default:
                    return GameText.T("Preparing Territory Paint...");
            }
        }

        private static string GiftGrabStatus(NetworkGiftGrabState giftGrab)
        {
            if (giftGrab == null)
            {
                return GameText.T("Synchronizing the Gift Grab arena...");
            }

            var totalRounds =
                MinigameCatalog.GetRoundCount(ScheduledMinigameId.GiftGrab);
            switch (giftGrab.Phase)
            {
                case NetworkGiftGrabPhase.Countdown:
                    return GameText.T("Get ready. Ten gifts begin in the shared arena.");
                case NetworkGiftGrabPhase.Running:
                    return GameText.T("WASD moves. Carry gifts home, throw while carrying, " +
                           "or push while empty-handed. Three gifts drop every 15 seconds.");
                case NetworkGiftGrabPhase.RoundResult:
                    return GameText.T("Stored gifts decide the round; gift ownership time breaks ties.");
                case NetworkGiftGrabPhase.Complete:
                    return GameText.F("{0} rounds complete. Points, total stored gifts, " +
                           "final-round rank, then server player order " +
                           "determine placement.", totalRounds);
                default:
                    return GameText.T("Preparing Gift Grab...");
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
            RefreshReticleColor(false);
            CloseItemShop();
            SetText(_turnText, GameText.T("TURN --"));
            SetText(_phaseText, GameText.T("WAITING FOR 4 PLAYERS"));
            SetText(_phaseTimerText, "--:--");
            cameraDirector?.SetUiPointerVisible(true);
        }

        private void RefreshReticleColor(bool visible)
        {
            if (_reticleText == null || uiBindings == null)
            {
                return;
            }

            _reticleText.color =
                visible &&
                _localAvatar != null &&
                _localAvatar.HasLocalDamageableFirearmTarget()
                    ? uiBindings.ReticleDamageableTargetColor
                    : uiBindings.ReticleDefaultColor;
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

        private static void SetItemIcon(Image target, Sprite sprite, Color color)
        {
            if (target == null)
            {
                return;
            }

            target.sprite = sprite;
            target.color = color;
            target.enabled = sprite != null;
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
                case BoardFlowState.TurnOverview: return GameText.T("BOARD OVERVIEW");
                case BoardFlowState.Descending: return GameText.T("DESCENDING");
                case BoardFlowState.Action: return GameText.T("FIRST-PERSON ACTION");
                case BoardFlowState.AscendingResolve: return GameText.T("RESOLVING / ASCENDING");
                case BoardFlowState.CombatResolve: return GameText.T("COMBAT QUEUE");
                case BoardFlowState.LandingEffectResolve: return GameText.T("LANDING EFFECTS");
                case BoardFlowState.MinigameIntroReady: return GameText.T("MINIGAME READY");
                case BoardFlowState.MinigameLoading: return GameText.T("LOADING MINIGAME");
                case BoardFlowState.MinigamePlaying: return GameText.T("MINIGAME");
                case BoardFlowState.MinigameResult: return GameText.T("MINIGAME RESULTS");
                case BoardFlowState.MatchComplete: return GameText.T("MATCH COMPLETE");
                default: return state.ToString().ToUpperInvariant();
            }
        }

        private static string MinigameName(ScheduledMinigameId minigame)
        {
            return GameText.T(MinigameCatalog.GetDisplayName(minigame));
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
                case ItemChoiceResolution.ItemSelected: return GameText.T("ITEM ACTIVE");
                case ItemChoiceResolution.DoNotUse: return GameText.T("DO NOT USE");
                case ItemChoiceResolution.TimedOut: return GameText.T("TIMEOUT / NO ITEM");
                case ItemChoiceResolution.Pending: return GameText.T("PENDING");
                default: return "--";
            }
        }

        private static string ActionIconLabel(PlayerBoardActionState state)
        {
            switch (state)
            {
                case PlayerBoardActionState.Dice: return GameText.T("DICE");
                case PlayerBoardActionState.Moving: return GameText.T("MOVE");
                case PlayerBoardActionState.Arrived: return GameText.T("ARRIVED");
                case PlayerBoardActionState.Fighting: return GameText.T("FIGHT");
                default: return string.Empty;
            }
        }

    }
}

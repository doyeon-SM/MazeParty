using System;
using System.Collections.Generic;
using System.Text;
using DG.Tweening;
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
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Runtime uGUI/Canvas presentation for the online board flow. All visible copy
    /// intentionally remains English until the project's Korean font is installed.
    /// </summary>
    public sealed class BoardFlowView : MonoBehaviour
    {
        internal const float BoardKillFeedEntrySeconds = 2f;
        internal const float BoardKillFeedBatchSeconds = 0.1f;
        private const char BoardKillItemMarker = '\uE000';
        private const string MoveKeys =
            "<sprite name=\"w\"><sprite name=\"a\">" +
            "<sprite name=\"s\"><sprite name=\"d\">";
        private static readonly Color AvailableItemIconColor = Color.white;
        private static readonly Color SoldItemIconColor =
            new Color(0.45f, 0.45f, 0.45f, 0.45f);
        private static readonly string[][] MinigameControlGuides =
        {
            Array.Empty<string>(),
            new[] { MoveKeys + "  MOVE", "<sprite name=\"mr\">  SCAN" },
            new[] { MoveKeys + "  PRESS SHOWN KEY" },
            new[] { MoveKeys + "  MOVE / STOP" },
            new[] { MoveKeys + "  MOVE", "<sprite name=\"ml\">  PUSH" },
            new[] { "<sprite name=\"ml\">  HOLD / RELEASE" },
            new[] { MoveKeys + "  MOVE", "<sprite name=\"ml\">  THROW / PUSH" },
            new[] { MoveKeys + "  MOVE / PAINT" },
            new[]
            {
                MoveKeys + "  MOVE",
                "<sprite name=\"mouse\">  TAGGER LOOK",
                "<sprite name=\"ml\">  TAGGER CATCH"
            },
            new[] { "<sprite name=\"a\"><sprite name=\"d\">  ALTERNATE" },
            new[] { "<sprite name=\"a\"><sprite name=\"s\"><sprite name=\"d\">  REPEAT" },
            new[] { "<sprite name=\"a\"><sprite name=\"d\">  MOVE SHIELD" },
            new[] { MoveKeys + "  MOVE", "<sprite name=\"ml\">  PASS / STUN" },
            new[] { MoveKeys + "  ROLL" },
            new[]
            {
                MoveKeys + "  MOVE",
                "<sprite name=\"mouse\">  LOOK",
                "<sprite name=\"ml\">  PUNCH"
            },
            new[] { MoveKeys + "  MOVE", "<sprite name=\"ml\">  PUSH" }
        };

        private ScheduledMinigameId _renderedControlMinigame;
        private GameLanguage _renderedControlLanguage;
        private bool _renderedControlHidden;
        private bool _controlGuideInitialized;

        [SerializeField] private GameplayCameraDirector cameraDirector;
        [SerializeField] private BoardCanvasBindings uiBindings;
        [SerializeField] private MinigameResultCanvasBindings resultUiBindings;

        private readonly Image[] _slotBackgrounds = new Image[GameplayInventory.Capacity];
        private readonly Image[] _slotIcons = new Image[GameplayInventory.Capacity];
        private readonly Button[] _choiceButtons = new Button[GameplayInventory.Capacity];
        private readonly Text[] _choiceLabels = new Text[GameplayInventory.Capacity];
        private readonly Text[] _playerRows = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Image[] _playerCards = new Image[MultiplayerConstants.MaxPlayers];
        private readonly Slider[] _playerHealthSliders =
            new Slider[MultiplayerConstants.MaxPlayers];
        private readonly Image[] _playerHealthFills = new Image[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerHealthTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerHealthDeltaTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerKeyTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerKeyDeltaTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerGoldTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerGoldDeltaTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Tween[] _healthTweens = new Tween[MultiplayerConstants.MaxPlayers];
        private readonly int[] _observedHealth = new int[MultiplayerConstants.MaxPlayers];
        private readonly int[] _observedKeys = new int[MultiplayerConstants.MaxPlayers];
        private readonly int[] _observedGold = new int[MultiplayerConstants.MaxPlayers];
        private readonly float[,] _deltaExpiresAt =
            new float[MultiplayerConstants.MaxPlayers, 3];
        private readonly bool[] _observedStats = new bool[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerActionIcons = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _playerRankTexts = new Text[MultiplayerConstants.MaxPlayers];
        private readonly Text[] _minigameReadyPlayerStates =
            new Text[MultiplayerConstants.MaxPlayers];
        private readonly TMP_Text[] _minigameControlRows = new TMP_Text[4];
        private readonly Button[] _shopOfferButtons = new Button[ItemShopRules.OfferCount];
        private readonly Image[] _shopOfferIcons = new Image[ItemShopRules.OfferCount];
        private readonly Text[] _shopOfferLabels = new Text[ItemShopRules.OfferCount];
        private readonly List<BoardKillFeedEntry> _pendingBoardKills =
            new List<BoardKillFeedEntry>();

        private GameObject _selectionPanel;
        private GameObject _readyPanel;
        private GameObject _resultPanel;
        private GameObject _reconnectOverlay;
        private GameObject _reticle;
        private GameObject _itemShopPanel;
        private GameObject _boardEventPopupPanel;
        private GameObject _boardKillFeedPanel;
        private BoardKillFeedView _boardKillFeedView;
        private Canvas _hudCanvas;
        private GraphicRaycaster _hudRaycaster;
        private Canvas _mapCanvas;
        private GraphicRaycaster _mapRaycaster;
        private Canvas _itemCanvas;
        private GraphicRaycaster _itemRaycaster;
        private Text _turnText;
        private Text _phaseText;
        private Text _phaseTimerText;
        private Image _reticleImage;
        private Text _movesText;
        private Text _ammoText;
        private Text _interactionPrompt;
        private Text _tooltipText;
        private Text _reconnectText;
        private Text _itemShopTitle;
        private Text _itemShopTooltip;
        private Text _itemShopStatus;
        private TMP_Text _boardEventPopupMessage;
        private Text _boardKillFeedMessage;
        private Text _minigameReadyTitle;
        private Text _minigameDescription;
        private Text _minigameReadyStatus;
        private Text _minigamePreviewPlaceholder;
        private Text _minefieldResultTitle;
        private Text _minefieldResultNote;
        private Text _minefieldResultSummary;
        private Text _readyButtonLabel;
        private RawImage _minigamePreviewImage;
        private VideoPlayer _minigamePreviewPlayer;
        private Button _noItemButton;
        private Button _readyButton;
        private Button _itemShopCloseButton;
        private NetworkPlayerAvatar _localAvatar;
        private readonly BoardSoundFeedback _soundFeedback = new BoardSoundFeedback();
        private KeyShopWorldMarker _keyShopMarker;
        private ItemShopWorldMarker _itemShopMarker;
        private BoardTopology _topology;
        private int _lastBoardEffectRevision = -1;
        private int _observedMinigameRevealRevision = -1;
        private float _minigameRevealObservedAt;
        private bool _wired;
        private int _openItemShopIndex = -1;
        private bool _topViewShopHighlightsVisible;
        private float _boardKillFeedVisibleUntil;
        private VideoClip _activeMinigamePreviewClip;

        public static BoardFlowView Instance { get; private set; }
        public static bool IsItemShopOpen =>
            Instance != null && Instance._openItemShopIndex >= 0;
        public bool BoardUiVisible =>
            (_hudCanvas == null || _hudCanvas.enabled) &&
            (_mapCanvas == null || _mapCanvas.enabled) &&
            (_itemCanvas == null || _itemCanvas.enabled);
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
            if (!visible && _openItemShopIndex >= 0)
            {
                CloseItemShop();
            }

            ResolveBoardCanvasRoots();
            SetCanvasVisible(_hudCanvas, _hudRaycaster, visible);
            SetCanvasVisible(_mapCanvas, _mapRaycaster, visible);
            SetCanvasVisible(_itemCanvas, _itemRaycaster, visible);
            if (!visible)
            {
                uiBindings?.Map?.MapView?.CloseFullMap();
            }
        }

        private void ResolveBoardCanvasRoots()
        {
            if (uiBindings == null)
            {
                return;
            }

            var hud = uiBindings.Hud;
            var map = uiBindings.Map;
            var item = uiBindings.Item;
            _hudCanvas = hud != null ? hud.RootCanvas : null;
            _hudRaycaster = hud != null ? hud.RootRaycaster : null;
            _mapCanvas = map != null ? map.RootCanvas : null;
            _mapRaycaster = map != null ? map.RootRaycaster : null;
            _itemCanvas = item != null ? item.RootCanvas : null;
            _itemRaycaster = item != null ? item.RootRaycaster : null;
        }

        private static void SetCanvasVisible(
            Canvas canvas,
            GraphicRaycaster raycaster,
            bool visible)
        {
            if (canvas != null)
            {
                canvas.enabled = visible;
            }
            if (raycaster != null)
            {
                raycaster.enabled = visible;
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
            StopMinigamePreview();
            UnwireMinigamePreview();
            UnwireButtons();
            UiPopupStack.Remove(_itemShopPanel);
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void EnqueueBoardKillNotification(
            int killerSlot,
            int victimSlot,
            PrototypeItemId itemId,
            PlayerRankingStats victimRankingStats)
        {
            if (killerSlot < 0 || killerSlot >= MultiplayerConstants.MaxPlayers ||
                victimSlot < 0 || victimSlot >= MultiplayerConstants.MaxPlayers ||
                killerSlot == victimSlot || uiBindings == null)
            {
                return;
            }

            var match = NetworkMatchState.Instance;
            var killer = match != null ? match.GetAvatarForSlot(killerSlot) : null;
            var victim = match != null ? match.GetAvatarForSlot(victimSlot) : null;
            _pendingBoardKills.Add(new BoardKillFeedEntry(
                ResolveBoardPlayerName(killer, killerSlot),
                ResolveBoardPlayerTextColor(killer, killerSlot),
                ResolveBoardPlayerName(victim, victimSlot),
                ResolveBoardPlayerTextColor(victim, victimSlot),
                itemId,
                victimSlot,
                victimRankingStats,
                Time.unscaledTime));
        }

        private void OnDisable()
        {
            for (var slot = 0; slot < _healthTweens.Length; slot++)
            {
                _healthTweens[slot]?.Kill();
                _healthTweens[slot] = null;
                _observedStats[slot] = false;
            }
            StopMinigamePreview();
            CloseItemShop();
            SetActive(_boardEventPopupPanel, false);
            SetText(_boardEventPopupMessage, string.Empty);
            ResetBoardKillFeed();
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
            RefreshBoardKillFeed();
            RefreshHeader(match);
            RefreshLocalPlayer(match);
            RefreshInventory();
            RefreshInteractionPrompt();
            RefreshPlayerRows(match);
            RefreshBoardEffects(match);
            RefreshKeyShop(match);
            RefreshItemShops(match);
            RefreshItemShopPanel(match);
            RefreshCursor(match);
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
            UiPopupStack.Push(_itemShopPanel, CloseItemShop);
            HideShopItemTooltip();
            SetText(_itemShopStatus, _localAvatar.HasFreeItemSlot
                ? GameText.T("Select an available item to buy it immediately.")
                : GameText.T("INVENTORY FULL - Browse only; purchases are disabled."));
        }

        public void CloseItemShop()
        {
            _openItemShopIndex = -1;
            SetActive(_itemShopPanel, false);
            UiPopupStack.Remove(_itemShopPanel);
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

            UnwireMinigamePreview();
            ResolveBoardCanvasRoots();
            var hud = uiBindings.Hud;
            var map = uiBindings.Map;
            var item = uiBindings.Item;
            _selectionPanel = item.ItemSelectionPanel;
            _readyPanel = hud.MinigameReadyPanel;
            _reconnectOverlay = hud.ReconnectOverlay;
            _reticle = hud.Reticle;
            _itemShopPanel = item.ItemShopPanel;
            _boardEventPopupPanel = hud.BoardEventPopupPanel;
            _boardKillFeedPanel = hud.BoardKillFeedPanel;
            _boardKillFeedView = _boardKillFeedPanel != null
                ? _boardKillFeedPanel.GetComponent<BoardKillFeedView>()
                : null;
            _reticleImage = hud.ReticleImage;
            _turnText = hud.TurnText;
            _phaseText = hud.PhaseText;
            _phaseTimerText = hud.PhaseTimerText;
            _movesText = map.MovesText;
            _ammoText = item.AmmoText;
            _interactionPrompt = item.InteractionPrompt;
            _tooltipText = item.TooltipText;
            _reconnectText = hud.ReconnectText;
            _itemShopTitle = item.ItemShopTitle;
            _itemShopTooltip = item.ItemShopTooltip;
            _itemShopStatus = item.ItemShopStatus;
            _boardEventPopupMessage = hud.BoardEventPopupMessage;
            _boardKillFeedMessage = hud.BoardKillFeedMessage;
            _minigameReadyTitle = hud.MinigameReadyTitle;
            _minigameDescription = hud.MinigameDescription;
            _minigameReadyStatus = hud.MinigameReadyStatus;
            _minigamePreviewPlaceholder =
                hud.MinigamePreviewPlaceholder;
            _minigamePreviewImage = hud.MinigamePreviewImage;
            _minigamePreviewPlayer = hud.MinigamePreviewPlayer;
            WireMinigamePreview();
            _noItemButton = item.NoItemButton;
            _readyButton = hud.ReadyButton;
            _readyButtonLabel = hud.ReadyButtonLabel;
            _itemShopCloseButton = item.ItemShopCloseButton;
            BindResultUi();

            CopyReferences(
                item.InventorySlotBackgrounds,
                _slotBackgrounds);
            CopyReferences(item.InventorySlotIcons, _slotIcons);
            CopyReferences(item.ItemChoiceButtons, _choiceButtons);
            CopyReferences(item.ItemChoiceLabels, _choiceLabels);
            CopyReferences(item.ShopOfferButtons, _shopOfferButtons);
            CopyReferences(item.ShopOfferIcons, _shopOfferIcons);
            CopyReferences(item.ShopOfferLabels, _shopOfferLabels);
            CopyReferences(hud.PlayerRows, _playerRows);
            CopyReferences(hud.PlayerCards, _playerCards);
            CopyReferences(hud.PlayerHealthSliders, _playerHealthSliders);
            CopyReferences(hud.PlayerHealthFills, _playerHealthFills);
            CopyReferences(hud.PlayerHealthTexts, _playerHealthTexts);
            CopyReferences(hud.PlayerHealthDeltaTexts, _playerHealthDeltaTexts);
            CopyReferences(hud.PlayerKeyTexts, _playerKeyTexts);
            CopyReferences(hud.PlayerKeyDeltaTexts, _playerKeyDeltaTexts);
            CopyReferences(hud.PlayerGoldTexts, _playerGoldTexts);
            CopyReferences(hud.PlayerGoldDeltaTexts, _playerGoldDeltaTexts);
            CopyReferences(hud.PlayerActionIcons, _playerActionIcons);
            CopyReferences(hud.PlayerRankTexts, _playerRankTexts);
            CopyReferences(
                hud.MinigameReadyPlayerStates,
                _minigameReadyPlayerStates);
            CopyReferences(
                hud.MinigameControlRows,
                _minigameControlRows);

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
            var showMinefieldReady = ShouldShowMinigameReadyPresentation(
                match.FlowState,
                match.IsGlobalSimulationPaused,
                match.IsReconnectPaused);
            SetActive(_selectionPanel, choicePending);
            SetActive(_readyPanel, showMinefieldReady);
            SetActive(_resultPanel,
                match.FlowState == BoardFlowState.MinigameResult &&
                !match.IsGlobalSimulationPaused);
            SetActive(_reconnectOverlay, match.IsReconnectPaused);
            var landingEffectMessage = match.LastLandingEffectMessage;
            var showBoardEventPopup = ShouldShowBoardEventPopup(
                match.FlowState,
                match.IsGlobalSimulationPaused,
                match.LastLandingEffectType,
                landingEffectMessage,
                match.ResourceTransferPresentationPhase);
            SetText(
                _boardEventPopupMessage,
                showBoardEventPopup
                    ? LandingEffectMessage.FormatEventPopup(
                        match.LastLandingEffectEncodedMessage,
                        slot => FormatMinigameResultPlayerName(match.GetAvatarForSlot(slot), slot))
                    : string.Empty);
            SetActive(_boardEventPopupPanel, showBoardEventPopup);
            var showReticle =
                ((match.FlowState == BoardFlowState.Action && !choicePending &&
                  !IsItemShopOpen) ||
                 (match.IsCombatPhase && _localAvatar != null &&
                  match.IsCombatParticipant(_localAvatar.AssignedSlot) &&
                  match.IsCombatAlive(_localAvatar.AssignedSlot))) &&
                !match.IsGlobalSimulationPaused;
            SetActive(_reticle, showReticle);
            RefreshReticleColor(showReticle);

            RefreshMinefieldPanelContent(match, showMinefieldReady);

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
        }

        private void RefreshLocalPlayer(NetworkMatchState match)
        {
            if (_localAvatar == null)
            {
                SetTextVisible(_movesText, string.Empty, false);
                return;
            }

            if (match.IsCombatPhase)
            {
                SetTextVisible(_movesText, string.Empty, false);
                return;
            }

            var isActionPhase = match.FlowState == BoardFlowState.Action;
            if (!isActionPhase)
            {
                SetTextVisible(_movesText, string.Empty, false);
                return;
            }

            SetTextVisible(
                _movesText,
                Mathf.Max(0, _localAvatar.LocalRemainingMoves).ToString(),
                true);
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
                        ? uiBindings.Item.InventorySelectedColor
                        : occupied
                            ? uiBindings.Item.InventoryOccupiedColor
                            : uiBindings.Item.InventoryEmptyColor;
                }

                if (_choiceButtons[i] != null)
                {
                    _choiceButtons[i].interactable = occupied;
                }
            }

            var firearmSelected = _localAvatar != null &&
                                  _localAvatar.LocalSelectedItemSlot >= 0 &&
                                  BoardItemPresentationRules.IsFirearm(
                                      _localAvatar.LocalEquippedItem);
            SetTextVisible(
                _ammoText,
                firearmSelected
                    ? Mathf.Max(0, _localAvatar.LocalItemCharges).ToString()
                    : string.Empty,
                firearmSelected);
        }

        private void RefreshInteractionPrompt()
        {
            var prompt = string.Empty;
            var visible = _localAvatar != null &&
                          _localAvatar.TryGetLocalBoardInteractionPrompt(
                              out prompt);
            SetTextVisible(
                _interactionPrompt,
                visible ? prompt : string.Empty,
                visible);
        }


        private void RefreshLandingHudDeltas(
            NetworkMatchState match,
            int slot,
            NetworkPlayerAvatar avatar,
            int health,
            float healthRatio)
        {
            if (avatar == null)
            {
                _observedStats[slot] = false;
                HideDelta(slot, 0, _playerHealthDeltaTexts[slot]);
                HideDelta(slot, 1, _playerKeyDeltaTexts[slot]);
                HideDelta(slot, 2, _playerGoldDeltaTexts[slot]);
                return;
            }

            var keys = avatar.KeyCount;
            var gold = avatar.Gold;
            var presenting = match.FlowState ==
                             BoardFlowState.LandingEffectResolve &&
                             !match.IsGlobalSimulationPaused;
            if (_observedStats[slot] && presenting)
            {
                var healthDelta = health - _observedHealth[slot];
                if (healthDelta != 0)
                {
                    ShowDelta(slot, 0, _playerHealthDeltaTexts[slot], healthDelta);
                    var slider = _playerHealthSliders[slot];
                    if (slider != null)
                    {
                        _healthTweens[slot]?.Kill();
                        _healthTweens[slot] = DOTween.To(
                                () => slider.value,
                                value => slider.SetValueWithoutNotify(value),
                                healthRatio,
                                0.65f)
                            .SetEase(Ease.OutCubic)
                            .SetUpdate(true);
                    }
                }

                var keyDelta = keys - _observedKeys[slot];
                if (keyDelta != 0)
                    ShowDelta(slot, 1, _playerKeyDeltaTexts[slot], keyDelta);
                var goldDelta = gold - _observedGold[slot];
                if (goldDelta != 0)
                    ShowDelta(slot, 2, _playerGoldDeltaTexts[slot], goldDelta);
            }

            _observedHealth[slot] = health;
            _observedKeys[slot] = keys;
            _observedGold[slot] = gold;
            _observedStats[slot] = true;
            if (!presenting)
            {
                HideDelta(slot, 0, _playerHealthDeltaTexts[slot]);
                HideDelta(slot, 1, _playerKeyDeltaTexts[slot]);
                HideDelta(slot, 2, _playerGoldDeltaTexts[slot]);
            }
            else
            {
                ExpireDelta(slot, 0, _playerHealthDeltaTexts[slot]);
                ExpireDelta(slot, 1, _playerKeyDeltaTexts[slot]);
                ExpireDelta(slot, 2, _playerGoldDeltaTexts[slot]);
            }
        }

        private void ShowDelta(int slot, int kind, Text label, int amount)
        {
            if (label == null)
                return;

            label.text = amount > 0
                ? "+" + amount.ToString("00")
                : "-" + (-amount).ToString("00");
            label.color = amount > 0
                ? uiBindings.Hud.HealthyHealthColor
                : uiBindings.Hud.CriticalHealthColor;
            label.gameObject.SetActive(true);
            _deltaExpiresAt[slot, kind] = Time.unscaledTime + 1.5f;
        }

        private void ExpireDelta(int slot, int kind, Text label)
        {
            if (label != null && Time.unscaledTime >= _deltaExpiresAt[slot, kind])
                label.gameObject.SetActive(false);
        }

        private void HideDelta(int slot, int kind, Text label)
        {
            _deltaExpiresAt[slot, kind] = 0f;
            if (label != null)
                label.gameObject.SetActive(false);
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
                        ? ResolveBoardPlayerTextColor(avatar, slot)
                        : uiBindings.Hud.DisconnectedPlayerColor;
                }

                if (_playerCards[slot] != null)
                {
                    _playerCards[slot].color =
                        uiBindings.Hud.GetPlayerCardColor(
                            ResolveBoardPlayerColor(avatar, slot));
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
                RefreshLandingHudDeltas(
                    match, slot, avatar,
                    avatar != null ? avatar.CurrentHealth : 0,
                    healthRatio);
                if (_playerHealthSliders[slot] != null)
                {
                    if (_healthTweens[slot] == null ||
                        !_healthTweens[slot].IsActive())
                        _playerHealthSliders[slot].SetValueWithoutNotify(healthRatio);
                }

                if (_playerHealthFills[slot] != null)
                {
                    _playerHealthFills[slot].gameObject.SetActive(
                        !match.IsArenaCombatPhase);
                    _playerHealthFills[slot].color = healthRatio > 0.5f
                        ? uiBindings.Hud.HealthyHealthColor
                        : healthRatio > 0.25f
                            ? uiBindings.Hud.WoundedHealthColor
                            : uiBindings.Hud.CriticalHealthColor;
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
                        ? uiBindings.Hud.CombatOutColor
                        : uiBindings.Hud.GetActionIconColor(actionState);
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

        private void RefreshMinefieldPanelContent(
            NetworkMatchState match,
            bool readyPresentationVisible)
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
            var hideReadyPresentation =
                !readyPresentationVisible ||
                revealPending ||
                isSkip;
            RefreshMinigamePreview(selected, hideReadyPresentation);
            RefreshMinigameControlRows(selected, hideReadyPresentation);
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
                _minigameDescription,
                revealPending || isSkip
                    ? string.Empty
                    : MinigameCatalog.GetDescription(selected));
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
                    ? uiBindings.Hud.ReadyPlayerCompleteColor
                    : uiBindings.Hud.ReadyPlayerWaitingColor;
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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
                    FormatMinigameResultPlayerName(avatar, rankedSlot),
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



        internal static IReadOnlyList<string> GetMinigameControlGuide(
            ScheduledMinigameId minigame)
        {
            var index = (int)minigame;
            if (index < 0 || index >= MinigameControlGuides.Length)
            {
                return Array.Empty<string>();
            }

            var sources = MinigameControlGuides[index];
            var localized = new string[sources.Length];
            for (var row = 0; row < sources.Length; row++)
            {
                var source = sources[row];
                var labelStart = source.LastIndexOf(">  ", StringComparison.Ordinal);
                localized[row] = labelStart < 0
                    ? GameText.T(source)
                    : source.Substring(0, labelStart + 3) +
                      GameText.T(source.Substring(labelStart + 3));
            }

            return localized;
        }

        internal static bool ShouldShowMinigameReadyPresentation(
            BoardFlowState state,
            bool isGlobalSimulationPaused,
            bool isReconnectPaused)
        {
            return !isGlobalSimulationPaused &&
                   !isReconnectPaused &&
                   (state == BoardFlowState.MinigameIntroReady ||
                    state == BoardFlowState.MinigameLoading);
        }

        private void RefreshMinigameControlRows(
            ScheduledMinigameId minigame,
            bool hidden)
        {
            if (_controlGuideInitialized &&
                _renderedControlMinigame == minigame &&
                _renderedControlLanguage == GameText.Language &&
                _renderedControlHidden == hidden)
            {
                return;
            }

            _renderedControlMinigame = minigame;
            _renderedControlLanguage = GameText.Language;
            _renderedControlHidden = hidden;
            _controlGuideInitialized = true;
            var guide = hidden
                ? Array.Empty<string>()
                : GetMinigameControlGuide(minigame);
            for (var index = 0; index < _minigameControlRows.Length; index++)
            {
                var row = _minigameControlRows[index];
                if (row == null)
                {
                    continue;
                }

                var visible = index < guide.Count;
                row.text = visible ? guide[index] : string.Empty;
                row.gameObject.SetActive(visible);
            }
        }

        private void RefreshMinigamePreview(
            ScheduledMinigameId minigame,
            bool hidden)
        {
            var clip = hidden || uiBindings == null
                ? null
                : uiBindings.Hud.GetMinigamePreviewClip(minigame);
            if (_minigamePreviewPlayer == null)
            {
                return;
            }

            if (_activeMinigamePreviewClip != clip ||
                _minigamePreviewPlayer.clip != clip)
            {
                _minigamePreviewPlayer.Stop();
                _minigamePreviewPlayer.clip = clip;
                _activeMinigamePreviewClip = clip;
                if (_minigamePreviewImage != null)
                {
                    _minigamePreviewImage.texture = null;
                    _minigamePreviewImage.enabled = false;
                }
                if (clip != null && Application.isPlaying)
                {
                    _minigamePreviewPlayer.Prepare();
                }
            }

            var showPreparedVideo =
                !hidden &&
                clip != null &&
                _minigamePreviewPlayer.isPrepared &&
                _minigamePreviewPlayer.texture != null;
            if (_minigamePreviewImage != null)
            {
                _minigamePreviewImage.texture = showPreparedVideo
                    ? _minigamePreviewPlayer.texture
                    : null;
                _minigamePreviewImage.enabled = showPreparedVideo;
            }
            SetActive(
                _minigamePreviewPlaceholder != null
                    ? _minigamePreviewPlaceholder.gameObject
                    : null,
                !hidden && !showPreparedVideo);
        }

        private void WireMinigamePreview()
        {
            if (_minigamePreviewPlayer == null)
            {
                return;
            }

            _minigamePreviewPlayer.prepareCompleted -=
                HandleMinigamePreviewPrepared;
            _minigamePreviewPlayer.prepareCompleted +=
                HandleMinigamePreviewPrepared;
            _minigamePreviewPlayer.errorReceived -=
                HandleMinigamePreviewError;
            _minigamePreviewPlayer.errorReceived +=
                HandleMinigamePreviewError;
        }

        private void UnwireMinigamePreview()
        {
            if (_minigamePreviewPlayer == null)
            {
                return;
            }

            _minigamePreviewPlayer.prepareCompleted -=
                HandleMinigamePreviewPrepared;
            _minigamePreviewPlayer.errorReceived -=
                HandleMinigamePreviewError;
        }

        private void HandleMinigamePreviewPrepared(VideoPlayer player)
        {
            if (player == null ||
                player != _minigamePreviewPlayer ||
                player.clip == null ||
                player.clip != _activeMinigamePreviewClip)
            {
                return;
            }

            if (_minigamePreviewImage != null)
            {
                _minigamePreviewImage.texture = player.texture;
                _minigamePreviewImage.enabled = player.texture != null;
            }
            SetActive(
                _minigamePreviewPlaceholder != null
                    ? _minigamePreviewPlaceholder.gameObject
                    : null,
                false);
            player.Play();
        }

        private void HandleMinigamePreviewError(
            VideoPlayer player,
            string message)
        {
            if (player != _minigamePreviewPlayer)
            {
                return;
            }

            if (_minigamePreviewImage != null)
            {
                _minigamePreviewImage.texture = null;
                _minigamePreviewImage.enabled = false;
            }
            SetActive(
                _minigamePreviewPlaceholder != null
                    ? _minigamePreviewPlaceholder.gameObject
                    : null,
                true);
            Debug.LogWarning(
                "Minigame preview video could not be prepared: " + message,
                this);
        }

        private void StopMinigamePreview()
        {
            if (_minigamePreviewPlayer != null)
            {
                _minigamePreviewPlayer.Stop();
                _minigamePreviewPlayer.clip = null;
            }
            if (_minigamePreviewImage != null)
            {
                _minigamePreviewImage.texture = null;
                _minigamePreviewImage.enabled = false;
            }
            _activeMinigamePreviewClip = null;
        }

        private void SetWaitingState()
        {
            StopMinigamePreview();
            SetBoardUiVisible(true);
            SetActive(_selectionPanel, false);
            SetActive(_readyPanel, false);
            SetActive(_resultPanel, false);
            SetActive(_reconnectOverlay, false);
            SetActive(_boardEventPopupPanel, false);
            SetText(_boardEventPopupMessage, string.Empty);
            ResetBoardKillFeed();
            SetActive(_reticle, false);
            RefreshReticleColor(false);
            CloseItemShop();
            SetText(_turnText, GameText.T("TURN --"));
            SetText(_phaseText, GameText.T("WAITING FOR 4 PLAYERS"));
            SetText(_phaseTimerText, "--:--");
            SetTextVisible(_movesText, string.Empty, false);
            SetTextVisible(_ammoText, string.Empty, false);
            SetTextVisible(_interactionPrompt, string.Empty, false);
            cameraDirector?.SetUiPointerVisible(true);
        }

        private void RefreshBoardKillFeed()
        {
            if (_boardKillFeedPanel == null || _boardKillFeedView == null ||
                !_boardKillFeedView.HasRequiredReferences)
            {
                return;
            }

            if (!_boardKillFeedPanel.activeSelf)
            {
                if (_pendingBoardKills.Count > 0)
                {
                    ShowNextBoardKill();
                }
                return;
            }

            if (Time.unscaledTime < _boardKillFeedVisibleUntil)
            {
                return;
            }

            if (_pendingBoardKills.Count > 0)
            {
                if (ShowNextBoardKill())
                    return;
            }

            SetActive(_boardKillFeedPanel, false);
            _boardKillFeedView.Clear();
            _boardKillFeedVisibleUntil = 0f;
        }

        private bool ShowNextBoardKill()
        {
            if (_boardKillFeedPanel == null || _boardKillFeedView == null ||
                !_boardKillFeedView.HasRequiredReferences ||
                _pendingBoardKills.Count == 0)
            {
                return false;
            }

            var batchEndsAt = _pendingBoardKills[0].ReceivedAt +
                              BoardKillFeedBatchSeconds;
            if (Time.unscaledTime < batchEndsAt)
                return false;

            var best = SelectBoardKillIndex(_pendingBoardKills, batchEndsAt);

            var entry = _pendingBoardKills[best];
            _pendingBoardKills.RemoveAt(best);
            var message = FormatBoardKillFeedMessage(
                entry.KillerName,
                entry.KillerColor,
                entry.VictimName,
                entry.VictimColor,
                entry.ItemId);
            _boardKillFeedView.Present(
                message.BeforeItem,
                GetBoardKillItemIcon(entry.ItemId),
                message.ItemName,
                message.AfterItem);
            SetActive(_boardKillFeedPanel, true);
            _boardKillFeedVisibleUntil =
                Time.unscaledTime + BoardKillFeedEntrySeconds;
            return true;
        }

        private void ResetBoardKillFeed()
        {
            _pendingBoardKills.Clear();
            _boardKillFeedVisibleUntil = 0f;
            SetActive(_boardKillFeedPanel, false);
            _boardKillFeedView?.Clear();
        }

        internal static int SelectBoardKillIndex(
            IReadOnlyList<BoardKillFeedEntry> entries,
            float batchEndsAt)
        {
            if (entries == null || entries.Count == 0)
                return -1;

            var best = 0;
            for (var index = 1; index < entries.Count; index++)
            {
                var candidate = entries[index];
                if (candidate.ReceivedAt > batchEndsAt)
                    break;
                var current = entries[best];
                var comparison = PlayerRankingRules.Compare(
                    candidate.VictimRankingStats,
                    current.VictimRankingStats);
                if (comparison < 0 || comparison == 0 &&
                    candidate.VictimSlot < current.VictimSlot)
                    best = index;
            }

            return best;
        }

        private static string ResolveBoardPlayerName(
            NetworkPlayerAvatar avatar,
            int slot)
        {
            return avatar != null && !string.IsNullOrWhiteSpace(avatar.DisplayName)
                ? avatar.DisplayName
                : GameText.F("Player {0}", slot + 1);
        }

        private static string FormatMinigameResultPlayerName(
            NetworkPlayerAvatar avatar,
            int slot)
        {
            return ColorizeBoardPlayerName(
                ResolveBoardPlayerName(avatar, slot),
                ResolveBoardPlayerTextColor(avatar, slot));
        }

        private static Color ResolveBoardPlayerTextColor(
            NetworkPlayerAvatar avatar,
            int slot)
        {
            var color = ResolveBoardPlayerColor(avatar, slot);
            return Instance != null && Instance.uiBindings != null
                ? Instance.uiBindings.Hud.GetPlayerTextColor(color)
                : BoardCanvasBindings.EnsureMinimumValue(color, 0.72f);
        }

        private static Color ResolveBoardPlayerColor(
            NetworkPlayerAvatar avatar,
            int slot)
        {
            if (avatar != null)
            {
                return avatar.Appearance.BodyColor;
            }

            return Instance != null && Instance.uiBindings != null
                ? Instance.uiBindings.Hud.GetPlayerColor(slot)
                : (Color)LobbyColorPalette.GetColor(slot);
        }

        internal static BoardKillFeedSegments FormatBoardKillFeedMessage(
            string killerName,
            Color killerColor,
            string victimName,
            Color victimColor,
            PrototypeItemId itemId)
        {
            var itemName = itemId == PrototypeItemId.None
                ? GameText.T("Punch")
                : IsBoardKillItem(itemId)
                    ? GameText.T(PrototypeItemCatalog.Get(itemId).DisplayName)
                    : GameText.T("ITEM");
            var particle = GameText.Language == GameLanguage.Korean
                ? KoreanInstrumentParticle(itemName)
                : string.Empty;
            var formatted = GameText.F(
                "{0} eliminated {1} with {2}{3}.",
                ColorizeBoardPlayerName(killerName, killerColor),
                ColorizeBoardPlayerName(victimName, victimColor),
                BoardKillItemMarker.ToString(),
                particle);
            var marker = formatted.IndexOf(BoardKillItemMarker);
            return marker >= 0
                ? new BoardKillFeedSegments(
                    formatted.Substring(0, marker),
                    itemName,
                    formatted.Substring(marker + 1))
                : new BoardKillFeedSegments(formatted, itemName, string.Empty);
        }

        private static bool IsBoardKillItem(PrototypeItemId itemId)
        {
            return itemId == PrototypeItemId.Pistol ||
                   itemId == PrototypeItemId.Sniper ||
                   itemId == PrototypeItemId.Grenade ||
                   itemId == PrototypeItemId.Mine;
        }

        private static Sprite GetBoardKillItemIcon(PrototypeItemId itemId)
        {
            return IsBoardKillItem(itemId)
                ? PrototypeItemCatalog.Get(itemId).Icon
                : null;
        }

        private static string KoreanInstrumentParticle(string noun)
        {
            if (string.IsNullOrEmpty(noun))
                return "으로";
            var last = noun[noun.Length - 1];
            if (last < '\uAC00' || last > '\uD7A3')
                return "으로";
            var finalConsonant = (last - '\uAC00') % 28;
            return finalConsonant == 0 || finalConsonant == 8
                ? "로"
                : "으로";
        }

        private static string ColorizeBoardPlayerName(
            string displayName,
            Color color)
        {
            var safeName = string.IsNullOrWhiteSpace(displayName)
                ? GameText.T("Player")
                : displayName
                    .Replace("&", "＆")
                    .Replace("<", "＜")
                    .Replace(">", "＞");
            return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" +
                   safeName + "</color>";
        }

        private void RefreshReticleColor(bool visible)
        {
            if (_reticleImage == null || uiBindings == null)
            {
                return;
            }

            _reticleImage.color =
                visible &&
                _localAvatar != null &&
                _localAvatar.HasLocalDamageableFirearmTarget()
                    ? uiBindings.Hud.ReticleDamageableTargetColor
                    : uiBindings.Hud.ReticleDefaultColor;
        }

        private static void CopyReferences<T>(T[] source, T[] destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                destination[index] = source[index];
            }
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null) target.text = value;
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        private static void SetTextVisible(
            Text target,
            string value,
            bool visible)
        {
            if (target == null)
            {
                return;
            }

            target.text = value;
            if (target.gameObject.activeSelf != visible)
            {
                target.gameObject.SetActive(visible);
            }
        }

        internal readonly struct BoardKillFeedEntry
        {
            public BoardKillFeedEntry(
                string killerName,
                Color killerColor,
                string victimName,
                Color victimColor,
                PrototypeItemId itemId,
                int victimSlot,
                PlayerRankingStats victimRankingStats,
                float receivedAt)
            {
                KillerName = killerName;
                KillerColor = killerColor;
                VictimName = victimName;
                VictimColor = victimColor;
                ItemId = itemId;
                VictimSlot = victimSlot;
                VictimRankingStats = victimRankingStats;
                ReceivedAt = receivedAt;
            }

            public string KillerName { get; }
            public Color KillerColor { get; }
            public string VictimName { get; }
            public Color VictimColor { get; }
            public PrototypeItemId ItemId { get; }
            public int VictimSlot { get; }
            public PlayerRankingStats VictimRankingStats { get; }
            public float ReceivedAt { get; }
        }

        internal readonly struct BoardKillFeedSegments
        {
            public BoardKillFeedSegments(
                string beforeItem,
                string itemName,
                string afterItem)
            {
                BeforeItem = beforeItem;
                ItemName = itemName;
                AfterItem = afterItem;
            }

            public string BeforeItem { get; }
            public string ItemName { get; }
            public string AfterItem { get; }
        }

        internal static bool ShouldShowBoardEventPopup(
            BoardFlowState state,
            bool isGlobalSimulationPaused,
            BoardLandingEffectType effectType,
            string message)
        {
            return ShouldShowBoardEventPopup(
                state,
                isGlobalSimulationPaused,
                effectType,
                message,
                BoardResourceTransferPhase.None);
        }

        internal static bool ShouldShowBoardEventPopup(
            BoardFlowState state,
            bool isGlobalSimulationPaused,
            BoardLandingEffectType effectType,
            string message,
            BoardResourceTransferPhase transferPhase)
        {
            return state == BoardFlowState.LandingEffectResolve &&
                   !isGlobalSimulationPaused &&
                   effectType == BoardLandingEffectType.SpecialEvent &&
                   !string.IsNullOrEmpty(message) &&
                   transferPhase != BoardResourceTransferPhase.Source &&
                   transferPhase != BoardResourceTransferPhase.Destination &&
                   transferPhase != BoardResourceTransferPhase.Complete;
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

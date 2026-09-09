using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MazeParty.Gameplay.BoardFlowTestbed
{
    /// <summary>
    /// Editor-only, network-free driver for the same flow/tile/camera rules used by
    /// Board.unity. Debug controls stand in for the other three online players.
    /// </summary>
    public sealed class BoardFlowLocalSimulator : MonoBehaviour
    {
        private const string DefaultWorldDieVisualPrefabPath =
            "Assets/MazeParty/Art/Dice/D12/Prefabs/D12WorldDieVisual.prefab";
        private const float WorldDieRollHorizontalImpulse = 4.5f;
        private const float WorldDieRollUpwardImpulse = 4f;
        private const float WorldDieRollTorqueImpulse = 7f;
        private const float WorldDieLandingClearance = 0.01f;
        private static readonly Color LocalWorldDieTint =
            new Color(0.95f, 0.25f, 0.25f);

        [SerializeField] private CharacterController player;
        [SerializeField] private Transform eyePivot;
        [SerializeField] private BoardTopology topology;
        [SerializeField] private GameplayCameraDirector cameraDirector;
        [SerializeField] private GameObject worldDieVisualPrefab;
        [SerializeField, Min(0.1f)] private float moveSpeed = 5f;
        [SerializeField, Min(0.01f)] private float lookSensitivity = 0.12f;
        [SerializeField, Min(0.05f)] private float worldDieNudgeHorizontalImpulse = 1.35f;
        [SerializeField, Min(0f)] private float worldDieNudgeUpwardImpulse = 0.25f;
        [SerializeField, Min(0f)] private float worldDieNudgeTorqueImpulse = 0.9f;

        private readonly BoardFlowStateMachine _flow = new BoardFlowStateMachine();
        private readonly BoardTraversalState _traversal = new BoardTraversalState();
        private readonly Image[] _slotImages = new Image[GameplayInventory.Capacity];
        private readonly Text[] _slotLabels = new Text[GameplayInventory.Capacity];
        private readonly Button[] _choiceButtons = new Button[GameplayInventory.Capacity];
        private readonly Text[] _playerRows = new Text[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly Image[] _playerCards = new Image[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly Image[] _playerHealthFills = new Image[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly Text[] _playerHealthTexts = new Text[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly Text[] _playerCurrencyTexts = new Text[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly Text[] _playerActionIcons = new Text[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly Text[] _playerRankTexts = new Text[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly PrototypeItemId[] _itemSlots =
            new PrototypeItemId[GameplayInventory.Capacity];
        private readonly Button[] _shopOfferButtons = new Button[ItemShopRules.OfferCount];
        private readonly Text[] _shopOfferLabels = new Text[ItemShopRules.OfferCount];
        private readonly int[] _maxHealth = new int[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly int[] _currentHealth = new int[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly int[] _keys = new int[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly int[] _gold = new int[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly int[] _minigameWins = new int[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly PlayerBoardActionState[] _actionStates =
            new PlayerBoardActionState[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly int[] _combatHealth =
            new int[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly double[] _arrivalTimes =
            new double[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly double[] _combatEliminatedAt =
            new double[BoardFlowStateMachine.RequiredPlayerCount];
        private readonly Queue<BoardCombatGroup> _combatQueue =
            new Queue<BoardCombatGroup>();

        private GameObject _selectionPanel;
        private GameObject _readyPanel;
        private GameObject _resultPanel;
        private GameObject _reticle;
        private Text _turnText;
        private Text _phaseText;
        private Text _phaseTimerText;
        private Text _choiceText;
        private Text _shieldText;
        private Text _diceText;
        private Text _movesText;
        private Text _ammoText;
        private Text _statusText;
        private Text _tooltipText;
        private Text _speedButtonLabel;
        private Button _noItemButton;
        private Button _readyButton;
        private Button _finishActionButton;
        private Button _stageFightButton;
        private Button _speedButton;
        private Button _pauseButton;
        private Button _damagePlayerButton;
        private Button _addGoldButton;
        private Button _buyKeyButton;
        private Button _itemShopCloseButton;
        private GameObject _itemShopPanel;
        private Text _itemShopTitle;
        private Text _itemShopTooltip;
        private Text _itemShopStatus;

        private double _simulationNow;
        private float _simulationSpeed = 1f;
        private float _yaw;
        private float _pitch;
        private int _roll;
        private int _remainingMoves;
        private int _selectedSlot = -1;
        private byte _occupiedMask;
        private bool _paused;
        private bool _editorPointerVisible;
        private ItemChoiceResolution _lastChoiceResolution = ItemChoiceResolution.NotStarted;
        private PlayerBoardBoundaryWalls _boundaryWalls;
        private KeyShopRuntimeState _keyShopState;
        private KeyShopWorldMarker _keyShopMarker;
        private ItemShopWorldMarker _itemShopMarker;
        private readonly ItemShopStock[] _itemShopStocks =
            new ItemShopStock[ItemShopRules.ShopCount];
        private readonly Vector2Int[] _itemShopLocations =
            new Vector2Int[ItemShopRules.ShopCount];
        private readonly int[] _itemShopAppearedTurns =
            new int[ItemShopRules.ShopCount];
        private readonly int[] _itemShopRevisions =
            new int[ItemShopRules.ShopCount];
        private readonly bool[] _itemShopActive = new bool[ItemShopRules.ShopCount];
        private int _openItemShopIndex = -1;
        private bool _keyShopRevealActive;
        private double _keyShopRevealEndsAt;
        private GameObject _worldDie;
        private Collider _worldDieCollider;
        private Rigidbody _worldDieBody;
        private WorldDieFaceMarker[] _worldDieFaceMarkers =
            Array.Empty<WorldDieFaceMarker>();
        private TextMesh _worldDieResult;
        private BoardTile _worldDieTile;
        private WorldDieRollPresentationPhase _worldDieRollPresentationPhase;
        private int _pendingWorldDieFace;
        private double _worldDieRollStartedAt = -1d;
        private Vector3 _worldDieLandingStartPosition;
        private Quaternion _worldDieLandingStartRotation = Quaternion.identity;
        private Vector3 _worldDieLandingTargetPosition;
        private Quaternion _worldDieLandingTargetRotation = Quaternion.identity;
        private bool _worldDieNudgeInProgress;
        private double _worldDieNudgeDeadline = -1d;
        private double _worldDieNudgeBelowThresholdSince = -1d;
        private double _worldDieHideDeadline = -1d;
        private bool _worldDiePhysicsSuspended;
        private bool _worldDieWasKinematicBeforeSuspension = true;
        private Vector3 _worldDieSuspendedLinearVelocity;
        private Vector3 _worldDieSuspendedAngularVelocity;
        private BoardLandingEffectLayout _boardEffectLayout;
        private int _nextLandingEffectSlot;
        private bool _stageTwoPlayerFightAtFinish;
        private bool _localCombatActive;
        private byte _localCombatParticipantMask;
        private byte _localCombatAliveMask;
        private Vector2Int _localCombatTile;
        private double _localCombatEndsAt;
        private double _nextLocalPunchAllowedAt;
        private double _nextLocalBoardPunchAllowedAt;
        private double _nextLocalPrimaryRepeatAt;
        private int _localCombatSequenceIndex;
        private int _localFightsResolved;
        private bool _pendingLocalCombatProtection;
        private double _personalProtectionEndsAtFlowTime;
        private FootstepAudioEmitter _localFootstepEmitter;
        private PlayerAvatarVisual _avatarVisual;
        private bool _localCrouching;
        private readonly Collider[] _standingClearanceHits = new Collider[16];
        private readonly FootstepCadenceTracker _localFootstepCadence =
            new FootstepCadenceTracker();

        public void Configure(
            CharacterController localPlayer,
            Transform localEye,
            BoardTopology boardTopology,
            GameplayCameraDirector director,
            GameObject dieVisualPrefab = null)
        {
            player = localPlayer;
            eyePivot = localEye;
            topology = boardTopology;
            cameraDirector = director;
            worldDieVisualPrefab = dieVisualPrefab;
        }

        private void Awake()
        {
            BindUi();
            WireButtons();
            _flow.Transitioned += OnTransitioned;
        }

        private void Start()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<CharacterController>();
            }
            if (topology == null)
            {
                topology = FindAnyObjectByType<BoardTopology>();
            }
            if (cameraDirector == null)
            {
                cameraDirector = FindAnyObjectByType<GameplayCameraDirector>();
            }

            if (player != null)
            {
                _avatarVisual = player.GetComponent<PlayerAvatarVisual>();
                if (_avatarVisual == null)
                {
                    _avatarVisual = player.gameObject.AddComponent<PlayerAvatarVisual>();
                }
                _avatarVisual.EnsureBuilt();
                _avatarVisual.ConfigureEyePivot(eyePivot);
                _avatarVisual.SetDisplayName("Local Player");
            }
            var remoteColors = new[]
            {
                new Color(0.25f, 0.55f, 1f),
                new Color(0.25f, 0.85f, 0.4f),
                new Color(1f, 0.75f, 0.2f)
            };
            for (var slot = 1; slot < BoardFlowStateMachine.RequiredPlayerCount; slot++)
            {
                var marker = GetRemoteMarker(slot);
                if (marker == null)
                {
                    continue;
                }
                var visual = marker.GetComponent<PlayerAvatarVisual>();
                if (visual == null)
                {
                    visual = marker.gameObject.AddComponent<PlayerAvatarVisual>();
                }
                visual.EnsureBuilt();
                visual.SetBodyColor(remoteColors[slot - 1]);
                visual.SetDisplayName("Player " + (slot + 1));
            }

            _boundaryWalls = player != null
                ? player.GetComponent<PlayerBoardBoundaryWalls>()
                : null;
            if (_boundaryWalls == null && player != null)
            {
                _boundaryWalls = player.gameObject.AddComponent<PlayerBoardBoundaryWalls>();
            }
            if (player != null)
            {
                _localFootstepEmitter = player.GetComponent<FootstepAudioEmitter>();
                if (_localFootstepEmitter == null)
                {
                    _localFootstepEmitter =
                        player.gameObject.AddComponent<FootstepAudioEmitter>();
                }
            }
            if (_boundaryWalls != null && player != null && topology != null)
            {
                _boundaryWalls.Configure(0, player, topology);
                _boundaryWalls.SetPresentationVisible(true);
            }

            _keyShopMarker = topology != null
                ? topology.GetComponent<KeyShopWorldMarker>()
                : null;
            _itemShopMarker = topology != null
                ? topology.GetComponent<ItemShopWorldMarker>()
                : null;
            _keyShopState = GetComponent<KeyShopRuntimeState>();
            if (_keyShopState == null)
            {
                _keyShopState = gameObject.AddComponent<KeyShopRuntimeState>();
            }
            _keyShopState.ResetToInactive();
            ApplyLocalKeyShopState();
            EnsureLocalWorldDie();

            _yaw = player != null ? player.transform.eulerAngles.y : 0f;
            cameraDirector?.SetLocalPlayerEye(eyePivot);
            cameraDirector?.SnapTo(GameplayMode.BoardTopDown, eyePivot);
            InitializeTraversal();
            InitializePlayerStatsAndBoardEffects();
            _flow.Start(_simulationNow, 1);
            RefreshLocalItemShops(1);
            TryPlaceLocalKeyShop(1);
            SetStatus("EDITOR LOCAL SIMULATION: board overview started.");
            RefreshUi();
        }

        private void OnDestroy()
        {
            _flow.Transitioned -= OnTransitioned;
            _traversal.Dispose();
            UnwireButtons();
        }

        private void Update()
        {
            var worldDieTimersWereSuspended =
                _paused || _keyShopRevealActive;
            var unscaledDeltaTime = Time.unscaledDeltaTime;
            _simulationNow += Time.unscaledDeltaTime * _simulationSpeed;
            _flow.Tick(_simulationNow);
            RefreshLocalCrouch();
            AdvanceLocalKeyShopReveal();
            AdvanceLocalCombat();
            AdvanceLocalLandingEffects();
            if (_lastChoiceResolution != _flow.ActionClock.ChoiceResolution)
            {
                _lastChoiceResolution = _flow.ActionClock.ChoiceResolution;
                if (_lastChoiceResolution == ItemChoiceResolution.TimedOut)
                {
                    PrepareLocalWorldDie();
                    SetStatus("Choice timed out. DO NOT USE was selected automatically.");
                }
            }
            HandleEditorPointerToggle();
            HandleInput();
            PreserveLocalWorldDieTimersDuringPause(
                worldDieTimersWereSuspended,
                unscaledDeltaTime);
            SetLocalWorldDiePhysicsSuspended(
                _paused || _keyShopRevealActive);
            UpdateLocalWorldDieRollPresentation();
            UpdateLocalWorldDieLifetime();
            UpdateWorldDieResultBillboard();
            RefreshUi();
        }

        private void FixedUpdate()
        {
            if (_paused || _keyShopRevealActive)
            {
                return;
            }

            UpdateLocalWorldDiePhysics();
        }

        public void SelectItem(int slot)
        {
            if ((_occupiedMask & (1 << slot)) == 0 ||
                !_flow.ActionClock.TrySelectItem(slot, _simulationNow))
            {
                return;
            }

            _selectedSlot = slot;
            PrepareLocalWorldDie();
            SetStatus("Item active. WASD moves in-room; aim at your world die to roll or nudge it.");
        }

        public void ChooseNoItem()
        {
            if (_flow.ActionClock.TryChooseNoItem(_simulationNow))
            {
                _selectedSlot = -1;
                PrepareLocalWorldDie();
                SetStatus("DO NOT USE selected. Aim at your world die and press RMB to roll.");
            }
        }

        public void FinishActionForAllPlayers()
        {
            if (_stageTwoPlayerFightAtFinish)
            {
                StageRemotePlayerTwoAtLocalTile();
            }
            for (var slot = 0; slot < BoardFlowStateMachine.RequiredPlayerCount; slot++)
            {
                _actionStates[slot] = PlayerBoardActionState.Arrived;
                _arrivalTimes[slot] = _simulationNow + slot * 0.001d;
                _flow.TryReportPlayerArrived(slot, _arrivalTimes[slot]);
            }
            SetStatus("EDITOR: all four arrival reports submitted.");
        }

        public void StageTwoPlayerFight()
        {
            if (_flow.State != BoardFlowState.Action)
            {
                SetStatus("EDITOR: stage a fight during FIRST-PERSON ACTION.");
                return;
            }

            _stageTwoPlayerFightAtFinish = true;
            SetStatus("EDITOR: P2 will finish in P1's room when FINISH ACTION is pressed.");
        }

        public void ReadyAllAndSkip()
        {
            if (_flow.TrySkipMinigame(_simulationNow))
            {
                SetStatus("EDITOR: all players ready; minigame skipped without rewards.");
            }
        }

        public void CycleSimulationSpeed()
        {
            _simulationSpeed = _simulationSpeed < 2f ? 10f : _simulationSpeed < 20f ? 60f : 1f;
            SetStatus("EDITOR: flow clock speed set to x" + _simulationSpeed.ToString("0") + ".");
        }

        public void DamageLocalPlayer()
        {
            if (_localCombatActive && IsLocalCombatAlive(0))
            {
                ApplyLocalCombatDamage(0);
                SetStatus("EDITOR: simulated P2 punch dealt 5 temporary HP damage to P1.");
                return;
            }

            _currentHealth[0] = PlayerStatRules.ClampHealth(
                _currentHealth[0] - 20,
                _maxHealth[0]);
            SetStatus("EDITOR: P1 took 20 damage. The HUD health bar updated immediately.");
        }

        public void AddLocalGold()
        {
            _gold[0] = PlayerStatRules.ApplyGoldDelta(_gold[0], 10);
            SetStatus("EDITOR: P1 received 10 gold.");
        }

        public void BuyLocalKey()
        {
            if (!PlayerStatRules.CanPurchaseKey(_gold[0]))
            {
                SetStatus("EDITOR: P1 needs 20 gold to buy one key.");
                return;
            }

            _gold[0] = PlayerStatRules.ApplyGoldDelta(
                _gold[0],
                -PlayerStatRules.KeyShopGoldPrice);
            _keys[0] = PlayerStatRules.AddKeys(_keys[0], 1);
            SetStatus("EDITOR: P1 bought one key for 20 gold.");
        }

        public void TogglePause()
        {
            if (_paused)
            {
                _flow.Resume(_simulationNow);
                _paused = false;
                SetStatus("EDITOR: reconnect-style global pause resumed.");
            }
            else if (_flow.Pause(_simulationNow))
            {
                _paused = true;
                SetStatus("EDITOR: reconnect-style global pause active; flow timers are frozen.");
            }
        }

        public void ShowItemTooltip(int slot)
        {
            if (_tooltipText == null || slot < 0 || slot >= GameplayInventory.Capacity)
            {
                return;
            }

            _tooltipText.text = ItemName(slot) + "\n" + ItemDescription(slot);
            _tooltipText.gameObject.SetActive(true);
        }

        public void HideItemTooltip()
        {
            if (_tooltipText != null)
            {
                _tooltipText.gameObject.SetActive(false);
            }
        }

        private void OnTransitioned(BoardFlowTransition transition)
        {
            switch (transition.Current)
            {
                case BoardFlowState.TurnOverview:
                    ResetLocalCombat();
                    _stageTwoPlayerFightAtFinish = false;
                    ResetActionState();
                    SetAllActionStates(PlayerBoardActionState.Hidden);
                    cameraDirector?.SwitchTo(GameplayMode.BoardTopDown);
                    RefreshLocalItemShops(transition.Turn);
                    TryPlaceLocalKeyShop(transition.Turn);
                    SetStatus("Board overview for turn " + transition.Turn + ".");
                    break;
                case BoardFlowState.Descending:
                    cameraDirector?.SwitchTo(GameplayMode.FirstPerson);
                    SetStatus("Descending through the local overhead bridge.");
                    break;
                case BoardFlowState.Action:
                    for (var slot = 0; slot < _arrivalTimes.Length; slot++)
                    {
                        _arrivalTimes[slot] = double.NaN;
                    }
                    if (_pendingLocalCombatProtection)
                    {
                        _personalProtectionEndsAtFlowTime =
                            _flow.ToFlowTime(_simulationNow) +
                            BoardCombatRules.NextActionItemProtectionSeconds;
                        _pendingLocalCombatProtection = false;
                    }
                    ResetActionState();
                    SetAllActionStates(PlayerBoardActionState.Dice);
                    InitializeTraversal();
                    RefreshBoundaryWalls();
                    SetStatus("Choose an item within 30 seconds; shield and action clocks started together.");
                    break;
                case BoardFlowState.AscendingResolve:
                    EndSelectedItemUse();
                    SetAllActionStates(PlayerBoardActionState.Hidden);
                    cameraDirector?.SwitchTo(GameplayMode.BoardTopDown);
                    SetStatus("Input closed. Five-second effect settle window.");
                    break;
                case BoardFlowState.CombatResolve:
                    BeginLocalCombatSequence();
                    break;
                case BoardFlowState.LandingEffectResolve:
                    BeginLocalLandingEffects();
                    SetStatus("All fights completed. Resolving landing effects in P1-P4 order.");
                    break;
                case BoardFlowState.MinigameIntroReady:
                    ResolveAllRemainingLocalLandingEffects();
                    SetStatus("Minigame TODO: click READY / SKIP ALL in the editor panel.");
                    break;
                case BoardFlowState.SkippedResult:
                    SetStatus("Result placeholder: no minigame reward. Next turn in three seconds.");
                    break;
            }

            ApplyBodyVisibility(transition.Current == BoardFlowState.Descending ||
                                transition.Current == BoardFlowState.Action ||
                                transition.Current == BoardFlowState.CombatResolve &&
                                _localCombatActive && IsLocalCombatAlive(0));
        }

        private void HandleInput()
        {
            if (_paused || _keyShopRevealActive || _openItemShopIndex >= 0 ||
                player == null)
            {
                return;
            }

            if (_flow.State == BoardFlowState.CombatResolve)
            {
                HandleLocalCombatInput();
                return;
            }

            if (_flow.State != BoardFlowState.Action)
            {
                return;
            }

            var choice = _flow.ActionClock.ChoiceResolution;
            if (choice == ItemChoiceResolution.Pending || choice == ItemChoiceResolution.NotStarted)
            {
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                var delta = mouse.delta.ReadValue() * lookSensitivity;
                _yaw += delta.x;
                _pitch = Mathf.Clamp(_pitch - delta.y, -85f, 85f);
                player.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                if (eyePivot != null)
                {
                    eyePivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
                }
            }

            if (mouse != null && !IsPointerOverUi())
            {
                if (mouse.rightButton.wasPressedThisFrame &&
                    TryGetAimedBoardShop(out var shopHit))
                {
                    if (shopHit.collider.GetComponentInParent<KeyShopWorldTarget>() != null)
                    {
                        TryPurchaseLocalKey();
                        return;
                    }

                    var itemTarget = shopHit.collider.GetComponentInParent<ItemShopWorldTarget>();
                    if (itemTarget != null)
                    {
                        OpenLocalItemShop(itemTarget.ShopIndex);
                        return;
                    }
                }
                else if (mouse.rightButton.wasPressedThisFrame &&
                         _roll <= 0 &&
                         _worldDieRollPresentationPhase ==
                         WorldDieRollPresentationPhase.None &&
                         TryGetAimedLocalWorldDie(out var rollRay))
                {
                    BeginLocalWorldDieRoll(rollRay);
                }
                var repeatPrimary = ShouldRepeatLocalPrimaryAction(
                    mouse,
                    PlayerUnarmedRules.PunchCooldownSeconds);
                if (repeatPrimary &&
                    _worldDieRollPresentationPhase ==
                    WorldDieRollPresentationPhase.None &&
                    TryGetAimedLocalWorldDie(out var dieRay))
                {
                    if (_roll <= 0)
                    {
                        NudgeLocalWorldDie(dieRay);
                        _avatarVisual?.TriggerPunch();
                        SetStatus("Your world die was nudged inside the current room.");
                    }
                }
                else if (repeatPrimary && _selectedSlot >= 0)
                {
                    if (mouse.leftButton.wasPressedThisFrame)
                    {
                        var item = _itemSlots[_selectedSlot];
                        _avatarVisual?.TriggerItemUse(item);
                        _occupiedMask = (byte)(_occupiedMask & ~(1 << _selectedSlot));
                        _itemSlots[_selectedSlot] = PrototypeItemId.None;
                        _selectedSlot = -1;
                        SetStatus("Prototype item consumed. Concrete combat/effect is TODO.");
                    }
                }
                else if (repeatPrimary)
                {
                    TryLocalBoardPunch();
                }
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }
            var input = new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
            input = Vector2.ClampMagnitude(input, 1f);
            var quietWalking = _localCrouching && FootstepRules.HasMovementIntent(input);
            var movement = player.transform.TransformDirection(new Vector3(input.x, 0f, input.y));
            movement.y = 0f;
            var previousPosition = player.transform.position;
            player.Move(movement *
                        (moveSpeed * FootstepRules.SpeedMultiplier(quietWalking) *
                         Time.unscaledDeltaTime));
            RecordLocalFootsteps(previousPosition, input, quietWalking);
            ResolveTraversal();
        }

        private void HandleLocalCombatInput()
        {
            if (!_localCombatActive || !IsLocalCombatAlive(0))
            {
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                var delta = mouse.delta.ReadValue() * lookSensitivity;
                _yaw += delta.x;
                _pitch = Mathf.Clamp(_pitch - delta.y, -85f, 85f);
                player.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                if (eyePivot != null)
                {
                    eyePivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
                }
            }

            if (mouse != null && !IsPointerOverUi() &&
                ShouldRepeatLocalPrimaryAction(
                    mouse,
                    BoardCombatRules.PunchCooldownSeconds))
            {
                TryLocalCombatPunch();
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            var input = new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) -
                (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) -
                (keyboard.sKey.isPressed ? 1f : 0f));
            input = Vector2.ClampMagnitude(input, 1f);
            var quietWalking = _localCrouching && FootstepRules.HasMovementIntent(input);
            var movement = player.transform.TransformDirection(
                new Vector3(input.x, 0f, input.y));
            movement.y = 0f;
            var previousPosition = player.transform.position;
            player.Move(movement *
                        (moveSpeed * FootstepRules.SpeedMultiplier(quietWalking) *
                         Time.unscaledDeltaTime));
            RecordLocalFootsteps(previousPosition, input, quietWalking);
            if (_traversal.IsInitialized)
            {
                ClampPlayerInsideTile(_traversal.CurrentTile);
            }
        }

        private void HandleEditorPointerToggle()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame)
            {
                return;
            }

            var canToggleDuringAction = _flow.State == BoardFlowState.Action &&
                                        !_flow.ActionClock.IsChoicePending;
            var canToggleDuringCombat = _flow.State == BoardFlowState.CombatResolve &&
                                        _localCombatActive && IsLocalCombatAlive(0);
            if (_paused || _keyShopRevealActive || _openItemShopIndex >= 0 ||
                (!canToggleDuringAction && !canToggleDuringCombat))
            {
                _editorPointerVisible = false;
                return;
            }

            _editorPointerVisible = !_editorPointerVisible;
            SetStatus(_editorPointerVisible
                ? "EDITOR: pointer released. Use the tools panel; press TAB to capture it again."
                : "EDITOR: pointer captured. Press TAB to use the tools panel.");
        }

        private void ResolveTraversal()
        {
            if (topology == null || !_traversal.IsInitialized)
            {
                return;
            }

            var current = _traversal.CurrentTile;
            var center = player.transform.TransformPoint(player.center);
            var outgoing = topology.GetOutgoingGates(current);
            var partial = false;
            for (var i = 0; i < outgoing.Count; i++)
            {
                var outcome = topology.ResolveGate(outgoing[i], _traversal, player, false, 1f);
                if (outcome == BoardGateTraversalOutcome.Committed)
                {
                    _remainingMoves = _traversal.RemainingMoves;
                    RefreshBoundaryWalls();
                    if (_remainingMoves == 0)
                    {
                        _actionStates[0] = PlayerBoardActionState.Arrived;
                        _flow.TryReportPlayerArrived(0, _simulationNow);
                        SetStatus("Moves are spent. You may keep positioning inside this room until all players arrive.");
                    }
                    return;
                }
                partial |= outcome == BoardGateTraversalOutcome.PartialCrossing;
            }

            if (!current.ContainsHorizontalPoint(center) && !partial)
            {
                ClampPlayerInsideTile(current);
            }
        }

        private void InitializeTraversal()
        {
            if (topology == null || player == null)
            {
                return;
            }

            var tile = topology.FindContainingTile(player.transform.position, 0.1f);
            if (tile == null)
            {
                for (var i = 0; i < topology.Tiles.Count; i++)
                {
                    if (topology.Tiles[i] != null && topology.Tiles[i].TileType == BoardTileType.Start)
                    {
                        tile = topology.Tiles[i];
                        Teleport(tile.GetRecoveryCenter(1f));
                        break;
                    }
                }
            }

            if (tile != null)
            {
                _traversal.Begin(tile, _remainingMoves);
                RefreshBoundaryWalls();
            }
        }

        private void ResetActionState()
        {
            _roll = 0;
            _remainingMoves = 0;
            _selectedSlot = -1;
            _editorPointerVisible = false;
            _localFootstepCadence.Reset();
            if (_traversal.IsInitialized)
            {
                _traversal.ResetMoves(0);
            }
            _boundaryWalls?.Hide();
            HideLocalWorldDie();
        }

        private void EndSelectedItemUse()
        {
            if (_selectedSlot >= 0)
            {
                _occupiedMask = (byte)(_occupiedMask & ~(1 << _selectedSlot));
                _itemSlots[_selectedSlot] = PrototypeItemId.None;
            }
            _selectedSlot = -1;
            _boundaryWalls?.Hide();
            HideLocalWorldDie();
        }

        private void RefreshBoundaryWalls()
        {
            if (_boundaryWalls == null || !_traversal.IsInitialized ||
                (_flow.State != BoardFlowState.Action &&
                 !(_flow.State == BoardFlowState.CombatResolve &&
                   _localCombatActive && IsLocalCombatAlive(0))))
            {
                _boundaryWalls?.Hide();
                return;
            }

            _boundaryWalls.Refresh(
                _traversal.CurrentTile,
                _flow.State == BoardFlowState.CombatResolve ? 0 : _remainingMoves);
        }

        private void ClampPlayerInsideTile(BoardTile tile)
        {
            if (tile == null || player == null)
            {
                return;
            }

            var center = player.transform.TransformPoint(player.center);
            var offset = center - tile.WorldCenter;
            var right = tile.transform.right.normalized;
            var forward = tile.transform.forward.normalized;
            var up = tile.transform.up.normalized;
            var safeExtent = Mathf.Max(0.1f, BoardTile.HalfRoomSize - player.radius - 0.02f);
            var horizontal = Mathf.Clamp(Vector3.Dot(offset, right), -safeExtent, safeExtent);
            var depth = Mathf.Clamp(Vector3.Dot(offset, forward), -safeExtent, safeExtent);
            var height = Vector3.Dot(offset, up);
            var clampedCenter = tile.WorldCenter + right * horizontal +
                                forward * depth + up * height;
            var correction = clampedCenter - center;
            if (correction.sqrMagnitude > 0.000001f)
            {
                Teleport(player.transform.position + correction);
            }
        }

        private void EnsureLocalWorldDie()
        {
            if (_worldDie != null)
            {
                return;
            }

            if (!TryCreateLocalWorldDieFromVisualPrefab(out var sourceError))
            {
                Debug.LogError(
                    "BoardFlow testbed could not instantiate its D12 visual prefab: " +
                    sourceError +
                    " Falling back to a red sphere with logical D12 markers.",
                    this);
                CreateFallbackLocalWorldDie();
            }

            _worldDie.name = "Local World Die (Editor)";
            _worldDieBody = _worldDie.AddComponent<Rigidbody>();
            _worldDieBody.interpolation = RigidbodyInterpolation.Interpolate;
            _worldDieBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _worldDieBody.isKinematic = true;
            _worldDieBody.detectCollisions = false;
            var renderers = _worldDie.GetComponentsInChildren<Renderer>(true);
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", LocalWorldDieTint);
                properties.SetColor("_Color", LocalWorldDieTint);
                renderers[rendererIndex].SetPropertyBlock(properties);
            }

            var labelObject = new GameObject("Public World Result");
            labelObject.transform.SetParent(_worldDie.transform, false);
            var visualScale = Mathf.Abs(_worldDie.transform.localScale.x);
            if (visualScale > 0.0001f)
            {
                labelObject.transform.localScale =
                    Vector3.one / visualScale;
            }
            _worldDieResult = labelObject.AddComponent<TextMesh>();
            _worldDieResult.anchor = TextAnchor.MiddleCenter;
            _worldDieResult.alignment = TextAlignment.Center;
            _worldDieResult.fontSize = 64;
            _worldDieResult.characterSize = 0.045f;
            _worldDieResult.color = Color.white;
            HideLocalWorldDie();
        }

        private bool TryCreateLocalWorldDieFromVisualPrefab(
            out string error)
        {
            var visualPrefab = worldDieVisualPrefab != null
                ? worldDieVisualPrefab
                : AssetDatabase.LoadAssetAtPath<GameObject>(
                    DefaultWorldDieVisualPrefabPath);
            if (visualPrefab == null)
            {
                error =
                    "assign worldDieVisualPrefab or restore " +
                    DefaultWorldDieVisualPrefabPath + ".";
                return false;
            }

            var sourceFilter = visualPrefab.GetComponent<MeshFilter>();
            var sourceRenderer = visualPrefab.GetComponent<MeshRenderer>();
            var sourceCollider = visualPrefab.GetComponent<MeshCollider>();
            var prefabMarkers =
                visualPrefab.GetComponentsInChildren<WorldDieFaceMarker>(true);
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
            {
                error = "the visual prefab has no root MeshFilter/sharedMesh.";
                return false;
            }
            if (sourceRenderer == null ||
                sourceRenderer.sharedMaterial == null)
            {
                error =
                    "the visual prefab has no root MeshRenderer/sharedMaterial.";
                return false;
            }
            if (sourceCollider == null ||
                sourceCollider.sharedMesh == null ||
                !sourceCollider.convex)
            {
                error =
                    "the visual prefab requires a convex root MeshCollider.";
                return false;
            }
            if (prefabMarkers.Length > 0 &&
                !HasCompleteD12MarkerSet(prefabMarkers))
            {
                error =
                    "the visual prefab has an incomplete D12 marker set.";
                return false;
            }

            _worldDie = Instantiate(visualPrefab);
            _worldDieCollider =
                _worldDie.GetComponent<MeshCollider>();
            _worldDieFaceMarkers =
                _worldDie.GetComponentsInChildren<WorldDieFaceMarker>(true);
            if (_worldDieFaceMarkers.Length == 0)
            {
                _worldDieFaceMarkers =
                    CreateLocalD12FaceMarkers(
                        _worldDie.transform);
            }
            Array.Sort(
                _worldDieFaceMarkers,
                (left, right) => left.Value.CompareTo(right.Value));

            error = string.Empty;
            return true;
        }

        private static WorldDieFaceMarker[] CreateLocalD12FaceMarkers(
            Transform parent)
        {
            var markers =
                new WorldDieFaceMarker[
                    WorldDieAuthorityModel.MaximumFace];
            for (var faceIndex = 0;
                 faceIndex < markers.Length;
                 faceIndex++)
            {
                var faceValue = faceIndex + 1;
                if (!WorldDieD12Layout.TryGetLocalNormal(
                        faceValue,
                        out var normal) ||
                    !WorldDieD12Layout.TryGetLocalMarkerPosition(
                        faceValue,
                        out var markerPosition))
                {
                    continue;
                }
                var markerObject =
                    new GameObject("Face " + faceValue);
                markerObject.transform.SetParent(parent, false);
                markerObject.transform.localPosition =
                    markerPosition;
                markerObject.transform.localRotation =
                    Quaternion.FromToRotation(Vector3.up, normal);
                var marker =
                    markerObject.AddComponent<WorldDieFaceMarker>();
                marker.Configure(faceValue);
                markers[faceIndex] = marker;
            }

            return markers;
        }

        private void CreateFallbackLocalWorldDie()
        {
            _worldDie =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _worldDie.transform.localScale = Vector3.one * 0.8f;
            _worldDieCollider = _worldDie.GetComponent<Collider>();
            _worldDieFaceMarkers =
                new WorldDieFaceMarker[
                    WorldDieAuthorityModel.MaximumFace];
            for (var faceIndex = 0;
                 faceIndex < _worldDieFaceMarkers.Length;
                 faceIndex++)
            {
                var faceValue = faceIndex + 1;
                WorldDieD12Layout.TryGetLocalNormal(
                    faceValue,
                    out var normal);
                var markerObject =
                    new GameObject("Fallback Face " + faceValue);
                markerObject.transform.SetParent(
                    _worldDie.transform,
                    false);
                markerObject.transform.localPosition =
                    normal * 0.5f;
                markerObject.transform.localRotation =
                    Quaternion.FromToRotation(Vector3.up, normal);
                var marker =
                    markerObject.AddComponent<WorldDieFaceMarker>();
                marker.Configure(faceValue);
                _worldDieFaceMarkers[faceIndex] = marker;
            }
        }

        private static bool HasCompleteD12MarkerSet(
            WorldDieFaceMarker[] markers)
        {
            if (markers == null ||
                markers.Length !=
                WorldDieAuthorityModel.MaximumFace)
            {
                return false;
            }

            var seen =
                new bool[
                    WorldDieAuthorityModel.MaximumFace + 1];
            for (var index = 0; index < markers.Length; index++)
            {
                var marker = markers[index];
                if (marker == null ||
                    marker.Value <
                    WorldDieAuthorityModel.MinimumFace ||
                    marker.Value >
                    WorldDieAuthorityModel.MaximumFace ||
                    seen[marker.Value])
                {
                    return false;
                }

                seen[marker.Value] = true;
            }

            return true;
        }

        private void PrepareLocalWorldDie()
        {
            EnsureLocalWorldDie();
            if (_worldDie == null || !_traversal.IsInitialized || player == null)
            {
                return;
            }

            _worldDieTile = _traversal.CurrentTile;
            var forward = Vector3.ProjectOnPlane(player.transform.forward, _worldDieTile.transform.up);
            if (forward.sqrMagnitude <= 0.000001f)
            {
                forward = _worldDieTile.transform.forward;
            }
            var target = player.transform.position + forward.normalized * 2f;
            target = ClampPointInsideTile(target, _worldDieTile, 0.65f);
            target.y = _worldDieTile.WorldCenter.y + 0.75f;
            StopLocalWorldDieNudge();
            ResetLocalWorldDieRollPresentation();
            _worldDie.transform.SetPositionAndRotation(target, UnityEngine.Random.rotation);
            _worldDieResult.text = "?";
            _worldDie.SetActive(true);
            _worldDieBody.detectCollisions = true;
            _worldDieHideDeadline = -1d;
            UpdateWorldDieResultBillboard();
        }

        private void HideLocalWorldDie()
        {
            _worldDieTile = null;
            StopLocalWorldDieNudge();
            ResetLocalWorldDieRollPresentation();
            ClearLocalWorldDiePhysicsSuspension();
            _worldDieHideDeadline = -1d;
            if (_worldDie != null)
            {
                _worldDie.SetActive(false);
            }
            if (_worldDieBody != null)
            {
                _worldDieBody.detectCollisions = false;
            }
        }

        private bool TryGetAimedLocalWorldDie(out Ray ray)
        {
            var origin = eyePivot != null
                ? eyePivot.position
                : player.transform.position + Vector3.up * 0.75f;
            var direction = eyePivot != null ? eyePivot.forward : player.transform.forward;
            ray = new Ray(origin, direction);
            return _worldDie != null && _worldDie.activeSelf &&
                   Physics.Raycast(
                       ray,
                       out var hit,
                       5.5f,
                       Physics.DefaultRaycastLayers,
                       QueryTriggerInteraction.Ignore) &&
                   hit.collider == _worldDieCollider;
        }

        private bool TryGetAimedBoardShop(out RaycastHit hit)
        {
            var origin = eyePivot != null
                ? eyePivot.position
                : player.transform.position + Vector3.up * 0.75f;
            var direction = eyePivot != null ? eyePivot.forward : player.transform.forward;
            if (!Physics.Raycast(
                    new Ray(origin, direction),
                    out hit,
                    ItemShopRules.InteractionDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide))
            {
                return false;
            }

            return hit.collider.GetComponentInParent<KeyShopWorldTarget>() != null ||
                   hit.collider.GetComponentInParent<ItemShopWorldTarget>() != null;
        }

        private void NudgeLocalWorldDie(Ray ray)
        {
            if (_worldDie == null || _worldDieTile == null ||
                _worldDieBody == null || _worldDieCollider == null ||
                _worldDieRollPresentationPhase !=
                WorldDieRollPresentationPhase.None ||
                !_worldDieCollider.Raycast(ray, out var hit, 5.5f))
            {
                return;
            }

            var direction = Vector3.ProjectOnPlane(ray.direction, _worldDieTile.transform.up);
            if (direction.sqrMagnitude <= 0.000001f)
            {
                direction = _worldDieTile.transform.forward;
            }
            direction.Normalize();
            _worldDieBody.isKinematic = false;
            _worldDieBody.detectCollisions = true;
            _worldDieBody.WakeUp();
            _worldDieNudgeInProgress = true;
            _worldDieNudgeDeadline = Time.unscaledTimeAsDouble + 1.5d;
            _worldDieNudgeBelowThresholdSince = -1d;
            var impulse = direction * worldDieNudgeHorizontalImpulse +
                          _worldDieTile.transform.up.normalized *
                          worldDieNudgeUpwardImpulse;
            _worldDieBody.AddForceAtPosition(impulse, hit.point, ForceMode.Impulse);
            if (worldDieNudgeTorqueImpulse > 0f)
            {
                _worldDieBody.AddTorque(
                    UnityEngine.Random.onUnitSphere * worldDieNudgeTorqueImpulse,
                    ForceMode.Impulse);
            }
        }

        private bool BeginLocalWorldDieRoll(Ray ray)
        {
            if (_worldDie == null ||
                !_worldDie.activeSelf ||
                _worldDieTile == null ||
                _worldDieBody == null ||
                _worldDieCollider == null ||
                _roll > 0 ||
                _worldDieRollPresentationPhase !=
                WorldDieRollPresentationPhase.None)
            {
                return false;
            }

            StopLocalWorldDieNudge();
            ResetLocalWorldDieRollPresentation();
            _pendingWorldDieFace = UnityEngine.Random.Range(
                WorldDieAuthorityModel.MinimumFace,
                WorldDieAuthorityModel.MaximumFace + 1);
            _worldDieRollPresentationPhase =
                WorldDieRollPresentationPhase.Tumbling;
            _worldDieRollStartedAt =
                Time.unscaledTimeAsDouble;
            _worldDieHideDeadline = -1d;
            if (_worldDieResult != null)
            {
                _worldDieResult.text = "?";
            }

            _worldDieBody.isKinematic = false;
            _worldDieBody.detectCollisions = true;
            _worldDieBody.WakeUp();
            var up = _worldDieTile.transform.up.normalized;
            var horizontalDirection =
                Vector3.ProjectOnPlane(ray.direction, up);
            if (horizontalDirection.sqrMagnitude <= 0.000001f)
            {
                horizontalDirection =
                    _worldDieTile.transform.forward;
            }

            horizontalDirection.Normalize();
            var impulse =
                horizontalDirection *
                WorldDieRollHorizontalImpulse +
                up * WorldDieRollUpwardImpulse;
            var forcePoint = _worldDieBody.worldCenterOfMass;
            if (_worldDieCollider.Raycast(
                    ray,
                    out var hit,
                    5.5f))
            {
                forcePoint = hit.point;
            }

            _worldDieBody.AddForceAtPosition(
                impulse,
                forcePoint,
                ForceMode.Impulse);
            _worldDieBody.AddTorque(
                UnityEngine.Random.onUnitSphere *
                WorldDieRollTorqueImpulse,
                ForceMode.Impulse);
            _avatarVisual?.TriggerPunch();
            SetStatus(
                "Rolling the local D12. Movement commits after its one-second landing.");
            return true;
        }

        private void UpdateLocalWorldDieRollPresentation()
        {
            if (_worldDieRollPresentationPhase ==
                    WorldDieRollPresentationPhase.None ||
                _worldDie == null ||
                !_worldDie.activeSelf ||
                _worldDieBody == null ||
                _worldDieTile == null ||
                _paused ||
                _keyShopRevealActive)
            {
                return;
            }

            var now = Time.unscaledTimeAsDouble;
            var elapsed = Math.Max(
                0d,
                now - _worldDieRollStartedAt);
            if (_worldDieRollPresentationPhase ==
                    WorldDieRollPresentationPhase.Tumbling &&
                WorldDieRollPresentationPolicy
                    .ShouldBeginLanding(elapsed))
            {
                BeginLocalWorldDieLanding();
            }

            if (_worldDieRollPresentationPhase !=
                WorldDieRollPresentationPhase.Landing)
            {
                return;
            }

            var progress =
                WorldDieRollPresentationPolicy
                    .GetLandingProgress(elapsed);
            var eased =
                progress * progress * (3f - 2f * progress);
            _worldDieBody.MovePosition(
                Vector3.LerpUnclamped(
                    _worldDieLandingStartPosition,
                    _worldDieLandingTargetPosition,
                    eased));
            _worldDieBody.MoveRotation(
                Quaternion.SlerpUnclamped(
                    _worldDieLandingStartRotation,
                    _worldDieLandingTargetRotation,
                    eased));

            if (WorldDieRollPresentationPolicy
                .ShouldCommitResult(elapsed))
            {
                CompleteLocalWorldDieLanding(now);
            }
        }

        private void BeginLocalWorldDieLanding()
        {
            if (!TryGetLocalWorldDieFaceGeometry(
                    _pendingWorldDieFace,
                    out var targetLocalNormal,
                    out var faceSupportDistance))
            {
                Debug.LogError(
                    "Local D12 cannot land on its preselected face. " +
                    "Verify the local D12 marker set contains values 1-12.",
                    this);
                StopLocalWorldDieNudge();
                ResetLocalWorldDieRollPresentation();
                return;
            }

            var currentPosition = _worldDieBody.position;
            var currentRotation = _worldDieBody.rotation;
            _worldDieBody.linearVelocity = Vector3.zero;
            _worldDieBody.angularVelocity = Vector3.zero;
            _worldDieBody.isKinematic = true;

            _worldDieLandingStartPosition =
                currentPosition;
            _worldDieLandingStartRotation =
                currentRotation;
            _worldDieLandingTargetPosition =
                ClampPointInsideTile(
                    currentPosition,
                    _worldDieTile,
                    0.65f);
            var tileUp =
                _worldDieTile.transform.up.normalized;
            var tileSurfacePoint =
                ResolveLocalWorldDieTileSurfacePoint(
                    _worldDieLandingTargetPosition,
                    tileUp);
            var currentCenterHeight = Vector3.Dot(
                _worldDieLandingTargetPosition,
                tileUp);
            var targetCenterHeight =
                WorldDieRollPresentationPolicy
                    .GetLandingCenterHeight(
                        Vector3.Dot(tileSurfacePoint, tileUp),
                        faceSupportDistance,
                        WorldDieLandingClearance);
            _worldDieLandingTargetPosition +=
                tileUp *
                (targetCenterHeight - currentCenterHeight);
            _worldDieLandingTargetRotation =
                WorldDieRollPresentationPolicy
                    .ResolveLandingRotation(
                        currentRotation,
                        targetLocalNormal,
                        tileUp);
            _worldDieRollPresentationPhase =
                WorldDieRollPresentationPhase.Landing;
        }

        private void CompleteLocalWorldDieLanding(double now)
        {
            var settledFace = _pendingWorldDieFace;
            _worldDieBody.position =
                _worldDieLandingTargetPosition;
            _worldDieBody.rotation =
                _worldDieLandingTargetRotation;
            if (!_worldDieBody.isKinematic)
            {
                _worldDieBody.linearVelocity = Vector3.zero;
                _worldDieBody.angularVelocity = Vector3.zero;
                _worldDieBody.isKinematic = true;
            }

            ResetLocalWorldDieRollPresentation();
            _roll = settledFace;
            _remainingMoves = settledFace;
            _actionStates[0] =
                PlayerBoardActionState.Moving;
            if (_worldDieResult != null)
            {
                _worldDieResult.text =
                    settledFace.ToString();
            }

            _worldDieHideDeadline =
                now +
                WorldDieResultPresentationPolicy
                    .DefaultVisibleSeconds;
            if (_traversal.IsInitialized)
            {
                _traversal.ResetMoves(settledFace);
            }

            RefreshBoundaryWalls();
            SetStatus(
                "Rolled " + settledFace +
                ". Blue walls are passable; black walls physically block entry.");
        }

        private bool TryGetLocalWorldDieFaceGeometry(
            int face,
            out Vector3 localNormal,
            out float supportDistance)
        {
            for (var index = 0;
                 index < _worldDieFaceMarkers.Length;
                 index++)
            {
                var marker = _worldDieFaceMarkers[index];
                if (marker == null ||
                    marker.Value != face ||
                    marker.LocalNormal.sqrMagnitude <=
                    0.000001f)
                {
                    continue;
                }

                localNormal =
                    marker.LocalNormal.normalized;
                var worldOffset =
                    marker.transform.position -
                    _worldDie.transform.position;
                supportDistance = Mathf.Abs(
                    Vector3.Dot(
                        worldOffset,
                        marker.transform.up.normalized));
                if (supportDistance <= 0.0001f)
                {
                    supportDistance = worldOffset.magnitude;
                }

                if (supportDistance <= 0.0001f)
                {
                    localNormal = default;
                    supportDistance = 0f;
                    return false;
                }

                return true;
            }

            localNormal = default;
            supportDistance = 0f;
            return false;
        }

        private Vector3 ResolveLocalWorldDieTileSurfacePoint(
            Vector3 horizontalPosition,
            Vector3 tileUp)
        {
            var tileCollider =
                _worldDieTile != null
                    ? _worldDieTile.GetComponent<Collider>()
                    : null;
            if (tileCollider == null || !tileCollider.enabled)
            {
                Debug.LogError(
                    "Local D12 landing requires an enabled collider on its board tile.",
                    this);
                return _worldDieTile != null
                    ? _worldDieTile.WorldCenter
                    : horizontalPosition;
            }

            var probe =
                horizontalPosition +
                tileUp * BoardTile.RoomSize;
            return tileCollider.ClosestPoint(probe);
        }

        private void PreserveLocalWorldDieTimersDuringPause(
            bool timersWereSuspended,
            double pausedDeltaSeconds)
        {
            if (!timersWereSuspended || pausedDeltaSeconds <= 0d)
            {
                return;
            }

            _worldDieRollStartedAt =
                WorldDieRollPresentationPolicy
                    .ShiftTimestampForPause(
                        _worldDieRollStartedAt,
                        pausedDeltaSeconds);
            _worldDieHideDeadline =
                WorldDieRollPresentationPolicy
                    .ShiftTimestampForPause(
                        _worldDieHideDeadline,
                        pausedDeltaSeconds);
            _worldDieNudgeDeadline =
                WorldDieRollPresentationPolicy
                    .ShiftTimestampForPause(
                        _worldDieNudgeDeadline,
                        pausedDeltaSeconds);
            _worldDieNudgeBelowThresholdSince =
                WorldDieRollPresentationPolicy
                    .ShiftTimestampForPause(
                        _worldDieNudgeBelowThresholdSince,
                        pausedDeltaSeconds);
        }

        private void SetLocalWorldDiePhysicsSuspended(bool suspended)
        {
            if (_worldDiePhysicsSuspended == suspended)
            {
                return;
            }

            _worldDiePhysicsSuspended = suspended;
            if (_worldDieBody == null)
            {
                return;
            }

            if (suspended)
            {
                _worldDieWasKinematicBeforeSuspension =
                    _worldDieBody.isKinematic;
                _worldDieSuspendedLinearVelocity =
                    _worldDieBody.linearVelocity;
                _worldDieSuspendedAngularVelocity =
                    _worldDieBody.angularVelocity;
                if (!_worldDieBody.isKinematic)
                {
                    _worldDieBody.isKinematic = true;
                }

                return;
            }

            var shouldRestoreDynamicBody =
                !_worldDieWasKinematicBeforeSuspension &&
                _worldDie != null &&
                _worldDie.activeSelf &&
                (_worldDieNudgeInProgress ||
                 _worldDieRollPresentationPhase ==
                 WorldDieRollPresentationPhase.Tumbling);
            if (shouldRestoreDynamicBody)
            {
                _worldDieBody.isKinematic = false;
                _worldDieBody.linearVelocity =
                    _worldDieSuspendedLinearVelocity;
                _worldDieBody.angularVelocity =
                    _worldDieSuspendedAngularVelocity;
                _worldDieBody.WakeUp();
            }

            _worldDieWasKinematicBeforeSuspension = true;
            _worldDieSuspendedLinearVelocity = Vector3.zero;
            _worldDieSuspendedAngularVelocity = Vector3.zero;
        }

        private void ClearLocalWorldDiePhysicsSuspension()
        {
            _worldDiePhysicsSuspended = false;
            _worldDieWasKinematicBeforeSuspension = true;
            _worldDieSuspendedLinearVelocity = Vector3.zero;
            _worldDieSuspendedAngularVelocity = Vector3.zero;
        }

        private void ResetLocalWorldDieRollPresentation()
        {
            if (_worldDieBody != null &&
                _worldDieRollPresentationPhase !=
                WorldDieRollPresentationPhase.None)
            {
                if (!_worldDieBody.isKinematic)
                {
                    _worldDieBody.linearVelocity =
                        Vector3.zero;
                    _worldDieBody.angularVelocity =
                        Vector3.zero;
                    _worldDieBody.isKinematic = true;
                }
            }

            _worldDieRollPresentationPhase =
                WorldDieRollPresentationPhase.None;
            _pendingWorldDieFace = 0;
            _worldDieRollStartedAt = -1d;
            _worldDieLandingStartPosition = default;
            _worldDieLandingStartRotation =
                Quaternion.identity;
            _worldDieLandingTargetPosition = default;
            _worldDieLandingTargetRotation =
                Quaternion.identity;
        }

        private void UpdateLocalWorldDieLifetime()
        {
            if (_worldDie != null && _worldDie.activeSelf && _roll > 0 &&
                _worldDieRollPresentationPhase ==
                WorldDieRollPresentationPhase.None &&
                _worldDieHideDeadline >= 0d &&
                Time.unscaledTimeAsDouble >= _worldDieHideDeadline)
            {
                HideLocalWorldDie();
            }
        }

        private bool IsLocalWorldDieResultPublic(double now)
        {
            return _roll > 0 &&
                   _worldDie != null &&
                   _worldDie.activeSelf &&
                   _worldDieRollPresentationPhase ==
                   WorldDieRollPresentationPhase.None &&
                   _worldDieHideDeadline >= 0d &&
                   now < _worldDieHideDeadline;
        }

        private string ResolveLocalWorldDieHudLabel()
        {
            if (_flow.State == BoardFlowState.CombatResolve)
            {
                return IsLocalCombatAlive(0)
                    ? "LMB  PUNCH"
                    : "FIGHT  SPECTATING";
            }

            if (_worldDieRollPresentationPhase !=
                WorldDieRollPresentationPhase.None)
            {
                return "DICE  ROLLING...";
            }

            if (_roll > 0)
            {
                return IsLocalWorldDieResultPublic(
                    Time.unscaledTimeAsDouble)
                    ? "DICE  " + _roll
                    : "DICE  ROLL COMPLETE";
            }

            return _flow.ActionClock.IsChoicePending
                ? "DICE  CHOOSE ITEM FIRST"
                : "AIM AT WORLD DIE / RMB ROLL";
        }

        private void UpdateLocalWorldDiePhysics()
        {
            var isPhysicalTumble =
                _worldDieRollPresentationPhase ==
                WorldDieRollPresentationPhase.Tumbling;
            if ((!_worldDieNudgeInProgress && !isPhysicalTumble) ||
                _worldDieBody == null ||
                _worldDieTile == null || !_worldDie.activeSelf)
            {
                return;
            }

            var clamped = ClampPointInsideTile(
                _worldDieBody.position,
                _worldDieTile,
                0.65f);
            clamped.y = _worldDieBody.position.y;
            if ((clamped - _worldDieBody.position).sqrMagnitude > 0.000001f)
            {
                _worldDieBody.position = clamped;
                var up = _worldDieTile.transform.up.normalized;
                _worldDieBody.linearVelocity =
                    Vector3.Project(_worldDieBody.linearVelocity, up);
            }

            if (isPhysicalTumble)
            {
                return;
            }

            var belowThreshold =
                _worldDieBody.linearVelocity.magnitude <= 0.06f &&
                _worldDieBody.angularVelocity.magnitude <= 0.1f;
            if (belowThreshold)
            {
                if (_worldDieNudgeBelowThresholdSince < 0d)
                {
                    _worldDieNudgeBelowThresholdSince = Time.unscaledTimeAsDouble;
                }
            }
            else
            {
                _worldDieNudgeBelowThresholdSince = -1d;
            }

            if (Time.unscaledTimeAsDouble >= _worldDieNudgeDeadline ||
                (_worldDieNudgeBelowThresholdSince >= 0d &&
                 Time.unscaledTimeAsDouble - _worldDieNudgeBelowThresholdSince >= 0.15d))
            {
                StopLocalWorldDieNudge();
            }
        }

        private void StopLocalWorldDieNudge()
        {
            if (_worldDieBody != null)
            {
                if (!_worldDieBody.isKinematic)
                {
                    _worldDieBody.linearVelocity = Vector3.zero;
                    _worldDieBody.angularVelocity = Vector3.zero;
                }
                _worldDieBody.isKinematic = true;
            }
            _worldDieNudgeInProgress = false;
            _worldDieNudgeDeadline = -1d;
            _worldDieNudgeBelowThresholdSince = -1d;
        }

        private void UpdateWorldDieResultBillboard()
        {
            if (_worldDie == null || !_worldDie.activeSelf || _worldDieResult == null)
            {
                return;
            }

            var labelTransform = _worldDieResult.transform;
            labelTransform.position = _worldDie.transform.position + Vector3.up * 0.9f;
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                var awayFromCamera = labelTransform.position - mainCamera.transform.position;
                if (awayFromCamera.sqrMagnitude > 0.000001f)
                {
                    labelTransform.rotation = Quaternion.LookRotation(
                        awayFromCamera.normalized,
                        Vector3.up);
                }
            }
        }

        private static Vector3 ClampPointInsideTile(
            Vector3 point,
            BoardTile tile,
            float margin)
        {
            var offset = point - tile.WorldCenter;
            var right = tile.transform.right.normalized;
            var forward = tile.transform.forward.normalized;
            var up = tile.transform.up.normalized;
            var extent = Mathf.Max(0.1f, BoardTile.HalfRoomSize - Mathf.Max(0f, margin));
            return tile.WorldCenter +
                   right * Mathf.Clamp(Vector3.Dot(offset, right), -extent, extent) +
                   forward * Mathf.Clamp(Vector3.Dot(offset, forward), -extent, extent) +
                   up * Vector3.Dot(offset, up);
        }

        private void InitializePlayerStatsAndBoardEffects()
        {
            _occupiedMask = 0;
            _selectedSlot = -1;
            for (var itemSlot = 0; itemSlot < _itemSlots.Length; itemSlot++)
            {
                _itemSlots[itemSlot] = PrototypeItemId.None;
            }

            for (var slot = 0; slot < BoardFlowStateMachine.RequiredPlayerCount; slot++)
            {
                _maxHealth[slot] = PlayerStatRules.DefaultMaxHealth;
                _currentHealth[slot] = PlayerStatRules.DefaultMaxHealth;
                _keys[slot] = PlayerStatRules.DefaultStartingKeys;
                _gold[slot] = PlayerStatRules.DefaultStartingGold;
                _minigameWins[slot] = 0;
                _actionStates[slot] = PlayerBoardActionState.Hidden;
            }

            if (topology == null)
            {
                return;
            }

            var seed = UnityEngine.Random.Range(1, int.MaxValue);
            _boardEffectLayout = BoardLandingEffectLayout.Create(topology.Tiles, seed);
            for (var i = 0; i < topology.Tiles.Count; i++)
            {
                var tile = topology.Tiles[i];
                if (tile == null)
                {
                    continue;
                }

                var effect = _boardEffectLayout.TryGetEffect(tile.Coordinate, out var assigned)
                    ? assigned
                    : BoardLandingEffectType.None;
                tile.ApplyLandingEffectPresentation(effect);
            }
        }

        private void StageRemotePlayerTwoAtLocalTile()
        {
            if (!_traversal.IsInitialized || player == null)
            {
                return;
            }

            var marker = GetRemoteMarker(1);
            if (marker == null)
            {
                return;
            }

            var forward = Vector3.ProjectOnPlane(
                player.transform.forward,
                _traversal.CurrentTile.transform.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = _traversal.CurrentTile.transform.forward;
            }
            marker.position = ClampPointInsideTile(
                player.transform.position + forward.normalized * 1.5f,
                _traversal.CurrentTile,
                0.6f);
            marker.position = new Vector3(
                marker.position.x,
                _traversal.CurrentTile.GetRecoveryCenter(1f).y,
                marker.position.z);
            _stageTwoPlayerFightAtFinish = false;
        }

        private void BeginLocalCombatSequence()
        {
            ResetLocalCombat();
            var rankingStats = new PlayerRankingStats[_playerRows.Length];
            var placements = new List<BoardCombatPlacement>(_playerRows.Length);
            for (var slot = 0; slot < _playerRows.Length; slot++)
            {
                rankingStats[slot] = new PlayerRankingStats(
                    _keys[slot],
                    _gold[slot],
                    _minigameWins[slot]);
                var tile = GetLocalSimulationTile(slot);
                if (tile == null)
                {
                    continue;
                }
                if (double.IsNaN(_arrivalTimes[slot]))
                {
                    _arrivalTimes[slot] = _simulationNow + slot * 0.001d;
                }
                placements.Add(new BoardCombatPlacement(
                    slot,
                    tile.Coordinate,
                    _arrivalTimes[slot]));
            }

            var ranks = PlayerRankingRules.Calculate(rankingStats);
            var groups = BoardCombatRules.BuildInitialQueue(placements, ranks);
            for (var i = 0; i < groups.Count; i++)
            {
                _combatQueue.Enqueue(groups[i]);
            }
            BeginNextLocalCombatOrFinish();
        }

        private void BeginNextLocalCombatOrFinish()
        {
            while (_combatQueue.Count > 0 &&
                   _localFightsResolved < BoardCombatRules.MaximumFightsPerTurn)
            {
                var group = _combatQueue.Dequeue();
                var mask = GetLocalOccupantMask(group.Coordinate);
                if (!HasMultipleLocalCombatants(mask))
                {
                    continue;
                }

                _localCombatActive = true;
                _localCombatParticipantMask = mask;
                _localCombatAliveMask = mask;
                _localCombatTile = group.Coordinate;
                _localCombatEndsAt = _flow.ToFlowTime(_simulationNow) +
                                     BoardCombatRules.FightDurationSeconds;
                _nextLocalPunchAllowedAt = _flow.ToFlowTime(_simulationNow);
                _localCombatSequenceIndex++;
                for (var slot = 0; slot < _combatHealth.Length; slot++)
                {
                    _combatEliminatedAt[slot] = double.NaN;
                    _combatHealth[slot] = IsLocalCombatParticipant(slot)
                        ? BoardCombatRules.TemporaryHealth
                        : 0;
                    _actionStates[slot] = IsLocalCombatParticipant(slot)
                        ? PlayerBoardActionState.Fighting
                        : PlayerBoardActionState.Hidden;
                }

                if (topology != null &&
                    topology.TryGetTile(_localCombatTile, out var tile) && tile != null)
                {
                    cameraDirector?.SetCombatSpectatorFocus(tile.WorldCenter);
                }
                var localFighting = IsLocalCombatAlive(0);
                cameraDirector?.SwitchTo(localFighting
                    ? GameplayMode.FirstPerson
                    : GameplayMode.CombatSpectator);
                ApplyBodyVisibility(localFighting);
                RefreshBoundaryWalls();
                SetStatus(localFighting
                    ? "EDITOR FIGHT: WASD moves in-room. LMB punches P2 for 5 temporary HP."
                    : "EDITOR SPECTATOR: input locked while the current fight resolves.");
                return;
            }

            _localCombatActive = false;
            _localCombatParticipantMask = 0;
            _localCombatAliveMask = 0;
            _localCombatEndsAt = 0d;
            _combatQueue.Clear();
            _boundaryWalls?.Hide();
            _flow.TryCompleteCombat(_simulationNow);
        }

        private void AdvanceLocalCombat()
        {
            if (!_localCombatActive || _paused || _keyShopRevealActive ||
                _flow.ToFlowTime(_simulationNow) < _localCombatEndsAt)
            {
                return;
            }

            ResolveCurrentLocalCombat();
        }

        private void TryLocalCombatPunch()
        {
            var flowNow = _flow.ToFlowTime(_simulationNow);
            if (!_localCombatActive || !IsLocalCombatAlive(0) ||
                flowNow < _nextLocalPunchAllowedAt)
            {
                return;
            }

            _nextLocalPunchAllowedAt = flowNow + BoardCombatRules.PunchCooldownSeconds;
            _avatarVisual?.TriggerPunch();
            var origin = eyePivot != null
                ? eyePivot.position
                : player.transform.position + Vector3.up * 0.75f;
            var direction = eyePivot != null
                ? eyePivot.forward.normalized
                : player.transform.forward.normalized;
            var targetSlot = -1;
            var bestDistance = float.MaxValue;
            for (var slot = 1; slot < BoardFlowStateMachine.RequiredPlayerCount; slot++)
            {
                if (!IsLocalCombatAlive(slot))
                {
                    continue;
                }
                var marker = GetRemoteMarker(slot);
                if (marker == null)
                {
                    continue;
                }
                var toTarget = marker.position + Vector3.up * 0.5f - origin;
                var distance = toTarget.magnitude;
                if (distance > BoardCombatRules.PunchRange + 0.5f ||
                    distance >= bestDistance ||
                    Vector3.Dot(direction, toTarget.normalized) < 0.72f)
                {
                    continue;
                }
                targetSlot = slot;
                bestDistance = distance;
            }

            if (targetSlot < 0)
            {
                SetStatus("EDITOR FIGHT: punch missed.");
                return;
            }

            var target = GetRemoteMarker(targetSlot);
            var push = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (push.sqrMagnitude > 0.0001f && target != null && topology != null &&
                topology.TryGetTile(_localCombatTile, out var combatTile))
            {
                target.position = ClampPointInsideTile(
                    target.position + push.normalized * 0.28f,
                    combatTile,
                    0.6f);
            }
            ApplyLocalCombatDamage(targetSlot);
            target?.GetComponent<PlayerAvatarVisual>()?.TriggerHit(PlayerHitRegion.Body);
            SetStatus("EDITOR FIGHT: P" + (targetSlot + 1) +
                      " took 5 temporary HP damage.");
        }

        private bool ShouldRepeatLocalPrimaryAction(
            Mouse mouse,
            double intervalSeconds)
        {
            if (mouse == null || !mouse.leftButton.isPressed)
            {
                _nextLocalPrimaryRepeatAt = 0d;
                return false;
            }

            var now = Time.unscaledTimeAsDouble;
            if (!mouse.leftButton.wasPressedThisFrame &&
                now < _nextLocalPrimaryRepeatAt)
            {
                return false;
            }

            _nextLocalPrimaryRepeatAt =
                now + Math.Max(0.01d, intervalSeconds);
            return true;
        }

        private void TryLocalBoardPunch()
        {
            var flowNow = _flow.ToFlowTime(_simulationNow);
            if (flowNow < _nextLocalBoardPunchAllowedAt || player == null)
            {
                return;
            }

            _nextLocalBoardPunchAllowedAt =
                flowNow + PlayerUnarmedRules.PunchCooldownSeconds;
            _avatarVisual?.TriggerPunch();
            var origin = eyePivot != null
                ? eyePivot.position
                : player.transform.position + Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
            var direction = eyePivot != null
                ? eyePivot.forward.normalized
                : player.transform.forward.normalized;
            var hits = Physics.SphereCastAll(
                origin,
                PlayerUnarmedRules.PunchRadius,
                direction,
                PlayerUnarmedRules.PunchRange,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            for (var index = 0; index < hits.Length; index++)
            {
                var collider = hits[index].collider;
                if (collider == null || collider.transform == player.transform ||
                    collider.transform.IsChildOf(player.transform))
                {
                    continue;
                }

                var zone = collider.GetComponent<PlayerHitZone>();
                var targetVisual = zone != null
                    ? collider.GetComponentInParent<PlayerAvatarVisual>()
                    : null;
                if (zone != null && targetVisual != null && targetVisual != _avatarVisual)
                {
                    targetVisual.TriggerHit(zone.Region);
                    SetStatus("EDITOR PUNCH: hit " + zone.Region +
                              ". HP unchanged; visual wobble only.");
                    return;
                }

                if (collider.GetComponentInParent<PlayerHitZoneOwner>() != null)
                {
                    continue;
                }
                if (!collider.isTrigger)
                {
                    break;
                }
            }

            SetStatus("EDITOR PUNCH: missed. HP unchanged.");
        }

        private void ApplyLocalCombatDamage(int slot)
        {
            if (!IsLocalCombatAlive(slot))
            {
                return;
            }

            _combatHealth[slot] = Mathf.Max(
                0,
                _combatHealth[slot] - BoardCombatRules.PunchDamage);
            if (_combatHealth[slot] > 0)
            {
                return;
            }

            _combatEliminatedAt[slot] = _flow.ToFlowTime(_simulationNow);
            _localCombatAliveMask = (byte)(_localCombatAliveMask & ~(1 << slot));
            _actionStates[slot] = PlayerBoardActionState.Hidden;
            if (slot == 0)
            {
                cameraDirector?.SwitchTo(GameplayMode.CombatSpectator);
                ApplyBodyVisibility(false);
            }
            if (BoardCombatRules.IsFightComplete(
                    _localCombatParticipantMask,
                    _localCombatAliveMask))
            {
                ResolveCurrentLocalCombat();
            }
        }

        private void ResolveCurrentLocalCombat()
        {
            if (!_localCombatActive)
            {
                return;
            }

            var rankingStats = new PlayerRankingStats[_playerRows.Length];
            for (var slot = 0; slot < rankingStats.Length; slot++)
            {
                rankingStats[slot] = new PlayerRankingStats(
                    _keys[slot],
                    _gold[slot],
                    _minigameWins[slot]);
            }
            var overallRanks = PlayerRankingRules.Calculate(rankingStats);
            var entries = new List<BoardCombatRankingEntry>();
            for (var slot = 0; slot < _combatHealth.Length; slot++)
            {
                if (IsLocalCombatParticipant(slot))
                {
                    entries.Add(new BoardCombatRankingEntry(
                        slot,
                        _combatHealth[slot],
                        _combatEliminatedAt[slot],
                        _arrivalTimes[slot],
                        overallRanks[slot]));
                }
            }

            var standings = BoardCombatRules.ResolveStandings(entries);
            var chainCandidates = new List<Vector2Int>();
            for (var i = 0; i < standings.Length; i++)
            {
                var standing = standings[i];
                RetreatLocalSimulationPlayer(
                    standing.Slot,
                    standing.RetreatDistance);
                var tile = GetLocalSimulationTile(standing.Slot);
                if (tile != null && !chainCandidates.Contains(tile.Coordinate))
                {
                    chainCandidates.Add(tile.Coordinate);
                }
                if (standing.Slot == 0)
                {
                    _pendingLocalCombatProtection = true;
                }
                _actionStates[standing.Slot] = PlayerBoardActionState.Hidden;
                _combatHealth[standing.Slot] = 0;
            }

            _localCombatActive = false;
            _localCombatParticipantMask = 0;
            _localCombatAliveMask = 0;
            _localCombatEndsAt = 0d;
            _localFightsResolved++;
            for (var i = 0; i < chainCandidates.Count; i++)
            {
                var mask = GetLocalOccupantMask(chainCandidates[i]);
                if (HasMultipleLocalCombatants(mask) &&
                    !LocalQueueContains(chainCandidates[i]))
                {
                    _combatQueue.Enqueue(new BoardCombatGroup(
                        chainCandidates[i],
                        mask,
                        _flow.ToFlowTime(_simulationNow),
                        false));
                }
            }
            BeginNextLocalCombatOrFinish();
        }

        private void RetreatLocalSimulationPlayer(int slot, int distance)
        {
            if (distance <= 0 || topology == null)
            {
                return;
            }

            if (slot == 0 && _traversal.IsInitialized)
            {
                var actual = _traversal.Retreat(distance).Length;
                while (actual < distance &&
                       TryGetDeterministicIncoming(_traversal.CurrentTile, out var previous))
                {
                    _traversal.Relocate(previous);
                    actual++;
                }
                Teleport(_traversal.CurrentTile.GetRecoveryCenter(1f));
                return;
            }

            var marker = GetRemoteMarker(slot);
            var tile = marker != null
                ? topology.FindContainingTile(marker.position, 0.1f)
                : null;
            for (var step = 0; step < distance && tile != null; step++)
            {
                if (!TryGetDeterministicIncoming(tile, out tile))
                {
                    break;
                }
            }
            if (marker != null && tile != null)
            {
                marker.position = tile.GetRecoveryCenter(1f);
            }
        }

        private bool TryGetDeterministicIncoming(BoardTile tile, out BoardTile previous)
        {
            previous = null;
            var incoming = topology.GetIncomingGates(tile);
            for (var i = 0; i < incoming.Count; i++)
            {
                var candidate = incoming[i] != null ? incoming[i].Source : null;
                if (candidate == null || previous != null &&
                    CompareCoordinates(candidate.Coordinate, previous.Coordinate) >= 0)
                {
                    continue;
                }
                previous = candidate;
            }
            return previous != null;
        }

        private byte GetLocalOccupantMask(Vector2Int coordinate)
        {
            byte mask = 0;
            for (var slot = 0; slot < BoardFlowStateMachine.RequiredPlayerCount; slot++)
            {
                var tile = GetLocalSimulationTile(slot);
                if (tile != null && tile.Coordinate == coordinate)
                {
                    mask = (byte)(mask | (1 << slot));
                }
            }
            return mask;
        }

        private bool LocalQueueContains(Vector2Int coordinate)
        {
            foreach (var group in _combatQueue)
            {
                if (group.Coordinate == coordinate)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsLocalCombatParticipant(int slot)
        {
            return slot >= 0 && slot < BoardFlowStateMachine.RequiredPlayerCount &&
                   (_localCombatParticipantMask & (1 << slot)) != 0;
        }

        private bool IsLocalCombatAlive(int slot)
        {
            return slot >= 0 && slot < BoardFlowStateMachine.RequiredPlayerCount &&
                   (_localCombatAliveMask & (1 << slot)) != 0;
        }

        private static bool HasMultipleLocalCombatants(byte mask)
        {
            return mask != 0 && (mask & (mask - 1)) != 0;
        }

        private static int CompareCoordinates(Vector2Int left, Vector2Int right)
        {
            var x = left.x.CompareTo(right.x);
            return x != 0 ? x : left.y.CompareTo(right.y);
        }

        private Transform GetRemoteMarker(int slot)
        {
            if (slot <= 0 || slot >= BoardFlowStateMachine.RequiredPlayerCount)
            {
                return null;
            }
            var marker = GameObject.Find("Simulated Remote Player " + (slot + 1));
            return marker != null ? marker.transform : null;
        }

        private void ResetLocalCombat()
        {
            _combatQueue.Clear();
            _localCombatActive = false;
            _localCombatParticipantMask = 0;
            _localCombatAliveMask = 0;
            _localCombatEndsAt = 0d;
            _localCombatSequenceIndex = 0;
            _localFightsResolved = 0;
            for (var slot = 0; slot < _combatHealth.Length; slot++)
            {
                _combatHealth[slot] = 0;
                _combatEliminatedAt[slot] = double.NaN;
            }
        }

        private void BeginLocalLandingEffects()
        {
            _nextLandingEffectSlot = 0;
            ResolveNextLocalLandingEffect();
        }

        private void AdvanceLocalLandingEffects()
        {
            if (_flow.State != BoardFlowState.LandingEffectResolve)
            {
                return;
            }

            var elapsed = Math.Max(0d, _flow.ToFlowTime(_simulationNow) - _flow.StateStartedAt);
            var targetResolvedCount = Mathf.Clamp(
                Mathf.FloorToInt((float)elapsed) + 1,
                1,
                BoardFlowStateMachine.RequiredPlayerCount);
            while (_nextLandingEffectSlot < targetResolvedCount)
            {
                ResolveNextLocalLandingEffect();
            }
        }

        private void ResolveAllRemainingLocalLandingEffects()
        {
            if (_boardEffectLayout == null || topology == null)
            {
                return;
            }

            while (_nextLandingEffectSlot < BoardFlowStateMachine.RequiredPlayerCount)
            {
                ResolveNextLocalLandingEffect();
            }
        }

        private void ResolveNextLocalLandingEffect()
        {
            if (_boardEffectLayout == null || topology == null ||
                _nextLandingEffectSlot >= BoardFlowStateMachine.RequiredPlayerCount)
            {
                return;
            }

            var slot = _nextLandingEffectSlot++;
            var tile = GetLocalSimulationTile(slot);
            if (tile == null ||
                !_boardEffectLayout.TryGetEffect(tile.Coordinate, out var effect))
            {
                return;
            }

            switch (effect)
            {
                case BoardLandingEffectType.GoldGain:
                case BoardLandingEffectType.GoldLoss:
                    var goldDelta = BoardLandingEffectLayout.GetGoldDelta(effect);
                    _gold[slot] = PlayerStatRules.ApplyGoldDelta(_gold[slot], goldDelta);
                    SetStatus("P" + (slot + 1) + " landing effect: " +
                              (goldDelta >= 0 ? "+" : string.Empty) + goldDelta + " gold.");
                    break;
                case BoardLandingEffectType.Healing:
                    var before = _currentHealth[slot];
                    _currentHealth[slot] = PlayerStatRules.ClampHealth(
                        before + BoardLandingEffectLayout.HealingAmount,
                        _maxHealth[slot]);
                    SetStatus("P" + (slot + 1) + " landing effect: healed " +
                              (_currentHealth[slot] - before) + " HP.");
                    break;
                case BoardLandingEffectType.ItemReward:
                    if (slot != 0)
                    {
                        SetStatus("P" + (slot + 1) +
                                  " landing effect: simulated random item reward.");
                        break;
                    }

                    var seed = unchecked(
                        _boardEffectLayout.Seed * 397 ^
                        _flow.CurrentTurn * 31 ^
                        tile.Coordinate.GetHashCode());
                    var reward = PrototypeItemCatalog.GetRandomId(new System.Random(seed));
                    SetStatus(TryAddLocalItem(reward)
                        ? "P1 landing effect: received " +
                          PrototypeItemCatalog.Get(reward).DisplayName + "."
                        : "P1 landing effect: inventory full; item reward was not received.");
                    break;
            }
        }

        private BoardTile GetLocalSimulationTile(int slot)
        {
            if (slot == 0)
            {
                return _traversal.IsInitialized ? _traversal.CurrentTile : null;
            }

            var marker = GameObject.Find("Simulated Remote Player " + (slot + 1));
            return marker != null
                ? topology.FindContainingTile(marker.transform.position, 0.1f)
                : null;
        }

        private void SetAllActionStates(PlayerBoardActionState state)
        {
            for (var slot = 0; slot < _actionStates.Length; slot++)
            {
                _actionStates[slot] = state;
            }
        }

        private void TryPlaceLocalKeyShop(int turn)
        {
            if (turn != KeyShopRuntimeState.InitialPlacementTurn ||
                _keyShopState == null || topology == null)
            {
                return;
            }

            var occupied = GetOccupiedBoardCoordinates(true);
            if (_keyShopState.TryBeginInitialPlacement(
                    turn,
                    topology.Tiles,
                    occupied,
                    out _))
            {
                ApplyLocalKeyShopState();
                _keyShopState.TryCompleteAppearance();
                ApplyLocalKeyShopState();
                SetStatus("A single Key Shop appeared on an unoccupied random room.");
            }
        }

        private void ApplyLocalKeyShopState()
        {
            _keyShopMarker?.ApplyReplicatedState(
                _keyShopState.State,
                _keyShopState.HasLocation,
                _keyShopState.Location,
                topology,
                Mathf.Max(0, _keyShopState.PlacementRevision));
        }

        private void RefreshLocalItemShops(int turn)
        {
            if (topology == null || turn < 1)
            {
                return;
            }

            for (var shopIndex = 0; shopIndex < ItemShopRules.ShopCount; shopIndex++)
            {
                var stock = _itemShopStocks[shopIndex];
                var expired = _itemShopActive[shopIndex] &&
                              turn - _itemShopAppearedTurns[shopIndex] >=
                              ItemShopRules.TurnsBeforeRefresh;
                if (!_itemShopActive[shopIndex] || stock == null ||
                    stock.IsSoldOut || expired)
                {
                    TryPlaceLocalItemShop(shopIndex, turn);
                }
                else
                {
                    ApplyLocalItemShopState(shopIndex);
                }
            }
        }

        private bool TryPlaceLocalItemShop(int shopIndex, int turn)
        {
            if (topology == null || shopIndex < 0 ||
                shopIndex >= ItemShopRules.ShopCount)
            {
                return false;
            }

            var occupied = GetOccupiedBoardCoordinates(false);
            var reserved = new List<Vector2Int>();
            if (_keyShopState != null && _keyShopState.HasLocation)
            {
                reserved.Add(_keyShopState.Location);
            }
            for (var other = 0; other < ItemShopRules.ShopCount; other++)
            {
                if (other != shopIndex && _itemShopActive[other])
                {
                    reserved.Add(_itemShopLocations[other]);
                }
            }

            var hadPrevious = _itemShopActive[shopIndex];
            var previous = _itemShopLocations[shopIndex];
            if (!ItemShopPlacementPolicy.TryChoose(
                    topology.Tiles,
                    occupied,
                    reserved,
                    UnityKeyShopRandomSource.Shared,
                    out var selectedTile,
                    hadPrevious,
                    previous))
            {
                return false;
            }

            _itemShopStocks[shopIndex] = new ItemShopStock(
                UnityEngine.Random.Range(1, int.MaxValue));
            _itemShopLocations[shopIndex] = selectedTile.Coordinate;
            _itemShopAppearedTurns[shopIndex] = turn;
            _itemShopRevisions[shopIndex]++;
            _itemShopActive[shopIndex] = true;
            ApplyLocalItemShopState(shopIndex);
            return true;
        }

        private List<Vector2Int> GetOccupiedBoardCoordinates(bool includeItemShops)
        {
            var occupied = new List<Vector2Int>();
            if (_traversal.IsInitialized)
            {
                occupied.Add(_traversal.CurrentTile.Coordinate);
            }

            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (!transforms[i].name.StartsWith(
                        "Simulated Remote Player",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var tile = topology != null
                    ? topology.FindContainingTile(transforms[i].position, 0.1f)
                    : null;
                if (tile != null && !occupied.Contains(tile.Coordinate))
                {
                    occupied.Add(tile.Coordinate);
                }
            }

            if (includeItemShops)
            {
                for (var shopIndex = 0;
                     shopIndex < ItemShopRules.ShopCount;
                     shopIndex++)
                {
                    if (_itemShopActive[shopIndex] &&
                        !occupied.Contains(_itemShopLocations[shopIndex]))
                    {
                        occupied.Add(_itemShopLocations[shopIndex]);
                    }
                }
            }

            return occupied;
        }

        private void ApplyLocalItemShopState(int shopIndex)
        {
            if (shopIndex < 0 || shopIndex >= ItemShopRules.ShopCount)
            {
                return;
            }

            var stock = _itemShopStocks[shopIndex];
            _itemShopMarker?.ApplyReplicatedState(
                shopIndex,
                _itemShopActive[shopIndex],
                _itemShopLocations[shopIndex],
                stock != null && stock.IsSoldOut,
                topology,
                Mathf.Max(0, _itemShopRevisions[shopIndex]));
        }

        private void TryPurchaseLocalKey()
        {
            if (_keyShopState == null || !_keyShopState.IsActive ||
                topology == null || !_traversal.IsInitialized ||
                _traversal.CurrentTile.Coordinate != _keyShopState.Location)
            {
                SetStatus("Key Shop purchase failed: stand in the shop room and aim at it.");
                return;
            }
            if (!PlayerStatRules.CanPurchaseKey(_gold[0]))
            {
                SetStatus("Key Shop purchase failed: P1 needs 20 gold.");
                return;
            }
            if (!_flow.Pause(_simulationNow))
            {
                return;
            }

            var occupied = GetOccupiedBoardCoordinates(true);
            if (!_keyShopState.TryBeginPurchaseRelocation(
                    topology.Tiles,
                    occupied,
                    out _))
            {
                _flow.Resume(_simulationNow);
                SetStatus("Key Shop could not find a valid relocation room.");
                return;
            }

            _gold[0] = PlayerStatRules.ApplyGoldDelta(
                _gold[0],
                -PlayerStatRules.KeyShopGoldPrice);
            _keys[0] = PlayerStatRules.AddKeys(_keys[0], 1);
            _keyShopRevealActive = true;
            _keyShopRevealEndsAt = _simulationNow + 5d;
            _editorPointerVisible = false;
            StopLocalWorldDieNudge();
            ApplyLocalKeyShopState();
            cameraDirector?.SwitchTo(GameplayMode.BoardTopDown);
            SetStatus("Key purchased. Global action paused for the five-second shop relocation view.");
        }

        private void AdvanceLocalKeyShopReveal()
        {
            if (!_keyShopRevealActive || _simulationNow < _keyShopRevealEndsAt)
            {
                return;
            }

            _keyShopState?.TryCompleteAppearance();
            ApplyLocalKeyShopState();
            _keyShopRevealActive = false;
            _keyShopRevealEndsAt = 0d;
            _flow.Resume(_simulationNow);
            cameraDirector?.SwitchTo(GameplayMode.FirstPerson);
            RefreshBoundaryWalls();
            SetStatus("Key Shop relocation complete. The exact action state resumed.");
        }

        private bool CanAccessLocalItemShop(int shopIndex)
        {
            if (shopIndex < 0 || shopIndex >= ItemShopRules.ShopCount ||
                !_itemShopActive[shopIndex] || _itemShopStocks[shopIndex] == null ||
                !_traversal.IsInitialized ||
                _traversal.CurrentTile.Coordinate != _itemShopLocations[shopIndex] ||
                player == null)
            {
                return false;
            }

            var marker = _itemShopMarker?.GetMarkerObject(shopIndex);
            return marker != null &&
                   Vector3.Distance(player.transform.position, marker.transform.position) <=
                   ItemShopRules.InteractionDistance;
        }

        private void OpenLocalItemShop(int shopIndex)
        {
            if (!CanAccessLocalItemShop(shopIndex))
            {
                SetStatus("Item Shop is out of interaction range.");
                return;
            }

            _openItemShopIndex = shopIndex;
            _editorPointerVisible = false;
            SetStatus("Item Shop opened. The global timer continues; movement and attacks are locked.");
        }

        public void CloseLocalItemShop()
        {
            _openItemShopIndex = -1;
            HideLocalShopTooltip();
        }

        public void PurchaseLocalShopItem(int offerIndex)
        {
            if (!CanAccessLocalItemShop(_openItemShopIndex))
            {
                CloseLocalItemShop();
                return;
            }

            var stock = _itemShopStocks[_openItemShopIndex];
            if (stock == null || stock.IsSold(offerIndex))
            {
                SetStatus("That one-copy offer is already sold.");
                return;
            }

            var itemId = stock.GetOffer(offerIndex);
            if (!PrototypeItemCatalog.IsValid(itemId))
            {
                return;
            }

            var definition = PrototypeItemCatalog.Get(itemId);
            if (!HasFreeLocalItemSlot())
            {
                SetStatus("Purchase failed: inventory is full.");
                return;
            }
            if (_gold[0] < definition.Price)
            {
                SetStatus("Purchase failed: not enough gold.");
                return;
            }
            if (!stock.TrySell(offerIndex) || !TryAddLocalItem(itemId))
            {
                return;
            }

            _gold[0] = PlayerStatRules.ApplyGoldDelta(_gold[0], -definition.Price);
            ApplyLocalItemShopState(_openItemShopIndex);
            SetStatus(stock.IsSoldOut
                ? "Purchased " + definition.DisplayName +
                  ". SOLD OUT; this shop moves and restocks next overview."
                : "Purchased " + definition.DisplayName + ".");
        }

        public void ShowLocalShopTooltip(int offerIndex)
        {
            if (_itemShopTooltip == null || _openItemShopIndex < 0 ||
                _openItemShopIndex >= ItemShopRules.ShopCount)
            {
                return;
            }

            var stock = _itemShopStocks[_openItemShopIndex];
            if (stock == null)
            {
                return;
            }

            var itemId = stock.GetOffer(offerIndex);
            if (!PrototypeItemCatalog.IsValid(itemId))
            {
                return;
            }

            var definition = PrototypeItemCatalog.Get(itemId);
            _itemShopTooltip.text = definition.DisplayName + "\n" +
                                    definition.Description + "\nPRICE  " +
                                    definition.Price + " GOLD";
            _itemShopTooltip.gameObject.SetActive(true);
        }

        public void HideLocalShopTooltip()
        {
            if (_itemShopTooltip != null)
            {
                _itemShopTooltip.gameObject.SetActive(false);
            }
        }

        private bool HasFreeLocalItemSlot()
        {
            return _occupiedMask != (1 << GameplayInventory.Capacity) - 1;
        }

        private bool TryAddLocalItem(PrototypeItemId itemId)
        {
            if (!PrototypeItemCatalog.IsValid(itemId))
            {
                return false;
            }

            for (var slot = 0; slot < GameplayInventory.Capacity; slot++)
            {
                if ((_occupiedMask & (1 << slot)) != 0)
                {
                    continue;
                }

                _itemSlots[slot] = itemId;
                _occupiedMask = (byte)(_occupiedMask | (1 << slot));
                return true;
            }

            return false;
        }

        private void RefreshLocalItemShopUi()
        {
            if (_openItemShopIndex >= 0 &&
                !CanAccessLocalItemShop(_openItemShopIndex))
            {
                CloseLocalItemShop();
                SetStatus("Item Shop closed because P1 left its interaction range.");
            }

            var isOpen = _openItemShopIndex >= 0 &&
                         _openItemShopIndex < ItemShopRules.ShopCount;
            SetActive(_itemShopPanel, isOpen);
            if (!isOpen)
            {
                return;
            }

            var stock = _itemShopStocks[_openItemShopIndex];
            SetText(_itemShopTitle, "ITEM SHOP " + (_openItemShopIndex + 1));
            var hasSpace = HasFreeLocalItemSlot();
            for (var offerIndex = 0;
                 offerIndex < ItemShopRules.OfferCount;
                 offerIndex++)
            {
                var sold = stock == null || stock.IsSold(offerIndex);
                var itemId = stock != null
                    ? stock.GetOffer(offerIndex)
                    : PrototypeItemId.None;
                var valid = PrototypeItemCatalog.IsValid(itemId);
                var definition = valid
                    ? PrototypeItemCatalog.Get(itemId)
                    : default;
                SetText(_shopOfferLabels[offerIndex], sold
                    ? "SOLD"
                    : definition.DisplayName + "\n" + definition.Price + " GOLD");
                if (_shopOfferButtons[offerIndex] != null)
                {
                    _shopOfferButtons[offerIndex].interactable =
                        !sold && valid && hasSpace && _gold[0] >= definition.Price;
                }
            }

            SetText(_itemShopStatus,
                stock != null && stock.IsSoldOut
                    ? "SOLD OUT - moves and restocks next overview."
                    : !hasSpace
                        ? "Inventory full. Offers remain visible."
                        : "Click one-copy offers to buy. Timer is still running.");
        }


        private void RefreshUi()
        {
            var globallyPaused = _paused || _keyShopRevealActive;
            RefreshLocalItemShopUi();
            var shopOpen = _openItemShopIndex >= 0;

            SetActive(_selectionPanel,
                _flow.State == BoardFlowState.Action &&
                _flow.ActionClock.IsChoicePending &&
                !globallyPaused && !shopOpen);
            SetActive(_readyPanel,
                _flow.State == BoardFlowState.MinigameIntroReady && !globallyPaused);
            SetActive(_resultPanel,
                _flow.State == BoardFlowState.SkippedResult && !globallyPaused);
            SetActive(_reticle,
                ((_flow.State == BoardFlowState.Action &&
                  !_flow.ActionClock.IsChoicePending) ||
                 (_flow.State == BoardFlowState.CombatResolve &&
                  _localCombatActive && IsLocalCombatAlive(0))) &&
                !globallyPaused && !shopOpen);

            SetText(_turnText, "TURN " + _flow.CurrentTurn);
            SetText(_phaseText,
                _paused
                    ? "RECONNECT PAUSE"
                    : _keyShopRevealActive
                        ? "KEY SHOP MOVING"
                        : _flow.State == BoardFlowState.CombatResolve &&
                          _localCombatActive
                            ? "FIGHT " + _localCombatSequenceIndex +
                              " / " + (_localCombatSequenceIndex + _combatQueue.Count)
                        : PhaseLabel(_flow.State));
            var remaining = _keyShopRevealActive
                ? Math.Max(0d, _keyShopRevealEndsAt - _simulationNow)
                : _flow.State == BoardFlowState.Action
                    ? _flow.GetActionRemaining(_simulationNow)
                    : _flow.State == BoardFlowState.CombatResolve
                        ? Math.Max(0d, _localCombatEndsAt -
                          _flow.ToFlowTime(_simulationNow))
                    : _flow.GetStateRemaining(_simulationNow);
            SetText(_phaseTimerText,
                _flow.State == BoardFlowState.MinigameIntroReady && !_keyShopRevealActive
                    ? "--:--"
                    : FormatClock(remaining));
            SetText(_choiceText, _flow.ActionClock.IsChoicePending
                ? "CHOOSE  " + FormatClock(_flow.GetChoiceRemaining(_simulationNow))
                : "CHOICE  " + _flow.ActionClock.ChoiceResolution.ToString().ToUpperInvariant());
            var shield = Math.Max(
                _flow.GetOpeningProtectionRemaining(_simulationNow),
                Math.Max(
                    0d,
                    _personalProtectionEndsAtFlowTime -
                    _flow.ToFlowTime(_simulationNow)));
            SetText(_shieldText, shield > 0d ? "SHIELD  " + shield.ToString("0.0") + "s" : "SHIELD  OFF");
            SetText(_diceText, ResolveLocalWorldDieHudLabel());
            var movementLabel = _flow.State == BoardFlowState.CombatResolve
                ? IsLocalCombatAlive(0) ? "WASD  MOVE" : "INPUT  LOCKED"
                : _worldDieRollPresentationPhase !=
                  WorldDieRollPresentationPhase.None
                    ? "MOVES  LOCKED / DIE ROLLING"
                : _roll > 0 && _remainingMoves == 0
                ? "MOVES  0 / FREE IN ROOM"
                : "MOVES  " + _remainingMoves;
            var keyboard = Keyboard.current;
            var quietWalkHeld = keyboard != null && keyboard.leftCtrlKey.isPressed;
            SetText(_movesText, movementLabel +
                ((_flow.State == BoardFlowState.Action ||
                  _flow.State == BoardFlowState.CombatResolve) &&
                 movementLabel != "INPUT  LOCKED"
                    ? quietWalkHeld
                        ? "  /  QUIET WALK 6m"
                        : "  /  LCTRL QUIET 6m"
                    : string.Empty));
            SetText(_ammoText, _selectedSlot >= 0 ? "CHARGE  1\nLMB  USE ITEM" : "CHARGE  --");
            SetText(_speedButtonLabel, "FLOW SPEED  x" + _simulationSpeed.ToString("0"));

            var rankingStats = new PlayerRankingStats[_playerRows.Length];
            for (var playerIndex = 0; playerIndex < rankingStats.Length; playerIndex++)
            {
                rankingStats[playerIndex] = new PlayerRankingStats(
                    _keys[playerIndex],
                    _gold[playerIndex],
                    _minigameWins[playerIndex]);
            }
            var ranks = PlayerRankingRules.Calculate(rankingStats);

            for (var playerIndex = 0; playerIndex < _playerRows.Length; playerIndex++)
            {
                SetText(_playerRows[playerIndex], playerIndex == 0
                    ? "P1  LOCAL"
                    : "P" + (playerIndex + 1) + "  SIMULATED");
                SetText(_playerRankTexts[playerIndex], "#" + ranks[playerIndex]);
                if (_playerRows[playerIndex] != null)
                {
                    _playerRows[playerIndex].color = PlayerColor(playerIndex);
                }
                if (_playerCards[playerIndex] != null)
                {
                    _playerCards[playerIndex].color = playerIndex == 0
                        ? new Color(0.16f, 0.3f, 0.5f, 0.98f)
                        : new Color(0.055f, 0.085f, 0.13f, 0.94f);
                }

                var showCombatHealth = _flow.State == BoardFlowState.CombatResolve &&
                                       IsLocalCombatParticipant(playerIndex);
                var maxHealth = showCombatHealth
                    ? BoardCombatRules.TemporaryHealth
                    : Mathf.Max(1, _maxHealth[playerIndex]);
                var shownHealth = showCombatHealth
                    ? _combatHealth[playerIndex]
                    : _currentHealth[playerIndex];
                var healthRatio = Mathf.Clamp01(shownHealth / (float)maxHealth);
                if (_playerHealthFills[playerIndex] != null)
                {
                    _playerHealthFills[playerIndex].fillAmount = healthRatio;
                    _playerHealthFills[playerIndex].color = healthRatio > 0.5f
                        ? new Color(0.2f, 0.82f, 0.38f, 1f)
                        : healthRatio > 0.25f
                            ? new Color(1f, 0.7f, 0.16f, 1f)
                            : new Color(0.95f, 0.2f, 0.2f, 1f);
                }
                SetText(_playerHealthTexts[playerIndex],
                    shownHealth + "/" + maxHealth);
                SetText(_playerCurrencyTexts[playerIndex],
                    "KEY  " + _keys[playerIndex] + "    GOLD  " + _gold[playerIndex]);
                var actionState = globallyPaused
                    ? PlayerBoardActionState.Hidden
                    : _actionStates[playerIndex];
                var combatOut = showCombatHealth && !IsLocalCombatAlive(playerIndex);
                SetText(_playerActionIcons[playerIndex], combatOut
                    ? "OUT"
                    : ActionIconLabel(actionState));
                if (_playerActionIcons[playerIndex] != null)
                {
                    _playerActionIcons[playerIndex].color = combatOut
                        ? new Color(1f, 0.25f, 0.2f)
                        : ActionIconColor(actionState);
                }
            }

            for (var i = 0; i < GameplayInventory.Capacity; i++)
            {
                var occupied = (_occupiedMask & (1 << i)) != 0 &&
                               PrototypeItemCatalog.IsValid(_itemSlots[i]);
                SetText(_slotLabels[i], occupied ? ItemName(i) : "EMPTY");
                if (_slotImages[i] != null)
                {
                    _slotImages[i].color = i == _selectedSlot
                        ? new Color(1f, 0.72f, 0.15f, 0.97f)
                        : occupied
                            ? new Color(0.18f, 0.36f, 0.58f, 0.94f)
                            : new Color(0.1f, 0.12f, 0.16f, 0.88f);
                }
                if (_choiceButtons[i] != null)
                {
                    _choiceButtons[i].interactable = occupied;
                    var label = _choiceButtons[i].GetComponentInChildren<Text>();
                    if (label != null)
                    {
                        label.text = occupied ? ItemName(i) : "EMPTY";
                    }
                }
            }

            cameraDirector?.SetUiPointerVisible(
                globallyPaused || shopOpen || _editorPointerVisible ||
                (_flow.State != BoardFlowState.Action &&
                 !(_flow.State == BoardFlowState.CombatResolve &&
                   _localCombatActive && IsLocalCombatAlive(0))) ||
                _flow.ActionClock.IsChoicePending);
        }

        private void BindUi()
        {
            _selectionPanel = FindNamed("ItemSelectionPanel");
            _readyPanel = FindNamed("MinigameReadyPanel");
            _resultPanel = FindNamed("SkippedResultPanel");
            _reticle = FindNamed("BoardReticle");
            _itemShopPanel = FindNamed("ItemShopPanel");
            _turnText = FindNamedComponent<Text>("TurnText");
            _phaseText = FindNamedComponent<Text>("PhaseText");
            _phaseTimerText = FindNamedComponent<Text>("PhaseTimerText");
            _choiceText = FindNamedComponent<Text>("BoardChoiceTimerText");
            _shieldText = FindNamedComponent<Text>("BoardShieldText");
            _diceText = FindNamedComponent<Text>("DiceText");
            _movesText = FindNamedComponent<Text>("MovesText");
            _ammoText = FindNamedComponent<Text>("BoardAmmoText");
            _statusText = FindNamedComponent<Text>("BoardStatusText");
            _tooltipText = FindNamedComponent<Text>("BoardTooltipText");
            _itemShopTitle = FindNamedComponent<Text>("ItemShopTitle");
            _itemShopTooltip = FindNamedComponent<Text>("ItemShopTooltip");
            _itemShopStatus = FindNamedComponent<Text>("ItemShopStatus");
            _noItemButton = FindNamedComponent<Button>("NoItemButton");
            _readyButton = FindNamedComponent<Button>("ReadyButton");
            _finishActionButton = FindNamedComponent<Button>("EditorFinishActionButton");
            _stageFightButton = FindNamedComponent<Button>("EditorStageFightButton");
            _speedButton = FindNamedComponent<Button>("EditorSpeedButton");
            _pauseButton = FindNamedComponent<Button>("EditorPauseButton");
            _damagePlayerButton = FindNamedComponent<Button>("EditorDamagePlayerButton");
            _addGoldButton = FindNamedComponent<Button>("EditorAddGoldButton");
            _buyKeyButton = FindNamedComponent<Button>("EditorBuyKeyButton");
            _itemShopCloseButton = FindNamedComponent<Button>("ItemShopCloseButton");
            _speedButtonLabel = _speedButton != null ? _speedButton.GetComponentInChildren<Text>() : null;
            for (var i = 0; i < GameplayInventory.Capacity; i++)
            {
                _slotImages[i] = FindNamedComponent<Image>("BoardInventorySlot" + i);
                _slotLabels[i] = FindNamedComponent<Text>("BoardInventorySlotLabel" + i);
                _choiceButtons[i] = FindNamedComponent<Button>("ItemChoiceButton" + i);
            }
            for (var i = 0; i < _playerRows.Length; i++)
            {
                _playerRows[i] = FindNamedComponent<Text>("PlayerState" + i);
                _playerCards[i] = FindNamedComponent<Image>("PlayerCard" + i);
                _playerHealthFills[i] = FindNamedComponent<Image>("PlayerHealthFill" + i);
                _playerHealthTexts[i] = FindNamedComponent<Text>("PlayerHealthText" + i);
                _playerCurrencyTexts[i] = FindNamedComponent<Text>("PlayerCurrency" + i);
                _playerActionIcons[i] = FindNamedComponent<Text>("PlayerActionIcon" + i);
                _playerRankTexts[i] = FindNamedComponent<Text>("PlayerRank" + i);
            }
            for (var i = 0; i < ItemShopRules.OfferCount; i++)
            {
                _shopOfferButtons[i] = FindNamedComponent<Button>("ItemShopOffer" + i);
                _shopOfferLabels[i] = FindNamedComponent<Text>("ItemShopOfferLabel" + i);
            }
        }

        private void WireButtons()
        {
            for (var i = 0; i < _choiceButtons.Length; i++)
            {
                var slot = i;
                _choiceButtons[i]?.onClick.AddListener(() => SelectItem(slot));
            }
            for (var i = 0; i < _shopOfferButtons.Length; i++)
            {
                var offer = i;
                _shopOfferButtons[i]?.onClick.AddListener(
                    () => PurchaseLocalShopItem(offer));
            }
            _noItemButton?.onClick.AddListener(ChooseNoItem);
            _readyButton?.onClick.AddListener(ReadyAllAndSkip);
            _finishActionButton?.onClick.AddListener(FinishActionForAllPlayers);
            _stageFightButton?.onClick.AddListener(StageTwoPlayerFight);
            _speedButton?.onClick.AddListener(CycleSimulationSpeed);
            _pauseButton?.onClick.AddListener(TogglePause);
            _damagePlayerButton?.onClick.AddListener(DamageLocalPlayer);
            _addGoldButton?.onClick.AddListener(AddLocalGold);
            _buyKeyButton?.onClick.AddListener(BuyLocalKey);
            _itemShopCloseButton?.onClick.AddListener(CloseLocalItemShop);
        }

        private void UnwireButtons()
        {
            for (var i = 0; i < _choiceButtons.Length; i++)
            {
                _choiceButtons[i]?.onClick.RemoveAllListeners();
            }
            for (var i = 0; i < _shopOfferButtons.Length; i++)
            {
                _shopOfferButtons[i]?.onClick.RemoveAllListeners();
            }
            _noItemButton?.onClick.RemoveListener(ChooseNoItem);
            _readyButton?.onClick.RemoveListener(ReadyAllAndSkip);
            _finishActionButton?.onClick.RemoveListener(FinishActionForAllPlayers);
            _stageFightButton?.onClick.RemoveListener(StageTwoPlayerFight);
            _speedButton?.onClick.RemoveListener(CycleSimulationSpeed);
            _pauseButton?.onClick.RemoveListener(TogglePause);
            _damagePlayerButton?.onClick.RemoveListener(DamageLocalPlayer);
            _addGoldButton?.onClick.RemoveListener(AddLocalGold);
            _buyKeyButton?.onClick.RemoveListener(BuyLocalKey);
            _itemShopCloseButton?.onClick.RemoveListener(CloseLocalItemShop);
        }

        private void ApplyBodyVisibility(bool firstPerson)
        {
            if (player == null)
            {
                return;
            }
            if (_avatarVisual != null)
            {
                _avatarVisual.SetOwnerFirstPerson(firstPerson);
                return;
            }
            var renderers = player.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = !firstPerson;
            }
        }

        private void RefreshLocalCrouch()
        {
            if (player == null)
            {
                return;
            }

            var keyboard = Keyboard.current;
            var canCrouch = _flow.State == BoardFlowState.Action ||
                            _flow.State == BoardFlowState.CombatResolve &&
                            _localCombatActive && IsLocalCombatAlive(0);
            var held = canCrouch && keyboard != null && keyboard.leftCtrlKey.isPressed;
            var crouching = held || _localCrouching && !CanLocalPlayerStand();
            if (_localCrouching == crouching)
            {
                _avatarVisual?.SetCrouching(crouching);
                return;
            }

            _localCrouching = crouching;
            player.height = crouching
                ? PlayerAvatarVisual.CrouchingControllerHeight
                : PlayerAvatarVisual.StandingControllerHeight;
            var center = player.center;
            center.y = crouching
                ? PlayerAvatarVisual.CrouchingControllerCenterY
                : PlayerAvatarVisual.StandingControllerCenterY;
            player.center = center;
            if (eyePivot != null)
            {
                var position = eyePivot.localPosition;
                position.y = crouching
                    ? PlayerAvatarVisual.CrouchingEyeHeight
                    : PlayerAvatarVisual.StandingEyeHeight;
                eyePivot.localPosition = position;
            }
            _avatarVisual?.SetCrouching(crouching);
        }

        private bool CanLocalPlayerStand()
        {
            var radius = Mathf.Max(0.01f, player.radius - 0.02f);
            var center = player.transform.TransformPoint(new Vector3(
                player.center.x,
                PlayerAvatarVisual.StandingControllerCenterY,
                player.center.z));
            var segment = Mathf.Max(
                0f,
                PlayerAvatarVisual.StandingControllerHeight * 0.5f - radius);
            var count = Physics.OverlapCapsuleNonAlloc(
                center + player.transform.up * segment,
                center - player.transform.up * segment,
                radius,
                _standingClearanceHits,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < count; index++)
            {
                var candidate = _standingClearanceHits[index];
                if (candidate != null &&
                    candidate.transform != player.transform &&
                    !candidate.transform.IsChildOf(player.transform))
                {
                    return false;
                }
            }
            return true;
        }

        private void Teleport(Vector3 target)
        {
            var wasEnabled = player.enabled;
            if (wasEnabled)
            {
                player.enabled = false;
            }
            player.transform.position = target;
            if (wasEnabled)
            {
                player.enabled = true;
            }
            _localFootstepCadence.Reset();
        }

        private void RecordLocalFootsteps(
            Vector3 previousPosition,
            Vector2 movementInput,
            bool quietWalking)
        {
            if (!FootstepRules.HasMovementIntent(movementInput) ||
                !player.isGrounded)
            {
                return;
            }

            var delta = player.transform.position - previousPosition;
            delta.y = 0f;
            var stepCount = _localFootstepCadence.RecordMovement(
                delta.magnitude,
                quietWalking);
            for (var step = 0; step < stepCount; step++)
            {
                _localFootstepEmitter?.PresentFootstep(
                    player.transform.position,
                    quietWalking);
            }
        }

        private T FindNamedComponent<T>(string name) where T : Component
        {
            var components = FindObjectsByType<T>(FindObjectsInactive.Include);
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i].gameObject.name == name)
                {
                    return components[i];
                }
            }
            return null;
        }

        private static GameObject FindNamed(string name)
        {
            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].gameObject.name == name)
                {
                    return transforms[i].gameObject;
                }
            }
            return null;
        }

        private void SetStatus(string message)
        {
            SetText(_statusText, message);
            Debug.Log("[Board Flow Testbed] " + message, this);
        }

        private string ItemName(int slot)
        {
            return slot >= 0 && slot < _itemSlots.Length &&
                   PrototypeItemCatalog.IsValid(_itemSlots[slot])
                ? PrototypeItemCatalog.Get(_itemSlots[slot]).DisplayName
                : "EMPTY";
        }

        private string ItemDescription(int slot)
        {
            return slot >= 0 && slot < _itemSlots.Length &&
                   PrototypeItemCatalog.IsValid(_itemSlots[slot])
                ? PrototypeItemCatalog.Get(_itemSlots[slot]).Description
                : "No item in this slot.";
        }

        private static string PhaseLabel(BoardFlowState state)
        {
            switch (state)
            {
                case BoardFlowState.TurnOverview: return "BOARD OVERVIEW";
                case BoardFlowState.Descending: return "DESCENDING";
                case BoardFlowState.Action: return "ACTION";
                case BoardFlowState.AscendingResolve: return "ASCENDING / RESOLVE";
                case BoardFlowState.CombatResolve: return "COMBAT QUEUE";
                case BoardFlowState.LandingEffectResolve: return "LANDING EFFECTS";
                case BoardFlowState.MinigameIntroReady: return "MINIGAME READY";
                case BoardFlowState.SkippedResult: return "RESULT";
                default: return state.ToString().ToUpperInvariant();
            }
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
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

        private static string FormatClock(double seconds)
        {
            var whole = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return (whole / 60).ToString("00") + ":" + (whole % 60).ToString("00");
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

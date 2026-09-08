using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
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
        [SerializeField] private CharacterController player;
        [SerializeField] private Transform eyePivot;
        [SerializeField] private BoardTopology topology;
        [SerializeField] private GameplayCameraDirector cameraDirector;
        [SerializeField, Min(0.1f)] private float moveSpeed = 5f;
        [SerializeField, Min(0.01f)] private float lookSensitivity = 0.12f;
        [SerializeField, Min(0f)] private float worldDieResultVisibleSeconds = 2f;
        [SerializeField, Min(0.05f)] private float worldDieNudgeHorizontalImpulse = 1.35f;
        [SerializeField, Min(0f)] private float worldDieNudgeUpwardImpulse = 0.25f;
        [SerializeField, Min(0f)] private float worldDieNudgeTorqueImpulse = 0.9f;

        private readonly BoardFlowStateMachine _flow = new BoardFlowStateMachine();
        private readonly BoardTraversalState _traversal = new BoardTraversalState();
        private readonly Image[] _slotImages = new Image[GameplayInventory.Capacity];
        private readonly Text[] _slotLabels = new Text[GameplayInventory.Capacity];
        private readonly Button[] _choiceButtons = new Button[GameplayInventory.Capacity];
        private readonly Text[] _playerRows = new Text[BoardFlowStateMachine.RequiredPlayerCount];

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
        private Button _speedButton;
        private Button _pauseButton;

        private double _simulationNow;
        private float _simulationSpeed = 1f;
        private float _yaw;
        private float _pitch;
        private int _roll;
        private int _remainingMoves;
        private int _selectedSlot = -1;
        private byte _occupiedMask = 0b0000_0111;
        private bool _paused;
        private bool _editorPointerVisible;
        private ItemChoiceResolution _lastChoiceResolution = ItemChoiceResolution.NotStarted;
        private PlayerBoardBoundaryWalls _boundaryWalls;
        private KeyShopRuntimeState _keyShopState;
        private KeyShopWorldMarker _keyShopMarker;
        private GameObject _worldDie;
        private Collider _worldDieCollider;
        private Rigidbody _worldDieBody;
        private TextMesh _worldDieResult;
        private BoardTile _worldDieTile;
        private bool _worldDieNudgeInProgress;
        private double _worldDieNudgeDeadline = -1d;
        private double _worldDieNudgeBelowThresholdSince = -1d;
        private double _worldDieHideDeadline = -1d;

        public void Configure(
            CharacterController localPlayer,
            Transform localEye,
            BoardTopology boardTopology,
            GameplayCameraDirector director)
        {
            player = localPlayer;
            eyePivot = localEye;
            topology = boardTopology;
            cameraDirector = director;
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

            _boundaryWalls = player != null
                ? player.GetComponent<PlayerBoardBoundaryWalls>()
                : null;
            if (_boundaryWalls == null && player != null)
            {
                _boundaryWalls = player.gameObject.AddComponent<PlayerBoardBoundaryWalls>();
            }
            if (_boundaryWalls != null && player != null && topology != null)
            {
                _boundaryWalls.Configure(0, player, topology);
                _boundaryWalls.SetPresentationVisible(true);
            }

            _keyShopMarker = topology != null
                ? topology.GetComponent<KeyShopWorldMarker>()
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
            _flow.Start(_simulationNow, 1);
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
            _simulationNow += Time.unscaledDeltaTime * _simulationSpeed;
            _flow.Tick(_simulationNow);
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
            UpdateLocalWorldDieLifetime();
            UpdateWorldDieResultBillboard();
            RefreshUi();
        }

        private void FixedUpdate()
        {
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
            for (var slot = 0; slot < BoardFlowStateMachine.RequiredPlayerCount; slot++)
            {
                _flow.TryReportPlayerArrived(slot, _simulationNow);
            }
            SetStatus("EDITOR: all four arrival reports submitted.");
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
                    ResetActionState();
                    cameraDirector?.SwitchTo(GameplayMode.BoardTopDown);
                    TryPlaceLocalKeyShop(transition.Turn);
                    SetStatus("Board overview for turn " + transition.Turn + ".");
                    break;
                case BoardFlowState.Descending:
                    cameraDirector?.SwitchTo(GameplayMode.FirstPerson);
                    SetStatus("Descending through the local overhead bridge.");
                    break;
                case BoardFlowState.Action:
                    ResetActionState();
                    InitializeTraversal();
                    RefreshBoundaryWalls();
                    SetStatus("Choose an item within 30 seconds; shield and action clocks started together.");
                    break;
                case BoardFlowState.AscendingResolve:
                    EndSelectedItemUse();
                    cameraDirector?.SwitchTo(GameplayMode.BoardTopDown);
                    SetStatus("Input closed. Five-second effect settle window.");
                    break;
                case BoardFlowState.MinigameIntroReady:
                    SetStatus("Minigame TODO: click READY / SKIP ALL in the editor panel.");
                    break;
                case BoardFlowState.SkippedResult:
                    SetStatus("Result placeholder: no economy update. Next turn in three seconds.");
                    break;
            }

            ApplyBodyVisibility(transition.Current == BoardFlowState.Descending ||
                                transition.Current == BoardFlowState.Action);
        }

        private void HandleInput()
        {
            if (_paused || _flow.State != BoardFlowState.Action || player == null)
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
                if (mouse.rightButton.wasPressedThisFrame && _roll <= 0 &&
                    TryGetAimedLocalWorldDie(out _))
                {
                    _roll = UnityEngine.Random.Range(1, 11);
                    _remainingMoves = _roll;
                    StopLocalWorldDieNudge();
                    if (_worldDie != null)
                    {
                        _worldDie.transform.rotation = UnityEngine.Random.rotation;
                    }
                    if (_worldDieResult != null)
                    {
                        _worldDieResult.text = _roll.ToString();
                    }
                    _worldDieHideDeadline = Time.unscaledTimeAsDouble +
                                            worldDieResultVisibleSeconds;
                    if (_traversal.IsInitialized)
                    {
                        _traversal.ResetMoves(_roll);
                    }
                    RefreshBoundaryWalls();
                    SetStatus("Rolled " + _roll + ". Blue walls are passable; black walls physically block entry.");
                }
                if (mouse.leftButton.wasPressedThisFrame &&
                    TryGetAimedLocalWorldDie(out var dieRay))
                {
                    if (_roll <= 0)
                    {
                        NudgeLocalWorldDie(dieRay);
                        SetStatus("Your world die was nudged inside the current room.");
                    }
                }
                else if (mouse.leftButton.wasPressedThisFrame && _selectedSlot >= 0)
                {
                    _occupiedMask = (byte)(_occupiedMask & ~(1 << _selectedSlot));
                    _selectedSlot = -1;
                    SetStatus("Prototype item consumed. Concrete combat/effect is TODO.");
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
            var movement = player.transform.TransformDirection(new Vector3(input.x, 0f, input.y));
            movement.y = 0f;
            player.Move(movement * (moveSpeed * Time.unscaledDeltaTime));
            ResolveTraversal();
        }

        private void HandleEditorPointerToggle()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame)
            {
                return;
            }

            if (_paused || _flow.State != BoardFlowState.Action || _flow.ActionClock.IsChoicePending)
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
            }
            _selectedSlot = -1;
            _boundaryWalls?.Hide();
            HideLocalWorldDie();
        }

        private void RefreshBoundaryWalls()
        {
            if (_boundaryWalls == null || !_traversal.IsInitialized ||
                _flow.State != BoardFlowState.Action)
            {
                _boundaryWalls?.Hide();
                return;
            }

            _boundaryWalls.Refresh(_traversal.CurrentTile, _remainingMoves);
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

            _worldDie = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _worldDie.name = "Local World Die (Editor)";
            _worldDie.transform.localScale = Vector3.one * 0.8f;
            _worldDieCollider = _worldDie.GetComponent<Collider>();
            _worldDieBody = _worldDie.AddComponent<Rigidbody>();
            _worldDieBody.interpolation = RigidbodyInterpolation.Interpolate;
            _worldDieBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _worldDieBody.isKinematic = true;
            _worldDieBody.detectCollisions = false;
            var renderer = _worldDie.GetComponent<Renderer>();
            if (renderer != null)
            {
                var properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", new Color(0.95f, 0.25f, 0.25f));
                properties.SetColor("_Color", new Color(0.95f, 0.25f, 0.25f));
                renderer.SetPropertyBlock(properties);
            }

            var labelObject = new GameObject("Public World Result");
            labelObject.transform.SetParent(_worldDie.transform, false);
            _worldDieResult = labelObject.AddComponent<TextMesh>();
            _worldDieResult.anchor = TextAnchor.MiddleCenter;
            _worldDieResult.alignment = TextAlignment.Center;
            _worldDieResult.fontSize = 64;
            _worldDieResult.characterSize = 0.045f;
            _worldDieResult.color = Color.white;
            HideLocalWorldDie();
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

        private void NudgeLocalWorldDie(Ray ray)
        {
            if (_worldDie == null || _worldDieTile == null ||
                _worldDieBody == null || _worldDieCollider == null ||
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

        private void UpdateLocalWorldDieLifetime()
        {
            if (_worldDie != null && _worldDie.activeSelf && _roll > 0 &&
                _worldDieHideDeadline >= 0d &&
                Time.unscaledTimeAsDouble >= _worldDieHideDeadline)
            {
                HideLocalWorldDie();
            }
        }

        private void UpdateLocalWorldDiePhysics()
        {
            if (!_worldDieNudgeInProgress || _worldDieBody == null ||
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

        private void TryPlaceLocalKeyShop(int turn)
        {
            if (turn != KeyShopRuntimeState.InitialPlacementTurn ||
                _keyShopState == null || topology == null)
            {
                return;
            }

            var occupied = new List<Vector2Int>();
            if (_traversal.IsInitialized)
            {
                occupied.Add(_traversal.CurrentTile.Coordinate);
            }
            var transforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Exclude);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (!transforms[i].name.StartsWith("Simulated Remote Player", StringComparison.Ordinal))
                {
                    continue;
                }
                var tile = topology.FindContainingTile(transforms[i].position, 0.1f);
                if (tile != null && !occupied.Contains(tile.Coordinate))
                {
                    occupied.Add(tile.Coordinate);
                }
            }

            if (_keyShopState.TryBeginInitialPlacement(
                    turn,
                    topology.Tiles,
                    occupied,
                    out _))
            {
                ApplyLocalKeyShopState();
                _keyShopState.TryCompleteAppearance();
                ApplyLocalKeyShopState();
                SetStatus("A single Key Shop appeared on an unoccupied random Normal room.");
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

        private void RefreshUi()
        {
            SetActive(_selectionPanel,
                _flow.State == BoardFlowState.Action && _flow.ActionClock.IsChoicePending && !_paused);
            SetActive(_readyPanel, _flow.State == BoardFlowState.MinigameIntroReady && !_paused);
            SetActive(_resultPanel, _flow.State == BoardFlowState.SkippedResult && !_paused);
            SetActive(_reticle,
                _flow.State == BoardFlowState.Action && !_flow.ActionClock.IsChoicePending && !_paused);

            SetText(_turnText, "TURN " + _flow.CurrentTurn);
            SetText(_phaseText, _paused ? "RECONNECT PAUSE" : PhaseLabel(_flow.State));
            var remaining = _flow.State == BoardFlowState.Action
                ? _flow.GetActionRemaining(_simulationNow)
                : _flow.GetStateRemaining(_simulationNow);
            SetText(_phaseTimerText,
                _flow.State == BoardFlowState.MinigameIntroReady ? "--:--" : FormatClock(remaining));
            SetText(_choiceText, _flow.ActionClock.IsChoicePending
                ? "CHOOSE  " + FormatClock(_flow.GetChoiceRemaining(_simulationNow))
                : "CHOICE  " + _flow.ActionClock.ChoiceResolution.ToString().ToUpperInvariant());
            var shield = _flow.GetOpeningProtectionRemaining(_simulationNow);
            SetText(_shieldText, shield > 0d ? "SHIELD  " + shield.ToString("0.0") + "s" : "SHIELD  OFF");
            SetText(_diceText, _roll > 0
                ? "DICE  " + _roll
                : _flow.ActionClock.IsChoicePending
                    ? "DICE  CHOOSE ITEM FIRST"
                    : "AIM AT WORLD DIE / RMB ROLL");
            SetText(_movesText, _roll > 0 && _remainingMoves == 0
                ? "MOVES  0  /  FREE MOVE IN ROOM"
                : "MOVES  " + _remainingMoves);
            SetText(_ammoText, _selectedSlot >= 0 ? "CHARGE  1\nLMB  USE ITEM" : "CHARGE  --");
            SetText(_speedButtonLabel, "FLOW SPEED  x" + _simulationSpeed.ToString("0"));

            for (var playerIndex = 0; playerIndex < _playerRows.Length; playerIndex++)
            {
                SetText(_playerRows[playerIndex], playerIndex == 0
                    ? "P1  LOCAL"
                    : "P" + (playerIndex + 1) + "  SIMULATED");
            }

            for (var i = 0; i < GameplayInventory.Capacity; i++)
            {
                var occupied = (_occupiedMask & (1 << i)) != 0;
                SetText(_slotLabels[i], occupied ? ItemName(i) : "EMPTY");
                if (_slotImages[i] != null)
                {
                    _slotImages[i].color = i == _selectedSlot
                        ? new Color(1f, 0.72f, 0.15f, 0.97f)
                        : occupied ? new Color(0.18f, 0.36f, 0.58f, 0.94f) : new Color(0.1f, 0.12f, 0.16f, 0.88f);
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
                _paused || _editorPointerVisible || _flow.State != BoardFlowState.Action ||
                _flow.ActionClock.IsChoicePending);
        }

        private void BindUi()
        {
            _selectionPanel = FindNamed("ItemSelectionPanel");
            _readyPanel = FindNamed("MinigameReadyPanel");
            _resultPanel = FindNamed("SkippedResultPanel");
            _reticle = FindNamed("BoardReticle");
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
            _noItemButton = FindNamedComponent<Button>("NoItemButton");
            _readyButton = FindNamedComponent<Button>("ReadyButton");
            _finishActionButton = FindNamedComponent<Button>("EditorFinishActionButton");
            _speedButton = FindNamedComponent<Button>("EditorSpeedButton");
            _pauseButton = FindNamedComponent<Button>("EditorPauseButton");
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
            }
        }

        private void WireButtons()
        {
            for (var i = 0; i < _choiceButtons.Length; i++)
            {
                var slot = i;
                _choiceButtons[i]?.onClick.AddListener(() => SelectItem(slot));
            }
            _noItemButton?.onClick.AddListener(ChooseNoItem);
            _readyButton?.onClick.AddListener(ReadyAllAndSkip);
            _finishActionButton?.onClick.AddListener(FinishActionForAllPlayers);
            _speedButton?.onClick.AddListener(CycleSimulationSpeed);
            _pauseButton?.onClick.AddListener(TogglePause);
        }

        private void UnwireButtons()
        {
            for (var i = 0; i < _choiceButtons.Length; i++)
            {
                _choiceButtons[i]?.onClick.RemoveAllListeners();
            }
            _noItemButton?.onClick.RemoveListener(ChooseNoItem);
            _readyButton?.onClick.RemoveListener(ReadyAllAndSkip);
            _finishActionButton?.onClick.RemoveListener(FinishActionForAllPlayers);
            _speedButton?.onClick.RemoveListener(CycleSimulationSpeed);
            _pauseButton?.onClick.RemoveListener(TogglePause);
        }

        private void ApplyBodyVisibility(bool firstPerson)
        {
            if (player == null)
            {
                return;
            }
            var renderers = player.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = !firstPerson;
            }
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

        private static string ItemName(int slot)
        {
            return slot == 0 ? "Pulse Blaster" : slot == 1 ? "Push Mine" : "Med Kit";
        }

        private static string ItemDescription(int slot)
        {
            return slot == 0
                ? "Prototype ranged item. LMB consumes its test charge."
                : slot == 1
                    ? "Prototype area item. Its combat effect is still TODO."
                    : "Prototype recovery item. Its healing effect is still TODO.";
        }

        private static string PhaseLabel(BoardFlowState state)
        {
            switch (state)
            {
                case BoardFlowState.TurnOverview: return "BOARD OVERVIEW";
                case BoardFlowState.Descending: return "DESCENDING";
                case BoardFlowState.Action: return "ACTION";
                case BoardFlowState.AscendingResolve: return "ASCENDING / RESOLVE";
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
    }
}

using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.GiftGrab;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    /// <summary>
    /// Offline Gift Grab harness injected into the production additive scene.
    /// It reuses scene art, gift instances and prefab labels while replacing
    /// only networking with a deterministic local authoritative session.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class GiftGrabSoloTestController : MonoBehaviour
    {
        private static readonly Color[] PlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        private readonly Transform[] _players =
            new Transform[GiftGrabRules.PlayerCount];
        private readonly PlayerAvatarVisual[] _playerVisuals =
            new PlayerAvatarVisual[GiftGrabRules.PlayerCount];
        private readonly Transform[] _giftAnchors =
            new Transform[GiftGrabRules.TotalGiftCount];
        private readonly TransformSnapshot[] _giftSnapshots =
            new TransformSnapshot[GiftGrabRules.TotalGiftCount];
        private readonly TransformSnapshot[] _playerLabelSnapshots =
            new TransformSnapshot[GiftGrabRules.PlayerCount];
        private readonly TransformSnapshot[] _baseLabelSnapshots =
            new TransformSnapshot[GiftGrabRules.PlayerCount];
        private readonly List<CameraState> _cameraStates =
            new List<CameraState>();
        private readonly List<ListenerState> _listenerStates =
            new List<ListenerState>();

        private GiftGrabSoloSession _session;
        private MinigameSoloHudView _hud;
        private Transform _runtimeRoot;
        private Camera _runtimeCamera;
        private NetworkGiftGrabState _productionState;
        private GiftGrabNetworkView _productionView;
        private GameObject _productionPlayerRoot;
        private GameObject _productionHud;
        private GameObject _arenaPresentation;
        private Transform[] _playerAnchors;
        private Transform[] _baseAnchors;
        private Transform[] _depositAnchors;
        private GiftGrabBaseLabel[] _playerLabels;
        private GiftGrabBaseLabel[] _baseLabels;
        private Transform _pushVfx;
        private Transform _throwVfx;
        private Transform _dropVfx;
        private Transform[] _stunVfx;
        private bool _productionStateWasEnabled;
        private bool _productionViewWasEnabled;
        private bool _productionPlayerRootWasActive;
        private bool _productionHudWasActive;
        private bool _arenaWasActive;
        private bool _productionCaptured;
        private bool _initialized;
        private uint _lastActionRevision;
        private float _pushVfxUntil;
        private float _throwVfxUntil;
        private float _dropVfxUntil;
        private string _feedback = string.Empty;
        private MinigameSoloFeedbackStyle _feedbackStyle =
            MinigameSoloFeedbackStyle.Neutral;
        private float _feedbackUntil;

        public bool IsInitialized => _initialized;
        public GiftGrabSoloSession Session => _session;
        public Camera RuntimeCamera => _runtimeCamera;
        public Transform LocalPlayer =>
            _players[GiftGrabSoloSession.LocalPlayerSlot];

        public void ConfigureHud(MinigameSoloHudView hud)
        {
            _hud = hud;
        }

        public void Begin(int seed)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The Gift Grab solo harness is already initialized.");
            }
            if (_hud == null || !_hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Gift Grab solo requires a valid MinigameSoloHud prefab " +
                    "instance.");
            }

            _hud.BindActions(RestartRound, StartNextSeed, StopSoloTest);
            DisableProductionPresentation();
            ResolveArenaContract();
            CreateRuntimeRoot();
            CreatePlayers();
            CreateRuntimeCamera();

            _session = new GiftGrabSoloSession();
            _session.Begin(seed);
            ResetPresentation();
            _initialized = true;
            RefreshPresentation();
            UpdateHud();
            Debug.Log(
                "[Minigame Solo Test] Gift Grab started with seed " + seed +
                ". Three deterministic practice players are active; no " +
                "network session was created.");
        }

        private void Update()
        {
            if (!_initialized || _session == null)
            {
                return;
            }
            if (HandleKeyboardShortcuts())
            {
                return;
            }

            var move = ReadMove();
            var mouse = Mouse.current;
            if (_session.Phase == GiftGrabSoloPhase.Running &&
                mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                if (!_session.TryLocalAction())
                {
                    ShowFeedback(
                        "NO ACTION TARGET",
                        MinigameSoloFeedbackStyle.Warning,
                        0.65f);
                }
            }

            var previousPhase = _session.Phase;
            var previousRound = _session.RoundNumber;
            _session.Tick(Time.unscaledDeltaTime, move);
            if (_session.Phase != previousPhase ||
                _session.RoundNumber != previousRound)
            {
                HandleSessionTransition(previousPhase, previousRound);
            }
            RefreshPresentation();
            UpdateHud();
        }

        private void OnDestroy()
        {
            if (_hud != null)
            {
                _hud.BindActions(null, null, null);
            }
            RestoreProductionPresentation();
        }

        public void RestartRound()
        {
            if (_session == null)
            {
                return;
            }
            _session.RestartCurrentRound();
            ResetPresentation();
            RefreshPresentation();
            UpdateHud();
        }

        public void StartNewSeed(int seed)
        {
            if (_session == null)
            {
                return;
            }
            _session.Begin(seed);
            ResetPresentation();
            RefreshPresentation();
            UpdateHud();
        }

        private void DisableProductionPresentation()
        {
            _productionState = FindAnyObjectByType<NetworkGiftGrabState>(
                FindObjectsInactive.Include);
            if (_productionState == null)
            {
                throw new InvalidOperationException(
                    "The active scene does not contain NetworkGiftGrabState.");
            }

            _productionView =
                _productionState.GetComponent<GiftGrabNetworkView>();
            if (_productionView == null)
            {
                throw new InvalidOperationException(
                    "NetworkGiftGrabState is missing GiftGrabNetworkView.");
            }
            _productionPlayerRoot = _productionView.PlayerRoot != null
                ? _productionView.PlayerRoot.gameObject
                : null;
            _productionHud = _productionView.Hud != null
                ? _productionView.Hud.gameObject
                : null;
            _arenaPresentation = _productionView.ArenaPresentation;
            _productionStateWasEnabled = _productionState.enabled;
            _productionViewWasEnabled = _productionView.enabled;
            _productionPlayerRootWasActive =
                _productionPlayerRoot != null &&
                _productionPlayerRoot.activeSelf;
            _productionHudWasActive =
                _productionHud != null && _productionHud.activeSelf;
            _arenaWasActive =
                _arenaPresentation != null &&
                _arenaPresentation.activeSelf;
            _productionCaptured = true;

            _productionState.enabled = false;
            _productionView.enabled = false;
            _productionPlayerRoot?.SetActive(false);
            _productionHud?.SetActive(false);
            // GiftGrabNetworkView.OnDisable hides this root. Solo deliberately
            // re-enables the production art after the production view stops.
            _arenaPresentation?.SetActive(true);
        }

        private void ResolveArenaContract()
        {
            if (_arenaPresentation == null)
            {
                throw new InvalidOperationException(
                    "Gift Grab scene is missing Arena Presentation.");
            }

            _playerAnchors = _productionView.PlayerAnchors;
            _baseAnchors = _productionView.BaseAnchors;
            _depositAnchors = _productionView.DepositedGiftAnchors;
            _playerLabels = _productionView.PlayerLabels;
            _baseLabels = _productionView.BaseLabels;
            var giftRoot = _productionView.GiftRoot;
            if (!HasFour(_playerAnchors) ||
                !HasFour(_baseAnchors) ||
                !HasFour(_depositAnchors) ||
                !HasFour(_playerLabels) ||
                !HasFour(_baseLabels) ||
                giftRoot == null ||
                giftRoot.childCount != GiftGrabRules.TotalGiftCount)
            {
                throw new InvalidOperationException(
                    "Gift Grab scene has an incomplete player, base, gift or " +
                    "prefab world-label contract.");
            }

            for (var giftId = 0;
                 giftId < GiftGrabRules.TotalGiftCount;
                 giftId++)
            {
                _giftAnchors[giftId] = giftRoot.GetChild(giftId);
                if (_giftAnchors[giftId].Find("Gift Visual") == null)
                {
                    throw new InvalidOperationException(
                        "Gift Anchor " + giftId +
                        " is missing its replaceable Gift Visual child.");
                }
                _giftSnapshots[giftId] =
                    TransformSnapshot.Capture(_giftAnchors[giftId]);
            }
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                if (!_playerLabels[slot].HasRequiredReferences ||
                    !_baseLabels[slot].HasRequiredReferences)
                {
                    throw new InvalidOperationException(
                        "Gift Grab visible label bindings are incomplete.");
                }
                _playerLabelSnapshots[slot] =
                    TransformSnapshot.Capture(_playerLabels[slot].transform);
                _baseLabelSnapshots[slot] =
                    TransformSnapshot.Capture(_baseLabels[slot].transform);
            }

            var vfxRoot = FindDescendant(
                _arenaPresentation.transform,
                "VFX Replacement Anchors");
            _pushVfx = RequireChild(vfxRoot, "Push VFX Anchor");
            _throwVfx = RequireChild(vfxRoot, "Throw VFX Anchor");
            _dropVfx = RequireChild(vfxRoot, "Drop VFX Anchor");
            _stunVfx = new Transform[GiftGrabRules.PlayerCount];
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                _stunVfx[slot] = RequireChild(
                    vfxRoot,
                    "Stun VFX " + (slot + 1));
            }
        }

        private void CreateRuntimeRoot()
        {
            _runtimeRoot = new GameObject("[Solo Test] Runtime").transform;
            _runtimeRoot.SetParent(transform, false);
        }

        private void CreatePlayers()
        {
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var root = new GameObject(
                    slot == GiftGrabSoloSession.LocalPlayerSlot
                        ? "Solo Gift Grab Player"
                        : "Practice Gift Grab Player " + (slot + 1));
                root.transform.SetParent(_runtimeRoot, false);
                root.transform.position = _playerAnchors[slot].position;
                var visual = root.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.SetOwnerFirstPerson(false);
                visual.SetBodyColor(PlayerColors[slot]);
                visual.SetDisplayName(GetPlayerName(slot));
                visual.SetTopViewHighlight(
                    slot == GiftGrabSoloSession.LocalPlayerSlot);
                DisableBuiltInNameplate(root.transform);
                DisableGeneratedHitColliders(root);
                _players[slot] = root.transform;
                _playerVisuals[slot] = visual;
            }
        }

        private void CreateRuntimeCamera()
        {
            foreach (var camera in FindObjectsByType<Camera>(
                         FindObjectsInactive.Include))
            {
                _cameraStates.Add(new CameraState(camera, camera.enabled));
                camera.enabled = false;
            }
            foreach (var listener in FindObjectsByType<AudioListener>(
                         FindObjectsInactive.Include))
            {
                _listenerStates.Add(
                    new ListenerState(listener, listener.enabled));
                listener.enabled = false;
            }

            var cameraObject = new GameObject(
                "Solo Output Camera",
                typeof(Camera),
                typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(_runtimeRoot, false);
            cameraObject.transform.SetPositionAndRotation(
                GiftGrabNetworkView.SharedCameraPosition,
                GiftGrabNetworkView.SharedCameraRotation);
            _runtimeCamera = cameraObject.GetComponent<Camera>();
            _runtimeCamera.orthographic = true;
            _runtimeCamera.orthographicSize =
                GiftGrabNetworkView.SharedCameraOrthographicSize;
            _runtimeCamera.nearClipPlane = 0.1f;
            _runtimeCamera.farClipPlane = 80f;
            _runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.018f, 0.028f, 0.045f, 1f);
        }

        private void ResetPresentation()
        {
            _feedback = string.Empty;
            _feedbackUntil = float.NegativeInfinity;
            _lastActionRevision = _session.ActionRevision;
            _pushVfxUntil = _throwVfxUntil = _dropVfxUntil =
                float.NegativeInfinity;
            _pushVfx.gameObject.SetActive(false);
            _throwVfx.gameObject.SetActive(false);
            _dropVfx.gameObject.SetActive(false);
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                _stunVfx[slot].gameObject.SetActive(false);
            }
        }

        private void RefreshPresentation()
        {
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var player = _session.GetPlayer(slot);
                var root = _players[slot];
                root.position = new Vector3(
                    player.Position.x,
                    GiftGrabNetworkView.PlayerPresentationHeight,
                    player.Position.y);
                if (player.Facing.sqrMagnitude > 0.001f)
                {
                    root.rotation = Quaternion.LookRotation(
                        new Vector3(player.Facing.x, 0f, player.Facing.y),
                        Vector3.up);
                }
                _playerVisuals[slot].SetEliminated(false);

                var playerLabel = _playerLabels[slot];
                playerLabel.transform.position =
                    root.position + Vector3.up * 2.9f;
                playerLabel.SetContent(
                    GetPlayerName(slot),
                    player.StunRemaining > 0d
                        ? "STUN " + player.StunRemaining.ToString("0.0") + "s"
                        : player.CarriedGiftId >= 0
                            ? "CARRYING GIFT"
                            : string.Empty,
                    slot == GiftGrabSoloSession.LocalPlayerSlot,
                    PlayerColors[slot]);
                playerLabel.FaceCamera(_runtimeCamera);

                _baseLabels[slot].SetContent(
                    GetPlayerName(slot) + " BASE",
                    player.StoredGiftCount + " GIFTS",
                    slot == GiftGrabSoloSession.LocalPlayerSlot,
                    PlayerColors[slot]);
                _baseLabels[slot].FaceCamera(_runtimeCamera);
            }

            for (var giftId = 0;
                 giftId < GiftGrabRules.TotalGiftCount;
                 giftId++)
            {
                RefreshGift(giftId);
            }
            RefreshActionVfx();
        }

        private void RefreshGift(int giftId)
        {
            var gift = _session.GetGift(giftId);
            var anchor = _giftAnchors[giftId];
            if (!gift.IsActive)
            {
                anchor.gameObject.SetActive(false);
                return;
            }
            anchor.gameObject.SetActive(true);
            if (gift.StoredOwnerSlot >= 0)
            {
                anchor.position = new Vector3(
                    gift.Position.x,
                    GiftGrabNetworkView.GiftPresentationHeight,
                    gift.Position.y);
                anchor.rotation = Quaternion.Euler(0f, giftId * 29f, 0f);
            }
            else if (gift.CarrierSlot >= 0)
            {
                var player = _session.GetPlayer(gift.CarrierSlot);
                var facing = player.Facing.sqrMagnitude > 0.001f
                    ? player.Facing.normalized
                    : Vector2.up;
                anchor.position = _players[gift.CarrierSlot].position +
                    new Vector3(facing.x * 0.65f, 1.25f,
                        facing.y * 0.65f);
                anchor.rotation = Quaternion.Euler(
                    0f,
                    Time.unscaledTime * 90f + giftId * 17f,
                    0f);
            }
            else
            {
                anchor.position = new Vector3(
                    gift.Position.x,
                    GiftGrabNetworkView.GiftPresentationHeight,
                    gift.Position.y);
                anchor.rotation = Quaternion.Euler(
                    0f,
                    Time.unscaledTime * 45f + giftId * 23f,
                    0f);
            }
        }

        private void RefreshActionVfx()
        {
            if (_lastActionRevision != _session.ActionRevision)
            {
                _lastActionRevision = _session.ActionRevision;
                TriggerActionVfx();
            }

            _pushVfx.gameObject.SetActive(Time.unscaledTime < _pushVfxUntil);
            _throwVfx.gameObject.SetActive(Time.unscaledTime < _throwVfxUntil);
            _dropVfx.gameObject.SetActive(Time.unscaledTime < _dropVfxUntil);
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var stunned = _session.GetPlayer(slot).StunRemaining > 0d;
                _stunVfx[slot].gameObject.SetActive(stunned);
                if (stunned)
                {
                    _stunVfx[slot].position =
                        _players[slot].position + Vector3.up * 0.04f;
                    _stunVfx[slot].Rotate(
                        Vector3.up,
                        150f * Time.unscaledDeltaTime,
                        Space.World);
                }
            }
        }

        private void TriggerActionVfx()
        {
            var actor = _session.LastActionActorSlot;
            var target = _session.LastActionTargetSlot;
            var giftId = _session.LastActionGiftId;
            var actorPosition = actor >= 0
                ? _session.GetPlayer(actor).Position
                : new Vector2(NetworkGiftGrabState.ArenaCenterX, 0f);
            var targetPosition = target >= 0
                ? _session.GetPlayer(target).Position
                : actorPosition;
            switch (_session.LastActionType)
            {
                case GiftGrabSoloActionType.Push:
                case GiftGrabSoloActionType.ThrownHit:
                    PlaceVfx(_pushVfx, targetPosition);
                    _pushVfxUntil = Time.unscaledTime + 0.38f;
                    ShowFeedback(
                        _session.LastActionType == GiftGrabSoloActionType.Push
                            ? "PUSH!"
                            : "GIFT HIT!",
                        MinigameSoloFeedbackStyle.Success,
                        0.75f);
                    break;
                case GiftGrabSoloActionType.Throw:
                    PlaceVfx(_throwVfx, actorPosition);
                    _throwVfxUntil = Time.unscaledTime + 0.38f;
                    ShowFeedback(
                        "GIFT THROWN",
                        MinigameSoloFeedbackStyle.Neutral,
                        0.65f);
                    break;
                case GiftGrabSoloActionType.Drop:
                case GiftGrabSoloActionType.BoundsReturn:
                    var position = giftId >= 0
                        ? _session.GetGift(giftId).Position
                        : targetPosition;
                    PlaceVfx(_dropVfx, position);
                    _dropVfxUntil = Time.unscaledTime + 0.38f;
                    break;
                case GiftGrabSoloActionType.Pickup:
                    if (actor == GiftGrabSoloSession.LocalPlayerSlot)
                    {
                        ShowFeedback(
                            "GIFT PICKED UP",
                            MinigameSoloFeedbackStyle.Success,
                            0.75f);
                    }
                    break;
                case GiftGrabSoloActionType.Deposit:
                    if (actor == GiftGrabSoloSession.LocalPlayerSlot)
                    {
                        ShowFeedback(
                            "GIFT STORED!",
                            MinigameSoloFeedbackStyle.Success,
                            0.9f);
                    }
                    break;
            }
        }

        private static void PlaceVfx(Transform effect, Vector2 position)
        {
            effect.position = new Vector3(position.x, 0.42f, position.y);
            effect.localScale = Vector3.one;
            effect.gameObject.SetActive(true);
        }

        private void HandleSessionTransition(
            GiftGrabSoloPhase previousPhase,
            int previousRound)
        {
            if (_session.Phase == GiftGrabSoloPhase.RoundResult)
            {
                var standing = _session.GetRoundResult(previousRound)
                    .GetStandingForSlot(GiftGrabSoloSession.LocalPlayerSlot);
                ShowFeedback(
                    "ROUND " + ToOrdinal(standing.Rank) + " · +" +
                    standing.Points + " PT",
                    MinigameSoloFeedbackStyle.Success,
                    3f);
            }
            else if (_session.Phase == GiftGrabSoloPhase.Countdown)
            {
                ResetPresentation();
            }
            else if (_session.Phase == GiftGrabSoloPhase.Complete)
            {
                ShowFeedback(
                    GetFinalStandingLabel(),
                    MinigameSoloFeedbackStyle.Success,
                    8f);
            }
        }

        private void UpdateHud()
        {
            if (_hud == null || _session == null)
            {
                return;
            }

            var local = _session.GetPlayer(GiftGrabSoloSession.LocalPlayerSlot);
            var feature = GetFeatureLabel(local);
            if (_session.Phase == GiftGrabSoloPhase.RoundResult)
            {
                var standing = _session.GetRoundResult(_session.RoundNumber)
                    .GetStandingForSlot(GiftGrabSoloSession.LocalPlayerSlot);
                feature = ToOrdinal(standing.Rank) + " · +" +
                          standing.Points + " PT · " +
                          standing.StoredGiftCount + " GIFTS";
            }
            else if (_session.Phase == GiftGrabSoloPhase.Complete)
            {
                feature = GetFinalStandingLabel();
            }

            var feedbackVisible = Time.unscaledTime < _feedbackUntil;
            _hud.SetContent(
                "DEVELOPER SOLO TEST  /  GIFT GRAB",
                "ROUND " + _session.RoundNumber + " / " +
                GiftGrabRules.RoundCount + "  ·  " +
                GetPhaseLabel() + "  ·  " +
                FormatClock(_session.RemainingSeconds),
                "YOU " + local.StoredGiftCount + " STORED  ·  " +
                (local.CarriedGiftId >= 0 ? "CARRYING" : "HANDS FREE") +
                "  ·  STUN " + local.StunRemaining.ToString("0.0") +
                "s  ·  ACTION " +
                local.ActionCooldownRemaining.ToString("0.0") +
                "s  ·  LOOSE " + _session.CountLooseGifts() +
                "  ·  SEED " + _session.Seed,
                feature,
                "WASD move + auto pickup  |  LMB throw / push  |  " +
                "R restart  |  N next seed  |  Esc stop",
                feedbackVisible ? _feedback : string.Empty,
                feedbackVisible
                    ? _feedbackStyle
                    : MinigameSoloFeedbackStyle.Neutral);
        }

        private string GetFeatureLabel(GiftGrabSoloPlayer local)
        {
            if (_session.Phase == GiftGrabSoloPhase.Countdown)
            {
                return "GET READY · FOUR CORNER BASES";
            }
            if (local.StunRemaining > 0d)
            {
                return "STUNNED · DROPPED GIFTS CAN BE TAKEN";
            }
            if (local.ActionCooldownRemaining > 0d)
            {
                return "ACTION RECOVERING · KEEP MOVING";
            }
            if (local.CarriedGiftId >= 0)
            {
                return "RETURN TO BLUE BASE · LMB THROWS";
            }
            return "COLLECT CENTER GIFTS · RAID RIVAL BASES · LMB PUSH";
        }

        private string GetPhaseLabel()
        {
            switch (_session.Phase)
            {
                case GiftGrabSoloPhase.Countdown: return "START IN";
                case GiftGrabSoloPhase.Running: return "RUNNING";
                case GiftGrabSoloPhase.RoundResult: return "ROUND RESULT";
                case GiftGrabSoloPhase.Complete: return "COMPLETE";
                default: return _session.Phase.ToString().ToUpperInvariant();
            }
        }

        private string GetFinalStandingLabel()
        {
            foreach (var entry in _session.Leaderboard)
            {
                if (entry.PlayerSlot == GiftGrabSoloSession.LocalPlayerSlot)
                {
                    return "MATCH " + ToOrdinal(entry.Rank) + " · " +
                           entry.TotalPoints + " PT · " +
                           entry.TotalStoredGiftCount + " GIFTS";
                }
            }
            return "MATCH COMPLETE";
        }

        private static Vector2 ReadMove()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }
            var move = Vector2.zero;
            if (keyboard.aKey.isPressed) move.x -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.sKey.isPressed) move.y -= 1f;
            if (keyboard.wKey.isPressed) move.y += 1f;
            return Vector2.ClampMagnitude(move, 1f);
        }

        private bool HandleKeyboardShortcuts()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                StopSoloTest();
                return true;
            }
            if (keyboard.rKey.wasPressedThisFrame)
            {
                RestartRound();
                return true;
            }
            if (keyboard.nKey.wasPressedThisFrame)
            {
                StartNextSeed();
                return true;
            }
            return false;
        }

        private void RestoreProductionPresentation()
        {
            if (!_productionCaptured)
            {
                return;
            }
            for (var giftId = 0;
                 giftId < GiftGrabRules.TotalGiftCount;
                 giftId++)
            {
                _giftSnapshots[giftId].Restore(_giftAnchors[giftId]);
            }
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                if (_playerLabels[slot] != null)
                {
                    _playerLabelSnapshots[slot].Restore(
                        _playerLabels[slot].transform);
                }
                if (_baseLabels[slot] != null)
                {
                    _baseLabelSnapshots[slot].Restore(
                        _baseLabels[slot].transform);
                }
            }
            if (_productionState != null)
            {
                _productionState.enabled = _productionStateWasEnabled;
            }
            if (_productionView != null)
            {
                _productionView.enabled = _productionViewWasEnabled;
            }
            if (_productionPlayerRoot != null)
            {
                _productionPlayerRoot.SetActive(_productionPlayerRootWasActive);
            }
            if (_productionHud != null)
            {
                _productionHud.SetActive(_productionHudWasActive);
            }
            if (_arenaPresentation != null)
            {
                _arenaPresentation.SetActive(_arenaWasActive);
            }
            foreach (var state in _cameraStates)
            {
                if (state.Camera != null)
                {
                    state.Camera.enabled = state.WasEnabled;
                }
            }
            foreach (var state in _listenerStates)
            {
                if (state.Listener != null)
                {
                    state.Listener.enabled = state.WasEnabled;
                }
            }
            _productionCaptured = false;
        }

        private void ShowFeedback(
            string message,
            MinigameSoloFeedbackStyle style,
            float seconds)
        {
            _feedback = message;
            _feedbackStyle = style;
            _feedbackUntil = Time.unscaledTime + seconds;
        }

        private static string GetPlayerName(int slot)
        {
            return slot == GiftGrabSoloSession.LocalPlayerSlot
                ? "SOLO DEV"
                : "PRACTICE " + (slot + 1);
        }

        private static void DisableBuiltInNameplate(Transform root)
        {
            var nameplate = FindDescendant(root, "NameplateAnchor");
            if (nameplate != null)
            {
                nameplate.gameObject.SetActive(false);
            }
        }

        private static void DisableGeneratedHitColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
        }

        private static bool HasFour<T>(T[] values) where T : class
        {
            if (values == null || values.Length != GiftGrabRules.PlayerCount)
            {
                return false;
            }
            foreach (var value in values)
            {
                if (value == null)
                {
                    return false;
                }
            }
            return true;
        }

        private static Transform RequireChild(Transform parent, string name)
        {
            if (parent == null)
            {
                throw new InvalidOperationException(
                    "Gift Grab scene is missing the expected parent for '" +
                    name + "'.");
            }
            var child = parent.Find(name);
            if (child == null)
            {
                throw new InvalidOperationException(
                    parent.name + " is missing child '" + name + "'.");
            }
            return child;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == name)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static string ToOrdinal(int rank)
        {
            switch (rank)
            {
                case 1: return "1ST";
                case 2: return "2ND";
                case 3: return "3RD";
                case 4: return "4TH";
                default: return "--";
            }
        }

        private static string FormatClock(double seconds)
        {
            var whole = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return (whole / 60).ToString("00") + ":" +
                   (whole % 60).ToString("00");
        }

        private void StartNextSeed()
        {
            if (_session != null)
            {
                StartNewSeed(unchecked(_session.Seed + 1));
            }
        }

        private static void StopSoloTest()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private readonly struct CameraState
        {
            public CameraState(Camera camera, bool wasEnabled)
            {
                Camera = camera;
                WasEnabled = wasEnabled;
            }

            public Camera Camera { get; }
            public bool WasEnabled { get; }
        }

        private readonly struct ListenerState
        {
            public ListenerState(AudioListener listener, bool wasEnabled)
            {
                Listener = listener;
                WasEnabled = wasEnabled;
            }

            public AudioListener Listener { get; }
            public bool WasEnabled { get; }
        }

        private readonly struct TransformSnapshot
        {
            private TransformSnapshot(
                Vector3 position,
                Quaternion rotation,
                Vector3 scale,
                bool active)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
                Active = active;
            }

            private Vector3 Position { get; }
            private Quaternion Rotation { get; }
            private Vector3 Scale { get; }
            private bool Active { get; }

            public static TransformSnapshot Capture(Transform transform)
            {
                return new TransformSnapshot(
                    transform.localPosition,
                    transform.localRotation,
                    transform.localScale,
                    transform.gameObject.activeSelf);
            }

            public void Restore(Transform transform)
            {
                if (transform == null)
                {
                    return;
                }
                transform.localPosition = Position;
                transform.localRotation = Rotation;
                transform.localScale = Scale;
                transform.gameObject.SetActive(Active);
            }
        }
    }
}

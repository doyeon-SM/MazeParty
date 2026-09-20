using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.BombPassing;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    /// <summary>
    /// Offline practice on the production Bomb Passing arena and world art.
    /// Only the existing developer HUD prefab is shown; gameplay has no UI.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class BombPassingSoloTestController : MonoBehaviour
    {
        public const int LocalPlayerSlot = 0;

        private static readonly Color[] PlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        private static readonly Color BombColor =
            new Color(1f, 0.23f, 0.1f);
        private static readonly int BaseColorProperty =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty =
            Shader.PropertyToID("_Color");
        private static readonly int EmissionColorProperty =
            Shader.PropertyToID("_EmissionColor");

        private readonly Transform[] _players =
            new Transform[BombPassingRules.PlayerCount];
        private readonly PlayerAvatarVisual[] _visuals =
            new PlayerAvatarVisual[BombPassingRules.PlayerCount];
        private readonly List<CameraState> _cameraStates =
            new List<CameraState>();
        private readonly List<ListenerState> _listenerStates =
            new List<ListenerState>();

        private BombPassingMatchState _match;
        private MinigameSoloHudView _hud;
        private NetworkBombPassingState _productionState;
        private BombPassingNetworkView _productionView;
        private Transform _arena;
        private Transform _bombTransform;
        private Renderer _bombRenderer;
        private Light _bombLight;
        private Camera _runtimeCamera;
        private MaterialPropertyBlock _colorBlock;
        private float _baseLightIntensity;
        private double _phaseElapsed;
        private int _seed;
        private bool _initialized;
        private bool _productionStateWasEnabled;
        private bool _productionViewWasEnabled;
        private bool _arenaWasActive;

        private enum SoloPhase : byte
        {
            Countdown,
            Playing,
            Complete
        }

        private SoloPhase _phase;

        public bool IsInitialized => _initialized;
        public int Seed => _seed;
        public BombPassingMatchState Match => _match;
        public Camera RuntimeCamera => _runtimeCamera;
        public Transform LocalPlayer => _players[LocalPlayerSlot];

        public void ConfigureHud(MinigameSoloHudView hud)
        {
            _hud = hud;
        }

        public void Begin(int seed)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "Bomb Passing solo is already initialized.");
            }

            if (_hud == null || !_hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Bomb Passing solo requires the authored " +
                    "developer HUD prefab instance.");
            }

            ResolveProductionScene();
            _hud.BindActions(RestartMatch, StartNextSeed, StopSoloTest);
            _colorBlock = new MaterialPropertyBlock();
            CreateRuntimeCamera();
            _initialized = true;
            BeginMatch(seed);
            Debug.Log(
                "[Minigame Solo Test] Bomb Passing started. " +
                "WASD moves, left click passes or stuns; three " +
                "practice opponents use the production arena.");
        }

        private void Update()
        {
            if (!_initialized || _match == null ||
                HandleKeyboardShortcuts())
            {
                return;
            }

            var delta = Math.Max(0d, Time.unscaledDeltaTime);
            switch (_phase)
            {
                case SoloPhase.Countdown:
                    _phaseElapsed += delta;
                    if (_phaseElapsed >=
                        BombPassingRules.CountdownSeconds)
                    {
                        _phase = SoloPhase.Playing;
                        _phaseElapsed = 0d;
                    }
                    break;
                case SoloPhase.Playing:
                    UpdateInputsAndPracticePlayers();
                    _match.AdvanceTo(
                        _match.MatchElapsedSeconds + delta);
                    if (_match.IsComplete)
                    {
                        _phase = SoloPhase.Complete;
                        _phaseElapsed = 0d;
                    }
                    break;
                case SoloPhase.Complete:
                    _phaseElapsed = Math.Min(
                        BombPassingRules.ResultSeconds,
                        _phaseElapsed + delta);
                    break;
            }

            RefreshPresentation();
        }

        private void OnDestroy()
        {
            if (_hud != null)
            {
                _hud.BindActions(null, null, null);
            }

            if (_productionState != null)
            {
                _productionState.enabled = _productionStateWasEnabled;
            }
            if (_productionView != null)
            {
                _productionView.enabled = _productionViewWasEnabled;
            }
            if (_arena != null)
            {
                _arena.gameObject.SetActive(_arenaWasActive);
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
        }

        private void ResolveProductionScene()
        {
            _productionState = FindAnyObjectByType<
                NetworkBombPassingState>(FindObjectsInactive.Include);
            _productionView = FindAnyObjectByType<
                BombPassingNetworkView>(FindObjectsInactive.Include);
            if (_productionState == null || _productionView == null ||
                _productionView.ArenaPresentation == null ||
                _productionView.PlayerRoot == null ||
                _productionView.BombTransform == null ||
                _productionView.BombRenderer == null ||
                _productionView.BombLight == null)
            {
                throw new InvalidOperationException(
                    "Bomb Passing scene presentation contract is " +
                    "incomplete.");
            }

            _arena = _productionView.ArenaPresentation.transform;
            _bombTransform = _productionView.BombTransform;
            _bombRenderer = _productionView.BombRenderer;
            _bombLight = _productionView.BombLight;
            _baseLightIntensity = Mathf.Max(
                0.01f,
                _bombLight.intensity);
            _productionStateWasEnabled = _productionState.enabled;
            _productionViewWasEnabled = _productionView.enabled;
            _arenaWasActive = _arena.gameObject.activeSelf;
            _productionState.enabled = false;
            _productionView.enabled = false;
            _arena.gameObject.SetActive(true);
            _productionView.PlayerRoot.gameObject.SetActive(true);

            for (var slot = 0; slot < _players.Length; slot++)
            {
                _players[slot] =
                    _productionView.GetPlayerTransform(slot);
                _visuals[slot] =
                    _productionView.GetPlayerVisual(slot);
                if (_players[slot] != null && _visuals[slot] != null)
                {
                    continue;
                }

                // A disabled additive scene can skip the production view's
                // Awake. Build only world-space practice avatars, never UI.
                var player = new GameObject(
                    "Solo Bomb Passing Player " + (slot + 1));
                player.transform.SetParent(
                    _productionView.PlayerRoot,
                    false);
                var visual = player.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                DisableGeneratedColliders(player);
                _players[slot] = player.transform;
                _visuals[slot] = visual;
            }
        }

        private void CreateRuntimeCamera()
        {
            foreach (var camera in FindObjectsByType<Camera>(
                         FindObjectsInactive.Include))
            {
                _cameraStates.Add(
                    new CameraState(camera, camera.enabled));
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
                "Solo Bomb Passing Camera",
                typeof(Camera),
                typeof(AudioListener));
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.SetPositionAndRotation(
                BombPassingNetworkView.SharedCameraPosition,
                BombPassingNetworkView.SharedCameraRotation);
            _runtimeCamera = cameraObject.GetComponent<Camera>();
            _runtimeCamera.orthographic = true;
            _runtimeCamera.orthographicSize =
                BombPassingNetworkView.SharedCameraOrthographicSize;
            _runtimeCamera.nearClipPlane = 0.1f;
            _runtimeCamera.farClipPlane = 100f;
            _runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.018f, 0.024f, 0.036f);
        }

        private void BeginMatch(int seed)
        {
            _seed = seed;
            _match = new BombPassingMatchState(seed);
            _phase = SoloPhase.Countdown;
            _phaseElapsed = 0d;
            RefreshPresentation();
        }

        private void UpdateInputsAndPracticePlayers()
        {
            var keyboard = Keyboard.current;
            var horizontal = 0d;
            var vertical = 0d;
            if (keyboard != null)
            {
                horizontal =
                    (keyboard.dKey.isPressed ? 1d : 0d) -
                    (keyboard.aKey.isPressed ? 1d : 0d);
                vertical =
                    (keyboard.wKey.isPressed ? 1d : 0d) -
                    (keyboard.sKey.isPressed ? 1d : 0d);
            }

            _match.SetMovementInput(
                LocalPlayerSlot,
                horizontal,
                vertical);
            if (Mouse.current != null &&
                Mouse.current.leftButton.wasPressedThisFrame)
            {
                _match.TryAttack(LocalPlayerSlot);
            }

            for (var slot = 1; slot < BombPassingRules.PlayerCount;
                 slot++)
            {
                UpdatePracticePlayer(slot);
            }
        }

        private void UpdatePracticePlayer(int slot)
        {
            var player = _match.GetPlayer(slot);
            if (player.IsEliminated)
            {
                _match.SetMovementInput(slot, 0d, 0d);
                return;
            }

            var bomb = _match.GetBomb();
            var targetX = bomb.X;
            var targetZ = bomb.Z;
            var attack = false;
            if (bomb.HolderSlot == slot)
            {
                var opponent = FindClosestOpponent(slot);
                if (opponent >= 0)
                {
                    var target = _match.GetPlayer(opponent);
                    targetX = target.X;
                    targetZ = target.Z;
                    attack = true;
                }
            }
            else if (bomb.HolderSlot >= 0)
            {
                var carrier = _match.GetPlayer(bomb.HolderSlot);
                var deltaX = carrier.X - player.X;
                var deltaZ = carrier.Z - player.Z;
                var close = (deltaX * deltaX) + (deltaZ * deltaZ) <
                    BombPassingRules.AttackDepth *
                    BombPassingRules.AttackDepth;
                targetX = close
                    ? carrier.X
                    : player.X - deltaX;
                targetZ = close
                    ? carrier.Z
                    : player.Z - deltaZ;
                attack = close;
            }

            var moveX = targetX - player.X;
            var moveZ = targetZ - player.Z;
            var magnitude = Math.Sqrt(
                (moveX * moveX) + (moveZ * moveZ));
            if (magnitude > 0.001d)
            {
                moveX /= magnitude;
                moveZ /= magnitude;
            }
            _match.SetMovementInput(slot, moveX, moveZ);
            if (attack)
            {
                _match.TryAttack(slot);
            }
        }

        private int FindClosestOpponent(int slot)
        {
            var actor = _match.GetPlayer(slot);
            var closest = -1;
            var bestDistance = double.MaxValue;
            for (var other = 0;
                 other < BombPassingRules.PlayerCount;
                 other++)
            {
                var target = _match.GetPlayer(other);
                if (other == slot || target.IsEliminated)
                {
                    continue;
                }

                var deltaX = target.X - actor.X;
                var deltaZ = target.Z - actor.Z;
                var distance =
                    (deltaX * deltaX) + (deltaZ * deltaZ);
                if (distance < bestDistance)
                {
                    closest = other;
                    bestDistance = distance;
                }
            }

            return closest;
        }

        private void RefreshPresentation()
        {
            if (_match == null || _arena == null)
            {
                return;
            }

            for (var slot = 0;
                 slot < BombPassingRules.PlayerCount;
                 slot++)
            {
                var player = _match.GetPlayer(slot);
                _players[slot].position = _arena.TransformPoint(
                    new Vector3(
                        (float)player.X,
                        BombPassingNetworkView.PlayerPresentationHeight,
                        (float)player.Z));
                _players[slot].rotation = Quaternion.LookRotation(
                    new Vector3(
                        (float)player.FacingX,
                        0f,
                        (float)player.FacingZ),
                    Vector3.up);
                _visuals[slot].SetBodyColor(PlayerColors[slot]);
                _visuals[slot].SetDisplayName(
                    slot == LocalPlayerSlot
                        ? "SOLO DEV"
                        : "PRACTICE " + (slot + 1));
                _visuals[slot].SetOwnerFirstPerson(false);
                _visuals[slot].SetTopViewHighlight(
                    slot == LocalPlayerSlot);
                _visuals[slot].SetEliminated(player.IsEliminated);
            }

            RefreshBomb();
            RefreshDeveloperHud();
        }

        private void RefreshBomb()
        {
            var visible = _phase == SoloPhase.Playing &&
                !_match.IsComplete;
            _bombTransform.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            var bomb = _match.GetBomb();
            _bombTransform.position = _arena.TransformPoint(
                new Vector3(
                    (float)bomb.X,
                    bomb.HolderSlot >= 0
                        ? BombPassingNetworkView.CarriedBombHeight
                        : BombPassingNetworkView.GroundBombHeight,
                    (float)bomb.Z));
            _bombTransform.Rotate(
                Vector3.up,
                100f * Time.unscaledDeltaTime,
                Space.World);
            var ratio = bomb.RemainingRatio;
            var frequency = ratio <= 0.1d
                ? 9f
                : ratio <= 0.2d
                    ? 5f
                    : ratio <= 0.5d
                        ? 2f
                        : 0f;
            var on = frequency <= 0f ||
                Mathf.Repeat(Time.unscaledTime * frequency, 1f) < 0.5f;
            _bombLight.enabled = on;
            _bombLight.intensity = on ? _baseLightIntensity : 0f;
            _colorBlock.Clear();
            _colorBlock.SetColor(BaseColorProperty, BombColor);
            _colorBlock.SetColor(ColorProperty, BombColor);
            _colorBlock.SetColor(
                EmissionColorProperty,
                on ? BombColor * 3f : BombColor * 0.15f);
            _bombRenderer.SetPropertyBlock(_colorBlock);
        }

        private void RefreshDeveloperHud()
        {
            if (_hud == null)
            {
                return;
            }

            var local = _match.GetPlayer(LocalPlayerSlot);
            var bomb = _match.GetBomb();
            var rank = _match.IsComplete
                ? _match.GetFinalRank(LocalPlayerSlot)
                : local.Rank;
            var phaseLabel = _phase == SoloPhase.Countdown
                ? "START IN " +
                  Math.Max(
                      0d,
                      BombPassingRules.CountdownSeconds -
                      _phaseElapsed).ToString("0.0") + "s"
                : _phase == SoloPhase.Complete
                    ? "COMPLETE"
                    : "BOMB " + bomb.BombNumber +
                      " · " +
                      bomb.RemainingSeconds.ToString("0.0") + "s";
            var outcome = rank > 0
                ? MinigameDisplayFormatter.ToOrdinal(rank) +
                  " · GOLD +" +
                  MinigameRewardRules.GetFinalPlacementGold(rank)
                : local.IsStunned
                    ? "STUNNED " +
                      local.StunRemainingSeconds.ToString("0.0") + "s"
                    : bomb.HolderSlot == LocalPlayerSlot
                        ? "YOU HAVE THE BOMB · CLICK TO PASS"
                        : bomb.HolderSlot >= 0
                            ? "CARRIER P" +
                              (bomb.HolderSlot + 1) +
                              " · CLICK TO STUN"
                            : "CENTER BOMB · TOUCH TO PICK UP";
            var last = _match.LastExplosion;
            var feedback = last.HasValue
                ? last.Value.EliminatedSlot >= 0
                    ? "P" +
                      (last.Value.EliminatedSlot + 1) +
                      " ELIMINATED"
                    : "UNHELD BOMB EXPLODED · NEW BOMB"
                : "THE FUSE STARTS WHEN EACH BOMB SPAWNS";
            _hud.SetContent(
                "BOMB PASSING · SOLO",
                phaseLabel,
                "SURVIVORS " + _match.SurvivorCount +
                " / 4 · SEED " + _seed,
                outcome,
                "WASD MOVE · LMB PASS / STUN · " +
                "R RESTART · N NEXT SEED · ESC STOP",
                feedback,
                rank == 1
                    ? MinigameSoloFeedbackStyle.Success
                    : rank > 1
                        ? MinigameSoloFeedbackStyle.Warning
                        : MinigameSoloFeedbackStyle.Neutral);
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
                RestartMatch();
                return true;
            }
            if (keyboard.nKey.wasPressedThisFrame)
            {
                StartNextSeed();
                return true;
            }

            return false;
        }

        private void RestartMatch()
        {
            BeginMatch(_seed);
        }

        private void StartNextSeed()
        {
            BeginMatch(unchecked(_seed + 1));
        }

        private static void DisableGeneratedColliders(GameObject root)
        {
            foreach (var collider in
                     root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
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
            public ListenerState(
                AudioListener listener,
                bool wasEnabled)
            {
                Listener = listener;
                WasEnabled = wasEnabled;
            }

            public AudioListener Listener { get; }
            public bool WasEnabled { get; }
        }
    }
}

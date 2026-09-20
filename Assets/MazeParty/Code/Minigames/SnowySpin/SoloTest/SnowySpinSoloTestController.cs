using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.SnowySpin;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    /// <summary>
    /// Offline practice using the production Snowy Spin arena and spheres.
    /// Its only Canvas is the existing developer HUD prefab.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class SnowySpinSoloTestController : MonoBehaviour
    {
        public const int LocalPlayerSlot = 0;

        private static readonly Color[] PlayerColors =
        {
            new Color(0.18f, 0.58f, 1f),
            new Color(1f, 0.32f, 0.24f),
            new Color(0.25f, 0.86f, 0.48f),
            new Color(0.82f, 0.35f, 1f)
        };

        private static readonly int BaseColorProperty =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty =
            Shader.PropertyToID("_Color");
        private readonly Transform[] _balls =
            new Transform[SnowySpinRules.PlayerCount];
        private readonly Renderer[] _renderers =
            new Renderer[SnowySpinRules.PlayerCount];
        private readonly List<CameraState> _cameras =
            new List<CameraState>();
        private readonly List<ListenerState> _listeners =
            new List<ListenerState>();

        private SnowySpinMatchState _match;
        private NetworkSnowySpinState _productionState;
        private SnowySpinNetworkView _productionView;
        private MinigameSoloHudView _hud;
        private Transform _arena;
        private Camera _runtimeCamera;
        private MaterialPropertyBlock _colorBlock;
        private double _phaseElapsed;
        private int _seed;
        private bool _initialized;
        private bool _stateWasEnabled;
        private bool _viewWasEnabled;
        private bool _arenaWasActive;
        private SoloPhase _phase;

        private enum SoloPhase : byte
        {
            Countdown,
            Playing,
            RoundBreak,
            Complete
        }

        public bool IsInitialized => _initialized;
        public int Seed => _seed;
        public SnowySpinMatchState Match => _match;
        public Camera RuntimeCamera => _runtimeCamera;
        public Transform LocalPlayer => _balls[LocalPlayerSlot];

        public void ConfigureHud(MinigameSoloHudView hud)
        {
            _hud = hud;
        }

        public void Begin(int seed)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "Snowy Spin solo is already initialized.");
            }

            if (_hud == null || !_hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Snowy Spin solo requires the authored developer HUD.");
            }

            ResolveProductionScene();
            _hud.BindActions(RestartMatch, StartNextSeed, StopSoloTest);
            _colorBlock = new MaterialPropertyBlock();
            CreateRuntimeCamera();
            _initialized = true;
            BeginMatch(seed);
            Debug.Log("[Minigame Solo Test] Snowy Spin started. " +
                "WASD rolls your ball against three practice opponents.");
        }

        private void Update()
        {
            if (!_initialized || _match == null ||
                HandleKeyboardShortcuts())
            {
                return;
            }

            Tick(Math.Max(0d, Time.unscaledDeltaTime));
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
                _productionState.enabled = _stateWasEnabled;
            }

            if (_productionView != null)
            {
                _productionView.enabled = _viewWasEnabled;
            }

            if (_arena != null)
            {
                _arena.gameObject.SetActive(_arenaWasActive);
            }

            foreach (var state in _cameras)
            {
                if (state.Camera != null)
                {
                    state.Camera.enabled = state.WasEnabled;
                }
            }

            foreach (var state in _listeners)
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
                NetworkSnowySpinState>(FindObjectsInactive.Include);
            _productionView = FindAnyObjectByType<
                SnowySpinNetworkView>(FindObjectsInactive.Include);
            if (_productionState == null || _productionView == null ||
                _productionView.ArenaPresentation == null)
            {
                throw new InvalidOperationException(
                    "Snowy Spin production scene is incomplete.");
            }

            _arena = _productionView.ArenaPresentation.transform;
            for (var slot = 0; slot < _balls.Length; slot++)
            {
                _balls[slot] = _productionView.GetBallTransform(slot);
                _renderers[slot] = _productionView.GetBallRenderer(slot);
                if (_balls[slot] == null || _renderers[slot] == null)
                {
                    throw new InvalidOperationException(
                        "Snowy Spin scene is missing ball " +
                        (slot + 1) + ".");
                }
            }

            _stateWasEnabled = _productionState.enabled;
            _viewWasEnabled = _productionView.enabled;
            _arenaWasActive = _arena.gameObject.activeSelf;
            _productionState.enabled = false;
            _productionView.enabled = false;
            _arena.gameObject.SetActive(true);
            for (var slot = 0; slot < _balls.Length; slot++)
            {
                _balls[slot].gameObject.SetActive(true);
            }
        }

        private void CreateRuntimeCamera()
        {
            foreach (var camera in FindObjectsByType<Camera>(
                         FindObjectsInactive.Include))
            {
                _cameras.Add(new CameraState(camera, camera.enabled));
                camera.enabled = false;
            }

            foreach (var listener in FindObjectsByType<AudioListener>(
                         FindObjectsInactive.Include))
            {
                _listeners.Add(new ListenerState(listener,
                    listener.enabled));
                listener.enabled = false;
            }

            var cameraObject = new GameObject(
                "Solo Snowy Spin Camera", typeof(Camera),
                typeof(AudioListener));
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.SetPositionAndRotation(
                SnowySpinNetworkView.SharedCameraPosition,
                SnowySpinNetworkView.SharedCameraRotation);
            _runtimeCamera = cameraObject.GetComponent<Camera>();
            _runtimeCamera.orthographic = true;
            _runtimeCamera.orthographicSize =
                SnowySpinNetworkView.SharedCameraOrthographicSize;
            _runtimeCamera.nearClipPlane = 0.1f;
            _runtimeCamera.farClipPlane = 100f;
            _runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.018f, 0.024f, 0.036f);
        }

        private void BeginMatch(int seed)
        {
            _seed = seed;
            _match = new SnowySpinMatchState(seed);
            _phase = SoloPhase.Countdown;
            _phaseElapsed = 0d;
            RefreshPresentation();
        }

        private void Tick(double delta)
        {
            switch (_phase)
            {
                case SoloPhase.Countdown:
                    _phaseElapsed += delta;
                    if (_phaseElapsed >= SnowySpinRules.CountdownSeconds)
                    {
                        _phase = SoloPhase.Playing;
                        _phaseElapsed = 0d;
                    }
                    break;
                case SoloPhase.Playing:
                    UpdateInputs();
                    _match.AdvanceTo(_match.RoundElapsedSeconds + delta);
                    if (_match.IsRoundComplete)
                    {
                        _phase = _match.IsComplete
                            ? SoloPhase.Complete
                            : SoloPhase.RoundBreak;
                        _phaseElapsed = 0d;
                    }
                    break;
                case SoloPhase.RoundBreak:
                    _phaseElapsed += delta;
                    if (_phaseElapsed >= SnowySpinRules.ResultSeconds)
                    {
                        _match.BeginNextRound();
                        _phase = SoloPhase.Countdown;
                        _phaseElapsed = 0d;
                    }
                    break;
                case SoloPhase.Complete:
                    _phaseElapsed = Math.Min(
                        SnowySpinRules.ResultSeconds,
                        _phaseElapsed + delta);
                    break;
            }
        }

        private void UpdateInputs()
        {
            var keyboard = Keyboard.current;
            var x = keyboard == null ? 0d :
                (keyboard.dKey.isPressed ? 1d : 0d) -
                (keyboard.aKey.isPressed ? 1d : 0d);
            var z = keyboard == null ? 0d :
                (keyboard.wKey.isPressed ? 1d : 0d) -
                (keyboard.sKey.isPressed ? 1d : 0d);
            _match.SetMovementInput(LocalPlayerSlot, x, z);

            for (var slot = 1; slot < SnowySpinRules.PlayerCount;
                slot++)
            {
                UpdatePracticePlayer(slot);
            }
        }

        private void UpdatePracticePlayer(int slot)
        {
            var ball = _match.GetPlayer(slot);
            if (ball.IsEliminated)
            {
                return;
            }

            var targetSlot = -1;
            var bestDistance = double.MaxValue;
            for (var other = 0; other < SnowySpinRules.PlayerCount;
                other++)
            {
                var opponent = _match.GetPlayer(other);
                if (other == slot || opponent.IsEliminated)
                {
                    continue;
                }

                var dx = opponent.X - ball.X;
                var dz = opponent.Z - ball.Z;
                var distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    targetSlot = other;
                }
            }

            var radius = Math.Sqrt(ball.X * ball.X + ball.Z * ball.Z);
            var outwardSpeed = radius > 0.001d
                ? (ball.X * ball.VelocityX +
                   ball.Z * ball.VelocityZ) / radius
                : 0d;
            double steerX;
            double steerZ;
            if (radius > 6.2d && outwardSpeed > 0d)
            {
                steerX = -ball.X;
                steerZ = -ball.Z;
            }
            else if (targetSlot >= 0)
            {
                var opponent = _match.GetPlayer(targetSlot);
                steerX = opponent.X - ball.X;
                steerZ = opponent.Z - ball.Z;
            }
            else
            {
                steerX = -ball.X;
                steerZ = -ball.Z;
            }

            var length = Math.Sqrt(steerX * steerX +
                steerZ * steerZ);
            _match.SetMovementInput(slot,
                length > 0.001d ? steerX / length : 0d,
                length > 0.001d ? steerZ / length : 0d);
        }

        private void RefreshPresentation()
        {
            if (_match == null || _arena == null)
            {
                return;
            }

            for (var slot = 0; slot < _balls.Length; slot++)
            {
                var ball = _match.GetPlayer(slot);
                _balls[slot].position = _arena.TransformPoint(
                    new Vector3((float)ball.X,
                        ball.IsEliminated ? -2.5f :
                            SnowySpinNetworkView.BallPresentationHeight,
                        (float)ball.Z));
                if (!ball.IsEliminated &&
                    _phase == SoloPhase.Playing)
                {
                    var axis = new Vector3((float)ball.VelocityZ,
                        0f, (float)-ball.VelocityX);
                    if (axis.sqrMagnitude > 0.0001f)
                    {
                        _balls[slot].Rotate(axis.normalized,
                            axis.magnitude * Mathf.Rad2Deg *
                            Time.unscaledDeltaTime /
                            (float)SnowySpinRules.BallRadius,
                            Space.World);
                    }
                }

                _colorBlock.Clear();
                _colorBlock.SetColor(BaseColorProperty,
                    PlayerColors[slot]);
                _colorBlock.SetColor(ColorProperty,
                    PlayerColors[slot]);
                _renderers[slot].SetPropertyBlock(_colorBlock);
            }

            RefreshDeveloperHud();
        }

        private void RefreshDeveloperHud()
        {
            if (_hud == null)
            {
                return;
            }

            var local = _match.GetPlayer(LocalPlayerSlot);
            var finalRank = _match.IsComplete
                ? _match.GetFinalRank(LocalPlayerSlot) : 0;
            var phaseLabel = _phase == SoloPhase.Playing
                ? "ROUND " + _match.RoundNumber + " / 3 · " +
                  Math.Max(0d, SnowySpinRules.RoundDurationSeconds -
                      _match.RoundElapsedSeconds).ToString("0.0") + "s"
                : _phase == SoloPhase.Complete
                    ? "COMPLETE"
                    : _phase == SoloPhase.RoundBreak
                        ? "ROUND RESULT · NEXT IN " +
                          Math.Max(0d,
                              SnowySpinRules.ResultSeconds -
                              _phaseElapsed).ToString("0.0") + "s"
                        : "ROUND " + _match.RoundNumber +
                          " START IN " +
                          Math.Max(0d,
                              SnowySpinRules.CountdownSeconds -
                              _phaseElapsed).ToString("0.0") + "s";
            var outcome = finalRank > 0
                ? MinigameDisplayFormatter.ToOrdinal(finalRank) +
                  " · GOLD +" +
                  MinigameRewardRules.GetFinalPlacementGold(finalRank)
                : local.IsEliminated
                    ? "FELL · WAIT FOR THE NEXT ROUND"
                    : "ROLL AND PUSH OPPONENTS OFF THE RIM";
            var lastFall = _match.LastFall;
            var feedback = lastFall.HasValue
                ? "P" + (lastFall.Value.Slot + 1) +
                  " FELL IN ROUND " + lastFall.Value.RoundNumber
                : "60s LIMIT · SURVIVORS RANK BY CENTER DISTANCE";
            _hud.SetContent(
                "SNOWY SPIN · SOLO",
                phaseLabel,
                "SURVIVORS " + _match.SurvivorCount +
                " / 4 · SCORE " + local.TotalScore +
                " · SEED " + _seed,
                outcome,
                "WASD ROLL · R RESTART · N NEXT SEED · ESC STOP",
                feedback,
                finalRank == 1
                    ? MinigameSoloFeedbackStyle.Success
                    : finalRank > 1 || local.IsEliminated
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
            public ListenerState(AudioListener listener,
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

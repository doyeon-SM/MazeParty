using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    /// <summary>
    /// Offline Red Light / Green Light harness injected by the minigame solo
    /// launcher. It uses the generated production arena and pure gameplay rules
    /// without starting NGO or Unity Gaming Services.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class RedLightGreenLightSoloTestController : MonoBehaviour
    {
        private const float RunnerStartZ =
            NetworkRedLightGreenLightState.ArenaMinZ + 0.75f;
        private const float FinishWorldZ =
            NetworkRedLightGreenLightState.ArenaMaxZ - 0.5f;
        private const float RunnerBoundsPadding = 0.8f;

        private RedLightGreenLightSoloSession _session;
        private Transform _runtimeRoot;
        private Transform _runnerRoot;
        private PlayerAvatarVisual _avatarVisual;
        private RedLightGreenLightPlayerPresentation _presentation;
        private Camera _runtimeCamera;
        private Transform _observerHead;
        private Renderer _greenSignalRenderer;
        private Renderer _redSignalRenderer;
        private Light _greenSignalLight;
        private Light _redSignalLight;
        private MaterialPropertyBlock _signalProperties;
        private string _feedback = string.Empty;
        private MinigameSoloFeedbackStyle _feedbackStyle =
            MinigameSoloFeedbackStyle.Neutral;
        private float _feedbackUntil = float.NegativeInfinity;
        private RedLightGreenLightSignalPhase _lastSignal =
            (RedLightGreenLightSignalPhase)byte.MaxValue;
        private MinigameSoloHudView _hud;
        private bool _initialized;

        public bool IsInitialized => _initialized;
        public RedLightGreenLightSoloSession Session => _session;
        public Camera RuntimeCamera => _runtimeCamera;
        public Transform RunnerRoot => _runnerRoot;

        public void ConfigureHud(MinigameSoloHudView hud)
        {
            _hud = hud;
        }

        public void Begin(int seed)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The Red Light / Green Light solo harness is already initialized.");
            }
            if (_hud == null || !_hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "The Red Light / Green Light solo harness requires a " +
                    "valid MinigameSoloHud prefab instance.");
            }

            _hud.BindActions(RestartRound, StartNextSeed, StopSoloTest);

            DisableProductionPresentation();
            ResolveArenaContract();
            _signalProperties = new MaterialPropertyBlock();
            CreateRuntimeRoot();
            CreateRunner();
            CreateRuntimeCamera();

            _session = new RedLightGreenLightSoloSession();
            _session.Begin(seed);
            ResetRoundPresentation();
            RefreshSignalPresentation();
            _initialized = true;
            UpdateHud();

            Debug.Log(
                "[Minigame Solo Test] Red Light / Green Light started with seed " +
                seed + ". No network session was created.");
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

            var previousPhase = _session.Phase;
            var previousRound = _session.RoundNumber;
            _session.Tick(Time.unscaledDeltaTime);
            ApplySessionTransition(previousPhase, previousRound);

            if (_session.Phase == RedLightGreenLightSoloPhase.Running)
            {
                SimulateLocalRunner();
            }

            ApplyPlayerPresentation();
            RefreshSignalPresentation();
            UpdateHud();
        }

        private void LateUpdate()
        {
            if (!_initialized ||
                _runtimeCamera == null ||
                _runnerRoot == null)
            {
                return;
            }

            _runtimeCamera.transform.SetPositionAndRotation(
                RedLightGreenLightNetworkView
                    .CalculatePlayerCameraPosition(_runnerRoot.position),
                RedLightGreenLightNetworkView.PlayerCameraRotation);
        }

        public void RestartRound()
        {
            if (_session == null)
            {
                return;
            }

            _session.RestartCurrentRound();
            ResetRoundPresentation();
            UpdateHud();
        }

        public void StartNewSeed(int seed)
        {
            if (_session == null)
            {
                return;
            }

            _session.Begin(seed);
            ResetRoundPresentation();
            UpdateHud();
        }

        private void SimulateLocalRunner()
        {
            var input = ReadMovementInput();
            var hasInput = input.sqrMagnitude > 0.0001f;
            if (hasInput)
            {
                _session.TrySubmitMovementIntent(
                    true,
                    out var resolution);
                ApplyMovementFeedback(resolution);
            }

            if (_session.Phase != RedLightGreenLightSoloPhase.Running ||
                !hasInput ||
                _session.SignalPhase == RedLightGreenLightSignalPhase.Red ||
                _session.LocalPlayer == null ||
                !_session.LocalPlayer.CanMove)
            {
                return;
            }

            input = Vector2.ClampMagnitude(input, 1f);
            var direction = new Vector3(input.x, 0f, input.y);
            var speed = _session.LocalPlayer
                .MovementSpeedMetersPerSecond;
            var position = _runnerRoot.position +
                           direction * speed * Time.unscaledDeltaTime;
            position.x = Mathf.Clamp(
                position.x,
                NetworkRedLightGreenLightState.ArenaMinX +
                RunnerBoundsPadding,
                NetworkRedLightGreenLightState.ArenaMaxX -
                RunnerBoundsPadding);
            position.y = 0f;
            position.z = Mathf.Clamp(
                position.z,
                RunnerStartZ,
                NetworkRedLightGreenLightState.ArenaMaxZ);
            _runnerRoot.position = position;
            _runnerRoot.rotation = Quaternion.LookRotation(
                direction.normalized,
                Vector3.up);

            var progress = GetProgressMeters();
            _session.SetLocalProgress(progress);
            if (position.z >= FinishWorldZ)
            {
                _session.TryFinishLocal(progress);
            }
        }

        private void DisableProductionPresentation()
        {
            var state = FindAnyObjectByType<
                NetworkRedLightGreenLightState>(
                FindObjectsInactive.Include);
            if (state == null)
            {
                throw new InvalidOperationException(
                    "The active scene does not contain NetworkRedLightGreenLightState.");
            }
            state.enabled = false;

            var view = state.GetComponent<RedLightGreenLightNetworkView>();
            if (view != null)
            {
                view.enabled = false;
            }

            SetNamedObjectActive("Runtime Runners", false);
            SetNamedObjectActive("Red Light Green Light HUD", false);
        }

        private void ResolveArenaContract()
        {
            var arena = FindNamedTransform("Arena Presentation");
            if (arena == null)
            {
                throw new InvalidOperationException(
                    "Red Light / Green Light scene contract is missing Arena Presentation.");
            }
            arena.gameObject.SetActive(true);

            _observerHead = FindNamedTransform("Observer Head");
            _greenSignalRenderer =
                FindNamedTransform("Green Signal")?.GetComponent<Renderer>();
            _redSignalRenderer =
                FindNamedTransform("Red Signal")?.GetComponent<Renderer>();
            _greenSignalLight =
                FindNamedTransform("Green Signal Light")?.GetComponent<Light>();
            _redSignalLight =
                FindNamedTransform("Red Signal Light")?.GetComponent<Light>();
        }

        private void CreateRuntimeRoot()
        {
            var rootObject = new GameObject("[Solo Test] Runtime");
            rootObject.transform.SetParent(transform, false);
            _runtimeRoot = rootObject.transform;
        }

        private void CreateRunner()
        {
            var runnerObject = new GameObject("Solo Runner");
            runnerObject.transform.SetParent(_runtimeRoot, false);
            _runnerRoot = runnerObject.transform;

            _avatarVisual = runnerObject.AddComponent<PlayerAvatarVisual>();
            _avatarVisual.EnsureBuilt();
            _avatarVisual.SetBodyColor(
                new Color(0.18f, 0.75f, 1f, 1f));
            _avatarVisual.SetDisplayName("SOLO DEV");
            _avatarVisual.SetTopViewHighlight(true);
            _presentation = runnerObject.AddComponent<
                RedLightGreenLightPlayerPresentation>();

            var colliders = runnerObject.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }
        }

        private void CreateRuntimeCamera()
        {
            var cameras = FindObjectsByType<Camera>(
                FindObjectsInactive.Include);
            for (var index = 0; index < cameras.Length; index++)
            {
                cameras[index].enabled = false;
            }

            var listeners = FindObjectsByType<AudioListener>(
                FindObjectsInactive.Include);
            for (var index = 0; index < listeners.Length; index++)
            {
                listeners[index].enabled = false;
            }

            var cameraObject = new GameObject(
                "Solo Output Camera",
                typeof(Camera),
                typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(_runtimeRoot, false);

            _runtimeCamera = cameraObject.GetComponent<Camera>();
            _runtimeCamera.orthographic = true;
            _runtimeCamera.orthographicSize =
                RedLightGreenLightNetworkView
                    .PlayerCameraOrthographicSize;
            _runtimeCamera.nearClipPlane = 0.1f;
            _runtimeCamera.farClipPlane = 150f;
            _runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.025f, 0.035f, 0.055f, 1f);
        }

        private void ApplySessionTransition(
            RedLightGreenLightSoloPhase previousPhase,
            int previousRound)
        {
            if (previousPhase == _session.Phase &&
                previousRound == _session.RoundNumber)
            {
                return;
            }

            if (_session.Phase == RedLightGreenLightSoloPhase.Countdown)
            {
                ResetRoundPresentation();
                Debug.Log(
                    "[Minigame Solo Test] Red Light / Green Light round " +
                    _session.RoundNumber + " ready.");
            }
            else if (_session.Phase ==
                     RedLightGreenLightSoloPhase.RoundResult)
            {
                var standing = _session
                    .GetRoundResult(_session.RoundNumber)
                    .GetStandingForSlot(
                        RedLightGreenLightSoloSession.LocalPlayerSlot);
                Debug.Log(
                    "[Minigame Solo Test] Red Light / Green Light round " +
                    _session.RoundNumber + " result: rank " +
                    standing.Rank + ", " +
                    standing.ForwardProgressMeters.ToString("0.0") +
                    "m.");
            }
            else if (_session.Phase ==
                     RedLightGreenLightSoloPhase.Complete)
            {
                Debug.Log(
                    "[Minigame Solo Test] Red Light / Green Light complete.");
            }
        }

        private void ResetRoundPresentation()
        {
            if (_runnerRoot != null)
            {
                _runnerRoot.SetPositionAndRotation(
                    new Vector3(
                        NetworkRedLightGreenLightState.ArenaCenterX,
                        0f,
                        RunnerStartZ),
                    Quaternion.identity);
            }
            _feedback = string.Empty;
            _feedbackStyle = MinigameSoloFeedbackStyle.Neutral;
            _feedbackUntil = float.NegativeInfinity;
            _lastSignal =
                (RedLightGreenLightSignalPhase)byte.MaxValue;
            ApplyPlayerPresentation();
            RefreshSignalPresentation();
        }

        private void ApplyPlayerPresentation()
        {
            if (_presentation == null || _session?.LocalPlayer == null)
            {
                return;
            }

            var player = _session.LocalPlayer;
            _presentation.ApplyState(
                player.ViolationCount,
                player.State ==
                    RedLightGreenLightPlayerState.Eliminated);
        }

        private void RefreshSignalPresentation()
        {
            if (_session == null)
            {
                return;
            }

            var phase = _session.Phase ==
                        RedLightGreenLightSoloPhase.Running
                ? _session.SignalPhase
                : RedLightGreenLightSignalPhase.Green;

            if (_observerHead != null)
            {
                var yaw = 180f;
                if (_session.Phase ==
                    RedLightGreenLightSoloPhase.Running)
                {
                    if (phase ==
                        RedLightGreenLightSignalPhase.TurnWarning)
                    {
                        var progress = 1f - Mathf.Clamp01(
                            _session.SignalRemainingSeconds /
                            (float)RedLightGreenLightRules
                                .TurnWarningSeconds);
                        yaw = Mathf.LerpAngle(180f, 0f, progress);
                    }
                    else if (phase ==
                             RedLightGreenLightSignalPhase.Red)
                    {
                        yaw = 0f;
                    }
                }
                _observerHead.localRotation =
                    Quaternion.Euler(0f, yaw, 0f);
            }

            var greenColor = phase ==
                RedLightGreenLightSignalPhase.Green
                    ? new Color(0.12f, 1f, 0.25f)
                    : phase ==
                      RedLightGreenLightSignalPhase.TurnWarning
                        ? new Color(1f, 0.7f, 0.08f)
                        : new Color(0.04f, 0.1f, 0.05f);
            var redColor = phase ==
                RedLightGreenLightSignalPhase.Red
                    ? new Color(1f, 0.08f, 0.04f)
                    : phase ==
                      RedLightGreenLightSignalPhase.TurnWarning
                        ? new Color(1f, 0.7f, 0.08f)
                        : new Color(0.12f, 0.025f, 0.02f);
            SetRendererColor(_greenSignalRenderer, greenColor);
            SetRendererColor(_redSignalRenderer, redColor);

            if (_greenSignalLight != null)
            {
                _greenSignalLight.enabled =
                    phase != RedLightGreenLightSignalPhase.Red;
                _greenSignalLight.color = greenColor;
                _greenSignalLight.intensity =
                    phase == RedLightGreenLightSignalPhase.Green
                        ? 4f
                        : 2f;
            }
            if (_redSignalLight != null)
            {
                _redSignalLight.enabled =
                    phase != RedLightGreenLightSignalPhase.Green;
                _redSignalLight.color = redColor;
                _redSignalLight.intensity =
                    phase == RedLightGreenLightSignalPhase.Red
                        ? 4f
                        : 2f;
            }

            if (_lastSignal != phase)
            {
                _lastSignal = phase;
                Debug.Log(
                    "[Minigame Solo Test] Signal: " +
                    phase.ToString().ToUpperInvariant());
            }
        }

        private void SetRendererColor(Renderer target, Color color)
        {
            if (target == null)
            {
                return;
            }

            target.GetPropertyBlock(_signalProperties);
            _signalProperties.SetColor("_BaseColor", color);
            _signalProperties.SetColor("_Color", color);
            target.SetPropertyBlock(_signalProperties);
            _signalProperties.Clear();
        }

        private void ApplyMovementFeedback(
            RedLightGreenLightMovementIntentResolution resolution)
        {
            if (resolution.BecameWarned)
            {
                _feedback = "WARNING · WALK SPEED";
                _feedbackStyle = MinigameSoloFeedbackStyle.Warning;
                _feedbackUntil = Time.unscaledTime + 1.5f;
            }
            else if (resolution.BecameEliminated)
            {
                _feedback = "ELIMINATED · MOVED ON RED";
                _feedbackStyle = MinigameSoloFeedbackStyle.Error;
                _feedbackUntil = Time.unscaledTime + 2f;
            }
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
                StartNewSeed(unchecked(_session.Seed + 1));
                return true;
            }

            return false;
        }

        private static Vector2 ReadMovementInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }

            var x = (keyboard.dKey.isPressed ? 1f : 0f) -
                    (keyboard.aKey.isPressed ? 1f : 0f);
            var y = (keyboard.wKey.isPressed ? 1f : 0f) -
                    (keyboard.sKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
        }

        private float GetProgressMeters()
        {
            return _runnerRoot == null
                ? 0f
                : Mathf.Max(0f, _runnerRoot.position.z - RunnerStartZ);
        }

        private string GetPhaseLabel()
        {
            switch (_session.Phase)
            {
                case RedLightGreenLightSoloPhase.Countdown:
                    return "START IN";
                case RedLightGreenLightSoloPhase.Running:
                    return "RUNNING";
                case RedLightGreenLightSoloPhase.RoundResult:
                    return "ROUND RESULT";
                case RedLightGreenLightSoloPhase.Complete:
                    return "COMPLETE";
                default:
                    return _session.Phase.ToString().ToUpperInvariant();
            }
        }

        private string GetSignalLabel()
        {
            if (_session.Phase == RedLightGreenLightSoloPhase.RoundResult)
            {
                var result = _session.GetRoundResult(_session.RoundNumber);
                var standing = result.GetStandingForSlot(
                    RedLightGreenLightSoloSession.LocalPlayerSlot);
                return "RANK " + standing.Rank +
                       "  ·  " + standing.Points + " PT";
            }
            if (_session.Phase == RedLightGreenLightSoloPhase.Complete)
            {
                var leaderboard = _session.Leaderboard;
                for (var index = 0;
                     leaderboard != null && index < leaderboard.Count;
                     index++)
                {
                    if (leaderboard[index].PlayerSlot ==
                        RedLightGreenLightSoloSession.LocalPlayerSlot)
                    {
                        return "MATCH RANK " +
                               leaderboard[index].Rank +
                               "  ·  " +
                               leaderboard[index].TotalPoints + " PT";
                    }
                }
                return "MATCH COMPLETE";
            }
            if (_session.Phase == RedLightGreenLightSoloPhase.Countdown)
            {
                return "GET READY";
            }

            switch (_session.SignalPhase)
            {
                case RedLightGreenLightSignalPhase.Green:
                    return "GREEN LIGHT · GO";
                case RedLightGreenLightSignalPhase.TurnWarning:
                    return "TURNING · STOP";
                case RedLightGreenLightSignalPhase.Red:
                    return "RED LIGHT · FREEZE";
                default:
                    return _session.SignalPhase.ToString().ToUpperInvariant();
            }
        }

        private static string GetPlayerStateLabel(
            RedLightGreenLightPlayerRoundState player)
        {
            if (player == null)
            {
                return "READY";
            }

            switch (player.State)
            {
                case RedLightGreenLightPlayerState.Healthy:
                    return "HEALTHY · RUN " +
                           player.MovementSpeedMetersPerSecond
                               .ToString("0.00") + "m/s";
                case RedLightGreenLightPlayerState.Warned:
                    return "WARNED · WALK " +
                           player.MovementSpeedMetersPerSecond
                               .ToString("0.00") + "m/s";
                case RedLightGreenLightPlayerState.Eliminated:
                    return "ELIMINATED";
                case RedLightGreenLightPlayerState.Finished:
                    return "FINISHED";
                default:
                    return player.State.ToString().ToUpperInvariant();
            }
        }

        private void StartNextSeed()
        {
            if (_session != null)
            {
                StartNewSeed(unchecked(_session.Seed + 1));
            }
        }

        private void UpdateHud()
        {
            if (_hud == null || _session == null)
            {
                return;
            }

            var feature = GetSignalLabel();
            if (_session.Phase == RedLightGreenLightSoloPhase.Running)
            {
                feature += "  ·  " +
                           _session.SignalRemainingSeconds.ToString("0.00") +
                           "s";
            }

            var feedbackVisible = Time.unscaledTime < _feedbackUntil;
            _hud.SetContent(
                "DEVELOPER SOLO TEST  /  RED LIGHT · GREEN LIGHT",
                "ROUND " + _session.RoundNumber + " / " +
                RedLightGreenLightRules.RoundCount + "  ·  " +
                GetPhaseLabel() + "  ·  " +
                FormatClock(_session.RemainingSeconds),
                "STATE " + GetPlayerStateLabel(_session.LocalPlayer) +
                "  ·  PROGRESS " +
                GetProgressMeters().ToString("0.0") + " / " +
                (FinishWorldZ - RunnerStartZ).ToString("0.0") + "m" +
                "  ·  SEED " + _session.Seed,
                feature,
                "WASD move  |  RED input: first warning slows, " +
                "next RED eliminates",
                feedbackVisible ? _feedback : string.Empty,
                feedbackVisible
                    ? _feedbackStyle
                    : MinigameSoloFeedbackStyle.Neutral);
        }

        private static void SetNamedObjectActive(
            string objectName,
            bool active)
        {
            var target = FindNamedTransform(objectName);
            if (target != null)
            {
                target.gameObject.SetActive(active);
            }
        }

        private static Transform FindNamedTransform(string objectName)
        {
            var transforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (transforms[index] != null &&
                    transforms[index].name == objectName)
                {
                    return transforms[index];
                }
            }
            return null;
        }

        private static string FormatClock(float seconds)
        {
            var safeSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (safeSeconds / 60).ToString("00") + ":" +
                   (safeSeconds % 60).ToString("00");
        }

        private static void StopSoloTest()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

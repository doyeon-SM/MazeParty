using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.WrongWay;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    /// <summary>
    /// Runtime-only WrongWay harness injected by the editor launcher. It builds
    /// a readable 50-step practice staircase and drives the production pure
    /// rules without creating an NGO session.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class WrongWaySoloTestController : MonoBehaviour
    {
        private const float StepRise = 0.24f;
        private const float StepDepth = 0.78f;
        private const float StepWidth = 5.4f;
        private const float RunnerGroundOffset = 0.78f;
        private const float ProgressMoveSpeed = 16f;

        private static readonly Color StepColorA =
            new Color(0.12f, 0.34f, 0.68f, 1f);
        private static readonly Color StepColorB =
            new Color(0.16f, 0.46f, 0.82f, 1f);
        private static readonly Color AccentColor =
            new Color(1f, 0.76f, 0.16f, 1f);

        private WrongWaySoloSession _session;
        private Transform _runtimeRoot;
        private Transform _runnerRoot;
        private PlayerAvatarVisual _avatarVisual;
        private Camera _runtimeCamera;
        private float _displayedProgress;
        private string _feedbackMessage = string.Empty;
        private MinigameSoloFeedbackStyle _feedbackStyle =
            MinigameSoloFeedbackStyle.Neutral;
        private float _feedbackUntil = float.NegativeInfinity;
        private MinigameSoloHudView _hud;
        private bool _initialized;
        private bool _snapCamera;

        public bool IsInitialized => _initialized;
        public WrongWaySoloSession Session => _session;
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
                    "The WrongWay solo harness is already initialized.");
            }
            if (_hud == null || !_hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "The WrongWay solo harness requires a valid " +
                    "MinigameSoloHud prefab instance.");
            }

            _hud.BindActions(RestartRound, StartNextSeed, StopSoloTest);

            DisableProductionPresentation();
            CreateRuntimeRoot();
            CreateEnvironment();
            CreateRunner();
            CreateRuntimeCamera();

            _session = new WrongWaySoloSession();
            _session.Begin(seed);
            ResetRoundPresentation();
            _initialized = true;
            UpdateHud();

            Debug.Log(
                "[Minigame Solo Test] WrongWay started with seed " +
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

            if (_session.Phase == WrongWaySoloPhase.Running &&
                TryReadDirection(out var direction) &&
                _session.TrySubmitDirection(
                    direction,
                    out var resolution))
            {
                ApplyInputFeedback(resolution);
                if (resolution.FinishedRound)
                {
                    LogFinishedRound();
                }
            }

            var targetProgress = _session.CompletedSteps;
            _displayedProgress = Mathf.MoveTowards(
                _displayedProgress,
                targetProgress,
                ProgressMoveSpeed * Time.unscaledDeltaTime);
            if (_runnerRoot != null)
            {
                _runnerRoot.position =
                    GetRunnerPosition(_displayedProgress);
            }

            if (_avatarVisual != null)
            {
                _avatarVisual.SetEliminated(
                    _session.IsInputLocked);
            }
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

            var focus = _runnerRoot.position +
                        new Vector3(0f, 0.55f, 2.6f);
            var desiredPosition = _runnerRoot.position +
                                  new Vector3(9.5f, 7.2f, -11.5f);
            var desiredRotation = Quaternion.LookRotation(
                focus - desiredPosition,
                Vector3.up);

            if (_snapCamera)
            {
                _runtimeCamera.transform.SetPositionAndRotation(
                    desiredPosition,
                    desiredRotation);
                _snapCamera = false;
                return;
            }

            var blend = 1f -
                        Mathf.Exp(-6f * Time.unscaledDeltaTime);
            _runtimeCamera.transform.position = Vector3.Lerp(
                _runtimeCamera.transform.position,
                desiredPosition,
                blend);
            _runtimeCamera.transform.rotation = Quaternion.Slerp(
                _runtimeCamera.transform.rotation,
                desiredRotation,
                blend);
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
            Debug.Log(
                "[Minigame Solo Test] WrongWay round " +
                _session.RoundNumber + " restarted.");
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
            Debug.Log(
                "[Minigame Solo Test] WrongWay started with seed " +
                seed + ".");
        }

        private void LogFinishedRound()
        {
            Debug.Log(
                "[Minigame Solo Test] WrongWay round " +
                _session.RoundNumber + " finished all 50 steps.");
        }

        private void CreateRuntimeRoot()
        {
            var runtimeObject =
                new GameObject("[Solo Test] WrongWay Runtime");
            runtimeObject.transform.SetParent(transform, false);
            _runtimeRoot = runtimeObject.transform;
        }

        private void CreateEnvironment()
        {
            var lightObject = new GameObject(
                "Solo Directional Light",
                typeof(Light));
            lightObject.transform.SetParent(_runtimeRoot, false);
            lightObject.transform.rotation =
                Quaternion.Euler(42f, -32f, 0f);
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.94f, 0.84f, 1f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;

            CreateBlock(
                "Start Platform",
                new Vector3(0f, -0.18f, -2.2f),
                new Vector3(8f, 0.35f, 5.2f),
                new Color(0.08f, 0.13f, 0.22f, 1f));

            for (var stepIndex = 1;
                 stepIndex <= WrongWayRules.StepCount;
                 stepIndex++)
            {
                var height = stepIndex * StepRise;
                var colorBand = (stepIndex / 5) % 2;
                var color = colorBand == 0
                    ? StepColorA
                    : StepColorB;
                if (stepIndex % 10 == 0)
                {
                    color = AccentColor;
                }

                CreateBlock(
                    "Step " + stepIndex.ToString("00"),
                    new Vector3(
                        0f,
                        height * 0.5f,
                        stepIndex * StepDepth),
                    new Vector3(
                        StepWidth,
                        height,
                        StepDepth + 0.025f),
                    color);
            }

            var finishZ =
                WrongWayRules.StepCount * StepDepth + 0.15f;
            var finishHeight =
                WrongWayRules.StepCount * StepRise;
            CreateBlock(
                "Finish Left Post",
                new Vector3(-3.15f, finishHeight + 1.5f, finishZ),
                new Vector3(0.32f, 3f, 0.32f),
                AccentColor);
            CreateBlock(
                "Finish Right Post",
                new Vector3(3.15f, finishHeight + 1.5f, finishZ),
                new Vector3(0.32f, 3f, 0.32f),
                AccentColor);
            CreateBlock(
                "Finish Banner",
                new Vector3(0f, finishHeight + 3f, finishZ),
                new Vector3(6.6f, 0.42f, 0.38f),
                new Color(1f, 0.28f, 0.18f, 1f));
        }

        private void CreateRunner()
        {
            var runnerObject = new GameObject("Solo Stair Runner");
            runnerObject.transform.SetParent(_runtimeRoot, false);
            _runnerRoot = runnerObject.transform;
            _runnerRoot.position = GetRunnerPosition(0f);

            _avatarVisual =
                runnerObject.AddComponent<PlayerAvatarVisual>();
            _avatarVisual.EnsureBuilt();
            _avatarVisual.SetBodyColor(
                new Color(0.18f, 0.78f, 1f, 1f));
            _avatarVisual.SetDisplayName("SOLO DEV");
            _avatarVisual.SetTopViewHighlight(true);

            var colliders =
                runnerObject.GetComponentsInChildren<Collider>(true);
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
            _runtimeCamera.fieldOfView = 48f;
            _runtimeCamera.nearClipPlane = 0.1f;
            _runtimeCamera.farClipPlane = 180f;
            _runtimeCamera.clearFlags =
                CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.025f, 0.04f, 0.09f, 1f);
            _snapCamera = true;
        }

        private void DisableProductionPresentation()
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include);
            for (var index = 0; index < behaviours.Length; index++)
            {
                var behaviour = behaviours[index];
                if (behaviour == null)
                {
                    continue;
                }

                var typeName = behaviour.GetType().Name;
                if (typeName == "NetworkWrongWayState" ||
                    typeName == "WrongWayNetworkView")
                {
                    behaviour.enabled = false;
                }
            }

            var transforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Include);
            for (var index = 0; index < transforms.Length; index++)
            {
                var candidate = transforms[index];
                if (candidate == null ||
                    candidate == transform ||
                    candidate.IsChildOf(transform))
                {
                    continue;
                }

                if (candidate.name == "Runtime Runners" ||
                    candidate.name == "WrongWay HUD" ||
                    candidate.name == "Arena Presentation")
                {
                    candidate.gameObject.SetActive(false);
                }
            }
        }

        private void ApplySessionTransition(
            WrongWaySoloPhase previousPhase,
            int previousRound)
        {
            if (previousPhase == _session.Phase &&
                previousRound == _session.RoundNumber)
            {
                return;
            }

            if (_session.Phase == WrongWaySoloPhase.Countdown)
            {
                ResetRoundPresentation();
                Debug.Log(
                    "[Minigame Solo Test] WrongWay round " +
                    _session.RoundNumber + " ready.");
            }
            else if (_session.Phase == WrongWaySoloPhase.RoundResult)
            {
                var standing = _session
                    .GetRoundResult(_session.RoundNumber)
                    .GetStandingForSlot(
                        WrongWaySoloSession.LocalPlayerSlot);
                Debug.Log(
                    "[Minigame Solo Test] WrongWay round " +
                    _session.RoundNumber + " result: rank " +
                    standing.Rank + ", " +
                    standing.CompletedSteps + " steps.");
            }
            else if (_session.Phase == WrongWaySoloPhase.Complete)
            {
                Debug.Log(
                    "[Minigame Solo Test] WrongWay complete.");
            }
        }

        private void ResetRoundPresentation()
        {
            _displayedProgress = 0f;
            if (_runnerRoot != null)
            {
                _runnerRoot.position = GetRunnerPosition(0f);
            }
            if (_avatarVisual != null)
            {
                _avatarVisual.SetEliminated(false);
            }

            _feedbackMessage = string.Empty;
            _feedbackStyle = MinigameSoloFeedbackStyle.Neutral;
            _feedbackUntil = float.NegativeInfinity;
            _snapCamera = true;
        }

        private void ApplyInputFeedback(
            WrongWayInputResolution resolution)
        {
            switch (resolution.Status)
            {
                case WrongWayInputStatus.Incorrect:
                    _feedbackMessage = "WRONG · GET UP!";
                    _feedbackStyle = MinigameSoloFeedbackStyle.Error;
                    _feedbackUntil = Time.unscaledTime +
                                     (float)WrongWayRules
                                         .IncorrectInputLockSeconds;
                    break;
                case WrongWayInputStatus.Correct:
                case WrongWayInputStatus.Finished:
                    _feedbackMessage = resolution.FinishedRound
                        ? "50 / 50 · FINISH!"
                        : "CORRECT · +1 STEP";
                    _feedbackStyle = MinigameSoloFeedbackStyle.Success;
                    _feedbackUntil = Time.unscaledTime + 0.28f;
                    break;
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

        private static bool TryReadDirection(
            out WrongWayDirection direction)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.wasPressedThisFrame)
                {
                    direction = WrongWayDirection.Up;
                    return true;
                }
                if (keyboard.sKey.wasPressedThisFrame)
                {
                    direction = WrongWayDirection.Down;
                    return true;
                }
                if (keyboard.aKey.wasPressedThisFrame)
                {
                    direction = WrongWayDirection.Left;
                    return true;
                }
                if (keyboard.dKey.wasPressedThisFrame)
                {
                    direction = WrongWayDirection.Right;
                    return true;
                }
            }

            direction = default;
            return false;
        }

        private string GetPhaseLabel()
        {
            switch (_session.Phase)
            {
                case WrongWaySoloPhase.Countdown:
                    return "START IN";
                case WrongWaySoloPhase.Running:
                    return "RUNNING";
                case WrongWaySoloPhase.RoundResult:
                    return "ROUND RESULT";
                case WrongWaySoloPhase.Complete:
                    return "COMPLETE";
                default:
                    return _session.Phase.ToString().ToUpperInvariant();
            }
        }

        private string GetPhaseMessage()
        {
            if (_session.Phase == WrongWaySoloPhase.RoundResult)
            {
                var result =
                    _session.GetRoundResult(_session.RoundNumber);
                var standing = result.GetStandingForSlot(
                    WrongWaySoloSession.LocalPlayerSlot);
                return "RANK " + standing.Rank +
                       "  /  " + standing.CompletedSteps +
                       " STEPS";
            }

            if (_session.Phase == WrongWaySoloPhase.Complete)
            {
                var leaderboard = _session.Leaderboard;
                for (var index = 0;
                     leaderboard != null &&
                     index < leaderboard.Count;
                     index++)
                {
                    if (leaderboard[index].PlayerSlot ==
                        WrongWaySoloSession.LocalPlayerSlot)
                    {
                        return "MATCH RANK " +
                               leaderboard[index].Rank +
                               "  /  " +
                               leaderboard[index]
                                   .TotalCompletedSteps +
                               " TOTAL STEPS";
                    }
                }

                return "MATCH COMPLETE";
            }

            return "GET READY";
        }

        private static string GetPromptLabel(
            WrongWayDirection direction)
        {
            switch (direction)
            {
                case WrongWayDirection.Up:
                    return "[ W ]   UP";
                case WrongWayDirection.Down:
                    return "[ S ]   DOWN";
                case WrongWayDirection.Left:
                    return "[ A ]   LEFT";
                case WrongWayDirection.Right:
                    return "[ D ]   RIGHT";
                default:
                    return direction.ToString().ToUpperInvariant();
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

            var feature =
                _session.Phase == WrongWaySoloPhase.Running &&
                _session.CurrentPrompt.HasValue
                    ? GetPromptLabel(_session.CurrentPrompt.Value)
                    : GetPhaseMessage();
            string feedback;
            MinigameSoloFeedbackStyle feedbackStyle;
            if (_session.IsInputLocked)
            {
                feedback = "FALLEN · INPUT LOCK " +
                           _session.InputLockSecondsRemaining.ToString("0.00") +
                           "s";
                feedbackStyle = MinigameSoloFeedbackStyle.Error;
            }
            else if (Time.unscaledTime < _feedbackUntil)
            {
                feedback = _feedbackMessage;
                feedbackStyle = _feedbackStyle;
            }
            else
            {
                feedback = string.Empty;
                feedbackStyle = MinigameSoloFeedbackStyle.Neutral;
            }

            _hud.SetContent(
                "DEVELOPER SOLO TEST  /  WRONGWAY",
                "ROUND " + _session.RoundNumber + " / " +
                WrongWayRules.RoundCount + "  ·  " +
                GetPhaseLabel() + "  ·  " +
                FormatClock(_session.RemainingSeconds),
                "STAIR " + _session.CompletedSteps + " / " +
                WrongWayRules.StepCount + "  ·  SEED " +
                _session.Seed,
                feature,
                "W / A / S / D  ·  match the shown direction",
                feedback,
                feedbackStyle);
        }

        private void CreateBlock(
            string objectName,
            Vector3 position,
            Vector3 scale,
            Color color)
        {
            var block = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            block.name = objectName;
            block.transform.SetParent(_runtimeRoot, false);
            block.transform.position = position;
            block.transform.localScale = scale;

            var collider = block.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            ApplySurfaceColor(block.GetComponent<Renderer>(), color);
        }

        private static void ApplySurfaceColor(
            Renderer renderer,
            Color color)
        {
            if (renderer == null)
            {
                return;
            }

            WorldTextOcclusion.ApplyBuildSafeSurface(renderer);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            properties.SetColor(
                "_EmissionColor",
                color * (color == AccentColor ? 0.3f : 0.03f));
            renderer.SetPropertyBlock(properties);
        }

        private static Vector3 GetRunnerPosition(
            float completedSteps)
        {
            var progress = Mathf.Clamp(
                completedSteps,
                0f,
                WrongWayRules.StepCount);
            return new Vector3(
                0f,
                progress * StepRise + RunnerGroundOffset,
                progress * StepDepth);
        }

        private static string FormatClock(float seconds)
        {
            var safeSeconds = Mathf.Max(
                0,
                Mathf.CeilToInt(seconds));
            return (safeSeconds / 60).ToString("00") + ":" +
                   (safeSeconds % 60).ToString("00");
        }

        private void StopSoloTest()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

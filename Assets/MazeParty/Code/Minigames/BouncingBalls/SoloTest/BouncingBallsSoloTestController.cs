using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.BouncingBalls;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class BouncingBallsSoloTestController : MonoBehaviour
    {
        public const int LocalPlayerSlot = 0;

        private static readonly Color[] PlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        private static readonly Color NeutralBallColor =
            new Color(0.91f, 0.96f, 1f);
        private static readonly int BaseColorProperty =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty =
            Shader.PropertyToID("_Color");

        private BouncingBallsMatchState _match;
        private MinigameSoloHudView _developerHud;
        private NetworkBouncingBallsState _productionState;
        private BouncingBallsNetworkView _productionView;
        private BouncingBallsHudBindings _productionHud;
        private Transform _arenaOrigin;
        private MaterialPropertyBlock _colorBlock;
        private Camera _runtimeCamera;
        private NetworkBouncingBallsPhase _phase;
        private double _phaseElapsed;
        private int _seed;
        private bool _initialized;

        public bool IsInitialized => _initialized;
        public int Seed => _seed;
        public BouncingBallsMatchState Match => _match;
        public NetworkBouncingBallsPhase Phase => _phase;
        public Camera RuntimeCamera => _runtimeCamera;

        public void ConfigureHud(MinigameSoloHudView hud)
        {
            _developerHud = hud;
        }

        public void Begin(int seed)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "Bouncing Balls solo is already initialized.");
            }

            if (_developerHud == null ||
                !_developerHud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Bouncing Balls solo requires the authored developer " +
                    "HUD prefab instance.");
            }

            _developerHud.BindActions(
                RestartMatch,
                StartNextSeed,
                StopSoloTest);
            ResolveAndDisableProduction();
            CreateRuntimeCamera();
            _colorBlock = new MaterialPropertyBlock();
            _initialized = true;
            BeginMatch(seed);
            Debug.Log(
                "[Minigame Solo Test] Bouncing Balls started: P1 uses " +
                "held A/D, with three practice shield players.");
        }

        private void Update()
        {
            if (!_initialized || _match == null)
            {
                return;
            }

            if (HandleKeyboardShortcuts())
            {
                return;
            }

            Tick(Math.Max(0d, Time.unscaledDeltaTime));
            RefreshPresentation();
        }

        private void OnDestroy()
        {
            if (_runtimeCamera != null)
            {
                Destroy(_runtimeCamera.gameObject);
            }
        }

        private void ResolveAndDisableProduction()
        {
            _productionState = FindAnyObjectByType<
                NetworkBouncingBallsState>(FindObjectsInactive.Include);
            _productionView = FindAnyObjectByType<
                BouncingBallsNetworkView>(FindObjectsInactive.Include);
            if (_productionState == null || _productionView == null)
            {
                throw new InvalidOperationException(
                    "Bouncing Balls production state and view were not " +
                    "found in the scene.");
            }

            _productionState.enabled = false;
            _productionView.enabled = false;
            var arena = _productionView.ArenaPresentation;
            _productionHud = _productionView.HudBindings;
            if (arena == null || _productionHud == null ||
                !_productionHud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Bouncing Balls scene presentation contract is " +
                    "incomplete.");
            }

            for (var slot = 0;
                 slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                if (_productionView.GetShieldTransform(slot) == null ||
                    _productionView.GetShieldRenderer(slot) == null)
                {
                    throw new InvalidOperationException(
                        "Bouncing Balls scene is missing shield " +
                        (slot + 1) + ".");
                }
            }

            for (var id = 0; id < BouncingBallsRules.BallCount; id++)
            {
                if (_productionView.GetBallTransform(id) == null ||
                    _productionView.GetBallRenderer(id) == null)
                {
                    throw new InvalidOperationException(
                        "Bouncing Balls scene is missing ball " +
                        (id + 1) + ".");
                }
            }

            _arenaOrigin = arena.transform;
            arena.SetActive(true);
            _productionHud.RootCanvas.gameObject.SetActive(true);
        }

        private void CreateRuntimeCamera()
        {
            var cameraObject = new GameObject(
                "[Developer] Bouncing Balls Solo Camera");
            cameraObject.transform.SetParent(transform, false);
            _runtimeCamera = cameraObject.AddComponent<Camera>();
            _runtimeCamera.orthographic = true;
            _runtimeCamera.orthographicSize =
                BouncingBallsNetworkView.SharedCameraOrthographicSize;
            _runtimeCamera.nearClipPlane = 0.1f;
            _runtimeCamera.farClipPlane = 100f;
            _runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.018f, 0.024f, 0.036f);
            cameraObject.transform.SetPositionAndRotation(
                BouncingBallsNetworkView.SharedCameraPosition,
                BouncingBallsNetworkView.SharedCameraRotation);
            cameraObject.AddComponent<AudioListener>();
        }

        private void BeginMatch(int seed)
        {
            _seed = seed;
            _match = new BouncingBallsMatchState(seed);
            _phase = NetworkBouncingBallsPhase.Countdown;
            _phaseElapsed = 0d;
            RefreshPresentation();
        }

        private void Tick(double deltaSeconds)
        {
            switch (_phase)
            {
                case NetworkBouncingBallsPhase.Countdown:
                    _phaseElapsed += deltaSeconds;
                    if (_phaseElapsed >=
                        BouncingBallsRules.CountdownSeconds)
                    {
                        _phase = NetworkBouncingBallsPhase.Playing;
                        _phaseElapsed = 0d;
                    }
                    break;
                case NetworkBouncingBallsPhase.Playing:
                    UpdateShieldInputs();
                    _match.AdvanceTo(Math.Min(
                        BouncingBallsRules.RoundSeconds,
                        _match.RoundElapsedSeconds + deltaSeconds));
                    if (_match.IsRoundComplete)
                    {
                        StopAllShields();
                        _phase = _match.IsComplete
                            ? NetworkBouncingBallsPhase.Complete
                            : NetworkBouncingBallsPhase.RoundBreak;
                        _phaseElapsed = 0d;
                    }
                    break;
                case NetworkBouncingBallsPhase.RoundBreak:
                    _phaseElapsed += deltaSeconds;
                    if (_phaseElapsed >=
                        NetworkBouncingBallsState.RoundBreakSeconds)
                    {
                        _match.BeginNextRound();
                        _phase = NetworkBouncingBallsPhase.Countdown;
                        _phaseElapsed = 0d;
                    }
                    break;
                case NetworkBouncingBallsPhase.Complete:
                    _phaseElapsed = Math.Min(
                        BouncingBallsRules.ResultSeconds,
                        _phaseElapsed + deltaSeconds);
                    break;
            }
        }

        private void UpdateShieldInputs()
        {
            _match.SetShieldInput(LocalPlayerSlot,
                ReadLocalDirection());
            for (var slot = 1;
                 slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                _match.SetShieldInput(slot,
                    ChoosePracticeDirection(slot));
            }
        }

        private int ChoosePracticeDirection(int slot)
        {
            var target = 0d;
            var bestArrival = double.PositiveInfinity;
            var shieldFace =
                BouncingBallsRules.ShieldRailDistance -
                BouncingBallsRules.BallRadius;
            for (var id = 0; id < BouncingBallsRules.BallCount; id++)
            {
                var ball = _match.GetBall(id);
                var radial = GetRadial(ball.X, ball.Y, slot);
                var outwardVelocity = GetRadial(
                    ball.VelocityX,
                    ball.VelocityY,
                    slot);
                if (outwardVelocity <= 0.01d)
                {
                    continue;
                }

                var arrival = Math.Max(
                    0d,
                    (shieldFace - radial) / outwardVelocity);
                var tangent = GetTangent(ball.X, ball.Y, slot) +
                    GetTangent(
                        ball.VelocityX,
                        ball.VelocityY,
                        slot) * arrival;
                if (arrival >= bestArrival ||
                    Math.Abs(tangent) >
                        BouncingBallsRules.GoalHalfWidth + 0.5d)
                {
                    continue;
                }

                bestArrival = arrival;
                target = Math.Max(
                    -BouncingBallsRules.ShieldMaximumOffset,
                    Math.Min(
                        BouncingBallsRules.ShieldMaximumOffset,
                        tangent));
            }

            var difference = target - _match.GetShield(slot).Center;
            return difference < -0.08d
                ? -1
                : difference > 0.08d
                    ? 1
                    : 0;
        }

        private void StopAllShields()
        {
            for (var slot = 0;
                 slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                _match.SetShieldInput(slot, 0);
            }
        }

        private void RefreshPresentation()
        {
            RefreshArena();
            RefreshProductionHud();
            RefreshDeveloperHud();
        }

        private void RefreshArena()
        {
            if (_arenaOrigin == null)
            {
                return;
            }

            var rail = (float)BouncingBallsRules.ShieldRailDistance;
            for (var slot = 0;
                 slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                var center = (float)_match.GetShield(slot).Center;
                Vector3 localPosition;
                switch (slot)
                {
                    case 0:
                        localPosition =
                            new Vector3(center, -rail, 0.38f);
                        break;
                    case 1:
                        localPosition =
                            new Vector3(rail, center, 0.38f);
                        break;
                    case 2:
                        localPosition =
                            new Vector3(center, rail, 0.38f);
                        break;
                    default:
                        localPosition =
                            new Vector3(-rail, center, 0.38f);
                        break;
                }

                _productionView.GetShieldTransform(slot).position =
                    _arenaOrigin.TransformPoint(localPosition);
                SetRendererColor(
                    _productionView.GetShieldRenderer(slot),
                    PlayerColors[slot]);
            }

            for (var id = 0; id < BouncingBallsRules.BallCount; id++)
            {
                var ball = _match.GetBall(id);
                _productionView.GetBallTransform(id).position =
                    _arenaOrigin.TransformPoint(
                        new Vector3((float)ball.X,
                            (float)ball.Y, 0.12f));
                SetRendererColor(
                    _productionView.GetBallRenderer(id),
                    BouncingBallsRules.IsValidPlayerSlot(
                        ball.OwnerSlot)
                        ? PlayerColors[ball.OwnerSlot]
                        : NeutralBallColor);
            }
        }

        private void RefreshProductionHud()
        {
            if (_productionHud == null ||
                !_productionHud.HasRequiredReferences)
            {
                return;
            }

            for (var slot = 0;
                 slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                _productionHud.PlayerNameTexts[slot].text =
                    slot == LocalPlayerSlot
                        ? "SOLO DEV"
                        : "PRACTICE " + (slot + 1);
                _productionHud.PlayerNameTexts[slot].color =
                    PlayerColors[slot];
                var score = _match.GetScore(slot);
                _productionHud.PlayerScoreTexts[slot].text =
                    "SCORE " + score;
            }
        }

        private void RefreshDeveloperHud()
        {
            if (_developerHud == null)
            {
                return;
            }

            var localScore = _match.GetScore(LocalPlayerSlot);
            var localConceded = _match.GetConceded(LocalPlayerSlot);
            var localRank = _match.IsComplete
                ? _match.GetFinalRank(LocalPlayerSlot)
                : 0;
            var primary = localRank > 0
                ? "COMPLETE · " +
                  MinigameDisplayFormatter.ToOrdinal(localRank) +
                  " · GOLD +" +
                  MinigameRewardRules.GetFinalPlacementGold(localRank)
                : "ROUND " + _match.RoundNumber + " / " +
                  BouncingBallsRules.RoundCount + " · " +
                  _phase.ToString().ToUpperInvariant();
            var recentGoal = _match.LastGoal;
            var feedback = recentGoal.HasValue &&
                recentGoal.Value.RoundNumber == _match.RoundNumber
                    ? recentGoal.Value.AwardedPoint
                        ? "P" + (recentGoal.Value.ScorerSlot + 1) +
                          " SCORED IN P" +
                          (recentGoal.Value.DefenderSlot + 1) +
                          "'S GOAL"
                        : "NEUTRAL BALL ENTERED P" +
                          (recentGoal.Value.DefenderSlot + 1) +
                          "'S GOAL · NO POINT"
                    : "BLOCK A BALL TO CLAIM YOUR COLOR";
            _developerHud.SetContent(
                "BOUNCING BALLS · SOLO",
                primary,
                "P1 SCORE " + localScore +
                " · CONCEDED " + localConceded,
                "3 BALLS · " + Remaining.ToString("0.0") +
                "s · " + feedback,
                "HOLD A / D MOVE · R RESTART · N NEXT SEED · ESC STOP",
                localRank > 0
                    ? "FINAL PLACEMENT REWARD ONLY"
                    : InstructionLabel,
                localRank == 1
                    ? MinigameSoloFeedbackStyle.Success
                    : localRank > 1
                        ? MinigameSoloFeedbackStyle.Warning
                        : MinigameSoloFeedbackStyle.Neutral);
        }

        private void SetRendererColor(Renderer renderer, Color color)
        {
            _colorBlock.Clear();
            _colorBlock.SetColor(BaseColorProperty, color);
            _colorBlock.SetColor(ColorProperty, color);
            renderer.SetPropertyBlock(_colorBlock);
        }

        private double Remaining
        {
            get
            {
                switch (_phase)
                {
                    case NetworkBouncingBallsPhase.Playing:
                        return Math.Max(0d,
                            BouncingBallsRules.RoundSeconds -
                            _match.RoundElapsedSeconds);
                    default:
                        return Math.Max(0d,
                            PhaseDuration - _phaseElapsed);
                }
            }
        }

        private double PhaseDuration
        {
            get
            {
                switch (_phase)
                {
                    case NetworkBouncingBallsPhase.Countdown:
                        return BouncingBallsRules.CountdownSeconds;
                    case NetworkBouncingBallsPhase.Playing:
                        return BouncingBallsRules.RoundSeconds;
                    case NetworkBouncingBallsPhase.RoundBreak:
                        return NetworkBouncingBallsState.RoundBreakSeconds;
                    case NetworkBouncingBallsPhase.Complete:
                        return BouncingBallsRules.ResultSeconds;
                    default:
                        return 1d;
                }
            }
        }

        private string PhaseLabel
        {
            get
            {
                switch (_phase)
                {
                    case NetworkBouncingBallsPhase.Countdown:
                        return "BOUNCING BALLS · GET READY";
                    case NetworkBouncingBallsPhase.Playing:
                        return "BOUNCING BALLS · DEFEND AND SCORE";
                    case NetworkBouncingBallsPhase.RoundBreak:
                        return "BOUNCING BALLS · ROUND RESULT";
                    case NetworkBouncingBallsPhase.Complete:
                        return "BOUNCING BALLS · FINAL RESULT";
                    default:
                        return "BOUNCING BALLS";
                }
            }
        }

        private string InstructionLabel
        {
            get
            {
                switch (_phase)
                {
                    case NetworkBouncingBallsPhase.Countdown:
                        return "GET READY TO MOVE YOUR SHIELD";
                    case NetworkBouncingBallsPhase.Playing:
                        return "BLOCK A BALL, CLAIM ITS COLOR, SCORE IN A GOAL";
                    case NetworkBouncingBallsPhase.RoundBreak:
                        return "ROUND 2 STARTS SOON · SCORES CARRY OVER";
                    case NetworkBouncingBallsPhase.Complete:
                        return "FINAL STANDINGS · PLACEMENT AWARDS GOLD";
                    default:
                        return string.Empty;
                }
            }
        }

        private void RestartMatch()
        {
            BeginMatch(_seed);
        }

        private void StartNextSeed()
        {
            BeginMatch(unchecked(_seed + 1));
        }

        private static int ReadLocalDirection()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return 0;
            }

            var a = keyboard.aKey.isPressed;
            var d = keyboard.dKey.isPressed;
            return a == d ? 0 : a ? -1 : 1;
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

        private static double GetRadial(
            double x,
            double y,
            int slot)
        {
            switch (slot)
            {
                case 0: return -y;
                case 1: return x;
                case 2: return y;
                default: return -x;
            }
        }

        private static double GetTangent(
            double x,
            double y,
            int slot)
        {
            return slot == 0 || slot == 2 ? x : y;
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

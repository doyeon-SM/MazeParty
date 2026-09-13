using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.Race;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class RaceSoloTestController : MonoBehaviour
    {
        private static readonly Color32[] PlayerColors =
        {
            new Color32(45, 122, 242, 255),
            new Color32(235, 57, 48, 255),
            new Color32(46, 199, 82, 255),
            new Color32(177, 68, 232, 255)
        };

        private readonly Transform[] _players =
            new Transform[RaceRules.PlayerCount];
        private readonly PlayerAvatarVisual[] _visuals =
            new PlayerAvatarVisual[RaceRules.PlayerCount];

        private RaceSoloSession _session;
        private MinigameSoloHudView _hud;
        private NetworkRaceState _productionState;
        private RaceNetworkView _productionView;
        private RaceHudBindings _productionHud;
        private GameObject _arenaPresentation;
        private GameObject _productionPlayerRoot;
        private Transform _runtimeRoot;
        private Camera _runtimeCamera;
        private bool _initialized;

        public bool IsInitialized => _initialized;
        public RaceSoloSession Session => _session;
        public Camera RuntimeCamera => _runtimeCamera;

        public void ConfigureHud(MinigameSoloHudView hud)
        {
            _hud = hud;
        }

        public void Begin(int seed)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "Race solo is already initialized.");
            }
            if (_hud == null || !_hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Race solo requires the developer HUD prefab instance.");
            }

            _hud.BindActions(RestartMatch, StartNextSeed, StopSoloTest);
            ResolveAndDisableProduction();
            CreateRuntimePresentation();
            _session = new RaceSoloSession();
            _session.Begin(seed);
            _initialized = true;
            RefreshPresentation(true);
            Debug.Log(
                "[Minigame Solo Test] Race started: three rounds, " +
                "500 alternating A/D steps.");
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

            _session.Tick(Time.unscaledDeltaTime, ReadStepInput());
            RefreshPresentation(false);
        }

        private void OnDestroy()
        {
            if (_runtimeRoot != null)
            {
                Destroy(_runtimeRoot.gameObject);
            }
        }

        private void ResolveAndDisableProduction()
        {
            _productionState = FindAnyObjectByType<NetworkRaceState>(
                FindObjectsInactive.Include);
            _productionView = FindAnyObjectByType<RaceNetworkView>(
                FindObjectsInactive.Include);
            if (_productionState == null || _productionView == null)
            {
                throw new InvalidOperationException(
                    "Race production state and view were not found in the " +
                    "scene.");
            }

            _productionState.enabled = false;
            _productionView.enabled = false;
            _arenaPresentation = FindDescendant(
                _productionState.transform,
                "Arena Presentation")?.gameObject;
            _productionPlayerRoot = FindDescendant(
                _productionState.transform,
                "Runtime Players")?.gameObject;
            _productionHud = _productionState
                .GetComponentInChildren<RaceHudBindings>(true);
            if (_arenaPresentation == null ||
                _productionPlayerRoot == null ||
                _productionHud == null ||
                !_productionHud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Race scene presentation contract is incomplete.");
            }

            _arenaPresentation.SetActive(true);
            _productionPlayerRoot.SetActive(false);
            _productionHud.gameObject.SetActive(true);
        }

        private void CreateRuntimePresentation()
        {
            _runtimeRoot = new GameObject("[Developer] Race Runtime").transform;
            var playerRoot = new GameObject("Solo Players").transform;
            playerRoot.SetParent(_runtimeRoot, false);
            for (var slot = 0; slot < RaceRules.PlayerCount; slot++)
            {
                var player = new GameObject("Solo Player " + (slot + 1));
                player.transform.SetParent(playerRoot, false);
                var visual = player.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.SetBodyColor(PlayerColors[slot]);
                visual.SetDisplayName(
                    slot == RaceSoloSession.LocalPlayerSlot
                        ? "SOLO DEV"
                        : "PRACTICE " + (slot + 1));
                visual.SetOwnerFirstPerson(false);
                visual.SetEliminated(false);
                visual.SetTopViewHighlight(
                    slot == RaceSoloSession.LocalPlayerSlot);
                DisableGeneratedHitColliders(player);
                _players[slot] = player.transform;
                _visuals[slot] = visual;
            }

            var cameraObject = new GameObject("Race Solo Camera");
            cameraObject.transform.SetParent(_runtimeRoot, false);
            _runtimeCamera = cameraObject.AddComponent<Camera>();
            _runtimeCamera.orthographic = true;
            _runtimeCamera.orthographicSize =
                RaceNetworkView.SharedCameraOrthographicSize;
            _runtimeCamera.nearClipPlane = 0.1f;
            _runtimeCamera.farClipPlane = 100f;
            _runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.018f, 0.024f, 0.036f);
            cameraObject.transform.SetPositionAndRotation(
                RaceNetworkView.SharedCameraPosition,
                RaceNetworkView.SharedCameraRotation);
            cameraObject.AddComponent<AudioListener>();
        }

        private void RefreshPresentation(bool snap)
        {
            for (var slot = 0; slot < RaceRules.PlayerCount; slot++)
            {
                var target = new Vector3(
                    NetworkRaceState.GetLaneX(slot),
                    RaceNetworkView.PlayerPresentationHeight,
                    NetworkRaceState.ProgressToWorldZ(
                        _session.GetProgress(slot)));
                _players[slot].position = snap
                    ? target
                    : Vector3.Lerp(
                        _players[slot].position,
                        target,
                        1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
                _players[slot].rotation = Quaternion.identity;
            }

            _productionHud.TimerDial.SetTime(
                _session.Remaining,
                GetPhaseDuration(_session.Phase));
            RefreshDeveloperHud();
        }

        private void RefreshDeveloperHud()
        {
            var stateLabel = _session.Phase == RaceSoloPhase.Complete
                ? BuildFinalLabel()
                : "ROUND " + _session.RoundNumber + "/" +
                  RaceRules.RoundCount + "  ·  " +
                  _session.Phase.ToString().ToUpperInvariant();
            var progress =
                "P1 " + _session.GetProgress(0) + "/500  ·  P2 " +
                _session.GetProgress(1) + "/500  ·  P3 " +
                _session.GetProgress(2) + "/500  ·  P4 " +
                _session.GetProgress(3) + "/500";
            var scores =
                "SCORE  " + _session.GetTotalPoints(0) + " / " +
                _session.GetTotalPoints(1) + " / " +
                _session.GetTotalPoints(2) + " / " +
                _session.GetTotalPoints(3);
            _hud.SetContent(
                "RACE · SOLO",
                stateLabel,
                progress,
                scores,
                "ALTERNATE A / D · FIRST TO 500",
                "R RESTART  ·  N NEXT SEED  ·  ESC STOP",
                MinigameSoloFeedbackStyle.Neutral);
        }

        private string BuildFinalLabel()
        {
            if (_session.Leaderboard == null)
            {
                return "COMPLETE";
            }
            for (var index = 0; index < _session.Leaderboard.Count; index++)
            {
                var entry = _session.Leaderboard[index];
                if (entry.PlayerSlot == RaceSoloSession.LocalPlayerSlot)
                {
                    return "COMPLETE  ·  " +
                           MinigameDisplayFormatter.ToOrdinal(entry.Rank) +
                           "  ·  " + entry.TotalPoints + " PTS";
                }
            }
            return "COMPLETE";
        }

        private void RestartMatch()
        {
            _session.Begin(_session.Seed);
            RefreshPresentation(true);
        }

        private void StartNextSeed()
        {
            _session.Begin(unchecked(_session.Seed + 1));
            RefreshPresentation(true);
        }

        private static RaceStepInput ReadStepInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return RaceStepInput.None;
            }
            var left = keyboard.aKey.wasPressedThisFrame;
            var right = keyboard.dKey.wasPressedThisFrame;
            if (left == right)
            {
                return RaceStepInput.None;
            }
            return left ? RaceStepInput.Left : RaceStepInput.Right;
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

        private static double GetPhaseDuration(RaceSoloPhase phase)
        {
            switch (phase)
            {
                case RaceSoloPhase.Countdown:
                    return NetworkRaceState.CountdownSeconds;
                case RaceSoloPhase.Running:
                    return RaceRules.RoundSeconds;
                case RaceSoloPhase.RoundResult:
                    return NetworkRaceState.RoundResultSeconds;
                default:
                    return 1d;
            }
        }

        private static void DisableGeneratedHitColliders(GameObject root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }
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

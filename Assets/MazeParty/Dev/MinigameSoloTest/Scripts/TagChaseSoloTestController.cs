using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.TagChase;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class TagChaseSoloTestController : MonoBehaviour
    {
        private static readonly Color32[] PlayerColors =
        {
            new Color32(45, 122, 242, 255),
            new Color32(235, 57, 48, 255),
            new Color32(46, 199, 82, 255),
            new Color32(177, 68, 232, 255)
        };

        private readonly Transform[] _players =
            new Transform[TagChaseRules.PlayerCount];
        private readonly PlayerAvatarVisual[] _visuals =
            new PlayerAvatarVisual[TagChaseRules.PlayerCount];

        private TagChaseSoloSession _session;
        private MinigameSoloHudView _hud;
        private NetworkTagChaseState _productionState;
        private TagChaseNetworkView _productionView;
        private TagChaseHudBindings _productionHud;
        private GameObject _arenaPresentation;
        private GameObject _productionPlayerRoot;
        private Transform _runtimeRoot;
        private Camera _runtimeCamera;
        private float _viewYaw;
        private float _viewPitch;
        private int _presentedRound;
        private bool _initialized;

        public bool IsInitialized => _initialized;
        public TagChaseSoloSession Session => _session;
        public Camera RuntimeCamera => _runtimeCamera;
        public Transform LocalPlayer =>
            _players[TagChaseSoloSession.LocalPlayerSlot];

        public void ConfigureHud(MinigameSoloHudView hud)
        {
            _hud = hud;
        }

        public void Begin(int seed)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "Tag Chase solo is already initialized.");
            }
            if (_hud == null || !_hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Tag Chase solo requires the developer HUD prefab " +
                    "instance.");
            }

            _hud.BindActions(
                RestartMatch,
                StartNextSeed,
                StopSoloTest);
            ResolveAndDisableProduction();
            CreateRuntimePresentation();
            _session = new TagChaseSoloSession();
            _session.Begin(seed);
            SyncViewToLocalFacing();
            _presentedRound = _session.RoundNumber;
            _initialized = true;
            RefreshPresentation(true);
            Debug.Log(
                "[Minigame Solo Test] Tag Chase started with a seeded " +
                "four-round tagger order.");
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

            ReadLook();
            _session.Tick(
                Time.unscaledDeltaTime,
                ReadMove(),
                _viewYaw,
                Mouse.current != null &&
                Mouse.current.leftButton.wasPressedThisFrame);
            if (_presentedRound != _session.RoundNumber)
            {
                _presentedRound = _session.RoundNumber;
                SyncViewToLocalFacing();
            }
            RefreshPresentation(false);
        }

        private void OnDestroy()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (_runtimeRoot != null)
            {
                Destroy(_runtimeRoot.gameObject);
            }
        }

        private void ResolveAndDisableProduction()
        {
            _productionState =
                FindAnyObjectByType<NetworkTagChaseState>(
                    FindObjectsInactive.Include);
            _productionView =
                FindAnyObjectByType<TagChaseNetworkView>(
                    FindObjectsInactive.Include);
            if (_productionState == null || _productionView == null)
            {
                throw new InvalidOperationException(
                    "Tag Chase production state and view were not found " +
                    "in the scene.");
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
                .GetComponentInChildren<TagChaseHudBindings>(true);
            if (_arenaPresentation == null ||
                _productionPlayerRoot == null ||
                _productionHud == null ||
                !_productionHud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Tag Chase scene presentation contract is incomplete.");
            }

            _arenaPresentation.SetActive(true);
            _productionPlayerRoot.SetActive(false);
            _productionHud.gameObject.SetActive(true);
        }

        private void CreateRuntimePresentation()
        {
            _runtimeRoot =
                new GameObject("[Developer] Tag Chase Runtime")
                    .transform;
            var playerRoot = new GameObject("Solo Players").transform;
            playerRoot.SetParent(_runtimeRoot, false);
            for (var slot = 0; slot < TagChaseRules.PlayerCount; slot++)
            {
                var player =
                    new GameObject("Solo Player " + (slot + 1));
                player.transform.SetParent(playerRoot, false);
                var visual = player.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.SetBodyColor(PlayerColors[slot]);
                visual.SetDisplayName(
                    slot == TagChaseSoloSession.LocalPlayerSlot
                        ? "SOLO DEV"
                        : "PRACTICE " + (slot + 1));
                DisableGeneratedHitColliders(player);
                _players[slot] = player.transform;
                _visuals[slot] = visual;
            }

            var cameraObject =
                new GameObject("Tag Chase Solo Camera");
            cameraObject.transform.SetParent(_runtimeRoot, false);
            _runtimeCamera = cameraObject.AddComponent<Camera>();
            _runtimeCamera.nearClipPlane = 0.08f;
            _runtimeCamera.farClipPlane = 100f;
            _runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.018f, 0.024f, 0.036f);
            cameraObject.AddComponent<AudioListener>();
        }

        private void RefreshPresentation(bool snap)
        {
            var localIsTagger =
                _session.IsTagger(TagChaseSoloSession.LocalPlayerSlot);
            for (var slot = 0; slot < TagChaseRules.PlayerCount; slot++)
            {
                var position = _session.GetPlayerPosition(slot);
                var target = new Vector3(
                    position.x,
                    TagChaseNetworkView.PlayerPresentationHeight,
                    position.y);
                _players[slot].position = snap
                    ? target
                    : Vector3.Lerp(
                        _players[slot].position,
                        target,
                        1f - Mathf.Exp(
                            -18f * Time.unscaledDeltaTime));

                var facing = _session.GetPlayerFacing(slot);
                if (facing.sqrMagnitude > 0.0001f)
                {
                    _players[slot].rotation = Quaternion.LookRotation(
                        new Vector3(facing.x, 0f, facing.y),
                        Vector3.up);
                }

                _visuals[slot].SetEliminated(_session.IsCaught(slot));
                _visuals[slot].SetOwnerFirstPerson(
                    localIsTagger &&
                    slot == TagChaseSoloSession.LocalPlayerSlot);
                _visuals[slot].SetTopViewHighlight(
                    !localIsTagger &&
                    slot == TagChaseSoloSession.LocalPlayerSlot &&
                    !_session.IsCaught(slot));
            }

            RefreshCamera(localIsTagger);
            RefreshHud(localIsTagger);
        }

        private void RefreshCamera(bool localIsTagger)
        {
            if (localIsTagger)
            {
                var position = _session.GetPlayerPosition(
                    TagChaseSoloSession.LocalPlayerSlot);
                _runtimeCamera.fieldOfView =
                    TagChaseNetworkView.TaggerFieldOfView;
                _runtimeCamera.transform.SetPositionAndRotation(
                    new Vector3(
                        position.x,
                        TagChaseNetworkView.FirstPersonEyeHeight,
                        position.y),
                    Quaternion.Euler(_viewPitch, _viewYaw, 0f));
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }

            var count = 0;
            var center = Vector3.zero;
            for (var slot = 0; slot < TagChaseRules.PlayerCount; slot++)
            {
                if (_session.IsTagger(slot) || _session.IsCaught(slot))
                {
                    continue;
                }
                var position = _session.GetPlayerPosition(slot);
                center += new Vector3(position.x, 0f, position.y);
                count++;
            }
            center = count > 0
                ? center / count
                : new Vector3(
                    NetworkTagChaseState.ArenaCenterX,
                    0f,
                    0f);

            var radius = 0f;
            for (var slot = 0; slot < TagChaseRules.PlayerCount; slot++)
            {
                if (_session.IsTagger(slot) || _session.IsCaught(slot))
                {
                    continue;
                }
                var position = _session.GetPlayerPosition(slot);
                radius = Mathf.Max(
                    radius,
                    Vector2.Distance(
                        new Vector2(center.x, center.z),
                        position));
            }

            var cameraPosition =
                center + new Vector3(
                    0f,
                    6.5f + radius * 0.45f,
                    -8f - radius * 0.85f);
            _runtimeCamera.fieldOfView =
                TagChaseNetworkView.SharedFieldOfView;
            _runtimeCamera.transform.SetPositionAndRotation(
                cameraPosition,
                Quaternion.LookRotation(
                    center + Vector3.up * 0.8f - cameraPosition,
                    Vector3.up));
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = true;
        }

        private void RefreshHud(bool localIsTagger)
        {
            var timerDuration = GetPhaseDuration(_session.Phase);
            _productionHud.TimerDial.SetTime(
                _session.Remaining,
                timerDuration);

            var stateLabel =
                _session.Phase == TagChaseSoloPhase.Complete
                    ? BuildFinalLabel()
                    : "ROUND " + _session.RoundNumber + "/4  ·  " +
                      (localIsTagger
                          ? "YOU ARE TAGGER"
                          : _session.IsCaught(
                              TagChaseSoloSession.LocalPlayerSlot)
                              ? "CAUGHT · SPECTATING"
                              : "YOU ARE RUNNER");
            var scores =
                "P1 " + _session.GetTotalScore(0) +
                "  ·  P2 " + _session.GetTotalScore(1) +
                "  ·  P3 " + _session.GetTotalScore(2) +
                "  ·  P4 " + _session.GetTotalScore(3);
            _hud.SetContent(
                "TAG CHASE · SOLO",
                stateLabel,
                scores,
                "TAGGER P" + (_session.TaggerSlot + 1),
                localIsTagger
                    ? "WASD MOVE  ·  MOUSE LOOK  ·  LMB CATCH"
                    : "WASD MOVE  ·  SURVIVE",
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
                if (entry.PlayerSlot == TagChaseSoloSession.LocalPlayerSlot)
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
            _presentedRound = _session.RoundNumber;
            SyncViewToLocalFacing();
            RefreshPresentation(true);
        }

        private void StartNextSeed()
        {
            _session.Begin(unchecked(_session.Seed + 1));
            _presentedRound = _session.RoundNumber;
            SyncViewToLocalFacing();
            RefreshPresentation(true);
        }

        private void ReadLook()
        {
            if (!_session.IsTagger(TagChaseSoloSession.LocalPlayerSlot) ||
                Mouse.current == null)
            {
                return;
            }
            var delta = Mouse.current.delta.ReadValue();
            _viewYaw += delta.x * 0.08f;
            _viewPitch = Mathf.Clamp(
                _viewPitch - delta.y * 0.08f,
                -75f,
                75f);
        }

        private void SyncViewToLocalFacing()
        {
            var facing = _session.GetPlayerFacing(
                TagChaseSoloSession.LocalPlayerSlot);
            _viewYaw = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg;
            _viewPitch = 0f;
        }

        private static Vector2 ReadMove()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }
            var input = Vector2.zero;
            if (keyboard.aKey.isPressed) input.x -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.wKey.isPressed) input.y += 1f;
            return Vector2.ClampMagnitude(input, 1f);
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

        private static double GetPhaseDuration(TagChaseSoloPhase phase)
        {
            switch (phase)
            {
                case TagChaseSoloPhase.Countdown:
                    return NetworkTagChaseState.CountdownSeconds;
                case TagChaseSoloPhase.Running:
                    return TagChaseRules.RoundSeconds;
                case TagChaseSoloPhase.RoundResult:
                    return NetworkTagChaseState.RoundResultSeconds;
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

        private static Transform FindDescendant(
            Transform root,
            string name)
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

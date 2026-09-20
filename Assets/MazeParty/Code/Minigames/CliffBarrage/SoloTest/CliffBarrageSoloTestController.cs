using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.CliffBarrage;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    /// <summary>
    /// Offline practice in the production cliff scene using the same pure
    /// simulation as the server. This does not exercise NGO authority/RPCs.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class CliffBarrageSoloTestController : MonoBehaviour
    {
        public const int LocalPlayerSlot = 0;

        private readonly Transform[] _players =
            new Transform[CliffBarrageRules.PlayerCount];
        private readonly PlayerAvatarVisual[] _visuals =
            new PlayerAvatarVisual[CliffBarrageRules.PlayerCount];
        private readonly RedLightGreenLightPlayerPresentation[] _torsoPoses =
            new RedLightGreenLightPlayerPresentation[
                CliffBarrageRules.PlayerCount];
        private readonly Transform[] _projectiles =
            new Transform[CliffBarrageRules.MaximumProjectiles];
        private readonly GameObject[] _laserRoots =
            new GameObject[CliffBarrageRules.MaximumLasers];
        private readonly Transform[] _warnings =
            new Transform[CliffBarrageRules.MaximumLasers];
        private readonly Transform[] _beams =
            new Transform[CliffBarrageRules.MaximumLasers];
        private readonly List<CameraState> _cameras = new List<CameraState>();
        private readonly List<ListenerState> _listeners =
            new List<ListenerState>();

        private CliffBarrageMatchState _match;
        private NetworkCliffBarrageState _productionState;
        private CliffBarrageNetworkView _productionView;
        private MinigameSoloHudView _hud;
        private Transform _arena;
        private Camera _runtimeCamera;
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
            RoundResult,
            Complete
        }

        public bool IsInitialized => _initialized;
        public int Seed => _seed;
        public CliffBarrageMatchState Match => _match;
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
                    "Cliff Barrage solo is already initialized.");
            }
            if (_hud == null || !_hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Cliff Barrage solo requires the authored developer HUD.");
            }

            ResolveProductionScene();
            _hud.BindActions(RestartMatch, StartNextSeed, StopSoloTest);
            CreateRuntimeCamera();
            _initialized = true;
            BeginMatch(seed);
            Debug.Log("[Minigame Solo Test] Cliff Barrage started. " +
                "WASD moves; left click pushes. NGO is not exercised.");
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
                NetworkCliffBarrageState>(FindObjectsInactive.Include);
            _productionView = FindAnyObjectByType<
                CliffBarrageNetworkView>(FindObjectsInactive.Include);
            if (_productionState == null || _productionView == null ||
                _productionView.ArenaPresentation == null)
            {
                throw new InvalidOperationException(
                    "Cliff Barrage production scene is incomplete.");
            }

            _arena = _productionView.ArenaPresentation.transform;
            for (var slot = 0; slot < _players.Length; slot++)
            {
                _players[slot] = _productionView.GetPlayerTransform(slot);
                if (_players[slot] == null)
                {
                    throw new InvalidOperationException(
                        "Cliff Barrage scene lacks player " +
                        (slot + 1) + ".");
                }
                _visuals[slot] = _players[slot]
                    .GetComponent<PlayerAvatarVisual>();
                _torsoPoses[slot] = _players[slot]
                    .GetComponent<RedLightGreenLightPlayerPresentation>();
            }
            for (var index = 0; index < _projectiles.Length; index++)
            {
                _projectiles[index] =
                    _productionView.GetProjectileTransform(index);
                if (_projectiles[index] == null)
                {
                    throw new InvalidOperationException(
                        "Cliff Barrage scene lacks projectile " +
                        (index + 1) + ".");
                }
            }
            for (var index = 0; index < _laserRoots.Length; index++)
            {
                _laserRoots[index] =
                    _productionView.GetLaserRoot(index);
                _warnings[index] =
                    _productionView.GetWarningBeam(index);
                _beams[index] =
                    _productionView.GetFiringBeam(index);
                if (_laserRoots[index] == null ||
                    _warnings[index] == null || _beams[index] == null)
                {
                    throw new InvalidOperationException(
                        "Cliff Barrage scene lacks laser rig " +
                        (index + 1) + ".");
                }
            }

            _stateWasEnabled = _productionState.enabled;
            _viewWasEnabled = _productionView.enabled;
            _arenaWasActive = _arena.gameObject.activeSelf;
            _productionState.enabled = false;
            _productionView.enabled = false;
            _arena.gameObject.SetActive(true);
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
                "Solo Cliff Barrage Camera",
                typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.SetPositionAndRotation(
                CliffBarrageNetworkView.SharedCameraPosition,
                CliffBarrageNetworkView.SharedCameraRotation);
            _runtimeCamera = cameraObject.GetComponent<Camera>();
            _runtimeCamera.orthographic = true;
            _runtimeCamera.orthographicSize =
                CliffBarrageNetworkView.SharedCameraOrthographicSize;
            _runtimeCamera.nearClipPlane = 0.1f;
            _runtimeCamera.farClipPlane = 100f;
            _runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.018f, 0.024f, 0.036f);
        }

        private void BeginMatch(int seed)
        {
            _seed = seed;
            _match = new CliffBarrageMatchState(seed);
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
                    if (_phaseElapsed >=
                        CliffBarrageRules.CountdownSeconds)
                    {
                        _phase = SoloPhase.Playing;
                        _phaseElapsed = 0d;
                    }
                    break;
                case SoloPhase.Playing:
                    UpdateInputs();
                    _match.AdvanceTo(
                        _match.RoundElapsedSeconds + delta);
                    if (_match.IsRoundComplete)
                    {
                        _phase = _match.IsComplete
                            ? SoloPhase.Complete
                            : SoloPhase.RoundResult;
                        _phaseElapsed = 0d;
                    }
                    break;
                case SoloPhase.RoundResult:
                    _phaseElapsed += delta;
                    if (_phaseElapsed >=
                        CliffBarrageRules.ResultSeconds)
                    {
                        _match.BeginNextRound();
                        _phase = SoloPhase.Playing;
                        _phaseElapsed = 0d;
                    }
                    break;
                case SoloPhase.Complete:
                    _phaseElapsed = Math.Min(
                        CliffBarrageRules.ResultSeconds,
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
            if (Mouse.current?.leftButton.wasPressedThisFrame == true)
            {
                _match.TryPush(LocalPlayerSlot);
            }

            for (var slot = 1; slot < _players.Length; slot++)
            {
                UpdatePracticePlayer(slot);
            }
        }

        private void UpdatePracticePlayer(int slot)
        {
            var player = _match.GetPlayer(slot);
            if (player.IsEliminated)
            {
                return;
            }

            var targetSlot = -1;
            var bestDistance = double.MaxValue;
            for (var otherSlot = 0; otherSlot < _players.Length;
                otherSlot++)
            {
                var other = _match.GetPlayer(otherSlot);
                if (otherSlot == slot || other.IsEliminated)
                {
                    continue;
                }
                var dx = other.X - player.X;
                var dz = other.Z - player.Z;
                var distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    targetSlot = otherSlot;
                }
            }

            var danger = Math.Max(Math.Abs(player.X),
                Math.Abs(player.Z));
            double steerX;
            double steerZ;
            if (danger > 6.2d || targetSlot < 0)
            {
                steerX = -player.X;
                steerZ = -player.Z;
            }
            else
            {
                var target = _match.GetPlayer(targetSlot);
                steerX = target.X - player.X;
                steerZ = target.Z - player.Z;
            }
            var length = Math.Sqrt(steerX * steerX +
                steerZ * steerZ);
            _match.SetMovementInput(slot,
                length > 0.001d ? steerX / length : 0d,
                length > 0.001d ? steerZ / length : 0d);
            if (targetSlot >= 0 && bestDistance <
                CliffBarrageRules.PushRange *
                CliffBarrageRules.PushRange)
            {
                _match.TryPush(slot);
            }
        }

        private void RefreshPresentation()
        {
            if (_match == null || _arena == null)
            {
                return;
            }

            for (var slot = 0; slot < _players.Length; slot++)
            {
                var player = _match.GetPlayer(slot);
                var root = _players[slot];
                root.gameObject.SetActive(!player.IsEliminated);
                if (player.IsEliminated)
                {
                    continue;
                }
                root.position = _arena.TransformPoint(new Vector3(
                    (float)player.X,
                    CliffBarrageNetworkView.PlayerPresentationHeight,
                    (float)player.Z));
                var facing = new Vector3(
                    (float)player.FacingX, 0f,
                    (float)player.FacingZ);
                if (facing.sqrMagnitude > 0.001f)
                {
                    root.rotation = Quaternion.LookRotation(
                        facing, Vector3.up);
                }
                root.localScale = Vector3.one;
                _visuals[slot].SetOwnerFirstPerson(false);
                _visuals[slot].SetTopViewHighlight(
                    slot == LocalPlayerSlot);
                _torsoPoses[slot].ApplyState(
                    player.HitCount, false);
            }

            for (var index = 0; index < _projectiles.Length; index++)
            {
                var projectile = _match.GetProjectile(index);
                _projectiles[index].gameObject.SetActive(
                    projectile.Active);
                if (projectile.Active)
                {
                    _projectiles[index].localPosition = new Vector3(
                        (float)projectile.X,
                        CliffBarrageNetworkView
                            .ProjectilePresentationHeight,
                        (float)projectile.Z);
                }
            }

            for (var index = 0; index < _laserRoots.Length; index++)
            {
                var laser = _match.GetLaser(index);
                var warning = laser.Phase ==
                    CliffBarrageLaserPhase.Warning;
                var firing = laser.Phase ==
                    CliffBarrageLaserPhase.Firing;
                _laserRoots[index].SetActive(warning || firing);
                if (!warning && !firing)
                {
                    continue;
                }
                var dx = (float)(laser.EndX - laser.StartX);
                var dz = (float)(laser.EndZ - laser.StartZ);
                var length = Mathf.Max(0.01f,
                    Mathf.Sqrt(dx * dx + dz * dz));
                var root = _laserRoots[index].transform;
                root.localPosition = new Vector3(
                    (float)((laser.StartX + laser.EndX) * 0.5d),
                    0f,
                    (float)((laser.StartZ + laser.EndZ) * 0.5d));
                root.localRotation = length > 0.01f
                    ? Quaternion.LookRotation(
                        new Vector3(dx, 0f, dz), Vector3.up)
                    : Quaternion.identity;
                _warnings[index].gameObject.SetActive(warning);
                _beams[index].gameObject.SetActive(firing);
                _warnings[index].localScale =
                    new Vector3(0.14f, 0.08f, length);
                _beams[index].localScale =
                    new Vector3(0.65f, 1.5f, length);
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
                ? _match.GetFinalRank(LocalPlayerSlot)
                : 0;
            var phaseLabel = _phase == SoloPhase.Playing
                ? "ROUND " + _match.RoundNumber + " / 3 · " +
                  Math.Max(0d,
                      CliffBarrageRules.RoundDurationSeconds -
                      _match.RoundElapsedSeconds).ToString("0.0") + "s"
                : _phase == SoloPhase.Complete
                    ? "COMPLETE"
                    : _phase == SoloPhase.RoundResult
                        ? "ROUND RESULT · NEXT IN " +
                          Math.Max(0d,
                              CliffBarrageRules.ResultSeconds -
                              _phaseElapsed).ToString("0.0") + "s"
                        : "ROUND " + _match.RoundNumber +
                          " START IN " +
                          Math.Max(0d,
                              CliffBarrageRules.CountdownSeconds -
                              _phaseElapsed).ToString("0.0") + "s";
            var outcome = finalRank > 0
                ? MinigameDisplayFormatter.ToOrdinal(finalRank) +
                  " · GOLD +" +
                  MinigameRewardRules.GetFinalPlacementGold(finalRank)
                : local.IsEliminated
                    ? "ELIMINATED · WAIT FOR NEXT ROUND"
                    : local.HitCount > 0
                        ? "TORSO LOST · ONE HIT LEFT"
                        : "DODGE SHELLS AND LASERS · PUSH TO THE EDGE";
            var feedback = _match.LastDamagedSlot >= 0
                ? "P" + (_match.LastDamagedSlot + 1) +
                  " TOOK A HIT THIS ROUND"
                : "LASER: 1s WARNING, 0.5s FIRE · " +
                  "1s HIT INVULNERABILITY";
            _hud.SetContent(
                "CLIFF BARRAGE · SOLO",
                phaseLabel,
                "SURVIVORS " + _match.SurvivorCount +
                " / 4 · SCORE " + local.TotalScore +
                " · SEED " + _seed,
                outcome,
                "WASD MOVE · LMB PUSH · R RESTART · " +
                "N NEXT SEED · ESC STOP",
                feedback,
                finalRank == 1
                    ? MinigameSoloFeedbackStyle.Success
                    : finalRank > 1 || local.IsEliminated ||
                      local.HitCount > 0
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

        private void RestartMatch() => BeginMatch(_seed);

        private void StartNextSeed() =>
            BeginMatch(unchecked(_seed + 1));

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

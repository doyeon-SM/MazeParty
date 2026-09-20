using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.ArenaCombat;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    /// <summary>
    /// Local practice on the authored arena. It uses the shared combat tuning
    /// and ranking rules, but does not pretend to exercise NGO authority/RPCs.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class ArenaCombatSoloTestController : MonoBehaviour
    {
        public const int LocalPlayerSlot = 0;

        private const float MoveSpeed = 5f;
        private const float AiMoveSpeed = 4.2f;
        private const float BodyRadius = 0.5f;
        private const float EyeOffset = 0.75f;
        private const float KnockbackDamping = 5f;

        private static readonly Color32[] PlayerColors =
        {
            new Color32(45, 122, 242, 255),
            new Color32(235, 57, 48, 255),
            new Color32(46, 199, 82, 255),
            new Color32(177, 68, 232, 255)
        };

        private readonly Fighter[] _fighters =
            new Fighter[ArenaCombatRules.PlayerCount];

        private MinigameSoloHudView _hud;
        private NetworkArenaCombatState _productionState;
        private ArenaCombatNetworkView _productionView;
        private GameObject _arena;
        private Camera _camera;
        private float _yaw;
        private float _pitch;
        private double _elapsed;
        private double _phaseElapsed;
        private int _seed;
        private bool _initialized;
        private PracticePhase _phase;
        private int[] _finalRanks;

        private enum PracticePhase : byte
        {
            Countdown,
            Playing,
            Complete
        }

        public bool IsInitialized => _initialized;
        public Camera RuntimeCamera => _camera;
        public Transform LocalPlayer => _fighters[LocalPlayerSlot]?.Root;
        public int Seed => _seed;
        public bool IsComplete => _phase == PracticePhase.Complete;

        public void ConfigureHud(MinigameSoloHudView hud)
        {
            _hud = hud;
        }

        public void Begin(int seed)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "Arena Combat solo is already initialized.");
            }
            if (_hud == null || !_hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Arena Combat solo requires the authored developer HUD.");
            }

            _productionState = FindAnyObjectByType<
                NetworkArenaCombatState>(FindObjectsInactive.Include);
            _productionView = FindAnyObjectByType<
                ArenaCombatNetworkView>(FindObjectsInactive.Include);
            if (_productionState == null || _productionView == null ||
                _productionView.ArenaPresentation == null)
            {
                throw new InvalidOperationException(
                    "Arena Combat production scene is incomplete.");
            }

            _productionState.enabled = false;
            _productionView.enabled = false;
            _arena = _productionView.ArenaPresentation;
            _arena.SetActive(true);
            _hud.BindActions(RestartMatch, StartNextSeed, StopSoloTest);
            CreateFighters();
            CreateCamera();
            _initialized = true;
            BeginMatch(seed);
            Debug.Log("[Minigame Solo Test] Arena Combat practice started. " +
                "WASD move, mouse look, LMB punch.");
        }

        private void Update()
        {
            if (!_initialized || HandleShortcuts())
            {
                return;
            }

            var delta = Mathf.Max(0f, Time.unscaledDeltaTime);
            switch (_phase)
            {
                case PracticePhase.Countdown:
                    _phaseElapsed += delta;
                    if (_phaseElapsed >= ArenaCombatRules.CountdownSeconds)
                    {
                        _phase = PracticePhase.Playing;
                        _phaseElapsed = 0d;
                    }
                    break;
                case PracticePhase.Playing:
                    TickPlaying(delta);
                    break;
            }

            RefreshPresentation();
        }

        private void OnDestroy()
        {
            _hud?.BindActions(null, null, null);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void CreateFighters()
        {
            var playerRoot = new GameObject("Practice Fighters").transform;
            playerRoot.SetParent(transform, false);
            for (var slot = 0; slot < ArenaCombatRules.PlayerCount; slot++)
            {
                var body = new GameObject("Practice Fighter " + (slot + 1));
                body.transform.SetParent(playerRoot, false);
                var eye = new GameObject("Eye Pivot").transform;
                eye.SetParent(body.transform, false);
                eye.localPosition = Vector3.up * EyeOffset;
                var visual = body.AddComponent<PlayerAvatarVisual>();
                visual.ConfigureEyePivot(eye);
                visual.EnsureBuilt();
                visual.SetBodyColor(PlayerColors[slot]);
                visual.SetDisplayName(slot == LocalPlayerSlot
                    ? "SOLO DEV"
                    : "PRACTICE " + (slot + 1));
                DisableGeneratedHitColliders(body);
                _fighters[slot] = new Fighter(body.transform, eye, visual);
            }
        }

        private void CreateCamera()
        {
            var cameraObject = new GameObject("Arena Combat Solo Camera");
            cameraObject.transform.SetParent(transform, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.fieldOfView = ArenaCombatNetworkView.FirstPersonFieldOfView;
            _camera.nearClipPlane = 0.08f;
            _camera.farClipPlane = 120f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.025f, 0.03f, 0.045f);
            cameraObject.AddComponent<AudioListener>();
        }

        private void BeginMatch(int seed)
        {
            _seed = seed;
            _phase = PracticePhase.Countdown;
            _phaseElapsed = 0d;
            _elapsed = 0d;
            _finalRanks = null;
            for (var slot = 0; slot < _fighters.Length; slot++)
            {
                var fighter = _fighters[slot];
                fighter.Root.position = ArenaCombatRules.GetSpawnPosition(slot);
                var towardCenter = new Vector3(
                    ArenaCombatRules.ArenaCenterX - fighter.Root.position.x,
                    0f,
                    -fighter.Root.position.z);
                fighter.Root.rotation = Quaternion.LookRotation(
                    towardCenter.normalized, Vector3.up);
                fighter.Health = BoardCombatRules.TemporaryHealth;
                fighter.EliminatedAt = double.NaN;
                fighter.NextPunchAt = 0d;
                fighter.Knockback = Vector3.zero;
                fighter.Visual.SetEliminated(false);
                fighter.Visual.SetOwnerFirstPerson(false);
                fighter.Root.gameObject.SetActive(true);
            }
            var localFacing = _fighters[LocalPlayerSlot].Root.forward;
            _yaw = Mathf.Atan2(localFacing.x, localFacing.z) *
                   Mathf.Rad2Deg;
            _pitch = 0f;
            RefreshPresentation();
        }

        private void TickPlaying(float delta)
        {
            _elapsed += delta;
            var local = _fighters[LocalPlayerSlot];
            if (local.IsAlive)
            {
                ReadLook();
                local.Root.rotation = Quaternion.Euler(0f, _yaw, 0f);
                MoveFighter(local, ReadMove(), MoveSpeed, delta,
                    Quaternion.Euler(0f, _yaw, 0f));
                if (Mouse.current != null &&
                    Mouse.current.leftButton.wasPressedThisFrame)
                {
                    TryPunch(LocalPlayerSlot,
                        Quaternion.Euler(_pitch, _yaw, 0f) *
                        Vector3.forward);
                }
            }

            for (var slot = 1; slot < _fighters.Length; slot++)
            {
                var fighter = _fighters[slot];
                if (!fighter.IsAlive)
                {
                    continue;
                }
                var targetSlot = GetNearestLivingTarget(slot);
                if (targetSlot < 0)
                {
                    continue;
                }
                var deltaToTarget = _fighters[targetSlot].Root.position -
                                    fighter.Root.position;
                deltaToTarget.y = 0f;
                var forward = deltaToTarget.sqrMagnitude > 0.0001f
                    ? deltaToTarget.normalized
                    : fighter.Root.forward;
                fighter.Root.rotation = Quaternion.LookRotation(
                    forward, Vector3.up);
                MoveFighter(fighter,
                    deltaToTarget.magnitude > 1.35f
                        ? Vector2.up
                        : Vector2.zero,
                    AiMoveSpeed, delta, fighter.Root.rotation);
                if (deltaToTarget.magnitude <=
                    BoardCombatRules.PunchRange + BodyRadius)
                {
                    TryPunch(slot, forward);
                }
            }

            foreach (var fighter in _fighters)
            {
                if (!fighter.IsAlive)
                {
                    continue;
                }
                fighter.Root.position = ClampToArena(
                    fighter.Root.position + fighter.Knockback * delta);
                fighter.Knockback = Vector3.Lerp(
                    fighter.Knockback, Vector3.zero,
                    1f - Mathf.Exp(-KnockbackDamping * delta));
            }

            if (ArenaCombatRules.ShouldEndMatch(
                    _elapsed, CountSurvivors()))
            {
                CompleteMatch();
            }
        }

        private static void MoveFighter(
            Fighter fighter,
            Vector2 input,
            float speed,
            float delta,
            Quaternion facing)
        {
            var direction = facing * new Vector3(input.x, 0f, input.y);
            fighter.Root.position = ClampToArena(
                fighter.Root.position + direction * speed * delta);
        }

        private void TryPunch(int attackerSlot, Vector3 direction)
        {
            var attacker = _fighters[attackerSlot];
            if (!attacker.IsAlive || _elapsed < attacker.NextPunchAt)
            {
                return;
            }
            attacker.NextPunchAt = _elapsed +
                BoardCombatRules.PunchCooldownSeconds;
            attacker.Visual.TriggerPunch();

            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : attacker.Root.forward;
            var bestSlot = -1;
            var bestDistance = float.MaxValue;
            for (var slot = 0; slot < _fighters.Length; slot++)
            {
                if (slot == attackerSlot || !_fighters[slot].IsAlive)
                {
                    continue;
                }
                var relative = _fighters[slot].Root.position -
                               attacker.Root.position;
                relative.y = 0f;
                var forwardDistance = Vector3.Dot(relative, direction);
                var lateral = relative - direction * forwardDistance;
                if (forwardDistance < 0f ||
                    forwardDistance > BoardCombatRules.PunchRange ||
                    lateral.magnitude > BodyRadius +
                    BoardCombatRules.PunchRadius ||
                    forwardDistance >= bestDistance)
                {
                    continue;
                }
                bestSlot = slot;
                bestDistance = forwardDistance;
            }
            if (bestSlot < 0)
            {
                return;
            }

            var victim = _fighters[bestSlot];
            victim.Health = Mathf.Max(0,
                victim.Health - BoardCombatRules.PunchDamage);
            victim.Visual.TriggerHit(PlayerHitRegion.Body);
            victim.Knockback = direction *
                BoardCombatRules.PunchKnockbackSpeed;
            if (bestSlot == LocalPlayerSlot)
            {
                ArenaCombatHitFlashView.Instance?.Flash();
            }
            if (victim.Health == 0)
            {
                victim.EliminatedAt = _elapsed;
                victim.Visual.SetEliminated(true);
            }
        }

        private int GetNearestLivingTarget(int attackerSlot)
        {
            var position = _fighters[attackerSlot].Root.position;
            var best = -1;
            var bestDistance = float.MaxValue;
            for (var slot = 0; slot < _fighters.Length; slot++)
            {
                if (slot == attackerSlot || !_fighters[slot].IsAlive)
                {
                    continue;
                }
                var distance = (_fighters[slot].Root.position - position)
                    .sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = slot;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private int CountSurvivors()
        {
            var count = 0;
            foreach (var fighter in _fighters)
            {
                if (fighter.IsAlive)
                {
                    count++;
                }
            }
            return count;
        }

        private void CompleteMatch()
        {
            var entries = new ArenaCombatRankingEntry[_fighters.Length];
            for (var slot = 0; slot < entries.Length; slot++)
            {
                var fighter = _fighters[slot];
                entries[slot] = new ArenaCombatRankingEntry(
                    slot, fighter.Health, fighter.EliminatedAt);
            }
            _finalRanks = ArenaCombatRules.ResolveRanks(entries);
            _phase = PracticePhase.Complete;
        }

        private void RefreshPresentation()
        {
            if (_camera == null)
            {
                return;
            }
            var local = _fighters[LocalPlayerSlot];
            var firstPerson = _phase == PracticePhase.Playing &&
                              local.IsAlive;
            local.Visual.SetOwnerFirstPerson(firstPerson);
            if (firstPerson)
            {
                _camera.fieldOfView =
                    ArenaCombatNetworkView.FirstPersonFieldOfView;
                _camera.transform.SetPositionAndRotation(
                    local.Eye.position,
                    Quaternion.Euler(_pitch, _yaw, 0f));
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                var center = new Vector3(
                    ArenaCombatRules.ArenaCenterX, 0f, 0f);
                if (_phase != PracticePhase.Countdown)
                {
                    var sum = Vector3.zero;
                    var count = 0;
                    foreach (var fighter in _fighters)
                    {
                        if (!fighter.IsAlive)
                        {
                            continue;
                        }
                        sum += fighter.Root.position;
                        count++;
                    }
                    if (count > 0)
                    {
                        center = sum / count;
                        center.y = 0f;
                    }
                }
                center.x = Mathf.Clamp(center.x,
                    ArenaCombatRules.ArenaCenterX - 3f,
                    ArenaCombatRules.ArenaCenterX + 3f);
                center.z = Mathf.Clamp(center.z, -3f, 3f);
                var position = center + new Vector3(0f, 12f, -14f);
                _camera.fieldOfView =
                    ArenaCombatNetworkView.SpectatorFieldOfView;
                _camera.transform.SetPositionAndRotation(
                    position,
                    Quaternion.LookRotation(
                        center + Vector3.up * 0.8f - position,
                        Vector3.up));
                Cursor.lockState = CursorLockMode.Confined;
                Cursor.visible = true;
            }

            var status = _phase == PracticePhase.Countdown
                ? "START IN " + Mathf.CeilToInt((float)
                    (ArenaCombatRules.CountdownSeconds - _phaseElapsed))
                : _phase == PracticePhase.Complete
                    ? "COMPLETE · RANK " +
                      (_finalRanks?[LocalPlayerSlot] ?? 0)
                    : local.IsAlive
                        ? "FIGHTING"
                        : "ELIMINATED · SPECTATING";
            var remaining = _phase == PracticePhase.Countdown
                ? ArenaCombatRules.CountdownSeconds - _phaseElapsed
                : _phase == PracticePhase.Playing
                    ? ArenaCombatRules.MatchDurationSeconds - _elapsed
                    : 0d;
            _hud.SetContent(
                "ARENA COMBAT · SOLO PRACTICE",
                status,
                "TIME " + Math.Max(0d, remaining).ToString("0.0") +
                "s · ALIVE " + CountSurvivors(),
                "ONE MATCH · NO HEALTH DISPLAY",
                "WASD MOVE · MOUSE LOOK · LMB PUNCH",
                "R RESTART · N NEXT SEED · ESC STOP",
                _phase == PracticePhase.Complete
                    ? MinigameSoloFeedbackStyle.Success
                    : local.IsAlive
                        ? MinigameSoloFeedbackStyle.Neutral
                        : MinigameSoloFeedbackStyle.Warning);
        }

        private void ReadLook()
        {
            if (Mouse.current == null)
            {
                return;
            }
            var delta = Mouse.current.delta.ReadValue();
            _yaw += delta.x * 0.08f;
            _pitch = Mathf.Clamp(_pitch - delta.y * 0.08f,
                -75f, 75f);
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

        private static Vector3 ClampToArena(Vector3 position)
        {
            position.x = Mathf.Clamp(position.x,
                ArenaCombatRules.ArenaCenterX -
                ArenaCombatRules.ArenaHalfWidth + BodyRadius,
                ArenaCombatRules.ArenaCenterX +
                ArenaCombatRules.ArenaHalfWidth - BodyRadius);
            position.z = Mathf.Clamp(position.z,
                -ArenaCombatRules.ArenaHalfDepth + BodyRadius,
                ArenaCombatRules.ArenaHalfDepth - BodyRadius);
            position.y = 1f;
            return position;
        }

        private bool HandleShortcuts()
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
        private void StartNextSeed() => BeginMatch(unchecked(_seed + 1));

        private static void DisableGeneratedHitColliders(GameObject root)
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

        private sealed class Fighter
        {
            public Fighter(
                Transform root,
                Transform eye,
                PlayerAvatarVisual visual)
            {
                Root = root;
                Eye = eye;
                Visual = visual;
                EliminatedAt = double.NaN;
            }

            public Transform Root { get; }
            public Transform Eye { get; }
            public PlayerAvatarVisual Visual { get; }
            public int Health { get; set; }
            public double EliminatedAt { get; set; }
            public double NextPunchAt { get; set; }
            public Vector3 Knockback { get; set; }
            public bool IsAlive => Health > 0 &&
                                   double.IsNaN(EliminatedAt);
        }
    }
}

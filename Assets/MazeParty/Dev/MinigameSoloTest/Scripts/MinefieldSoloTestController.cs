using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.Minefield;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    /// <summary>
    /// Runtime-only Minefield harness injected by the editor launcher. It reuses
    /// the production arena and offline gameplay components without starting NGO
    /// or Unity Gaming Services.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class MinefieldSoloTestController : MonoBehaviour
    {
        private const float RunnerStartZ =
            NetworkMinefieldState.ArenaMinZ + 0.75f;
        private const float FinishWorldZ =
            NetworkMinefieldState.ArenaMaxZ - 0.5f;
        private const float CrusherStartOffset = 2.5f;
        private const float CrusherEndOffset = 1f;
        private const float MineTriggerRadius = 1.05f;
        private const float SonarVisualSeconds = 0.75f;

        private MinefieldSoloSession _session;
        private NetworkMinefieldState _networkState;
        private MinefieldNetworkView _networkView;
        private Transform _sceneRoot;
        private GameObject _arenaPresentation;
        private Transform _runtimeRoot;
        private Transform _mineRoot;
        private MinefieldMineRegistry _mineRegistry;
        private Transform _playerRoot;
        private MinefieldPlayerActor _playerActor;
        private MinefieldPlayerMotor _playerMotor;
        private MinefieldSonar _sonar;
        private MinefieldCrusher _crusher;
        private Camera _runtimeCamera;
        private LineRenderer _sonarPulse;
        private Material _sonarPulseMaterial;
        private float _sonarPulseUntil = float.NegativeInfinity;
        private bool _initialized;

        public bool IsInitialized => _initialized;
        public MinefieldSoloSession Session => _session;
        public MinefieldPlayerActor PlayerActor => _playerActor;
        public Camera RuntimeCamera => _runtimeCamera;

        public int ActiveMineCount
        {
            get
            {
                if (_mineRegistry == null)
                {
                    return 0;
                }

                var count = 0;
                var mines = _mineRegistry.Mines;
                for (var index = 0; index < mines.Count; index++)
                {
                    if (mines[index] != null && mines[index].IsArmed)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void Begin(int seed)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The Minefield solo harness is already initialized.");
            }

            _networkState = FindAnyObjectByType<NetworkMinefieldState>();
            if (_networkState == null)
            {
                throw new InvalidOperationException(
                    "The active scene does not contain NetworkMinefieldState. " +
                    "Launch this harness with the Minefield scene.");
            }

            _sceneRoot = _networkState.transform;
            DisableNetworkPresentation();
            ResolveArena();
            CreateRuntimeRoots();
            CreateRuntimeCamera();
            CreatePlayer();
            ResolveCrusher();

            _session = new MinefieldSoloSession();
            _session.Begin(seed);
            BuildCurrentRound();
            _initialized = true;

            Debug.Log(
                "[Minigame Solo Test] Minefield started with seed " +
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

            if (_session.Phase == MinefieldSoloPhase.Running &&
                _playerActor != null)
            {
                if (_playerActor.CanMove &&
                    _playerRoot.position.z >= FinishWorldZ)
                {
                    _playerActor.ApplyAuthoritativeFinish();
                }

                if (!_playerActor.CanMove)
                {
                    _session.ResolveCurrentRound(
                        _playerActor.State == MinefieldPlayerState.Finished);
                }
            }

            _session.Tick(Time.unscaledDeltaTime);
            ApplySessionTransition(previousPhase, previousRound);

            if (_sonarPulse != null)
            {
                _sonarPulse.enabled =
                    Time.unscaledTime < _sonarPulseUntil &&
                    _session.Phase == MinefieldSoloPhase.Running;
            }
        }

        private void LateUpdate()
        {
            if (!_initialized || _runtimeCamera == null || _playerRoot == null)
            {
                return;
            }

            _runtimeCamera.transform.SetPositionAndRotation(
                MinefieldNetworkView.CalculatePlayerCameraPosition(
                    _playerRoot.position),
                MinefieldNetworkView.PlayerCameraRotation);
        }

        private void OnGUI()
        {
            if (!_initialized || _session == null)
            {
                return;
            }

            GUILayout.BeginArea(
                new Rect(18f, 18f, 455f, 260f),
                GUI.skin.box);
            GUILayout.Label("DEVELOPER SOLO TEST  /  MINEFIELD");
            GUILayout.Label(
                "ROUND " + _session.RoundNumber + " / " +
                MinefieldRules.RoundCount + "  ·  " +
                GetPhaseLabel() + "  ·  " +
                FormatClock(_session.RemainingSeconds));
            GUILayout.Label(
                "STATE " + GetPlayerStateLabel() +
                "  ·  MINES " + ActiveMineCount + " / " +
                NetworkMinefieldState.MineCount +
                "  ·  CLEARS " + _session.ClearedRoundCount);
            GUILayout.Label("SEED " + _session.Seed);
            GUILayout.Space(4f);
            GUILayout.Label(
                "WASD move  |  stop + RMB sonar  |  " +
                "first mine slows, second mine eliminates");

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Restart Round (R)", GUILayout.Height(32f)))
            {
                RestartRound();
            }
            if (GUILayout.Button("Next Seed (N)", GUILayout.Height(32f)))
            {
                StartNewSeed(unchecked(_session.Seed + 1));
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Stop Solo Test (Esc)", GUILayout.Height(30f)))
            {
                StopSoloTest();
            }
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (_sonar != null)
            {
                _sonar.PulseResolved -= HandleSonarResolved;
            }
            if (_sonarPulseMaterial != null)
            {
                Destroy(_sonarPulseMaterial);
            }
        }

        public void RestartRound()
        {
            if (_session == null)
            {
                return;
            }

            _session.RestartCurrentRound();
            BuildCurrentRound();
        }

        public void StartNewSeed(int seed)
        {
            if (_session == null)
            {
                return;
            }

            _session.Begin(seed);
            BuildCurrentRound();
        }

        private void DisableNetworkPresentation()
        {
            _networkState.enabled = false;
            _networkView = _sceneRoot.GetComponent<MinefieldNetworkView>();
            if (_networkView != null)
            {
                _networkView.enabled = false;
            }

            DisableAndDestroyGeneratedChild("Runtime Runners");
            DisableAndDestroyGeneratedChild("Runtime Mine Markers");
            DisableAndDestroyGeneratedChild("Minefield HUD");
        }

        private void DisableAndDestroyGeneratedChild(string childName)
        {
            var child = FindDescendant(_sceneRoot, childName);
            if (child == null)
            {
                return;
            }

            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        private void ResolveArena()
        {
            var arena = FindDescendant(_sceneRoot, "Arena Presentation");
            if (arena == null)
            {
                throw new InvalidOperationException(
                    "Minefield scene contract is missing Arena Presentation.");
            }

            _arenaPresentation = arena.gameObject;
            _arenaPresentation.SetActive(true);
        }

        private void CreateRuntimeRoots()
        {
            var rootObject = new GameObject("[Solo Test] Runtime");
            rootObject.transform.SetParent(transform, false);
            _runtimeRoot = rootObject.transform;

            var minesObject = new GameObject("Local Mines");
            minesObject.transform.SetParent(_runtimeRoot, false);
            _mineRoot = minesObject.transform;
            _mineRegistry = minesObject.AddComponent<MinefieldMineRegistry>();
        }

        private void CreateRuntimeCamera()
        {
            var cameraObject = new GameObject(
                "Solo Output Camera",
                typeof(Camera),
                typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(_runtimeRoot, false);

            _runtimeCamera = cameraObject.GetComponent<Camera>();
            _runtimeCamera.orthographic = true;
            _runtimeCamera.orthographicSize =
                MinefieldNetworkView.PlayerCameraOrthographicSize;
            _runtimeCamera.nearClipPlane = 0.1f;
            _runtimeCamera.farClipPlane = 100f;
            _runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.025f, 0.035f, 0.055f, 1f);
        }

        private void CreatePlayer()
        {
            var playerObject = new GameObject("Solo Runner");
            playerObject.transform.SetParent(_runtimeRoot, false);
            _playerRoot = playerObject.transform;

            var character = playerObject.AddComponent<CharacterController>();
            character.center = new Vector3(0f, 1f, 0f);
            character.height = 2f;
            character.radius = 0.5f;
            character.stepOffset = 0.3f;
            character.slopeLimit = 60f;

            _playerActor = playerObject.AddComponent<MinefieldPlayerActor>();
            _playerActor.ConfigurePlayerSlot(0);
            var input = playerObject.AddComponent<MinefieldInputAdapter>();
            _sonar = playerObject.AddComponent<MinefieldSonar>();
            _playerMotor = playerObject.AddComponent<MinefieldPlayerMotor>();

            var avatarVisual = playerObject.AddComponent<PlayerAvatarVisual>();
            avatarVisual.EnsureBuilt();
            avatarVisual.SetBodyColor(new Color(0.2f, 0.72f, 1f, 1f));
            avatarVisual.SetDisplayName("SOLO DEV");

            playerObject.AddComponent<MinefieldPlayerPresentation>();
            DisableGeneratedHitColliders(playerObject, character);
            CreatePlayerSiren(avatarVisual);
            CreateSonarPulse();

            _sonar.Configure(_playerActor, _playerMotor, _mineRegistry);
            _sonar.ConfigureDetection(
                NetworkMinefieldState.SonarRadius,
                SonarVisualSeconds,
                0.05f,
                0f,
                SonarVisualSeconds);
            _sonar.SetAutoResolveLocally(true);
            _sonar.PulseResolved += HandleSonarResolved;

            _playerMotor.Configure(
                input,
                _playerActor,
                _sonar,
                _runtimeCamera.transform);
            _playerMotor.ConfigureMovement(
                NetworkMinefieldState.RunnerSpeed,
                NetworkMinefieldState.RunnerSpeed *
                FootstepRules.WalkSpeedMultiplier,
                28f,
                720f);
            _playerMotor.SetLocalPrediction(true);
            _playerMotor.SetInputAuthority(true);
            _playerMotor.SetMovementEnabled(false);
        }

        private void CreatePlayerSiren(PlayerAvatarVisual avatarVisual)
        {
            var head = FindDescendant(_playerRoot, "HeadAnchor");
            var sirenRootObject = new GameObject("Proximity Siren");
            sirenRootObject.transform.SetParent(
                head != null ? head : _playerRoot,
                false);
            sirenRootObject.transform.localPosition =
                new Vector3(0f, 0.72f, 0f);

            var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseObject.name = "Siren Base";
            baseObject.transform.SetParent(sirenRootObject.transform, false);
            baseObject.transform.localScale =
                new Vector3(0.28f, 0.1f, 0.28f);
            RemoveCollider(baseObject);
            ApplySurfaceColor(
                baseObject.GetComponent<Renderer>(),
                new Color(0.12f, 0.12f, 0.14f, 1f));

            var lensObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lensObject.name = "Siren Red Lens";
            lensObject.transform.SetParent(sirenRootObject.transform, false);
            lensObject.transform.localPosition =
                new Vector3(0f, 0.22f, 0f);
            lensObject.transform.localScale =
                new Vector3(0.34f, 0.26f, 0.34f);
            RemoveCollider(lensObject);
            var lensRenderer = lensObject.GetComponent<Renderer>();

            var lightObject = new GameObject("Siren Red Light");
            lightObject.transform.SetParent(sirenRootObject.transform, false);
            lightObject.transform.localPosition =
                new Vector3(0f, 0.25f, 0f);
            var warningLight = lightObject.AddComponent<Light>();
            warningLight.type = LightType.Point;
            warningLight.color = Color.red;
            warningLight.range = 3f;
            warningLight.intensity = 0f;
            warningLight.enabled = false;

            var siren = sirenRootObject.AddComponent<MinefieldProximitySiren>();
            siren.Configure(
                _playerActor,
                _mineRegistry,
                lensObject.transform,
                lensRenderer,
                warningLight);
            siren.ConfigureWarning(
                NetworkMinefieldState.SonarRadius,
                1f,
                4f,
                4f);
        }

        private void CreateSonarPulse()
        {
            var pulseObject = new GameObject("Sonar Pulse");
            pulseObject.transform.SetParent(_playerRoot, false);
            pulseObject.transform.localPosition =
                new Vector3(0f, 0.06f, 0f);

            _sonarPulse = pulseObject.AddComponent<LineRenderer>();
            _sonarPulse.loop = true;
            _sonarPulse.useWorldSpace = false;
            _sonarPulse.widthMultiplier = 0.09f;
            _sonarPulse.positionCount = 65;
            _sonarPulse.startColor =
                new Color(1f, 0.2f, 0.15f, 0.9f);
            _sonarPulse.endColor = _sonarPulse.startColor;
            _sonarPulseMaterial =
                WorldTextOcclusion.CreateBuildSafeLitMaterial(
                    "Minefield Solo Sonar Pulse");
            if (_sonarPulseMaterial != null)
            {
                _sonarPulse.material = _sonarPulseMaterial;
            }

            for (var point = 0; point < _sonarPulse.positionCount; point++)
            {
                var angle = point / 64f * Mathf.PI * 2f;
                _sonarPulse.SetPosition(
                    point,
                    new Vector3(
                        Mathf.Cos(angle) *
                        NetworkMinefieldState.SonarRadius,
                        0f,
                        Mathf.Sin(angle) *
                        NetworkMinefieldState.SonarRadius));
            }

            _sonarPulse.enabled = false;
        }

        private void ResolveCrusher()
        {
            _crusher = _sceneRoot.GetComponentInChildren<MinefieldCrusher>(true);
            if (_crusher == null)
            {
                throw new InvalidOperationException(
                    "Minefield scene contract is missing its crusher.");
            }

            _crusher.gameObject.SetActive(true);
            _crusher.SetSimulationAuthority(true);
            _crusher.SetAutoResolveContacts(true);
        }

        private void BuildCurrentRound()
        {
            if (_session == null)
            {
                return;
            }

            _playerActor.ResetForRoundAuthoritatively(0, false);
            _playerMotor.TeleportAuthoritatively(
                GetRunnerStartPosition(),
                Quaternion.identity);
            _playerMotor.SetMovementEnabled(false);
            _sonar.SetSonarEnabled(false);
            _sonarPulseUntil = float.NegativeInfinity;
            if (_sonarPulse != null)
            {
                _sonarPulse.enabled = false;
            }

            RebuildMines(_session.CreateCurrentMineLayout());
            ConfigureCrusherForRound();

            Debug.Log(
                "[Minigame Solo Test] Round " +
                _session.RoundNumber + " ready.");
        }

        private void RebuildMines(Vector3[] positions)
        {
            for (var index = _mineRoot.childCount - 1; index >= 0; index--)
            {
                var child = _mineRoot.GetChild(index);
                var mine = child.GetComponent<MinefieldMine>();
                if (mine != null)
                {
                    mine.ArmForRoundAuthoritatively(false);
                }

                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }

            for (var index = 0; index < positions.Length; index++)
            {
                var mineObject = new GameObject("Mine " + (index + 1));
                mineObject.transform.SetParent(_mineRoot, false);
                mineObject.transform.position = positions[index];

                var trigger = mineObject.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0f, 0.1f, 0f);
                trigger.radius = MineTriggerRadius;

                var mine = mineObject.AddComponent<MinefieldMine>();
                mine.ConfigureRegistry(_mineRegistry);
                mine.SetAutoResolveContacts(true);

                var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.name = "Sonar Revealed Visual";
                visual.transform.SetParent(mineObject.transform, false);
                visual.transform.localPosition =
                    new Vector3(0f, 0.08f, 0f);
                visual.transform.localScale =
                    new Vector3(0.7f, 0.14f, 0.7f);
                RemoveCollider(visual);
                ApplySurfaceColor(
                    visual.GetComponent<Renderer>(),
                    new Color(1f, 0.12f, 0.06f, 1f),
                    Color.red * 2f);
                visual.SetActive(false);
                mine.ConfigureVisuals(null, visual);
            }
        }

        private void ConfigureCrusherForRound()
        {
            var height = _crusher.transform.position.y;
            var start = new Vector3(
                NetworkMinefieldState.ArenaCenterX,
                height,
                NetworkMinefieldState.ArenaMinZ - CrusherStartOffset);
            var end = new Vector3(
                NetworkMinefieldState.ArenaCenterX,
                height,
                NetworkMinefieldState.ArenaMaxZ + CrusherEndOffset);
            _crusher.ConfigurePath(start, end);
            _crusher.SetSweepSpeed(
                Vector3.Distance(start, end) /
                (float)NetworkMinefieldState.RunSeconds);
            _crusher.ResetForRoundAuthoritatively();
        }

        private void ApplySessionTransition(
            MinefieldSoloPhase previousPhase,
            int previousRound)
        {
            if (previousPhase == _session.Phase &&
                previousRound == _session.RoundNumber)
            {
                return;
            }

            switch (_session.Phase)
            {
                case MinefieldSoloPhase.Countdown:
                    BuildCurrentRound();
                    break;
                case MinefieldSoloPhase.Running:
                    BeginRunning();
                    break;
                case MinefieldSoloPhase.RoundResult:
                    EnterRoundResult();
                    break;
                case MinefieldSoloPhase.Complete:
                    EnterComplete();
                    break;
            }
        }

        private void BeginRunning()
        {
            _playerActor.SetHazardsEnabledAuthoritatively(true);
            _playerMotor.SetMovementEnabled(true);
            _sonar.SetSonarEnabled(true);
            _crusher.BeginSweepAuthoritatively();
        }

        private void EnterRoundResult()
        {
            if (_playerActor.CanMove)
            {
                _playerActor.ApplyAuthoritativeElimination(
                    MinefieldEliminationCause.RoundTimeout,
                    gameObject);
            }

            _playerMotor.SetMovementEnabled(false);
            _sonar.SetSonarEnabled(false);
            _crusher.StopSweepAuthoritatively();
        }

        private void EnterComplete()
        {
            _playerMotor.SetMovementEnabled(false);
            _sonar.SetSonarEnabled(false);
            _crusher.StopSweepAuthoritatively();
            Debug.Log(
                "[Minigame Solo Test] Minefield complete. Cleared " +
                _session.ClearedRoundCount + " / " +
                MinefieldRules.RoundCount + " rounds.");
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

        private void StopSoloTest()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void HandleSonarResolved(
            MinefieldSonar _,
            MinefieldSonarResult result)
        {
            _sonarPulseUntil = Time.unscaledTime + SonarVisualSeconds;
        }

        private string GetPhaseLabel()
        {
            switch (_session.Phase)
            {
                case MinefieldSoloPhase.Countdown:
                    return "START IN";
                case MinefieldSoloPhase.Running:
                    return "RUNNING";
                case MinefieldSoloPhase.RoundResult:
                    return _session.WasRoundCleared(_session.RoundNumber)
                        ? "CLEARED"
                        : "FAILED";
                case MinefieldSoloPhase.Complete:
                    return "COMPLETE";
                default:
                    return _session.Phase.ToString().ToUpperInvariant();
            }
        }

        private string GetPlayerStateLabel()
        {
            return _playerActor != null
                ? _playerActor.State.ToString().ToUpperInvariant()
                : "MISSING";
        }

        private static Vector3 GetRunnerStartPosition()
        {
            return new Vector3(
                NetworkMinefieldState.ArenaCenterX,
                0f,
                RunnerStartZ);
        }

        private static string FormatClock(float seconds)
        {
            var safeSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (safeSeconds / 60).ToString("00") + ":" +
                   (safeSeconds % 60).ToString("00");
        }

        private static void DisableGeneratedHitColliders(
            GameObject player,
            CharacterController character)
        {
            var colliders = player.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                if (colliders[index] != character)
                {
                    colliders[index].enabled = false;
                }
            }
        }

        private static void RemoveCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private static void ApplySurfaceColor(
            Renderer renderer,
            Color color,
            Color emission = default)
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
            properties.SetColor("_EmissionColor", emission);
            renderer.SetPropertyBlock(properties);
        }

        private static Transform FindDescendant(
            Transform root,
            string objectName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == objectName)
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(root.GetChild(index), objectName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}

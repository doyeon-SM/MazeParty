using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.Flight;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class FlightNetworkView : MonoBehaviour
    {
        [SerializeField] private NetworkFlightState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private GameObject arenaPresentation, obstaclePrefab, explosionPrefab;
        [SerializeField] private Material skyboxMaterial;
        [SerializeField] private Transform playerRoot, obstacleRoot;
        [SerializeField] private MinigameCommonHudView practiceHud;
        [SerializeField] private MinigameStartCountdownView practiceCountdown;
        private double _courseDuration;
        private readonly PlayerAvatarVisual[] _players = new PlayerAvatarVisual[4];
        private readonly FlightAvatarPresentation[] _flightVisuals = new FlightAvatarPresentation[4];
        private readonly Transform[,] _obstacles = new Transform[FlightRules.GatePoolCount, 2];
        private readonly bool[] _lastAlive = new bool[4];
        private readonly double[] _heights = new double[4], _velocities = new double[4];
        private readonly int[] _flaps = new int[4];
        private GameplayCameraDirector _director;
        private FlightCourse _course;
        private ulong _seed;
        private bool _practice, _observed;
        private Camera _output;
        private Rect _savedRect;
        private bool _savedOrthographic;
        private float _savedOrthographicSize;
        private Skybox _skybox;
        private Material _savedSky;
        private bool _savedSkyEnabled, _addedSky;
        private CameraClearFlags _savedClearFlags;
        public static Vector3 Origin => new Vector3(1500, 20, 0);
        public static Vector3 CameraPosition => Origin + new Vector3((float)FlightRules.CameraOffsetX, 0, -30);
        public CinemachineCamera SharedCamera => sharedCamera;
        public bool HasRequiredReferences => state != null && sharedCamera != null && arenaPresentation != null &&
            obstaclePrefab != null && explosionPrefab != null && skyboxMaterial != null && playerRoot != null &&
            obstacleRoot != null && practiceHud != null && practiceHud.HasRequiredReferences &&
            practiceCountdown != null && practiceCountdown.HasRequiredReferences;
        public void Configure(NetworkFlightState authority, CinemachineCamera camera, GameObject environment, Transform players,
            Transform obstacles, GameObject obstacle, GameObject explosion, Material sky, MinigameCommonHudView hud, MinigameStartCountdownView countdown)
        {
            state = authority; sharedCamera = camera; arenaPresentation = environment; playerRoot = players; obstacleRoot = obstacles;
            obstaclePrefab = obstacle; explosionPrefab = explosion; skyboxMaterial = sky; practiceHud = hud; practiceCountdown = countdown;
        }
        private void Awake()
        {
            if (practiceHud != null) practiceHud.enabled = false;
            if (practiceCountdown != null) practiceCountdown.enabled = false;
            SetVisible(false);
        }
        private void OnDisable()
        {
            SetVisible(false); RestoreOutputCamera();
            if (_director != null) _director.ClearMinigameCamera(sharedCamera);
        }
        public void ResetPracticePresentation()
        {
            _observed = false; _course = null;
            practiceCountdown.SetCountdown(0, false, false);
        }
        private void LateUpdate()
        {
            if (_practice) return;
            var match = NetworkMatchState.Instance;
            var visible = match != null && state != null && state.IsSpawned && match.CurrentMinigame == ScheduledMinigameId.Flight &&
                (match.FlowState == BoardFlowState.MinigamePlaying || match.FlowState == BoardFlowState.MinigameResult);
            SetVisible(visible);
            if (!visible)
            {
                _observed = false; RestoreOutputCamera();
                if (_director != null) _director.ClearMinigameCamera(sharedCamera);
                return;
            }
            var local = -1;
            for (var i = 0; i < 4; i++)
            {
                var avatar = match.GetAvatarForSlot(i);
                if (avatar != null && avatar.IsOwner)
                { local = i; avatar.GetLocalFlightPose(state, out _heights[i], out _velocities[i], out _flaps[i]); }
                else { state.GetPresentation(i, out _heights[i], out _velocities[i]); _flaps[i] = state.GetPlayer(i).Flaps; }
            }
            Render(state.Seed, state.RunElapsed, state.IsAlive, local, match, state.Phase == FlightPhase.Countdown && state.Remaining > 2);
            _director ??= FindAnyObjectByType<GameplayCameraDirector>();
            _director?.SetMinigameCamera(sharedCamera);
            BindOutputCamera(_director != null ? _director.OutputCamera : Camera.main);
        }
        public void RenderPractice(FlightMatchState simulation, Camera output)
        {
            _practice = true; SetVisible(true);
            practiceCountdown.SetCountdown(Mathf.Clamp((int)Math.Ceiling(simulation.Remaining), 1, 3), simulation.Phase == FlightPhase.Countdown);
            practiceHud.ShowPracticeTimeOnly(simulation.Phase == FlightPhase.Countdown ? FlightRules.RunSeconds :
                simulation.Phase == FlightPhase.Running ? simulation.Remaining : 0, FlightRules.RunSeconds);
            for (var i = 0; i < 4; i++) { _heights[i] = simulation.Height(i); _velocities[i] = simulation.Velocity(i); _flaps[i] = simulation.Flaps(i); }
            Render(simulation.Seed, simulation.RunElapsed, simulation.IsAlive, 0, null,
                simulation.Phase == FlightPhase.Countdown && simulation.Remaining > 2);
            output.orthographic = true; output.orthographicSize = (float)FlightRules.ViewHalfHeight;
            output.transform.SetPositionAndRotation(CameraPosition, Quaternion.identity);
            BindOutputCamera(output);
        }
        private void Render(ulong seed, double elapsed, Func<int, bool> alive, int local, NetworkMatchState match, bool showName)
        {
            var duration = MinigameRulesConfig.FromMatch(ScheduledMinigameId.Flight, match?.GameSettings).Seconds;
            if (_course == null || _seed != seed || _courseDuration != duration) { _course = new FlightCourse(seed, duration); _courseDuration = duration; _seed = seed; _observed = false; }
            EnsureWorld(local);
            sharedCamera.transform.SetPositionAndRotation(CameraPosition, Quaternion.identity);
            for (var i = 0; i < 4; i++)
            {
                var visual = _players[i]; var avatar = match != null ? match.GetAvatarForSlot(i) : null;
                // Depth separation affects only rendering; all players use the same collision plane/course.
                visual.transform.SetPositionAndRotation(Origin + new Vector3(0, (float)_heights[i], i == local ? -.8f : .1f * i),
                    Quaternion.AngleAxis((float)_velocities[i] * 2, Vector3.forward) * Quaternion.Euler(0, 90, 0));
                visual.SetDisplayName(avatar != null ? avatar.DisplayName : GameText.F("Player {0}", i + 1));
                visual.SetNameplateVisible(showName && i == local);
                _flightVisuals[i].SetGhost(i != local);
                if (avatar != null)
                {
                    var appearance = avatar.Appearance; visual.SetBodyColor(appearance.BodyColor);
                    visual.ApplyAppearance(appearance.EyeId, appearance.MouthId, appearance.HatId, appearance.ExpressionId);
                }
                _flightVisuals[i].SetFlaps(_flaps[i]);
                if (_observed && _lastAlive[i] && !alive(i))
                {
                    // Remote deaths must not obscure the local player's flight path.
                    if (i == local)
                        OneShotVfxPool.Play(explosionPrefab, visual.transform.position, Quaternion.identity, 1f);
                    GameSound.Play(SoundKeys.MinigameFlightExplosion, .75f);
                }
                visual.gameObject.SetActive(alive(i)); _lastAlive[i] = alive(i);
            }
            for (var i = 0; i < FlightRules.GatePoolCount; i++)
            {
                _course.PooledGate(i, elapsed, out var sequence, out var x);
                var gate = _course[Math.Min(sequence, _course.Count - 1)];
                SetColumn(_obstacles[i, 0], (float)x, (float)gate.Lower, false);
                SetColumn(_obstacles[i, 1], (float)x, (float)gate.Upper, true);
            }
            _observed = true;
        }
        private static void SetColumn(Transform column, float x, float edge, bool top)
        {
            column.SetPositionAndRotation(Origin + new Vector3(x, edge, 0), Quaternion.Euler(0, 0, top ? 0 : 180));
            column.localScale = new Vector3(1, top ? 12 - edge : 12 + edge, 1);
        }
        private void EnsureWorld(int local)
        {
            var colors = new[] { new Color(.16f, .48f, .95f), new Color(.92f, .2f, .16f), new Color(.18f, .78f, .32f), new Color(.7f, .26f, .9f) };
            for (var i = 0; i < 4; i++) if (_players[i] == null)
            {
                var root = new GameObject("Flight Player " + (i + 1)); root.transform.SetParent(playerRoot, false);
                var visual = root.AddComponent<PlayerAvatarVisual>(); visual.EnsureBuilt(); visual.SetBodyColor(colors[i]);
                visual.SetOwnerFirstPerson(false);
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                _players[i] = visual; _flightVisuals[i] = root.AddComponent<FlightAvatarPresentation>();
                _flightVisuals[i].Initialize(visual, i != local);
            }
            for (var i = 0; i < FlightRules.GatePoolCount; i++)
                for (var side = 0; side < 2; side++) if (_obstacles[i, side] == null)
                {
                    var column = Instantiate(obstaclePrefab, obstacleRoot);
                    column.name = "Flight Obstacle " + i + (side == 0 ? " Lower" : " Upper");
                    _obstacles[i, side] = column.transform;
                }
        }
        private void SetVisible(bool visible)
        {
            if (arenaPresentation != null) arenaPresentation.SetActive(visible);
            if (playerRoot != null) playerRoot.gameObject.SetActive(visible);
            if (obstacleRoot != null) obstacleRoot.gameObject.SetActive(visible);
            if (practiceHud != null) practiceHud.gameObject.SetActive(visible && _practice);
            if (!visible && practiceCountdown != null) practiceCountdown.SetCountdown(0, false, false);
        }
        // A fixed logical viewport keeps collision boundaries and the 3-ahead/5-total preview
        // identical on ultrawide and narrow displays. Restore the board camera when leaving.
        private void BindOutputCamera(Camera camera)
        {
            if (camera == null) return;
            if (_output != camera)
            {
                RestoreOutputCamera(); _output = camera; _savedRect = camera.rect; _savedClearFlags = camera.clearFlags;
                _savedOrthographic = camera.orthographic; _savedOrthographicSize = camera.orthographicSize;
                _skybox = camera.GetComponent<Skybox>(); _addedSky = _skybox == null;
                if (_addedSky) _skybox = camera.gameObject.AddComponent<Skybox>();
                _savedSky = _skybox.material; _savedSkyEnabled = _skybox.enabled;
                _skybox.material = skyboxMaterial; _skybox.enabled = true; camera.clearFlags = CameraClearFlags.Skybox;
            }
            // The board brain intentionally disables lens-mode overrides. Switch its actual
            // output as well, then restore it on exit; the Flight rig supplies the fixed lens.
            camera.orthographic = true;
            camera.orthographicSize = (float)FlightRules.ViewHalfHeight;
            var width = camera.targetTexture != null ? camera.targetTexture.width : Screen.width;
            var height = camera.targetTexture != null ? camera.targetTexture.height : Screen.height;
            var scale = (float)width / Math.Max(1, height) / (16f / 9f);
            camera.rect = scale > 1 ? new Rect((1 - 1 / scale) / 2, 0, 1 / scale, 1) : new Rect(0, (1 - scale) / 2, 1, scale);
        }
        private void RestoreOutputCamera()
        {
            if (_output != null)
            {
                _output.rect = _savedRect; _output.clearFlags = _savedClearFlags;
                _output.orthographic = _savedOrthographic; _output.orthographicSize = _savedOrthographicSize;
            }
            if (_skybox != null)
            {
                if (_addedSky) Destroy(_skybox);
                else { _skybox.material = _savedSky; _skybox.enabled = _savedSkyEnabled; }
            }
            _skybox = null; _output = null;
        }
    }
}

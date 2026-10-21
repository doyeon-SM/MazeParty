using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.Race2;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class Race2NetworkView : MonoBehaviour
    {
        [SerializeField] private NetworkRace2State state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private GameObject arenaPresentation, boxPrefab, explosionPrefab;
        [SerializeField] private Transform playerRoot, boxRoot;
        [SerializeField] private MinigameCommonHudView practiceHud;
        [SerializeField] private MinigameStartCountdownView practiceCountdown;
        private double _courseDuration;
        private readonly PlayerAvatarVisual[] _players = new PlayerAvatarVisual[4];
        private readonly Transform[,] _boxes = new Transform[4, Race2Rules.BoxCount];
        private readonly bool[] _lastAlive = new bool[4];
        private GameplayCameraDirector _director;
        private Race2BoxStream _stream;
        private ulong _seed;
        private int _round, _watching = -1;
        private bool _practice, _observed;
        public int SpectatedSlot { get; private set; } = -1;
        public int WatchedSlot => _watching;
        public CinemachineCamera SharedCamera => sharedCamera;
        public Vector3 CameraPosition => sharedCamera.transform.position;
        public Quaternion CameraRotation => sharedCamera.transform.rotation;
        public float CameraFieldOfView => sharedCamera.Lens.FieldOfView;
        public bool HasRequiredReferences => state != null && sharedCamera != null && arenaPresentation != null &&
            boxPrefab != null && explosionPrefab != null && playerRoot != null && boxRoot != null &&
            practiceHud != null && practiceHud.HasRequiredReferences && practiceCountdown != null && practiceCountdown.HasRequiredReferences;
        public void Configure(NetworkRace2State authority, CinemachineCamera camera, GameObject arena, Transform players,
            Transform boxes, GameObject box, GameObject explosion, MinigameCommonHudView hud, MinigameStartCountdownView countdown)
        {
            state = authority; sharedCamera = camera; arenaPresentation = arena; playerRoot = players; boxRoot = boxes;
            boxPrefab = box; explosionPrefab = explosion; practiceHud = hud; practiceCountdown = countdown;
        }
        public static Vector3 PlayerPosition(int slot, byte lane) => new Vector3(
            NetworkRaceState.TrackCenterX + (slot - 1.5f) * 8f + (lane == 0 ? -1.2f : 1.2f), RaceNetworkView.PlayerPresentationHeight, -10f);
        public const float PlayerCameraFieldOfView = 50f;
        public static Quaternion PlayerCameraRotation => Quaternion.Euler(28f, 0f, 0f);
        public static Vector3 CalculatePlayerCameraPosition(int slot) =>
            (PlayerPosition(slot, 0) + PlayerPosition(slot, 1)) * .5f +
            Vector3.up * 5.5f + Vector3.back * 9f;
        private void Awake()
        {
            if (practiceHud != null) practiceHud.enabled = false;
            if (practiceCountdown != null) practiceCountdown.enabled = false;
            SetVisible(false);
        }
        private void OnDisable()
        {
            SetVisible(false);
            if (_director != null) _director.ClearMinigameCamera(sharedCamera);
        }
        public void ResetPracticePresentation()
        {
            _observed = false; _watching = -1; _round = 0;
            practiceCountdown.SetCountdown(0, false, false);
        }
        private void LateUpdate()
        {
            if (_practice) return;
            var match = NetworkMatchState.Instance;
            var visible = match != null && state != null && state.IsSpawned && match.CurrentMinigame == ScheduledMinigameId.Race2 &&
                (match.FlowState == BoardFlowState.MinigamePlaying || match.FlowState == BoardFlowState.MinigameResult);
            SetVisible(visible);
            if (!visible) { _observed = false; if (_director != null) _director.ClearMinigameCamera(sharedCamera); return; }
            var local = -1;
            for (var i = 0; i < 4; i++) if (match.GetAvatarForSlot(i) != null && match.GetAvatarForSlot(i).IsOwner) local = i;
            Render(state.Seed, state.RoundNumber, state.Phase, state.RoundElapsed, state.IsAlive,
                i => i == local && state.Phase == Race2Phase.Running && !state.IsPaused ?
                    match.GetAvatarForSlot(i).GetLocalRace2Lane(state) : state.GetLane(i), local, match);
            _director ??= FindAnyObjectByType<GameplayCameraDirector>();
            _director?.SetMinigameCamera(sharedCamera);
        }
        public void RenderPractice(Race2MatchState simulation)
        {
            _practice = true; SetVisible(true);
            practiceCountdown.SetCountdown(Mathf.Clamp((int)Math.Ceiling(simulation.Remaining), 1, 3), simulation.Phase == Race2Phase.Countdown);
            var running = simulation.Phase == Race2Phase.Running;
            practiceHud.ShowPracticeClock(running ? simulation.Remaining : simulation.Phase == Race2Phase.Countdown ? Race2Rules.RoundSeconds : 0d,
                Race2Rules.RoundSeconds, simulation.RoundNumber, Race2Rules.RoundCount);
            Render(simulation.Seed, simulation.RoundNumber, simulation.Phase, simulation.RoundElapsed,
                simulation.IsAlive, simulation.GetLane, 0, null);
        }
        private void Render(ulong seed, int round, Race2Phase phase, double elapsed, Func<int,bool> alive, Func<int,byte> lane,
            int local, NetworkMatchState match)
        {
            EnsureWorld();
            var duration = MinigameRulesConfig.FromMatch(ScheduledMinigameId.Race2, match?.GameSettings).Seconds;
            var newRound = _round != round || _seed != seed || _stream == null || _courseDuration != duration;
            if (newRound) { _stream = new Race2BoxStream(seed, round, duration); _courseDuration = duration; _round = round; _seed = seed; _observed = false; _watching = local; }
            // A close rear perspective keeps the avatar's back and the approaching stream in view.
            var lens = sharedCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = PlayerCameraFieldOfView;
            lens.NearClipPlane = .1f; lens.FarClipPlane = 500f;
            sharedCamera.Lens = lens;
            var oldWatching = _watching;
            for (var i = 0; i < 4; i++)
            {
                var avatar = match != null ? match.GetAvatarForSlot(i) : null; var visual = _players[i];
                visual.transform.SetPositionAndRotation(PlayerPosition(i, lane(i)), Quaternion.identity);
                visual.SetDisplayName(avatar != null ? avatar.DisplayName : GameText.F("Player {0}", i + 1));
                if (avatar != null)
                {
                    var appearance = avatar.Appearance; visual.SetBodyColor(appearance.BodyColor);
                    visual.ApplyAppearance(appearance.EyeId, appearance.MouthId, appearance.HatId, appearance.ExpressionId);
                }
                if (_observed && _lastAlive[i] && !alive(i))
                {
                    OneShotVfxPool.Play(explosionPrefab, visual.transform.position, Quaternion.identity, 1f);
                    if (i == oldWatching) GameSound.Play(SoundKeys.MinigameCoinBomb);
                }
                visual.gameObject.SetActive(alive(i)); _lastAlive[i] = alive(i);
                for (var b = 0; b < Race2Rules.BoxCount; b++)
                {
                    _stream.Box(b, elapsed, out var sequence, out var z);
                    var position = PlayerPosition(i, _stream.Lane(sequence)); position.y = .59f; position.z += (float)z;
                    _boxes[i,b].position = position;
                    _boxes[i,b].gameObject.SetActive(phase != Race2Phase.Inactive && (phase != Race2Phase.Countdown || b < Race2Rules.PreviewBoxCount));
                }
            }
            if (alive(local)) _watching = local;
            else if (_watching < 0 || !alive(_watching))
            {
                var next = Race2Rules.NextSpectator(_watching >= 0 ? _watching : local, alive);
                if (next >= 0) _watching = next;
            }
            SpectatedSlot = _watching >= 0 && _watching != local && alive(_watching) ? _watching : -1;
            if (_watching >= 0)
                sharedCamera.transform.SetPositionAndRotation(CalculatePlayerCameraPosition(_watching), PlayerCameraRotation);
            _observed = true;
        }
        private void SetVisible(bool visible)
        {
            if (arenaPresentation != null) arenaPresentation.SetActive(visible);
            if (playerRoot != null) playerRoot.gameObject.SetActive(visible);
            if (boxRoot != null) boxRoot.gameObject.SetActive(visible);
            if (practiceHud != null) practiceHud.gameObject.SetActive(visible && _practice);
            if (!visible && practiceCountdown != null) practiceCountdown.SetCountdown(0, false, false);
        }
        private void EnsureWorld()
        {
            var colors = new[] { new Color(.16f,.48f,.95f), new Color(.92f,.2f,.16f), new Color(.18f,.78f,.32f), new Color(.7f,.26f,.9f) };
            for (var i = 0; i < 4; i++)
            {
                if (_players[i] == null)
                {
                    var root = new GameObject("Race 2 Player " + (i + 1)); root.transform.SetParent(playerRoot, false);
                    var visual = root.AddComponent<PlayerAvatarVisual>(); visual.EnsureBuilt(); visual.SetBodyColor(colors[i]);
                    visual.SetOwnerFirstPerson(false); visual.SetNameplateVisible(true);
                    foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                    _players[i] = visual;
                }
                for (var b = 0; b < Race2Rules.BoxCount; b++) if (_boxes[i,b] == null)
                {
                    var box = Instantiate(boxPrefab, boxRoot); box.name = "Race 2 Box " + (i + 1) + "-" + b;
                    _boxes[i,b] = box.transform;
                }
            }
        }
    }
}

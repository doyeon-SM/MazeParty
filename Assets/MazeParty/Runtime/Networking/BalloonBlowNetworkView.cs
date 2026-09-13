using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.BalloonBlow;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Client-only presentation for the server-authoritative Balloon Blow
    /// state. Every client registers the same fixed Cinemachine view, while
    /// only the owning player's station receives the top-view highlight.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BalloonBlowNetworkView : MonoBehaviour
    {
        public const float PlayerPresentationHeight = 1.05f;
        public const float SharedCameraOrthographicSize = 7.8f;

        private const float BalloonMinimumScale = 0.22f;
        private const float BalloonMaximumScale = 1.7f;
        private const float BalloonScaleSpeed = 12f;

        private static readonly Color[] FallbackPlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        [SerializeField] private NetworkBalloonBlowState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform[] playerAnchors =
            new Transform[BalloonBlowRules.PlayerCount];
        [SerializeField] private Transform[] balloonAnchors =
            new Transform[BalloonBlowRules.PlayerCount];
        [SerializeField] private BalloonBlowStationLabel[] stationLabels =
            new BalloonBlowStationLabel[BalloonBlowRules.PlayerCount];
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private AudioSource cueAudioSource;
        [SerializeField] private BalloonBlowHudBindings hud;

        private readonly PlayerView[] _players =
            new PlayerView[BalloonBlowRules.PlayerCount];
        private readonly BalloonView[] _balloons =
            new BalloonView[BalloonBlowRules.PlayerCount];
        private readonly bool[] _wasPopped =
            new bool[BalloonBlowRules.PlayerCount];

        private GameplayCameraDirector _cameraDirector;
        private bool _cameraConfigured;
        private bool _worldVisible;
        private bool _worldVisibilityInitialized;
        private int _localSlot = -1;

        public static Vector3 SharedCameraPosition =>
            new Vector3(0f, 11.5f, -16f);

        public static Quaternion SharedCameraRotation =>
            Quaternion.Euler(36f, 0f, 0f);

        public void Configure(
            NetworkBalloonBlowState networkState,
            CinemachineCamera camera,
            Transform runtimePlayers,
            Transform[] fixedPlayerAnchors,
            Transform[] fixedBalloonAnchors,
            BalloonBlowStationLabel[] fixedStationLabels,
            GameObject arena,
            AudioSource audioSource,
            BalloonBlowHudBindings hudBindings)
        {
            state = networkState;
            sharedCamera = camera;
            playerRoot = runtimePlayers;
            playerAnchors = fixedPlayerAnchors;
            balloonAnchors = fixedBalloonAnchors;
            stationLabels = fixedStationLabels;
            arenaPresentation = arena;
            cueAudioSource = audioSource;
            hud = hudBindings;
            _cameraConfigured = false;
            ConfigureCamera();
            CacheBalloonViews();
            if (Application.isPlaying)
            {
                EnsurePlayers();
            }
        }

        private void Awake()
        {
            state ??= GetComponent<NetworkBalloonBlowState>();
            ConfigureCamera();
            CacheBalloonViews();
            EnsurePlayers();
            SetWorldPresentationActive(false);
            SetHudActive(false);
        }

        private void OnDisable()
        {
            SetWorldPresentationActive(false);
            SetHudActive(false);
            UnregisterCamera();
        }

        private void Update()
        {
            state ??= GetComponent<NetworkBalloonBlowState>();
            EnsurePlayers();
            CacheBalloonViews();

            var match = NetworkMatchState.Instance;
            var selected = match != null && match.IsBalloonBlowPhase;
            if (selected)
            {
                RegisterCamera();
            }
            else
            {
                UnregisterCamera();
            }

            var shouldShowWorld =
                state != null && state.IsSpawned && selected &&
                (match.FlowState == BoardFlowState.MinigamePlaying ||
                 match.FlowState == BoardFlowState.SkippedResult);
            var shouldShowHud = shouldShowWorld &&
                                match.FlowState ==
                                BoardFlowState.MinigamePlaying;
            SetHudActive(shouldShowHud);
            if (!shouldShowWorld)
            {
                SetWorldPresentationActive(false);
                return;
            }

            SetWorldPresentationActive(true);
            ResolveLocalSlot(match);
            RefreshPlayers(match);
            RefreshBalloons();
            RefreshStationLabels(match);
            RefreshHud(match);
        }

        private void ConfigureCamera()
        {
            if (sharedCamera == null)
            {
                return;
            }

            var lens = sharedCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = SharedCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 100f;
            sharedCamera.Lens = lens;
            sharedCamera.ForceCameraPosition(
                SharedCameraPosition,
                SharedCameraRotation);
            sharedCamera.Priority = 0;
            _cameraConfigured = true;
        }

        private void RegisterCamera()
        {
            _cameraDirector ??=
                FindAnyObjectByType<GameplayCameraDirector>();
            if (!_cameraConfigured)
            {
                ConfigureCamera();
            }
            if (_cameraDirector != null && sharedCamera != null)
            {
                _cameraDirector.SetMinigameCamera(sharedCamera);
            }
        }

        private void UnregisterCamera()
        {
            if (_cameraDirector != null && sharedCamera != null)
            {
                _cameraDirector.ClearMinigameCamera(sharedCamera);
            }
            if (sharedCamera != null)
            {
                sharedCamera.Priority = 0;
            }
        }

        private void CacheBalloonViews()
        {
            if (balloonAnchors == null)
            {
                return;
            }

            for (var slot = 0;
                 slot < _balloons.Length && slot < balloonAnchors.Length;
                 slot++)
            {
                if (_balloons[slot] != null ||
                    balloonAnchors[slot] == null)
                {
                    continue;
                }

                var body = balloonAnchors[slot].Find("Balloon Body");
                var knot = balloonAnchors[slot].Find("Balloon Knot");
                if (body != null && knot != null)
                {
                    _balloons[slot] = new BalloonView(
                        balloonAnchors[slot],
                        body,
                        knot);
                }
            }
        }

        private void EnsurePlayers()
        {
            if (playerRoot == null || playerAnchors == null)
            {
                return;
            }

            for (var slot = 0;
                 slot < _players.Length && slot < playerAnchors.Length;
                 slot++)
            {
                if (_players[slot] != null || playerAnchors[slot] == null)
                {
                    continue;
                }

                var playerObject = new GameObject(
                    "Balloon Player " + (slot + 1));
                playerObject.transform.SetParent(playerRoot, false);
                playerObject.transform.SetPositionAndRotation(
                    playerAnchors[slot].position,
                    playerAnchors[slot].rotation);

                var visual = playerObject.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.SetOwnerFirstPerson(false);
                visual.SetBodyColor(FallbackPlayerColors[slot]);
                visual.SetDisplayName("Player " + (slot + 1));
                DisableBuiltInNameplate(playerObject.transform);
                DisableGeneratedHitColliders(playerObject);

                _players[slot] = new PlayerView(
                    playerObject.transform,
                    visual);
            }
        }

        private void ResolveLocalSlot(NetworkMatchState match)
        {
            var resolved = -1;
            for (var slot = 0; slot < _players.Length; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar != null && avatar.IsOwner)
                {
                    resolved = slot;
                    break;
                }
            }
            if (_localSlot == resolved)
            {
                return;
            }

            _localSlot = resolved;
            for (var slot = 0; slot < _players.Length; slot++)
            {
                _players[slot]?.Visual.SetTopViewHighlight(
                    slot == _localSlot);
            }
        }

        private void RefreshPlayers(NetworkMatchState match)
        {
            for (var slot = 0; slot < _players.Length; slot++)
            {
                var player = _players[slot];
                if (player == null)
                {
                    continue;
                }

                if (playerAnchors != null &&
                    slot < playerAnchors.Length &&
                    playerAnchors[slot] != null)
                {
                    player.Root.SetPositionAndRotation(
                        playerAnchors[slot].position,
                        playerAnchors[slot].rotation);
                }

                var avatar = match.GetAvatarForSlot(slot);
                if (avatar != null)
                {
                    var appearance = avatar.Appearance;
                    player.Visual.SetBodyColor(appearance.BodyColor);
                    player.Visual.ApplyAppearance(
                        appearance.EyeId,
                        appearance.MouthId,
                        appearance.HatId);
                }
                player.Visual.SetEliminated(false);
            }
        }

        private void RefreshBalloons()
        {
            for (var slot = 0; slot < _balloons.Length; slot++)
            {
                var balloon = _balloons[slot];
                if (balloon == null)
                {
                    continue;
                }

                var popped = state.IsPlayerPopped(slot);
                var progress = Mathf.Clamp(
                    state.GetPlayerProgress(slot),
                    0f,
                    BalloonBlowRules.MaxProgressPercent);
                if (popped && !_wasPopped[slot])
                {
                    cueAudioSource?.Play();
                }
                _wasPopped[slot] = popped;

                var normalized = progress /
                                 BalloonBlowRules.MaxProgressPercent;
                var targetScale = Mathf.Lerp(
                    BalloonMinimumScale,
                    BalloonMaximumScale,
                    normalized);
                balloon.SetScale(targetScale, popped);
            }
        }

        private void RefreshStationLabels(NetworkMatchState match)
        {
            if (stationLabels == null)
            {
                return;
            }

            var outputCamera = Camera.main;
            for (var slot = 0;
                 slot < stationLabels.Length &&
                 slot < BalloonBlowRules.PlayerCount;
                 slot++)
            {
                var label = stationLabels[slot];
                if (label == null)
                {
                    continue;
                }

                var avatar = match.GetAvatarForSlot(slot);
                var playerName = avatar != null
                    ? avatar.DisplayName
                    : "Player " + (slot + 1);
                var color = avatar != null
                    ? avatar.Appearance.BodyColor
                    : FallbackPlayerColors[slot];
                if (playerAnchors != null &&
                    slot < playerAnchors.Length &&
                    playerAnchors[slot] != null)
                {
                    label.transform.position =
                        playerAnchors[slot].position +
                        Vector3.up * 3.2f;
                }
                label.SetContent(
                    playerName,
                    state.GetPlayerProgress(slot),
                    state.IsPlayerPopped(slot),
                    slot == _localSlot,
                    color);
                label.FaceCamera(outputCamera);
            }
        }

        private void RefreshHud(NetworkMatchState match)
        {
            if (hud == null || !hud.HasRequiredReferences)
            {
                return;
            }

            hud.PhaseText.text = GetPhaseLabel(state.Phase);
            hud.TimerText.text =
                MinigameDisplayFormatter.FormatClock(state.RemainingSeconds);
            hud.RoundText.text = "ROUND " + state.RoundNumber + " / " +
                                 MinigameCatalog.GetRoundCount(
                                     ScheduledMinigameId.BalloonBlow);
            hud.InstructionText.text = GetLocalInstruction();
            hud.PausePanel.SetActive(state.IsPaused);
            hud.ControlsPanel.SetActive(
                state.Phase == NetworkBalloonBlowPhase.Countdown ||
                state.Phase == NetworkBalloonBlowPhase.Running);
            var showResult =
                state.Phase == NetworkBalloonBlowPhase.RoundResult ||
                state.Phase == NetworkBalloonBlowPhase.Complete;
            hud.ResultPanel.SetActive(showResult);
            if (showResult)
            {
                hud.ResultText.text = GetResultLabel();
            }

            for (var slot = 0; slot < BalloonBlowRules.PlayerCount; slot++)
            {
                var progress = Mathf.Clamp(
                    state.GetPlayerProgress(slot),
                    0f,
                    BalloonBlowRules.MaxProgressPercent);
                var avatar = match.GetAvatarForSlot(slot);
                var playerName = avatar != null
                    ? avatar.DisplayName
                    : "PLAYER " + (slot + 1);
                hud.PlayerRows[slot].text =
                    playerName.ToUpperInvariant() + "  ·  " +
                    Mathf.RoundToInt(progress) + "%  ·  " +
                    GetPlayerStateLabel(slot);
                hud.PlayerProgressFills[slot].fillAmount =
                    progress / BalloonBlowRules.MaxProgressPercent;
                var rowColor = slot == _localSlot
                    ? new Color(1f, 0.88f, 0.25f, 1f)
                    : hud.GetDefaultPlayerRowColor(slot);
                hud.PlayerRows[slot].color = rowColor;
            }
        }

        private string GetLocalInstruction()
        {
            if (_localSlot < 0)
            {
                return "HOLD LEFT CLICK TO INFLATE";
            }

            switch (state.GetPlayerPhase(_localSlot))
            {
                case BalloonBlowPlayerPhase.Inflating:
                    return "INFLATING · RELEASE BEFORE 2.0 SECONDS";
                case BalloonBlowPlayerPhase.Cooldown:
                    return "RESTING · THE BALLOON IS SLOWLY SHRINKING";
                case BalloonBlowPlayerPhase.AwaitingRelease:
                    return "RELEASE LEFT CLICK TO REARM";
                case BalloonBlowPlayerPhase.Popped:
                    return "BALLOON POPPED · WAIT FOR THE ROUND";
                default:
                    return "HOLD LEFT CLICK TO INFLATE";
            }
        }

        private string GetPlayerStateLabel(int slot)
        {
            if (state.IsPlayerPopped(slot))
            {
                return MinigameDisplayFormatter.ToOrdinal(
                    state.GetPopOrder(slot));
            }
            if (state.IsPlayerInflating(slot))
            {
                return "INFLATING";
            }

            switch (state.GetPlayerPhase(slot))
            {
                case BalloonBlowPlayerPhase.Cooldown:
                    return "RESTING";
                case BalloonBlowPlayerPhase.AwaitingRelease:
                    return "RELEASE";
                default:
                    return "READY";
            }
        }

        private string GetResultLabel()
        {
            if (_localSlot < 0)
            {
                return state.Phase == NetworkBalloonBlowPhase.Complete
                    ? "MATCH COMPLETE"
                    : "ROUND COMPLETE";
            }

            if (state.Phase == NetworkBalloonBlowPhase.Complete)
            {
                return "MATCH " +
                       MinigameDisplayFormatter.ToOrdinal(
                           state.GetFinalRank(_localSlot)) +
                       "\n" + state.GetScore(_localSlot) + " POINTS";
            }
            return "ROUND " +
                   MinigameDisplayFormatter.ToOrdinal(
                       state.GetRoundRank(_localSlot)) +
                   "\n+" + state.GetRoundPoints(_localSlot) + " POINTS";
        }

        private static string GetPhaseLabel(NetworkBalloonBlowPhase phase)
        {
            switch (phase)
            {
                case NetworkBalloonBlowPhase.Countdown:
                    return "BALLOON BLOW · GET READY";
                case NetworkBalloonBlowPhase.Running:
                    return "BALLOON BLOW · INFLATE";
                case NetworkBalloonBlowPhase.RoundResult:
                    return "BALLOON BLOW · ROUND RESULT";
                case NetworkBalloonBlowPhase.Complete:
                    return "BALLOON BLOW · MATCH RESULT";
                default:
                    return "BALLOON BLOW";
            }
        }

        private void SetWorldPresentationActive(bool active)
        {
            if (_worldVisibilityInitialized &&
                _worldVisible == active)
            {
                return;
            }

            _worldVisibilityInitialized = true;
            _worldVisible = active;
            if (arenaPresentation != null)
            {
                arenaPresentation.SetActive(active);
            }
            if (playerRoot != null)
            {
                playerRoot.gameObject.SetActive(active);
            }
        }

        private void SetHudActive(bool active)
        {
            if (hud != null && hud.gameObject.activeSelf != active)
            {
                hud.gameObject.SetActive(active);
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

        private static void DisableBuiltInNameplate(Transform root)
        {
            var nameplate = FindDescendant(root, "NameplateAnchor");
            if (nameplate != null)
            {
                nameplate.gameObject.SetActive(false);
            }
        }

        private static Transform FindDescendant(
            Transform root,
            string childName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == childName)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(root.GetChild(index), childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private sealed class PlayerView
        {
            public PlayerView(Transform root, PlayerAvatarVisual visual)
            {
                Root = root;
                Visual = visual;
            }

            public Transform Root { get; }
            public PlayerAvatarVisual Visual { get; }
        }

        private sealed class BalloonView
        {
            private readonly Transform _root;
            private readonly Transform _body;
            private readonly Transform _knot;

            public BalloonView(
                Transform root,
                Transform body,
                Transform knot)
            {
                _root = root;
                _body = body;
                _knot = knot;
            }

            public void SetScale(float scale, bool popped)
            {
                var visible = !popped;
                _body.gameObject.SetActive(visible);
                _knot.gameObject.SetActive(visible);
                if (!visible)
                {
                    return;
                }

                var current = _root.localScale.x;
                var smoothed = Mathf.Lerp(
                    current,
                    scale,
                    1f - Mathf.Exp(
                        -BalloonScaleSpeed * Time.unscaledDeltaTime));
                _root.localScale = Vector3.one * smoothed;
            }
        }
    }
}

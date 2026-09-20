using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Fixed shared-camera presentation for the A/S/D sequence memory game.
    /// All Canvas content comes from the authored SequenceMemoryHud prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SequenceMemoryNetworkView : MonoBehaviour
    {
        public const float ArenaCenterX = 1140f;
        public const float SharedCameraOrthographicSize = 9.5f;

        private static readonly Color[] FallbackPlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        [SerializeField] private NetworkSequenceMemoryState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform[] playerAnchors =
            new Transform[SequenceMemoryRules.PlayerCount];
        [SerializeField] private Transform npcAnchor;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private AudioSource npcToneSource;
        [SerializeField] private AudioSource playerToneSource;
        [SerializeField] private AudioClip highTone;
        [SerializeField] private AudioClip middleTone;
        [SerializeField] private AudioClip lowTone;
        [SerializeField] private SequenceMemoryHudBindings hud;

        private readonly PlayerView[] _players =
            new PlayerView[SequenceMemoryRules.PlayerCount];
        private GameplayCameraDirector _cameraDirector;
        private NetworkSequenceMemoryState _subscribedState;
        private bool _cameraRegistered;
        private bool _visibilityInitialized;
        private bool _worldVisible;
        private int _localSlot = -1;

        public static Vector3 SharedCameraPosition =>
            new Vector3(ArenaCenterX, 12f, -14f);

        public static Quaternion SharedCameraRotation =>
            Quaternion.Euler(32f, 0f, 0f);

        private void Awake()
        {
            ResolveReferences();
            ConfigureCamera();
            EnsurePlayers();
            SetWorldPresentationActive(false);
            SetHudActive(false);
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureToneSubscription();
        }

        private void OnDisable()
        {
            UnsubscribeFromToneEvents();
            SetWorldPresentationActive(false);
            SetHudActive(false);
            UnregisterCamera();
            npcToneSource?.Stop();
            playerToneSource?.Stop();
        }

        private void Update()
        {
            ResolveReferences();
            EnsureToneSubscription();
            EnsurePlayers();

            var match = NetworkMatchState.Instance;
            var selected = match != null && match.IsSequenceMemoryPhase;
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
                UnregisterCamera();
                return;
            }

            SetWorldPresentationActive(true);
            RegisterCamera();
            ResolveLocalSlot(match);
            RefreshPlayers(match);
            RefreshHud(match);
        }

        private void ResolveReferences()
        {
            state ??= GetComponent<NetworkSequenceMemoryState>();
            sharedCamera ??=
                GetComponentInChildren<CinemachineCamera>(true);
            if (playerRoot == null)
            {
                playerRoot = FindDescendant(transform, "Runtime Players");
            }
            if (arenaPresentation == null)
            {
                var arena = FindDescendant(
                    transform,
                    "Arena Presentation");
                arenaPresentation = arena != null
                    ? arena.gameObject
                    : null;
            }
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
        }

        private void EnsurePlayers()
        {
            if (!Application.isPlaying || playerRoot == null ||
                playerAnchors == null)
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
                    "Sequence Memory Player " + (slot + 1));
                playerObject.transform.SetParent(playerRoot, false);
                playerObject.transform.SetPositionAndRotation(
                    playerAnchors[slot].position,
                    playerAnchors[slot].rotation);

                var visual = playerObject.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.SetOwnerFirstPerson(false);
                visual.SetTopViewHighlight(false);
                visual.SetBodyColor(FallbackPlayerColors[slot]);
                visual.SetDisplayName("PLAYER " + (slot + 1));
                visual.SetEliminated(false);

                var presentation = playerObject.AddComponent<
                    RedLightGreenLightPlayerPresentation>();
                presentation.ApplyState(0, false);
                DisableGeneratedHitColliders(playerObject);
                _players[slot] = new PlayerView(
                    playerObject.transform,
                    visual,
                    presentation);
            }
        }

        private void ResolveLocalSlot(NetworkMatchState match)
        {
            var resolved = -1;
            for (var slot = 0;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
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
                    player.Visual.SetDisplayName(
                        string.IsNullOrWhiteSpace(avatar.DisplayName)
                            ? "PLAYER " + (slot + 1)
                            : avatar.DisplayName);
                }

                var mistakeCount = state.GetMistakeCount(slot);
                var eliminated = state.IsPlayerEliminated(slot);
                if (mistakeCount != player.LastMistakeCount ||
                    eliminated != player.LastEliminated)
                {
                    player.Presentation.ApplyState(
                        mistakeCount,
                        eliminated);
                    player.LastMistakeCount = mistakeCount;
                    player.LastEliminated = eliminated;
                }
                player.Visual.SetTopViewHighlight(slot == _localSlot);
            }
        }

        private void RefreshHud(NetworkMatchState match)
        {
            if (hud == null || !hud.HasRequiredReferences)
            {
                return;
            }

            hud.NpcSequenceText.text = GetNpcSequenceLabel();

            for (var slot = 0;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                var name = avatar != null &&
                           !string.IsNullOrWhiteSpace(avatar.DisplayName)
                    ? avatar.DisplayName
                    : "PLAYER " + (slot + 1);
                hud.PlayerNameTexts[slot].text = name;
                var input = state.GetPlayerInput(slot);
                hud.PlayerInputTexts[slot].text =
                    string.IsNullOrEmpty(input) ? "—" : input;
                hud.PlayerStatusTexts[slot].text =
                    GetPlayerStatusLabel(slot);
                hud.PlayerRows[slot].color = slot == _localSlot
                    ? new Color(1f, 0.88f, 0.25f, 1f)
                    : hud.GetDefaultPlayerRowColor(slot);
            }
        }

        private string GetNpcSequenceLabel()
        {
            if (state.Phase ==
                NetworkSequenceMemoryPhase.AcceptingInput)
            {
                return "— HIDDEN —";
            }

            var visible = state.VisibleProblem;
            return string.IsNullOrEmpty(visible) ? "—" : visible;
        }

        private string GetPlayerStatusLabel(int slot)
        {
            if (state.IsPlayerEliminated(slot))
            {
                return "OUT · 2 MISSES";
            }
            if (state.GetMistakeCount(slot) > 0)
            {
                var turnStatus = state.GetPlayerTurnStatus(slot);
                if (turnStatus == SequenceMemoryPlayerTurnStatus.Correct)
                {
                    return "CORRECT · TORSO LOST";
                }
                if (turnStatus == SequenceMemoryPlayerTurnStatus.Failed)
                {
                    return "WRONG · TORSO LOST";
                }
            }

            switch (state.GetPlayerTurnStatus(slot))
            {
                case SequenceMemoryPlayerTurnStatus.Entering:
                    return "INPUTTING";
                case SequenceMemoryPlayerTurnStatus.Correct:
                    return "CORRECT · WAITING";
                case SequenceMemoryPlayerTurnStatus.Failed:
                    return "WRONG · LOCKED";
                case SequenceMemoryPlayerTurnStatus.LockedForMatchEnd:
                    return "SURVIVED";
                case SequenceMemoryPlayerTurnStatus.Eliminated:
                    return "OUT";
                default:
                    return "WATCHING";
            }
        }

        private void HandleToneRequested(
            SequenceMemoryInput input,
            bool fromNpc)
        {
            var clip = GetToneClip(input);
            var source = fromNpc ? npcToneSource : playerToneSource;
            if (source != null && clip != null)
            {
                source.PlayOneShot(clip);
            }
        }

        private AudioClip GetToneClip(SequenceMemoryInput input)
        {
            switch (input)
            {
                case SequenceMemoryInput.A:
                    return highTone;
                case SequenceMemoryInput.S:
                    return middleTone;
                default:
                    return lowTone;
            }
        }

        private void EnsureToneSubscription()
        {
            if (_subscribedState == state)
            {
                return;
            }

            UnsubscribeFromToneEvents();
            _subscribedState = state;
            if (_subscribedState != null)
            {
                _subscribedState.ToneRequested += HandleToneRequested;
            }
        }

        private void UnsubscribeFromToneEvents()
        {
            if (_subscribedState != null)
            {
                _subscribedState.ToneRequested -= HandleToneRequested;
                _subscribedState = null;
            }
        }

        private void RegisterCamera()
        {
            if (_cameraRegistered || sharedCamera == null)
            {
                return;
            }

            _cameraDirector ??= FindAnyObjectByType<GameplayCameraDirector>();
            if (_cameraDirector != null)
            {
                _cameraDirector.SetMinigameCamera(sharedCamera);
                _cameraRegistered = true;
            }
        }

        private void UnregisterCamera()
        {
            if (!_cameraRegistered)
            {
                return;
            }

            if (_cameraDirector != null && sharedCamera != null)
            {
                _cameraDirector.ClearMinigameCamera(sharedCamera);
            }
            if (sharedCamera != null)
            {
                sharedCamera.Priority = 0;
            }
            _cameraRegistered = false;
        }

        private void SetWorldPresentationActive(bool active)
        {
            if (_visibilityInitialized && _worldVisible == active)
            {
                return;
            }

            _visibilityInitialized = true;
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
            if (hud != null && hud.RootCanvas != null &&
                hud.RootCanvas.gameObject.activeSelf != active)
            {
                hud.RootCanvas.gameObject.SetActive(active);
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
            public PlayerView(
                Transform root,
                PlayerAvatarVisual visual,
                RedLightGreenLightPlayerPresentation presentation)
            {
                Root = root;
                Visual = visual;
                Presentation = presentation;
                LastMistakeCount = -1;
            }

            public Transform Root { get; }
            public PlayerAvatarVisual Visual { get; }
            public RedLightGreenLightPlayerPresentation Presentation {
                get;
            }
            public int LastMistakeCount { get; set; }
            public bool LastEliminated { get; set; }
        }
    }
}

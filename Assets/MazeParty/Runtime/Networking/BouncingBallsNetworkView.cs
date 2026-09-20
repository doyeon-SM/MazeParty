using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.BouncingBalls;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Shared top-down arena presentation. Scene-authored transforms and the
    /// BouncingBallsHud prefab are the sole visual sources; the network state
    /// supplies only positions, owners, timing and scores.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BouncingBallsNetworkView : MonoBehaviour
    {
        public const float ArenaCenterX = 1260f;
        public const float SharedCameraOrthographicSize = 11f;

        private static readonly Color NeutralBallColor =
            new Color(0.91f, 0.96f, 1f);
        private static readonly Color[] FallbackPlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };
        private static readonly int BaseColorProperty =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty =
            Shader.PropertyToID("_Color");

        [SerializeField] private NetworkBouncingBallsState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private Transform[] shieldTransforms =
            new Transform[BouncingBallsRules.PlayerCount];
        [SerializeField] private Transform[] ballTransforms =
            new Transform[BouncingBallsRules.BallCount];
        [SerializeField] private Renderer[] shieldRenderers =
            new Renderer[BouncingBallsRules.PlayerCount];
        [SerializeField] private Renderer[] ballRenderers =
            new Renderer[BouncingBallsRules.BallCount];
        [SerializeField] private BouncingBallsHudBindings hud;

        private GameplayCameraDirector _cameraDirector;
        private MaterialPropertyBlock _colorBlock;
        private bool _cameraRegistered;
        private bool _worldVisible;
        private bool _visibilityInitialized;
        private bool _hadVisibleFrame;

        public static Vector3 SharedCameraPosition =>
            new Vector3(ArenaCenterX, 0f, -20f);

        public static Quaternion SharedCameraRotation =>
            Quaternion.identity;

        public GameObject ArenaPresentation => arenaPresentation;
        public BouncingBallsHudBindings HudBindings => hud;

        public Transform GetShieldTransform(int slot)
        {
            return shieldTransforms != null && slot >= 0 &&
                slot < shieldTransforms.Length
                    ? shieldTransforms[slot]
                    : null;
        }

        public Transform GetBallTransform(int ballId)
        {
            return ballTransforms != null && ballId >= 0 &&
                ballId < ballTransforms.Length
                    ? ballTransforms[ballId]
                    : null;
        }

        public Renderer GetShieldRenderer(int slot)
        {
            return shieldRenderers != null && slot >= 0 &&
                slot < shieldRenderers.Length
                    ? shieldRenderers[slot]
                    : null;
        }

        public Renderer GetBallRenderer(int ballId)
        {
            return ballRenderers != null && ballId >= 0 &&
                ballId < ballRenderers.Length
                    ? ballRenderers[ballId]
                    : null;
        }

        private void Awake()
        {
            ResolveReferences();
            ConfigureCamera();
            _colorBlock = new MaterialPropertyBlock();
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
            ResolveReferences();
            var match = NetworkMatchState.Instance;
            var selected = match != null && match.IsBouncingBallsPhase;
            var shouldShowWorld = state != null && state.IsSpawned &&
                selected &&
                (match.FlowState == BoardFlowState.MinigamePlaying ||
                 match.FlowState == BoardFlowState.SkippedResult);
            var shouldShowHud = shouldShowWorld &&
                match.FlowState == BoardFlowState.MinigamePlaying;

            SetHudActive(shouldShowHud);
            if (!shouldShowWorld)
            {
                SetWorldPresentationActive(false);
                UnregisterCamera();
                return;
            }

            SetWorldPresentationActive(true);
            RegisterCamera();
            RefreshArena(match);
            RefreshHud(match);
            _hadVisibleFrame = true;
        }

        private void ResolveReferences()
        {
            state ??= GetComponent<NetworkBouncingBallsState>();
            sharedCamera ??=
                GetComponentInChildren<CinemachineCamera>(true);
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

        private void RefreshArena(NetworkMatchState match)
        {
            if (arenaPresentation == null)
            {
                return;
            }

            var origin = arenaPresentation.transform;
            var rail = (float)BouncingBallsRules.ShieldRailDistance;
            for (var slot = 0; slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                var center = state.GetShieldCenter(slot);
                Vector3 localPosition;
                switch (slot)
                {
                    case 0:
                        localPosition = new Vector3(center, -rail, 0.38f);
                        break;
                    case 1:
                        localPosition = new Vector3(rail, center, 0.38f);
                        break;
                    case 2:
                        localPosition = new Vector3(center, rail, 0.38f);
                        break;
                    default:
                        localPosition = new Vector3(-rail, center, 0.38f);
                        break;
                }

                if (shieldTransforms != null &&
                    slot < shieldTransforms.Length &&
                    shieldTransforms[slot] != null)
                {
                    ApplyPosition(
                        shieldTransforms[slot],
                        origin.TransformPoint(localPosition),
                        false);
                }
                if (shieldRenderers != null &&
                    slot < shieldRenderers.Length)
                {
                    SetRendererColor(
                        shieldRenderers[slot],
                        GetPlayerColor(match, slot));
                }
            }

            for (var ballId = 0; ballId < BouncingBallsRules.BallCount;
                 ballId++)
            {
                var xy = state.GetBallPosition(ballId);
                if (ballTransforms != null &&
                    ballId < ballTransforms.Length &&
                    ballTransforms[ballId] != null)
                {
                    ApplyPosition(
                        ballTransforms[ballId],
                        origin.TransformPoint(
                            new Vector3(xy.x, xy.y, 0.12f)),
                        true);
                }
                if (ballRenderers != null &&
                    ballId < ballRenderers.Length)
                {
                    var owner = state.GetBallOwner(ballId);
                    SetRendererColor(
                        ballRenderers[ballId],
                        BouncingBallsRules.IsValidPlayerSlot(owner)
                            ? GetPlayerColor(match, owner)
                            : NeutralBallColor);
                }
            }
        }

        private void ApplyPosition(
            Transform visual,
            Vector3 target,
            bool snapOnTeleport)
        {
            visual.position = !_hadVisibleFrame ||
                (snapOnTeleport &&
                 (visual.position - target).sqrMagnitude > 4f)
                ? target
                : Vector3.Lerp(
                    visual.position,
                    target,
                    Mathf.Clamp01(Time.unscaledDeltaTime * 25f));
        }

        private void RefreshHud(NetworkMatchState match)
        {
            if (hud == null || !hud.HasRequiredReferences)
            {
                return;
            }

            var remaining = state.Remaining;
            var duration = GetPhaseDuration(state.Phase);
            if (match.IsReconnectPaused)
            {
                remaining = match.ReconnectRemaining;
                duration = NetworkMatchState.ReconnectGraceSeconds;
            }
            hud.TimerDial.SetTime(remaining, duration);
            hud.RoundText.text = "ROUND " +
                Mathf.Clamp(
                    state.RoundNumber,
                    1,
                    BouncingBallsRules.RoundCount) +
                " / " + BouncingBallsRules.RoundCount;
            hud.PhaseText.text = match.IsReconnectPaused
                ? "PLAYER DISCONNECTED · MATCH PAUSED"
                : GetPhaseLabel(state.Phase);
            hud.InstructionText.text = match.IsReconnectPaused
                ? "Waiting up to 60 seconds for reconnection."
                : GetInstructionLabel(state.Phase);

            for (var slot = 0; slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                hud.PlayerNameTexts[slot].text = avatar != null &&
                    !string.IsNullOrWhiteSpace(avatar.DisplayName)
                        ? avatar.DisplayName
                        : "PLAYER " + (slot + 1);
                hud.PlayerNameTexts[slot].color =
                    GetPlayerColor(match, slot);
                var rank = state.GetFinalRank(slot);
                hud.PlayerScoreTexts[slot].text = rank > 0
                    ? MinigameDisplayFormatter.ToOrdinal(rank) +
                      " · SCORE " + state.GetScore(slot)
                    : "SCORE " + state.GetScore(slot);
                hud.PlayerConcededTexts[slot].text = rank > 0
                    ? "GOLD +" +
                      MinigameRewardRules.GetFinalPlacementGold(rank) +
                      " · CONCEDED " + state.GetConceded(slot)
                    : "CONCEDED " + state.GetConceded(slot);
            }
        }

        private static string GetPhaseLabel(
            NetworkBouncingBallsPhase phase)
        {
            switch (phase)
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

        private static string GetInstructionLabel(
            NetworkBouncingBallsPhase phase)
        {
            switch (phase)
            {
                case NetworkBouncingBallsPhase.Countdown:
                    return "A / D · MOVE YOUR SHIELD";
                case NetworkBouncingBallsPhase.Playing:
                    return "BLOCK BALLS TO CLAIM YOUR COLOR · SCORE IN A GOAL";
                case NetworkBouncingBallsPhase.RoundBreak:
                    return "ROUND 2 STARTS SOON · SCORES CARRY OVER";
                case NetworkBouncingBallsPhase.Complete:
                    return "FINAL STANDINGS · PLACEMENT AWARDS GOLD";
                default:
                    return string.Empty;
            }
        }

        private static double GetPhaseDuration(
            NetworkBouncingBallsPhase phase)
        {
            switch (phase)
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

        private static Color GetPlayerColor(
            NetworkMatchState match,
            int slot)
        {
            var avatar = match != null
                ? match.GetAvatarForSlot(slot)
                : null;
            return avatar != null
                ? avatar.Appearance.BodyColor
                : FallbackPlayerColors[slot];
        }

        private void SetRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            _colorBlock ??= new MaterialPropertyBlock();
            _colorBlock.Clear();
            _colorBlock.SetColor(BaseColorProperty, color);
            _colorBlock.SetColor(ColorProperty, color);
            renderer.SetPropertyBlock(_colorBlock);
        }

        private void RegisterCamera()
        {
            if (_cameraRegistered || sharedCamera == null)
            {
                return;
            }

            _cameraDirector ??= FindAnyObjectByType<
                GameplayCameraDirector>();
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
            if (!active)
            {
                _hadVisibleFrame = false;
            }
            if (arenaPresentation != null)
            {
                arenaPresentation.SetActive(active);
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
                var found = FindDescendant(
                    root.GetChild(index),
                    childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}

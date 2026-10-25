using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.OddOneOut;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class OddOneOutNetworkView : MonoBehaviour
    {
        [SerializeField] private NetworkOddOneOutState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform[] playerAnchors = new Transform[4];
        [SerializeField] private BouncingBallsHudBindings scoreHud;
        [SerializeField] private OddOneOutHudBindings actionHud;
        [SerializeField] private GameObject arrivalVfxPrefab;
        [SerializeField] private MinigameStartCountdownView practiceCountdown;
        private SoundHandle _resultSound;
        private readonly PlayerAvatarVisual[] _players = new PlayerAvatarVisual[4];
        private GameplayCameraDirector _cameraDirector;
        private bool _practice, _observed;
        private int _lastRound;
        private OddOneOutPhase _lastPhase;
        private static readonly Color[] Colors = { new Color(.16f,.48f,.95f), new Color(.92f,.2f,.16f),
            new Color(.18f,.78f,.32f), new Color(.7f,.26f,.9f) };
        public CinemachineCamera SharedCamera => sharedCamera;
        public Vector3 CameraPosition => sharedCamera.transform.position;
        public Quaternion CameraRotation => sharedCamera.transform.rotation;
        public float CameraSize => sharedCamera.Lens.OrthographicSize;
        public bool HasRequiredReferences => state != null && sharedCamera != null && arenaPresentation != null &&
            playerRoot != null && playerAnchors != null && playerAnchors.Length == 4 && Array.TrueForAll(playerAnchors, a => a != null) &&
            scoreHud != null && scoreHud.HasRequiredReferences && actionHud != null && actionHud.HasRequiredReferences && arrivalVfxPrefab != null && practiceCountdown != null && practiceCountdown.HasRequiredReferences;
        public void Configure(NetworkOddOneOutState authority, CinemachineCamera camera, GameObject arena,
            Transform players, Transform[] anchors, BouncingBallsHudBindings scores, OddOneOutHudBindings actions, GameObject vfx)
        {
            state = authority; sharedCamera = camera; arenaPresentation = arena; playerRoot = players;
            playerAnchors = anchors; scoreHud = scores; actionHud = actions; arrivalVfxPrefab = vfx;
        }
        public void ConfigurePracticeCountdown(MinigameStartCountdownView countdown) { practiceCountdown = countdown; }
        public void ResetPracticePresentation()
        {
            _observed = false;
            GameSound.Stop(_resultSound, 0.1f); _resultSound = default;
            practiceCountdown.SetCountdown(0, false, false);
        }
        private void Awake()
        {
            // Practice supplies this instance's time; online uses the Board's shared countdown.
            if (practiceCountdown != null) practiceCountdown.enabled = false;
            SetVisible(false);
        }
        private void OnDisable()
        {
            SetVisible(false);
            if (_cameraDirector != null) _cameraDirector.ClearMinigameCamera(sharedCamera);
        }
        private void LateUpdate()
        {
            if (_practice) return;
            var match = NetworkMatchState.Instance;
            var visible = state != null && state.IsSpawned && match != null && match.CurrentMinigame == ScheduledMinigameId.OddOneOut &&
                (match.FlowState == BoardFlowState.MinigamePlaying || match.FlowState == BoardFlowState.MinigameResult);
            SetVisible(visible);
            if (!visible)
            {
                _observed = false;
                if (_cameraDirector != null) _cameraDirector.ClearMinigameCamera(sharedCamera);
                return;
            }
            _cameraDirector ??= FindAnyObjectByType<GameplayCameraDirector>();
            _cameraDirector?.SetMinigameCamera(sharedCamera);
            var localSlot = -1;
            for (var i = 0; i < 4; i++) if (match.GetAvatarForSlot(i) != null && match.GetAvatarForSlot(i).IsOwner) localSlot = i;
            Render(state.Phase, state.RoundNumber, state.WinnerSlot, i => state.GetVisibleChoice(i, localSlot), state.GetScore,
                localSlot, !state.IsPaused && state.Remaining > 0d, state.Remaining, match);
        }
        public void RenderPractice(OddOneOutMatchState simulation)
        {
            _practice = true; SetVisible(true);
            // The online match uses the Board's shared HUD; standalone practice owns this authored instance.
            practiceCountdown.SetCountdown(Mathf.Clamp((int)Math.Ceiling(simulation.Remaining), 1, 3),
                simulation.Phase == OddOneOutPhase.Countdown && simulation.Remaining > 0d);
            Render(simulation.Phase, simulation.RoundNumber, simulation.WinnerSlot,
                i => simulation.GetVisibleChoice(i, 0), simulation.GetScore, 0, true, simulation.Remaining, null);
        }
        private void Render(OddOneOutPhase phase, int round, int winner, Func<int,bool> choice, Func<int,int> score,
            int localSlot, bool inputAllowed, double remaining, NetworkMatchState match)
        {
            EnsurePlayers();
            for (var slot = 0; slot < 4; slot++)
            {
                var visual = _players[slot]; var avatar = match != null ? match.GetAvatarForSlot(slot) : null;
                var name = avatar != null ? avatar.DisplayName : GameText.F("Player {0}", slot + 1);
                var color = avatar != null ? avatar.Appearance.BodyColor : Colors[slot];
                visual.transform.SetPositionAndRotation(playerAnchors[slot].position, playerAnchors[slot].rotation);
                visual.SetDisplayName(name); visual.SetBodyColor(color);
                if (avatar != null)
                {
                    var a = avatar.Appearance; visual.ApplyAppearance(a.EyeId, a.MouthId, a.HatId, a.ExpressionId);
                }
                visual.SetCrouchingImmediate(choice(slot));
                scoreHud.SetPlayer(slot, name, score(slot), color);
            }
            actionHud.SetSelectionTime(phase, remaining);
            actionHud.SetAction(phase == OddOneOutPhase.Choosing && localSlot >= 0 && inputAllowed,
                localSlot >= 0 && choice(localSlot));
            // Late joiners receive the current result without replaying a historical win effect.
            if (phase != OddOneOutPhase.Reveal && _lastPhase == OddOneOutPhase.Reveal)
            {
                GameSound.Stop(_resultSound, 0.1f); _resultSound = default;
            }
            if (_observed && phase == OddOneOutPhase.Reveal && (_lastRound != round || _lastPhase != phase) && winner >= 0)
            {
                OneShotVfxPool.Play(arrivalVfxPrefab, _players[winner].transform.position + Vector3.up * .65f, Quaternion.identity, 1.1f);
                GameSound.Stop(_resultSound, 0.1f);
                _resultSound = GameSound.Play(SoundKeys.MinigameFinish);
            }
            _observed = true; _lastRound = round; _lastPhase = phase;
        }
        private void SetVisible(bool value)
        {
            if (practiceCountdown != null)
            {
                practiceCountdown.gameObject.SetActive(value && _practice);
                if (!value) practiceCountdown.SetCountdown(0, false, false);
            }
            if (!value) { GameSound.Stop(_resultSound, 0.1f); _resultSound = default; }
            if (arenaPresentation != null) arenaPresentation.SetActive(value);
            if (playerRoot != null) playerRoot.gameObject.SetActive(value);
            if (scoreHud != null) scoreHud.RootCanvas.gameObject.SetActive(value);
            if (actionHud != null) actionHud.RootCanvas.gameObject.SetActive(value);
        }
        private void EnsurePlayers()
        {
            for (var i = 0; i < 4; i++)
            {
                if (_players[i] != null) continue;
                var root = new GameObject("Odd One Out Player " + (i + 1)); root.transform.SetParent(playerRoot, false);
                var visual = root.AddComponent<PlayerAvatarVisual>(); visual.EnsureBuilt();
                visual.SetOwnerFirstPerson(false); visual.SetNameplateVisible(true);
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                _players[i] = visual;
            }
        }
    }
}

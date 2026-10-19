using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    /// <summary>Authored ME / P1-P4 markers shared with the board camera.</summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class MinigameLocalPlayerHighlight : MonoBehaviour
    {
        [SerializeField] private BoardLocalPlayerMarkerView markers;
        private readonly Transform[] _targets = new Transform[4];
        private readonly PlayerAvatarVisual[] _visuals = new PlayerAvatarVisual[4];
        private Scene _scene;
        private float _nextResolve;
        public bool HasRequiredReferences => markers != null && markers.HasRequiredReferences;
        public void Configure(BoardLocalPlayerMarkerView value) => markers = value;
        private void OnDisable() { markers?.Hide(); RestoreNames(); }
        private void RestoreNames()
        {
            foreach (var visual in _visuals)
                if (visual != null) visual.SetNameplateVisible(true);
        }
        internal static bool ShouldShowMarkers(bool countdown, double remaining) =>
            countdown && remaining > 2d && remaining <= 3d;

        private void LateUpdate()
        {
            var match = NetworkMatchState.Instance;
            if (!HasRequiredReferences || match == null || !match.IsSpawned ||
                !MinigameCatalog.IsRegistered(match.CurrentMinigame))
            { markers?.Hide(); RestoreNames(); return; }
            var scene = SceneManager.GetSceneByName(MinigameCatalog.GetSceneName(match.CurrentMinigame));
            if (!scene.IsValid() || !scene.isLoaded) { markers.Hide(); RestoreNames(); return; }
            if (_scene != scene || Time.unscaledTime >= _nextResolve)
            {
                RestoreNames();
                _scene = scene;
                _nextResolve = Time.unscaledTime + .5f;
                ResolveTargets(match, scene);
            }
            var camera = Camera.main;
            var localSlot = -1;
            for (var slot = 0; slot < 4; slot++)
                if (match.GetAvatarForSlot(slot)?.IsOwner == true) localSlot = slot;
            if (localSlot < 0 || camera == null) { markers.Hide(); return; }
            var showMarkers = ShouldShowMarkers(match.IsMinigameRoundCountdown,
                match.MinigameRoundCountdownRemaining);
            for (var slot = 0; slot < 4; slot++)
            {
                var target = _targets[slot];
                var visible = showMarkers && target != null && target.gameObject.activeInHierarchy;
                var visual = _visuals[slot];
                if (visual != null)
                {
                    visual.SetNameplateVisible(false);
                    visual.SetTopViewHighlight(false);
                }
                if (slot == localSlot) markers.Present(target, camera, visible);
                markers.PresentPlayer(slot, target, camera, visible && slot != localSlot,
                    match.GetAvatarForSlot(slot)?.Appearance.BodyColor ?? Color.white);
            }
        }
        private void ResolveTargets(NetworkMatchState match, Scene scene)
        {
            System.Array.Clear(_targets, 0, _targets.Length);
            System.Array.Clear(_visuals, 0, _visuals.Length);
            if (match.CurrentMinigame == ScheduledMinigameId.SnowySpin)
            {
                foreach (var view in FindObjectsByType<SnowySpinNetworkView>(FindObjectsInactive.Include))
                    if (view.gameObject.scene == scene)
                        for (var slot = 0; slot < 4; slot++) _targets[slot] = view.GetPlayerBall(slot);
                return;
            }
            if (match.CurrentMinigame == ScheduledMinigameId.BouncingBalls)
            {
                foreach (var view in FindObjectsByType<BouncingBallsNetworkView>(FindObjectsInactive.Include))
                    if (view.gameObject.scene == scene)
                        for (var slot = 0; slot < 4; slot++) _targets[slot] = view.GetShieldTransform(slot);
                return;
            }
            var visuals = FindObjectsByType<PlayerAvatarVisual>(FindObjectsInactive.Include);
            for (var slot = 0; slot < 4; slot++)
            {
                if (match.CurrentMinigame == ScheduledMinigameId.ArenaCombat)
                    _visuals[slot] = match.GetAvatarForSlot(slot)?.AvatarVisual;
                else
                    foreach (var visual in visuals)
                        if (visual.gameObject.scene == scene && visual.name == GetAvatarRootName(match.CurrentMinigame, slot))
                        { _visuals[slot] = visual; break; }
                var selected = _visuals[slot];
                if (selected != null) _targets[slot] = selected.Bindings?.HeadAnchor ?? selected.transform;
            }
        }
        private static string GetAvatarRootName(
            ScheduledMinigameId minigame,
            int slot)
        {
            string prefix;
            switch (minigame)
            {
                case ScheduledMinigameId.Minefield:
                case ScheduledMinigameId.RedLightGreenLight:
                case ScheduledMinigameId.StableFooting:
                case ScheduledMinigameId.TerritoryPaint:
                    prefix = "Runner ";
                    break;
                case ScheduledMinigameId.WrongWay:
                    prefix = "WrongWay Runner ";
                    break;
                case ScheduledMinigameId.BalloonBlow:
                    prefix = "Balloon Player ";
                    break;
                case ScheduledMinigameId.Race2:
                    prefix = "Race 2 Player ";
                    break;
                case ScheduledMinigameId.OddOneOut:
                    prefix = "Odd One Out Player ";
                    break;
                case ScheduledMinigameId.CoinCollect:
                    prefix = "Coin Collect Player ";
                    break;
                case ScheduledMinigameId.GiftGrab:
                    prefix = "Gift Grab Player ";
                    break;
                case ScheduledMinigameId.TagChase:
                    prefix = "Tag Chase Player ";
                    break;
                case ScheduledMinigameId.Race:
                    prefix = "Race Player ";
                    break;
                case ScheduledMinigameId.SequenceMemory:
                    prefix = "Sequence Memory Player ";
                    break;
                case ScheduledMinigameId.BombPassing:
                    prefix = "Bomb Passing Player ";
                    break;
                case ScheduledMinigameId.CliffBarrage:
                    prefix = "Cliff Barrage Player ";
                    break;
                default:
                    return null;
            }

            return prefix + (slot + 1);
        }

    }
}

using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// A client-only world-space marker for the first two seconds of the shared
    /// minigame start countdown. It follows the local representation rather than
    /// the persistent board avatar, which is not used by every minigame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameLocalPlayerHighlight : MonoBehaviour
    {
        private const float AvatarHalfExtent = 0.96f;
        private const float BallHalfExtent = 0.86f;
        private const float ShieldHalfExtent = 1.34f;
        private const float LineWidth = 0.14f;

        private GameObject _outline;
        private Transform _target;
        private ScheduledMinigameId _targetMinigame;
        private int _targetSlot = -1;
        private Scene _targetScene;

        public static void EnsureInstalled(GameObject host)
        {
            if (host != null &&
                host.GetComponent<MinigameLocalPlayerHighlight>() == null)
            {
                host.AddComponent<MinigameLocalPlayerHighlight>();
            }
        }

        public static bool IsHighlightWindow(
            bool countdownActive,
            double remainingSeconds)
        {
            return countdownActive && remainingSeconds > 1d;
        }

        private void OnDisable()
        {
            Hide();
            ClearTarget();
        }

        private void OnDestroy()
        {
            if (_outline == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_outline);
            }
            else
            {
                DestroyImmediate(_outline);
            }
        }

        private void LateUpdate()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsSpawned ||
                !IsHighlightWindow(
                    match.IsMinigameStartCountdown,
                    match.MinigameStartCountdownRemaining))
            {
                Hide();
                ClearTarget();
                return;
            }

            var sceneName = MinigameCatalog.GetSceneName(match.CurrentMinigame);
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded ||
                !TryGetLocalSlot(match, out var slot))
            {
                Hide();
                ClearTarget();
                return;
            }

            if (_target == null || _targetMinigame != match.CurrentMinigame ||
                _targetSlot != slot || _targetScene != scene ||
                _target.gameObject.scene != scene ||
                !_target.gameObject.activeInHierarchy)
            {
                _target = ResolveTarget(match.CurrentMinigame, scene, slot);
                _targetMinigame = match.CurrentMinigame;
                _targetSlot = slot;
                _targetScene = scene;
            }

            if (_target == null || !_target.gameObject.activeInHierarchy)
            {
                Hide();
                return;
            }

            ShowAt(_target, match.CurrentMinigame, slot);
        }

        private static bool TryGetLocalSlot(
            NetworkMatchState match,
            out int slot)
        {
            for (slot = 0; slot < 4; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar != null && avatar.IsOwner)
                {
                    return true;
                }
            }

            slot = -1;
            return false;
        }

        private static Transform ResolveTarget(
            ScheduledMinigameId minigame,
            Scene scene,
            int slot)
        {
            if (minigame == ScheduledMinigameId.SnowySpin)
            {
                var views = FindObjectsByType<SnowySpinNetworkView>(
                    FindObjectsInactive.Include);
                foreach (var view in views)
                {
                    if (view.gameObject.scene == scene)
                    {
                        return view.GetPlayerBall(slot);
                    }
                }

                return null;
            }

            if (minigame == ScheduledMinigameId.BouncingBalls)
            {
                var views = FindObjectsByType<BouncingBallsNetworkView>(
                    FindObjectsInactive.Include);
                foreach (var view in views)
                {
                    if (view.gameObject.scene == scene)
                    {
                        return view.GetShieldTransform(slot);
                    }
                }

                return null;
            }

            var rootName = GetAvatarRootName(minigame, slot);
            if (rootName == null)
            {
                return null;
            }

            // The views create their players after scene load. Search every
            // countdown frame until the correct scene-local visual appears.
            var visuals = FindObjectsByType<PlayerAvatarVisual>(
                FindObjectsInactive.Include);
            foreach (var visual in visuals)
            {
                if (visual.gameObject.scene == scene &&
                    visual.gameObject.name == rootName)
                {
                    return visual.transform;
                }
            }

            return null;
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
                default:
                    return null;
            }

            return prefix + (slot + 1);
        }

        private void ShowAt(
            Transform target,
            ScheduledMinigameId minigame,
            int slot)
        {
            EnsureOutline();
            if (_outline == null)
            {
                return;
            }

            var marker = _outline.transform;
            if (minigame == ScheduledMinigameId.BouncingBalls)
            {
                marker.SetPositionAndRotation(
                    target.position + new Vector3(0f, 0f, -0.16f),
                    Quaternion.Euler(90f, 0f, 0f));
                marker.localScale = slot == 1 || slot == 3
                    ? new Vector3(0.21f, 1f, 1f)
                    : new Vector3(1f, 1f, 0.21f);
            }
            else
            {
                var verticalOffset = minigame == ScheduledMinigameId.SnowySpin
                    ? -0.62f
                    : -0.98f;
                marker.SetPositionAndRotation(
                    target.position + Vector3.up * verticalOffset,
                    Quaternion.identity);
                var extent = minigame == ScheduledMinigameId.SnowySpin
                    ? BallHalfExtent
                    : AvatarHalfExtent;
                var scale = extent / ShieldHalfExtent;
                marker.localScale = new Vector3(scale, 1f, scale);
            }

            if (!_outline.activeSelf)
            {
                _outline.SetActive(true);
            }
        }

        private void EnsureOutline()
        {
            if (_outline != null)
            {
                return;
            }

            // BoardFlowView lives on the screen-space BoardCanvas prefab. Keep
            // the world marker outside that hierarchy so disabling the Canvas
            // during play never hides or scales the outline.
            _outline = TopViewHighlightUtility.CreateSquareOutline(
                null,
                "Countdown Local Player White Outline",
                ShieldHalfExtent,
                LineWidth,
                0f);
            _outline.SetActive(false);
        }

        private void Hide()
        {
            if (_outline != null && _outline.activeSelf)
            {
                _outline.SetActive(false);
            }
        }

        private void ClearTarget()
        {
            _target = null;
            _targetSlot = -1;
            _targetScene = default;
        }
    }
}

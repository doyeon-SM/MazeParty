using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Keeps player names on every minigame representation and briefly
    /// surrounds the local player whenever a replicated round number changes.
    /// The board-only top-view marker remains a separate presentation.
    /// Avatar games reuse PlayerAvatarPresentation; ball/shield games use the shared
    /// PlayerWorldIndicator prefab from PlayerAvatarPresentationAssets.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class MinigameLocalPlayerHighlight : MonoBehaviour
    {
        private const float RoundLocationHighlightSeconds = 3f;
        private const float SnowySpinNameOffset = 1.15f;
        private const float BouncingBallsNameOffset = 1.05f;
        private const float SnowySpinHighlightScale = 1.2f;

        private readonly List<PlayerAvatarVisual> _standardVisuals =
            new List<PlayerAvatarVisual>(4);
        private readonly PlayerWorldIndicator[] _specialIndicators =
            new PlayerWorldIndicator[4];

        private PlayerAvatarVisual _localVisual;
        private Scene _standardVisualScene;
        private bool _missingIndicatorReported;
        private bool _hasTrackedMinigame;
        private ScheduledMinigameId _trackedMinigame;
        private int _trackedRoundNumber;
        private float _roundLocationHighlightEndsAt;

        public static void EnsureInstalled(GameObject host)
        {
            if (host != null &&
                host.GetComponent<MinigameLocalPlayerHighlight>() == null)
            {
                host.AddComponent<MinigameLocalPlayerHighlight>();
            }
        }

        private static bool UsesWorldIndicator(ScheduledMinigameId minigame)
        {
            return minigame == ScheduledMinigameId.SnowySpin ||
                   minigame == ScheduledMinigameId.BouncingBalls;
        }

        private void OnDisable()
        {
            ClearStandardHighlight();
            HideSpecialIndicators();
            ResetStandardVisualCache();
            ResetRoundTracking();
        }

        private void OnDestroy()
        {
            for (var slot = 0; slot < _specialIndicators.Length; slot++)
            {
                var indicator = _specialIndicators[slot];
                if (indicator == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(indicator.gameObject);
                }
                else
                {
                    DestroyImmediate(indicator.gameObject);
                }
            }
        }

        private void LateUpdate()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsSpawned ||
                !MinigameCatalog.IsRegistered(match.CurrentMinigame) ||
                !TryGetLocalSlot(match, out var localSlot))
            {
                ClearPresentation();
                return;
            }

            var sceneName = MinigameCatalog.GetSceneName(
                match.CurrentMinigame);
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                ClearPresentation();
                return;
            }

            var highlightVisible = match.IsMinigameStartCountdown &&
                                   match.MinigameStartCountdownRemaining > 0d;
            var locationHighlightVisible =
                RefreshRoundLocationHighlight(match.CurrentMinigame);
            if (UsesWorldIndicator(match.CurrentMinigame))
            {
                ClearStandardHighlight();
                RefreshSpecialPresentation(
                    match,
                    scene,
                    localSlot,
                    highlightVisible || locationHighlightVisible);
                return;
            }

            HideSpecialIndicators();
            RefreshStandardNameplates(match, scene);
            var localVisual = ResolveAvatarVisual(
                match.CurrentMinigame,
                scene,
                localSlot);
            if (_localVisual != localVisual)
            {
                ClearStandardHighlight();
                _localVisual = localVisual;
            }

            if (_localVisual == null)
            {
                return;
            }

            _localVisual.SetNameplateVisible(true);
            _localVisual.SetTopViewHighlight(false);
            _localVisual.SetLocationHighlightVisible(
                highlightVisible || locationHighlightVisible);
        }

        private bool RefreshRoundLocationHighlight(
            ScheduledMinigameId minigame)
        {
            if (!_hasTrackedMinigame || _trackedMinigame != minigame)
            {
                _hasTrackedMinigame = true;
                _trackedMinigame = minigame;
                _trackedRoundNumber = 0;
                _roundLocationHighlightEndsAt = 0f;
            }

            if (TryGetRoundNumber(minigame, out var roundNumber) &&
                roundNumber != _trackedRoundNumber)
            {
                _trackedRoundNumber = roundNumber;
                _roundLocationHighlightEndsAt =
                    Time.unscaledTime + RoundLocationHighlightSeconds;
            }

            return Time.unscaledTime < _roundLocationHighlightEndsAt;
        }

        private static bool TryGetRoundNumber(
            ScheduledMinigameId minigame,
            out int roundNumber)
        {
            roundNumber = 0;
            switch (minigame)
            {
                case ScheduledMinigameId.Minefield:
                    roundNumber = NetworkMinefieldState.Instance?.RoundNumber ?? 0;
                    break;
                case ScheduledMinigameId.WrongWay:
                    roundNumber = NetworkWrongWayState.Instance?.RoundNumber ?? 0;
                    break;
                case ScheduledMinigameId.RedLightGreenLight:
                    roundNumber = NetworkRedLightGreenLightState.Instance?.RoundNumber ?? 0;
                    break;
                case ScheduledMinigameId.StableFooting:
                    roundNumber = NetworkStableFootingState.Instance?.RoundNumber ?? 0;
                    break;
                case ScheduledMinigameId.BalloonBlow:
                    roundNumber = NetworkBalloonBlowState.Instance?.RoundNumber ?? 0;
                    break;
                case ScheduledMinigameId.GiftGrab:
                    roundNumber = NetworkGiftGrabState.Instance?.RoundNumber ?? 0;
                    break;
                case ScheduledMinigameId.TerritoryPaint:
                    roundNumber = NetworkTerritoryPaintState.Instance?.RoundNumber ?? 0;
                    break;
                case ScheduledMinigameId.TagChase:
                    roundNumber = NetworkTagChaseState.Instance?.RoundNumber ?? 0;
                    break;
                case ScheduledMinigameId.Race:
                    roundNumber = NetworkRaceState.Instance?.RoundNumber ?? 0;
                    break;
                case ScheduledMinigameId.SequenceMemory:
                    roundNumber = NetworkSequenceMemoryState.Instance?.RoundNumber ?? 0;
                    break;
                case ScheduledMinigameId.BouncingBalls:
                    roundNumber = NetworkBouncingBallsState.Instance?.RoundNumber ?? 0;
                    break;
                case ScheduledMinigameId.BombPassing:
                    roundNumber = NetworkBombPassingState.Instance?.RoundNumber ?? 0;
                    break;
                case ScheduledMinigameId.SnowySpin:
                    roundNumber = NetworkSnowySpinState.Instance?.RoundNumber ?? 0;
                    break;
                case ScheduledMinigameId.ArenaCombat:
                {
                    var arena = NetworkArenaCombatState.Instance;
                    roundNumber = arena != null &&
                                  arena.Phase !=
                                  NetworkArenaCombatPhase.Inactive
                        ? 1
                        : 0;
                    break;
                }
                case ScheduledMinigameId.CliffBarrage:
                    roundNumber = NetworkCliffBarrageState.Instance?.RoundNumber ?? 0;
                    break;
            }

            return roundNumber > 0;
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

        private void RefreshStandardNameplates(
            NetworkMatchState match,
            Scene scene)
        {
            if (match.CurrentMinigame == ScheduledMinigameId.ArenaCombat)
            {
                ResetStandardVisualCache();
                for (var slot = 0; slot < 4; slot++)
                {
                    var visual = match.GetAvatarForSlot(slot)?.AvatarVisual;
                    if (visual != null)
                    {
                        visual.SetNameplateVisible(true);
                    }
                }
                return;
            }

            if (_standardVisualScene != scene ||
                _standardVisuals.Count < 4)
            {
                _standardVisuals.Clear();
                var visuals = FindObjectsByType<PlayerAvatarVisual>(
                    FindObjectsInactive.Include);
                foreach (var visual in visuals)
                {
                    if (visual != null && visual.gameObject.scene == scene)
                    {
                        _standardVisuals.Add(visual);
                    }
                }
                _standardVisualScene = scene;
            }

            foreach (var visual in _standardVisuals)
            {
                if (visual != null)
                {
                    visual.SetNameplateVisible(true);
                }
            }
        }

        private static PlayerAvatarVisual ResolveAvatarVisual(
            ScheduledMinigameId minigame,
            Scene scene,
            int slot)
        {
            if (minigame == ScheduledMinigameId.ArenaCombat)
            {
                return NetworkMatchState.Instance?
                    .GetAvatarForSlot(slot)?.AvatarVisual;
            }

            var rootName = GetAvatarRootName(minigame, slot);
            if (rootName == null)
            {
                return null;
            }

            var visuals = FindObjectsByType<PlayerAvatarVisual>(
                FindObjectsInactive.Include);
            foreach (var visual in visuals)
            {
                if (visual.gameObject.scene == scene &&
                    visual.gameObject.name == rootName)
                {
                    return visual;
                }
            }

            return null;
        }

        private void RefreshSpecialPresentation(
            NetworkMatchState match,
            Scene scene,
            int localSlot,
            bool highlightVisible)
        {
            if (!EnsureSpecialIndicators())
            {
                return;
            }

            var camera = Camera.main;
            for (var slot = 0; slot < _specialIndicators.Length; slot++)
            {
                var indicator = _specialIndicators[slot];
                var target = ResolveSpecialTarget(
                    match.CurrentMinigame,
                    scene,
                    slot);
                if (indicator == null || target == null ||
                    !target.gameObject.activeInHierarchy || camera == null)
                {
                    indicator?.SetVisible(false);
                    continue;
                }

                var avatar = match.GetAvatarForSlot(slot);
                indicator.SetDisplayName(
                    avatar != null &&
                    !string.IsNullOrWhiteSpace(avatar.DisplayName)
                        ? avatar.DisplayName
                        : GameText.F("PLAYER {0}", slot + 1));
                indicator.SetPose(
                    target,
                    camera,
                    match.CurrentMinigame ==
                        ScheduledMinigameId.SnowySpin
                            ? SnowySpinNameOffset
                            : BouncingBallsNameOffset,
                    GetSpecialHighlightScale(
                        match.CurrentMinigame));
                indicator.SetLocalStartHighlight(
                    slot == localSlot && highlightVisible);
            }
        }

        private bool EnsureSpecialIndicators()
        {
            var assets = Resources.Load<PlayerAvatarPresentationAssets>(
                PlayerAvatarPresentationAssets.ResourcePath);
            var source = assets != null
                ? assets.WorldIndicatorPrefab
                : null;
            if (source == null || !source.HasRequiredReferences)
            {
                if (!_missingIndicatorReported)
                {
                    _missingIndicatorReported = true;
                    Debug.LogError(
                        "Shared player world indicator prefab is missing or " +
                        "incomplete. Run MazeParty/Multiplayer/Install " +
                        "Shared Player World Indicator.",
                        this);
                }
                return false;
            }

            for (var slot = 0; slot < _specialIndicators.Length; slot++)
            {
                if (_specialIndicators[slot] != null)
                {
                    continue;
                }

                var indicator = Instantiate(source);
                indicator.gameObject.name =
                    "Player World Indicator " + (slot + 1);
                if (gameObject.scene.IsValid())
                {
                    SceneManager.MoveGameObjectToScene(
                        indicator.gameObject,
                        gameObject.scene);
                }
                indicator.SetVisible(false);
                _specialIndicators[slot] = indicator;
            }

            _missingIndicatorReported = false;
            return true;
        }

        private static Transform ResolveSpecialTarget(
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
            }
            else if (minigame == ScheduledMinigameId.BouncingBalls)
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
            }

            return null;
        }

        private static float GetSpecialHighlightScale(
            ScheduledMinigameId minigame)
        {
            return minigame == ScheduledMinigameId.SnowySpin
                ? SnowySpinHighlightScale
                : 1f;
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
                case ScheduledMinigameId.CliffBarrage:
                    prefix = "Cliff Barrage Player ";
                    break;
                default:
                    return null;
            }

            return prefix + (slot + 1);
        }

        private void ClearPresentation()
        {
            ClearStandardHighlight();
            HideSpecialIndicators();
            ResetStandardVisualCache();
            ResetRoundTracking();
        }

        private void ClearStandardHighlight()
        {
            if (_localVisual != null)
            {
                _localVisual.SetTopViewHighlight(false);
                _localVisual.SetLocationHighlightVisible(false);
                _localVisual = null;
            }
        }

        private void HideSpecialIndicators()
        {
            foreach (var indicator in _specialIndicators)
            {
                indicator?.SetVisible(false);
            }
        }

        private void ResetStandardVisualCache()
        {
            _standardVisualScene = default;
            _standardVisuals.Clear();
        }

        private void ResetRoundTracking()
        {
            _hasTrackedMinigame = false;
            _trackedMinigame = default;
            _trackedRoundNumber = 0;
            _roundLocationHighlightEndsAt = 0f;
        }
    }
}

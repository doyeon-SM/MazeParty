using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Maps replicated flow state to the local-only Cinemachine presentation.
    /// Camera completion never blocks or advances the authoritative server timeline.
    /// </summary>
    public sealed class BoardFlowCameraPresenter : MonoBehaviour
    {
        private const int NameplateOcclusionHitCapacity = 64;

        [SerializeField] private GameplayCameraDirector cameraDirector;

        private readonly RaycastHit[] _nameplateOcclusionHits =
            new RaycastHit[NameplateOcclusionHitCapacity];
        private readonly NetworkPlayerAvatar[] _observedNameplateAvatars =
            new NetworkPlayerAvatar[MultiplayerConstants.MaxPlayers];
        private readonly PlayerAvatarVisual[] _observedNameplateVisuals =
            new PlayerAvatarVisual[MultiplayerConstants.MaxPlayers];
        private NetworkPlayerAvatar _localAvatar;
        private BoardTopology _topology;
        private int _observedRevision = -1;
        private bool _wasReconnectPaused;
        private bool _hasObservedState;

        public void Configure(GameplayCameraDirector director)
        {
            cameraDirector = director;
        }

        private void OnDisable()
        {
            // Player objects persist with the bootstrap scene. Never carry the
            // first-person hidden-body or board top-view highlight presentation
            // out of the Board scene and into the lobby.
            if (_localAvatar != null)
            {
                _localAvatar.AvatarVisual?.SetOwnerFirstPerson(false);
                _localAvatar.AvatarVisual?.SetTopViewHighlight(false);
            }
            ClearNameplateOcclusion();
            BoardFlowView.Instance?.SetTopViewShopHighlights(false);
        }

        private void Update()
        {
            if (cameraDirector == null)
            {
                cameraDirector = FindAnyObjectByType<GameplayCameraDirector>();
            }

            ResolveLocalAvatar();
            var match = NetworkMatchState.Instance;
            if (cameraDirector == null || match == null || !match.IsSpawned || !match.GameplayEnabled)
            {
                ClearNameplateOcclusion();
                return;
            }

            if (_localAvatar != null)
            {
                cameraDirector.SetLocalPlayerEye(_localAvatar.EyePivot);
            }

            if (match.IsReconnectPaused)
            {
                _wasReconnectPaused = true;
                RefreshOpponentNameplateOcclusion(
                    match,
                    cameraDirector.ActiveMode);
                return;
            }

            RefreshCombatSpectatorFocus(match);
            var targetMode = match.IsKeyShopRevealActive
                ? GameplayMode.BoardTopDown
                : ModeFor(match, _localAvatar);
            if (!_hasObservedState || _wasReconnectPaused)
            {
                cameraDirector.SnapTo(targetMode, _localAvatar != null ? _localAvatar.EyePivot : null);
                _observedRevision = match.StateRevision;
                _hasObservedState = true;
                _wasReconnectPaused = false;
                ApplyLocalBodyVisibility(targetMode);
            }
            else if (_observedRevision != match.StateRevision)
            {
                _observedRevision = match.StateRevision;
                if (cameraDirector.ActiveMode != targetMode)
                {
                    cameraDirector.SwitchTo(targetMode);
                }
                ApplyLocalBodyVisibility(targetMode);
            }

            // Camera movement and obstacle visibility change continuously even
            // while the replicated flow revision remains unchanged.
            RefreshOpponentNameplateOcclusion(match, targetMode);
        }

        private void RefreshOpponentNameplateOcclusion(
            NetworkMatchState match,
            GameplayMode mode)
        {
            var outputCamera = cameraDirector != null
                ? cameraDirector.OutputCamera
                : null;
            var canTestLineOfSight = outputCamera != null &&
                                     mode != GameplayMode.Minigame;

            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var avatar = _observedNameplateAvatars[slot];
                if (avatar == null || !avatar.IsSpawned ||
                    avatar.AssignedSlot != slot)
                {
                    // Missing seats do not trigger the match-wide fallback
                    // search. A spawned cached avatar remains valid throughout
                    // reconnect grace even while the present bit is cleared.
                    avatar = match.IsPlayerPresent(slot)
                        ? match.GetAvatarForSlot(slot)
                        : null;
                }
                _observedNameplateAvatars[slot] = avatar;
                var visual = avatar != null ? avatar.AvatarVisual : null;
                var previous = _observedNameplateVisuals[slot];
                if (previous != null && previous != visual)
                {
                    previous.SetNameplateOccluded(false);
                }
                _observedNameplateVisuals[slot] = visual;

                if (visual == null)
                {
                    continue;
                }

                var isLocalAvatar = avatar == _localAvatar || avatar.IsOwner;
                visual.SetNameplateOccluded(
                    canTestLineOfSight &&
                    !isLocalAvatar &&
                    IsNameplateBlocked(outputCamera, visual));
            }
        }

        private bool IsNameplateBlocked(
            Camera outputCamera,
            PlayerAvatarVisual targetVisual)
        {
            var origin = outputCamera.transform.position;
            var offset = targetVisual.NameplateOcclusionTarget - origin;
            var distance = offset.magnitude;
            if (distance <= 0.001f)
            {
                return false;
            }

            var hitCount = Physics.RaycastNonAlloc(
                origin,
                offset / distance,
                _nameplateOcclusionHits,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < hitCount; index++)
            {
                var collider = _nameplateOcclusionHits[index].collider;
                if (collider == null ||
                    collider.GetComponentInParent<NetworkPlayerAvatar>() != null)
                {
                    continue;
                }

                var boundaryWall =
                    collider.GetComponentInParent<BoardBoundaryWallVisual>();
                if (boundaryWall != null && !boundaryWall.IsVisible)
                {
                    // Remote players' private movement walls keep their
                    // colliders active for simulation but are not visible to
                    // this client, so they must not mask a nickname.
                    continue;
                }

                return true;
            }

            // A full non-alloc buffer means line of sight is ambiguous. Hiding
            // the remote label avoids leaking it through a dense obstacle setup.
            return hitCount == _nameplateOcclusionHits.Length;
        }

        private void ClearNameplateOcclusion()
        {
            for (var slot = 0; slot < _observedNameplateVisuals.Length; slot++)
            {
                _observedNameplateVisuals[slot]?.SetNameplateOccluded(false);
                _observedNameplateAvatars[slot] = null;
                _observedNameplateVisuals[slot] = null;
            }
        }

        private void ResolveLocalAvatar()
        {
            if (_localAvatar != null && _localAvatar.IsSpawned)
            {
                return;
            }

            var manager = NetworkManager.Singleton;
            var playerObject = manager != null && manager.SpawnManager != null
                ? manager.SpawnManager.GetLocalPlayerObject()
                : null;
            _localAvatar = playerObject != null
                ? playerObject.GetComponent<NetworkPlayerAvatar>()
                : null;
        }

        private void ApplyLocalBodyVisibility(GameplayMode targetMode)
        {
            if (_localAvatar == null)
            {
                return;
            }

            _localAvatar.AvatarVisual?.SetOwnerFirstPerson(
                targetMode == GameplayMode.FirstPerson);
            _localAvatar.AvatarVisual?.SetTopViewHighlight(
                targetMode == GameplayMode.BoardTopDown);
            BoardFlowView.Instance?.SetTopViewShopHighlights(
                targetMode == GameplayMode.BoardTopDown);
        }

        private void RefreshCombatSpectatorFocus(NetworkMatchState match)
        {
            if (!match.IsCombatPhase || !match.IsCombatActive)
            {
                return;
            }

            if (_topology == null)
            {
                _topology = FindAnyObjectByType<BoardTopology>();
            }

            if (_topology != null &&
                _topology.TryGetTile(match.CombatTile, out var tile) && tile != null)
            {
                cameraDirector.SetCombatSpectatorFocus(tile.WorldCenter);
            }
        }

        private static GameplayMode ModeFor(
            NetworkMatchState match,
            NetworkPlayerAvatar localAvatar)
        {
            if (match.FlowState == BoardFlowState.CombatResolve && match.IsCombatActive)
            {
                return localAvatar != null &&
                       match.IsCombatParticipant(localAvatar.AssignedSlot) &&
                       match.IsCombatAlive(localAvatar.AssignedSlot) &&
                       localAvatar.IsCombatAlive
                    ? GameplayMode.FirstPerson
                    : GameplayMode.CombatSpectator;
            }

            if ((match.FlowState == BoardFlowState.MatchComplete &&
                 match.IsAwardCeremonyActive) ||
                match.FlowState == BoardFlowState.MinigamePlaying ||
                (match.FlowState == BoardFlowState.MinigameResult &&
                 match.CurrentMinigame !=
                 MazeParty.Gameplay.Minigames.ScheduledMinigameId.Skip))
            {
                return GameplayMode.Minigame;
            }

            return match.FlowState == BoardFlowState.Descending ||
                   match.FlowState == BoardFlowState.Action
                    ? GameplayMode.FirstPerson
                    : GameplayMode.BoardTopDown;
        }
    }
}

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
        [SerializeField] private GameObject resourceTransferCoinPrefab;
        [SerializeField] private GameObject resourceTransferKeyPrefab;
        [SerializeField] private BoardLandingEffectFeedbackView
            landingEffectFeedbackPrefab;
        [SerializeField] private GameObject landingEventLightningPrefab;
        [SerializeField, Min(0f)] private float resourceTransferHeadOffset = 0.3f;
        [SerializeField, Min(0.1f)] private float resourceTransferTravelHeight = 1.5f;

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
        private GameObject _resourceTransferVisual;
        private int _resourceTransferVisualRevision = -1;
        private BoardResourceTransferPhase _resourceTransferVisualPhase;
        private BoardLandingEffectFeedbackView _landingEffectFeedbackVisual;
        private int _landingEffectFeedbackRevision = -1;
        private bool _landingEffectFeedbackInitialized;

        public void Configure(GameplayCameraDirector director)
        {
            cameraDirector = director;
        }

        public void ConfigureResourceTransferAssets(
            GameObject coinPrefab,
            GameObject keyPrefab)
        {
            resourceTransferCoinPrefab = coinPrefab;
            resourceTransferKeyPrefab = keyPrefab;
        }

        public void ConfigureLandingEffectFeedbackAssets(
            BoardLandingEffectFeedbackView feedbackPrefab,
            GameObject lightningPrefab)
        {
            landingEffectFeedbackPrefab = feedbackPrefab;
            landingEventLightningPrefab = lightningPrefab;
            OneShotVfxPool.Prewarm(landingEventLightningPrefab, 4);
        }

        private void OnEnable()
        {
            OneShotVfxPool.Prewarm(landingEventLightningPrefab, 4);
        }

        private void OnDisable()
        {
            // Player objects persist with the bootstrap scene. Never carry the
            // first-person hidden-body or board top-view highlight presentation
            // out of the Board scene and into the lobby.
            if (_localAvatar != null)
            {
                var visual = _localAvatar.AvatarVisual;
                if (visual != null)
                {
                    visual.SetOwnerFirstPerson(false);
                    visual.SetTopViewHighlight(false);
                    visual.SetBoardTopViewHighlightVisible(false);
                }
            }
            ClearNameplateOcclusion();
            ClearResourceTransferVisual();
            ClearLandingEffectFeedbackVisual();
            _landingEffectFeedbackInitialized = false;
            _landingEffectFeedbackRevision = -1;
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
                ClearResourceTransferVisual();
                ClearLandingEffectFeedbackVisual();
                _landingEffectFeedbackInitialized = false;
                _landingEffectFeedbackRevision = -1;
                return;
            }

            RefreshLandingEffectFeedbackPresentation(match);

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
            var resourceTransferPhase =
                RefreshResourceTransferPresentation(match);
            var targetMode = match.IsKeyShopRevealActive
                ? GameplayMode.BoardTopDown
                : (resourceTransferPhase == BoardResourceTransferPhase.Source ||
                   resourceTransferPhase == BoardResourceTransferPhase.Destination)
                    ? GameplayMode.BoardResourceEvent
                    : ModeFor(match, _localAvatar);
            if (!_hasObservedState || _wasReconnectPaused)
            {
                cameraDirector.SnapTo(targetMode, _localAvatar != null ? _localAvatar.EyePivot : null);
                _observedRevision = match.StateRevision;
                _hasObservedState = true;
                _wasReconnectPaused = false;
                ApplyLocalBodyVisibility(targetMode);
            }
            else if (_observedRevision != match.StateRevision ||
                     cameraDirector.ActiveMode != targetMode)
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

        private void RefreshLandingEffectFeedbackPresentation(
            NetworkMatchState match)
        {
            var snapshot = match.LandingEffectFeedback;
            if (!_landingEffectFeedbackInitialized)
            {
                _landingEffectFeedbackInitialized = true;
                _landingEffectFeedbackRevision = -1;
            }

            if (_landingEffectFeedbackRevision == snapshot.Revision)
            {
                return;
            }

            ClearLandingEffectFeedbackVisual();
            if (!BoardLandingEffectFeedbackRules.IsVisible(
                    snapshot,
                    match.SynchronizedNow))
            {
                // Inactive, invalid, and expired snapshots cannot become
                // presentable later. Consume them so they do not retry forever.
                _landingEffectFeedbackRevision = snapshot.Revision;
                return;
            }

            if (match.IsReconnectPaused || match.IsGlobalSimulationPaused ||
                match.FlowState != BoardFlowState.LandingEffectResolve)
            {
                // A valid one-second snapshot can arrive before the local flow
                // and presentation dependencies are ready. Leave its revision
                // pending so Update retries while the snapshot is still visible.
                return;
            }

            var avatar = match.GetAvatarForSlot(snapshot.Slot);
            if (avatar == null)
            {
                return;
            }

            if (snapshot.FeedbackKind == BoardLandingEffectFeedbackKind.Event)
            {
                if (landingEventLightningPrefab != null)
                {
                    OneShotVfxPool.Play(
                        landingEventLightningPrefab,
                        avatar.transform.position,
                        Quaternion.identity,
                        1f);
                    _landingEffectFeedbackRevision = snapshot.Revision;
                }
                return;
            }

            if (landingEffectFeedbackPrefab == null ||
                !BoardLandingEffectFeedbackRules.TryGetContent(
                    snapshot,
                    out var iconKind,
                    out var label))
            {
                return;
            }

            var head = ResolveHeadAnchor(avatar);
            if (head == null)
            {
                return;
            }

            var remaining = (float)
                BoardLandingEffectFeedbackRules.GetRemainingSeconds(
                    snapshot,
                    match.SynchronizedNow);
            _landingEffectFeedbackVisual = Instantiate(
                landingEffectFeedbackPrefab);
            _landingEffectFeedbackVisual.name =
                "Board Landing Effect Feedback";
            _landingEffectFeedbackVisual.Present(
                head,
                cameraDirector != null ? cameraDirector.OutputCamera : null,
                iconKind,
                label,
                remaining);
            _landingEffectFeedbackRevision = snapshot.Revision;
        }

        private BoardResourceTransferPhase RefreshResourceTransferPresentation(
            NetworkMatchState match)
        {
            if (match.FlowState != BoardFlowState.LandingEffectResolve ||
                match.IsGlobalSimulationPaused)
            {
                ClearResourceTransferVisual();
                return BoardResourceTransferPhase.None;
            }

            var phase = match.GetResourceTransferPresentationPhase(
                out _,
                out var motionProgress);
            if (phase != BoardResourceTransferPhase.Source &&
                phase != BoardResourceTransferPhase.Destination)
            {
                ClearResourceTransferVisual();
                return phase;
            }

            var snapshot = match.ResourceTransferPresentation;
            var focusSlot = phase == BoardResourceTransferPhase.Source
                ? snapshot.SourceSlot
                : snapshot.DestinationSlot;
            var avatar = match.GetAvatarForSlot(focusSlot);
            if (avatar == null)
            {
                ClearResourceTransferVisual();
                return BoardResourceTransferPhase.None;
            }

            var head = ResolveHeadAnchor(avatar);
            cameraDirector.SetBoardResourceEventFocus(
                avatar.transform,
                head,
                resourceTransferHeadOffset +
                resourceTransferTravelHeight * 0.5f);
            RefreshResourceTransferVisual(
                snapshot,
                phase,
                motionProgress,
                avatar.transform,
                head);
            return phase;
        }

        private void RefreshResourceTransferVisual(
            BoardResourceTransferSnapshot snapshot,
            BoardResourceTransferPhase phase,
            float motionProgress,
            Transform avatarRoot,
            Transform head)
        {
            if (_resourceTransferVisual == null ||
                _resourceTransferVisualRevision != snapshot.Revision ||
                _resourceTransferVisualPhase != phase)
            {
                ClearResourceTransferVisual();
                var prefab = snapshot.ResourceKind ==
                             BoardSpecialEventResource.Key
                    ? resourceTransferKeyPrefab
                    : resourceTransferCoinPrefab;
                if (prefab == null)
                {
                    return;
                }

                _resourceTransferVisual = Instantiate(prefab);
                _resourceTransferVisual.name =
                    "Board Resource Transfer " + snapshot.ResourceKind;
                DisableResourceVisualBehaviours(_resourceTransferVisual);
                _resourceTransferVisualRevision = snapshot.Revision;
                _resourceTransferVisualPhase = phase;
            }

            var headPosition = head != null
                ? head.position
                : avatarRoot.position +
                  Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
            var eased = motionProgress * motionProgress *
                        (3f - 2f * motionProgress);
            var height = phase == BoardResourceTransferPhase.Source
                ? Mathf.Lerp(0f, resourceTransferTravelHeight, eased)
                : Mathf.Lerp(resourceTransferTravelHeight, 0f, eased);
            var visualTransform = _resourceTransferVisual.transform;
            visualTransform.position = headPosition +
                                       Vector3.up *
                                       (resourceTransferHeadOffset + height);
            visualTransform.rotation = Quaternion.Euler(
                0f,
                eased * 240f,
                0f);
            var disappear = phase == BoardResourceTransferPhase.Source
                ? Mathf.InverseLerp(0.72f, 1f, motionProgress)
                : Mathf.InverseLerp(0.82f, 1f, motionProgress);
            visualTransform.localScale = Vector3.one *
                                         Mathf.Lerp(1f, 0.2f, disappear);
        }

        private void ClearResourceTransferVisual()
        {
            if (_resourceTransferVisual != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_resourceTransferVisual);
                }
                else
                {
                    DestroyImmediate(_resourceTransferVisual);
                }
            }

            _resourceTransferVisual = null;
            _resourceTransferVisualRevision = -1;
            _resourceTransferVisualPhase = BoardResourceTransferPhase.None;
        }

        private void ClearLandingEffectFeedbackVisual()
        {
            if (_landingEffectFeedbackVisual != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_landingEffectFeedbackVisual.gameObject);
                }
                else
                {
                    DestroyImmediate(_landingEffectFeedbackVisual.gameObject);
                }
            }

            _landingEffectFeedbackVisual = null;
        }

        private static Transform ResolveHeadAnchor(NetworkPlayerAvatar avatar)
        {
            var bindings = avatar.AvatarVisual != null
                ? avatar.AvatarVisual.Bindings
                : null;
            return bindings != null && bindings.HeadAnchor != null
                ? bindings.HeadAnchor
                : avatar.EyePivot;
        }

        private static void DisableResourceVisualBehaviours(GameObject visual)
        {
            var behaviours = visual.GetComponentsInChildren<MonoBehaviour>(true);
            for (var index = 0; index < behaviours.Length; index++)
            {
                behaviours[index].enabled = false;
            }

            var colliders = visual.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }
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
                // Null-conditional access bypasses UnityEngine.Object's destroyed-
                // object equality. Use Unity's explicit check so scene teardown
                // cannot call into an already destroyed avatar every frame.
                var visual = _observedNameplateVisuals[slot];
                if (visual != null)
                {
                    visual.SetNameplateOccluded(false);
                }
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

            var visual = _localAvatar.AvatarVisual;
            if (visual != null)
            {
                visual.SetOwnerFirstPerson(
                    targetMode == GameplayMode.FirstPerson);
                // Board top view reuses the persistent WaterShield request. The
                // square highlight remains available to minigames only.
                visual.SetTopViewHighlight(false);
                visual.SetBoardTopViewHighlightVisible(
                    targetMode == GameplayMode.BoardTopDown);
            }
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

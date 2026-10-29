using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Maps replicated flow state to the local-only Cinemachine presentation.
    /// Camera completion never blocks or advances the authoritative server timeline.
    /// </summary>
    public sealed partial class BoardFlowCameraPresenter : MonoBehaviour
    {
        private const int NameplateOcclusionHitCapacity = 64;

        [SerializeField] private GameplayCameraDirector cameraDirector;
        [SerializeField] private BoardLocalPlayerMarkerView localPlayerMarker;
        private GameplayCameraDirector _subscribedDirector;

        public BoardLocalPlayerMarkerView LocalPlayerMarker => localPlayerMarker;
        [SerializeField] private BoardEventSplitView eventSplitViewPrefab;
        private BoardEventSplitView _eventSplitView;
        private readonly Transform[] _eventRoots = new Transform[4];
        private readonly Transform[] _eventHeads = new Transform[4];
        private readonly int[] _retreatSlots = new int[4];
        private readonly GameObject[] _eventVisuals = new GameObject[4];
        private int _eventVisualRevision = -1;
        private byte _landingEventPlayedMask;
        [SerializeField] private GameObject resourceTransferCoinPrefab;
        [SerializeField] private GameObject resourceTransferKeyPrefab;
        [SerializeField] private BoardLandingEffectFeedbackView
            landingEffectFeedbackPrefab;
        [SerializeField] private GameObject landingEventLightningPrefab;
        [SerializeField] private GameObject combatHitSparkVfxPrefab;
        [SerializeField] private GameObject landingGiftPrefab;
        [SerializeField] private GameObject landingHealingShieldPrefab;
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
        private GameObject _resourceTransferVisualPrefab;
        private GameObject _landingHealingShieldVisual;
        private int _resourceTransferVisualRevision = -1;
        private BoardResourceTransferPhase _resourceTransferVisualPhase;
        private KeyPurchasePresentationPhase _observedKeyPurchasePhase;
        private BoardLandingEffectFeedbackView _landingEffectFeedbackVisual;
        private int _landingEffectFeedbackRevision = -1;
        private bool _landingEffectFeedbackInitialized;

        public void Configure(GameplayCameraDirector director)
        {
            cameraDirector = director;
            if (isActiveAndEnabled) BindCameraEvents();
        }

        public void ConfigureLocalPlayerMarker(BoardLocalPlayerMarkerView marker)
        {
            localPlayerMarker = marker;
            if (localPlayerMarker != null) localPlayerMarker.Hide();
        }

        private void BindCameraEvents()
        {
            if (_subscribedDirector == cameraDirector) return;
            UnbindCameraEvents();
            _subscribedDirector = cameraDirector;
            if (_subscribedDirector == null) return;
            _subscribedDirector.TransitionStarted += OnCameraTransitionChanged;
            _subscribedDirector.TransitionCompleted += OnCameraTransitionChanged;
        }

        private void UnbindCameraEvents()
        {
            if (_subscribedDirector != null)
            {
                _subscribedDirector.TransitionStarted -= OnCameraTransitionChanged;
                _subscribedDirector.TransitionCompleted -= OnCameraTransitionChanged;
            }
            _subscribedDirector = null;
        }

        private void OnCameraTransitionChanged(GameplayMode previous, GameplayMode target)
        {
            // Completion can run after Update. Restore presentation in that same
            // frame rather than briefly exposing the face inside the camera.
            ApplyLocalBodyVisibility(target);
        }

        private void ClearLocalPresentation()
        {
            cameraDirector?.ClearShopReveal();
            ClearForestGhostPresentation();
            ClearEventPresentation();
            if (_localAvatar != null && _localAvatar.AvatarVisual != null)
            {
                var visual = _localAvatar.AvatarVisual;
                visual.SetOwnerFirstPerson(false);
                visual.SetCameraTransitionHidden(false);
                visual.SetTopViewHighlight(false);
                visual.SetBoardTopViewHighlightVisible(false);
            }
            if (localPlayerMarker != null) localPlayerMarker.Hide();
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

        public void ConfigureCombatHitSpark(GameObject prefab)
        {
            combatHitSparkVfxPrefab = prefab;
            OneShotVfxPool.Prewarm(combatHitSparkVfxPrefab, 4);
        }

        public void PresentCombatHitSpark(Vector3 point, Vector3 normal)
        {
            CombatHitVfx.Play(combatHitSparkVfxPrefab, point, normal);
        }

        public void ConfigureLandingRewardAssets(GameObject giftPrefab, GameObject healingShieldPrefab)
        {
            landingGiftPrefab = giftPrefab;
            landingHealingShieldPrefab = healingShieldPrefab;
        }

        private void OnEnable()
        {
            BindCameraEvents();
            if (localPlayerMarker != null) localPlayerMarker.Hide();
            OneShotVfxPool.Prewarm(landingEventLightningPrefab, 4);
            OneShotVfxPool.Prewarm(combatHitSparkVfxPrefab, 4);
        }

        private void OnDisable()
        {
            // Player objects persist with the bootstrap scene. Never carry the
            // first-person hidden-body or board top-view highlight presentation
            // out of the Board scene and into the lobby.
            UnbindCameraEvents();
            ClearMagicMazePresentation();
            ClearLocalPresentation();
            ClearNameplateOcclusion();
            ClearResourceTransferVisual();
            ClearLandingEffectFeedbackVisual();
            _landingEffectFeedbackInitialized = false;
            _landingEffectFeedbackRevision = -1;

        }

        private void Update()
        {
            if (cameraDirector == null)
            {
                cameraDirector = FindAnyObjectByType<GameplayCameraDirector>();
            }

            BindCameraEvents();
            ResolveLocalAvatar();
            var match = NetworkMatchState.Instance;
            if (cameraDirector == null || match == null || !match.IsSpawned || !match.GameplayEnabled)
            {
                ClearMagicMazePresentation();
                ClearLocalPresentation();
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

            if (match.IsReconnectPaused && !match.IsCombatRetreatActive)
            {
                _wasReconnectPaused = true;
                ApplyLocalBodyVisibility(cameraDirector.ActiveMode);
                RefreshOpponentNameplateOcclusion(
                    match,
                    cameraDirector.ActiveMode);
                return;
            }

            if (RefreshMagicMazePresentation(match)) return;

            RefreshCombatSpectatorFocus(match);
            var splitEvent = match.IsCombatRetreatActive
                ? RefreshCombatRetreatPresentation(match)
                : RefreshSplitEventPresentation(match);
            var purchasePhase = match.KeyPurchasePhase;
            RefreshShopRevealCamera(match, purchasePhase);
            var resourceTransferPhase = match.IsKeyShopRevealActive
                ? RefreshKeyPurchasePresentation(match)
                : RefreshResourceTransferPresentation(match);
            var landingFocus = !splitEvent && !match.IsKeyShopRevealActive && resourceTransferPhase !=
                               BoardResourceTransferPhase.Source &&
                               resourceTransferPhase !=
                               BoardResourceTransferPhase.Destination &&
                               RefreshLandingCameraFocus(match);
            var ghostFocus = RefreshForestGhostPresentation(match);
            var targetMode = ghostFocus
                ? (match.ForestGhost.Slot >= 0 ? GameplayMode.BoardResourceEvent : GameplayMode.CombatSpectator)
                : splitEvent ? GameplayMode.BoardResourceEvent : match.IsKeyShopRevealActive
                ? purchasePhase == KeyPurchasePresentationPhase.AcquireKey
                    ? GameplayMode.BoardResourceEvent
                    : purchasePhase >= KeyPurchasePresentationPhase.ReturnToFirstPerson
                        ? GameplayMode.FirstPerson
                        : GameplayMode.BoardTopDown
                : (resourceTransferPhase == BoardResourceTransferPhase.Source ||
                   resourceTransferPhase == BoardResourceTransferPhase.Destination)
                    ? GameplayMode.BoardResourceEvent
                    : landingFocus
                    ? GameplayMode.BoardResourceEvent
                    : ModeFor(match, _localAvatar);
            if (!_hasObservedState || _wasReconnectPaused)
            {
                cameraDirector.SnapTo(targetMode, _localAvatar != null ? _localAvatar.EyePivot : null);
                _observedRevision = match.StateRevision;
                _hasObservedState = true;
                _wasReconnectPaused = false;
            }
            else if (_observedRevision != match.StateRevision ||
                     cameraDirector.ActiveMode != targetMode)
            {
                _observedRevision = match.StateRevision;
                if (cameraDirector.ActiveMode != targetMode)
                {
                    if (ghostFocus || targetMode == GameplayMode.BoardResourceEvent ||
                        cameraDirector.ActiveMode == GameplayMode.BoardResourceEvent ||
                        purchasePhase == KeyPurchasePresentationPhase.RevealShop &&
                        _observedKeyPurchasePhase != KeyPurchasePresentationPhase.RevealShop)
                        cameraDirector.SnapTo(targetMode);
                    else
                    {
                        System.Func<double> transitionElapsed =
                            double.IsNaN(match.GetBoardCameraTransitionElapsed())
                                ? null : match.GetBoardCameraTransitionElapsed;
                        cameraDirector.SwitchTo(targetMode, transitionElapsed);
                    }
                }
            }

            _observedKeyPurchasePhase = purchasePhase;
            ApplyLocalBodyVisibility(targetMode);

            // Camera movement and obstacle visibility change continuously even
            // while the replicated flow revision remains unchanged.
            RefreshOpponentNameplateOcclusion(match, targetMode);
        }

        public void ConfigureEventSplitView(BoardEventSplitView prefab) => eventSplitViewPrefab = prefab;

        private bool RefreshCombatRetreatPresentation(NetworkMatchState match)
        {
            ClearEventVisuals();
            if (match.CombatRetreat.HasReached(match.SynchronizedNow, BoardCombatRetreatTiming.TopViewAt) || eventSplitViewPrefab == null)
            { ClearEventPresentation(); return false; }
            var count = 0;
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if (!match.CombatRetreat.Contains(slot)) continue;
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar == null) continue;
                _eventRoots[count] = avatar.transform;
                _eventHeads[count] = ResolveHeadAnchor(avatar);
                _retreatSlots[count++] = slot;
            }
            if (count == 0) { ClearEventPresentation(); return false; }
            if (_eventSplitView == null)
            {
                _eventSplitView = Instantiate(eventSplitViewPrefab);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_eventSplitView.gameObject, gameObject.scene);
            }
            _eventSplitView.PresentCombatRetreat(cameraDirector, _eventRoots, _eventHeads,
                _retreatSlots, count, match);
            cameraDirector.SetBoardResourceEventFocus(_eventRoots[0], _eventHeads[0], 0f);
            return true;
        }

        private bool RefreshSplitEventPresentation(NetworkMatchState match)
        {
            if (match.FlowState != BoardFlowState.LandingEffectResolve)
            { ClearEventPresentation(); return false; }
            var now = match.LandingFeedbackPresentationNow;
            var feedback = match.LandingEffectFeedback;
            var transfer = match.ResourceTransferPresentation;
            var transferPhase = match.GetResourceTransferPresentationPhase(out _, out _);
            var transferring = transferPhase == BoardResourceTransferPhase.Source ||
                               transferPhase == BoardResourceTransferPhase.Destination;
            var mask = transferring ? (byte)((1 << transfer.SourceSlot) | (1 << transfer.DestinationSlot)) :
                BoardLandingEffectFeedbackRules.GetEventFocusMask(feedback, now);
            if (mask == 0 || eventSplitViewPrefab == null)
            { ClearEventPresentation(); return false; }

            var count = 0;
            for (var index = 0; index < MultiplayerConstants.MaxPlayers; index++)
            {
                var slot = transferring ? index == 0 ? transfer.SourceSlot :
                    index == 1 ? transfer.DestinationSlot : -1 : index;
                if (slot < 0 || (mask & (1 << slot)) == 0) continue;
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar == null) { ClearEventPresentation(); return false; }
                _eventRoots[count] = avatar.transform;
                _eventHeads[count] = ResolveHeadAnchor(avatar);
                count++;
            }
            if (_eventSplitView == null)
            {
                _eventSplitView = Instantiate(eventSplitViewPrefab);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                    _eventSplitView.gameObject, gameObject.scene);
            }
            var height = resourceTransferHeadOffset + resourceTransferTravelHeight * 0.5f;
            _eventSplitView.Present(cameraDirector, _eventRoots, _eventHeads, count, height);
            cameraDirector.SetBoardResourceEventFocus(_eventRoots[0], _eventHeads[0], height);
            if (transferring) { ClearEventVisuals(); return true; }
            if (_eventVisualRevision != feedback.Revision)
            {
                ClearEventVisuals();
                _eventVisualRevision = feedback.Revision;
            }
            var playing = BoardLandingEffectFeedbackRules.IsEffectVisible(feedback, now);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                if (!playing || (mask & (1 << slot)) == 0 || avatar == null)
                {
                    DestroyEventVisual(slot);
                    continue;
                }
                var prefab = feedback.ResourceKind == BoardSpecialEventResource.Key
                    ? resourceTransferKeyPrefab : resourceTransferCoinPrefab;
                if (_eventVisuals[slot] == null && prefab != null)
                {
                    _eventVisuals[slot] = Instantiate(prefab);
                    DisableResourceVisualBehaviours(_eventVisuals[slot]);
                }
                if (_eventVisuals[slot] != null)
                    PositionResourceVisual(_eventVisuals[slot], feedback.SignedAmount < 0
                        ? BoardResourceTransferPhase.Source : BoardResourceTransferPhase.Destination,
                        BoardEffectShotTiming.MotionProgress(
                            BoardLandingEffectFeedbackRules.GetShotElapsed(feedback, now)),
                        avatar.transform, ResolveHeadAnchor(avatar));
            }
            return true;
        }

        private void DestroyEventVisual(int slot)
        {
            if (_eventVisuals[slot] == null) return;
            if (Application.isPlaying) Destroy(_eventVisuals[slot]);
            else DestroyImmediate(_eventVisuals[slot]);
            _eventVisuals[slot] = null;
        }
        private void ClearEventVisuals()
        {
            for (var slot = 0; slot < _eventVisuals.Length; slot++) DestroyEventVisual(slot);
            _eventVisualRevision = -1;
        }
        private void ClearEventPresentation()
        {
            ClearEventVisuals();
            if (_eventSplitView != null) _eventSplitView.Clear();
        }

        private void RefreshLandingEffectFeedbackPresentation(NetworkMatchState match)
        {
            var snapshot = match.LandingEffectFeedback;
            if (!_landingEffectFeedbackInitialized)
            {
                _landingEffectFeedbackInitialized = true;
                _landingEffectFeedbackRevision = -1;
            }

            var isNew = _landingEffectFeedbackRevision != snapshot.Revision;
            if (isNew)
            {
                ClearLandingEffectFeedbackVisual();
                _landingEventPlayedMask = 0;
                _landingEffectFeedbackRevision = snapshot.Revision;
            }

            if (!BoardLandingEffectFeedbackRules.IsEffectVisible(
                    snapshot, match.LandingFeedbackPresentationNow))
            {
                ClearLandingEffectFeedbackVisual();
                _landingEffectFeedbackRevision = snapshot.Revision;
                return;
            }

            if (match.FlowState != BoardFlowState.LandingEffectResolve)
                return;
            if (isNew && (match.IsReconnectPaused || match.IsGlobalSimulationPaused))
                return;

            var avatar = match.GetAvatarForSlot(snapshot.Slot);
            if (avatar == null)
                return;

            if (snapshot.FeedbackKind == BoardLandingEffectFeedbackKind.Event) return;
            if (snapshot.FeedbackKind == BoardLandingEffectFeedbackKind.EventIntro)
            {
                var bit = (byte)(1 << snapshot.Slot);
                if ((_landingEventPlayedMask & bit) == 0 && landingEventLightningPrefab != null)
                {
                    OneShotVfxPool.Play(landingEventLightningPrefab, PlayerAvatarVisual.GetFeetPosition(avatar.transform),
                        Quaternion.identity, (float)BoardEffectShotTiming.Motion);
                    _landingEventPlayedMask |= bit;
                }
                return;
            }

            if (landingEffectFeedbackPrefab == null ||
                !BoardLandingEffectFeedbackRules.TryGetContent(
                    snapshot, out var iconKind, out var label))
                return;

            var head = ResolveHeadAnchor(avatar);
            if (head == null)
                return;

            if (_landingEffectFeedbackVisual == null)
            {
                _landingEffectFeedbackVisual = Instantiate(landingEffectFeedbackPrefab);
                _landingEffectFeedbackVisual.name = "Board Landing Effect Feedback";
            }
            var remaining = (float)BoardLandingEffectFeedbackRules.GetEffectRemainingSeconds(
                snapshot, match.LandingFeedbackPresentationNow);
            _landingEffectFeedbackVisual.Present(head,
                cameraDirector != null ? cameraDirector.OutputCamera : null,
                iconKind, label, remaining);

            // Damage already travels through the shared ApplyDamage / PresentHitRpc path.
            // A full-health healing tile still shows its shield with a +0 result.
            if (_landingHealingShieldVisual == null &&
                snapshot.FeedbackKind == BoardLandingEffectFeedbackKind.Health &&
                (match.LastLandingEffectType == BoardLandingEffectType.Healing20 ||
                 match.LastLandingEffectType == BoardLandingEffectType.Healing10) &&
                landingHealingShieldPrefab != null)
            {
                _landingHealingShieldVisual = Instantiate(
                    landingHealingShieldPrefab, avatar.transform, false);
                _landingHealingShieldVisual.name = "Board Landing Healing Shield";
            }

            _landingEffectFeedbackRevision = snapshot.Revision;
        }

        private bool RefreshLandingCameraFocus(NetworkMatchState match)
        {
            if (match.FlowState != BoardFlowState.LandingEffectResolve ||
                match.IsGlobalSimulationPaused)
                return false;

            var focusSlot = match.LastLandingEffectSlot;
            if (focusSlot < 0)
                return false;

            var feedback = match.LandingEffectFeedback;
            var eventFocus = BoardLandingEffectFeedbackRules.GetEventFocusSlot(
                feedback, match.LandingFeedbackPresentationNow);
            if (eventFocus >= 0)
                focusSlot = eventFocus;
            else if (match.LastLandingEffectType == BoardLandingEffectType.SpecialEvent &&
                     !(feedback.FeedbackKind == BoardLandingEffectFeedbackKind.EventIntro &&
                       BoardLandingEffectFeedbackRules.IsVisible(feedback, match.LandingFeedbackPresentationNow)))
                return false;

            var avatar = match.GetAvatarForSlot(focusSlot);
            if (avatar == null)
                return false;

            cameraDirector.SetBoardResourceEventFocus(
                avatar.transform,
                ResolveHeadAnchor(avatar),
                0f);
            return true;
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
                out var phaseProgress,
                out var motionProgress);
            if (phase != BoardResourceTransferPhase.Source &&
                phase != BoardResourceTransferPhase.Destination)
            {
                var landingPhase = RefreshLandingResourcePresentation(match);
                if (landingPhase != BoardResourceTransferPhase.None)
                    return landingPhase;
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
            var phaseElapsed = phaseProgress * (phase == BoardResourceTransferPhase.Source
                ? BoardResourceTransferPresentationRules.SourceDurationSeconds
                : BoardResourceTransferPresentationRules.DestinationDurationSeconds);
            var visible = phase == BoardResourceTransferPhase.Source
                ? phaseElapsed >= BoardEffectShotTiming.Lead
                : phaseElapsed < BoardEffectShotTiming.Motion;
            if (visible) RefreshResourceTransferVisual(snapshot, phase, motionProgress, avatar.transform, head);
            else ClearResourceTransferVisual();
            return phase;
        }

        private BoardResourceTransferPhase RefreshLandingResourcePresentation(NetworkMatchState match)
        {
            var feedback = match.LandingEffectFeedback;
            if (!BoardLandingEffectFeedbackRules.IsVisible(
                    feedback, match.LandingFeedbackPresentationNow) ||
                (feedback.FeedbackKind != BoardLandingEffectFeedbackKind.Gold &&
                 feedback.FeedbackKind != BoardLandingEffectFeedbackKind.Item))
                return BoardResourceTransferPhase.None;

            var avatar = match.GetAvatarForSlot(feedback.Slot);
            if (avatar == null)
                return BoardResourceTransferPhase.None;

            var prefab = feedback.FeedbackKind == BoardLandingEffectFeedbackKind.Item
                ? landingGiftPrefab : resourceTransferCoinPrefab;
            if (prefab == null)
                return BoardResourceTransferPhase.None;

            var head = ResolveHeadAnchor(avatar);
            var phase = feedback.FeedbackKind == BoardLandingEffectFeedbackKind.Gold &&
                        match.LastLandingEffectType == BoardLandingEffectType.GoldLoss
                ? BoardResourceTransferPhase.Source
                : BoardResourceTransferPhase.Destination;
            var elapsed = match.LandingFeedbackPresentationNow - feedback.StartedAt;
            var motion = BoardEffectShotTiming.MotionProgress(elapsed);
            cameraDirector.SetBoardResourceEventFocus(avatar.transform, head,
                resourceTransferHeadOffset + resourceTransferTravelHeight * 0.5f);
            if (!BoardEffectShotTiming.IsEffectVisible(elapsed))
            {
                ClearResourceTransferVisual();
                return phase;
            }
            RefreshResourceTransferVisual(new BoardResourceTransferSnapshot
            {
                Active = true,
                Revision = feedback.Revision,
                Resource = (byte)BoardSpecialEventResource.Gold,
                SourceSlot = feedback.Slot,
                DestinationSlot = feedback.Slot,
                Amount = 1
            }, phase, motion, avatar.transform, head, prefab);
            if (feedback.FeedbackKind == BoardLandingEffectFeedbackKind.Item &&
                _resourceTransferVisual != null)
                _resourceTransferVisual.GetComponent<GiftVisualVariants>()?.Present(
                    (ulong)(uint)match.BoardEffectSeed, feedback.Revision, feedback.Slot);
            return phase;
        }

        private void RefreshShopRevealCamera(NetworkMatchState match, KeyPurchasePresentationPhase phase)
        {
            if (phase == KeyPurchasePresentationPhase.RevealShop && match.KeyShopHasLocation)
            {
                var topology = BoardMapRuntimeLoader.ActiveTopology ?? FindAnyObjectByType<BoardTopology>();
                if (topology != null && topology.TryGetTile(match.KeyShopLocation, out var tile))
                {
                    cameraDirector.SetShopReveal(tile, match.KeyShopZoomOutProgress);
                    return;
                }
            }
            cameraDirector.ClearShopReveal();
        }

        private BoardResourceTransferPhase RefreshKeyPurchasePresentation(NetworkMatchState match)
        {
            var phase = match.GetKeyPurchasePhase(out var motion);
            var avatar = match.GetAvatarForSlot(match.KeyShopBuyerSlot);
            if (phase != KeyPurchasePresentationPhase.AcquireKey || avatar == null)
            {
                ClearResourceTransferVisual();
                return BoardResourceTransferPhase.None;
            }

            var head = ResolveHeadAnchor(avatar);
            cameraDirector.SetBoardResourceEventFocus(avatar.transform, head,
                resourceTransferHeadOffset + resourceTransferTravelHeight * 0.5f);
            if (motion <= 0f || motion >= 1f)
            {
                ClearResourceTransferVisual();
                return BoardResourceTransferPhase.Destination;
            }
            RefreshResourceTransferVisual(new BoardResourceTransferSnapshot
            {
                Active = true,
                Revision = match.KeyShopRevealRevision,
                Resource = (byte)BoardSpecialEventResource.Key,
                DestinationSlot = match.KeyShopBuyerSlot,
                Amount = 1
            }, BoardResourceTransferPhase.Destination, motion, avatar.transform, head);
            return BoardResourceTransferPhase.Destination;
        }

        private void RefreshResourceTransferVisual(
            BoardResourceTransferSnapshot snapshot,
            BoardResourceTransferPhase phase,
            float motionProgress,
            Transform avatarRoot,
            Transform head,
            GameObject prefabOverride = null)
        {
            if (snapshot.Amount <= 0)
            {
                ClearResourceTransferVisual();
                return;
            }
            var prefab = prefabOverride != null ? prefabOverride :
                snapshot.ResourceKind == BoardSpecialEventResource.Key
                    ? resourceTransferKeyPrefab : resourceTransferCoinPrefab;
            if (_resourceTransferVisual == null ||
                _resourceTransferVisualPrefab != prefab ||
                _resourceTransferVisualRevision != snapshot.Revision ||
                _resourceTransferVisualPhase != phase)
            {
                ClearResourceTransferVisual();
                if (prefab == null)
                {
                    return;
                }

                _resourceTransferVisual = Instantiate(prefab);
                _resourceTransferVisualPrefab = prefab;
                _resourceTransferVisual.name =
                    "Board Resource Transfer " + snapshot.ResourceKind;
                DisableResourceVisualBehaviours(_resourceTransferVisual);
                _resourceTransferVisualRevision = snapshot.Revision;
                _resourceTransferVisualPhase = phase;
            }

            PositionResourceVisual(_resourceTransferVisual, phase, motionProgress, avatarRoot, head);
        }

        private void PositionResourceVisual(GameObject visual, BoardResourceTransferPhase phase,
            float motionProgress, Transform avatarRoot, Transform head)
        {
            var headPosition = head != null
                ? head.position
                : avatarRoot.position +
                  Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
            var eased = motionProgress * motionProgress *
                        (3f - 2f * motionProgress);
            var height = phase == BoardResourceTransferPhase.Source
                ? Mathf.Lerp(0f, resourceTransferTravelHeight, eased)
                : Mathf.Lerp(resourceTransferTravelHeight, 0f, eased);
            var visualTransform = visual.transform;
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
            _resourceTransferVisualPrefab = null;
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
            if (_landingHealingShieldVisual != null)
            {
                if (Application.isPlaying)
                    Destroy(_landingHealingShieldVisual);
                else
                    DestroyImmediate(_landingHealingShieldVisual);
            }
            _landingHealingShieldVisual = null;
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
                    previous.SetBoardNameplateCamera(null);
                }
                _observedNameplateVisuals[slot] = visual;

                if (visual == null)
                {
                    continue;
                }

                visual.SetBoardNameplateCamera(canTestLineOfSight ? outputCamera : null);
                var isLocalAvatar = avatar == _localAvatar || avatar.IsOwner;
                visual.SetNameplateOccluded(
                    canTestLineOfSight && mode != GameplayMode.BoardResourceEvent &&
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
                if (collider == null || MagicMazeWall.AllowsSightAndAttacks(collider) ||
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
                    visual.SetBoardNameplateCamera(null);
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

            ClearLocalPresentation();
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
                if (localPlayerMarker != null) localPlayerMarker.Hide();
                return;
            }

            var transitioning = cameraDirector != null && cameraDirector.IsTransitioning;
            var hideHeadCosmetics = transitioning &&
                (targetMode == GameplayMode.FirstPerson ||
                 cameraDirector.CompletedMode == GameplayMode.FirstPerson);
            var match = NetworkMatchState.Instance;
            var preparingMinigame = match != null &&
                (match.FlowState == BoardFlowState.MinigameIntroReady ||
                 match.FlowState == BoardFlowState.MinigameLoading);
            var showingTopView = targetMode == GameplayMode.BoardTopDown &&
                                 !transitioning && !preparingMinigame &&
                                 !BoardFlowView.ShouldShowBoardEventPopup(match);
            var visual = _localAvatar.AvatarVisual;
            if (visual != null)
            {
                visual.SetCameraTransitionHidden(hideHeadCosmetics);
                visual.SetOwnerFirstPerson(
                    targetMode == GameplayMode.FirstPerson && !transitioning);
                // Board top view reuses the persistent WaterShield request. The
                // square highlight remains available to minigames only.
                visual.SetTopViewHighlight(false);
                visual.SetBoardTopViewHighlightVisible(
                    targetMode == GameplayMode.BoardTopDown);
            }
            if (localPlayerMarker != null)
            {
                var output = cameraDirector != null ? cameraDirector.OutputCamera : null;
                localPlayerMarker.Present(ResolveHeadAnchor(_localAvatar), output, showingTopView);
                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                {
                    var avatar = match != null ? match.GetAvatarForSlot(slot) : null;
                    var showOther = showingTopView && avatar != null &&
                                    avatar.IsSpawned && avatar != _localAvatar;
                    localPlayerMarker.PresentPlayer(slot,
                        avatar != null ? ResolveHeadAnchor(avatar) : null,
                        output, showOther,
                        avatar != null ? avatar.Appearance.BodyColor : Color.white);
                }
            }

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

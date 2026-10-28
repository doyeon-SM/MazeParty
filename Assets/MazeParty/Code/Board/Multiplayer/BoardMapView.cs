using System;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace MazeParty.Multiplayer
{
    internal struct BoardMapObservationSchedule
    {
        private bool _started;
        private double _nextSampleAt;

        public void Reset()
        {
            _started = false;
            _nextSampleAt = 0d;
        }

        public bool ShouldSample(double now)
        {
            if (!_started)
            {
                _started = true;
                _nextSampleAt = now +
                    BoardMapView.PlayerObservationIntervalSeconds;
                return true;
            }

            if (now < _nextSampleAt)
                return false;

            var overdue = Math.Max(0d, now - _nextSampleAt);
            var elapsedIntervals = Math.Floor(
                overdue / BoardMapView.PlayerObservationIntervalSeconds) + 1d;
            _nextSampleAt += elapsedIntervals *
                BoardMapView.PlayerObservationIntervalSeconds;
            return true;
        }
    }

    /// <summary>
    /// Updates the prefab-authored minimap and full map. The live minimap follows
    /// the local eye heading; the full map is north-up.
    /// </summary>
    public sealed class BoardMapView : MonoBehaviour
    {
        public const int GridSize = 7;
        public const int CellCount = GridSize * GridSize;
        internal const double PlayerObservationIntervalSeconds = 1d;
        private const int VisionSampleCount = 33;
        private const int FullMapSortingOrder = 50;

        [SerializeField] private GameObject minimapPanel;
        [SerializeField] private BoardMinimapView liveMinimap;
        [SerializeField] private GameObject fullMapPanel;
        [SerializeField] private BoardMinimapView fullMap;
        [SerializeField] private Button fullMapCloseButton;
        [SerializeField, HideInInspector] private int mapPresentationVersion;
        private bool _fullMapOpen;
        private bool _bombardmentTargeting;
        private BoardMapCanvasBindings _bindings;
        private bool _fullMapBoardAvailable;
        private bool _fullMapCloseButtonWired;
        private Canvas _mapCanvas;
        private int _defaultSortingOrder;
        private bool _sortingOrderCaptured;
        public bool FullMapOpen => _fullMapOpen;

        private BoardTopology _topology;
        private readonly BoardPlayerMapKnowledge _playerKnowledge =
            new BoardPlayerMapKnowledge();
        private readonly RaycastHit[] _playerSightHits = new RaycastHit[32];
        private readonly RaycastHit[] _visionSightHits = new RaycastHit[32];
        private readonly float[] _visionClearFractions =
            new float[VisionSampleCount];
        private BoardMapObservationSchedule _playerObservationSchedule;
        private NetworkMatchState _knowledgeMatch;
        private BoardTopology _knowledgeTopology;
        private GameplayCameraDirector _cameraDirector;

        public bool HasRequiredReferences =>
            minimapPanel != null &&
            liveMinimap != null && liveMinimap.HasRequiredReferences &&
            fullMapPanel != null && fullMap != null && fullMap.HasRequiredReferences &&
            fullMapCloseButton != null;

        private void OnEnable()
        {
            CaptureSortingOrder();
            _bindings = GetComponent<BoardMapCanvasBindings>();
            WireFullMapCloseButton(true);
        }

        private void LateUpdate()
        {
            if (!HasRequiredReferences)
            {
                return;
            }

            var manager = NetworkManager.Singleton;
            var localObject = manager != null && manager.SpawnManager != null
                ? manager.SpawnManager.GetLocalPlayerObject() : null;
            var localAvatar = localObject != null ? localObject.GetComponent<NetworkPlayerAvatar>() : null;
            var wasTargeting = _bombardmentTargeting;
            _bombardmentTargeting = localAvatar != null && localAvatar.IsSettingBombardment && localAvatar.CurrentHealth > 0;
            if (wasTargeting && !_bombardmentTargeting) CloseFullMap();
            _bindings?.PresentBombardment(_bombardmentTargeting, localAvatar != null ? localAvatar.LocalBombardmentTargetCount : 0);
            var match = NetworkMatchState.Instance;
            var isReady = match != null && match.IsSpawned && match.GameplayEnabled;
            var action = isReady && (match.FlowState == BoardFlowState.Descending ||
                match.FlowState == BoardFlowState.Action ||
                match.FlowState == BoardFlowState.AscendingResolve ||
                match.FlowState == BoardFlowState.CombatResolve ||
                match.FlowState == BoardFlowState.LandingEffectResolve);
            if (_cameraDirector == null)
            {
                _cameraDirector = FindAnyObjectByType<GameplayCameraDirector>();
            }

            // Map UI belongs to first-person board exploration. In every
            // top-view, event and spectator camera the board is already visible,
            // so the map and minimap stay closed.
            var mapAvailable = action && _cameraDirector != null &&
                               IsMapUiAllowed(_cameraDirector.ActiveMode);
            var keyboard = Keyboard.current;
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var typing = selected != null && selected.TryGetComponent<InputField>(out var input) && input.isFocused;
            var rawToggleRequested = !typing && keyboard != null &&
                                     keyboard.mKey.wasPressedThisFrame;
            var toggleRequested = !_bombardmentTargeting && rawToggleRequested &&
                (!LocalInputGate.BlocksGameplayInput ||
                 (_fullMapOpen && UiPopupStack.IsTop(fullMapPanel)));
            if (!typing && _fullMapOpen &&
                UiPopupStack.IsTop(fullMapPanel) &&
                keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                var menu = FindAnyObjectByType<GameMenuView>();
                if (menu == null || !menu.isActiveAndEnabled)
                {
                    CloseFullMap();
                    toggleRequested = false;
                }
            }
            if (!mapAvailable)
            {
                SuspendPlayerObservation();
                UpdateFullMapState(false, toggleRequested);
                SetActive(minimapPanel, false);
                return;
            }

            if (_topology == null)
            {
                _topology = FindAnyObjectByType<BoardTopology>();
            }
            if (_topology == null)
            {
                SuspendPlayerObservation();
                UpdateFullMapState(false, false);
                SetActive(minimapPanel, false);
                return;
            }

            UpdateFullMapState(true, toggleRequested);
            SetActive(minimapPanel, !_fullMapOpen);

            RefreshPlayerKnowledge(match, localAvatar);
            if (_fullMapOpen)
            {
                fullMap.Refresh(
                    _topology,
                    match,
                    localAvatar,
                    BoardMinimapDisplayContext.FullMap,
                    _playerKnowledge);
                var mouse = Mouse.current;
                if (wasTargeting && _bombardmentTargeting && match.CanAcceptActionInput &&
                    UiPopupStack.IsTop(fullMapPanel) && mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    if (fullMap.TryGetGroundPoint(_topology, mouse.position.ReadValue(),
                        _mapCanvas != null && _mapCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                            ? _mapCanvas.worldCamera : null, out var point))
                        localAvatar.ChooseBombardmentTarget(point);
                    else GameSound.Play(SoundKeys.UiError);
                }
            }
            else
            {
                liveMinimap.Refresh(
                    _topology,
                    match,
                    localAvatar,
                    BoardMinimapDisplayContext.Minimap,
                    _playerKnowledge);
            }
        }

        public void UpdateFullMapState(bool boardAvailable, bool toggleRequested)
        {
            _fullMapBoardAvailable = boardAvailable;
            if (!boardAvailable)
            {
                SetFullMapOpen(false);
            }
            else if (_bombardmentTargeting)
            {
                SetFullMapOpen(true);
            }
            else if (toggleRequested)
            {
                SetFullMapOpen(!_fullMapOpen);
            }

            RefreshFullMapVisibility();
        }

        internal static bool IsMapUiAllowed(GameplayMode mode)
        {
            return mode == GameplayMode.FirstPerson;
        }

        public void CloseFullMap()
        {
            if (_bombardmentTargeting)
            {
                UiPopupStack.Push(fullMapPanel, CloseFullMap);
                return;
            }
            SetFullMapOpen(false);
            RefreshFullMapVisibility();
        }

        private void SetFullMapOpen(bool open)
        {
            if (_fullMapOpen == open)
            {
                if (!open)
                {
                    UiPopupStack.Remove(fullMapPanel);
                    RestoreMapSortingOrder();
                }
                return;
            }

            _fullMapOpen = open;
            ApplyMapSortingOrder(open);
            if (open)
            {
                if (fullMapPanel != null)
                {
                    fullMapPanel.SetActive(true);
                    UiPopupStack.Push(fullMapPanel, CloseFullMap);
                }
            }
            else
            {
                UiPopupStack.Remove(fullMapPanel);
            }
        }

        private void RefreshFullMapVisibility()
        {
            if (fullMapPanel != null)
            {
                SetActive(
                    fullMapPanel,
                    _fullMapBoardAvailable && _fullMapOpen);
            }

            if (fullMapCloseButton != null)
            {
                fullMapCloseButton.gameObject.SetActive(
                    _fullMapBoardAvailable && _fullMapOpen && !_bombardmentTargeting);
            }
        }

        private void OnDisable()
        {
            SuspendPlayerObservation();
            WireFullMapCloseButton(false);
            _fullMapBoardAvailable = false;
            _bombardmentTargeting = false;
            CloseFullMap();
            RestoreMapSortingOrder();
        }

        private void CaptureSortingOrder()
        {
            if (_sortingOrderCaptured)
            {
                return;
            }

            _mapCanvas = GetComponent<Canvas>();
            if (_mapCanvas == null)
            {
                _mapCanvas = GetComponentInParent<Canvas>();
            }
            if (_mapCanvas == null)
            {
                var bindings = GetComponentInChildren<BoardMapCanvasBindings>(
                    true);
                _mapCanvas = bindings != null ? bindings.RootCanvas : null;
            }
            if (_mapCanvas == null)
            {
                return;
            }

            _defaultSortingOrder = _mapCanvas.sortingOrder;
            _sortingOrderCaptured = true;
        }

        private void ApplyMapSortingOrder(bool fullMapOpen)
        {
            CaptureSortingOrder();
            if (_mapCanvas == null)
            {
                return;
            }

            _mapCanvas.sortingOrder = fullMapOpen
                ? Mathf.Max(FullMapSortingOrder, _defaultSortingOrder)
                : _defaultSortingOrder;
        }

        private void RestoreMapSortingOrder()
        {
            if (_mapCanvas != null && _sortingOrderCaptured)
            {
                _mapCanvas.sortingOrder = _defaultSortingOrder;
            }
        }

        private void WireFullMapCloseButton(bool subscribe)
        {
            if (fullMapCloseButton == null ||
                subscribe == _fullMapCloseButtonWired)
            {
                return;
            }

            _fullMapCloseButtonWired = subscribe;
            if (subscribe)
            {
                fullMapCloseButton.onClick.AddListener(CloseFullMap);
            }
            else
            {
                fullMapCloseButton.onClick.RemoveListener(CloseFullMap);
            }
        }

        private void RefreshPlayerKnowledge(
            NetworkMatchState match,
            NetworkPlayerAvatar localAvatar)
        {
            if (_knowledgeMatch != match || _knowledgeTopology != _topology)
            {
                _knowledgeMatch = match;
                _knowledgeTopology = _topology;
                _playerKnowledge.Reset();
                _playerObservationSchedule.Reset();
            }
            SeedInitialPlayerPositions();

            if (_fullMapOpen || localAvatar == null)
            {
                SuspendPlayerObservation();
                return;
            }

            if (_cameraDirector == null)
                _cameraDirector = FindAnyObjectByType<GameplayCameraDirector>();
            // ActiveMode changes when the first-person blend starts. Sampling
            // the actual output camera during that blend prevents a player the
            // user can already see from being omitted until the blend completes.
            if (_cameraDirector == null ||
                _cameraDirector.ActiveMode != GameplayMode.FirstPerson ||
                _cameraDirector.OutputCamera == null)
            {
                SuspendPlayerObservation();
                return;
            }

            if (!_playerObservationSchedule.ShouldSample(
                    Time.unscaledTimeAsDouble))
            {
                return;
            }

            _playerKnowledge.BeginObservationFrame();
            if (localAvatar.HasLogicalBoardTile)
            {
                _playerKnowledge.Observe(
                    localAvatar.AssignedSlot,
                    localAvatar.LogicalBoardTileCoordinate);
            }

            var outputCamera = _cameraDirector.OutputCamera;
            RefreshVisionMask(outputCamera);
            match.ObserveChestsOnLocalMap(outputCamera, _playerSightHits);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if (slot == localAvatar.AssignedSlot)
                    continue;

                // A spawned cached avatar can remain visible during reconnect
                // grace even while the replicated present bit is temporarily
                // clear. Visibility and spawn state are the presentation truth.
                var target = match.GetAvatarForSlot(slot);
                if (target == null || !target.IsSpawned ||
                    !target.HasLogicalBoardTile || target.IsCloaked ||
                    !IsDirectlyVisible(outputCamera, target))
                {
                    continue;
                }

                _playerKnowledge.Observe(
                    slot,
                    target.LogicalBoardTileCoordinate);
            }

            _playerKnowledge.EndObservationFrame();
        }

        private void SuspendPlayerObservation()
        {
            _playerKnowledge.ClearCurrentVisibility();
            _playerObservationSchedule.Reset();
            if (liveMinimap != null)
                liveMinimap.PresentVisionUnavailable();
        }

        private void SeedInitialPlayerPositions()
        {
            var mapRoot = BoardMapRuntimeLoader.ResolveActiveMapRoot(_topology);
            if (mapRoot == null)
                return;

            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var avatar = _knowledgeMatch != null
                    ? _knowledgeMatch.GetAvatarForSlot(slot)
                    : null;
                if (avatar == null || !avatar.IsSpawned)
                    continue;

                var start = mapRoot.GetStartTile(slot);
                if (start != null)
                    _playerKnowledge.SeedIfUnknown(slot, start.Coordinate);
            }
        }

        private bool IsDirectlyVisible(
            Camera outputCamera,
            NetworkPlayerAvatar target)
        {
            var visual = target.AvatarVisual;
            var bindings = visual != null ? visual.Bindings : null;
            var targetPoint = bindings != null && bindings.HeadAnchor != null
                ? bindings.HeadAnchor.position
                : target.EyePivot != null
                    ? target.EyePivot.position
                    : target.transform.position + Vector3.up * 0.75f;
            return IsPointDirectlyVisible(
                outputCamera,
                targetPoint,
                _playerSightHits);
        }

        private void RefreshVisionMask(Camera outputCamera)
        {
            var forward = Vector3.ProjectOnPlane(
                outputCamera.transform.forward,
                Vector3.up);
            var left = Vector3.ProjectOnPlane(
                outputCamera.ViewportPointToRay(
                    new Vector3(0f, 0.5f, 0f)).direction,
                Vector3.up);
            var right = Vector3.ProjectOnPlane(
                outputCamera.ViewportPointToRay(
                    new Vector3(1f, 0.5f, 0f)).direction,
                Vector3.up);
            if (forward.sqrMagnitude <= 0.0001f ||
                left.sqrMagnitude <= 0.0001f ||
                right.sqrMagnitude <= 0.0001f)
            {
                liveMinimap.PresentVisionUnavailable();
                return;
            }

            forward.Normalize();
            left.Normalize();
            right.Normalize();
            var halfAngle = Mathf.Max(
                Vector3.Angle(forward, left),
                Vector3.Angle(forward, right));
            var maximumDistance = Mathf.Max(
                BoardTile.RoomSize,
                liveMinimap.VisibleWorldRadius);
            var origin = outputCamera.transform.position;
            for (var index = 0; index < VisionSampleCount; index++)
            {
                var viewportX = index / (VisionSampleCount - 1f);
                var direction = Vector3.ProjectOnPlane(
                    outputCamera.ViewportPointToRay(
                        new Vector3(viewportX, 0.5f, 0f)).direction,
                    Vector3.up);
                _visionClearFractions[index] =
                    direction.sqrMagnitude > 0.0001f
                        ? GetVisionClearFraction(
                            origin,
                            direction.normalized,
                            maximumDistance)
                        : 0f;
            }

            liveMinimap.PresentVision(
                halfAngle,
                _visionClearFractions);
        }

        private float GetVisionClearFraction(
            Vector3 origin,
            Vector3 direction,
            float maximumDistance)
        {
            var hitCount = Physics.RaycastNonAlloc(
                origin,
                direction,
                _visionSightHits,
                maximumDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            var closest = maximumDistance;
            for (var index = 0; index < hitCount; index++)
            {
                var hit = _visionSightHits[index];
                if (!IsSightOccluder(hit.collider))
                    continue;
                closest = Mathf.Min(closest, hit.distance);
            }

            return maximumDistance > 0f
                ? Mathf.Clamp01(closest / maximumDistance)
                : 0f;
        }

        internal static bool IsPointDirectlyVisible(
            Camera outputCamera,
            Vector3 targetPoint,
            RaycastHit[] sightHits)
        {
            if (outputCamera == null || sightHits == null ||
                sightHits.Length == 0)
            {
                return false;
            }

            var viewport = outputCamera.WorldToViewportPoint(targetPoint);
            if (viewport.z <= outputCamera.nearClipPlane ||
                viewport.x < 0f || viewport.x > 1f ||
                viewport.y < 0f || viewport.y > 1f)
            {
                return false;
            }

            var origin = outputCamera.transform.position;
            var offset = targetPoint - origin;
            var distance = offset.magnitude;
            if (distance <= 0.001f)
                return true;

            var hitCount = Physics.RaycastNonAlloc(
                origin,
                offset / distance,
                sightHits,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < hitCount; index++)
            {
                if (!IsSightOccluder(sightHits[index].collider))
                    continue;
                return false;
            }

            return hitCount < sightHits.Length;
        }

        private static bool IsSightOccluder(Collider collider)
        {
            if (collider == null ||
                collider.GetComponentInParent<NetworkPlayerAvatar>() != null)
            {
                return false;
            }

            var boundaryWall =
                collider.GetComponentInParent<BoardBoundaryWallVisual>();
            return boundaryWall == null || boundaryWall.IsVisible;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target.activeSelf != active) target.SetActive(active);
        }
    }
}

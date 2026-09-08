using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [Serializable]
    public struct WorldDieReconnectSnapshot
    {
        public bool IsValid;
        public WorldDieAuthoritySnapshot Authority;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 LinearVelocity;
        public Vector3 AngularVelocity;
        public bool IsNudging;
        public float NudgeRemainingSeconds;
        public float ResultVisibleRemainingSeconds;
    }

    /// <summary>
    /// Publicly observed, server-owned physical die for one stable player slot.
    /// Clients may request a push, but the server resolves the sender's avatar and
    /// repeats all authority, phase, tile, distance and ray checks before applying it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    [RequireComponent(typeof(NetworkRigidbody))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public sealed class NetworkWorldDie : NetworkBehaviour
    {
        private static readonly HashSet<NetworkWorldDie> ActiveServerDice =
            new HashSet<NetworkWorldDie>();

        [Header("Slot")]
        [SerializeField, Range(0, MultiplayerConstants.MaxPlayers - 1)]
        private int configuredSlot;

        [Header("Placement")]
        [SerializeField, Min(0.1f)] private float spawnHeight = 0.75f;
        [SerializeField, Range(0f, 1f)] private float boundaryRestitution = 0.15f;

        [Header("Interaction")]
        [SerializeField, Min(0.5f)] private float interactionDistance = 4.5f;
        [SerializeField, Min(0.1f)] private float rayOriginTolerance = 1f;
        [SerializeField, Min(0.1f)] private float horizontalImpulse = 4.5f;
        [SerializeField, Min(0.1f)] private float upwardImpulse = 4f;
        [SerializeField, Min(0.1f)] private float torqueImpulse = 7f;
        [SerializeField, Min(0.05f)] private float nudgeHorizontalImpulse = 1.35f;
        [SerializeField, Min(0f)] private float nudgeUpwardImpulse = 0.25f;
        [SerializeField, Min(0f)] private float nudgeTorqueImpulse = 0.9f;
        [SerializeField, Min(0.001f)] private float nudgeLinearSettleThreshold = 0.06f;
        [SerializeField, Min(0.001f)] private float nudgeAngularSettleThreshold = 0.1f;
        [SerializeField, Min(0f)] private float nudgeSettleHoldSeconds = 0.15f;
        [SerializeField, Min(0.1f)] private float maximumNudgeSeconds = 1.5f;

        [Header("Settlement")]
        [SerializeField, Min(0.001f)] private float linearSettleThreshold = 0.08f;
        [SerializeField, Min(0.001f)] private float angularSettleThreshold = 0.12f;
        [SerializeField, Min(0f)] private float settleHoldSeconds = 0.65f;
        [SerializeField, Min(0.5f)] private float maximumRollSeconds = 6f;

        [Header("Presentation")]
        [SerializeField, Min(0f)] private float resultVisibleSeconds =
            WorldDieResultPresentationPolicy.DefaultVisibleSeconds;
        [SerializeField] private Renderer[] dieRenderers = Array.Empty<Renderer>();
        [SerializeField] private WorldDieFaceMarker[] faceMarkers =
            Array.Empty<WorldDieFaceMarker>();
        [SerializeField] private TextMesh publicResultText;

        private readonly NetworkVariable<int> _slot = new NetworkVariable<int>(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> _phase = new NetworkVariable<byte>(
            (byte)WorldDiePhase.Hidden,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _publicFace = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector2Int> _tileCoordinate =
            new NetworkVariable<Vector2Int>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _simulationPaused =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly WorldDieAuthorityModel _authority = new WorldDieAuthorityModel();
        private readonly List<Vector3> _localFaceNormals = new List<Vector3>(10);
        private readonly List<int> _faceValues = new List<int>(10);

        private Rigidbody _body;
        private Collider _dieCollider;
        private NetworkTransform _networkTransform;
        private BoardTile _assignedTile;
        private WorldDieTileFrame _tileFrame;
        private bool _hasTileFrame;
        private Vector3 _pausedLinearVelocity;
        private Vector3 _pausedAngularVelocity;
        private double _nextCollisionIsolationRefresh;
        private bool _nudgeInProgress;
        private double _nudgeDeadline = -1d;
        private double _nudgeBelowThresholdSince = -1d;
        private double _resultHideDeadline = -1d;
        private double _localPauseStartedAt = -1d;

        public event Action<NetworkWorldDie, int> SettledOnServer;

        public int ConfiguredSlot => configuredSlot;
        public int AssignedSlot => _slot.Value;
        public WorldDiePhase Phase => (WorldDiePhase)_phase.Value;
        public int PublicFace => _publicFace.Value;
        public Vector2Int TileCoordinate => _tileCoordinate.Value;
        public bool IsSimulationPaused => _simulationPaused.Value;
        public bool IsVisible => Phase != WorldDiePhase.Hidden;

        private void Awake()
        {
            CacheComponents();
            ApplyVisibility(WorldDiePhase.Hidden);
        }

        public override void OnNetworkSpawn()
        {
            CacheComponents();
            _phase.OnValueChanged += OnPhaseChanged;
            _publicFace.OnValueChanged += OnPublicFaceChanged;
            ApplyVisibility(Phase);
            RefreshWorldResultText();

            if (!IsServer)
            {
                return;
            }

            ActiveServerDice.Add(this);
            if (OwnerClientId != NetworkManager.ServerClientId)
            {
                NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
            }

            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _body.isKinematic = true;
            _body.detectCollisions = false;
            _authority.AssignSlot(configuredSlot);
            _slot.Value = configuredSlot;
            RefreshCollisionIsolationOnServer();
        }

        public override void OnNetworkDespawn()
        {
            _phase.OnValueChanged -= OnPhaseChanged;
            _publicFace.OnValueChanged -= OnPublicFaceChanged;
            ActiveServerDice.Remove(this);
        }

        public void ConfigureSceneDie(
            int slot,
            Renderer[] renderers,
            WorldDieFaceMarker[] markers,
            TextMesh resultText)
        {
            configuredSlot = Mathf.Clamp(slot, 0, MultiplayerConstants.MaxPlayers - 1);
            dieRenderers = renderers ?? Array.Empty<Renderer>();
            faceMarkers = markers ?? Array.Empty<WorldDieFaceMarker>();
            publicResultText = resultText;
            CacheFaceMarkers();
            RefreshWorldResultText();
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            var match = NetworkMatchState.Instance;
            if (match != null && match.IsReconnectPaused != _authority.IsPaused)
            {
                SetSimulationPausedOnServer(match.IsReconnectPaused, ServerNow);
            }

            if (ServerNow >= _nextCollisionIsolationRefresh)
            {
                _nextCollisionIsolationRefresh = ServerNow + 0.5d;
                RefreshCollisionIsolationOnServer();
            }

            if (_authority.IsPaused)
            {
                return;
            }

            if (_authority.Phase == WorldDiePhase.Settled)
            {
                if (WorldDieResultPresentationPolicy.ShouldHide(
                        _authority.Phase,
                        _authority.IsPaused,
                        ServerNow,
                        _resultHideDeadline))
                {
                    HideOnServer();
                }
                return;
            }

            if (_authority.Phase == WorldDiePhase.Ready)
            {
                if (_nudgeInProgress)
                {
                    UpdateNudgeMotionOnServer(ServerNow);
                }
                return;
            }

            if (_authority.Phase != WorldDiePhase.Rolling)
            {
                return;
            }

            ConstrainToAssignedTileOnServer();
            var decision = _authority.ObserveMotion(
                ServerNow,
                _body.linearVelocity.magnitude,
                _body.angularVelocity.magnitude,
                linearSettleThreshold,
                angularSettleThreshold,
                settleHoldSeconds,
                maximumRollSeconds);
            if (decision == WorldDieMotionDecision.ForceSettle)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            if (decision != WorldDieMotionDecision.None)
            {
                SettleOnServer();
            }
        }

        private void LateUpdate()
        {
            if (publicResultText == null || Phase == WorldDiePhase.Hidden)
            {
                return;
            }

            var labelTransform = publicResultText.transform;
            labelTransform.position = transform.position + Vector3.up * 0.9f;
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                var awayFromCamera = labelTransform.position - mainCamera.transform.position;
                if (awayFromCamera.sqrMagnitude > 0.000001f)
                {
                    labelTransform.rotation = Quaternion.LookRotation(
                        awayFromCamera.normalized,
                        Vector3.up);
                }
            }
        }

        /// <summary>
        /// Reveals and resets the slot die at its owner's current logical tile.
        /// Intended to be called by the server immediately after item choice resolves.
        /// </summary>
        public bool PrepareOnServer(BoardTile tile)
        {
            return PrepareOnServer(
                tile,
                tile != null ? tile.WorldCenter : Vector3.zero,
                tile != null ? tile.transform.forward : Vector3.forward);
        }

        public bool PrepareOnServer(
            BoardTile tile,
            Vector3 ownerPosition,
            Vector3 ownerForward)
        {
            if (!IsServer || tile == null || !ValidateFaceMarkers())
            {
                return false;
            }

            if (!_authority.Prepare(configuredSlot, tile.Coordinate))
            {
                return false;
            }

            _assignedTile = tile;
            _tileFrame = new WorldDieTileFrame(
                tile.WorldCenter,
                tile.transform.right,
                tile.transform.forward,
                tile.transform.up,
                BoardTile.HalfRoomSize);
            _hasTileFrame = true;

            var up = tile.transform.up.normalized;
            var right = tile.transform.right.normalized;
            var forward = tile.transform.forward.normalized;
            var ownerOffset = ownerPosition - tile.WorldCenter;
            var ownerHorizontal = right * Vector3.Dot(ownerOffset, right) +
                                  forward * Vector3.Dot(ownerOffset, forward);
            var projectedForward = Vector3.ProjectOnPlane(ownerForward, up);
            if (projectedForward.sqrMagnitude <= 0.000001f)
            {
                projectedForward = forward;
            }
            var desiredPosition = tile.WorldCenter + ownerHorizontal +
                                  projectedForward.normalized * 2f +
                                  up * spawnHeight;
            var dieBounds = _dieCollider.bounds;
            _tileFrame.Constrain(
                desiredPosition,
                Vector3.zero,
                WorldDieTileFrame.ProjectedExtent(dieBounds, right),
                WorldDieTileFrame.ProjectedExtent(dieBounds, forward),
                0f,
                out var targetPosition,
                out _);
            var targetRotation = Quaternion.Euler(
                UnityEngine.Random.Range(0f, 360f),
                UnityEngine.Random.Range(0f, 360f),
                UnityEngine.Random.Range(0f, 360f));

            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.isKinematic = true;
            _body.detectCollisions = true;
            _nudgeInProgress = false;
            _nudgeDeadline = -1d;
            _nudgeBelowThresholdSince = -1d;
            _resultHideDeadline = -1d;
            _localPauseStartedAt = -1d;
            TeleportOnServer(targetPosition, targetRotation);
            SyncAuthorityToNetwork();
            RefreshCollisionIsolationOnServer();
            return true;
        }

        public void HideOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            _authority.Hide();
            _assignedTile = null;
            _hasTileFrame = false;
            _pausedLinearVelocity = Vector3.zero;
            _pausedAngularVelocity = Vector3.zero;
            _nudgeInProgress = false;
            _nudgeDeadline = -1d;
            _nudgeBelowThresholdSince = -1d;
            _resultHideDeadline = -1d;
            _localPauseStartedAt = -1d;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.isKinematic = true;
            _body.detectCollisions = false;
            SyncAuthorityToNetwork();
        }

        /// <summary>
        /// Entry point for a future local input raycaster. This RPC is callable on a
        /// server-owned die, so the server resolves the sender back to its owned avatar
        /// and rejects attempts against another slot.
        /// </summary>
        public void RequestRollFromLocalRay(Ray worldRay)
        {
            if (!IsSpawned || !IsClient)
            {
                return;
            }

            RequestPushRpc(worldRay.origin, worldRay.direction);
        }

        public void RequestNudgeFromLocalRay(Ray worldRay)
        {
            if (!IsSpawned || !IsClient)
            {
                return;
            }

            RequestNudgeRpc(worldRay.origin, worldRay.direction);
        }

        // Compatibility seam for existing prototype callers. A push is the
        // authoritative roll action; a light reposition uses RequestNudgeFromLocalRay.
        public void RequestPushFromLocalRay(Ray worldRay)
        {
            RequestRollFromLocalRay(worldRay);
        }

        public bool TryApplyPushOnServer(
            NetworkPlayerAvatar requester,
            Ray claimedWorldRay,
            out WorldDiePushRejectReason rejectReason)
        {
            rejectReason = WorldDiePushRejectReason.ActionUnavailable;
            if (!IsServer || requester == null || !requester.IsSpawned ||
                !IsFinite(claimedWorldRay.origin) || !IsFinite(claimedWorldRay.direction))
            {
                return false;
            }

            var match = NetworkMatchState.Instance;
            if (match == null || _assignedTile == null || !_hasTileFrame ||
                claimedWorldRay.direction.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            var expectedRayOrigin = requester.EyePivot != null
                ? requester.EyePivot.position
                : requester.transform.position + Vector3.up * 0.75f;
            if (Vector3.Distance(claimedWorldRay.origin, expectedRayOrigin) >
                rayOriginTolerance)
            {
                return false;
            }

            if (Vector3.Distance(requester.transform.position, transform.position) >
                interactionDistance + 1f)
            {
                return false;
            }

            var normalizedRay = new Ray(
                claimedWorldRay.origin,
                claimedWorldRay.direction.normalized);
            if (!_dieCollider.Raycast(normalizedRay, out var hit, interactionDistance))
            {
                return false;
            }

            var context = new WorldDiePushContext(
                requester.AssignedSlot,
                match.CanAcceptActionInput,
                requester.HasResolvedItemChoice,
                requester.HasRolled || match.HasRolled(requester.AssignedSlot),
                match.IsReconnectPaused,
                _assignedTile.ContainsHorizontalPoint(requester.transform.position, 0.25f));
            if (!_authority.TryBeginRoll(context, ServerNow, out rejectReason))
            {
                return false;
            }

            _publicFace.Value = 0;
            _phase.Value = (byte)WorldDiePhase.Rolling;
            _simulationPaused.Value = false;
            _body.isKinematic = false;
            _body.detectCollisions = true;
            _body.WakeUp();
            _nudgeInProgress = false;
            _nudgeDeadline = -1d;
            _nudgeBelowThresholdSince = -1d;

            var horizontalDirection = Vector3.ProjectOnPlane(
                claimedWorldRay.direction,
                _tileFrame.Up);
            if (horizontalDirection.sqrMagnitude <= 0.000001f)
            {
                horizontalDirection = Vector3.ProjectOnPlane(
                    transform.position - requester.transform.position,
                    _tileFrame.Up);
            }

            horizontalDirection = horizontalDirection.sqrMagnitude > 0.000001f
                ? horizontalDirection.normalized
                : _tileFrame.Forward;
            var impulse = horizontalDirection * horizontalImpulse +
                          _tileFrame.Up * upwardImpulse;
            _body.AddForceAtPosition(impulse, hit.point, ForceMode.Impulse);
            _body.AddTorque(
                UnityEngine.Random.onUnitSphere * torqueImpulse,
                ForceMode.Impulse);
            return true;
        }

        public bool TryApplyNudgeOnServer(
            NetworkPlayerAvatar requester,
            Ray claimedWorldRay,
            out WorldDiePushRejectReason rejectReason)
        {
            rejectReason = WorldDiePushRejectReason.ActionUnavailable;
            if (!TryValidateInteractionOnServer(
                    requester,
                    claimedWorldRay,
                    out var normalizedRay,
                    out var hit,
                    out var context))
            {
                return false;
            }

            rejectReason = _authority.ValidatePush(context);
            if (rejectReason != WorldDiePushRejectReason.None)
            {
                return false;
            }

            var direction = Vector3.ProjectOnPlane(normalizedRay.direction, _tileFrame.Up);
            if (direction.sqrMagnitude <= 0.000001f)
            {
                direction = Vector3.ProjectOnPlane(
                    transform.position - requester.transform.position,
                    _tileFrame.Up);
            }
            direction = direction.sqrMagnitude > 0.000001f
                ? direction.normalized
                : _tileFrame.Forward;

            _body.isKinematic = false;
            _body.detectCollisions = true;
            _body.WakeUp();
            _nudgeInProgress = true;
            _nudgeDeadline = ServerNow + maximumNudgeSeconds;
            _nudgeBelowThresholdSince = -1d;
            var impulse = direction * nudgeHorizontalImpulse +
                          _tileFrame.Up * nudgeUpwardImpulse;
            _body.AddForceAtPosition(impulse, hit.point, ForceMode.Impulse);
            if (nudgeTorqueImpulse > 0f)
            {
                _body.AddTorque(
                    UnityEngine.Random.onUnitSphere * nudgeTorqueImpulse,
                    ForceMode.Impulse);
            }
            return true;
        }

        private bool TryValidateInteractionOnServer(
            NetworkPlayerAvatar requester,
            Ray claimedWorldRay,
            out Ray normalizedRay,
            out RaycastHit hit,
            out WorldDiePushContext context)
        {
            normalizedRay = default;
            hit = default;
            context = default;
            if (!IsServer || requester == null || !requester.IsSpawned ||
                !IsFinite(claimedWorldRay.origin) || !IsFinite(claimedWorldRay.direction))
            {
                return false;
            }

            var match = NetworkMatchState.Instance;
            if (match == null || _assignedTile == null || !_hasTileFrame ||
                claimedWorldRay.direction.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            var expectedRayOrigin = requester.EyePivot != null
                ? requester.EyePivot.position
                : requester.transform.position + Vector3.up * 0.75f;
            if (Vector3.Distance(claimedWorldRay.origin, expectedRayOrigin) >
                rayOriginTolerance ||
                Vector3.Distance(requester.transform.position, transform.position) >
                interactionDistance + 1f)
            {
                return false;
            }

            normalizedRay = new Ray(
                claimedWorldRay.origin,
                claimedWorldRay.direction.normalized);
            if (!_dieCollider.Raycast(normalizedRay, out hit, interactionDistance))
            {
                return false;
            }

            context = new WorldDiePushContext(
                requester.AssignedSlot,
                match.CanAcceptActionInput,
                requester.HasResolvedItemChoice,
                requester.HasRolled || match.HasRolled(requester.AssignedSlot),
                match.IsReconnectPaused,
                _assignedTile.ContainsHorizontalPoint(requester.transform.position, 0.25f));
            return true;
        }

        public void SetSimulationPausedOnServer(bool paused, double now)
        {
            if (!IsServer || paused == _authority.IsPaused)
            {
                return;
            }

            if (paused)
            {
                if (_authority.Phase == WorldDiePhase.Rolling || _nudgeInProgress)
                {
                    _pausedLinearVelocity = _body.linearVelocity;
                    _pausedAngularVelocity = _body.angularVelocity;
                }

                _localPauseStartedAt = now;
                _authority.SetPaused(true, now);
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.isKinematic = true;
            }
            else
            {
                if (_localPauseStartedAt >= 0d)
                {
                    var pausedSeconds = Math.Max(0d, now - _localPauseStartedAt);
                    if (_nudgeDeadline >= 0d)
                    {
                        _nudgeDeadline += pausedSeconds;
                    }
                    if (_resultHideDeadline >= 0d)
                    {
                        _resultHideDeadline += pausedSeconds;
                    }
                }

                _localPauseStartedAt = -1d;
                var resumeDynamic = _authority.Phase == WorldDiePhase.Rolling ||
                                    (_authority.Phase == WorldDiePhase.Ready &&
                                     _nudgeInProgress);
                _authority.SetPaused(false, now);
                _body.isKinematic = !resumeDynamic;
                if (resumeDynamic)
                {
                    _body.linearVelocity = _pausedLinearVelocity;
                    _body.angularVelocity = _pausedAngularVelocity;
                    _body.WakeUp();
                }

                _pausedLinearVelocity = Vector3.zero;
                _pausedAngularVelocity = Vector3.zero;
            }

            _simulationPaused.Value = _authority.IsPaused;
        }

        public WorldDieReconnectSnapshot CaptureReconnectSnapshotOnServer()
        {
            if (!IsServer)
            {
                return default;
            }

            var now = ServerNow;
            var timerNow = _authority.IsPaused && _localPauseStartedAt >= 0d
                ? _localPauseStartedAt
                : now;
            return new WorldDieReconnectSnapshot
            {
                IsValid = true,
                Authority = _authority.Capture(now),
                Position = _body.position,
                Rotation = _body.rotation,
                LinearVelocity = _authority.IsPaused
                    ? _pausedLinearVelocity
                    : _body.linearVelocity,
                AngularVelocity = _authority.IsPaused
                    ? _pausedAngularVelocity
                    : _body.angularVelocity,
                IsNudging = _nudgeInProgress,
                NudgeRemainingSeconds = _nudgeInProgress
                    ? Mathf.Max(0f, (float)(_nudgeDeadline - timerNow))
                    : 0f,
                ResultVisibleRemainingSeconds =
                    _authority.Phase == WorldDiePhase.Settled &&
                    _resultHideDeadline >= 0d
                        ? Mathf.Max(0f, (float)(_resultHideDeadline - timerNow))
                        : 0f
            };
        }

        public bool RestoreReconnectSnapshotOnServer(
            WorldDieReconnectSnapshot snapshot,
            BoardTile tile)
        {
            if (!IsServer || !snapshot.IsValid ||
                !snapshot.Authority.IsValid ||
                (snapshot.Authority.Phase != WorldDiePhase.Hidden &&
                 (tile == null ||
                  tile.Coordinate != snapshot.Authority.TileCoordinate)) ||
                !_authority.Restore(snapshot.Authority, ServerNow))
            {
                return false;
            }

            _assignedTile = tile;
            _hasTileFrame = tile != null;
            if (tile != null)
            {
                _tileFrame = new WorldDieTileFrame(
                    tile.WorldCenter,
                    tile.transform.right,
                    tile.transform.forward,
                    tile.transform.up,
                    BoardTile.HalfRoomSize);
            }

            configuredSlot = snapshot.Authority.Slot;
            _pausedLinearVelocity = snapshot.LinearVelocity;
            _pausedAngularVelocity = snapshot.AngularVelocity;
            _nudgeInProgress = snapshot.IsNudging &&
                               snapshot.Authority.Phase == WorldDiePhase.Ready &&
                               snapshot.NudgeRemainingSeconds > 0f;
            _nudgeDeadline = _nudgeInProgress
                ? ServerNow + snapshot.NudgeRemainingSeconds
                : -1d;
            _nudgeBelowThresholdSince = -1d;
            _resultHideDeadline = snapshot.Authority.Phase == WorldDiePhase.Settled
                ? ServerNow + Mathf.Max(0f, snapshot.ResultVisibleRemainingSeconds)
                : -1d;
            _localPauseStartedAt = snapshot.Authority.IsPaused ? ServerNow : -1d;
            TeleportOnServer(snapshot.Position, snapshot.Rotation);
            _body.detectCollisions = snapshot.Authority.Phase != WorldDiePhase.Hidden;
            var resumeDynamic =
                                (snapshot.Authority.Phase == WorldDiePhase.Rolling ||
                                 _nudgeInProgress) &&
                                !snapshot.Authority.IsPaused;
            if (resumeDynamic)
            {
                _body.isKinematic = false;
                _body.linearVelocity = snapshot.LinearVelocity;
                _body.angularVelocity = snapshot.AngularVelocity;
            }
            else
            {
                if (!_body.isKinematic)
                {
                    _body.linearVelocity = Vector3.zero;
                    _body.angularVelocity = Vector3.zero;
                }

                _body.isKinematic = true;
            }
            SyncAuthorityToNetwork();
            RefreshCollisionIsolationOnServer();
            return true;
        }

        private void SettleOnServer()
        {
            var face = WorldDieFaceResolver.ResolveHighestFace(
                _body.rotation,
                _localFaceNormals,
                _faceValues);
            if (face == 0)
            {
                Debug.LogError(
                    "World die could not resolve a face. Verify its ten face markers.",
                    this);
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.isKinematic = true;
                return;
            }

            if (!_authority.MarkSettled(face))
            {
                return;
            }

            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.isKinematic = true;
            _nudgeInProgress = false;
            _nudgeDeadline = -1d;
            _nudgeBelowThresholdSince = -1d;
            _resultHideDeadline = ServerNow + resultVisibleSeconds;
            SyncAuthorityToNetwork();
            SettledOnServer?.Invoke(this, face);
        }

        private void UpdateNudgeMotionOnServer(double now)
        {
            ConstrainToAssignedTileOnServer();
            var belowThreshold =
                _body.linearVelocity.magnitude <= nudgeLinearSettleThreshold &&
                _body.angularVelocity.magnitude <= nudgeAngularSettleThreshold;
            if (belowThreshold)
            {
                if (_nudgeBelowThresholdSince < 0d)
                {
                    _nudgeBelowThresholdSince = now;
                }
            }
            else
            {
                _nudgeBelowThresholdSince = -1d;
            }

            if (now >= _nudgeDeadline ||
                (_nudgeBelowThresholdSince >= 0d &&
                 now - _nudgeBelowThresholdSince >= nudgeSettleHoldSeconds))
            {
                StopNudgeOnServer();
            }
        }

        private void StopNudgeOnServer()
        {
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.isKinematic = true;
            _nudgeInProgress = false;
            _nudgeDeadline = -1d;
            _nudgeBelowThresholdSince = -1d;
        }

        private void ConstrainToAssignedTileOnServer()
        {
            if (!_hasTileFrame || _dieCollider == null)
            {
                return;
            }

            var bounds = _dieCollider.bounds;
            var rightExtent = WorldDieTileFrame.ProjectedExtent(
                bounds,
                _tileFrame.Right);
            var forwardExtent = WorldDieTileFrame.ProjectedExtent(
                bounds,
                _tileFrame.Forward);
            if (!_tileFrame.Constrain(
                    _body.position,
                    _body.linearVelocity,
                    rightExtent,
                    forwardExtent,
                    boundaryRestitution,
                    out var constrainedPosition,
                    out var constrainedVelocity))
            {
                return;
            }

            _body.position = constrainedPosition;
            _body.linearVelocity = constrainedVelocity;
        }

        private bool ValidateFaceMarkers()
        {
            CacheFaceMarkers();
            if (_faceMarkersValid())
            {
                return true;
            }

            Debug.LogError(
                "NetworkWorldDie requires exactly one outward-facing marker for each value 1-10.",
                this);
            return false;

            bool _faceMarkersValid()
            {
                if (_localFaceNormals.Count != WorldDieAuthorityModel.MaximumFace ||
                    _faceValues.Count != WorldDieAuthorityModel.MaximumFace)
                {
                    return false;
                }

                var seen = 0;
                for (var i = 0; i < _faceValues.Count; i++)
                {
                    var bit = 1 << (_faceValues[i] - 1);
                    if ((seen & bit) != 0)
                    {
                        return false;
                    }

                    seen |= bit;
                }

                return seen == (1 << WorldDieAuthorityModel.MaximumFace) - 1;
            }
        }

        private void CacheFaceMarkers()
        {
            if (faceMarkers == null || faceMarkers.Length == 0)
            {
                faceMarkers = GetComponentsInChildren<WorldDieFaceMarker>(true);
            }

            _localFaceNormals.Clear();
            _faceValues.Clear();
            for (var i = 0; i < faceMarkers.Length; i++)
            {
                var marker = faceMarkers[i];
                if (marker == null)
                {
                    continue;
                }

                _localFaceNormals.Add(marker.LocalNormal);
                _faceValues.Add(marker.Value);
            }
        }

        private void CacheComponents()
        {
            if (_body == null)
            {
                _body = GetComponent<Rigidbody>();
            }

            if (_dieCollider == null)
            {
                _dieCollider = GetComponent<Collider>();
            }

            if (_networkTransform == null)
            {
                _networkTransform = GetComponent<NetworkTransform>();
            }

            if (dieRenderers == null || dieRenderers.Length == 0)
            {
                dieRenderers = GetComponentsInChildren<Renderer>(true);
            }
        }

        private void SyncAuthorityToNetwork()
        {
            _slot.Value = _authority.Slot;
            _phase.Value = (byte)_authority.Phase;
            _publicFace.Value = _authority.SettledFace;
            _tileCoordinate.Value = _authority.TileCoordinate;
            _simulationPaused.Value = _authority.IsPaused;
            ApplyVisibility(_authority.Phase);
        }

        private void TeleportOnServer(Vector3 position, Quaternion rotation)
        {
            _body.position = position;
            _body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            if (IsSpawned && _networkTransform != null)
            {
                _networkTransform.Teleport(position, rotation, transform.localScale);
            }
        }

        private void OnPhaseChanged(byte _, byte current)
        {
            ApplyVisibility((WorldDiePhase)current);
            RefreshWorldResultText();
        }

        private void OnPublicFaceChanged(int _, int __)
        {
            RefreshWorldResultText();
        }

        private void RefreshWorldResultText()
        {
            if (publicResultText == null)
            {
                return;
            }

            switch (Phase)
            {
                case WorldDiePhase.Ready:
                    publicResultText.text = "?";
                    break;
                case WorldDiePhase.Rolling:
                    publicResultText.text = "...";
                    break;
                case WorldDiePhase.Settled:
                    publicResultText.text = PublicFace.ToString();
                    break;
                default:
                    publicResultText.text = string.Empty;
                    break;
            }
        }

        private void ApplyVisibility(WorldDiePhase phase)
        {
            CacheComponents();
            var visible = phase != WorldDiePhase.Hidden;
            for (var i = 0; i < dieRenderers.Length; i++)
            {
                if (dieRenderers[i] != null)
                {
                    dieRenderers[i].enabled = visible;
                }
            }

            if (_dieCollider != null)
            {
                _dieCollider.enabled = visible;
            }
        }

        private void RefreshCollisionIsolationOnServer()
        {
            if (!IsServer || _dieCollider == null)
            {
                return;
            }

            foreach (var other in ActiveServerDice)
            {
                if (other == null || other == this || other._dieCollider == null)
                {
                    continue;
                }

                Physics.IgnoreCollision(_dieCollider, other._dieCollider, true);
            }

            var avatars = FindObjectsByType<NetworkPlayerAvatar>(
                FindObjectsInactive.Include);
            for (var i = 0; i < avatars.Length; i++)
            {
                var avatarCollider = avatars[i] != null
                    ? avatars[i].GetComponent<CharacterController>()
                    : null;
                if (avatarCollider != null)
                {
                    Physics.IgnoreCollision(_dieCollider, avatarCollider, true);
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestPushRpc(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            RpcParams rpcParams = default)
        {
            var requester = ResolveSenderAvatar(rpcParams.Receive.SenderClientId);
            if (requester != null)
            {
                TryApplyPushOnServer(
                    requester,
                    new Ray(rayOrigin, rayDirection),
                    out _);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestNudgeRpc(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            RpcParams rpcParams = default)
        {
            var requester = ResolveSenderAvatar(rpcParams.Receive.SenderClientId);
            if (requester != null)
            {
                TryApplyNudgeOnServer(
                    requester,
                    new Ray(rayOrigin, rayDirection),
                    out _);
            }
        }

        private NetworkPlayerAvatar ResolveSenderAvatar(ulong senderClientId)
        {
            var avatars = FindObjectsByType<NetworkPlayerAvatar>(
                FindObjectsInactive.Exclude);
            for (var i = 0; i < avatars.Length; i++)
            {
                var avatar = avatars[i];
                if (avatar != null &&
                    avatar.IsSpawned &&
                    avatar.OwnerClientId == senderClientId &&
                    avatar.AssignedSlot == _authority.Slot)
                {
                    return avatar;
                }
            }

            return null;
        }

        private double ServerNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.timeAsDouble;

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z);
        }
    }
}

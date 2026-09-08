using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum WorldDiePhase : byte
    {
        Hidden,
        Ready,
        Rolling,
        Settled
    }

    public enum WorldDiePushRejectReason : byte
    {
        None,
        InvalidSlot,
        WrongPlayer,
        NotReady,
        ActionUnavailable,
        ItemChoicePending,
        AlreadyRolled,
        ReconnectPaused,
        PlayerOutsideAssignedTile
    }

    public enum WorldDieMotionDecision : byte
    {
        None,
        Settle,
        ForceSettle
    }

    public static class WorldDieResultPresentationPolicy
    {
        public const float DefaultVisibleSeconds = 2f;

        public static bool ShouldHide(
            WorldDiePhase phase,
            bool isPaused,
            double now,
            double hideDeadline)
        {
            return phase == WorldDiePhase.Settled &&
                   !isPaused &&
                   hideDeadline >= 0d &&
                   now >= hideDeadline;
        }
    }

    public readonly struct WorldDiePushContext
    {
        public WorldDiePushContext(
            int requesterSlot,
            bool actionAvailable,
            bool itemChoiceResolved,
            bool alreadyRolled,
            bool reconnectPaused,
            bool requesterInsideAssignedTile)
        {
            RequesterSlot = requesterSlot;
            ActionAvailable = actionAvailable;
            ItemChoiceResolved = itemChoiceResolved;
            AlreadyRolled = alreadyRolled;
            ReconnectPaused = reconnectPaused;
            RequesterInsideAssignedTile = requesterInsideAssignedTile;
        }

        public int RequesterSlot { get; }
        public bool ActionAvailable { get; }
        public bool ItemChoiceResolved { get; }
        public bool AlreadyRolled { get; }
        public bool ReconnectPaused { get; }
        public bool RequesterInsideAssignedTile { get; }
    }

    [Serializable]
    public struct WorldDieAuthoritySnapshot
    {
        public bool IsValid;
        public int Slot;
        public WorldDiePhase Phase;
        public int SettledFace;
        public Vector2Int TileCoordinate;
        public bool IsPaused;
        public double RollElapsedSeconds;
        public double StableElapsedSeconds;
    }

    /// <summary>
    /// Pure server-side rules for one slot-bound physical die. The model deliberately
    /// has no NetworkObject or Rigidbody dependency so authority and reconnect edge
    /// cases can be covered by EditMode tests.
    /// </summary>
    public sealed class WorldDieAuthorityModel
    {
        public const int MinimumFace = 1;
        public const int MaximumFace = 10;

        private double _rollStartedAt = -1d;
        private double _stableSince = -1d;
        private double _pausedAt = -1d;

        public int Slot { get; private set; } = -1;
        public WorldDiePhase Phase { get; private set; } = WorldDiePhase.Hidden;
        public int SettledFace { get; private set; }
        public Vector2Int TileCoordinate { get; private set; }
        public bool IsPaused { get; private set; }

        public bool AssignSlot(int slot)
        {
            if (slot < 0 || slot >= MultiplayerConstants.MaxPlayers)
            {
                return false;
            }

            Slot = slot;
            return true;
        }

        public bool Prepare(int slot, Vector2Int tileCoordinate)
        {
            if (!AssignSlot(slot))
            {
                return false;
            }

            TileCoordinate = tileCoordinate;
            SettledFace = 0;
            Phase = WorldDiePhase.Ready;
            IsPaused = false;
            _rollStartedAt = -1d;
            _stableSince = -1d;
            _pausedAt = -1d;
            return true;
        }

        public void Hide()
        {
            Phase = WorldDiePhase.Hidden;
            SettledFace = 0;
            IsPaused = false;
            _rollStartedAt = -1d;
            _stableSince = -1d;
            _pausedAt = -1d;
        }

        public bool TryBeginRoll(
            WorldDiePushContext context,
            double now,
            out WorldDiePushRejectReason rejectReason)
        {
            rejectReason = ValidatePush(context);
            if (rejectReason != WorldDiePushRejectReason.None)
            {
                return false;
            }

            Phase = WorldDiePhase.Rolling;
            SettledFace = 0;
            _rollStartedAt = now;
            _stableSince = -1d;
            return true;
        }

        public WorldDiePushRejectReason ValidatePush(WorldDiePushContext context)
        {
            if (Slot < 0 || Slot >= MultiplayerConstants.MaxPlayers)
            {
                return WorldDiePushRejectReason.InvalidSlot;
            }

            if (context.RequesterSlot != Slot)
            {
                return WorldDiePushRejectReason.WrongPlayer;
            }

            if (Phase != WorldDiePhase.Ready)
            {
                return WorldDiePushRejectReason.NotReady;
            }

            if (!context.ActionAvailable)
            {
                return WorldDiePushRejectReason.ActionUnavailable;
            }

            if (!context.ItemChoiceResolved)
            {
                return WorldDiePushRejectReason.ItemChoicePending;
            }

            if (context.AlreadyRolled)
            {
                return WorldDiePushRejectReason.AlreadyRolled;
            }

            if (context.ReconnectPaused || IsPaused)
            {
                return WorldDiePushRejectReason.ReconnectPaused;
            }

            return context.RequesterInsideAssignedTile
                ? WorldDiePushRejectReason.None
                : WorldDiePushRejectReason.PlayerOutsideAssignedTile;
        }

        public WorldDieMotionDecision ObserveMotion(
            double now,
            float linearSpeed,
            float angularSpeed,
            float linearSettleThreshold,
            float angularSettleThreshold,
            double settleHoldSeconds,
            double maximumRollSeconds)
        {
            if (Phase != WorldDiePhase.Rolling || IsPaused)
            {
                return WorldDieMotionDecision.None;
            }

            if (_rollStartedAt < 0d)
            {
                _rollStartedAt = now;
            }

            if (maximumRollSeconds > 0d && now - _rollStartedAt >= maximumRollSeconds)
            {
                return WorldDieMotionDecision.ForceSettle;
            }

            var belowThreshold =
                linearSpeed <= Mathf.Max(0f, linearSettleThreshold) &&
                angularSpeed <= Mathf.Max(0f, angularSettleThreshold);
            if (!belowThreshold)
            {
                _stableSince = -1d;
                return WorldDieMotionDecision.None;
            }

            if (_stableSince < 0d)
            {
                _stableSince = now;
                return WorldDieMotionDecision.None;
            }

            return now - _stableSince >= Math.Max(0d, settleHoldSeconds)
                ? WorldDieMotionDecision.Settle
                : WorldDieMotionDecision.None;
        }

        public bool MarkSettled(int face)
        {
            if (Phase != WorldDiePhase.Rolling ||
                face < MinimumFace ||
                face > MaximumFace)
            {
                return false;
            }

            SettledFace = face;
            Phase = WorldDiePhase.Settled;
            IsPaused = false;
            _stableSince = -1d;
            _pausedAt = -1d;
            return true;
        }

        public void SetPaused(bool paused, double now)
        {
            if (paused == IsPaused || Phase == WorldDiePhase.Hidden)
            {
                return;
            }

            if (paused)
            {
                IsPaused = true;
                _pausedAt = now;
                return;
            }

            var pausedDuration = _pausedAt >= 0d ? Math.Max(0d, now - _pausedAt) : 0d;
            if (_rollStartedAt >= 0d)
            {
                _rollStartedAt += pausedDuration;
            }

            if (_stableSince >= 0d)
            {
                _stableSince += pausedDuration;
            }

            IsPaused = false;
            _pausedAt = -1d;
        }

        public WorldDieAuthoritySnapshot Capture(double now)
        {
            var effectiveNow = IsPaused && _pausedAt >= 0d ? _pausedAt : now;
            return new WorldDieAuthoritySnapshot
            {
                IsValid = Slot >= 0 && Slot < MultiplayerConstants.MaxPlayers,
                Slot = Slot,
                Phase = Phase,
                SettledFace = SettledFace,
                TileCoordinate = TileCoordinate,
                IsPaused = IsPaused,
                RollElapsedSeconds = _rollStartedAt >= 0d
                    ? Math.Max(0d, effectiveNow - _rollStartedAt)
                    : 0d,
                StableElapsedSeconds = _stableSince >= 0d
                    ? Math.Max(0d, effectiveNow - _stableSince)
                    : -1d
            };
        }

        public bool Restore(WorldDieAuthoritySnapshot snapshot, double now)
        {
            if (!snapshot.IsValid ||
                snapshot.Slot < 0 ||
                snapshot.Slot >= MultiplayerConstants.MaxPlayers ||
                snapshot.SettledFace < 0 ||
                snapshot.SettledFace > MaximumFace ||
                (snapshot.Phase == WorldDiePhase.Settled &&
                 snapshot.SettledFace < MinimumFace))
            {
                return false;
            }

            Slot = snapshot.Slot;
            Phase = snapshot.Phase;
            SettledFace = snapshot.SettledFace;
            TileCoordinate = snapshot.TileCoordinate;
            IsPaused = snapshot.IsPaused;
            _rollStartedAt = snapshot.Phase == WorldDiePhase.Rolling
                ? now - Math.Max(0d, snapshot.RollElapsedSeconds)
                : -1d;
            _stableSince = snapshot.Phase == WorldDiePhase.Rolling &&
                           snapshot.StableElapsedSeconds >= 0d
                ? now - snapshot.StableElapsedSeconds
                : -1d;
            _pausedAt = snapshot.IsPaused ? now : -1d;
            return true;
        }
    }

    /// <summary>
    /// Oriented square used by the server to keep a dynamic die inside its assigned
    /// board tile without relying on client physics or globally toggled colliders.
    /// </summary>
    public readonly struct WorldDieTileFrame
    {
        public WorldDieTileFrame(
            Vector3 center,
            Vector3 right,
            Vector3 forward,
            Vector3 up,
            float halfExtent)
        {
            Center = center;
            Right = NormalizeOr(right, Vector3.right);
            Forward = NormalizeOr(forward, Vector3.forward);
            Up = NormalizeOr(up, Vector3.up);
            HalfExtent = Mathf.Max(0.01f, halfExtent);
        }

        public Vector3 Center { get; }
        public Vector3 Right { get; }
        public Vector3 Forward { get; }
        public Vector3 Up { get; }
        public float HalfExtent { get; }

        public bool ContainsCenter(Vector3 worldPosition, float margin = 0f)
        {
            var offset = worldPosition - Center;
            var limit = Mathf.Max(0f, HalfExtent - Mathf.Max(0f, margin));
            return Mathf.Abs(Vector3.Dot(offset, Right)) <= limit &&
                   Mathf.Abs(Vector3.Dot(offset, Forward)) <= limit;
        }

        public bool Constrain(
            Vector3 position,
            Vector3 velocity,
            float projectedHalfExtentRight,
            float projectedHalfExtentForward,
            float restitution,
            out Vector3 constrainedPosition,
            out Vector3 constrainedVelocity)
        {
            var offset = position - Center;
            var horizontal = Vector3.Dot(offset, Right);
            var depth = Vector3.Dot(offset, Forward);
            var height = Vector3.Dot(offset, Up);
            var horizontalLimit = Mathf.Max(
                0.01f,
                HalfExtent - Mathf.Max(0f, projectedHalfExtentRight));
            var depthLimit = Mathf.Max(
                0.01f,
                HalfExtent - Mathf.Max(0f, projectedHalfExtentForward));
            var clampedHorizontal = Mathf.Clamp(horizontal, -horizontalLimit, horizontalLimit);
            var clampedDepth = Mathf.Clamp(depth, -depthLimit, depthLimit);
            var changed = !Mathf.Approximately(horizontal, clampedHorizontal) ||
                          !Mathf.Approximately(depth, clampedDepth);

            constrainedPosition = Center +
                                  Right * clampedHorizontal +
                                  Forward * clampedDepth +
                                  Up * height;
            constrainedVelocity = velocity;
            if (!changed)
            {
                return false;
            }

            var bounce = Mathf.Clamp01(restitution);
            var rightSpeed = Vector3.Dot(constrainedVelocity, Right);
            if ((horizontal > horizontalLimit && rightSpeed > 0f) ||
                (horizontal < -horizontalLimit && rightSpeed < 0f))
            {
                constrainedVelocity -= Right * rightSpeed * (1f + bounce);
            }

            var forwardSpeed = Vector3.Dot(constrainedVelocity, Forward);
            if ((depth > depthLimit && forwardSpeed > 0f) ||
                (depth < -depthLimit && forwardSpeed < 0f))
            {
                constrainedVelocity -= Forward * forwardSpeed * (1f + bounce);
            }

            return true;
        }

        public static float ProjectedExtent(Bounds worldBounds, Vector3 axis)
        {
            var normalized = NormalizeOr(axis, Vector3.right);
            var extents = worldBounds.extents;
            return Mathf.Abs(normalized.x) * extents.x +
                   Mathf.Abs(normalized.y) * extents.y +
                   Mathf.Abs(normalized.z) * extents.z;
        }

        private static Vector3 NormalizeOr(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude > 0.000001f ? value.normalized : fallback;
        }
    }

    public static class WorldDieFaceResolver
    {
        public static int ResolveHighestFace(
            Quaternion dieRotation,
            IReadOnlyList<Vector3> localFaceNormals,
            IReadOnlyList<int> faceValues)
        {
            if (localFaceNormals == null ||
                faceValues == null ||
                localFaceNormals.Count == 0 ||
                localFaceNormals.Count != faceValues.Count)
            {
                return 0;
            }

            var result = 0;
            var bestDot = float.NegativeInfinity;
            for (var i = 0; i < localFaceNormals.Count; i++)
            {
                var value = faceValues[i];
                var normal = localFaceNormals[i];
                if (value < WorldDieAuthorityModel.MinimumFace ||
                    value > WorldDieAuthorityModel.MaximumFace ||
                    normal.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                var worldNormal = dieRotation * normal.normalized;
                var dot = Vector3.Dot(worldNormal, Vector3.up);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    result = value;
                }
            }

            return result;
        }
    }
}

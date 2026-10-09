using System;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum BoardResourceTransferPhase : byte
    {
        None,
        Result,
        Source,
        Destination,
        Complete
    }

    [Serializable]
    public struct BoardResourceTransferSnapshot :
        INetworkSerializable,
        IEquatable<BoardResourceTransferSnapshot>
    {
        public bool Active;
        public int Revision;
        public int SourceSlot;
        public int DestinationSlot;
        public byte Resource;
        public int Amount;
        public double StartedAt;

        public BoardSpecialEventResource ResourceKind =>
            (BoardSpecialEventResource)Resource;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Active);
            serializer.SerializeValue(ref Revision);
            serializer.SerializeValue(ref SourceSlot);
            serializer.SerializeValue(ref DestinationSlot);
            serializer.SerializeValue(ref Resource);
            serializer.SerializeValue(ref Amount);
            serializer.SerializeValue(ref StartedAt);
        }

        public bool Equals(BoardResourceTransferSnapshot other)
        {
            return Active == other.Active &&
                   Revision == other.Revision &&
                   SourceSlot == other.SourceSlot &&
                   DestinationSlot == other.DestinationSlot &&
                   Resource == other.Resource &&
                   Amount == other.Amount &&
                   StartedAt.Equals(other.StartedAt);
        }

        public override bool Equals(object obj)
        {
            return obj is BoardResourceTransferSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                Active,
                Revision,
                SourceSlot,
                DestinationSlot,
                Resource,
                Amount,
                StartedAt);
        }
    }

    /// <summary>
    /// Shared, deterministic timing for the result popup and the two world-space
    /// transfer shots. Each camera shot reserves a short lead before its model
    /// starts moving so the resource never leaves the frame before the cut lands.
    /// </summary>
    public static class BoardResourceTransferPresentationRules
    {
        public const double ResultDurationSeconds = 3d;
        public const double SourceDurationSeconds = 2d;
        public const double DestinationDurationSeconds = 2d;
        public const double CameraLeadSeconds = 0.4d;
        public const double TotalDurationSeconds =
            ResultDurationSeconds +
            SourceDurationSeconds +
            DestinationDurationSeconds;

        public static BoardResourceTransferPhase GetPhase(
            BoardResourceTransferSnapshot snapshot,
            double synchronizedNow,
            out float phaseProgress,
            out float motionProgress)
        {
            phaseProgress = 0f;
            motionProgress = 0f;
            if (!snapshot.Active ||
                snapshot.Amount <= 0 ||
                snapshot.SourceSlot < 0 ||
                snapshot.SourceSlot >= MultiplayerConstants.MaxPlayers ||
                snapshot.DestinationSlot < 0 ||
                snapshot.DestinationSlot >= MultiplayerConstants.MaxPlayers ||
                snapshot.SourceSlot == snapshot.DestinationSlot ||
                !Enum.IsDefined(
                    typeof(BoardSpecialEventResource),
                    snapshot.ResourceKind))
            {
                return BoardResourceTransferPhase.None;
            }

            var elapsed = Math.Max(0d, synchronizedNow - snapshot.StartedAt);
            if (elapsed < ResultDurationSeconds)
            {
                phaseProgress = Normalize(elapsed, ResultDurationSeconds);
                return BoardResourceTransferPhase.Result;
            }

            elapsed -= ResultDurationSeconds;
            if (elapsed < SourceDurationSeconds)
            {
                phaseProgress = Normalize(elapsed, SourceDurationSeconds);
                motionProgress = Normalize(
                    Math.Max(0d, elapsed - CameraLeadSeconds),
                    SourceDurationSeconds - CameraLeadSeconds);
                return BoardResourceTransferPhase.Source;
            }

            elapsed -= SourceDurationSeconds;
            if (elapsed < DestinationDurationSeconds)
            {
                phaseProgress = Normalize(elapsed, DestinationDurationSeconds);
                motionProgress = Normalize(
                    Math.Max(0d, elapsed - CameraLeadSeconds),
                    DestinationDurationSeconds - CameraLeadSeconds);
                return BoardResourceTransferPhase.Destination;
            }

            phaseProgress = 1f;
            motionProgress = 1f;
            return BoardResourceTransferPhase.Complete;
        }

        public static double GetRemainingSeconds(
            BoardResourceTransferSnapshot snapshot,
            double synchronizedNow)
        {
            return snapshot.Active
                ? Math.Max(
                    0d,
                    TotalDurationSeconds -
                    Math.Max(0d, synchronizedNow - snapshot.StartedAt))
                : 0d;
        }

        private static float Normalize(double value, double duration)
        {
            return duration <= 0d
                ? 1f
                : Mathf.Clamp01((float)(value / duration));
        }
    }
}

using System;
using MazeParty.Gameplay;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    // At most three retreat steps: start, current center, then gate + center per step.
    public struct BoardCombatRetreatPath : INetworkSerializable, IEquatable<BoardCombatRetreatPath>
    {
        public FixedList128Bytes<Vector3> Points;
        public Quaternion Facing;

        public void Add(Vector3 point)
        {
            if (Points.Length == 0 || (Points[Points.Length - 1] - point).sqrMagnitude > 0.000001f)
                Points.Add(point);
        }

        public Vector3 Evaluate(double elapsed)
        {
            if (Points.Length == 0) return Vector3.zero;
            var length = 0f;
            for (var i = 1; i < Points.Length; i++) length += Vector3.Distance(Points[i - 1], Points[i]);
            var remaining = length * BoardCombatRetreatTiming.MotionProgress(elapsed);
            for (var i = 1; i < Points.Length; i++)
            {
                var segment = Vector3.Distance(Points[i - 1], Points[i]);
                if (remaining <= segment && segment > 0.000001f)
                    return Vector3.Lerp(Points[i - 1], Points[i], remaining / segment);
                remaining -= segment;
            }
            return Points[Points.Length - 1];
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Facing);
            var count = Points.Length;
            serializer.SerializeValue(ref count);
            if (count < 0 || count > Points.Capacity) throw new InvalidOperationException("Invalid retreat path length.");
            if (serializer.IsReader) Points.Clear();
            for (var i = 0; i < count; i++)
            {
                var point = serializer.IsReader ? Vector3.zero : Points[i];
                serializer.SerializeValue(ref point);
                if (serializer.IsReader) Points.Add(point);
            }
        }

        public bool Equals(BoardCombatRetreatPath other) => Facing.Equals(other.Facing) && Points.Equals(other.Points);
        public override bool Equals(object obj) => obj is BoardCombatRetreatPath other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Facing, Points);
    }

    public struct BoardCombatRetreatSnapshot : INetworkSerializable, IEquatable<BoardCombatRetreatSnapshot>
    {
        public byte Mask;
        public double StartedAt;
        public bool Paused;
        public double PausedAt;
        public BoardCombatRetreatPath Path0, Path1, Path2, Path3;

        public bool Contains(int slot) => slot >= 0 && slot < 4 && (Mask & (1 << slot)) != 0;
        public bool HasReached(double now, double offset) => (Paused ? PausedAt : now) >= StartedAt + offset;
        public double Elapsed(double now) => Math.Max(0d, (Paused ? PausedAt : now) - StartedAt);
        public BoardCombatRetreatPath GetPath(int slot) => slot == 0 ? Path0 : slot == 1 ? Path1 : slot == 2 ? Path2 : Path3;
        public void SetPath(int slot, BoardCombatRetreatPath path)
        {
            if (slot < 0 || slot >= 4) throw new ArgumentOutOfRangeException(nameof(slot));
            Mask |= (byte)(1 << slot);
            switch (slot)
            {
                case 0: Path0 = path; break;
                case 1: Path1 = path; break;
                case 2: Path2 = path; break;
                case 3: Path3 = path; break;
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Mask);
            serializer.SerializeValue(ref StartedAt);
            serializer.SerializeValue(ref Paused);
            serializer.SerializeValue(ref PausedAt);
            serializer.SerializeValue(ref Path0);
            serializer.SerializeValue(ref Path1);
            serializer.SerializeValue(ref Path2);
            serializer.SerializeValue(ref Path3);
        }

        public bool Equals(BoardCombatRetreatSnapshot other) => Mask == other.Mask && StartedAt.Equals(other.StartedAt) &&
            Paused == other.Paused && PausedAt.Equals(other.PausedAt) && Path0.Equals(other.Path0) &&
            Path1.Equals(other.Path1) && Path2.Equals(other.Path2) && Path3.Equals(other.Path3);
        public override bool Equals(object obj) => obj is BoardCombatRetreatSnapshot other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Mask, StartedAt, Paused, PausedAt);
    }
}
